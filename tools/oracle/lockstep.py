"""Lockstep: one scripted session played by the ORIGINAL DARKLAND.EXE and by the C# rewrite, states compared.

usage: python -I -B lockstep.py <DARKLAND.EXE> <game folder> <script> <out folder> [--save <DKSAVEn.SAV>]
           [--headless <Darklands.Host.Headless.exe>] [--battle-trace] [--no-wall-draws] [--coverage]

The sessions are in lockstep-sessions/ (each from the player's SAVES/DKSAVE0.SAV, given with --save).

The script holds the C# headless host's steps (src/Darklands.Host.Headless/Program.cs), one per line, `#` comments:
key:<c>|enter|esc|space|up|...|alt<c>|f<n>, move:<x>,<y>, click:<x>,<y>, option:<n> (the pointer onto line n of
the card menu shown), state:<name>; wait/shot steps are for the C# only. With --save both start from that saved game
(the script loads it from the start screen: key:t, key:l).

1. The original runs from its own start (R000+0x0022, command line -Q) in the DOS machine of dosmachine.py,
   extended here with what a whole session needs:
   * EMS through INT 67h (256 pages, the frame at D000; mapping copies pages in and out); the EMS functions'
     far pointer (root 05F8:000E) is the handler at once and the EMS check (05F8:0064) passes;
   * the interrupt vectors kept here, not at 0000:0000 (the root image sits at linear 0 at its link-time
     addresses); the timer interrupt is raised at the PIT's programmed rate from the instruction count
     (BYTES_PER_MS bytes of code per millisecond), into the game's own handler (root 047C:01EB); the DOS time,
     the BIOS ticks and so time(0) follow the same count: every run is the same run;
   * no drivers: misc.exe is not loaded (root 047C:07CE, 047C:08C8 return) and no sound driver is chosen (no
     CONFIG.DRK: nsound.dlc); the slot table answers 0, except: keys (047C:073D peeks the key's code, 047C:0744
     takes it), DOS console input (INT 21h 0Bh/07h/08h, the load's question) and the mouse (INT 33h 3/5/6,
     DS:6EEC..6EFB) from the script; the screen-save and picture buffers (047C:04D5, 047C:05BC/05AE) as real DOS
     blocks; fixed font metrics (6 x 8, as card_oracle), so menus are chosen by line (option:<n>);
   * the copy-protection question (R041+0x0000) passes at once, as the C# has it;
   * a step is given when the game is idle: IDLE_POLLS polls (key_pressed, or the menu bar's handler root 0000:0000
     every screen's loop calls) and IDLE_MS of emulated time with no random number drawn and the same card; a click
     holds the button CLICK_MS once the game has read it;
   * the battle: one call of the game's timer callback (047C:000A) at the head of each pass (R014+0x3166), the
     time-based timer kept out of the pass (the C# host then advances one 60 Hz tick a pass); the ticks each battle's
     set-up takes (combat_state_clear to the enemies' turn, then to the first pass: its loads run at the emulator's
     speed) are measured and given to the C# (DARKLANDS_BATTLE_SETUP_TICKS);
   * state:<name> runs the original's own saved-game writer (R037 1462:2ACE) on a copy of the machine;
   * every srand((unsigned)time(0)) (a call that follows `call time`) is recorded: the clock seeds; the other
     seeds (party seed + place, battle seed) are the game's own and the C# computes them.
2. The C# headless host plays the same steps with those clock seeds (clock:<n>,...), its state steps write the
   game being played through OriginalSaves.Write.
3. Per state: savdiff sections, the random generator's state (DS:7B20 / MscRandom.State) and the card; header
   word +0x84 (DS:E898, the card screen's text state) is reported apart (the C# writes the loaded file's). The
   saved games each wrote with Alt-S are compared too (the header's two texts apart).

Out folder: report.json, original.log (keys, clicks, cards, clock seeds, states with the Save/Load flags),
original-<state>.sav, cs/<state>.sav, original-file-*, cs-saves/, rewrite.log; with --battle-trace a line per
battle pass in original-battle.trace / rewrite-battle.trace (the combat clock DS:07D4 / Battle.Clock and the
random state; compare them by clock value: the start-of-battle pause lasts a different number of passes).
With --coverage the original's executed blocks and branch outcomes over the whole session go to <out>/cov/cov-0.jsonl (the
format of card_oracle's and battle_oracle's; block_coverage.py takes such a folder as a source), only if every state agrees.
The C# side has its own debugging aids: DARKLANDS_TRACE_MAP (each step of the map's marker),
DARKLANDS_RANDOM_TRACE (each seed and draw with its callers), DARKLANDS_STEP_TRACE (each combatant's step).
"""
from __future__ import annotations

import argparse
import json
import struct
import sys

import unicorn
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from dosmachine import DS, REG, SENTINEL_SEG, Machine, Stop, linear  # noqa: E402

BYTES_PER_MS = 200                 # the machine's speed: executed code bytes per millisecond
EMS_SEG, FRAME, EMS_PAGES = 0xF800, 0xD000, 256
DRIVER_SEG, DRIVER_FIRST, DRIVER_LAST = 0x047C, 0x04C7, 0x0791
DRIVER_LOAD, DRIVER_SLOTS = (0x047C, 0x07CE), (0x047C, 0x08C8)
MAIN = (0x0E76, 0x0022)             # R000+0x0022
SAVE_RECT = (0x047C, 0x04D5)
EMS_CHECK, MOUSE_INIT = (0x05F8, 0x0064), (0x047C, 0x11F6)
MOUSE_X, MOUSE_Y, MOUSE_BUTTONS, MOUSE_PRESSES, MOUSE_SHIFT, MOUSE_ON = 0x6EEC, 0x6EEE, 0x6EF0, 0x6EF2, 0x6EFA, 0x6EFB
COPY_PROTECTION = (0x1462, 0x0000)  # R041+0x0000 (thunk 09C0:1FD1)
CHAR_WIDTH, FONT_HEIGHT = (0x047C, 0x04E3), (0x047C, 0x051B)
HOTSPOTS = 0xE96E                   # the card menu's hot spots: count, highlight, width, x, y[20], height[20] (bytes)
PIC_OPEN, PIC_CLOSE = (0x047C, 0x05BC), (0x047C, 0x05AE)
KEY_PRESSED, GET_KEY = (0x047C, 0x073D), (0x047C, 0x0744)   # driver slots: a key waiting? / the next key (scan << 8 | char)
CARD, HOLDRAND = 0xA772, 0x7B20     # the card being run; the random generator's state (MSC holdrand)
SAVE_ITEM, LOAD_ITEM = 0x9920, 0x9934  # the Game menu's Save Game / Load Saved Game flags (1 on), which hot_keys obeys
SAV_WRITE = (0x1462, 0x2ACE)        # R037+0x2ACE (comment): the saved-game writer
MACHINE_STATE = ("resident", "trampolines", "call_stack", "mcbs", "dta", "scratch_sp", "count", "next_irq", "irqs", "pit_divisor",
                 "pit_latch", "pit_write", "vsync", "ems_pages", "ems_map", "ems_saved", "ivt", "waits", "pic_size", "progress", "held", "button_read", "_dos_extended", "_ticked", "battle_passes", "press_ms", "idle_mark", "idle_since", "mouse", "presses", "releases")
IDLE_MS = 3000                      # and as long without a random number drawn, a card change or a step
IDLE_POLLS = 2000
BATTLE_PASS_LIN = 0x0E76 * 16 + 0x3166   # R014+0x3166 combat_input, called once a pass of the battle's loop
CLEAR_TIMERS_VEC = (0x09C0, 0x1C61)  # R014+0x010B's vector to R020+0x06B6 (combat_state_clear: the combat clock back to 0)
ENEMY_TURN_VEC = (0x09C0, 0x1C43)    # R014+0x0280's vector to R018+0x0706 (the enemies' turn), after the set-up's file loads
WALL_DRAWS = ((0x8381, 0x838B), (0x8438, 0x8442), (0x84AE, 0x84BE))   # R014+0x827A: push 2; call random_n; (after)
NEXT_TICK = 0xD9E4                 # the clock value of the battle's next combat tick (set at R014+0x0311)
COMBAT_CLOCK = 0x07D4               # the combat clock the timer callback counts while the battle runs
TIMER_CALLBACK = (0x047C, 0x000A)   # the game's timer callback (its counters, the combat clock DS:07D4), called by its INT 8 handler
BATTLE_QUIET = 5_000_000            # bytes of code without the time-based timer after a battle pass began
POLL_POINTS = {0x00000}             # root 0000:0000, the menu bar's handler: every screen's loop calls it each pass
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
        # The root image sits at linear 0 (link-time addresses): the vectors cannot live at 0000:0000. They are kept here
        # (INT 21h 25h/35h, the timer interrupt); the two readers of the vector table are given what they would find.
        self.write(EMS_SEG, 0, bytes([0xCD, 0x67, 0xCF]))      # int 67h; iret
        self.write(EMS_SEG, 0x0A, b"EMMXXXX0")
        self.write(EMS_SEG, 0x122, bytes([0xFF] * 4))       # the list of lists' device chain: empty
        self.write(EMS_SEG, 0x30, bytes([0xCF]))                # IRET: every vector at first
        self.write(EMS_SEG, 0x40, bytes([0x90, 0xCF]))          # INT 8: the BIOS tick (hooked), IRET
        self.ivt = {n: (EMS_SEG, 0x30) for n in range(256)}
        self.ivt[8] = (EMS_SEG, 0x40)
        self.ivt[0x67] = (EMS_SEG, 0)
        # root 05F8: the EMS functions call the far pointer at 05F8:000E, first 05F8:0012 (EMS check, then the int 67h
        # vector into the pointer): the pointer is the vector at once, and the check (05F8:0064) passes (DS:0942 = 0)
        self.write(0x05F8, 0x0E, struct.pack("<HH", 0, EMS_SEG))
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
            if self.rt.root[DRIVER_SEG * 16 + off] == 0xE8:
                self.write(DRIVER_SEG, off + 3, struct.pack("<HH", 0x50, EMS_SEG))
        for off in [o for o in self.driver_slots() if (DRIVER_SEG, o) not in (KEY_PRESSED, GET_KEY, SAVE_RECT, PIC_OPEN, PIC_CLOSE, CHAR_WIDTH, FONT_HEIGHT)]:
            self.stub(DRIVER_SEG, off, self._driver("047C:%04X" % off), "driver 047C:%04X" % off)
        # gfx_save_rect: the screen under a box goes to a DOS block whose segment the caller frees later: a real block
        self.stub(*SAVE_RECT, lambda m: (m.alloc(1) or 0, 0), "save_rect")
        # a picture drawn into memory (root 0617:045C): 047C:05BC(width, height) opens the buffer, 047C:05AE returns its segment
        self.pic_size = 0
        self.stub(*PIC_OPEN, lambda m: (setattr(m, "pic_size", m.arg(0) * m.arg(1)), (0, 0))[1], "pic_open")
        self.stub(*PIC_CLOSE, lambda m: (m.alloc((m.pic_size + 15) // 16 or 1) or 0, 0), "pic_close")
        # the driver's font metrics, fixed as in card_oracle: the menus' hot spots (DS:E96E) need lines of some height
        self.stub(*CHAR_WIDTH, lambda m: (6, 0), "char_width")
        self.stub(*FONT_HEIGHT, lambda m: (8, 0), "font_height")
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
        self.battle_trace = None        # a list: a line at the head of each battle pass (--battle-trace)
        # the clock ticks each battle's set-up took (the loads before the enemies' turn, then up to the first pass): the C# is given them
        # (the loads, up to the clock's read at R014+0x0311 that sets the first tick's time DS:D9E4 = read + 8, from there to the first pass)
        self.set_up_ticks: list[tuple[int, int, int]] = []
        self._setting_up = None
        self.hook(*CLEAR_TIMERS_VEC, lambda m: setattr(m, "_setting_up", [None]) if 14 in m.resident else None)
        self.hook(*ENEMY_TURN_VEC, self._enemy_turn)
        self._hook_address(BATTLE_PASS_LIN)
        self.uc.hook_add(unicorn.UC_HOOK_MEM_READ, self._button_read, begin=DS * 16 + MOUSE_BUTTONS, end=DS * 16 + MOUSE_BUTTONS + 1)
        self.idle_mark = None
        self.idle_since = 0
        self.mouse = [160, 100, 0]
        self.presses, self.releases = [0, 0], [0, 0]
        self.on_key = None              # called (outside the emulation) before a key of the script is given
        self.waits = 0                  # key polls with nothing to give
        self.progress = 0
        self.stub(*KEY_PRESSED, self._key_pressed, "key_pressed")
        self.stub(*GET_KEY, self._get_key, "get_key")
        self._key_pressed.__func__.outside = True
        self._get_key.__func__.outside = True

    def driver_slots(self) -> list[int]:
        """The slot table misc.exe fills: `jmp far 0:0` (EA 00 00 00 00) or `call 048A` (lazy entries) between 047C:04C7 and
        the driver loader."""
        root, base = self.rt.root, DRIVER_SEG * 16
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
        mark = (self.read(DS, HOLDRAND, 4), self.word(DS, CARD))
        if mark != self.idle_mark:
            # the game is still at work (it drew random numbers or moved to another card): not idle yet
            self.idle_mark = mark
            self.waits = 0
            self.idle_since = self.ms
        if self.waits < IDLE_POLLS or self.ms < self.idle_since + IDLE_MS:
            self.waits += 1
            return None
        while self.script and self.script[0][0] not in ("key", "move", "click", "option"):
            kind, value = self.script.pop(0)
            if kind == "state":
                self.capture(value)
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
                if self._setting_up is not None:
                    loads = self._setting_up[0] or 0
                    read = (self.word(DS, NEXT_TICK) - 8) & 0xFFFF
                    self.set_up_ticks.append((loads, read - loads, self.word(DS, COMBAT_CLOCK) - read))
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
        k = self._next_key()
        return (0 if k is None else k, 0)

    def _get_key(self, m):
        # get_key waits for a key: the game is idle at once (a click still held is let go first)
        if self.held:
            self._release()
        self.waits, self.idle_since, self.idle_mark = IDLE_POLLS, -IDLE_MS, (self.read(DS, HOLDRAND, 4), self.word(DS, CARD))
        k = self._next_key()
        while k is None:                # the steps before the key (moves, clicks, states)
            if self.held:
                self._release()
            self.waits, self.idle_since = IDLE_POLLS, -IDLE_MS
            k = self._next_key()
        self.script.pop(0)
        self.waits = 0
        self.idle_since = self.ms
        self.log.append("key %04X at %d ms in %s" % (k, self.ms, " < ".join(self.call_stack[-4:])))
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
            self.write(ss, sp, struct.pack("<HHH", ip, cs, self.uc.reg_read(REG["EFLAGS"]) & 0xFFFF))
            self.set_reg("SP", sp)
            seg, off = self.ivt[8]
            self.uc.reg_write(REG["EFLAGS"], self.uc.reg_read(REG["EFLAGS"]) & ~0x300)
            self.uc.reg_write(REG["CS"], seg)
            self.uc.reg_write(REG["IP"], off)
            return
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
    def _driver(self, coord: str):
        def h(m):
            m.driver_calls[coord] = m.driver_calls.get(coord, 0) + 1
            return (0, 0)
        return h

    def valid(self, lin: int) -> bool:
        # int86 runs `int nn; retf` built on the stack (root 06A1:1C58); root 0000:0000 (the menu bar) is code too
        return DS * 16 <= lin < DS * 16 + 0x10000 or lin < len(self.rt.root) or super().valid(lin)

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
            if ah == 0x25:
                self.ivt[ax & 0xFF] = (self.reg("DS"), self.reg("DX"))
                return
            if ah == 0x35:
                seg, off = self.ivt[ax & 0xFF]
                self.uc.reg_write(REG["ES"], seg)
                self.set_reg("BX", off)
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


def read_script(path: Path) -> list[tuple[str, str]]:
    out = []
    for line in path.read_text(encoding="utf-8").splitlines():
        line = line.split("#")[0].strip()
        if line:
            kind, _, value = line.partition(":")
            out.append((kind, value))
    return out


CARD_CALL = (0x03A1, 0x02CC)        # the main loop's `call far es:[bx]` of the card table (DS:A772 = the card)
SRAND = (0x06A1, 0x29A4)


HEADLESS = HERE.parent.parent / "src" / "Darklands.Host.Headless" / "bin" / "Release" / "net10.0" / "Darklands.Host.Headless.exe"


def run_original(exe: Path, game: str, steps, save: Path | None, budget: int, battle_trace=None, no_wall_draws=False, cov_out: dict | None = None):
    """The session in the original: (states {name: (sav, random, card)}, clock seeds in order, log, how it ended, saved files,
    each battle's set-up ticks). cov_out, when given, is filled with the coverage record."""
    s = Session(exe.read_bytes(), game, max_instructions=budget, coverage=cov_out is not None)
    if no_wall_draws:
        # an experiment: the animated pieces' picture variant (random_n(2) in R014+0x827A, each combat_draw) leaves the game's generator as
        # it was (the C# makes those draws: it then parts from the original at the battle's first frame)
        kept = {}
        for before, after in WALL_DRAWS:
            s.hook(0x0E76, before, lambda m: kept.__setitem__("r", m.read(DS, HOLDRAND, 4)), record="R014")
            s.hook(0x0E76, after, lambda m: m.write(DS, HOLDRAND, kept["r"]), record="R014")
    if save:
        s.vfs.put("saves/dksave0.sav", save.read_bytes())
        if save.with_suffix(".BSV").exists():
            s.vfs.put("saves/dksave0.bsv", save.with_suffix(".BSV").read_bytes())
    s.script = list(steps)
    s.battle_trace = battle_trace
    seeds = []
    def srand(m):
        # a clock seed when the call follows `call time` (06A1:23A8; push ax): srand((unsigned)time(0)); other seeds come from the game
        ret = linear(m.word(m.reg("SS"), m.reg("SP") + 2), m.word(m.reg("SS"), m.reg("SP")))
        clock = bytes(m.uc.mem_read(ret - 14, 5)) == bytes([0x9A, 0xA8, 0x23, 0xA1, 0x06])
        if clock:
            seeds.append(m.arg(0))
        m.log.append("srand %d%s at %d ms in %s" % (m.arg(0), "" if clock else " (not the clock)", m.ms, " < ".join(m.call_stack[-2:])))
    s.hook(*SRAND, srand)
    s.hook(*CARD_CALL, lambda m: m.log.append("card %X at %d ms" % (m.word(DS, CARD), m.ms)))
    try:
        s.start()
        end = "the program ended"
    except Stop as e:
        end = str(e)[:600]
    if cov_out is not None:
        cov_out.update(s.coverage_result())
    files = {k.rpartition("\\")[2].upper(): bytes(v) for k, v in s.vfs.files.items()
             if k.startswith("saves") and k.endswith((".sav", ".bsv")) and k.rpartition("\\")[2] not in ("dksave0.sav", "dksave0.bsv")}
    return s.states, seeds, [line for line in s.log if not line.startswith("INT")], end, files, s.set_up_ticks


def run_rewrite(headless: Path, game: str, steps, save: Path | None, seeds, out: Path, battle_trace=False, set_up_ticks=(), clock_jumps=()):
    """The session in the C# headless host: (states {name: (sav, random, card)}, its output)."""
    import shutil
    import subprocess
    saves = out / "cs-saves"
    shutil.rmtree(saves, ignore_errors=True)
    saves.mkdir(parents=True)
    if save:
        shutil.copy(save, saves / "DKSAVE0.SAV")
        if save.with_suffix(".BSV").exists():
            shutil.copy(save.with_suffix(".BSV"), saves / "DKSAVE0.BSV")
    shots = out / "cs"
    shutil.rmtree(shots, ignore_errors=True)
    args = [str(headless), game, str(shots), "clock:" + ",".join(str(v) for v in (seeds or [1])), "saves:" + str(saves)]
    args += ["%s:%s" % (k, v) if v else k for k, v in steps]
    import os
    env = dict(os.environ, DARKLANDS_BATTLE_TICK_PER_PASS="1")   # one 60 Hz tick a battle pass, as the original is run here
    if battle_trace:
        env["DARKLANDS_BATTLE_TRACE"] = "1"
    if set_up_ticks:
        # the clock the original's battle set-ups took here (its loads run at the emulator's speed): the C#'s set-up takes none
        env["DARKLANDS_BATTLE_SETUP_TICKS"] = ";".join("%d,%d,%d" % p for p in set_up_ticks)
    if clock_jumps:
        # the clock a long pass of the original took beyond its one tick (the timer's time-based callbacks): the C# has no emulated time
        env["DARKLANDS_BATTLE_CLOCK_JUMPS"] = ";".join(",".join(str(v) for v in p) for p in clock_jumps)
    run = subprocess.run(args, capture_output=True, text=True, timeout=1800, env=env)
    states = {}
    for line in run.stdout.splitlines():
        if line.startswith("state ") and " card 0x" in line:
            name = line[6:line.index(":")]
            words = line.split()
            card, random = int(words[words.index("card") + 1], 16), words[words.index("random") + 1]
            states[name] = ((shots / (name + ".sav")).read_bytes(), int(random, 16) if random != "?" else None, card)
    return states, run.stdout + run.stderr


def compare(steps, original: dict, rewrite: dict) -> list[dict]:
    """Per state step in the script's order: the differences (savdiff sections, the random state, the card)."""
    import savdiff
    rows = []
    for kind, name in steps:
        if kind != "state":
            continue
        row = {"state": name}
        if name not in original or name not in rewrite:
            row["missing"] = [side for side, d in (("original", original), ("rewrite", rewrite)) if name not in d]
        else:
            (osav, orand, ocard), (csav, crand, ccard) = original[name], rewrite[name]
            d = savdiff.diff(csav, osav)
            head = d.get("head", {}).get("0", {})
            # +0x84 (DS:E898, the card screen's text state): the C# keeps the loaded file's (a known divergence, see the report)
            if "20" in head or "21" in head:
                row["text state"] = {k: v for k, v in head.items() if k in ("20", "21")}
                head = {k: v for k, v in head.items() if k not in ("20", "21")}
                if head:
                    d["head"]["0"] = head
                else:
                    del d["head"]
            if d:
                row["sav"] = d
            if orand != crand:
                row["random"] = {"original": "%08X" % orand, "rewrite": "%08X" % crand if crand is not None else None}
            if ocard != ccard:
                row["card"] = {"original": "%X" % ocard, "rewrite": "%X" % ccard}
        rows.append(row)
    return rows


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("exe")
    ap.add_argument("game")
    ap.add_argument("script")
    ap.add_argument("out")
    ap.add_argument("--save", help="a .SAV given to both as SAVES/DKSAVE0.SAV (with its .BSV)")
    ap.add_argument("--headless", default=str(HEADLESS))
    ap.add_argument("--budget", type=int, default=2_000_000_000)
    ap.add_argument("--battle-trace", action="store_true", help="original-battle.trace / rewrite-battle.trace: each battle pass")
    ap.add_argument("--no-wall-draws", action="store_true", help="experiment: R014+0x827A's random_n(2) leaves the generator as it was")
    ap.add_argument("--coverage", action="store_true", help="<out>/cov/cov-0.jsonl: the original's blocks and branch outcomes (kept only if all states agree)")
    a = ap.parse_args()
    out = Path(a.out)
    out.mkdir(parents=True, exist_ok=True)
    steps = read_script(Path(a.script))
    save = Path(a.save) if a.save else None
    import time
    t = time.time()
    trace = [] if a.battle_trace else None
    cov = {} if a.coverage else None
    original, seeds, log, end, files, set_up = run_original(Path(a.exe), a.game, steps, save, a.budget, trace, a.no_wall_draws, cov)
    if trace is not None:
        (out / "original-battle.trace").write_text(chr(10).join(trace), encoding="utf-8")
    t_original = time.time() - t
    (out / "original.log").write_text(chr(10).join(log + ["end: " + end]), encoding="utf-8")
    for name, (sav, _, _) in original.items():
        (out / ("original-%s.sav" % name)).write_bytes(sav)
    t = time.time()
    rewrite, output = run_rewrite(Path(a.headless), a.game, steps, save, seeds, out, a.battle_trace, set_up)
    if a.battle_trace:
        (out / "rewrite-battle.trace").write_text(chr(10).join(l for l in output.splitlines() if l.startswith("pass ")), encoding="utf-8")
    t_rewrite = time.time() - t
    (out / "rewrite.log").write_text(output, encoding="utf-8")
    rows = compare(steps, original, rewrite)
    # the saved games each game wrote itself (Alt-S): the same names, the same bytes by savdiff section
    import savdiff
    for name, data in sorted(files.items()):
        (out / ("original-file-" + name)).write_bytes(data)
        theirs = out / "cs-saves" / name
        if not name.endswith(".SAV"):
            continue
        row = {"file": name}
        if not theirs.exists():
            row["missing"] = ["rewrite"]
        else:
            d = savdiff.diff(theirs.read_bytes(), data)
            if d:
                row["sav"] = d
            if data[:0x64] != theirs.read_bytes()[:0x64]:
                row["texts"] = {"original": data[:0x64].hex(), "rewrite": theirs.read_bytes()[:0x64].hex()}
        rows.append(row)
    if cov is not None:
        if all(set(r) <= {"state", "file", "text state", "texts"} for r in rows):
            (out / "cov").mkdir(exist_ok=True)
            (out / "cov" / "cov-0.jsonl").write_text(json.dumps({"unit": Path(a.script).stem, **cov}, separators=(",", ":")) + "\n", encoding="utf-8")
        else:
            print("coverage not written: the states differ")
    report = {"script": a.script, "save": a.save, "clock seeds": seeds, "battle set-up ticks": set_up, "original end": end,
              "seconds": {"original": round(t_original), "rewrite": round(t_rewrite)}, "states": rows}
    (out / "report.json").write_text(json.dumps(report, indent=1), encoding="utf-8")
    first = next((r for r in rows if set(r) - {"state", "file", "text state", "texts"}), None)
    for r in rows:
        print("%-12s %s" % (r.get("state") or r["file"], "same" if set(r) <= {"state", "file", "text state", "texts"} else json.dumps(
            {k: v for k, v in r.items() if k not in ("state", "file")})[:300]))
    print("original: %s (%d s); rewrite %d s; first divergence: %s" % (end, t_original, t_rewrite,
                                                                       (first.get("state") or first["file"]) if first else "none"))


if __name__ == "__main__":
    main()
