# Writing card scripts

A card is a scene: a picture, texts from its `.MSG` file, and choices. The game moves from card
to card; each card's `Run` returns the number of the next one. Cards live in
`src/Darklands.Cards/Scripts`, one class per card (or a small group of closely related cards).

## Sources of truth

* Semantics: the verified reference decompilation (machine-local: `reference` in local/paths.json;
  see docs/TRACEABILITY.md). Its byte-matched C compiles to the original bytes; its drafts must be
  read with the disassembly. The card API names below are the reference's readable names; the
  card system lives in `src/Darklands.Cards` (`CardContext`, `CardMenu`, `ICardScreen`). The code cites the original coordinates only.
* Card number → original function: `CardTable` (generated) and the crosswalk notes.
* Never copy their C. Read it, understand what the card does, and write it in our own words with
  our API. Keep the original behaviour exactly: the same order of random draws (MSC evaluates call
  arguments right to left), the same conditions, texts, pictures and next cards.

## Shape of a card

```csharp
public sealed class AtTheInn : Card              // a name that says what the scene is
{
    public override int Number => 0x1D;           // the card number

    public override int Run(CardContext c)
    {
        Show(c, 337);                             // card picture (PictureNames number)
        c.Next = Number;                          // next_card = own number: stay while unchanged
        var menu = Menu(c, block: 0);             // the text block with the option lines
        menu.Enable(0, 1);                        // opt_flag_0 = opt_flag_1 = 1
        menu[2] = OptionState.Shown;              // opt_flag_2 = 2: shown, not choosable
        // set up variables, effects done on arrival ...
        return menu.Run(
            new MenuLine(() => c.Next = 0x1E),                                   // plain choice
            new MenuLine(() => { c.PassHours(8); c.Next = Number; },
                OptionHint.Saying(NoRisk)),                                      // Shift hint
            new MenuLine(chance => TryToBribe(c, chance),                         // handed the chance
                () => OptionHint.ChanceOf(BribeChance(c), c.Leader)));
    }
}
```

The menu (`CardMenu`, `src/Darklands.Cards/CardMenu.cs`) is the original's loop: the states are
`opt_flag_k` (every line starts hidden; actions may change them and `menu.Block`, the next round
shows it), line k is `opt_fn[k]` and its action. `Run` offers the lines while `c.Next` is the card's
number (it does not set it). The chosen line is rated again on the choice, as the original does (the
line's `Popup` hint, else `menu.Popup`, for a choice made in the item or saint pop-up box), so the
handler's side effects ($ChosenOneName ...) happen; only an action that takes the chance gets it. `EachRound` runs at the start of every round
(block or flags re-read each round; moving the card on ends the loop), `OnChoice` after the choice.
A non-hidden line without a `MenuLine` shows "screwed up." and does nothing. The hint texts are
`CardTexts`; the usual pop-up hints are `c.MemberPopupHint()`, `c.SaintPopupHint()` and
`c.SaintPopupChanceHint()`. The golden traces (`GoldenTraceTests`) record every card's run; a
refactoring must leave them unchanged.

* Picture numbers: `pic_names.f_XXXX` is picture `(0xXXXX - 0x0A47) / 4` in `PictureNames`.
* `opt_flag_k` 0/1/2 → `menu[k] = OptionState.Hidden/Enabled/Shown`.
* `card_text(i, how)` → `c.Screen.Text(i)` (`how` 1 → `TextFrame.BlankPage`).
* `advance_hours(n)` → `c.PassHours(n)`; `rand_mod(n)` → `c.Random(n)`.
* Hint functions (`struct res` with `.a = chance or -1`, `.b = member or -2`) → `OptionHint`.
* Name helper methods after what they do (`TryToBribeTheGuard`), not after addresses.
* `$variables` in texts: set `c.Variables` (Money, Numbers, Chosen, Named, Texts, Location, Item,
  Conditions) before showing the text.

## The rules API (the reference's readable name → C#)

Everything a script needs is on `CardContext` (`c`), grouped in `CardContext.Party.cs`,
`.Items.cs`, `.World.cs`, `.Events.cs` and `.Engine.cs`; the rules themselves live in
`Darklands.Core.Rules` and `Darklands.Core.Events`. `who` keeps the original meaning: 0..4,
`Who.Leader` (−1), `Who.Everybody` (−2). Skill and attribute numbers are the game's
(`(int)Skill.Religion`, `(int)Attribute.DivineFavor`). The original's bugs are reproduced and
listed in `OriginalBehaviour` (each with a fix flag where cheap); do not "fix" them in scripts.

| reference name | C# |
|---|---|
| `leader`, `party_size`, `party[who][0]` | `c.Leader`, `c.PartySize`, `c.Status(who)` |
| `member_skill`, `set_skill`, `add_skill` | `c.Skill`, `c.SetSkill`, `c.AddSkill` |
| `member_attr`, `set_attr`, `add_attr` | `c.Attribute`, `c.SetAttribute`, `c.AddAttribute` |
| `member_attr_max`, `set_attr_max`, `add_attr_max`, `heal_attr_pct` | `c.AttributeMaximum`, `c.SetAttributeMaximum`, `c.AddAttributeMaximum`, `c.HealAttribute` |
| `member_name`, `member_short_name`, `member_age` | `c.MemberName`, `c.MemberShortName`, `c.MemberAge` |
| `member_best_skill`, `party_best_skill_lo/hi` | `c.BestWeaponSkill`, `c.PartyBestHandWeaponSkill`, `c.PartyBestMissileSkill` |
| `party_min/max/avg_skill`, `party_min/max_skill2`, `party_min/max_skill_sum` | `c.PartyMinSkill`, `c.PartyMaxSkill`, `c.PartyAverageSkill` (one or two skills), `c.PartyMinSkillSum`, `c.PartyMaxSkillSum` |
| `party_min/max/avg_attr`, `party_max_attr_sum` | `c.PartyMinAttribute`, `c.PartyMaxAttribute`, `c.PartyAverageAttribute`, `c.PartyMaxAttributeSum` |
| `member_eff_attr2`, `party_*_eff_attr2(_bonus)` | `c.EffectiveAgility`, `c.PartyAverage/Min/MaxEffectiveAgility`, `c.PartyMax/MinMountedAgility` |
| `best_member`, `best_value`, `best_skill` | `c.BestMember`, `c.BestValue`, `c.BestSkillFound` (`c.BestAttributeValue` for attribute queries) |
| `party_count_able` (able **women**), `party_present_count`, `party_all_ok` | `c.AbleWomen()`, `c.PresentCount()`, `c.PartyAllWell()` |
| `party_get`, `party_set` (4 fame, 5 all mounted, 6 stone) | `c.PartyGet`, `c.PartySet` |
| `practice_skill`, `improve_roll`, `injure`, `give_reward` (fame) | `c.PracticeSkill`, `c.ImproveRoll`, `c.Injure`, `c.GiveReward` |
| `raise_attr_temp`, `lower_skill_temp` (raises!) | `c.RaiseAttributeTemporarily`, `c.RaiseSkillTemporarily` |
| `party_lore_level` | `c.PartyLoreLevel()` |
| `member_bit`, `set_member_bit`, `saint_chance_lo/hi`, `invoke_saint` | `c.KnowsSaint`, `c.LearnSaint`, `c.SaintChanceLow/High`, `c.InvokeSaint` (prayer screen hook) |
| `count_items`, `count_items_flags`, `best_item_quality`, `has_item` | `c.CountItems`, `c.CountItemsWithFlags`, `c.BestQualityWithFlags`, `c.HasItem` |
| `add_item`, `add_items`, `add_item_rec`, `take_item`, `remove_item_stack` | `c.AddItem`, `c.AddItems`, `c.AddItemStack`, `c.TakeItem`, `c.RemoveItemStack` |
| `lose_items`, `keep_items_pct`, `remove_items_flags`, `equipped_item`, `item_quality(_flags)` | `c.LoseItems`, `c.KeepItems`, `c.RemoveItemsWithFlags`, `c.EquippedItem`, `c.ItemQuality(ByFlags)` |
| `add_member`, `party_report_away`, `party_drop_away`, `party_rejoin_away`, `run_battle` | `c.AddMember`, `c.ReportAway`, `c.DropAway`, `c.RejoinAway`, `c.RunBattle` (battle hook) |
| `cur_loc`, `city_rec`, `load_city` | `c.Here`, `c.City`, `c.LoadCity` |
| `loc_query`, `cur_loc_query`, `loc_set`, `cur_rep` | `c.LocationQuery`, `c.HereQuery`, `c.LocationSet`, `c.ReputationHere` |
| `loc_reputation`, `loc_reputation_adj`, `loc_hostile` | `c.Reputation`, `c.AdjustReputation`, `c.Hostile()` |
| `loc_side_get/set`, `side1/2_strength`, `side1/2_percent`, `conflict_outcome` | `c.SideOf`, `c.SetSide`, `c.SideStrength(∓1)`, `c.SidePercent(∓1)`, `c.ConflictOutcome` |
| `nearest_city`, `nearest_loc_type`, `thunk 09C0:1F13` | `c.NearestCity`, `c.NearestLocationOfType`, `c.FarLocationOfType` |
| `event_new`, `event_renew`, `event_update`, `event_free` | `c.Events.Create`, `.Renew`, `.Update(…, EventUpdate.Add)`, `.Free` |
| `event_here`, `event_here_type`, `event_here_extend` | `c.EventHere(kind, hours)`, `c.EventHere(kind, type, hours)`, `c.ExtendEventHere` |
| `event_exists`, `event_started`, `event_count`, `event_at`, `event_type`, `event_type_at` | `c.Events.Exists`, `.Started`, `.Count`, `.At` (`c.EventAtHere`), `.HasType`, `.TypeAt` |
| `event_value`, `event_value_at`, `event_value_type`, `event_value_type_at` | `c.Events.Value`, `.ValueAt`, `.ValueOfType`, `.ValueOfTypeAt` |
| `event_find`, `event_find_loc`, `event_remove`, `last_event` | `c.Events.Find`, `.FindLocation`, `.Remove`, `c.LastEvent` |
| `ev8_exists`, `ev8_at`, `ev8_at_owner`, `ev8_value`, `ev8_owner_value`, `ev8_resolve` | `c.Events.QuestExists`, `.QuestAt`, `.QuestAt(tag, owner, loc)`, `.QuestValue`, `.QuestWithValue`, `c.ResolveQuest` |
| `ev24_exists`, `ev24_at`, `ev24_at_owner` | `c.Events.ResolvedQuestExists`, `.ResolvedQuestAt` |
| `loc_fact(_any)`, `loc_fact_value(_any)`, `loc_facts_clear` | `c.Events.Fact`, `.FactAnywhere`, `.FactValue`, `.FactValueAnywhere`, `.ClearFacts` |
| `event_loc_of`, `event_type_of`, `event_f2e_of` | `c.EventLocation`, `c.EventType`, `c.EventDetail` |
| `card_items[]`, `card_saints[]`, `items_usable`, `saints_usable` | `c.SetCardItems(…)`, `c.SetCardSaints(…)`, `c.ItemsUsable()`, `c.SaintsUsable()` (→ `OptionState`) |
| `menu_member`, `menu_slot`, `menu_popup` | `c.MenuMember`, `c.MenuSlot`, `c.MenuPopup` (set by the screen's pop-up box) |
| `DS:5204` (the pop-up lists every item/saint for every member) | `c.PopupListsAll` |
| `chance_text`, `name_to_slot` | `c.ChanceText(lo, hi)`, `c.NameToSlot` |
| `advance_days`, `wait_until_hour`, `is_daylight`, `random_n` | `c.PassDays`, `c.WaitUntilHour`, `c.IsDaylight`, `c.RandomScaled` |

The R039 helpers card scripts reach through vectors have C# names too (`thunk 09C0:1F09`
`StartDragonQuest`, `thunk 09C0:2175` `StartQuest3`, `thunk 09C0:21B1` `StartQuest10`, `thunk 09C0:20E9`
`LocalPrice`, `thunk 09C0:2099` `ServicePrice`, `thunk 09C0:2107` `PriceEvents`, `thunk 09C0:1F81`
`FeastDayNearThisMonth` …): see the XML docs in `CardContext.World.cs`.

Saints: `c.InvokeSaint` opens the prayer screen through `c.SaintPrayer` (Presentation
`SaintPrayerScreen`, attached with `SaintPrayerScreen.Attach(context, ui, panel)`), whose help is
`c.SaintEffects` (Core `SaintEffects` / `SaintBook`); it returns −1 cancelled, 0 failed, 1 helped.

Not written yet, behind hooks: battles (`c.Battle`), `random_name` and `name_to_slot` kinds 2 and 6–8
(name tables in the EXE), the shop and stash lists of `count_items` (who ≥ 5).
