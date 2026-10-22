"""The ORIGINAL side of the session tester (lockstep.py): the DOS machine of dosmachine.py extended with what a whole session needs, the
script's input, the clock, the hooks and stubs, the recording of states, coverage and battle traces.

Everything here drives, hooks, stubs or feeds the emulated original, or decides what is taken from it; nothing here reads the C# side.
The recordings (recording.py) hash this module, dosmachine.py and run_unit.py (and battle_orig.py for fights), not lockstep.py or
battle_oracle.py (the C# run, the comparison, the reports). The description of the machine is in lockstep.py's docstring.
"""

from __future__ import annotations

import argparse
import json
import os
import struct
import sys

import unicorn
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from dosmachine import BASE, DS, P, REG, SENTINEL_SEG, Machine, Stop, linear  # noqa: E402

BYTES_PER_MS = 200                 # the machine's speed: executed code bytes per millisecond
EMS_SEG, FRAME, EMS_PAGES = 0xF800, 0xD000, 256
# Segments are the link-time ones of the notation (root 047C:0xxx, R014 = 0E76) put through P: the segment the image is loaded at is added
DRIVER_SEG, DRIVER_FIRST, DRIVER_LAST = P(0x047C), 0x04C7, 0x0791
DRIVER_LOAD, DRIVER_SLOTS = (P(0x047C), 0x07CE), (P(0x047C), 0x08C8)
MAIN = (P(0x0E76), 0x0022)          # R000+0x0022
SAVE_RECT = (P(0x047C), 0x04D5)
EMS_CHECK, MOUSE_INIT = (P(0x05F8), 0x0064), (P(0x047C), 0x11F6)
MOUSE_X, MOUSE_Y, MOUSE_BUTTONS, MOUSE_PRESSES, MOUSE_SHIFT, MOUSE_ON = 0x6EEC, 0x6EEE, 0x6EF0, 0x6EF2, 0x6EFA, 0x6EFB
KEYBOARD_FLAGS = (0x40, 0x17)       # 0040:0017, the BIOS keyboard flags (Shift, Ctrl, Alt held): the BIOS data area
COPY_PROTECTION = (P(0x1462), 0x0000)  # R041+0x0000 (thunk 09C0:1FD1)
CHAR_WIDTH, FONT_HEIGHT = (P(0x047C), 0x04E3), (P(0x047C), 0x051B)
HOTSPOTS = 0xE96E                   # the card menu's hot spots: count, highlight, width, x, y[20], height[20] (bytes)
PIC_OPEN, PIC_CLOSE = (P(0x047C), 0x05BC), (P(0x047C), 0x05AE)
KEY_PRESSED, GET_KEY = (P(0x047C), 0x073D), (P(0x047C), 0x0744)   # driver slots: a key waiting? / the next key (scan << 8 | char)
CARD, HOLDRAND = 0xA772, 0x7B20     # the card being run; the random generator's state (MSC holdrand)
SAVE_ITEM, LOAD_ITEM = 0x9920, 0x9934  # the Game menu's Save Game / Load Saved Game flags (1 on), which hot_keys obeys
SAV_WRITE = (P(0x1462), 0x2ACE)        # R037+0x2ACE (comment): the saved-game writer
MACHINE_STATE = ("resident", "trampolines", "call_stack", "mcbs", "dta", "scratch_sp", "count", "next_irq", "irqs", "pit_divisor",
                 "pit_latch", "pit_write", "vsync", "ems_pages", "ems_map", "ems_saved", "waits", "pic_size", "progress", "held", "button_read", "_dos_extended", "_ticked", "battle_passes", "_buffered", "still_clock", "still_passes", "press_ms", "idle_mark", "idle_since", "mouse", "presses", "releases")
IDLE_MS = 3000                      # and as long without a random number drawn, a card change or a step
IDLE_POLLS = 2000
BATTLE_PAUSE_PASSES = 296           # the battle's start pause (it waits for Space, the combat clock standing still): the key is given after this many passes at one
                                    # clock value, the C# host's pause under the script's wait:300 (298 passes at one value with one more set-up tick); the original's
                                    # ms-based idle test would give it after however many passes IDLE_MS takes at the speed the text drawn in them runs
BATTLE_PASS_LIN = P(0x0E76) * 16 + 0x3166   # R014+0x3166 combat_input, called once a pass of the battle's loop
CLEAR_TIMERS_VEC = (P(0x09C0), 0x1C61)  # R014+0x010B's vector to R020+0x06B6 (combat_state_clear: the combat clock back to 0)
STOCK_LOOP = (P(0x1462), 0x0A4C, 0x0AE2)   # R032+0x0A4C..0x0AE2: draw_barter_screen's stock listing (the loop that calls shop_has_item for every item)
STOCK_LOOP_STACK = 0x400             # the dead stack below the loop's SP that a timer interrupt there leaves as it found it
ENEMY_TURN_VEC = (P(0x09C0), 0x1C43)    # R014+0x0280's vector to R018+0x0706 (the enemies' turn), after the set-up's file loads
WALL_DRAWS = ((0x8381, 0x838B), (0x8438, 0x8442), (0x84AE, 0x84BE))   # R014+0x827A: push 2; call random_n; (after)
NEXT_TICK = 0xD9E4                 # the clock value of the battle's next combat tick (set at R014+0x0311)
COMBAT_CLOCK = 0x07D4               # the combat clock the timer callback counts while the battle runs
ACTIVATIONS_AT = 0xDA4F     # DS word: the combat clock of the next activations check (R014+0x0332 sets it to the clock + 120)
TIMER_CALLBACK = (P(0x047C), 0x000A)   # the game's timer callback (its counters, the combat clock DS:07D4), called by its INT 8 handler
BATTLE_QUIET = 5_000_000            # bytes of code without the time-based timer after a battle pass began
POLL_POINTS = {BASE}                # root 0000:0000, the menu bar's handler: every screen's loop calls it each pass
BWAIT_ACTUAL: list[int] = []         # per bwait step in script order: the passes the original really waited (run_original fills it; the C# waits as long)
COMBATANTS, RECORD, COUNT = 0x9C55, 0x80, 13   # DS:9C55, 13 combatant records of 0x80 bytes
CLICK_MS = 300                      # how long a scripted click holds the left button                   # key polls with no key before the script's next key is due




class Session(Machine):
    """The DOS machine with EMS, a deterministic clock, no drivers and the script's input."""

    def __init__(self, exe: bytes, game: str, **kw):
        super().__init__(exe, game, **kw)
        self.seen_int: dict = {}
        # no sound card: without CONFIG.DRK the program takes nsound.dlc (DS:A876 = 'n'), as the C# headless host plays without sound;
        # a sound driver would be loaded by the stubbed misc.exe slots and unloaded (freed) by every battle start (R002+0x0025)
        self.vfs.deleted.add("config.drk")
        self.driver_calls: dict[str, int] = {}
        # The vector table (0000:0000) and the BIOS data area are the machine's (dosmachine.py): every vector an IRET (F800:0030) at first,
        # the timer interrupt the BIOS tick (hooked) and IRET, INT 67h the EMM's entry. The game's own installs (INT 21h 25h and the direct
        # writes of root 044A:01EC / 044A:0214, INT 9 at 0000:0024) change the table as on a DOS machine; its keyboard handler is never run.
        self.write(EMS_SEG, 0, bytes([0xCD, 0x67, 0xCF]))      # int 67h; iret
        self.write(EMS_SEG, 0x0A, b"EMMXXXX0")
        self.write(EMS_SEG, 0x122, bytes([0xFF] * 4))       # the list of lists' device chain: empty
        self.write(EMS_SEG, 0x30, bytes([0xCF]))                # IRET: every vector at first
        self.write(EMS_SEG, 0x40, bytes([0x90, 0xCF]))          # INT 8: the BIOS tick (hooked), IRET
        self.set_vector(8, EMS_SEG, 0x40)
        self.set_vector(0x67, EMS_SEG, 0)
        # root 05F8: the EMS functions call the far pointer at 05F8:000E, first 05F8:0012 (EMS check, then the int 67h
        # vector into the pointer): the pointer is the vector at once, and the check (05F8:0064) passes (DS:0942 = 0)
        self.write(P(0x05F8), 0x0E, struct.pack("<HH", 0, EMS_SEG))
        self.stub(*EMS_CHECK, lambda m: (m.put_word(DS, 0x942, 0), (0, 0))[1], "ems_check")
        # root 047C:11F6, the mouse check (the int 33h vector, then the driver): present, 320 x 200 (shift 1), at the centre
        self.stub(*MOUSE_INIT, self._mouse_init, "mouse_init")
        self.hook(EMS_SEG, 0x40, self._bios_tick, record="root")
        self.pit_divisor, self.pit_latch, self.pit_write = 0x10000, None, []
        self.next_irq = self._period()
        self.irqs = 0
        self.vsync = 0
        self.ems_pages: dict[int, list] = {}
        self.ems_map: list = [None] * 4
        self.ems_saved: dict = {}
        self.stub(*DRIVER_LOAD, lambda m: (0, 0), "driver_load")
        self.stub(*DRIVER_SLOTS, lambda m: (0, 0), "driver_slots")
        # the lazy slots' far pointers (called directly too, e.g. `lcall es:[0531]` in root 047C:1354): a RETF at F800:0050
        self.write(EMS_SEG, 0x50, bytes([0xCB]))
        for off in self.driver_slots():
            if self.root[DRIVER_SEG * 16 - BASE + off] == 0xE8:
                self.write(DRIVER_SEG, off + 3, struct.pack("<HH", 0x50, EMS_SEG))
        for off in [o for o in self.driver_slots() if (DRIVER_SEG, o) not in (KEY_PRESSED, GET_KEY, SAVE_RECT, PIC_OPEN, PIC_CLOSE, CHAR_WIDTH, FONT_HEIGHT)]:
            self.stub(DRIVER_SEG, off, self._driver("047C:%04X" % off), "driver 047C:%04X" % off)
        # gfx_save_rect: the screen under a box goes to a DOS block whose segment the caller frees later: a real block
        self.stub(*SAVE_RECT, lambda m: (m.alloc(1) or 0, 0), "save_rect")
        # a picture drawn into memory (root 0617:045C): 047C:05BC(width, height) opens the buffer, 047C:05AE returns its segment
        self.pic_size = 0
        self.stub(*PIC_OPEN, lambda m: (setattr(m, "pic_size", m.arg(0) * m.arg(1)), (0, 0))[1], "pic_open")
        self.stub(*PIC_CLOSE, lambda m: (m.alloc((m.pic_size + 15) // 16 or 1) or 0, 0), "pic_close")
        # the driver's font metrics (char_width 047C:04E3(font, ch), font_height 047C:051B(font)): the wrapping of the card texts and so the
        # menus' hot spots (DS:E96E) follow the real fonts.
        self.fonts = self._read_fonts(game)
        self.stub(*CHAR_WIDTH, self._char_width, "char_width")
        self.stub(*FONT_HEIGHT, self._font_height, "font_height")
        # the copy-protection question (R041+0x0000, asked by the first visit to the map): it passes at once, as the C# has it (no
        # question, no random draws: see COVERAGE.md R041); a wrong answer would end the program
        self.stub(*COPY_PROTECTION, lambda m: (m.log.append("copy protection passed (stubbed) at %d ms" % m.ms), (0, 0))[1],
                  "copy_protection", record="R041")
        self.script: list[tuple[str, str]] = []
        self.states: dict[str, bytes] = {}
        self.held = False
        self.button_read = False
        self._dos_extended = 0
        self.press_ms = 0
        self._polled = None
        for lin in POLL_POINTS:
            self._hook_address(lin)
        self._ticked = False
        self.battle_passes = 0
        self.still_clock, self.still_passes = None, 0      # the combat clock of the last pass and the passes at that value
        self.battle_pause = 0           # lockstep.py's sessions: the passes a battle's start pause lasts before the script's key is given (battle_oracle gives its own)
        self.battle_trace = None        # a list: a line at the head of each battle pass (--battle-trace)
        # the clock ticks each battle's set-up took (the loads before the enemies' turn, then up to the first pass): the C# is given them
        # (the loads, up to the clock's read at R014+0x0311 that sets the first tick's time DS:D9E4 = read + 8, from there to the first pass)
        self.set_up_ticks: list[tuple[int, int, int, int]] = []
        self._setting_up = None
        self.hook(*CLEAR_TIMERS_VEC, lambda m: setattr(m, "_setting_up", [None]) if 14 in m.resident else None)
        self.hook(*ENEMY_TURN_VEC, self._enemy_turn)
        self._hook_address(BATTLE_PASS_LIN)
        # The stock listing reads an uninitialised stack word of shop_has_item ([bp-2], R032+0x1D18, the draw_barter_screen frame's dead
        # stack) that a timer interrupt taken inside this loop overwrites with its own frame and pushes (university-shop.steps: a tick
        # wrote 0 there, so the first item was tested with the city-size row instead of row 1 and the stock differed: the first
        # P bought nothing). On a real PC that is the chance of the tick's phase, not behaviour the C# can follow, so an interrupt taken
        # inside the loop leaves the dead stack below its SP as it found it (stock_irq_restore) and the word keeps what the listing left.
        self.in_stock_loop = False
        self._restore_stack = None
        self.hook(STOCK_LOOP[0], STOCK_LOOP[1], lambda m: setattr(m, "in_stock_loop", True), record="R032")
        self.hook(STOCK_LOOP[0], STOCK_LOOP[2], lambda m: setattr(m, "in_stock_loop", False), record="R032")
        self.uc.hook_add(unicorn.UC_HOOK_MEM_READ, self._button_read, begin=DS * 16 + MOUSE_BUTTONS, end=DS * 16 + MOUSE_BUTTONS + 1)
        # the BIOS keyboard flags (0040:0017: Shift, Ctrl, Alt held; R031+0x10AE and R016+0x0294 test them: give one against give the stack) are
        # the BIOS data area's byte, 0 (nothing held) until the script's shift:on gives the two Shift bits
        self.shift = 0
        self.write(*KEYBOARD_FLAGS, bytes([self.shift]))
        self._buffered = False
        self.idle_mark = None
        self.idle_since = 0
        self.mouse = [160, 100, 0]
        self.presses, self.releases = [0, 0], [0, 0]
        self.on_key = None              # called (outside the emulation) before a key of the script is given
        self.waits = 0                  # key polls with nothing to give
        self.bwait_polls, self.bwait_seen = 0, -1
        self.bwait_log = []             # per bwait step in script order: [n, passes the original really waited] (None while waiting)
        self.bwait_at = None            # the entry of bwait_log being waited for, and the battle pass it began at
        self.bwait = None               # bwait:<n>: the battle pass number the steps after it wait for (orders given mid-fight)
        self.progress = 0
        self.snap_plan = None           # (script steps left, take): a snapshot is due at the first idle stub handler entered with that many left
        self.stub(*KEY_PRESSED, self._key_pressed, "key_pressed")
        self.stub(*GET_KEY, self._get_key, "get_key")
        self._key_pressed.__func__.outside = True
        self._get_key.__func__.outside = True

    def driver_slots(self) -> list[int]:
        """The slot table misc.exe fills: `jmp far 0:0` (EA 00 00 00 00) or `call 048A` (lazy entries) between 047C:04C7 and
        the driver loader."""
        root, base = self.root, DRIVER_SEG * 16 - BASE
        out = []
        for off in range(DRIVER_FIRST, DRIVER_LOAD[1]):
            b = root[base + off:base + off + 5]
            if b == bytes([0xEA, 0, 0, 0, 0]) and (off - DRIVER_FIRST) % 7 == 0:
                out.append(off)
            elif b[0] == 0xE8 and (off + 3 + struct.unpack_from("<h", b, 1)[0]) & 0xFFFF == 0x048A:
                out.append(off)
        return out

    def _enemy_turn(self, m):
        if self._setting_up is not None and self._setting_up[0] is None:
            self._setting_up[0] = self.word(DS, COMBAT_CLOCK)

    def _release(self):
        """A click is let go CLICK_MS after the press once the game has read the button (DS:6EF0): held longer, a map pass aims at
        the same point of the view again and a menu acts on the press only; screens that wait for the release go on."""
        self.held = False
        self.put_word(DS, MOUSE_BUTTONS, 0)
        self.mouse[2] = 0
        self.releases[0] += 1
        self.waits = 0
        self.idle_since = self.ms

    def _button_read(self, uc, access, address, size, value, data):
        if self.held:
            self.button_read = True

    def _mouse(self, fn: int):
        """INT 33h from the script's pointer: x in the driver's 640 columns (the game shifts it by DS:6EFA = 1)."""
        x, y, b = self.mouse
        if fn == 0:
            self.set_reg("AX", 0xFFFF)
            self.set_reg("BX", 2)
        elif fn == 3:
            self.set_reg("BX", b)
            self.set_reg("CX", x << 1)
            self.set_reg("DX", y)
        elif fn in (5, 6):
            k = self.reg("BX") & 1
            counts = self.presses if fn == 5 else self.releases
            self.set_reg("AX", b)
            self.set_reg("BX", counts[k])
            self.set_reg("CX", x << 1)
            self.set_reg("DX", y)
            counts[k] = 0
        elif fn == 4:
            self.mouse[0], self.mouse[1] = self.reg("CX") >> 1, self.reg("DX")

    def _mouse_init(self, m):
        self.write(DS, MOUSE_SHIFT, bytes([1]))
        self.put_word(DS, MOUSE_X, 160)
        self.put_word(DS, MOUSE_Y, 100)
        self.put_word(DS, MOUSE_BUTTONS, 0)
        self.write(DS, MOUSE_ON, bytes([0xFF]))
        return (0xFFFF, 0)

    # ---- input -----------------------------------------------------------------------
    def _next_key(self):
        """The script's next key once the game has polled IDLE_POLLS times without one, drawing no random number and staying
        on its card (it waits for input), else None.
        The steps before it are taken first: a state is captured; a move puts the pointer there, a click also presses the
        left button (let go at the next poll); after either the game waits again before the next step."""
        if self.held:
            return None
        # a key handed to the game stays in the keyboard buffer until a get_key takes it (a message panel looks at it with key_pressed
        # and goes on without taking it, as R021+0x01E2 does; the box and flush_keys R002+0x0769 after it then find it): without this
        # the next panel waited for another idle period and the key was taken later than in the real buffer (orders-attack-b)
        if self._buffered:
            if self.script and self.script[0][0] == "key":
                return key_code(self.script[0][1])
            self._buffered = False
        mark = (self.read(DS, HOLDRAND, 4), self.word(DS, CARD))
        # bwait:<n> (orders in a battle that is running: the idle test below never passes there): the steps after it wait for n more
        # battle passes instead, then are given at once (the C# host's wait:<n> frames, a frame being a pass there)
        while self.script and self.script[0][0] == "bwait":
            self._end_bwait()
            self.bwait = self.battle_passes + int(self.script[0][1])
            self.bwait_at = (len(self.bwait_log), self.battle_passes)
            self.bwait_log.append([int(self.script.pop(0)[1]), None])
        if self.bwait is not None:
            if self.battle_passes < self.bwait:
                # a loop that polls without passing combat_input (a menu, a sheet) is idle: the passes to wait for would never come
                self.bwait_polls = self.bwait_polls + 1 if self.battle_passes == self.bwait_seen else 0
                self.bwait_seen = self.battle_passes
                if self.bwait_polls < IDLE_POLLS:
                    return None
            self._end_bwait()
            self.bwait = None
            self.idle_mark = mark
            self.waits, self.idle_since = IDLE_POLLS, -IDLE_MS
        if mark != self.idle_mark:
            # the game is still at work (it drew random numbers or moved to another card): not idle yet
            self.idle_mark = mark
            self.waits = 0
            self.idle_since = self.ms
        paused = bool(self.battle_pause) and 14 in self.resident and self.still_passes >= self.battle_pause
        if not paused and (self.waits < IDLE_POLLS or self.ms < self.idle_since + IDLE_MS):
            self.waits += 1
            return None
        while self.script and self.script[0][0] not in ("key", "move", "click", "option"):
            kind, value = self.script.pop(0)
            if kind == "state":
                self.capture(value)
            elif kind == "shift":
                self.shift = 3 if value == "on" else 0
                self.write(*KEYBOARD_FLAGS, bytes([self.shift]))
        if not self.script:
            raise Stop("script ended")
        kind, value = self.script[0]
        if kind in ("move", "click", "option"):
            self.script.pop(0)
            if kind == "option":
                n = int(value)
                if not 0 <= n < self.byte(DS, HOTSPOTS):
                    raise Stop("option %d: the menu has %d lines" % (n, self.byte(DS, HOTSPOTS)))
                x, y = self.word(DS, HOTSPOTS + 4) + 0x3C + 1, self.byte(DS, HOTSPOTS + 6 + n) + 1
            else:
                x, y = (int(v) for v in value.split(","))
            self.put_word(DS, MOUSE_X, x)
            self.put_word(DS, MOUSE_Y, y)
            self.mouse[0], self.mouse[1] = x, y
            if kind == "click":
                self.mouse[2] = 1
                self.presses[0] += 1
                self.held = True
                self.button_read = False
                self.press_ms = self.ms
                self.put_word(DS, MOUSE_BUTTONS, 1)
                self.put_word(DS, MOUSE_PRESSES, self.word(DS, MOUSE_PRESSES) | 1)
            self.log.append("%s %s at %d ms in %s" % (kind, value, self.ms, " < ".join(self.call_stack[-3:])))
            self.waits = 0
            self.idle_since = self.ms
            return None
        self._buffered = True
        return key_code(value)

    def capture(self, name: str):
        """The state now, as the original's own saved-game writer (R037 1462:2ACE) writes it, run on a copy of the machine
        that is thrown away afterwards."""
        keep = self.save_machine()
        keep_random, keep_card = struct.unpack("<I", self.read(DS, HOLDRAND, 4))[0], self.word(DS, CARD)
        try:
            for k in [k for k in self.vfs.files if k.startswith("saves") and "dksave" in k]:
                del self.vfs.files[k]
            self.ensure(37)
            self.call(*SAV_WRITE, [self.push_string("Darklands")])
            names = [k for k in self.vfs.files if k.startswith("saves") and "dksave" in k and k.endswith(".sav")]
            self.states[name] = (bytes(self.vfs.files[names[0]]), keep_random, keep_card)
            self.log.append("state %s at %d ms, card %X (Save Game %d, Load Saved Game %d)" % (
                name, self.ms, keep_card, self.byte(DS, SAVE_ITEM), self.byte(DS, LOAD_ITEM)))
        finally:
            self.load_machine(keep)

    def save_machine(self):
        import copy
        return (bytes(self.uc.mem_read(0, 0x100000)), self.uc.context_save(),
                copy.deepcopy({k: getattr(self, k) for k in MACHINE_STATE}), {k: bytearray(v) for k, v in self.vfs.files.items()},
                set(self.vfs.deleted), {h: list(x) for h, x in self.vfs.handles.items()})

    def load_machine(self, keep):
        mem, ctx, attrs, files, deleted, handles = keep
        self.uc.mem_write(0, mem)
        self.uc.context_restore(ctx)
        self.uc.ctl_remove_cache(0, 0x100000)
        self._decoded = {}
        for k, v in attrs.items():
            setattr(self, k, v)
        self.vfs.files, self.vfs.deleted, self.vfs.handles = files, deleted, handles

    def _on_code(self, uc, address, size, _):
        # the battle's pass (combat_input, R014+0x3166): one call of the game's timer callback first, as the C# headless host's one tick
        # a pass (DARKLANDS_BATTLE_TICK_PER_PASS); the time-based timer waits BATTLE_QUIET bytes, so it only comes in a pass that blocks
        if address == BATTLE_PASS_LIN and 14 in self.resident:
            if self._ticked:
                self._ticked = False
            else:
                self._ticked = True
                self.next_irq = self.count + BATTLE_QUIET
                self.battle_passes += 1
                clock = self.word(DS, COMBAT_CLOCK)
                self.still_clock, self.still_passes = clock, (self.still_passes + 1 if clock == self.still_clock else 1)
                if self._setting_up is not None:
                    loads = self._setting_up[0] or 0
                    read = (self.word(DS, NEXT_TICK) - 8) & 0xFFFF
                    # the fourth number: the part of the late ticks before R014+0x0332 read the clock for the next activations check
                    # (DS:DA4F, 120 later)
                    self.set_up_ticks.append((loads, read - loads, self.word(DS, COMBAT_CLOCK) - read,
                                              (self.word(DS, ACTIVATIONS_AT) - 120 - read) & 0xFFFF))
                    self._setting_up = None
                if self.battle_trace is not None:
                    self.battle_trace.append("pass clock %d rand %08X" % (self.word(DS, COMBAT_CLOCK), struct.unpack("<I", self.read(DS, HOLDRAND, 4))[0]))
                ss, sp = self.reg("SS"), (self.reg("SP") - 4) & 0xFFFF
                self.write(ss, sp, struct.pack("<HH", address - self.reg("CS") * 16, self.reg("CS")))
                self.set_reg("SP", sp)
                self.uc.reg_write(REG["CS"], TIMER_CALLBACK[0])
                self.uc.reg_write(REG["IP"], TIMER_CALLBACK[1])
                return
        # a polling point: the script's steps are looked at (outside the emulation), then the function runs as it is
        if address in POLL_POINTS and self.depth == 1:
            if self._polled == address:
                self._polled = None
            else:
                self._polled = address
                uc.emu_stop()
                self.pending = ("stub", self._poll)
                return
        super()._on_code(uc, address, size, _)

    def _poll(self, m):
        """A pass of a screen's loop that may not read the keyboard (the menu bar's handler): the steps that are not keys."""
        self._next_key()
        return None

    def _end_bwait(self):
        # the passes the original really waited for this bwait (less than n where a blocking read or a modal loop ended the wait): the C# waits as long
        if self.bwait_at is not None:
            self.bwait_log[self.bwait_at[0]][1] = self.battle_passes - self.bwait_at[1]
            self.bwait_at = None

    def _drop_bwait(self):
        # a read that blocks (a message waiting for a key) is idle already: the passes to wait for would never come
        self._end_bwait()
        self.bwait = None
        while self.script and self.script[0][0] == "bwait":
            self.bwait_log.append([int(self.script.pop(0)[1]), 0])

    def _dos_key(self, function: int):
        """INT 21h 0Bh: AL = FFh when a key is due; 07h / 08h: its character (an extended key: 0, then its scan code)."""
        if function == 0x0B:
            k = self._dos_extended or self._next_key()
            self.set_reg("AX", (self.reg("AX") & 0xFF00) | (0xFF if k else 0))
            return None
        if self._dos_extended:
            k, self._dos_extended = self._dos_extended, 0
            self.set_reg("AX", (self.reg("AX") & 0xFF00) | (k >> 8))
            return None
        k = self._get_key(self)[0]
        if k & 0xFF:
            self.set_reg("AX", (self.reg("AX") & 0xFF00) | (k & 0xFF))
        else:
            self._dos_extended = k
            self.set_reg("AX", self.reg("AX") & 0xFF00)
        return None

    def _key_pressed(self, m):
        # the waiting key's code without taking it (as INT 16h function 1): hot_keys (root 0000:0CAA) looks at it and takes only its own
        # the overland map's own loop (R030+0x0540 and R030+0x056D, return 1462:0545 / 1462:0572) looks at the keyboard before it
        # calls the menu bar (hot_keys looks second, root 0000:0CAF) and throws away a key it does not know: an Alt key (the hot keys
        # Alt-S, Alt-L ...) delivered to it is lost, whereas the rewrite handles hot keys first. A real key press goes to whichever
        # look follows it; the script's Alt keys are delivered to hot_keys' look.
        due = next((s for s in self.script if s[0] in ("key", "move", "click", "option")), None)
        if (due and due[0] == "key" and due[1].lower().startswith("alt")
                and self.call_stack and self.call_stack[-1].startswith("R030+")
                and self.word(self.reg("SS"), self.reg("SP")) in (0x0545, 0x0572)):
            return (0, 0)
        # likewise the card menu's own look (R043+0x1BD2, return 150B:11DC, which only knows Enter, Space and the cursor keys and drops the rest) and the
        # "press a key" wait's (wait_continue R043+0x32B6, return 150B:288E, which takes any key to go on) come after the menu bar's pass in the same
        # loop: an Alt key the script has due waits for hot_keys' look of the next pass (menubar: Alt-O; save-load: Alt-L after the rest)
        if (due and due[0] == "key" and due[1].lower().startswith("alt")
                and self.word(self.reg("SS"), self.reg("SP") + 2) == P(0x150B) and self.word(self.reg("SS"), self.reg("SP")) in (0x11DC, 0x288E)):
            return (0, 0)
        k = self._next_key()
        return (0 if k is None else k, 0)

    def _get_key(self, m):
        # get_key waits for a key: the game is idle at once (a click still held is let go first)
        if self.held:
            self._release()
        self._drop_bwait()
        self.waits, self.idle_since, self.idle_mark = IDLE_POLLS, -IDLE_MS, (self.read(DS, HOLDRAND, 4), self.word(DS, CARD))
        k = self._next_key()
        while k is None:                # the steps before the key (moves, clicks, states)
            if self.held:
                self._release()
            self._drop_bwait()
            self.waits, self.idle_since = IDLE_POLLS, -IDLE_MS
            k = self._next_key()
        self.script.pop(0)
        self._buffered = False
        self.waits = 0
        self.idle_since = self.ms
        self.still_passes = 0           # a key ends the battle's pause
        self.log.append("key %04X at %d ms (battle pass %d, clock %d) in %s" % (k, self.ms, self.battle_passes, self.word(DS, COMBAT_CLOCK), " < ".join(self.call_stack[-4:])))
        return (k, 0)

    # ---- the clock ---------------------------------------------------------------------
    @property
    def ms(self) -> int:
        return self.count // BYTES_PER_MS

    def ticks(self) -> int:
        return self.ms * 182 // 10000

    def _period(self) -> int:
        """Bytes of code between two timer interrupts (the PIT's divisor at 1193182 Hz)."""
        return max(1, self.pit_divisor * 1000 * BYTES_PER_MS // 1193182)

    def _bios_tick(self, m):
        self.put_word(0x40, 0x6C, self.word(0x40, 0x6C) + 1)

    def _on_block(self, uc, address, size, _):
        if (self.count >= self.next_irq and self.uc.reg_read(REG["EFLAGS"]) & 0x200 and address not in self._hooked
                and address < SENTINEL_SEG * 16):
            self.next_irq += self._period()
            self.irqs += 1
            cs = self.reg("CS")
            ip = (address - cs * 16) & 0xFFFF
            sp = (self.reg("SP") - 6) & 0xFFFF
            ss = self.reg("SS")
            if self.in_stock_loop:
                self._restore_stack = (address, ss, self.reg("SP"), self.read(ss, (self.reg("SP") - STOCK_LOOP_STACK) & 0xFFFF, STOCK_LOOP_STACK))
            self.write(ss, sp, struct.pack("<HHH", ip, cs, self.uc.reg_read(REG["EFLAGS"]) & 0xFFFF))
            self.set_reg("SP", sp)
            seg, off = self.vector(8)
            self.uc.reg_write(REG["EFLAGS"], self.uc.reg_read(REG["EFLAGS"]) & ~0x300)
            self.uc.reg_write(REG["CS"], seg)
            self.uc.reg_write(REG["IP"], off)
            return
        if self._restore_stack is not None and address == self._restore_stack[0] and self.reg("SP") == self._restore_stack[2]:
            # the interrupt taken in the stock loop has returned: its frame and pushes below SP are put back as they were
            _, ss, sp, dead = self._restore_stack
            self._restore_stack = None
            self.write(ss, (sp - STOCK_LOOP_STACK) & 0xFFFF, dead)
        if self.held and self.button_read and self.ms >= self.press_ms + CLICK_MS:
            self._release()
        if self.count > self.progress:
            self.progress += 20_000_000
            print("%7d ms  %s  %s" % (self.ms, self.function_at(address), " < ".join(self.call_stack[-3:])), flush=True)
        super()._on_block(uc, address, size, _)

    def _on_port(self, uc, port, size, *rest):
        if len(rest) == 2:          # out: (value, user data)
            value = rest[0]
            if port == 0x43:
                self.pit_write = [] if value & 0x30 else self.pit_write
                if value & 0x30 == 0:
                    t = self.pit_divisor - (self.count % self._period()) * self.pit_divisor // self._period()
                    self.pit_latch = [t & 0xFF, (t >> 8) & 0xFF]
            elif port == 0x40:
                self.pit_write.append(value & 0xFF)
                if len(self.pit_write) == 2:
                    self.pit_divisor = (self.pit_write[0] | (self.pit_write[1] << 8)) or 0x10000
                    self.pit_write = []
            return
        if port == 0x40:
            if self.pit_latch:
                return self.pit_latch.pop(0)
            return 0
        if port == 0x3DA:           # VGA status: retrace (bit 3) every other read
            self.vsync ^= 1
            return 9 if self.vsync else 0
        if port == 0x60:
            return 0
        return 0

    # ---- drivers -----------------------------------------------------------------------
    @staticmethod
    def _read_fonts(game: str) -> list:
        """FONTS.FNT: per font (first, last, data width, fixed width, height, spacing, line spacing, width table)."""
        path = next((os.path.join(game, f) for f in os.listdir(game) if f.lower() == "fonts.fnt"), None)
        if path is None:
            return []
        data = open(path, "rb").read()
        fonts = []
        for i in range(struct.unpack_from("<H", data, 0)[0]):
            head = struct.unpack_from("<H", data, 2 + 2 * i)[0] - 8
            first, last, _w, fixed, height, spacing, line, _u = data[head:head + 8]
            fonts.append((first, last, fixed, height, spacing, line, data[head - (last - first + 1):head]))
        return fonts

    def _font_of(self, slot: int):
        if not self.fonts:
            return None
        idx = slot - 1      # the loader (047C:07CE) puts font n of FONTS.FNT into slot n + 1
        return self.fonts[min(max(idx, 0), len(self.fonts) - 1)]

    def _char_width(self, m):
        # orig: char_width drv+0x11A9: 0 for a byte with the top bit set or outside first..last; the fixed width (header -5) when
        # non-zero, else the width table; plus the spacing (header -3)
        f, ch = self._font_of(m.arg(0)), m.arg(1) & 0xFF
        if f is None:
            return (6, 0)
        if ch == 0 or ch >= 0x80 or not f[0] <= ch <= f[1]:
            return (0, 0)
        return ((f[2] or f[6][ch - f[0]]) + f[4], 0)

    def _font_height(self, m):
        # orig: font_height drv+0x11FA: the glyph height (header -4) plus the line spacing (header -2)
        f = self._font_of(m.arg(0))
        return (8, 0) if f is None else (f[3] + f[5], 0)

    def _driver(self, coord: str):
        def h(m):
            m.driver_calls[coord] = m.driver_calls.get(coord, 0) + 1
            return (0, 0)
        return h

    def valid(self, lin: int) -> bool:
        # int86 runs `int nn; retf` built on the stack (root 06A1:1C58); root 0000:0000 (the menu bar) is code too
        return DS * 16 <= lin < DS * 16 + 0x10000 or super().valid(lin)

    # ---- interrupts --------------------------------------------------------------------
    def _on_int(self, uc, intno, _):
        ax = self.reg("AX")
        ah = ax >> 8
        key = (intno, ah)
        if key not in self.seen_int:
            self.log.append("INT %02X AX=%04X at %s" % (intno, ax, self.function_at(linear(self.reg("CS"), self.reg("IP")))))
        self.seen_int[key] = self.seen_int.get(key, 0) + 1
        if intno == 0x21:
            if ah in (0x0B, 0x07, 0x08):
                # DOS console input (kbhit / getch, root 06A1:1C0A / 06A1:1C30), e.g. the load's question: the script's keys, outside
                uc.emu_stop()
                self.pending = ("stub", lambda m, f=ah: m._dos_key(f))
                return
            if ah == 0x2C:
                ms = self.ms
                sec = 12 * 3600 + ms // 1000
                self.set_reg("CX", ((sec // 3600 % 24) << 8) | (sec // 60 % 60))
                self.set_reg("DX", ((sec % 60) << 8) | (ms // 10 % 100))
                return
            if ah == 0x52:
                self.uc.reg_write(REG["ES"], EMS_SEG)
                self.set_reg("BX", 0x100)
                return
            if ah == 0x19:                  # current drive: C:
                self.set_reg("AX", (ax & 0xFF00) | 2)
                return
            if ah == 0x0E:                  # select drive: 26 drives
                self.set_reg("AX", (ax & 0xFF00) | 26)
                return
            if ah == 0x47:                  # current directory: the root
                self.write(self.reg("DS"), self.reg("SI"), b"\0")
                self._carry(False, 0x0100)
                return
            if ah == 0x3B:                  # chdir
                self._carry(False, 0)
                return
            if ah == 0x36:                  # free disk space: 8 x 512-byte sectors, 20000 of 40000 clusters free
                for r, v in (("AX", 8), ("BX", 20000), ("CX", 512), ("DX", 40000)):
                    self.set_reg(r, v)
                return
            if ah == 0x57 and (ax & 0xFF) == 0:
                self.set_reg("CX", 0)
                self.set_reg("DX", 0x2121)
                self._carry(False)
                return
            return super()._on_int(uc, intno, _)
        if intno == 0x67:
            return self._ems()
        if intno == 0x1A and ah == 0:
            t = self.ticks()
            self.set_reg("CX", t >> 16)
            self.set_reg("DX", t & 0xFFFF)
            self.set_reg("AX", 0)
            return
        if intno == 0x10:
            if ah == 0x1C:
                self.set_reg("AX", 0)
            elif ah == 0x0E:
                self.vfs.console.append(ax & 0xFF)
            return
        if intno == 0x33:
            return self._mouse(ax)
        if intno == 0x2F:
            return
        self.pending = ("stop", "INT %02Xh AX=%04X at %04X:%04X" % (intno, ax, self.reg("CS"), self.reg("IP")))
        uc.emu_stop()

    def _unmap(self, p: int):
        cur = self.ems_map[p]
        if cur is not None:
            self.ems_pages[cur[0]][cur[1]][:] = self.read(FRAME, p * 0x4000, 0x4000)

    def _ems(self):
        ah, al = self.reg("AX") >> 8, self.reg("AX") & 0xFF
        bx, dx = self.reg("BX"), self.reg("DX")
        status, out_al = 0, 0
        used = sum(len(v) for v in self.ems_pages.values())
        if ah == 0x40:
            pass
        elif ah == 0x41:
            self.set_reg("BX", FRAME)
        elif ah == 0x42:
            self.set_reg("BX", EMS_PAGES - used)
            self.set_reg("DX", EMS_PAGES)
        elif ah in (0x43, 0x5A):
            if bx > EMS_PAGES - used:
                status = 0x88
            else:
                h = 1
                while h in self.ems_pages:
                    h += 1
                self.ems_pages[h] = [bytearray(0x4000) for _ in range(bx)]
                self.set_reg("DX", h)
        elif ah == 0x44:
            if al > 3 or (bx != 0xFFFF and (dx not in self.ems_pages or bx >= len(self.ems_pages[dx]))):
                status = 0x8A
            else:
                self._unmap(al)
                self.ems_map[al] = None if bx == 0xFFFF else (dx, bx)
                if bx != 0xFFFF:
                    self.write(FRAME, al * 0x4000, self.ems_pages[dx][bx])
        elif ah == 0x45:
            for p in range(4):
                if self.ems_map[p] and self.ems_map[p][0] == dx:
                    self._unmap(p)
                    self.ems_map[p] = None
            self.ems_pages.pop(dx, None)
        elif ah == 0x46:
            out_al = 0x40
        elif ah == 0x47:
            self.ems_saved[dx] = list(self.ems_map)
        elif ah == 0x48:
            for p in range(4):
                self._unmap(p)
            self.ems_map = list(self.ems_saved.pop(dx, self.ems_map))
            for p, cur in enumerate(self.ems_map):
                if cur:
                    self.write(FRAME, p * 0x4000, self.ems_pages[cur[0]][cur[1]])
        elif ah == 0x4B:
            self.set_reg("BX", len(self.ems_pages))
        elif ah == 0x4C:
            self.set_reg("BX", len(self.ems_pages.get(dx, [])))
        else:
            status = 0x84
            self.log.append("EMS function %02Xh" % ah)
        self.set_reg("AX", (status << 8) | out_al)

    # ---- the program ------------------------------------------------------------------
    def start(self, arguments=("-Q",)):
        words = [self.push_string("DARKLAND.EXE")] + [self.push_string(a) for a in arguments]
        self.scratch_sp -= 2 * (len(words) + 1)
        argv = self.scratch_sp
        self.write(DS, argv, struct.pack("<%dH" % (len(words) + 1), *words, 0))
        env = self.push_string("")
        self.ensure(0)
        return self.call(*MAIN, [len(words), argv, env], regs={"EFLAGS": 0x0202})   # DOS starts a program with IF set

    # ---- snapshots ---------------------------------------------------------------------------
    # The state of a run at its start point (after the boot and the load for a session, at the battle's entry R002+0x0000 for a fight), taken
    # once and kept under a key of everything that made it (recording.Snapshot): a later live run builds the same Session (the same stubs
    # and hooks), puts this state back and carries on from the same point of the same call loop, so it is the run it would have been.
    SNAPSHOT_SKIP = frozenset((
        "exe", "rt", "uc", "md", "images", "stubs", "hooks", "names", "thunks", "functions", "_hooked", "_decoded", "fonts", "vfs",   # built alike by every Session
        "script", "battle_trace", "_next_key", "on_key", "before_stub", "snap_plan", "coverage", "budget", "battle_pause", "_block", "pending"))      # the run's own wiring

    def _handler_kind(self, handler):
        if handler == self._poll:
            return ("poll",)
        if handler == self._key_pressed:
            return ("key_pressed",)
        if handler == self._get_key:
            return ("get_key",)
        return ("dos_key", handler.__defaults__[0])         # _on_int's `lambda m, f=ah: m._dos_key(f)`

    def _handler_of(self, kind):
        return {"poll": lambda: self._poll, "key_pressed": lambda: self._key_pressed, "get_key": lambda: self._get_key,
                "dos_key": lambda: (lambda m, f=kind[-1]: m._dos_key(f))}[kind[0]]()

    def plan_snapshot(self, left: int, take):
        """Take a snapshot (take(machine, resume kind)) at the first stub handler, entered with `left` script steps still to give, at which the game
        is idle: a blocking key read, or IDLE_POLLS polls since the last step (the next call may give the next step)."""
        self.snap_plan = (left, take)
        self.before_stub = self._plan_check

    def _plan_check(self, m, handler):
        plan = self.snap_plan
        if plan is None or len(self.script) != plan[0]:
            return
        kind = self._handler_kind(handler)
        if kind[0] == "get_key" or (kind[0] == "dos_key" and kind[1] in (0x07, 0x08)) or self.waits >= IDLE_POLLS:
            self.snap_plan = None
            plan[1](self, kind)

    def snapshot_state(self, resume, extra, full) -> dict:
        """The machine as a plain-data dict. resume: ("code",) inside the emulation at the first instruction of a block (the block's own effects on
        the clock and the coverage are taken back: the emulation entering it again does them again) or the kind of the stub handler about to run.
        full: the whole script the run began with; the steps given by now are kept with the state and checked against the next run's."""
        import collections
        v = self.vfs
        attrs = {k: val for k, val in vars(self).items() if k not in self.SNAPSHOT_SKIP}
        if resume[0] == "code":
            address, count, last_jcc = self._block
            attrs["count"], attrs["_last_block_jcc"] = count, last_jcc
            attrs["recent"] = collections.deque(list(self.recent)[:-1], maxlen=self.recent.maxlen)
        files, cached = {}, []
        for n, data in v.files.items():
            if v.same_on_disk(n, data):
                cached.append(n)
            else:
                files[n] = bytes(data)
        return {"mem": bytes(self.uc.mem_read(0, 0x100000)), "ctx": self.uc.context_save(), "attrs": attrs, "files": files, "cached": cached,
                "deleted": set(v.deleted), "handles": {h: list(x) for h, x in v.handles.items()}, "console": bytes(v.console),
                "given": list(full[:len(full) - len(self.script)]), "resume": resume, "extra": extra}

    def restore_state(self, snap: dict):
        self.uc.mem_write(0, snap["mem"])
        self.uc.context_restore(snap["ctx"])
        self.uc.ctl_remove_cache(0, 0x100000)
        self._decoded = {}
        for k, val in snap["attrs"].items():
            setattr(self, k, val)
        self.pending = None
        v = self.vfs
        v.files = {k: bytearray(d) for k, d in snap["files"].items()}
        v.deleted, v.handles, v.console = set(snap["deleted"]), {h: list(x) for h, x in snap["handles"].items()}, bytearray(snap["console"])
        for n in snap["cached"]:
            v.get(n)                                  # the game folder's bytes, as the run had read them
        n = len(snap["given"])
        if self.script[:n] != snap["given"]:
            raise SystemExit("the snapshot was taken after other steps than this script's first %d" % n)
        self.script = self.script[n:]

    def resume_state(self, snap: dict):
        """Carry on from a restored snapshot to the end of the run (as start() does)."""
        kind = snap["resume"]
        return self.resume(None if kind[0] == "code" else self._handler_of(kind))


KEY_NAMES = {"enter": 0x1C0D, "esc": 0x011B, "space": 0x3920, "up": 0x4800, "down": 0x5000, "left": 0x4B00, "right": 0x4D00,
             "home": 0x4700, "end": 0x4F00, "pgup": 0x4900, "pgdn": 0x5100, "backspace": 0x0E08, "tab": 0x0F09,
             **{"f%d" % k: (0x3A + k) << 8 for k in range(1, 11)},
             **{"alt" + c: v << 8 for c, v in zip("qopsdflcm", (0x10, 0x18, 0x19, 0x1F, 0x20, 0x21, 0x26, 0x2E, 0x32))}}
LETTERS = [0x1E, 0x30, 0x2E, 0x20, 0x12, 0x21, 0x22, 0x23, 0x17, 0x24, 0x25, 0x26, 0x32,
           0x31, 0x18, 0x19, 0x10, 0x13, 0x1F, 0x14, 0x16, 0x2F, 0x11, 0x2D, 0x15, 0x2C]


def key_code(name: str) -> int:
    """The game's key code (scan << 8 | character), as Darklands.Platform.Keys gives it."""
    if name.lower() in KEY_NAMES:
        return KEY_NAMES[name.lower()]
    c = name[0]
    if c.isalpha():
        return LETTERS[ord(c.lower()) - 97] << 8 | ord(c)
    if "1" <= c <= "9":
        return (ord(c) - ord("1") + 2) << 8 | ord(c)
    return {"0": 0x0B30, " ": 0x3920}[c]

CARD_CALL = (P(0x03A1), 0x02CC)        # the main loop's `call far es:[bx]` of the card table (DS:A772 = the card)
SRAND = (P(0x06A1), 0x29A4)
SRAND_CLOCK_CALL = bytes([0x9A, 0xA8, 0x23]) + struct.pack("<H", P(0x06A1))   # call far 06A1:23A8 (time), five bytes before the return into the srand call


class ClockTrace(list):
    """The original's battle passes, a line each (the lines clock_jumps reads), with the combat clock at each random draw (rand, root
    06A1:29B6) and where a tick's scheduler (1DB7:0D36) or melee exchanges (0E76:2A4A) begin: what each pass did before the next one's head."""

    def __init__(self):
        super().__init__()
        self.draw_clocks, self.draws_at = [], []            # the combat clock at each draw; the draws made before each pass head
        self.phase_clocks, self.phases_at = [], []          # (-1 or -2, combat clock); the phase marks made before each pass head

    def append(self, line):
        self.draws_at.append(len(self.draw_clocks))
        self.phases_at.append(len(self.phase_clocks))
        super().append(line)


def clock_jumps(lines):
    """[(clock at a pass head, timer callbacks beyond its one, "draw:rise" ...)] where the original's clock rose by more than one to the next
    pass: a pass that computes for long (R014+0x4BFA..0x66FE) lets the time-based timer callbacks come, which the C# (one tick a pass) does not
    have. The rises that came between the pass's random draws (a draw that reads the clock, a re-path time, sees them) are listed by the
    draw's number in the pass; the callbacks beyond those come after the pass. The rises before the tick's scheduler and melee exchanges (which
    test the clock against re-path and engage times before any draw) are listed as "-1:n" and "-2:n"."""
    clocks = [int(l.split()[2]) for l in lines]
    draw_clocks, draws_at = getattr(lines, "draw_clocks", []), getattr(lines, "draws_at", [])
    phase_clocks, phases_at = getattr(lines, "phase_clocks", []), getattr(lines, "phases_at", [])
    out = []
    for k, (a, b) in enumerate(zip(clocks, clocks[1:])):
        if b - a <= 1:
            continue
        cur, rises = a + 1, []
        if k + 1 < len(draws_at):
            for phase, c in phase_clocks[phases_at[k]:phases_at[k + 1]]:
                if c > cur:
                    rises.append("%d:%d" % (phase, c - cur))
                    cur = c
            for n, c in enumerate(draw_clocks[draws_at[k]:draws_at[k + 1]]):
                if c > cur:
                    rises.append("%d:%d" % (n, c - cur))
                    cur = c
        out.append((a, b - cur) + tuple(rises))
    return out


def session_start(steps) -> tuple[dict, int]:
    """(the key parts, the number of script steps) up to a session's snapshot point: the boot and, when the script loads a saved game first
    (key t, key l), the load; otherwise the start screen."""
    script = list(steps)
    n = 3 if len(script) >= 3 and script[0] == ("key", "t") and script[1][0] == "wait" and script[2] == ("key", "l") else 0
    return {"prefix": script[:n]}, n


def trace_state(t) -> dict:
    """A trace's lines and its lists of marks (the draws and phases made before each pass head), to put back in place."""
    return {"lines": list(t), "lists": {k: list(v) for k, v in vars(t).items() if isinstance(v, list)}}


def set_trace_state(t, st: dict):
    t[:] = st["lines"]
    for k, v in st["lists"].items():
        getattr(t, k)[:] = v


def run_original(exe: Path, game: str, steps, save: Path | None, budget: int, battle_trace=None, no_wall_draws=False, cov_out: dict | None = None, snapshots=None):
    """The session in the original: (states {name: (sav, random, card)}, clock seeds in order, log, how it ended, saved files,
    each battle's set-up ticks). cov_out, when given, is filled with the coverage record."""
    s = Session(exe.read_bytes(), game, max_instructions=budget, coverage=cov_out is not None)
    if no_wall_draws:
        # an experiment: the animated pieces' picture variant (random_n(2) in R014+0x827A, each combat_draw) leaves the game's generator as
        # it was (the C# makes those draws: it then parts from the original at the battle's first frame)
        kept = {}
        for before, after in WALL_DRAWS:
            s.hook(P(0x0E76), before, lambda m: kept.__setitem__("r", m.read(DS, HOLDRAND, 4)), record="R014")
            s.hook(P(0x0E76), after, lambda m: m.write(DS, HOLDRAND, kept["r"]), record="R014")
    if save:
        s.vfs.put("saves/dksave0.sav", save.read_bytes())
        if save.with_suffix(".BSV").exists():
            s.vfs.put("saves/dksave0.bsv", save.with_suffix(".BSV").read_bytes())
    elif (Path(game) / "SAVES" / "DEFAULT").is_file():
        # a session from the start screen without a saved game: the character creation reads SAVES\DEFAULT (R037+0x4184), hidden from the game folder
        s.vfs.put("saves/default", (Path(game) / "SAVES" / "DEFAULT").read_bytes())
    s.script = list(steps)
    s.battle_trace = battle_trace
    s.battle_pause = BATTLE_PAUSE_PASSES
    if getattr(battle_trace, "records", False):
        battle_trace.session = s
    if isinstance(battle_trace, ClockTrace):
        s.hook(P(0x1DB7), 0x0D36, lambda m: battle_trace.phase_clocks.append((-1, m.word(DS, COMBAT_CLOCK))), record="R014")
        s.hook(P(0x0E76), 0x2A4A, lambda m: battle_trace.phase_clocks.append((-2, m.word(DS, COMBAT_CLOCK))), record="R014")
        s.hook(P(0x06A1), 0x29B6, lambda m: battle_trace.draw_clocks.append(m.word(DS, COMBAT_CLOCK)))
    seeds = []
    def srand(m):
        # a clock seed when the call follows `call time` (06A1:23A8; push ax): srand((unsigned)time(0)); other seeds come from the game
        ret = linear(m.word(m.reg("SS"), m.reg("SP") + 2), m.word(m.reg("SS"), m.reg("SP")))
        clock = bytes(m.uc.mem_read(ret - 14, 5)) == SRAND_CLOCK_CALL
        if clock:
            seeds.append(m.arg(0))
        m.log.append("srand %d%s at %d ms in %s" % (m.arg(0), "" if clock else " (not the clock)", m.ms, " < ".join(m.call_stack[-2:])))
    s.hook(*SRAND, srand)
    s.hook(*CARD_CALL, lambda m: m.log.append("card %X at %d ms" % (m.word(DS, CARD), m.ms)))
    snap = snapshots.load() if snapshots is not None else None
    full = list(s.script)
    if snap is not None:
        s.restore_state(snap)
        seeds[:] = snap["extra"]["seeds"]
        if battle_trace is not None:
            set_trace_state(battle_trace, snap["extra"]["trace"])
    elif snapshots is not None:
        s.plan_snapshot(len(s.script) - session_start(steps)[1],
                        lambda m, kind: snapshots.store(m.snapshot_state(kind, {"seeds": list(seeds), "trace": trace_state(battle_trace) if battle_trace is not None else None},
                                                                         full)))
    try:
        if snap is not None:
            s.resume_state(snap)
        else:
            s.start()
        end = "the program ended"
    except Stop as e:
        end = str(e)[:600]
    if cov_out is not None:
        cov_out.update(s.coverage_result())
    # the saved games the session wrote; DKSAVE0 (the one --save put there) only if the session changed it (an Alt-S over it)
    given = {"dksave0.sav": save.read_bytes() if save else None,
             "dksave0.bsv": save.with_suffix(".BSV").read_bytes() if save and save.with_suffix(".BSV").exists() else None}
    files = {k.rpartition("\\")[2].upper(): bytes(v) for k, v in s.vfs.files.items()
             if k.startswith("saves") and k.endswith((".sav", ".bsv")) and given.get(k.rpartition("\\")[2], None) != bytes(v)}
    BWAIT_ACTUAL[:] = [min(n, a) if a is not None else n for n, a in s.bwait_log]
    return s.states, seeds, [line for line in s.log if not line.startswith("INT")], end, files, s.set_up_ticks


class TraceList(ClockTrace):
    """The battle's trace lines of --battle-records: every pass line also carries the 13 combatant records (DS:9C55), the queued damage
    (DS:D70C) and the attack rates (DS:D493), as battle_oracle.py's lines do (plus ClockTrace's clock marks for the clock jumps)."""
    def __init__(self, records=True):
        super().__init__()
        self.records, self.session = records, None

    def append(self, line):
        s = self.session
        if s is not None:
            line = "%s cbt %s q %s ar %s" % (line, s.read(DS, 0x9C55, 13 * 0x80).hex().upper(), s.read(DS, 0xD70C, 26).hex().upper(),
                                            s.read(DS, 0xD493, 13).hex().upper())
        super().append(line)


def stale_destinations(lines):
    """[(record, x, y)] of the enemy records (5..12): the destination (+0x08..+0x0B) at the first pass where the record is in use. The original
    copies the enemy file's record whole into the combatant (R025+0x0267) and the destination keeps the stack bytes it held until the enemy
    first moves; the C# leaves it 0 (DARKLANDS_BATTLE_STALE_DEST), and a dragon's breath at a spot (target -1, facing_to_target R014+0x306A)
    turns toward it."""
    out = {}
    for line in lines:
        w = line.split()
        if len(w) < 6 or "cbt" not in w:
            continue
        records = bytes.fromhex(w[w.index("cbt") + 1])
        for i in range(5, COUNT):
            r = records[i * RECORD:(i + 1) * RECORD]
            if i not in out and r[0x14] != 0:
                out[i] = tuple(v - 65536 if v >= 32768 else v for v in struct.unpack_from("<2H", r, 8))
    return [(i,) + out[i] for i in sorted(out)]


def record_session(exe: Path, game: str, steps, save: Path | None, budget: int, battle_records: bool, no_wall_draws: bool, coverage: bool,
                   snapshots=None) -> dict:
    """The live run of a session, as the payload the recording stores and the comparison reads (always a ClockTrace: the clock jumps of a
    battle's long passes are given to the C#; the combatant records of every pass only with battle_records)."""
    import time
    t = time.time()
    cov = {} if coverage else None
    trace = TraceList() if battle_records else ClockTrace()
    original, seeds, log, end, files, set_up = run_original(exe, game, steps, save, budget, trace, no_wall_draws, cov, snapshots)
    return {"states": original, "seeds": seeds, "log": log, "end": end, "files": files, "set_up": set_up, "trace": list(trace),
            "jumps": clock_jumps(trace), "bwait": list(BWAIT_ACTUAL), "stale": stale_destinations(trace) if battle_records else None, "cov": cov,
            "seconds": time.time() - t}
