"""Compiles the host's fragment shaders (src/Darklands.Host.Sdl/Shaders/*.frag.hlsl) to the bytecode SDL3's GPU renderer takes.

    python tools/shaders/build_shaders.py [--dxc <dxc.exe>]

Writes, beside each source, the .dxil (Direct3D 12, shader model 6.0) and the .spv (SPIR-V for Vulkan: Linux, the Steam Deck).
Both are committed and embedded in the program; run this after changing a shader.

The compiler: Microsoft's DirectXShaderCompiler (dxc) from its GitHub releases, which writes both (it signs DXIL with the
dxil.dll beside it). Where it is: --dxc, else the environment variable DARKLANDS_DXC, else the key "dxc" of local/paths.json
(relative to the project root; the file of the checkout, or DARKLANDS_ROOT/code/local/paths.json). Without one, the newest
Windows SDK's dxc writes the .dxil only (it has no SPIR-V code generation) and the .spv files are left as they are.
dxc is a build tool (University of Illinois/NCSA licence); nothing of it ships with the program.
"""
import json
import os
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SHADERS = ROOT / "src" / "Darklands.Host.Sdl" / "Shaders"

DXIL = ["-T", "ps_6_0", "-E", "main", "-O3", "-Wno-ignored-attributes"]   # the [[vk::...]] attributes are SPIR-V's
SPIRV = ["-T", "ps_6_0", "-E", "main", "-O3", "-spirv", "-fspv-target-env=vulkan1.0"]


def configured_dxc():
    """The dxc named by --dxc, DARKLANDS_DXC or local/paths.json, or None."""
    if "--dxc" in sys.argv:
        return Path(sys.argv[sys.argv.index("--dxc") + 1])
    if os.environ.get("DARKLANDS_DXC"):
        return Path(os.environ["DARKLANDS_DXC"])
    candidates = [ROOT / "local" / "paths.json"]
    if os.environ.get("DARKLANDS_ROOT"):
        candidates.append(Path(os.environ["DARKLANDS_ROOT"]) / "code" / "local" / "paths.json")
    for paths in candidates:
        if paths.is_file():
            value = json.loads(paths.read_text(encoding="utf-8")).get("dxc")
            if value:
                base = Path(os.environ.get("DARKLANDS_ROOT", ROOT.parent))
                return Path(value) if Path(value).is_absolute() else base / value
    return None


def sdk_dxc():
    found = sorted(Path(r"C:\Program Files (x86)\Windows Kits\10\bin").glob("10.*/x64/dxc.exe"))
    return found[-1] if found else None


def compile_to(dxc, flags, source, output):
    subprocess.run([str(dxc), *flags, str(source), "-Fo", str(output)], check=True)
    print(output.relative_to(ROOT), output.stat().st_size, "bytes")


def main():
    dxc = configured_dxc()
    if dxc is not None and not dxc.is_file():
        raise SystemExit(f"dxc not found: {dxc}")
    spirv = dxc is not None
    if dxc is None:
        dxc = sdk_dxc()
        if dxc is None:
            raise SystemExit("no dxc: give --dxc <path>, DARKLANDS_DXC, or \"dxc\" in local/paths.json")
        print("no DirectXShaderCompiler release named: the Windows SDK's dxc, DXIL only (the .spv files are kept)")
    for source in sorted(SHADERS.glob("*.frag.hlsl")):
        compile_to(dxc, DXIL, source, source.with_suffix(".dxil"))
        if spirv:
            compile_to(dxc, SPIRV, source, source.with_suffix(".spv"))


if __name__ == "__main__":
    main()
