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
- **The message box on the world map** (a place noticed on the way, such as a robber knight's territory). Its lines are
  now one pixel further apart, as in the original; the pointer is hidden while it waits; and the key that closes it stops
  a walk to a clicked point, as in the original, where our game walked on. Wrong since 0.9.0. Found by the new sessions
  below (`lta-robber-cairn`). Before and after pictures: `docs/FIXED.md`, "The message box on the world map".

New recorded sessions, after GitHub issue 7: a game saved in the wilderness next to a count's castle, a robber knight's
tower, a monastery, a village, a cave, a mine and a ruin is loaded and the place entered, saved there and loaded again,
and then the place's choices taken; and games the original itself saved at those places are loaded from the start
screen. All 14 agree with the original and are now part of every release's check.

## Battles

- Shift with an arrow now scrolls the battle view at the same steady speed as Shift with the pointer at an edge, ten steps
  a second at most (GitHub issue 12). It followed the keys' repeat: one step, a pause, then three times faster. In the
  original both go at the speed of its drawing, which is why they matched there.

## Fixed rules: potions, saints and the banks

In the Fixed rules (the default for new games; Restored too), what the potions and the saints give now lasts about a day,
as the game's own texts say, and does what they describe (in Original nothing changes). Each with its own entry in
`docs/FIXED.md` ("Potions and saints"):

- Deadly Blade, Strongedge and Great Power last about a day, replace each other's dose, and work with the weapons the
  manual names.
- Hardarmor and Firewall last about a day; Firewall guards against flame (Arabian Fire, a dragon's breath) only.
- St. Polycarp, St. Charity and St. Erasmus give fire protection for about a day instead of a lasting charm.
- St. Pantaleon: fire protection 30 and +1 armour against wolves, boars, bears and spiders, about a day.
- St. Patrick: +2 armour against missiles for about a day; St. Dunstan: the party's metal armour 20 points better for
  about a day.
- The party's standing with the Fugger, the Medici and the Hanse survives saving and loading.

Potions and charms already in a saved game stay as they were.

More of the same, also each with its entry in `docs/FIXED.md` and before / after pictures where it shows (and
`docs/SAINTS.md`: what every saint the Fixed rules change gives, the gifts their descriptions leave out included):

- Healing (Essence of Grace, a saint) no longer pulls down an attribute a boost has lifted above its maximum.
- Great Power makes the weapon's quality 14, 22 or 30 points better for about a day, as the manual says, instead of
  scrambling its damage and penetration.
- St. Dismas helps the whole party; St. Christopher heals a little and speeds overland travel for a week; St. Michael
  improves the party's weapons and armour in fights for about a day; St. Anthony heals Strength and Endurance instead of
  lifting them for a day; St. Thomas Aquinas raises Religion, and the party's standing where there is a monastery or a
  university.
- The potion thrown at footpads is used up.
- Outside a city's walls at night, the grapple and everybody climbing have the climbers' own chances, as by day.

## The Comfort interface

- At a merchant's barter and at the inn's item store, F1 to F5 open the members' sheets, as a click on the party does
  (GitHub issue 9). Classic keeps the 1992 pages, where only the click opens them.
- On the loot page, a click on a member or F1 to F5 open the members' sheets, so you can see who can carry what before you take it
  (GitHub issue 10). The original has no way to open a sheet there, and Classic keeps it so.
- In the pack lists of the barter, the item store and the loot page, an asterisk before a line marks an item the member has
  equipped, so a quick key does not sell or put away a worn weapon or armour unseen (GitHub issue 4). The character sheet's Pack
  list marks them too, which also shows what the sign means. The mark only shows; Classic keeps the 1992 lists, which mark nothing.
- At a card menu, ↑ / ↓ (and the mouse wheel) with the pointer off the lines now put the pointer just past the end of the
  widest line, so the hand no longer covers the start of the line it lights (GitHub issue 11). Classic is unchanged.

## Translations

- The untranslated-text audit came out in 1.1.4 (texts a pack never reached, letters lost in some texts, the pronouns):
  see its notes. In 1.2 a pack also names the rules in the notice a saved game with other rules gives.
- The docks' destinations ("boat to …") are in the pack's words: they were the only place names a card put into its
  text without the pack (GitHub issue 5). The foreign traders' and the merchants' titles and the names of the people the
  cards make up (a hermit, a village's Schulz, a rival dragon slayer) and the saint's name on the burnt ship are in the
  pack's words too; a member who joins keeps
  the original's name, as the saved game stores it. A test now fails if a card fills a name into its text without the pack.
- Without a pack every screen is the original's, as before.
