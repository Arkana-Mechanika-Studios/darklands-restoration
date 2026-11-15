#!/bin/sh
# Builds src/Darklands.Host.Sdl/Native/linux-x64/libSDL3.so.0, which the Linux release ships beside the program, from SDL's official
# source release (run again only to move to another SDL version: change VERSION and SHA256, then THIRD_PARTY_NOTICES.md).
#   docker run --rm -v "$PWD:/work" -w /work ubuntu:22.04 sh tools/linux/build_sdl3.sh src/Darklands.Host.Sdl/Native/linux-x64
# Built on Ubuntu 22.04 (glibc 2.35) so that it loads on Ubuntu 22.04 and later, Debian 12 and later and SteamOS; SDL opens
# its video and sound back ends (X11, Wayland, PipeWire, PulseAudio, ALSA, ...) at run time, so the system needs none of them
# beyond what it uses. The version and the archive's SHA-256 match the Windows SDL3.dll (THIRD_PARTY_NOTICES.md).
set -eu
VERSION=3.4.20
SHA256=f3bb06f55bed9557227a700af2ea62e49497ebdf58037732a731a5fb3ef8581f
OUT=${1:?usage: build_sdl3.sh <out folder>}
mkdir -p "$OUT"
OUT=$(cd "$OUT" && pwd)

export DEBIAN_FRONTEND=noninteractive
apt-get update -qq
apt-get install -y -qq --no-install-recommends ca-certificates curl build-essential cmake ninja-build pkg-config \
  libasound2-dev libpulse-dev libpipewire-0.3-dev libx11-dev libxext-dev libxrandr-dev libxcursor-dev libxfixes-dev \
  libxi-dev libxss-dev libxtst-dev libxkbcommon-dev libwayland-dev wayland-protocols libdecor-0-dev libdrm-dev libgbm-dev \
  libegl1-mesa-dev libgl1-mesa-dev libgles2-mesa-dev libdbus-1-dev libudev-dev libibus-1.0-dev >/dev/null

WORK=$(mktemp -d)
cd "$WORK"
curl -fsSL -o sdl.tar.gz "https://github.com/libsdl-org/SDL/releases/download/release-$VERSION/SDL3-$VERSION.tar.gz"
echo "$SHA256  sdl.tar.gz" | sha256sum -c -
tar -xzf sdl.tar.gz
cmake -S "SDL3-$VERSION" -B build -G Ninja -DCMAKE_BUILD_TYPE=Release -DSDL_SHARED=ON -DSDL_STATIC=OFF -DSDL_TESTS=OFF \
  -DSDL_EXAMPLES=OFF -DSDL_INSTALL=OFF -DSDL_RPATH=OFF
cmake --build build
# the soname file itself, not the symlink chain; stripped of its symbols for debugging only
cp -L build/libSDL3.so.0 "$OUT/libSDL3.so.0"
strip --strip-unneeded "$OUT/libSDL3.so.0"
sha256sum "$OUT/libSDL3.so.0"
# the newest glibc symbol it needs (the oldest system it loads on)
objdump -T "$OUT/libSDL3.so.0" | grep -o 'GLIBC_[0-9.]*' | sort -t. -k2,2n -k3,3n -u | tail -1
