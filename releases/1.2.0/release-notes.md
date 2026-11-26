<!-- DRAFT for 1.2.0: gathered while 1.2 is made; finished before the release (docs/RC-CHECKLIST.md, A0), and this line removed. -->
Darklands Restoration 1.2.0

## Faithfulness fixes since 1.1

Places where our game differed from the original program. They are fixed in every rule set, Original included: the
Original rules are the faithful game, checked against the original in every release.

- **The citadel's courtyard after one of its trials.** Coming back to the courtyard from one of the citadel's rooms, the
  party now starts at that room's door with the courtyard empty, and the rooms already finished have their doors marked,
  as in the original. Our game built the courtyard fresh every time and made the party fight its guards again: the court
  builder never got the last castle's flags, as the original's does. Wrong in every release since 1.0.0. Proven against
  the original by two recorded sessions from a player's saved games at the courtyard (`court-return`, `court-gate`).
- **Groningen after a game loaded in the wilderness** (already in 1.1.5). A game saved on the world map and loaded again,
  then taken straight to Groningen, showed the city wrong: a stray `Ö` before its name, no Stadtplatz and no Markt on the
  main street, the inn's name missing, a city lord's title left as `CityLordTitle`, and the city's own numbers (the toll
  at the gate, for one) read from the wrong place. The game now reads Groningen's own record on arrival, as the original
  does. Wrong since 0.9.0 (GitHub issue 7). Proven against the original by a recorded session (`wild-load-groningen`).

## Translations

- The untranslated-text audit came out in 1.1.4 (texts a pack never reached, letters lost in some texts, the pronouns):
  see its notes. In 1.2 a pack also names the rules in the notice a saved game with other rules gives.
- Without a pack every screen is the original's, as before.
