# Making a picture pack

For artists. A picture pack ("HD pack") replaces the game's pictures and sprites with PNG files of your own. It changes only
what you see: the game decides, rolls and times everything exactly as before. With no pack the game looks as it always did.

This guide is the friendly path; the exact rules are in [PACKS.md](../PACKS.md#picture-packs), and the first-pack
walkthrough in [README.md](README.md#a-first-pack-in-ten-minutes) shows the whole loop in ten minutes.

## What you can replace

Anything the game draws from a picture file, as a whole picture or as one image of a bank:

* the **start screen**, the card pictures (cities, inns, gates, roads, encounters), and other full-screen pictures;
* **sprites and icons** from the image banks (small icons and the like);
* the **battle**: the animation frames of fighters and the sprites of the battlefield;
* the **drop capitals**, the big first letter of a card's text.

What you replace is always an asset, a picture the game loads as one piece. The game draws the screen at 320x200; a
replacement is stretched over the rectangle its asset covers.

## What you cannot replace (yet)

* **Fonts and text.** The game's text is drawn by the game, in its own letters, over your pictures. Only the drop capitals
  (`dropcap_A.png` to `dropcap_Z.png`) are pictures.
* **Menus, boxes and the pointer** stay as they are, drawn over whatever you replaced. That is deliberate: a replaced
  picture shows only where the original picture is still visible.
* **Colour cycling** (palette animation) does not carry over to a replacement.
* **Recoloured figures and markers**: sprites the game draws in other colours (for example coloured markers) keep the
  colours of your PNG, and are not recoloured.
* **Changing size or layout.** Your picture is fitted to the original's rectangle. You cannot make a picture bigger on
  screen, move it, or add a new one.

## How a file is named

A replacement is found by the asset's name plus `.png`. Folders are written with `/` (or `\`), a `:` in a name becomes `_`,
and capital letters do not matter. These are the forms of name:

| Asset | Example file |
|---|---|
| a whole picture file | `PICS/STARTSCR.PIC.png` |
| one image of a bank (index after `#`) | `COMMONSP.IMG#12.png` |
| a battle animation frame (`#frame.facing`, inside an archive) | `F01C.CAT/F01WKSW.IMC#3.5.png` |
| a battlefield sprite (`@` and the offset in hex) | `BC/CAVE.FLC@1A40.png` |
| a drop capital | `dropcap_D.png` |

Card pictures are files called `PICS/<name>.PIC`, so the picture `aproch1` (a city gate) is `PICS/APROCH1.PIC.png`. The
names of the card pictures are listed, by number, in `src/Darklands.Cards/PictureNames.cs`.

Put the files anywhere inside your pack folder; only the file name counts. The folder form above (`PICS/...`) is a
convenient way to keep order, not a requirement.

### Finding the name of a picture you see

This is the one step that needs a tool you build yourself. The headless host (`Darklands.Host.Headless`, in the source
tree; the release does not include it) takes a screenshot of a scripted run and writes a list of the assets on the screen. Any pack
will do, even an empty folder:

```text
Darklands.Host.Headless <game folder> <output folder> hdpack:<pack folder> wait:200 shot:start
```

It writes `start.png` (the plain frame), `start.hd.png` (the frame with your replacements, `hdscale:<n>` times 320x200,
default 4) and `start.assets.txt`. Each line of that last file is one asset on the screen:

```text
PICS/STARTSCR.PIC	320x200	64000 px	replaced
```

The columns are the asset's name (use it for your file), its original size, how many pixels of it show on this screen
and, once your pack has it, `replaced`. If you cannot build the tool, use the example names above and the names of the
card pictures.

## Sizes and scale factors

A picture is stretched over its asset's rectangle, so any size works. A whole multiple of the original size (2x, 4x,
8x) keeps its proportions and stays sharp:

| Original asset (example sizes) | At 2x | At 4x |
|---|---|---|
| 320x200 (a full screen) | 640x400 | 1280x800 |
| 64x48 | 128x96 | 256x192 |

The game window is by default 720x400 at scale 1 (`display.scale` multiplies it, `display.resolution` sets it
directly; [SETTINGS.md](../SETTINGS.md)), so the game is shown at about twice the original size, more in a larger window.
A 4x picture is a good target: it looks right in a large window and is not wasteful. A picture may have up to about 67
million pixels.

## Colours, palette and transparency

* **Format.** Ordinary PNG: grey, colour, palette, with or without alpha (or a `tRNS` chunk), 8 bit (16 bit is read at 8).
  **Interlaced PNG is not supported**; save without interlacing.
* **Transparency.** Where your picture is transparent, the frame behind shows through. For sprites you do not need to
  draw the outline: the game already knows which pixels belong to the sprite, and the replacement shows only there.
  Soft or anti-aliased edges are cut to that shape, so keep the sprite's important parts inside its original silhouette.
* **Fades.** A replaced picture follows the palette of the original: when the game fades a picture out, greys the options
  or lightens it, your picture fades, greys or lightens with it. You do not draw those states.
* **Use true colour.** You are not limited to the original's 256 colours.

## The folder

A minimal pack is just a folder of PNG files named after assets:

```text
my-first-pack/
  PICS/
    STARTSCR.PIC.png
    APROCH1.PIC.png
  COMMONSP.IMG#12.png
  dropcap_D.png
```

### `pack.json` (optional)

Use it only if you want file names of your own. Its entries win over the file names. Comments and trailing commas are allowed,
and paths are relative to the pack folder:

```json
{
  "assets": {
    "PICS\\STARTSCR.PIC": "title.png",
    "COMMONSP.IMG#12": "icons/sword.png"
  }
}
```

The only key for pictures is `assets`: an object of asset name to file. In JSON, write a `\` in a name as `\\`. A pack's
`pack.json` may also hold the sound pack's `soundfont` and `card:` keys ([AUDIO.md](AUDIO.md)); the picture loader leaves
those alone, and any other unknown key is reported and ignored.

## Step by step: a new title screen

1. **Make the folder.** Inside `packs` (next to the program) create `title-pack`.
2. **Find the size.** Run the headless host as shown above and read `start.assets.txt` (or assume the original is
   the full 320x200 frame, and check the first time you try it).
3. **Paint.** Work at 1280x800, four times the frame. Keep the title's name, buttons and the lettering of the
   menu entries in their original places: the menu text is drawn by the game over your picture, so a picture
   with a different layout would put the text on the wrong spot.
4. **Save** as `title-pack/PICS/STARTSCR.PIC.png` (non-interlaced).
5. **Choose it.** In the launcher's **HD pack** row choose `title-pack`, or put
   `"display": { "hdPack": "packs/title-pack" }` in `darklands.settings.json`, or start with
   `--hd-pack packs/title-pack`.
6. **Start the game.** The title screen is yours. The menu entries are still the game's. Check the console for problems
   (a broken PNG is reported, and that picture keeps its original pixels).

## Tips

* **Keep the original framing.** A picture is stretched over the original's rectangle, so keep the original's
  composition, field of view and proportions. A picture of other proportions is squashed to fit.
* **Mind the overlays.** The game draws text, option lines, boxes, the party's panels and the pointer over your picture.
  Keep the places where text appears calm (low contrast, no fine detail), or it becomes hard to read. Compare with a
  screenshot of the original screen (F12) to see where the text lands.
* **Align on whole pixels.** Draw on a grid of the scale factor (4 original pixels = 16 pixels at 4x), so edges of the
  original that line up with the screen still do.
* **Sprites at the same anchor.** A battle sprite is placed by its original rectangle; keep your figure in the same
  position inside the picture, with the same feet line, and animation frames will not jitter.
* **One facing, one frame.** Battle frames are named `frame.facing`; replace all of an animation's frames, or the
  unreplaced ones will show the old art between yours.
* **Work in layers.** Keep the originals' proportions in a template over your file (made from your own screenshot) and hide it on export.
* **Test small first.** Replace one picture and look at it before painting fifty.

## How to check in the game

1. Choose the pack and start the game (the launcher's help line for the HD pack row says how many pictures it replaces).
2. Go to the screen: Quickstart or Create a New World from the start screen, then walk to a city or a map.
3. Press F12 to save a screenshot of the window to compare with one taken without the pack.
4. Look at the fades: leave and enter a card, or open a menu, and check your picture fades with the game's.
5. Read the console messages for a file that could not be read.

## Rights

Paint your own pictures. Do not trace, scan or re-export the game's own art into a pack you share: it is not free.
A pack made of your own work, or of work whose licence allows it, may be shared with its licence stated. Pictures are only ever
loaded and drawn; a pack cannot run anything.
