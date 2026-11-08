# The launcher's art

The launcher (src/Darklands.Launcher) is laid out on a 1280 x 720 design, scaled to the window (960 x 540 at the
smallest, any size larger, fullscreen included). Each picture below is optional: without it the launcher draws a
stand-in in code. Art is made at **twice the design's size**.

## Putting art in

1. Single pictures: save each as `<name>.png` (or `.webp`, `.jpg`) under exactly the name below, in a folder of your own.
   Sliced pictures come with `<name>.slice.txt`: "left top right bottom", the borders in the picture's pixels (corners
   and plate ends keep their shape, the rest stretches; top and bottom 0 for a plate).
2. Sheets with several pieces and gaps between them (as the artist delivered them on 2026-11-08): convert them to RGBA
   PNG in `<folder>/rgba/` and run `dotnet run --project tools/launcher-art/SheetCutter -- <folder>`. It finds each piece
   by its alpha, keys out a checkerboard painted in place of transparency, assembles the 9-slices on one grid (a piece
   short of a shared cut line is extended to it), writes the pieces and their `.slice.txt` to `<folder>/out` and its
   checks to `<folder>/report.txt` (halos, seams, sizes). Its sheet names are those of the 2026-11-08 delivery
   (`card-parchment-9slice`, `settings-panel-9slice`, `fleuron-rule-and-dropcap`, `section-icons`, `plate-dark-3states`,
   `plate-red-play`, `plate-parchment`, `corner-ornament`, `background-v2`).
3. Run `powershell -ExecutionPolicy Bypass -File tools\launcher-art\Convert-LauncherArt.ps1 -Source <folder>` (or
   `<folder>\out`): it writes `src/Darklands.Launcher/Resources/art/<name>.png` (8-bit, no larger than its limit; a
   `.slice.txt` is copied and scaled with it).
4. Build: the art is embedded and replaces the stand-in. Delete a file from `Resources/art` to go back to the stand-in.

## The pictures

| name | on the design | how it is drawn |
|---|---|---|
| `background` | the whole window | 16:9, no transparency; at least 1920 x 1080, ideally 2560 x 1440. Enlarged until it covers the window, keeping its **focus** (the castle: 30.5% across, 34% down the 2026-11-08 painting) at (660, 318), in the gap between the panels; darkened at the edges and behind the title in code. A new painting with its focus elsewhere: change `FocusX`/`FocusY` in LauncherScreen.cs. |
| `card-left` + slice | 414 x 508 | the left card, in nine slices; its corners at half their pixels (a 120-pixel corner is 60 design pixels). The one in place is the artist's whole-card picture of 2026-11-08 (on transparency), cut out and reduced by `PaperTool cutout`. |
| `card-settings` + slice | 494 x 692 | the settings panel, in nine slices. As in the mock-up, "Settings" is written in ink on the paper. The one in place is the artist's whole-panel picture of 2026-11-08, with its own gold frame and star corners (so `corner` is not drawn over it), cut out by `PaperTool cutout`, its printed rule painted out (the launcher draws the rule beside "Settings"). Art WITH a dark band at least 40 pixels tall (found down the top-left corner's middle) still gets "Settings" in cream on it. |
| `corner` | 60 x 60 | (not in place: the settings art has its corners) the TOP-LEFT bronze corner, drawn only without `card-settings`; mirrored to the panel's four corners, reaching 9 design pixels past them. |
| `fleuron` | 20-26 high | the fleur-de-lis at the middle of the rules. |
| `rule-left`, `rule-right` | 5-7 high | the rule's two halves; the knob end (three times the rule's height) keeps its shape, the taper stretches. |
| `dropcap` | 46 x 46 | the illuminated capitals' frame, WITHOUT the letter (drawn on it in cream). |
| `plate-stone` + slice | 168 x 44 | "Save and quit" (the parchment plate), three slices, no text. |
| `plate-red` + slice | 168 x 44 | "Play", three slices, no text (the word and its gold diamonds are drawn). |
| `plate-small` + slice, `plate-small-hover` + slice, `plate-small-pressed` + slice | about 78 x 22 | "Browse…", "Change…", "Test sound"; the hover one when pointed at or focused, the pressed one while the mouse button is held on it (a plate acts when let go over it). |
| `hint-bar` (+ slice) | 422 x 40-120 | the dark bar above the buttons; drawn in code until given. |
| `icon-game`, `icon-display`, `icon-sound`, `icon-startup` | 22 x 22 | the four sections' icons. |
| `icon-link` | 16 x 16 | before each link on the left card. |
| `icon-problem`, `icon-found` | 12 x 12 | ⓘ before a problem line and in the hint bar, ✓ before "Found"; drawn in code until given. |

## Fonts

TrueType files (static instances, not variable fonts) in `src/Darklands.Launcher/Resources/`, embedded by the build;
all under the SIL Open Font License (`Resources/OFL.txt`; list each in THIRD_PARTY_NOTICES.md when added):

| file | used for | without it |
|---|---|---|
| `EBGaramond12-Regular.ttf` (in place) | all text | — |
| `EBGaramond-Italic.ttf` (in place) | captions, status and problem lines, the hint bar | the Regular |
| `EBGaramond-SemiBold.ttf` (in place) | section headings, the cards' headings, the drop caps, "Restoration Project", "Save and quit" | the Regular, thickened by a pixel |
| `Texturina-Bold.ttf` (in place) | "Darklands", "Settings", "Play" (the closest free face to the mock-up's lettering) | EB Garamond SemiBold |

The static fonts come from the families' upstream repositories, which Google Fonts publishes
(github.com/octaviopardo/EBGaramond12 and github.com/Omnibus-Type/Texturina, `fonts/ttf/`; Google Fonts' own
repository holds only their variable fonts, whose weights the launcher's rasteriser cannot choose).
