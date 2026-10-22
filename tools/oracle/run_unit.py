"""Execute one original Darklands 483.07 function from the unchanged EXE bytes.

This is a bounded, synthetic-input Unicorn oracle for composition tests. It loads the
root image, the requested RTLink overlay chain and the preloaded data section at
their link-time addresses (no relocation is applied, so every segment word keeps its
link-time frame), seeds DS memory from the spec, enters the function with a sentinel
FAR return address and runs until that sentinel is reached.

Every CALL executed by the function itself (near, far, far-indirect) is *not* followed:
it is recorded with the caller's stack words and replaced by the scripted result for
that callee (AX/DX, optional DS writes). The output is therefore the function's own
contract: its ordered calls with arguments, its DS writes, and its returned AX/DX/flags.

usage: python -I run_unit.py <DARKLAND.EXE> <spec.json> [out.json]

Spec (JSON):
  unit, entry {segment, offset}  link-time CS:IP of the function
  sections [index...]            RTLink sections to load (chain to the root)
  ds                             DGROUP frame (0x321A for 483.07)
  callsites {"<linear hex>": "<callee name>"}   names for recorded calls
  args ["hex word", ...]         caller-pushed arguments, first argument first
  memory [{"offset": hex, "hex": bytes, "segment"?: hex}]   seeding (DS unless segment given)
  results {"<callee>": [{"ax":..,"dx":..,"writes":[{"offset","hex"}]}...]}  per call, in order
  arg_words {"<callee>": n}      stack words recorded per call (default 4)
  max_instructions               guard (default 200000)
  stub_targets {"SEG:OFF": name}  calls to these link-time targets (direct far or near) are stubbed
                                 and recorded under the name, also when following
  follow                         true: execute every call for real except callsites named in "stub"
                                 (whole call tree from original bytes; INT still stops the run)
  stub_return_words              true: a skipped call still writes its return address below SP
                                 (callers that later read stale stack words see what the real call left)
  read [{"segment", "offset", "length"}]   raw bytes to report after the run (e.g. stack words
                                 the function leaves, which the change lists filter out)
  coverage                       true: record executed basic blocks and conditional-branch outcomes
                                 (also on when the environment variable DL_COVERAGE_DIR is set; see below)

Coverage recording (tools/oracle/block_coverage.py): with "coverage" or DL_COVERAGE_DIR, a Unicorn
UC_HOOK_BLOCK hook records every translated block and the code hook records, for each executed
conditional branch (jcc, jcxz, loop*), the address that ran next. At the end of the run (and before
every at_hook, which may swap an overlay in) the linear addresses are mapped to original coordinates:
the record (root or RNNN) whose unchanged EXE bytes are at that address, and the payload offset in it
(root: the linear address). The result carries them as "coverage"; with DL_COVERAGE_DIR one JSON line
per run is also appended to <dir>/cov-<pid>.jsonl, so corpus builders need no change.
"""
from __future__ import annotations

import collections
import hashlib
import json
import os
import struct
import sys
from pathlib import Path

import capstone
import unicorn
from unicorn import x86_const as x86

EXE_SHA256 = "90138ae88acf66ad765e96c0465c1a4aa11eb09b74f365ed18c0f01beb9a86c2"
SENTINEL_SEG, SENTINEL_OFF = 0xF000, 0x0000
STACK_TOP = 0xFFF0   # MSC medium model: SS = DGROUP; the stack sits at the top of DGROUP
NEEDS_DECODE = {0xE8, 0x9A, 0xFF, 0xCD, 0xCC, 0xCE, 0xF4, 0xCF, 0x26, 0x2E, 0x36, 0x3E, 0x64, 0x65, 0x66, 0x67, 0xF0, 0xF2, 0xF3}
FLAGS = {"cf": 0, "pf": 2, "af": 4, "zf": 6, "sf": 7, "df": 10, "of": 11}
CONDITIONAL = {"jo", "jno", "jb", "jae", "je", "jne", "jbe", "ja", "js", "jns", "jp", "jnp", "jl", "jge", "jle", "jg",
               "jcxz", "jecxz", "loop", "loope", "loopne"}


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


class Rtlink:
    """Minimal RTLink/Plus section table reader (483.07 layout)."""

    def __init__(self, data: bytes):
        self.data = data
        hdr = struct.unpack_from("<14H", data, 0)
        pages, last, hdr_paras, cs = hdr[2], hdr[1], hdr[4], hdr[11]
        self.hdr_size = hdr_paras * 16
        mz_size = (pages - 1) * 512 + (last or 512)
        self.root = data[self.hdr_size:mz_size]
        self.reloc_table = [struct.unpack_from("<HH", data, hdr[12] + 4 * k) for k in range(hdr[3])]   # MZ relocations: (offset, segment)
        mgr = self.hdr_size + cs * 16
        rd = lambda o: struct.unpack_from("<H", data, mgr + o)[0]
        count_ptr, table_ptr, entsize = rd(0xB58), rd(0xB5A), rd(0xB5C)
        n = struct.unpack_from("<H", data, mgr + count_ptr)[0]
        self.sections = []
        for i in range(n):
            w = struct.unpack_from("<9H", data, mgr + table_ptr + i * entsize)
            file_off = (w[2] | ((w[3] & 0xFF) << 16)) * 16
            img_off = (file_off + 4 * w[5] + 15) & ~15
            self.sections.append({"index": i, "load_seg": w[0], "mem_paras": w[4],
                                  "parent": w[6], "file_offset": img_off, "length": w[8] * 16,
                                  "reloc_offset": file_off, "reloc_count": w[5]})   # (offset, segment) pairs before the image


class CoverageRecorder:
    """Executed instructions, translated blocks and conditional-branch outcomes of one run, in original
    coordinates. Linear addresses are resolved per "epoch" (between overlay swaps) against the EXE
    images: the owner of an address is the record whose bytes are there, preferring the records the
    spec loaded (last loaded first)."""

    def __init__(self, rt: "Rtlink", exe: bytes, loaded_order: list):
        self.rt, self.exe = rt, exe
        self.order = list(reversed(loaded_order)) + [s["index"] for s in rt.sections if s["index"] not in loaded_order]
        self.insns, self.blocks, self.edges = {}, {}, {}   # record -> set
        self._insn, self._block, self._edge = {}, {}, set()   # linear -> bytes, linear -> size, (jcc, next)

    def image_bytes(self, idx, linear, n):
        if idx == "root":
            return self.rt.root[linear:linear + n] if linear + n <= len(self.rt.root) else None
        s = self.rt.sections[idx]
        off = linear - s["load_seg"] * 16
        if off < 0 or off + n > s["length"]:
            return None
        return self.exe[s["file_offset"] + off:s["file_offset"] + off + n]

    def owner(self, linear, code):
        if linear + len(code) <= len(self.rt.root) and self.rt.root[linear:linear + len(code)] == code:
            return "root", linear
        for idx in self.order:
            if self.image_bytes(idx, linear, len(code)) == code:
                return "R%03d" % idx, linear - self.rt.sections[idx]["load_seg"] * 16
        return None, linear

    def flush(self, uc):
        resolved = {}
        for lin, code in self._insn.items():
            rec, pay = self.owner(lin, code)
            resolved[lin] = (rec, pay)
            self.insns.setdefault(rec or "?", set()).add(pay)
        for lin, size in self._block.items():
            rec, pay = resolved.get(lin) or self.owner(lin, bytes(uc.mem_read(lin, min(size, 16))))
            self.blocks.setdefault(rec or "?", set()).add((pay, size))
        for jcc, nxt in self._edge:
            rec, pay = resolved.get(jcc, (None, jcc))
            nrec, npay = resolved.get(nxt, (None, nxt))
            if rec is not None and nrec == rec:
                self.edges.setdefault(rec, set()).add((pay, npay))
        self._insn, self._block, self._edge = {}, {}, set()

    def result(self):
        return {"insns": {k: sorted(v) for k, v in self.insns.items()},
                "blocks": {k: sorted(v) for k, v in self.blocks.items()},
                "edges": {k: sorted(v) for k, v in self.edges.items()}}


def commit_coverage(result: dict) -> None:
    """Record a run made with "coverage_defer" (a case the builder keeps) in DL_COVERAGE_DIR."""
    cov_dir = os.environ.get("DL_COVERAGE_DIR")
    if cov_dir and "coverage" in result:
        with open(os.path.join(cov_dir, "cov-%d.jsonl" % os.getpid()), "a") as f:
            f.write(json.dumps({"unit": result.get("unit"), "error": None, **result["coverage"]}, separators=(",", ":")) + "\n")


class CoverageSet:
    """What a set of runs exercised (run results with "coverage"), to keep only the generated cases
    that exercise something new: `adds(result)` is true when the run reached an instruction or a
    branch outcome the set had not, and takes it in."""

    def __init__(self):
        self.insns, self.edges = set(), set()

    def adds(self, result: dict) -> bool:
        cov = result.get("coverage") or {}
        new_i = {(rec, p) for rec, ps in cov.get("insns", {}).items() for p in ps} - self.insns
        new_e = {(rec, tuple(e)) for rec, es in cov.get("edges", {}).items() for e in es} - self.edges
        self.insns |= new_i
        self.edges |= new_e
        return bool(new_i or new_e)


def run(exe: bytes, spec: dict, hooks: dict | None = None, at_hooks: dict | None = None) -> dict:
    """hooks: optional {callee name: f(cpu, stack_words) -> (ax, dx)} for stubs that must read or write memory.
    at_hooks: optional {linear address: f(cpu)} called when execution reaches that address (before the instruction runs),
    e.g. to swap an overlay into memory or to snapshot memory."""
    if sha256(exe) != EXE_SHA256:
        raise SystemExit("not the canonical 483.07 DARKLAND.EXE")
    rt = Rtlink(exe)
    cpu = unicorn.Uc(unicorn.UC_ARCH_X86, unicorn.UC_MODE_16)
    cpu.mem_map(0, 0x100000)
    loaded = [{"name": "root", "file_offset": rt.hdr_size, "length": len(rt.root),
               "linear": 0, "sha256": sha256(rt.root)}]
    cpu.mem_write(0, rt.root)
    for idx in spec["sections"]:
        s = rt.sections[idx]
        img = exe[s["file_offset"]:s["file_offset"] + s["length"]]
        cpu.mem_write(s["load_seg"] * 16, img)
        loaded.append({"name": "s%03d" % idx, "file_offset": s["file_offset"], "length": len(img),
                       "linear": s["load_seg"] * 16, "sha256": sha256(img)})
    ds = spec["ds"]
    for patch in spec.get("memory", []):
        seg = int(patch["segment"], 16) if "segment" in patch else ds
        cpu.mem_write(seg * 16 + int(patch["offset"], 16), bytes.fromhex(patch["hex"]))
    before = bytes(cpu.mem_read(ds * 16, 0x10000))
    before_all = bytes(cpu.mem_read(0, 0x100000)) if spec.get("track_all") else None

    STACK_SEG = int(str(spec.get("ss", ds)), 0) if isinstance(spec.get("ss"), str) else spec.get("ss", ds)
    # entry: caller-pushed argument words (first argument lowest), then FAR return to the sentinel
    args = [int(str(a), 16) for a in spec.get("args", [])]
    sp = STACK_TOP - 2 * len(args)
    if args:
        cpu.mem_write(STACK_SEG * 16 + sp, struct.pack("<%dH" % len(args), *args))
    sp -= 4
    cpu.mem_write(STACK_SEG * 16 + sp, struct.pack("<HH", SENTINEL_OFF, SENTINEL_SEG))
    cpu.reg_write(x86.UC_X86_REG_SS, STACK_SEG)
    cpu.reg_write(x86.UC_X86_REG_SP, sp)
    cpu.reg_write(x86.UC_X86_REG_DS, ds)
    cpu.reg_write(x86.UC_X86_REG_ES, ds)
    cpu.reg_write(x86.UC_X86_REG_BP, spec.get("bp", 0x5A5A))
    cpu.reg_write(x86.UC_X86_REG_SI, spec.get("si", 0x1357))
    cpu.reg_write(x86.UC_X86_REG_DI, spec.get("di", 0x2468))
    cpu.reg_write(x86.UC_X86_REG_EFLAGS, 0x0002 | (0x400 if spec.get("df") else 0))

    md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_16)
    callsites = {int(k, 16): v for k, v in spec.get("callsites", {}).items()}
    results = {k: list(v) for k, v in spec.get("results", {}).items()}
    arg_words = spec.get("arg_words", {})
    calls, ds_writes, executed, follows = [], [], set(), []
    recent = collections.deque(maxlen=24)
    decoded = {}   # address -> (bytes, instruction): the code of a loaded overlay does not change while it runs
    follow = bool(spec.get("follow"))
    stub_names = set(spec.get("stub", []))
    stub_targets = {k.upper(): v for k, v in spec.get("stub_targets", {}).items()}   # "SEG:OFF" -> name
    state = {"count": 0, "error": None}

    state["min_sp"] = sp
    cov_dir = os.environ.get("DL_COVERAGE_DIR")
    cov = CoverageRecorder(rt, exe, spec["sections"]) if (spec.get("coverage") or cov_dir) else None

    def on_block(uc, address, size, _):
        if address not in cov._block:
            cov._block[address] = size

    traced = {int(k, 16): v for k, v in spec.get("trace", {}).items()}
    trace = []

    def on_code(uc, address, size, _):
        state["count"] += 1
        if cov is not None and state.get("jcc") is not None:
            cov._edge.add((state["jcc"], address))
            state["jcc"] = None
        if address in traced:
            ss_, sp_ = uc.reg_read(x86.UC_X86_REG_SS), uc.reg_read(x86.UC_X86_REG_SP)
            bp_ = uc.reg_read(x86.UC_X86_REG_BP)
            trace.append({"name": traced[address], "bp": bp_, "ss": ss_,
                          "stack": list(struct.unpack("<8H", bytes(uc.mem_read(ss_ * 16 + sp_, 16)))),
                          "frame": list(struct.unpack("<24H", bytes(uc.mem_read(ss_ * 16 + ((bp_ - 0x30) & 0xFFFF), 48))))})
        state["min_sp"] = min(state["min_sp"], uc.reg_read(x86.UC_X86_REG_SP))
        if state["count"] > spec.get("max_instructions", 200000):
            state["error"] = "instruction limit"
            uc.emu_stop()
            return
        if address == SENTINEL_SEG * 16 + SENTINEL_OFF:
            uc.emu_stop()
            return
        if at_hooks and address in at_hooks:
            if cov is not None:
                cov.flush(uc)   # resolve what ran so far before the hook swaps code
            at_hooks[address](uc)
            decoded.clear()   # the hook may have loaded another overlay
        entry = decoded.get(address)
        if entry is None:
            code = bytes(uc.mem_read(address, 16))
            ins = next(md.disasm(code, address & 0xFFFF), None)
            if ins is None:
                state["error"] = "undecodable at %05x" % address
                uc.emu_stop()
                return
            entry = decoded[address] = (code, ins)
        code, ins = entry
        executed.add(address)
        if cov is not None:
            if address not in cov._insn:
                cov._insn[address] = code[:ins.size]
            if ins.mnemonic in CONDITIONAL:
                state["jcc"] = address
        recent.append(address)
        if ins.mnemonic in ("int", "into", "hlt", "iret"):
            state["error"] = "%s at %05x" % (ins.mnemonic, address)
            uc.emu_stop()
            return
        if ins.mnemonic not in ("call", "lcall"):
            return
        target = None
        if code[0] == 0x9A:
            off, seg = struct.unpack_from("<HH", code, 1)
            target = "%04X:%04X" % (seg, off)
        elif code[0] == 0xE8:
            cs = uc.reg_read(x86.UC_X86_REG_CS)
            target = "%04X:%04X" % (cs, (uc.reg_read(x86.UC_X86_REG_IP) + 3 + struct.unpack_from("<h", code, 1)[0]) & 0xFFFF)
        stub_name = stub_targets.get(target)
        if follow and callsites.get(address) not in stub_names and stub_name is None:
            follows.append("%05x" % address)
            return
        name = callsites.get(address) or stub_name
        if name is None:
            if code[0] == 0x9A:
                off, seg = struct.unpack_from("<HH", code, 1)
                name = "far:%04X:%04X" % (seg, off)
            elif code[0] == 0xE8:
                cs = uc.reg_read(x86.UC_X86_REG_CS)
                tgt = (uc.reg_read(x86.UC_X86_REG_IP) + 3 + struct.unpack_from("<h", code, 1)[0]) & 0xFFFF
                name = "near:%04X:%04X" % (cs, tgt)
            else:
                name = "indirect@%05x" % address
        ss, spv = uc.reg_read(x86.UC_X86_REG_SS), uc.reg_read(x86.UC_X86_REG_SP)
        n = arg_words.get(name, 4)
        stack = list(struct.unpack("<%dH" % n, bytes(uc.mem_read(ss * 16 + spv, 2 * n))))
        rec = {"site": "%05x" % address, "callee": name, "args": ["%04X" % w for w in stack]}
        if code[0] == 0xFF:  # far/near indirect: record the resolved vector
            rec["insn"] = "%s %s" % (ins.mnemonic, ins.op_str)
        script = results.get(name)
        if hooks and name in hooks:
            ax, dx = hooks[name](uc, stack)
            res = {"ax": ax, "dx": dx}
        else:
            res = script.pop(0) if script else spec.get("default_result", {"ax": 0, "dx": 0})
        for w in res.get("writes", []):
            uc.mem_write(ds * 16 + int(w["offset"], 16), bytes.fromhex(w["hex"]))
        uc.reg_write(x86.UC_X86_REG_AX, int(str(res.get("ax", 0)), 0))
        uc.reg_write(x86.UC_X86_REG_DX, int(str(res.get("dx", 0)), 0))
        rec["result"] = {"ax": "%04X" % int(str(res.get("ax", 0)), 0), "dx": "%04X" % int(str(res.get("dx", 0)), 0)}
        calls.append(rec)
        next_ip = (uc.reg_read(x86.UC_X86_REG_IP) + ins.size) & 0xFFFF
        if spec.get("stub_return_words"):
            # what the skipped CALL would have left below SP: the return CS (far calls) and IP
            words = [uc.reg_read(x86.UC_X86_REG_CS), next_ip] if code[0] in (0x9A,) or ins.mnemonic == "lcall" else [next_ip]
            for k, word in enumerate(words):
                uc.mem_write(ss * 16 + ((spv - 2 * (k + 1)) & 0xFFFF), struct.pack("<H", word))
        uc.reg_write(x86.UC_X86_REG_IP, next_ip)
        if code[0] == 0xE8 and bytes(uc.mem_read(address - 1, 1)) == b"\x0e":
            # `push cs; call near` reaches a far function, whose RETF would pop the pushed CS too
            uc.reg_write(x86.UC_X86_REG_SP, (uc.reg_read(x86.UC_X86_REG_SP) + 2) & 0xFFFF)

    def in_stack(linear):
        lo = STACK_SEG * 16 + state["min_sp"] - 0x20
        return lo <= linear < STACK_SEG * 16 + STACK_TOP + 2 * len(args)

    def on_write(uc, access, address, size, value, _):
        if ds * 16 <= address < ds * 16 + 0x10000:  # stack-window writes are dropped after the run
            ds_writes.append({"offset": "%04X" % (address - ds * 16), "size": size,
                              "value": "%0*X" % (size * 2, value & ((1 << (8 * size)) - 1))})

    cpu.hook_add(unicorn.UC_HOOK_CODE, on_code)
    cpu.hook_add(unicorn.UC_HOOK_MEM_WRITE, on_write)
    if cov is not None:
        cpu.hook_add(unicorn.UC_HOOK_BLOCK, on_block)
    entry = spec["entry"]
    seg, off = int(entry["segment"], 16), int(entry["offset"], 16)
    cpu.reg_write(x86.UC_X86_REG_CS, seg)
    cpu.emu_start(seg * 16 + off, 0xFFFFF, count=spec.get("max_instructions", 200000) + 10)
    if cov is not None:
        cov.flush(cpu)
        coverage = cov.result()
        if cov_dir and not spec.get("coverage_defer"):
            with open(os.path.join(cov_dir, "cov-%d.jsonl" % os.getpid()), "a") as f:
                f.write(json.dumps({"unit": spec.get("unit"), "error": state["error"], **coverage}, separators=(",", ":")) + "\n")
    if state["error"]:
        raise SystemExit("oracle stopped: " + state["error"] + "; last calls from " + ", ".join(follows[-12:]) + "; last instructions " + " ".join("%05x" % a for a in recent))
    after = bytes(cpu.mem_read(ds * 16, 0x10000))
    eflags = cpu.reg_read(x86.UC_X86_REG_EFLAGS)
    final = {k.lower(): "%04X" % cpu.reg_read(getattr(x86, "UC_X86_REG_" + k))
             for k in ("AX", "BX", "CX", "DX", "SI", "DI", "BP", "SP", "DS", "ES", "SS")}
    final["flags"] = {k: bool(eflags >> b & 1) for k, b in FLAGS.items()}
    changed = [{"offset": "%04X" % i, "before": "%02X" % before[i], "after": "%02X" % after[i]}
               for i in range(0x10000) if before[i] != after[i] and not in_stack(ds * 16 + i)]
    ds_writes = [w for w in ds_writes if not in_stack(ds * 16 + int(w["offset"], 16))]
    mem_changed = None
    if before_all is not None:
        after_all = bytes(cpu.mem_read(0, 0x100000))
        mem_changed = [{"linear": "%05X" % i, "after": "%02X" % after_all[i]}
                       for i in range(0x100000)
                       if before_all[i] != after_all[i] and not in_stack(i)]
    reads = [bytes(cpu.mem_read(int(r["segment"], 16) * 16 + int(r["offset"], 16), r["length"])).hex()
             for r in spec.get("read", [])]
    return {
        "authority": "test_only_original_byte_execution",
        "reads": reads,
        "unit": spec["unit"],
        "originalExeSha256": EXE_SHA256.upper(),
        "loadedImages": loaded,
        "entry": entry,
        "instructions": state["count"],
        "stack": {"ss": "%04X" % STACK_SEG, "top": "%04X" % STACK_TOP, "min_sp": "%04X" % state["min_sp"]},
        "uniqueAddresses": len(executed),
        "calls": calls,
        "trace": trace,
        "followedCalls": len(follows),
        "dsWrites": ds_writes,
        "dsChanged": changed,
        "memChanged": mem_changed,
        "final": final,
        **({"coverage": coverage} if cov is not None else {}),
    }


def main() -> None:
    exe = Path(sys.argv[1]).read_bytes()
    spec = json.loads(Path(sys.argv[2]).read_text())
    out = run(exe, spec)
    text = json.dumps(out, indent=1)
    if len(sys.argv) > 3:
        Path(sys.argv[3]).write_text(text + "\n")
    else:
        print(text)


if __name__ == "__main__":
    main()
