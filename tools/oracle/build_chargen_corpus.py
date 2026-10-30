"""Original-byte corpus for the character generator (overlay R037).

usage: python -I -B build_chargen_corpus.py <DARKLAND.EXE> <out chargen.json>

Runs, from the unchanged EXE, with generated characters:
  occupation_list  R037+0x691E  which occupations may come next (DS:8BD2[], DS:8C00, DS:8BFC)
  serve_term       R037+0x7BCC  one term (with its career events and items, followed for real)
  career_events    R037+0x7D8A  the items, saints and formulas of an occupation
Item definitions are generated (item-definition-table far pointers seeded into a scratch segment); the
generator's own tables come from the EXE. Only inputs and results are stored; no game bytes.
"""
from __future__ import annotations

import json
import random
import struct
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from run_unit import CoverageSet, commit_coverage, run  # noqa: E402

DS = 0x321A
HOLDRAND = 0x7B20
AGE, ATTRS, SKILLS, SEX, ITEM_COUNT = 0xE81C, 0xE86E, 0xE875, 0xE821, 0xE888
TAKEN, LAST, PREVIOUS, THIRD, BACKGROUND = 0x4402, 0x4428, 0x442A, 0x8C12, 0x8C02
LIST, COUNT, CURSOR, POINTS, TERM_POINTS = 0x8BD2, 0x8C00, 0x8BFC, 0x8BFA, 0x8C48
FAR = 0x2E38
SAINTS, FORMULAS, KIT, ITEM_DEFINITION_TABLE = 0x3B1A, 0x3B04, 0x3984, 0x0890
ITEM_SEG = 0x6000
SECTIONS = [136, 29, 37]
FN = {"occupation_list": "22BE", "serve_term": "356C", "career_events": "372A"}


def w(v: int) -> str:
    return struct.pack("<H", v & 0xFFFF).hex()


def ds(offset: int, data: str) -> dict:
    return {"offset": "%04X" % offset, "hex": data}


def far(offset: int, data: str, segment: int = FAR) -> dict:
    return {"segment": "%04X" % segment, "offset": "%04X" % offset, "hex": data}


def item_definition_table(r):
    """200 item definitions: type (+0x1E), weight (+0x25), quality (+0x26) in 0x30-byte records."""
    defs, memory = [], []
    for k in range(200):
        rec = bytearray(0x30)
        t, wt, q = r.randint(0, 255), r.randint(0, 40), r.randint(0, 99)
        rec[0x1E], rec[0x25], rec[0x26] = t, wt, q
        defs.append([t, wt, q])
        memory.append(far(ITEM_DEFINITION_TABLE + 4 * k, struct.pack("<HH", k * 0x30, ITEM_SEG).hex()))
        memory.append(far(k * 0x30, rec.hex(), ITEM_SEG))
    return defs, memory


def character(r, adult: bool):
    age = r.choice([15, 20, 25, 30, 35, 40, 45, 50, 55, 60, 65, 70]) if adult else 0
    attrs = [r.choice([1, 12, 15, 20, 25, 30, 35, 40, r.randint(1, 60)]) for _ in range(6)] + [99]
    skills = [r.choice([0, 5, 6, 10, 15, 20, 30, r.randint(0, 60)]) for _ in range(19)]
    p = r.choice([0.0, 0.05, 0.15, 0.4])
    taken = [1 if r.random() < p else 0 for _ in range(37)]
    last, prev, third = (r.choice([-1] + list(range(37))) for _ in range(3))
    background = r.choice([0, 1, 2, 3, 4, 5])
    sex = r.randint(0, 1)
    inputs = {"age": age, "attributes": attrs, "skills": skills, "taken": taken, "last": last, "previous": prev,
              "third": third, "background": background, "female": sex}
    memory = [ds(AGE, w(age)), ds(ATTRS, bytes(a & 0xFF for a in attrs).hex()),
              ds(SKILLS, bytes(s & 0xFF for s in skills).hex()), ds(TAKEN, bytes(taken).hex()),
              ds(LAST, w(last)), ds(PREVIOUS, w(prev)), ds(THIRD, w(third)), ds(BACKGROUND, w(background)),
              ds(SEX, "%02X" % sex), ds(LIST, "AA" * 40), ds(COUNT, w(0x5555)), ds(CURSOR, w(7))]
    return inputs, memory


def knowledge(r):
    saints = bytes(r.choice([0, 0, 0, r.randint(0, 255)]) for _ in range(20))
    formulas = bytes(r.choice([0, 0, 1, 2, 3, r.randint(0, 7)]) for _ in range(22))
    state = r.getrandbits(32)
    kit_count = r.randint(0, 5)
    inputs = {"saints": saints.hex(), "formulas": formulas.hex(), "state": state, "kitCount": kit_count}
    memory = [far(SAINTS, saints.hex()), far(FORMULAS, formulas.hex()), ds(HOLDRAND, struct.pack("<I", state).hex()),
              ds(ITEM_COUNT, "%02X" % kit_count)]
    return inputs, memory


def cases(r):
    out = []
    for _ in range(1500):
        inputs, memory = character(r, adult=r.random() < 0.85)
        out.append(("occupation_list", memory, [], inputs))
    defs, def_memory = item_definition_table(r)
    for _ in range(600):
        adult = r.random() < 0.8
        inputs, memory = character(r, adult)
        k_inputs, k_memory = knowledge(r)
        p = r.randint(0, 36) if adult else r.randint(0, 5)
        points = r.randint(0, 50)
        inputs.update(k_inputs, occupation=p, points=points)
        out.append(("serve_term", memory + k_memory + def_memory + [ds(POINTS, w(points)), ds(TERM_POINTS, w(0x1234))],
                    ["%04X" % p], inputs))
    for _ in range(400):
        k_inputs, k_memory = knowledge(r)
        k = r.randint(0, 36)
        k_inputs.update(occupation=k)
        out.append(("career_events", k_memory + def_memory, ["%04X" % k], k_inputs))
    return out + targeted(random.Random(20261019)), defs


def targeted(r):
    """occupation_list for the combinations the random characters miss (docs/faithfulness/branches-missed.csv):
    every pair of the last two occupations, each occupation taken alone, young and strong characters."""
    out = []

    def case(last, prev, taken=None, age=None, attr1=None, base=None):
        inputs = dict(base) if base is not None else character(r, adult=True)[0]
        inputs["last"], inputs["previous"] = last, prev
        if taken is not None:
            inputs["taken"] = taken
        if age is not None:
            inputs["age"] = age
        if attr1 is not None:
            inputs["attributes"][1] = attr1
        memory = [ds(AGE, w(inputs["age"])), ds(ATTRS, bytes(a & 0xFF for a in inputs["attributes"]).hex()),
                  ds(SKILLS, bytes(s & 0xFF for s in inputs["skills"]).hex()), ds(TAKEN, bytes(inputs["taken"]).hex()),
                  ds(LAST, w(last)), ds(PREVIOUS, w(prev)), ds(THIRD, w(inputs["third"])),
                  ds(BACKGROUND, w(inputs["background"])), ds(SEX, "%02X" % inputs["female"]),
                  ds(LIST, "AA" * 40), ds(COUNT, w(0x5555)), ds(CURSOR, w(7))]
        out.append(("occupation_list", memory, [], inputs))

    for last in range(-1, 37):
        for prev in range(-1, 37):
            case(last, prev, taken=[0] * 37)
    for k in range(37):
        taken = [0] * 37
        taken[k] = 1
        for last, prev in ((-1, -1), (r.randint(0, 36), r.randint(0, 36))):
            case(last, prev, taken=taken)
    for age in (15, 20, 25, 26, 30):
        for attr1 in (20, 24, 25, 40):
            case(-1, -1, taken=[0] * 37, age=age, attr1=attr1)
    # every pair again for an adult with every attribute and skill at 40 (the occupations that also ask for
    # attributes, skills and a man), both sexes
    for female in (0, 1):
        for last in range(-1, 37):
            for prev in range(-1, 37):
                inputs, _ = character(r, adult=True)
                inputs.update(age=30, attributes=[40] * 6 + [99], skills=[40] * 19, female=female)
                inputs["extra"] = True
                case(last, prev, taken=[0] * 37, base=inputs)
    return out


def main() -> None:
    exe = Path(sys.argv[1]).read_bytes()
    r = random.Random(20261017)
    result = []
    all_cases, defs = cases(r)
    seen = CoverageSet()
    for name, memory, args, inputs in all_cases:
        extra = inputs.pop("extra", False)
        res = run(exe, {"unit": name, "entry": {"segment": "18C8", "offset": FN[name]}, "sections": SECTIONS, "ds": DS,
                        "memory": memory, "args": args, "follow": True, "track_all": True, "coverage": True,
                        "coverage_defer": True})
        if not seen.adds(res) and extra:
            continue   # the strong characters' sweep is kept where it exercises something new
        commit_coverage(res)
        result.append({"function": name, "inputs": inputs, "ax": res["final"]["ax"], "dx": res["final"]["dx"],
                       "changed": {c["linear"]: c["after"] for c in res["memChanged"]}})
    corpus = {
        "authority": "test_only_original_byte_execution",
        "source": "tools/oracle/build_chargen_corpus.py",
        "dataSegment": "%04X" % DS,
        "scratchA": "%04X" % LIST,
        "holdrand": "%04X" % HOLDRAND,
        "items": defs,
        "cases": result,
    }
    Path(sys.argv[2]).write_text(json.dumps(corpus, separators=(",", ":")) + "\n")
    print(len(result), "cases")


if __name__ == "__main__":
    main()
