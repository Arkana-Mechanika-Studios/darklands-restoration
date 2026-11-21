# Third-party notices

## VGA 8×16 text font

`src/Darklands.Platform/Resources/vga-8x16.b64` is the 8×16 text-mode (INT 10h) font as supplied by
DOSBox-X (https://dosbox-x.com, GNU General Public License v2 or later), a recreation of the IBM VGA
BIOS font. It is used to show the original game's start-up banner in text mode, as the original
does on a VGA card. It is stored base64-encoded (4,096 bytes of 8×16 glyphs).

## SDL 3

`src/Darklands.Host.Sdl/Native/win-x64/SDL3.dll` is SDL 3.4.20, unmodified, from the official Windows
x64 release (`SDL3-3.4.20-win32-x64.zip`, https://github.com/libsdl-org/SDL/releases, SHA-256 of the
archive 368bed8b24e44ad9ea372990814f2343fcfb9e468abb7f8f4a3bc146c7304752, of the DLL
66844f10251cc1c10619ed5d8e4e3224f564900ee6d69ba87d63de782addae40). The SDL host's build and publish
output carry it next to the program. `src/Darklands.Host.Sdl/Native/linux-x64/libSDL3.so.0` is the same
SDL 3.4.20, unmodified, built on Ubuntu 22.04 from the official source release (`SDL3-3.4.20.tar.gz`, SHA-256
of the archive f3bb06f55bed9557227a700af2ea62e49497ebdf58037732a731a5fb3ef8581f, of the library 8a69ef868a47f29232faf0cecdac61b407700c205440434b38e4eb8cb1e55c4d) by
`tools/linux/build_sdl3.sh`; the Linux publish carries it next to the program. Source code: https://www.libsdl.org/.
Its licence:

```text
Simple DirectMedia Layer
Copyright (C) 1997-2026 Sam Lantinga <slouken@libsdl.org>

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

## NVorbis

Sound packs (docs/PACKS.md) decode Ogg Vorbis files with NVorbis (https://github.com/NVorbis/NVorbis), through its
NuGet package, under the MIT License; no NVorbis source code is included in this repository.

```text
Copyright (c) 2020 Andrew Ward

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated
documentation files (the "Software"), to deal in the Software without restriction, including without limitation the
rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit
persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the
Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
```

## MeltySynth

Sound packs (docs/PACKS.md) play General MIDI (.mid) files through a SoundFont with MeltySynth
(https://github.com/sinshu/meltysynth), a synthesizer written entirely in C#, through its NuGet package (2.4.1), under the
MIT License; no MeltySynth source code is included in this repository. No SoundFont is included either: the player
supplies one. The package's licence file also names the work MeltySynth is based on.

```text
C# Synth (http://csharpsynthproject.codeplex.com):
Copyright (C) 2014 Alex Veltsistas

TinySoundFont (https://github.com/schellingb/TinySoundFont):
Copyright (C) 2017, 2018 Bernhard Schelling
Based on SFZero, Copyright (C) 2012 Steve Folta (https://github.com/stevefolta/SFZero)

MeltySynth:
Copyright (C) 2021 Nobuaki Tanaka

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated
documentation files (the "Software"), to deal in the Software without restriction, including without limitation the
rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit
persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the
Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
```

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

## EB Garamond, Grenze and Grenze Gotisch (launcher, Workshop)

The launcher draws its text with these fonts (and the Workshop's start page its wordmark with the two Grenze ones), unmodified, each licensed under the SIL Open Font License, Version 1.1
(https://openfontlicense.org): they may be used, embedded and redistributed with any software, and may not be sold by
themselves.

* `src/Darklands.Launcher/Resources/EBGaramond12-Regular.ttf`: EB Garamond 12 by Georg Duffner
  (https://github.com/georgd/EB-Garamond). Copyright 2010-2013 Georg A. Duffner, 2013 Siva Kalyan.
* `src/Darklands.Launcher/Resources/EBGaramond-Italic.ttf`, `EBGaramond-SemiBold.ttf`: EB Garamond, the static fonts of
  the family's upstream (https://github.com/octaviopardo/EBGaramond12, `fonts/ttf/`), which Google Fonts publishes.
  Copyright 2017 The EB Garamond Project Authors; 2025 Deborah Khodanovich (RCS citation glyphs).
* `src/Darklands.Launcher/Resources/Grenze-Black.ttf`: Grenze, the static font of the family's upstream
  (https://github.com/Omnibus-Type/Grenze, `fonts/ttf/`), which Google Fonts publishes. Copyright 2019, The Grenze Project
  Authors.
* `src/Darklands.Launcher/Resources/GrenzeGotisch-Black.ttf`: Grenze Gotisch, the static font of its upstream
  (https://github.com/Omnibus-Type/Grenze-Gotisch, `fonts/ttf/`); the launcher takes only its "a" from it. Copyright 2020
  The Grenze Gotisch Project Authors.

The full licence text, with these copyright lines, is in `src/Darklands.Launcher/Resources/OFL.txt` and is copied next to the
launcher in the build and publish output.

EB Garamond 12 (`EBGaramond12-Regular.ttf`) is also the card window's font of the "HD fonts" font pack the program ships
(`packs/hd-fonts/fonts/`, unmodified, with `OFL.txt` beside it). DejaVu Sans Mono (above) is its screens' font there, unmodified.

## The launcher's art

The launcher's pictures (`src/Darklands.Launcher/Resources/art/`: the background painting, the parchment panels, the
plates, the ornaments and the icons) are the project's own artwork, made by the project (Arkana Mechanika Studios) with AI
image generation. They are distributed with the project under the project's own licence (`LICENSE`).

## StbTrueTypeSharp

The developer console, the launcher and the game's translated text (a translation pack's TrueType / OpenType fonts) rasterise
their fonts with StbTrueTypeSharp (https://github.com/rds1983/StbSharp,
NuGet package), a C# port of Sean Barrett's stb_truetype, which is in the public domain (or under the
MIT licence, at the user's choice). No source of it is included in this repository.

## Linguini

Translations are Project Fluent (`.ftl`) files, read with Linguini (https://github.com/Ygg01/Linguini, NuGet packages
`Linguini.Bundle`, `Linguini.Syntax` and `Linguini.Shared`), used under the MIT License (the packages are dual-licensed
MIT OR Apache-2.0); no Linguini source code is included in this repository. Only the `Darklands.Localization` project
refers to it.

```text
Copyright 2021 Daniel Fath

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated
documentation files (the "Software"), to deal in the Software without restriction, including without limitation the
rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit
persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the
Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
```

## Avalonia and AvaloniaEdit (the Workshop)

The Darklands Workshop (a download of its own, not the game) is built on Avalonia 11.3 (`Avalonia`, `Avalonia.Desktop`,
`Avalonia.Themes.Fluent`) and its translation box on AvaloniaEdit 11.3 (`Avalonia.AvaloniaEdit`, a port of AvalonEdit),
both under the MIT License: Copyright © The AvaloniaUI Project; AvalonEdit Copyright © AlphaSierraPapa for the SharpDevelop
Team. https://github.com/AvaloniaUI/Avalonia, https://github.com/AvaloniaUI/AvaloniaEdit

> Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated
> documentation files (the "Software"), to deal in the Software without restriction, including without limitation the
> rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit
> persons to whom the Software is furnished to do so, subject to the following conditions:
>
> The above copyright notice and this permission notice shall be included in all copies or substantial portions of the
> Software.
>
> THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
> WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
> COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
> OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

## Inter (the Workshop's text)

The Workshop draws its words with Inter 4.1 by Rasmus Andersson (https://rsms.me/inter, https://github.com/rsms/inter),
unmodified: the static fonts `src/Darklands.Workshop/Assets/Fonts/Inter-Regular.ttf`, `Inter-Medium.ttf`,
`Inter-SemiBold.ttf` and `Inter-Bold.ttf` of the release (`extras/ttf/`). Copyright (c) 2016 The Inter Project Authors.
Licensed under the SIL Open Font License, Version 1.1 (https://openfontlicense.org): they may be used, embedded and
redistributed with any software, and may not be sold by themselves. The full licence text is in
`src/Darklands.Workshop/Assets/Fonts/Inter-OFL.txt` and is copied next to the Workshop (`Inter-OFL.txt`) in the build and
publish output.

## Lucide (the Workshop's icons)

The Workshop's line icons (`src/Darklands.Workshop/WorkshopIcons.cs`) are drawn after the Lucide icon set
(https://lucide.dev), under the ISC License: Copyright (c) for portions of Lucide are held by Cole Bemis 2013-2022 as part
of Feather (MIT); all other copyright (c) for Lucide are held by Lucide Contributors 2022.

> Permission to use, copy, modify, and/or distribute this software for any purpose with or without fee is hereby granted,
> provided that the above copyright notice and this permission notice appear in all copies.
>
> THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED
> WARRANTIES OF MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY SPECIAL, DIRECT, INDIRECT, OR
> CONSEQUENTIAL DAMAGES OR ANY DAMAGES WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN ACTION OF
> CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.

## Original game data

The game's data files (DARKLAND.EXE and its data, MicroProse 1992) are not included; the player
supplies a copy of the original game.
