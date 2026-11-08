"""Compiles the host's fragment shaders (src/Darklands.Host.Sdl/Shaders/*.frag.hlsl) to the bytecode SDL3's GPU renderer takes.

    python tools/shaders/build_shaders.py [--dxc <dxc.exe>]

DXIL (Direct3D 12, shader model 6.0) with dxc: the one given, else the newest Windows SDK's (its dxil.dll signs the result).
The .dxil files are committed beside their sources and embedded in the program; run this after changing a shader.
SPIR-V (Vulkan: Linux, the Steam Deck) needs a dxc built with SPIR-V code generation (the Windows SDK's has none): with one,
--spirv also writes the .spv files, which the host then uses where Direct3D 12 is missing.
"""
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SHADERS = ROOT / "src" / "Darklands.Host.Sdl" / "Shaders"


def find_dxc() -> Path:
    if "--dxc" in sys.argv:
        return Path(sys.argv[sys.argv.index("--dxc") + 1])
    kits = Path(r"C:\Program Files (x86)\Windows Kits\10\bin")
    found = sorted(kits.glob("10.*/x64/dxc.exe"))
    if not found:
        raise SystemExit("dxc.exe not found: install the Windows SDK or give --dxc <path>")
    return found[-1]


def main():
    dxc = find_dxc()
    for source in sorted(SHADERS.glob("*.frag.hlsl")):
        outputs = [(source.with_suffix(".dxil"), [])]
        if "--spirv" in sys.argv:
            outputs.append((source.with_suffix(".spv"), ["-spirv", "-fspv-preserve-bindings"]))
        for output, extra in outputs:
            cmd = [str(dxc), "-T", "ps_6_0", "-E", "main", "-O3", *extra, str(source), "-Fo", str(output)]
            subprocess.run(cmd, check=True)
            print(output.relative_to(ROOT), output.stat().st_size, "bytes")


if __name__ == "__main__":
    main()
