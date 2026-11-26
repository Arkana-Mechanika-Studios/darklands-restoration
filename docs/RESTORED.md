# The Restored rules

**Restored** is the Fixed rules plus content the 1992 game holds but never lets the player reach, and what its designers
meant it to do. Each item is in the game's own files (its cards, texts and pictures), or was in the game in an earlier release
of 1992, and is put back with as little new writing as possible. Original and Fixed do not change.

Restored is **not offered to players yet**: the launcher and `rules.preset` offer Fixed and Original
([SETTINGS.md](SETTINGS.md#the-rules-preset)). It is being built and checked item by item. Every item is a switch of its own,
tested with the switch on and off (the register: [faithfulness/SWITCHES.md](faithfulness/SWITCHES.md)), with its sources and
the decision to restore it in [faithfulness/RESTORATION.md](faithfulness/RESTORATION.md). Each item has a page of its own:
how to find it, what happens, screenshots from play. The screenshots are played again from the recorded paths
in [tools/screenshots/restored](../tools/screenshots/restored/play.py); where a page sets a starting state with the developer
console, it says so. What Restored adds:

## Cities

- [The physician's second door](restored/physician-door.md): the business district has a second door to the physician's house
  (the game reaches it only from the crafts quarter).
- [Weapons training with the city guard](restored/guard-training.md): the town hall's guard can be asked for weapons training,
  once a day, while the city is not hostile to the party.
- [The clothmakers' guild by day](restored/cloth-guild-by-day.md): the guild sells its armour by day too (the game sells it
  only to a party that wakes somebody at night).
- [The prison rescue](restored/prison-rescue.md): in a city's prison, a bribe that works and a won fight free the imprisoned companion, as the earlier releases did (483.04 to
  483.06); drawing steel first tells the party that the guards of every floor must fall before the cells can be searched, and
  a lost fight or a flight frees nobody (the last release, 483.07, frees him at once, even before a fight the party then loses).

## Travel

- [The Hussite band on the road](restored/hussite-band.md): on the roads near St. Joachimsthal, Buerglitz, Prag, Kuttenberg and Bruenn the party can meet a band of armed Hussites,
  as in the releases 483.04 to 483.06 (483.07 removed the encounter from the road table). Meeting them marks the nearest city
  as risen and preparing for war, which its gossip, its docks and the boats that land there then show.
