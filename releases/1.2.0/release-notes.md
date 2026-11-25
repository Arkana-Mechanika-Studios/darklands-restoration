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

## Translations

- **Texts a translation pack never reached.** An audit of every text the game draws found screens that showed the
  English with a pack loaded: the formulae's short names in the alchemy screen's batch list, the character generator's
  families, occupations, formulae and saints, the town size and standing on the party information screen, the camp's
  details, the saved games' places and months on the load screen, the battle's potions, its "(Ldr)" and "(All)" and its
  members' messages, the store's message when it is full, the shop names and guild words the cards put in, the dragons',
  dwarfs' and titles' names, and the map's messages. They are now the pack's words (new keys `map:<n>.text`,
  `id:rw.store.no-room`, and the EXE's word tables). A pack's own `var-he`, `var-his` … now answer the game's pronouns.
- **Letters lost in some texts.** A pack's text in a card's hint and tell line, the battle's place notes and member
  messages, and the store's pages went through the game's 8-bit text, so a Chinese or Cyrillic letter was drawn as `?`.
  They now draw the pack's letters.
- Without a pack every screen is the original's, as before.
