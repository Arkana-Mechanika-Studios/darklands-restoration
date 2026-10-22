"""Card oracle: every card function of the ORIGINAL DARKLAND.EXE, run with the card engine's services.

usage: python -I -B card_oracle.py <DARKLAND.EXE> <game folder> <states folder> <out cards.json.gz>
           [--cards 2,6,0x13] [--workers 8] [--coverage DIR]

The states folder holds the start states the C# exports (tests/Darklands.Cards.Tests/Oracle,
CardOracleStates: DARKLANDS_CARD_ORACLE_EXPORT): for location 3 and 120 and options 0..9 a saved
game in the original's format and its side values. Each state is booted once in a DOS machine
(dosmachine.py): the world loader of the card engine (world_load, R043 1462:0100: DARKLAND.LST, .CTY,
.LOC, .DSC), then the original's own saved-game loader (R037 1C7D:0D24) on the saved game, then the
values a saved game does not hold (random generator, bankers, city record, the card engine's
variables as the C# card context starts them). The boot is kept as a snapshot.

Each card (card table R136+0xD090 -> its RTLink vector -> record 44..135) then runs from every start
state, choosing option 0..9 like the golden traces, plain and probing:

* original code runs for real: the card, the card engine's rules (items_usable, saints_usable, the
  text expansion), R029 rules, R039/s030/s031/... helpers through their overlay vectors, the CRT;
* stubbed (presentation and input): drawing the card (the picture, the party panel; the card's
  .MSG is loaded for real), card_text (recorded: block, frame and the block as the original's
  text_expand gives it), card_options + card_menu (recorded: block, option states; the choice comes
  from the policy; probing, every offered line's hint handler opt_fn[k] is asked as Shift over the
  line does, and an item/saint line's choice is made in its pop-up box), the hourglass, message
  boxes, the full screens (shop, camp, ...), battles (result 0, as the C# without a battle);
* recorded: every rand() draw, the next card, and the saved game the original's own writer (R037
  1462:2ACE) makes of the state after the card.

The corpus holds per card and run the trace lines and the bytes of the final saved game that differ
from the start state's. tests/Darklands.Cards.Tests/Oracle/CardOracleTests replays it against the C#.
"""
from __future__ import annotations

import argparse
import gzip
import json
import os
import re
import struct
import sys
import time
from multiprocessing import Pool
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from dosmachine import DS, P, TRAMPOLINE_SEG as TRAMPOLINE, Machine, Stop, linear  # noqa: E402
import savdiff  # noqa: E402
from unicorn import UC_HOOK_MEM_READ  # noqa: E402
from unicorn.x86_const import UC_X86_REG_CS, UC_X86_REG_IP  # noqa: E402

# ---- DGROUP ---------------------------------------------------------------------------------
CUR_LOC, CARD, NEXT_CARD, HOLDRAND = 0x907E, 0xA772, 0xE48A, 0x7B20
PREV_SCREEN, CARD_BEFORE_PREVIOUS, MAP_LEAVE = 0xA88D, 0xA88F, 0xA891
ADVANCE_HOURS = (P(0x1367), 0x05C8)
CITY_REC, CITY_DESC = 0x9785, 0x978B
OPT_ON, OPT_FN, OPT_FLAGS = 0xE99C, 0xEA14, 0xEE76
CARD_ITEMS, CARD_SAINTS = 0xEE43, 0xEE4B
MENU_MEMBER, MENU_SLOT, MENU_POPUP, POPUP_ALL = 0xEE6C, 0xEE6A, 0xEE6E, 0x5204
ITEM_FN, SAINT_FN = 0xEC00, 0xECF6
POPUP_LAYOUT, ITEM_LAYOUT, SAINT_LAYOUT, LAYOUT_SIZE = 0xEA64, 0xEB5A, 0xEC50, 0xF6   # struct layout {..., +0xA6 handlers}
MSG_BUF = 0x9085
# text variables (the reference's names)
VAR_CHOSEN = (0xEE8C, 0xEE8E, 0xEE90, 0xEE92, 0xEE94)
VAR_MONEY = (0xEE9C, 0xEEA2, 0xEEA8, 0xEEAE, 0xED46)
VAR_NUMBERS = (0xED4C, 0xED50)
VAR_TEXTS = (0xED6D, 0xED95, 0xEDBD, 0xEDE5)
VAR_ITEM, VAR_LOC, VAR_LOC2, VAR_SUPPORT, VAR_CITY_LOCATION = 0xED5C, 0xE89B, 0xED54, 0xED5E, 0xED52
CONDITIONS = 0x9C30                 # the conditional passages of the texts (bytes)
CARD_NAME = 0xE3D4
BANKERS = 0x4BB6
REPORT_SKILLS = 0x26BC
SAINT_MESSAGES = 0x01BD             # "show saintly changes" (Alt-C)
PARTY, PARTY_SIZE = 0x9C69, 0xA67E
CARD_TABLE = (P(0x2DAE), 0x0000)
SAVED = "saves/dksave0.sav".replace("/", "\\")   # what the original's writer creates (the folder is empty)
# card engine (R043 150B) and services
CARD_LOAD, CARD_LOAD_NEAR, MSG_LOAD = (P(0x150B), 0x000A), (P(0x150B), 0x0110), (P(0x150B), 0x020E)
CARD_TEXT, CARD_OPTIONS, TEXT_EXPAND, CARD_MENU = (P(0x150B), 0x03B0), (P(0x150B), 0x049A), (P(0x150B), 0x06FE), (P(0x150B), 0x1142)
HOURGLASS = (P(0x150B), 0x2A04)
SAVE_RECT = (P(0x047C), 0x04D5)        # gfx_save_rect (screen under a box -> the segment of a DOS block)
MESSAGE_BOX = (P(0x1367), 0x0C30)
FILM_PAN = (P(0x030B), 0x0004)        # root: plays <name>.PAN (the films), presentation
SHOW_MESSAGE = (P(0x0000), 0x24BC)     # show_message (text, ticks): a box for a while
HAS_ITEM, MEMBER_BIT = (P(0x0E76), 0x118E), (P(0x0E76), 0x1260)
RAND = (P(0x06A1), 0x29B6)
TIME = (P(0x06A1), 0x23A8)             # time(long *)
WORLD_LOAD, SAV_LOAD, SAV_WRITE = (P(0x1462), 0x0100), (P(0x1C7D), 0x0D24), (P(0x1462), 0x2ACE)


def zero(m):
    return (0, 0)


# Presentation and input the card engine and the rules reach: they return at once with the value
# given (name -> (link-time segment, offset, AX)). Everything else runs from the original bytes.
PRESENTATION = {
    # root 0000: menu bar, party panel, boxes, keys
    "menu_bar_run": (0x0000, 0x0000, 0), "party_panel_frames": (0x0000, 0x1588, 0), "party_panel_rows": (0x0000, 0x1716, 0),
    "member_status_letter": (0x0000, 0x18FA, 0), "member_bars": (0x0000, 0x1A9C, 0), "draw_frame_box": (0x0000, 0x2286, 0),
    "text_box": (0x0000, 0x23E6, 0), "show_difficulty": (0x0000, 0x1438, 0), "flush_keys": (0x0000, 0x1384, 0),
    "copy_rect_clipped": (0x0000, 0x14F6, 0), "hot_keys": (0x0000, 0x0CAA, 0), "exit_if_requested": (0x0000, 0x1338, 0),
    # root 047C: graphics, mouse, keyboard, sound
    "gfx_copy_rect": (0x047C, 0x0553, 0), "gfx_text": (0x047C, 0x055A, 0), "gfx_draw_pic": (0x047C, 0x05A5, 0),
    "gfx_recolor_rect": (0x047C, 0x04DC, 0), "gfx_fill_rect": (0x047C, 0x0952, 0), "gfx_line": (0x047C, 0x0D62, 0),
    "gfx_draw_sprite": (0x047C, 0x04EF, 0), "set_palette": (0x047C, 0x058B, 0),
    "key_pressed": (0x047C, 0x073D, 1), "get_key": (0x047C, 0x0744, 0x1C0D), "sound_play": (0x047C, 0x078A, 0),
    "mouse_set_cursor": (0x047C, 0x124F, 0), "mouse_move": (0x047C, 0x1266, 0),
    "mouse_show": (0x047C, 0x1293, 0), "mouse_hide": (0x047C, 0x12C3, 0), "font_height": (0x047C, 0x051B, 8),
    "char_width": (0x047C, 0x04E3, 6),
    "gfx_0545": (0x047C, 0x0545, 0), "gfx_04f8": (0x047C, 0x04F8, 0), "gfx_0568": (0x047C, 0x0568, 0),
    "gfx_056f": (0x047C, 0x056F, 0), "gfx_059e": (0x047C, 0x059E, 0),
    # the driver calls of loading a picture into memory (root 0617:045C, the portrait add_member R029+0x4650 loads)
    "gfx_0de6": (0x047C, 0x0DE6, 0), "gfx_05bc": (0x047C, 0x05BC, 0), "gfx_0f60": (0x047C, 0x0F60, 0),
    "gfx_05b5": (0x047C, 0x05B5, 0), "gfx_05ae": (0x047C, 0x05AE, 0),
    # root 0617: pictures
    "pic_draw": (0x0617, 0x01B2, 0), "pic_draw_1": (0x0617, 0x0230, 0), "pic_draw_pal": (0x0617, 0x024A, 0),
    "pic_draw_0": (0x0617, 0x0264, 0), "pic_draw_1r": (0x0617, 0x04D0, 0), "pic_draw_open": (0x0617, 0x04E2, 0),
    "pic_draw_mem": (0x0617, 0x0546, 0),
    # root 0265: text drawing
    "draw_text_far": (0x0265, 0x0004, 0), "draw_text": (0x0265, 0x006E, 0), "draw_text_centered": (0x0265, 0x00C4, 0),
    "draw_text_wrapped_far": (0x0265, 0x00F0, 0), "measure_text_wrapped_far": (0x0265, 0x0614, 0),
    "measure_text_wrapped": (0x0265, 0x0654, 0), "text_pixel_width": (0x0265, 0x084E, 0),
    "rect_copy_paged": (0x03EC, 0x000E, 0),
    # the card engine's drawing (R043 150B)
    "card_redraw_01e0": (0x150B, 0x01E0, 0), "card_frame_28e6": (0x150B, 0x28E6, 0), "wait_continue": (0x150B, 0x2826, 0x1C0D),
    "hint_show_1d56": (0x150B, 0x1D56, 0), "card_redraw_13f2": (0x150B, 0x13F2, 0),
    "palette_fade": (0x17E5, 0x0008, 0), "palette_17e5_01ec": (0x17E5, 0x01EC, 0), "palette_17e5_024c": (0x17E5, 0x024C, 0),
    # saved games: the battle file
    "battle_restore_vec": (0x09C0, 0x1B17, 0), "battle_save_vec": (0x09C0, 0x1B21, 0),
    "mem_log": (0x1462, 0x0000, 0),
}
DRAW_TEXT_WRAPPED = (P(0x0265), 0x0134)
# the full screens and the world's moves the cards reach (recorded; the C# OracleScreens records its own)
BARTER, CAMP, ITEM_STORE = (P(0x0E76), 0x21AA), (P(0x0E76), 0x222A), (P(0x0E76), 0x225C)   # R029 wrappers (screen + card redraw)
PRAYER = (P(0x1462), 0x0000)          # R034 saint prayer screen (thunk 09C0:1EAF): (who, saint, 1) -> result
BATTLE_RUN = (P(0x0E76), 0x0000)      # R002 battle_run (thunk 09C0:18BF)
CARDS_SHUTDOWN, CARDS_STARTUP = (P(0x1462), 0x054E), (P(0x1462), 0x078E)   # R043: the world to locs.tmp/events.tmp and back
BATTLE_BYTE, BATTLE_RESULT = 0x907A, 0xA893   # DS:907A (cleared by battle_run), DS:A893
SIDE_MESSAGE = (P(0x150B), 0x1F40)     # (who, colour): msg_buf beside a member's portrait, wait for a key


class CardRun:
    """The policy and the trace of one run (the same policy as the C# TracingScreen)."""

    def __init__(self, option: int, probe: bool, messages):
        self.option, self.probe, self.messages = option, probe, messages
        self.menus = 0
        self.path = None                     # [n, ...]: the k-th menu chooses the (n mod count)-th enabled line (variant part "P2_0_1")
        self.force = None                    # (block, line): the first menu of that block chooses that line whatever its state
        self.lines: list[str] = []
        self.card_name = ""
        self.texts_name = ""                 # the .MSG whose blocks are loaded (card_load, or the oracle's preload)
        self.draws = 0
        self.stray: set[str] = set()         # out-of-bounds reads the original made (see Oracle.stray_read)


class Oracle:
    def __init__(self, exe: bytes, game: str, states: Path, coverage: bool = False):
        self.exe, self.game, self.states = exe, game, states
        self.m = Machine(exe, game, coverage=coverage)
        self.run: CardRun | None = None
        self.snapshots = {}
        self.initial_savs = {}
        self.in_card = False
        self.card_code = None
        self.messages = MessageFiles(Path(game) / "MSGFILES")
        self.cards = self.card_table()
        self.names = card_names()
        for name, (seg, off, ax) in PRESENTATION.items():
            rec = "R043" if seg in (0x150B, 0x17E5) or (seg, off) == (0x1462, 0x0000) else None
            self.m.stub(P(seg), off, (lambda v: lambda m: (v, 0))(ax), name, record=rec)
        self.install()

    # ---- the card table ----------------------------------------------------------------
    def card_table(self) -> dict[int, tuple]:
        m = self.m
        out = {}
        for k in range(1, 366):
            off, seg = struct.unpack("<HH", m.read(CARD_TABLE[0], CARD_TABLE[1] + 4 * k, 4))
            t = m.thunks.get(linear(seg, off))
            if t is None:
                continue
            rec, ts, to, name, target = t
            out[k] = (rec, ts, to, target)
        return out

    # ---- boot --------------------------------------------------------------------------
    def boot(self, key: str):
        if key in self.snapshots:
            self.m.restore(self.snapshots[key])
            self.initial_sav = self.initial_savs[key]
            return
        m = self.m
        if "clean" not in self.snapshots:
            self.snapshots["clean"] = m.snapshot()
        m.restore(self.snapshots["clean"])
        sav = (self.states / f"state-{key}.sav").read_bytes()
        extra = json.loads((self.states / f"state-{key}.json").read_text())
        m.put_word(DS, CUR_LOC, struct.unpack_from("<h", sav, 0x7C)[0])
        m.ensure(43)
        m.call(*WORLD_LOAD, [m.push_string("darkland.loc")])
        m.vfs.put("saves\\oracle.sav", sav)
        m.ensure(37)
        m.call(*SAV_LOAD, [m.push_string("oracle.sav"), DS])
        # what a saved game does not hold
        m.write(DS, HOLDRAND, struct.pack("<I", extra["random"]))
        m.write(DS, BANKERS, struct.pack("<hhh", extra["fugger"], extra["medici"], extra["hanse"]))
        m.put_word(DS, REPORT_SKILLS, 1 if extra["reportSkillChanges"] else 0)
        m.write(DS, SAINT_MESSAGES, bytes([1 if extra["showSaintlyChanges"] else 0]))
        city = bytes.fromhex(extra["cityRecord"])
        off, seg = m.word(DS, CITY_REC), m.word(DS, CITY_REC + 2)
        m.write(seg, off, city)
        # city_desc: the C# takes the description of the working city record's city (TextVariables, UNCERTAIN there)
        dsc = (Path(self.game) / "DARKLAND.DSC").read_bytes()
        m.write(DS, CITY_DESC, dsc[1 + 0x50 * extra["city"]:1 + 0x50 * (extra["city"] + 1)])
        self.reset_engine()
        self.initial_sav = self.initial_savs[key] = sav
        m.scratch_sp = m.scratch_sp   # strings pushed during the boot stay
        self.snapshots[key] = m.snapshot()

    def reset_engine(self):
        """The card engine's variables as a new C# CardContext has them."""
        m = self.m
        m.write(DS, OPT_FLAGS, bytes(20))
        m.write(DS, OPT_ON, bytes(20))
        m.write(DS, CARD_ITEMS, struct.pack("<8h", *([-1] * 8)))
        for a in (MENU_MEMBER, MENU_SLOT, MENU_POPUP, POPUP_ALL, NEXT_CARD):
            m.put_word(DS, a, 0)
        # the text variables as a new CardVariables has them: $ChosenOneName..$ChosenFiveName = members 0..4,
        # money, numbers, texts, names, item 0, $LocName/$LocName2 none, support 0, $CityLocation 0, no conditions
        for k, a in enumerate(VAR_CHOSEN):
            m.put_word(DS, a, k)
        for a in VAR_MONEY:
            m.write(DS, a, bytes(6))
        for a in VAR_NUMBERS:
            m.write(DS, a, bytes(4))
        for a in VAR_TEXTS:
            m.write(DS, a, bytes(40))
        m.write(P(0x2E38), 0x1A96, bytes(5 * 30))            # var_named
        m.put_word(DS, VAR_ITEM, 0)
        m.put_word(DS, VAR_LOC, -1)
        m.put_word(DS, VAR_LOC2, -1)
        m.write(DS, VAR_SUPPORT, bytes(14))
        m.put_word(DS, VAR_CITY_LOCATION, 0)
        m.write(DS, CONDITIONS, bytes(10))

    # ---- services ------------------------------------------------------------------------
    def install(self):
        m = self.m
        m.stub(*SAVE_RECT, self.save_rect)
        m.stub(*CARD_LOAD, self.card_load, record="R043")
        self.card_load.__func__.outside = True
        m.stub(*CARD_LOAD_NEAR, self.card_load_near, record="R043")
        self.card_load_near.__func__.outside = True
        m.hook(*CARD_TEXT, self.card_text, record="R043")
        m.hook(*CARD_OPTIONS, self.card_options, record="R043")
        m.stub(*DRAW_TEXT_WRAPPED, self.draw_text_wrapped)
        m.stub(*SIDE_MESSAGE, self.side_message, record="R043")
        m.stub(*CARD_MENU, self.card_menu, record="R043")
        self.card_menu.__func__.outside = True
        m.stub(*HOURGLASS, self.hourglass, record="R043")
        m.stub(*MESSAGE_BOX, self.message_box)
        m.stub(*FILM_PAN, self.film)
        m.stub(*SHOW_MESSAGE, self.message_box)
        m.hook(*RAND, self.rand)
        # reads through index -1 of the location and event tables: the original then reads memory that holds something
        # else (a card run without its quest); the run is marked
        for lo, hi, what in ((linear(P(0x2E38), 0x10BE) - 4, linear(P(0x2E38), 0x10BE), "locations[-1]"),
                             (linear(P(0x2E38), 0x1B2C) - 4, linear(P(0x2E38), 0x1B2C), "events[-1]")):
            m.uc.hook_add(UC_HOOK_MEM_READ, (lambda w: lambda uc, acc, addr, size, val, ud: self.stray_read(w))(what),
                          begin=lo, end=hi - 1)
        # the card's own code reading through a null far pointer (the record of an empty event slot, e.g. the quest slot
        # DS:E7D8 when the card runs without its quest): the fields come from the interrupt vectors at 0000:0000, which
        # differ from machine to machine; the run is marked too. (The rules' event scans read empty slots so on every
        # run and are not counted.)
        m.uc.hook_add(UC_HOOK_MEM_READ, lambda uc, acc, addr, size, val, ud: self.null_read(uc), begin=0, end=0xFF)
        m.stub(*BARTER, self.barter, record="R029")
        m.stub(*CAMP, lambda mm: (self.log("screen camp %d" % mm.sarg(0)), (0, 0))[1], record="R029")
        m.stub(*ITEM_STORE, lambda mm: (self.log("screen itemstore"), (0, 0))[1], record="R029")
        m.stub(*PRAYER, self.prayer, record="R034")
        m.stub(*BATTLE_RUN, self.battle, record="R002")
        m.stub(*CARDS_SHUTDOWN, zero, record="R043")
        m.stub(*CARDS_STARTUP, zero, record="R043")

    def save_rect(self, m):
        """gfx_save_rect: the screen under a box is kept in a DOS block, whose segment the caller frees afterwards
        (R037+0x43E6 through 044A:0164, which ends the game if DOS refuses): a real block, one paragraph."""
        seg = m.alloc(1)
        return (seg or 0, 0)

    def null_read(self, uc):
        if self.run is not None and self.in_card and self.card_code is not None:
            here = uc.reg_read(UC_X86_REG_CS) * 16 + uc.reg_read(UC_X86_REG_IP)
            if self.card_code[0] <= here < self.card_code[1]:
                self.run.stray.add("null pointer")

    def stray_read(self, what: str):
        if self.run is not None and self.in_card:
            self.run.stray.add(what)

    def log(self, line: str):
        if self.run is not None:
            self.run.lines.append(line)

    def barter(self, m):
        i, off, seg, x, y = m.sarg(0), m.arg(1), m.arg(2), m.arg(3), m.arg(4)
        if off == 0 and seg == 0:   # the shop slot's own name, R136+0xA70F[i]
            off, seg = m.word(P(0x290E), 0x207F + 4 * i), m.word(P(0x290E), 0x207F + 4 * i + 2)
        name = m.cstr(seg, off, 80).decode("latin-1")
        self.log("screen barter %d %X %s" % (i, (y << 16) | x, name))
        return (0, 0)

    def prayer(self, m):
        self.log("prayer %d %d" % (m.sarg(0), m.sarg(1)))
        return (0, 0)

    def battle(self, m):
        """battle_run(mode, side, seed, 9 parameters): no battle, as the C# without one (result 0)."""
        args = [m.sarg(k) for k in range(12)]
        self.log("battle %d %d %s" % (args[1], args[2], " ".join(str(a) for a in args[3:])))
        m.write(DS, BATTLE_BYTE, bytes(1))
        m.put_word(DS, BATTLE_RESULT, 0)
        return (0, 0)

    def shown(self, name: str, pic: str) -> str:
        """card_load and card_load_near both load the picture and the card's texts; the trace says "show" when
        the texts are another card's than the ones shown, else "picture" (the C# Show / ShowPicture)."""
        line = "picture %s" % pic.lower() if name == self.run.card_name else "show %s %s" % (name, pic.lower())
        self.run.card_name = self.run.texts_name = name
        return line

    def card_load(self, m):
        name = m.cstr(DS, m.arg(0)).decode("latin-1")
        pic = m.cstr(m.arg(2), m.arg(1)).decode("latin-1")
        # the rules redraw the card after a screen (thunk 09C0:1EA5 from R029): the C# screen does that itself
        caller = m.word(m.reg("SS"), m.reg("SP") + 2)
        if caller == TRAMPOLINE:
            self.log("redraw %s %s" % (name, pic.lower()))
            self.run.card_name = self.run.texts_name = name
        else:
            self.log(self.shown(name, pic))
        m.call(*MSG_LOAD, [m.arg(0)])
        return (0, 0)

    def card_load_near(self, m):
        name = m.cstr(DS, m.arg(0)).decode("latin-1")
        pic = m.cstr(DS, m.arg(1)).decode("latin-1")
        self.log(self.shown(name, pic))
        m.call(*MSG_LOAD, [m.arg(0)])
        return (0, 0)

    def card_text(self, m):
        block, how = m.sarg(0), m.sarg(1)
        # C# TextFrame: 0 and 2 draw on the picture, anything else on a blank page
        self.pending = "text %d %d" % (block, 0 if how in (0, 2) else 1)

    def card_options(self, m):
        self.options_block = m.sarg(0)
        self.pending = None
        self.options_text = None
        self.pending_options = True

    def draw_text_wrapped(self, m):
        """The first text drawn after card_text / card_options is the block as text_expand gave it."""
        text = m.cstr(DS, m.arg(1), 1200)
        if self.pending is not None:
            self.log("%s %s" % (self.pending, text.hex()))
            self.pending = None
        elif self.pending_options:
            self.options_text = text.hex()
            self.pending_options = False
        return (0, 0)

    def side_message(self, m):
        self.log("tell %d %s" % (m.sarg(0), m.cstr(DS, MSG_BUF, 400).decode("latin-1")))
        return (0, 0)

    def card_menu(self, m):
        run = self.run
        run.menus += 1
        if run.menus > 50:
            raise Stop("The card keeps asking: it never moves on.")
        block = self.options_block
        lines = self.messages.option_lines(run.texts_name, block)
        states = list(m.read(DS, OPT_ON, 20))[:lines]
        self.log("menu %d [%s] %s" % (block, "".join(str(s) for s in states), self.options_text or ""))
        enabled = [i for i, s in enumerate(states) if s == 1]
        forced = run.force is not None and run.force[0] == block and run.force[1] < len(states)
        if forced:
            # a forced line (the start-state variant part "F<block>.<line>"): chosen whatever its state, once
            choice = run.force[1]
            run.force = None
        else:
            if not enabled:
                raise Stop("Menu %d offers no option." % block)
            if run.path is not None and run.menus - 1 < len(run.path):
                choice = enabled[run.path[run.menus - 1] % len(enabled)]
            else:
                first = enabled.index(run.option) if run.option in enabled else 0
                choice = enabled[(first + run.menus - 1) % len(enabled)]
        if run.probe:
            for i, s in enumerate(states):
                if s != 0 or (forced and i == choice):
                    self.log("hint %d: %s" % (i, self.hint(i)))
        m.put_word(DS, MENU_POPUP, 0)
        if run.probe:
            kind = self.messages.popup_kind(run.texts_name, block, choice)
            if kind is not None:
                pc = self.popup_choice(kind)
                if pc is not None:
                    # card_mouse opens the box: DS:EA64 = DS:EB5A (items) / DS:EC50 (saints), so DS:EB0A = item_fn / saint_fn
                    m.write(DS, POPUP_LAYOUT, m.read(DS, SAINT_LAYOUT if kind else ITEM_LAYOUT, LAYOUT_SIZE))
                    m.put_word(DS, MENU_POPUP, 1)
                    m.put_word(DS, MENU_MEMBER, pc[0])
                    m.put_word(DS, MENU_SLOT, pc[1])
        self.log("choose %d" % choice)
        return (choice, 0)

    def hint(self, k: int) -> str:
        """opt_fn[k]() as card_mouse asks it under Shift: menu_member/menu_slot = line / 4, line % 4."""
        m = self.m
        m.put_word(DS, MENU_MEMBER, k // 4)
        m.put_word(DS, MENU_SLOT, k % 4)
        m.write(DS, MSG_BUF, b"\0")
        off, seg = m.word(DS, OPT_FN + 4 * k), m.word(DS, OPT_FN + 4 * k + 2)
        ax, dx = m.call(seg, off, [])
        chance = ax - 0x10000 if ax & 0x8000 else ax
        member = dx - 0x10000 if dx & 0x8000 else dx
        msg = m.cstr(DS, MSG_BUF, 300).decode("latin-1") if chance == -1 else ""
        return "%d '%s' %d" % (chance, msg, member)

    def popup_choice(self, saints: bool):
        """The member and slot the pop-up box lets the player choose (the C# TracingScreen.PopupChoice)."""
        m = self.m
        lst = struct.unpack("<4h", m.read(DS, CARD_SAINTS if saints else CARD_ITEMS, 8))
        every = m.word(DS, POPUP_ALL) != 0
        size = m.word(DS, PARTY_SIZE)
        for i in range(size - 1, -1, -1):
            status = m.byte(DS, PARTY + 0x80 * i)
            if status in (0, 5):
                continue
            for j in range(4):
                if lst[j] == -1:
                    continue
                if every:
                    return (i, j)
                if saints:
                    if m.call(*MEMBER_BIT, [i, lst[j]])[0] != 0:
                        return (i, j)
                elif m.call(*HAS_ITEM, [i, lst[j]])[0] == 1:
                    return (i, j)
        return None

    def hourglass(self, m):
        self.log("time %d" % m.sarg(0))
        return (0, 0)

    def film(self, m):
        self.log("film %s" % m.cstr(DS, m.arg(0), 40).decode("latin-1").lower())
        return (0, 0)

    def message_box(self, m):
        self.log("tell -2 %s" % m.cstr(DS, m.arg(0), 400).decode("latin-1"))
        return (0, 0)

    def rand(self, m):
        if self.run is not None:
            self.run.draws += 1

    def clock(self) -> int:
        """time(0) of the machine's fixed DOS clock, as srand((unsigned)time(0)) takes it (the C# ClockSeed)."""
        m = self.m
        m.restore(self.snapshots["clean"]) if "clean" in self.snapshots else None
        ax, dx = m.call(*TIME, [0, 0])
        return ax

    # ---- a run -------------------------------------------------------------------------------
    def run_card(self, card: int, key: str, option: int, probe: bool) -> dict:
        key, force, path = split_key(key)
        self.boot(key)
        m = self.m
        rec, seg, off, target = self.cards[card]
        m.put_word(DS, CARD, card)
        self.run = CardRun(option, probe, self.messages)
        self.run.force = force
        self.run.path = path
        self.options_block = 0
        self.pending = None
        self.options_text = None
        self.pending_options = False
        m.ensure(rec)
        self.card_code = self.m.extent(rec)
        name = self.names.get(card)
        if name and (name + ".msg")[:12].lower() in self.messages.entries:
            # the card's own texts, as the card before it would usually have left them (some cards never load
            # theirs: they keep the previous card's, see CARDS.md)
            m.ensure(43)
            m.call(*MSG_LOAD, [m.push_string(name)])
            self.run.texts_name = name
            m.ensure(rec)
        try:
            self.in_card = True
            ax, _ = m.call(seg, off, [])
            self.in_card = False
            nxt = ax - 0x10000 if ax & 0x8000 else ax
            end = "next %d" % nxt
            if not -6 <= nxt <= -2:
                self.after_card(nxt)
        except Stop as e:
            end = "stops %s" % str(e)[:300]
            nxt = None
        self.in_card = False
        run, self.run = self.run, None
        state = m.read(DS, HOLDRAND, 4)
        result = {"trace": run.lines, "end": end, "random": "%08X" % struct.unpack("<I", state)[0], "draws": run.draws}
        if run.stray:
            result["stray"] = sorted(run.stray)
        if nxt is not None:
            result["sav"] = self.final_sav()
        return result

    def after_card(self, nxt: int):
        """What the main loop (root 03A1:000A) does after a card that does not start a battle:
        `if (DS:A772 != 0xc) DS:A891 = 0; DS:A88F = DS:A88D; DS:A88D = DS:A772; DS:A772 = next; thunk 09C0:190F(0)`
        (advance_hours(0)); the C# CardDispatcher.RunCard does the same."""
        m = self.m
        cur = m.word(DS, CARD)
        if cur != 0x0C:
            m.put_word(DS, MAP_LEAVE, 0)
        m.put_word(DS, CARD_BEFORE_PREVIOUS, m.word(DS, PREV_SCREEN))
        m.put_word(DS, PREV_SCREEN, cur)
        m.put_word(DS, CARD, nxt)
        m.call(*ADVANCE_HOURS, [0])

    def final_sav(self) -> dict:
        m = self.m
        for k in [k for k in m.vfs.files if k.startswith("saves\\dksave")]:
            del m.vfs.files[k]
        m.ensure(37)
        try:
            m.call(*SAV_WRITE, [m.push_string("Darklands")])
        except Stop as e:
            return {"error": str(e)[:300]}
        out = bytes(m.vfs.files[SAVED])
        # the place name is compared as a text (the original writes stack bytes after it and after the comment)
        return {"place": out[:0x15].split(bytes(1))[0].decode("latin-1"), "sections": savdiff.diff(self.initial_sav, out)}


def split_key(key: str):
    """A run key "L3-Vfoo+F12.3-O5": the start state's key (the "F<block>.<line>" parts of the variant dropped: they change no state,
    they force the choice of that line in the first menu of that block) and the forced (block, line) or None and the menu path (variant part "P<n>_<n>..") or None."""
    if "-V" not in key:
        return key, None, None
    head, rest = key.split("-V", 1)
    variant, option = rest.rsplit("-O", 1)
    parts = variant.split("+")
    force = None
    path = None
    keep = []
    for p in parts:
        if re.fullmatch(r"F\d+\.\d+", p):
            force = tuple(int(x) for x in p[1:].split("."))
        elif re.fullmatch(r"P\d+(_\d+)*", p):
            path = [int(x) for x in p[1:].split("_")]
        else:
            keep.append(p)
    return (head + ("-V" + "+".join(keep) if keep else "") + "-O" + option), force, path


def card_names() -> dict[int, str]:
    """The cards' MSG names as the C# CardTable has them (src/Darklands.Cards/CardTable.cs)."""
    import re
    path = HERE.parent.parent / "src" / "Darklands.Cards" / "CardTable.cs"
    out = {}
    for m in re.finditer(r'new\((\d+), "([^"]*)", (-?\d+)\)', path.read_text(encoding="utf-8")):
        if m.group(2):
            out[int(m.group(1))] = m.group(2)
    return out


class MessageFiles:
    """The cards' .MSG files in MSGFILES (as the C# GameAssets.Messages reads them)."""

    def __init__(self, path: Path):
        self.data = path.read_bytes()
        n = struct.unpack_from("<H", self.data, 0)[0]
        self.entries = {}
        for i in range(n):
            r = 2 + i * 0x18
            name = self.data[r:r + 12].split(b"\0")[0].decode("latin-1").lower()
            size, off = struct.unpack_from("<ii", self.data, r + 0x10)
            self.entries[name] = (off, size)
        self.cache = {}

    def blocks(self, card: str):
        if card not in self.cache:
            e = self.entries.get((card + ".msg")[:12].lower())   # catalog_find: strnicmp(.., 12)
            out = []
            if e is not None:
                d = self.data[e[0]:e[0] + e[1]]
                n, k = d[0], 1
                for _ in range(n):
                    k += 5
                    end = d.index(b"\0", k)
                    out.append(d[k:end])
                    k = end + 1
            self.cache[card] = out
        return self.cache[card]

    def option_lines(self, card: str, block: int) -> int:
        bl = self.blocks(card)
        if block < 0 or block >= len(bl):
            return 20
        return min(20, sum(1 for b in bl[block] if b in (0x15, 0x10, 0x16, 0x06)))

    def popup_kind(self, card: str, block: int, line: int):
        """True: the line opens the saints' box, False the items' box, None no box."""
        bl = self.blocks(card)
        if block < 0 or block >= len(bl):
            return None
        markers = [b for b in bl[block] if b in (0x15, 0x10, 0x16, 0x06)]
        if line >= len(markers):
            return None
        return {0x10: False, 0x16: True}.get(markers[line])


# ---- driver ----------------------------------------------------------------------------------
_W = {}


def init_worker(exe_path, game, states, coverage_dir):
    _W["args"] = (exe_path, game, states, coverage_dir)
    _W["oracle"] = Oracle(Path(exe_path).read_bytes(), game, Path(states), coverage=bool(coverage_dir))
    _W["coverage"] = coverage_dir


def run_card_safe(card: int) -> tuple[int, dict]:
    """run_card_all; a failure of the emulator itself is reported and the worker's machine is made anew."""
    try:
        return run_card_all(card)
    except Exception as e:  # noqa: BLE001
        exe_path, game, states, coverage_dir = _W["args"]
        _W["oracle"] = Oracle(Path(exe_path).read_bytes(), game, Path(states), coverage=bool(coverage_dir))
        return card, {"error": "%s: %s" % (type(e).__name__, str(e)[:300])}


def run_card_all(card: int) -> tuple[int, dict]:
    o = _W["oracle"]
    runs = {}
    t = time.time()
    for probe in (False, True):
        for location in (3, 120):
            for option in range(10):
                key = "L%d-O%d" % (location, option)
                runs["%s %s" % ("probe" if probe else "plain", key)] = o.run_card(card, key, option, probe)
    if _W["coverage"]:
        cov = o.m.coverage_result()
        o.m.cov_insn_rec, o.m.cov_edge_rec = {}, {}
        with open(os.path.join(_W["coverage"], "cov-%d.jsonl" % os.getpid()), "a") as f:
            f.write(json.dumps({"unit": "card %X" % card, **cov}, separators=(",", ":")) + "\n")
    return card, {"function": o.cards[card][3], "record": "R%03d" % o.cards[card][0], "seconds": round(time.time() - t, 1),
                  **share_texts(runs)}


def share_texts(runs: dict) -> dict:
    """The texts of a card's runs once: a trace line "text 2 0 <hex>" becomes "text 2 0 #k" with texts[k] = hex."""
    texts, index = [], {}
    for r in runs.values():
        for i, line in enumerate(r["trace"]):
            if line.startswith(("text ", "menu ")):
                head, _, tail = line.rpartition(" ")
                if tail and all(ch in "0123456789abcdef" for ch in tail):
                    if tail not in index:
                        index[tail] = len(texts)
                        texts.append(tail)
                    r["trace"][i] = "%s #%d" % (head, index[tail])
    return {"texts": texts, "runs": runs}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("exe")
    ap.add_argument("game")
    ap.add_argument("states")
    ap.add_argument("out")
    ap.add_argument("--cards")
    ap.add_argument("--workers", type=int, default=8)
    ap.add_argument("--coverage")
    a = ap.parse_args()
    if a.coverage:
        os.makedirs(a.coverage, exist_ok=True)
    probe = Oracle(Path(a.exe).read_bytes(), a.game, Path(a.states))
    cards = sorted(probe.cards) if not a.cards else [int(x, 0) for x in a.cards.split(",")]
    clock = probe.clock()
    del probe
    # results are kept as they arrive (<out>.partial.jsonl): a long run can be inspected, and resumed
    partial = Path(a.out + ".partial.jsonl")
    results = {}
    if partial.exists():
        for line in partial.read_text(encoding="utf-8").splitlines():
            k, v = json.loads(line)
            if "error" not in v:
                results[k] = v
    todo = [c for c in cards if c not in results]
    with Pool(a.workers, initializer=init_worker, initargs=(a.exe, a.game, a.states, a.coverage)) as pool, \
            partial.open("a", encoding="utf-8") as f:
        for k, v in pool.imap_unordered(run_card_safe, todo):
            f.write(json.dumps([k, v], separators=(",", ":")) + "\n")
            f.flush()
            results[k] = v
            if "error" in v:
                print("card %X: %s" % (k, v["error"]), flush=True)
    corpus = {"authority": "test_only_original_byte_execution", "source": "tools/oracle/card_oracle.py",
              "clock": "%04X" % clock, "cards": {"%03X" % k: results[k] for k in sorted(results) if k in cards}}
    with gzip.open(a.out, "wt", encoding="utf-8") as f:
        json.dump(corpus, f, separators=(",", ":"), sort_keys=True)
    print("cards", len(results))


if __name__ == "__main__":
    main()
