# The Fixed rules

New games play with the **Fixed** rules by default: the 1992 game with its gameplay bugs fixed. **Original** keeps the
1992 rules, bugs included. Both fix the bugs that damage saved games or end the program, and plain display glitches.

Choose in the launcher (Advanced tab, "Rules"), in the settings file (`rules.preset`: `"fixed"` or `"original"`), or for
one run with `--rules fixed` or `--rules original` ([SETTINGS.md](SETTINGS.md#the-rules-preset)). The choice is for new
games only: a saved game keeps the rules it was started with, and a game saved before this version loads as Original. Saved
games stay in the original's format either way, and the 1992 game loads them.

Each fix is a switch of its own, with the original's behaviour kept beside it and tested both ways (the register:
[faithfulness/SWITCHES.md](faithfulness/SWITCHES.md)). What Fixed changes:

## Cards, places and the party

- A timed game event that adds to a value no longer gets twice the value when it is first set.
- Raising an attribute's maximum stays within 0–99.
- Asking for the party's best weapon skill names the best one, not always the same skill.
- Cards that add two attributes together add both, instead of counting the first one twice.
- A city's unrest counts when the game asks whether the city is hostile (the original's test had its terms swapped).
- A card that changes a city's bowyer or gunsmith changes that shop, not the other one.
- A place's list of three remembered values keeps the older values when a new one is added, instead of overwriting them all.
- An item given to every member is given once, not twice.
- A temporary boost for the whole party is taken back when it runs out.
- A saint learned by the leader is learned by the leader (the original learned none and changed another member's formulas).
- St. Cecilia's standing, prayed for in the open country, goes to the nearest city instead of nowhere.
- A saint's weapon-skill effect prayed for the whole party works on the leader's weapons, instead of doing nothing.
- When items may be lost one by one, each item rolls for itself; an empty pack no longer loses the next item picked up.
- Cards that ask for the party's best item of a kind look at each item's own kind.
- Skill and attribute boosts for the whole party give each member the boost once.
- A battle at a place is set up from the party's seed once, not twice.
- Paying fame leaves fame minus the cost, instead of the cost minus fame (often below zero).
- Whether a shop has an item depends on the city's size, not on a leftover value.
- At the university, a failed search for formulas shuts the formulas line for the week, not the saints line.
- At a church at night, the altar boy asked about Mass between 9:00 and 14:59 gives the right answer, not a stray hour.
- A clerk's "come back later" at the town hall or the fortress keeps its promise: the party is welcome there the next day.
- A City Lord's commission given at the fortress (in a city without a town hall) is paid when it is done, as the town hall pays its own.
- A village Schulz's robber-knight task is paid when it is done (ask him for information).
- Alt-L, then Esc on the load screen, goes back to the game as it was (the original went on with an empty party and no events).
- Alchemists sell formulas from the whole range, stronger potions and higher grades included (the original's sold only the
  first seven potions).
- A saint's help or a price set during a battle ends when it should (the original lost it after the battle, so it lasted for good).
- A temporary attribute boost gives back exactly what it added when it ends (the original could leave the attribute lower than
  before when boosts overlapped or met the maximum).
- Claiming friendship with a Hussite band succeeds at the chance shown (the original needed the roll to equal it exactly).
- In the peat bog, a rope lets the party throw a rope (the original asked for a polearm, a bow or metal armour instead).

## Camp

- Stopping a brew gives back only its ingredients, not empty stacks for unused ingredient slots.
- Stopping a brew finds every brew of that member.
- At an inn, the teacher list shows every teacher of the town, and each row is that teacher (the original counted the teachers of
  other towns, leaving some out and sometimes picking another town's).
- Leaving a stay by clicking an empty party row ends it as the other ways do: Save and Load come back, the music
  too, and unfinished brews stop as on any way out (the original left Save and Load off until another screen).

## Battles

- An empty enemy group no longer takes every later group with it: the other groups still come.
- A cloud that has gone no longer blocks shots.
- On the loot screen after a battle, D skips the member who left by the stairs, not another one.
- Esc in order mode with nobody selected no longer leaves the battle half-paused.
- Enter (travel individually) with nobody selected gives no order and changes nothing else.
- U with nobody selected and the party split changes nothing.
- The mark the stairs leave on a wall group is kept when the level is left, so that wall is not opened again.
