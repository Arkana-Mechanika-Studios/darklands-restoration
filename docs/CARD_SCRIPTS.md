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
        c.Next = Number;                          // following-card = own number: stay while unchanged
        var menu = Menu(c, block: 0);             // the text block with the option lines
        menu.Enable(0, 1);                        // card-option-flag-0 = card-option-flag-1 = 1
        menu[2] = OptionState.Shown;              // card-option-flag-2 = 2: shown, not choosable
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
shows it), line k is `card-option-handler[k]` and its action. `Run` offers the lines while `c.Next` is the card's
number (it does not set it). The chosen line is rated again on the choice, as the original does (the
line's `Popup` hint, else `menu.Popup`, for a choice made in the item or saint pop-up box), so the
handler's side effects ($ChosenOneName ...) happen; only an action that takes the chance gets it. `EachRound` runs at the start of every round
(block or flags re-read each round; moving the card on ends the loop), `OnChoice` after the choice.
A non-hidden line without a `MenuLine` shows "screwed up." and does nothing. The hint texts are
`CardTexts`; the usual pop-up hints are `c.MemberPopupHint()`, `c.SaintPopupHint()` and
`c.SaintPopupChanceHint()`. The golden traces (`GoldenTraceTests`) record every card's run; a
refactoring must leave them unchanged.

* Picture numbers: `image-file-names.f_XXXX` is picture `(0xXXXX - 0x0A47) / 4` in `PictureNames`.
* `opt_flag_k` 0/1/2 → `menu[k] = OptionState.Hidden/Enabled/Shown`.
* `card-text-block(i, how)` → `c.Screen.Text(i)` (`how` 1 → `TextFrame.BlankPage`).
* `pass-hours(n)` → `c.PassHours(n)`; `roll-below(n)` → `c.Random(n)`.
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
| `leader`, `party-headcount`, `party[who][0]` | `c.Leader`, `c.PartySize`, `c.Status(who)` |
| `member-ability`, `ability-assign`, `raise-skill-by` | `c.Skill`, `c.SetSkill`, `c.AddSkill` |
| `member-stat`, `stat-assign`, `raise-attribute-by` | `c.Attribute`, `c.SetAttribute`, `c.AddAttribute` |
| `member-stat-cap`, `stat-cap-assign`, `raise-attribute-cap-by`, `heal-attribute-percent` | `c.AttributeMaximum`, `c.SetAttributeMaximum`, `c.AddAttributeMaximum`, `c.HealAttribute` |
| `member-label`, `member-brief-label`, `member-years` | `c.MemberName`, `c.MemberShortName`, `c.MemberAge` |
| `member-top-skill`, `party-top-ability-low/hi` | `c.BestWeaponSkill`, `c.PartyBestHandWeaponSkill`, `c.PartyBestMissileSkill` |
| `party-lowest/max/avg_skill`, `party-lowest/max_skill2`, `party-lowest/max_skill_sum` | `c.PartyMinSkill`, `c.PartyMaxSkill`, `c.PartyAverageSkill` (one or two skills), `c.PartyMinSkillSum`, `c.PartyMaxSkillSum` |
| `party-lowest/max/avg_attr`, `party-stat-top-sum` | `c.PartyMinAttribute`, `c.PartyMaxAttribute`, `c.PartyAverageAttribute`, `c.PartyMaxAttributeSum` |
| `member-effective-stat-two`, `party_*_eff_attr2(_bonus)` | `c.EffectiveAgility`, `c.PartyAverage/Min/MaxEffectiveAgility`, `c.PartyMax/MinMountedAgility` |
| `top-scoring-member`, `top-value-so-far`, `top-skill-so-far` | `c.BestMember`, `c.BestValue`, `c.BestSkillFound` (`c.BestAttributeValue` for attribute queries) |
| `party-able-total` (able **women**), `party-here-total`, `party-all-fit` | `c.AbleWomen()`, `c.PresentCount()`, `c.PartyAllWell()` |
| `party-read`, `party-write` (4 fame, 5 all mounted, 6 stone) | `c.PartyGet`, `c.PartySet` |
| `exercise-skill`, `improvement-roll`, `injure`, `award-prize` (fame) | `c.PracticeSkill`, `c.ImproveRoll`, `c.Injure`, `c.GiveReward` |
| `stat-temporary-boost`, `skill-temporary-drop` (raises!) | `c.RaiseAttributeTemporarily`, `c.RaiseSkillTemporarily` |
| `party-lore-rating` | `c.PartyLoreLevel()` |
| `member-flag-bit`, `member-flag-set`, `saint-odds-low/hi`, `call-on-saint` | `c.KnowsSaint`, `c.LearnSaint`, `c.SaintChanceLow/High`, `c.InvokeSaint` (prayer screen hook) |
| `item-tally`, `item-tally-by-flags`, `top-quality-of-item`, `carries-item` | `c.CountItems`, `c.CountItemsWithFlags`, `c.BestQualityWithFlags`, `c.HasItem` |
| `give-one-item-form`, `give-several-items`, `give-item-record-form`, `pick-up-item`, `item-stack-take-out` | `c.AddItem`, `c.AddItems`, `c.AddItemStack`, `c.TakeItem`, `c.RemoveItemStack` |
| `drop-items`, `item-keep-percent`, `items-take-out-by-flags`, `worn-item`, `item-grade(_flags)` | `c.LoseItems`, `c.KeepItems`, `c.RemoveItemsWithFlags`, `c.EquippedItem`, `c.ItemQuality(ByFlags)` |
| `join-party-member`, `party-report-absent`, `party-send-away`, `party-recall-away`, `battle-driver` | `c.AddMember`, `c.ReportAway`, `c.DropAway`, `c.RejoinAway`, `c.RunBattle` (battle hook) |
| `location-now`, `city-record`, `city-read` | `c.Here`, `c.City`, `c.LoadCity` |
| `place-query`, `location-now-query`, `place-assign`, `reputation-now` | `c.LocationQuery`, `c.HereQuery`, `c.LocationSet`, `c.ReputationHere` |
| `place-standing`, `place-standing-adjust`, `place-hostile` | `c.Reputation`, `c.AdjustReputation`, `c.Hostile()` |
| `place-side-read/set`, `side1/2_strength`, `side1/2_percent`, `fight-resolution` | `c.SideOf`, `c.SetSide`, `c.SideStrength(∓1)`, `c.SidePercent(∓1)`, `c.ConflictOutcome` |
| `closest-city`, `closest-place-of-kind`, `thunk 09C0:1F13` | `c.NearestCity`, `c.NearestLocationOfType`, `c.FarLocationOfType` |
| `event-create`, `event-refresh`, `event-advance`, `event-release` | `c.Events.Create`, `.Renew`, `.Update(…, EventUpdate.Add)`, `.Free` |
| `event-here-test`, `event-here-by-type`, `event-here-lengthen` | `c.EventHere(kind, hours)`, `c.EventHere(kind, type, hours)`, `c.ExtendEventHere` |
| `event-present`, `event-begun-flag`, `event-total`, `event-at-place`, `event-kind`, `event-kind-at` | `c.Events.Exists`, `.Started`, `.Count`, `.At` (`c.EventAtHere`), `.HasType`, `.TypeAt` |
| `event-datum`, `event-datum-at`, `event-datum-kind`, `event-datum-kind-at` | `c.Events.Value`, `.ValueAt`, `.ValueOfType`, `.ValueOfTypeAt` |
| `event-search`, `event-search-by-place`, `event-delete`, `latest-event` | `c.Events.Find`, `.FindLocation`, `.Remove`, `c.LastEvent` |
| `open-quest-exists`, `open-quest-at`, `open-quest-at-owner`, `open-quest-value`, `open-quest-owner-value`, `open-quest-resolve` | `c.Events.QuestExists`, `.QuestAt`, `.QuestAt(tag, owner, loc)`, `.QuestValue`, `.QuestWithValue`, `c.ResolveQuest` |
| `resolved-quest-exists`, `resolved-quest-at`, `resolved-quest-at-owner` | `c.Events.ResolvedQuestExists`, `.ResolvedQuestAt` |
| `place-flag(_any)`, `place-flag-value(_any)`, `place-flags-reset` | `c.Events.Fact`, `.FactAnywhere`, `.FactValue`, `.FactValueAnywhere`, `.ClearFacts` |
| `event-place-of`, `event-kind-of`, `event-detail-word-of` | `c.EventLocation`, `c.EventType`, `c.EventDetail` |
| `card-item-lines[]`, `card-saint-lines[]`, `usable-item-list`, `saints-available` | `c.SetCardItems(…)`, `c.SetCardSaints(…)`, `c.ItemsUsable()`, `c.SaintsUsable()` (→ `OptionState`) |
| `menu-owner-member`, `menu-choice-index`, `popup-menu-box` | `c.MenuMember`, `c.MenuSlot`, `c.MenuPopup` (set by the screen's pop-up box) |
| `DS:5204` (the pop-up lists every item/saint for every member) | `c.PopupListsAll` |
| `chance-line`, `label-to-slot` | `c.ChanceText(lo, hi)`, `c.NameToSlot` |
| `pass-days`, `wait-for-hour`, `daylight-test`, `roll-n` | `c.PassDays`, `c.WaitUntilHour`, `c.IsDaylight`, `c.RandomScaled` |

The R039 helpers card scripts reach through vectors have C# names too (`thunk 09C0:1F09`
`StartDragonQuest`, `thunk 09C0:2175` `StartQuest3`, `thunk 09C0:21B1` `StartQuest10`, `thunk 09C0:20E9`
`LocalPrice`, `thunk 09C0:2099` `ServicePrice`, `thunk 09C0:2107` `PriceEvents`, `thunk 09C0:1F81`
`FeastDayNearThisMonth` …): see the XML docs in `CardContext.World.cs`.

Saints: `c.InvokeSaint` opens the prayer screen through `c.SaintPrayer` (Presentation
`SaintPrayerScreen`, attached with `SaintPrayerScreen.Attach(context, ui, panel)`), whose help is
`c.SaintEffects` (Core `SaintEffects` / `SaintBook`); it returns −1 cancelled, 0 failed, 1 helped.

Not written yet, behind hooks: battles (`c.Battle`), `made-up-name` and `label-to-slot` kinds 2 and 6–8
(name tables in the EXE), the shop and stash lists of `item-tally` (who ≥ 5).
