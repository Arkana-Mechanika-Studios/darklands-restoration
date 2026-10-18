# Third-party notices

## VGA 8×16 text font

`src/Darklands.Platform/Resources/vga-8x16.b64` is the 8×16 text-mode (INT 10h) font as supplied by
DOSBox-X (https://dosbox-x.com, GNU General Public License v2 or later), a recreation of the IBM VGA
BIOS font. It is used to show the original game's start-up banner in text mode, as the original
does on a VGA card. It is stored base64-encoded (4,096 bytes of 8×16 glyphs).

## SDL 2

`src/Darklands.Host.Sdl/Native/win-x64/SDL2.dll` is SDL 2.32.10, unmodified, from the official Windows
x64 release (`SDL2-2.32.10-win32-x64.zip`, https://github.com/libsdl-org/SDL/releases, SHA-256 of the
archive 6cf9706eefd0a4a06dc764007934d428afaf029fabdd408a9e646048c91e18fb). The SDL host's build and publish
output carry it next to the program. Source code: https://www.libsdl.org/. Its licence:

```text
Simple DirectMedia Layer
Copyright (C) 1997-2025 Sam Lantinga <slouken@libsdl.org>

This software is provided 'as-is', without any express or implied
warranty.  In no event will the authors be held liable for any damages
arising from the use of this software.

Permission is granted to anyone to use this software for any purpose,
including commercial applications, and to alter it and redistribute it
freely, subject to the following restrictions:

1. The origin of this software must not be misrepresented; you must not
   claim that you wrote the original software. If you use this software
   in a product, an acknowledgment in the product documentation would be
   appreciated but is not required.
2. Altered source versions must be plainly marked as such, and must not be
   misrepresented as being the original software.
3. This notice may not be removed or altered from any source distribution.
```

## mt32emu

MT-32/CM-32L emulation uses the Munt mt32emu library through its NuGet package (GNU Lesser General
Public License v2.1 or later); no mt32emu source code is included in this repository. MT-32 control
and PCM ROMs are not included; the player supplies them.

## DejaVu Sans Mono (developer console)

`src/Darklands.Host.Sdl/Resources/DejaVuSansMono.ttf` is DejaVu Sans Mono 2.37, unmodified
(https://dejavu-fonts.github.io). The SDL host's developer console uses it as its default font.
Its licence, as embedded in the font file:

```text
Fonts are (c) Bitstream (see below). DejaVu changes are in public domain.

Bitstream Vera Fonts Copyright
------------------------------

Copyright (c) 2003 by Bitstream, Inc. All Rights Reserved. Bitstream Vera is a trademark of Bitstream, Inc.

Permission is hereby granted, free of charge, to any person obtaining a copy of the fonts accompanying this license ("Fonts") and associated documentation files (the "Font Software"), to reproduce and distribute the Font Software, including without limitation the rights to use, copy, merge, publish, distribute, and/or sell copies of the Font Software, and to permit persons to whom the Font Software is furnished to do so, subject to the following conditions:

The above copyright and trademark notices and this permission notice shall be included in all copies of one or more of the Font Software typefaces.

The Font Software may be modified, altered, or added to, and in particular the designs of glyphs or characters in the Fonts may be modified and additional glyphs or  or characters may be added to the Fonts, only if the fonts are renamed to names not containing either the words "Bitstream" or the word "Vera".

This License becomes null and void to the extent applicable to Fonts or Font Software that has been modified and is distributed under the "Bitstream Vera" names.

The Font Software may be sold as part of a larger software package but no copy of one or more of the Font Software typefaces may be sold by itself.

THE FONT SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO ANY WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT OF COPYRIGHT, PATENT, TRADEMARK, OR OTHER RIGHT. IN NO EVENT SHALL BITSTREAM OR THE GNOME FOUNDATION BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, INCLUDING ANY GENERAL, SPECIAL, INDIRECT, INCIDENTAL, OR CONSEQUENTIAL DAMAGES, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF THE USE OR INABILITY TO USE THE FONT SOFTWARE OR FROM OTHER DEALINGS IN THE FONT SOFTWARE.

Except as contained in this notice, the names of Gnome, the Gnome Foundation, and Bitstream Inc., shall not be used in advertising or otherwise to promote the sale, use or other dealings in this Font Software without prior written authorization from the Gnome Foundation or Bitstream Inc., respectively. For further information, contact: fonts at gnome dot org.
```

## StbTrueTypeSharp

The developer console rasterises its font with StbTrueTypeSharp (https://github.com/rds1983/StbSharp,
NuGet package), a C# port of Sean Barrett's stb_truetype, which is in the public domain (or under the
MIT licence, at the user's choice). No source of it is included in this repository.

## Original game data

The game's data files (DARKLAND.EXE and its data, MicroProse 1992) are not included; the player
supplies a copy of the original game.
