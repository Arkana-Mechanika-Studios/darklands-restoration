# The Workshop's sample translation packs

The source of the samples the Workshop's download holds in `Workshop/examples/languages` (docs/LOCALISATION.md, "The
samples"; the build copies them, `src/Darklands.Workshop/Darklands.Workshop.csproj`).

They are a deliberate, limited exception to the project shipping no translation of the game's text, decided by the project
on 2026-11-22: short excerpts of the opening only, to show the tool.

* `sample-fr`, `sample-es`, `sample-pl`, `sample-ru`, `sample-el`: the city square and the church by day (cards `$CITYS00`
  and `$CITYC00`), the menu bar's and the party information screen's headings.
* `sample-ja`, `sample-ko`: the church's first text, with Noto Sans CJK.
* `sample-fr-layouts`: a few French screen texts with the pack's screen layouts (`interface/layout.json`).

Rules for anything added here:

* only the pack's own words, by key: never the English text, and nothing from the German edition;
* no font built from the game's fonts (the Workshop builds it for the player when the sample is opened); only openly licensed
  TrueType fonts, with their licence (`fonts/`: Noto Sans CJK, one copy, copied into each CJK sample by the build);
* each pack named as a sample in its `pack.json` ("Français (sample)");
* the game's check clean once the font is built (`SamplePacksTests` holds the rest).
