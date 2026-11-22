# The Workshop's sample translation packs

The source of the samples the Workshop's download holds in `Workshop/examples/sample-languages` (docs/LOCALISATION.md, "The
samples"; the build copies them, `src/Darklands.Workshop/Darklands.Workshop.csproj`).

They are a deliberate, limited exception to the project shipping no translation of the game's text, decided by the project
on 2026-11-22: short excerpts of the opening only, to show the tool.

* `sample-fr`, `sample-es`, `sample-pl`, `sample-ru`, `sample-el`: the city square and the church by day (cards `$CITYS00`
  and `$CITYC00`), the menu bar's and the party information screen's headings.
* `sample-ja`, `sample-ko`: the church's first text, with Noto Sans CJK.
* `sample-fr-layouts`: a few French screen texts with the pack's screen layouts (`interface/layout.json`).

Rules for anything added here:

* only the pack's own words, by key: never the English text, and nothing from the German edition;
* fonts: the Latin, Greek and Cyrillic samples ship their pixel font packs (`fonts/`, built from the original fonts by the
  Workshop's builder, `DarklandsRP --make-font-pack`, in the original format: the user's decision, 2026-11-22); otherwise
  only openly licensed TrueType fonts, with their licence (`fonts/` here: Noto Sans CJK, one copy, copied into each CJK
  sample by the build);
* the money words (`ui.ftl`: `id-rw-money-*`) in every card sample, so its `$Money` amounts read in its language;
* each pack named as a sample in its `pack.json` ("Français (sample)");
* the game's check clean (`SamplePacksTests` and `MoneyWordsAndPackLettersTests` hold the rest).
