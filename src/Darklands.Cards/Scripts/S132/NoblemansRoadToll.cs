using Darklands.Core;
using Darklands.Core.Characters;
using Darklands.Core.Economy;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$MeetH02": an elderly nobleman riding with his knights and guards demands a "road toll"
/// that depends on how full the purse is. The party may pay, pray, talk him out of it (fame and
/// Speak Common), refuse, ride off (only when all are mounted), or throw a potion and escape.
/// Refusing or a failed escape leaves the choice to submit (losing money and weapons) or to fight.
/// </summary>
// orig: R132+0x034E (186C:000E) matched; tests: CardSmokeTests
//   option 0: action R132+0x0764; hint R132+0x078C
//   option 1: action R132+0x07C4; hint R132+0x0816
//   option 2: action R132+0x084E; hint R132+0x089E
//   option 3: action R132+0x08DE; hint R132+0x0908
//   option 4: action R132+0x0940; hint R132+0x09D0
//   option 5: action R132+0x0A10; hint R132+0x0A7C
//   option 6: action R132+0x0AB4; hint R132+0x0ADC
//   option 7: action R132+0x0B14; hint R132+0x0B1A
//   option 8: action R132+0x0B52; hint R132+0x0B58
//   option 9: action R132+0x0B90; hint -
//   item pop-up: R132+0x0C3E, R132+0x0C9E
//   saint pop-up: R132+0x0D8C
//   other: R132+0x063C, R132+0x0C06, R132+0x0D00,
//     R132+0x0E14
// DORMANT (generated): option slots 10..19 keep the default handler R132+0x0C06 ("screwed up.") and are never shown.
// DORMANT (generated): option line(s) 8, 9 are never enabled (every assignment sets their flag to 0).
// C: the card-engine globals DS:EE70 / DS:EE72 the C sets are the pop-up boxes' drawing flags (R043+0x2ACA, see DIVERGENCE_REVIEW); presentation only, not modelled.
// C: clamp_range(lo, hi, v) is Math.Clamp(v, lo, hi) here; every call has lo <= hi.
public sealed class NoblemansRoadToll : Card
{
    public override int Number => 355;

    private const int Road = 12;

    private static readonly int SpeakCommon = (int)Skill.SpeakCommon;
    private static readonly int Riding = (int)Skill.Riding;

    public override int Run(CardContext c)
    {
        Show(c, 140);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2, 3, 4, 5);
        c.State.SavedCard2 = 0;
        c.Variables.Chosen[0] = c.Leader;
        // The original first works out a toll from the purse and the bank notes, then overwrites it at once.
        // C: (unsigned)money_pfennigs(...) truncates each long total to 16 bits before the divisions.
        // C: DS:9072 is the bank notes' money triple (Party.BankNotes).
        var purse = c.State.Party.Purse;
        c.Variables.Money[0] = Money.FromPfennigs((ushort)purse.TotalPfennigs / 50 + (ushort)c.State.Party.BankNotes.TotalPfennigs / 100);
        c.Variables.Money[0] = purse.TotalPfennigs < 30 ? Money.From(0, 0, 6)
            : purse.TotalPfennigs < 300 ? Money.From(0, 2, 0)
            : Money.From(0, 20, 0);
        if (purse < c.Variables.Money[0])
        {
            menu[0] = OptionState.Shown;
        }
        c.SetCardItems(0x65, 0x66);
        c.SetCardSaints(0x05, 0x4E);
        menu[5] = c.ItemsUsable();
        menu[1] = c.SaintsUsable();

        // After every choice the leader is $ChosenOneName again, before the option's hint is worked out.
        Func<OptionHint> AsLeader(Func<OptionHint> hint) => () =>
        {
            c.Variables.Chosen[0] = c.Leader;
            return hint();
        };
        var assured = AsLeader(OptionHint.Saying(CardTexts.SuccessAssured));
        var combat = AsLeader(OptionHint.Saying(CardTexts.EnterCombat));
        // The card's pop-up choices (the saints' too) are rated by the potion hints, by the slot chosen: the loop calls
        // item_fn[0][menu_slot] for every pop-up. The saint box's own handlers (R132+0x0D04 / R132+0x0D8C, the usual chance
        // text) answer a Shift hint in the saint box and an Enter there (opt_fn is then the box's table).
        // C: item_fn[0][2] / [0][3] are not set by this card (stale from an earlier card); only slots 0 and 1 have
        //   potions or saints here, so the pop-up never reaches them.
        var potionHint = AsLeader(() => PotionChance(c));
        menu.ItemBox = potionHint;
        menu.SaintBox = c.SaintPopupChanceHint;
        menu.PopupRatedByItems = true;
        // orig: R132+0x057B: var_chosen1 = leader after card_menu, before the choice is rated
        menu.OnChoice = () => c.Variables.Chosen[0] = c.Leader;

        // DORMANT: in text block 4 the option lines 0..5 are never offered (an Offer for this block lists other lines); they can be offered in other blocks only.
        // DORMANT: in text block 6 the option lines 0..5 are never offered (an Offer for this block lists other lines, the switch to this block hides them); they can be offered in other blocks only.
        // DORMANT: in text block 7 the option lines 0..5 are never offered (an Offer for this block lists other lines, the switch to this block hides them); they can be offered in other blocks only.
        // DORMANT: in text block 10 the option lines 0..5 are never offered (an Offer for this block lists other lines); they can be offered in other blocks only.
        return menu.Run(
            // orig: R132+0x0764 / R132+0x078C matched
            new MenuLine(_ => PayTheToll(c), assured),
            // orig: R132+0x07C4 / R132+0x0816 matched; pop-up R132+0x0C3E / _095e matched
            new MenuLine(_ => Pray(c, menu), assured, potionHint),
            // orig: R132+0x084E / R132+0x089E matched
            new MenuLine(chance => TalkThemOutOfIt(c, menu, chance), AsLeader(() => TalkChance(c))),
            // orig: R132+0x08DE / R132+0x0908 matched
            // C: declared int, falls off its end without a value (ignored).
            new MenuLine(_ => menu.Offer(4, 6, 7), AsLeader(OptionHint.Saying(CardTexts.Unknown))),
            // orig: R132+0x0940 / R132+0x09D0 matched
            new MenuLine(chance => RideOff(c, menu, chance), AsLeader(() => RideChance(c))),
            // orig: R132+0x0A10 / R132+0x0A7C matched; pop-up R132+0x0C3E / _095e matched
            new MenuLine(chance => ThrowAPotion(c, menu, chance), AsLeader(OptionHint.Saying(CardTexts.SelectPotion)), potionHint),
            // orig: R132+0x0AB4 / R132+0x0ADC matched
            new MenuLine(_ => Submit(c), assured),
            // orig: R132+0x0B14 / R132+0x0B1A matched
            new MenuLine(_ => FightTheGuards(c), combat),
            // orig: R132+0x0B52 / R132+0x0B58 matched
            // DORMANT: line 8 is never enabled; it is the same fight as line 7.
            new MenuLine(_ => FightTheGuards(c), combat),
            // orig: R132+0x0B90 / R132+0x0B92 matched
            // DORMANT: line 9 is never enabled; its handler is empty, and its hint R132+0x0B92 is an empty void
            //   function, so the C would take whatever DX:AX held as the hint (here "Success is assured.").
            // DORMANT: R132+0x0D00, _09c2, _0ad4 and _0ad6 are further empty functions nothing calls.
            new MenuLine(_ => { }, assured));
    }

    /// <summary>The party leaves the place for the open road.</summary>
    private static void OnToTheRoad(CardContext c)
    {
        c.State.CurrentLocation = GameState.Wilderness;
        c.Next = Road;
    }

    // orig: R132+0x0764 matched
    private static void PayTheToll(CardContext c)
    {
        c.State.Party.Purse = c.State.Party.Purse.Subtract(c.Variables.Money[0]);
        c.Screen.Text(1);
        OnToTheRoad(c);
    }

    /// <summary>Only a prayer that returns exactly 1 confuses the nobleman (text 2); otherwise the prayer is greyed out.</summary>
    // orig: R132+0x07C4 matched
    // C: returns 0 after a cancelled box, otherwise returns without a value (ignored).
    private void Pray(CardContext c, CardMenu menu)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        var result = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuEntrySlot]);
        if (result == -1)
        {
            return;
        }
        if (result == 1)
        {
            c.Screen.Text(2);
            OnToTheRoad(c);
            return;
        }
        menu[1] = OptionState.Shown;
        c.Next = Number;
    }

    /// <summary>Fame plus the leader's Speak Common, 1..99.</summary>
    // orig: R132+0x089E matched
    private static OptionHint TalkChance(CardContext c) =>
        OptionHint.ChanceOf(Math.Clamp(c.PartyGet(4) + c.Skill(-1, SpeakCommon), 1, 99), c.Leader);

    // orig: R132+0x084E matched
    private static void TalkThemOutOfIt(CardContext c, CardMenu menu, int chance)
    {
        if (c.Random(100) <= chance)
        {
            c.Screen.Text(13);
            c.PracticeSkill(c.Leader, SpeakCommon, 7, 10, 0);
            OnToTheRoad(c);
            return;
        }
        menu[2] = OptionState.Shown;
        c.Screen.Text(14);
    }

    /// <summary>
    /// The slowest mounted agility plus half the Riding of that member, 0..99; 0 unless all are
    /// mounted. The original's handler does not say who acts: what is left over is 99 when the
    /// sum was over 99, else 0 (the first member).
    /// </summary>
    // orig: R132+0x09D0 matched
    // C: checked against the asm: the hint is a void function; its "result" is clamp_range's DX:AX, AX = the clamped
    //   sum and DX = 99 when the sum was over 99, otherwise 0 (the low bound). party_min_eff_attr2_bonus runs before
    //   best_member (unsigned char) is read for the Riding skill; the halving truncates toward zero.
    private static OptionHint RideChance(CardContext c)
    {
        var sum = c.PartyMinMountedAgility() + c.Skill(c.BestMember, Riding) / 2;
        if (c.PartyGet(5) == 0)
        {
            sum = 0;
        }
        return OptionHint.ChanceOf(Math.Clamp(sum, 0, 99), sum > 99 ? 99 : 0);
    }

    /// <summary>
    /// Without a chance the mounted guard corners them at once (text 7); within it they outride
    /// the pursuit (text 5); otherwise the pursuit gains on them (text 6). Either way the choice
    /// is then to submit or fight.
    /// </summary>
    // orig: R132+0x0940 matched
    private static void RideOff(CardContext c, CardMenu menu, int chance)
    {
        menu.Offer(menu.Block, 6, 7);
        if (chance == 0)
        {
            menu.Block = 7;
            return;
        }
        if (c.Random(100) <= chance)
        {
            c.Screen.Text(5);
            c.PracticeSkill(-2, Riding, 1, 10, 0);
            OnToTheRoad(c);
            return;
        }
        menu.Block = 6;
        c.PracticeSkill(-2, Riding, 0, 10, 0);
    }

    /// <summary>
    /// The potion pop-up hints (by slot): the member becomes $ChosenOneName; 99 (first potion) or
    /// 95 (second) when everyone is mounted, else the slowest mounted agility plus the potion's
    /// quality (+1 within 1..99, or +10 within 10..95).
    /// </summary>
    // orig: R132+0x0C3E (slot 0) / R132+0x0C9E (slot 1) matched
    private static OptionHint PotionChance(CardContext c)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        var quality = () => c.ItemQuality(c.Variables.Chosen[0], c.CardItems[c.MenuEntrySlot]);
        int chance;
        if (c.MenuSlot == 0)
        {
            chance = c.PartyGet(5) != 0 ? 99 : Math.Clamp(c.PartyMinMountedAgility() + quality() + 1, 1, 99);
        }
        else
        {
            chance = c.PartyGet(5) != 0 ? 95 : Math.Clamp(c.PartyMinMountedAgility() + quality() + 10, 10, 95);
        }
        return OptionHint.ChanceOf(chance, c.MenuMember);
    }

    /// <summary>The potion scatters the riders and the party hides (text 9), or fizzles (text 10: submit or fight).</summary>
    // orig: R132+0x0A10 matched
    private static void ThrowAPotion(CardContext c, CardMenu menu, int chance)
    {
        c.TakeItem(c.MenuMember, c.CardItems[c.MenuSlot]);
        if (c.Random(100) <= chance)
        {
            c.Screen.Text(9);
            OnToTheRoad(c);
            return;
        }
        menu.Offer(10, 6, 7);
    }

    /// <summary>The knight's men take all money and weapons (text 8); the party's place is not reset here.</summary>
    // orig: R132+0x0AB4 matched
    private static void Submit(CardContext c)
    {
        c.LoseItems(-2);
        c.Screen.Text(8);
        c.PassHours(2);
        c.Next = Road;
    }

    /// <summary>
    /// The fight with the knight and his men. Won: the nobleman escapes and the nearest city's
    /// reputation suffers (text 11); fled: the away members return without their packs (text
    /// 15); lost or surrendered: robbed (texts 12, 8). Any other result offers the menu again.
    /// </summary>
    // orig: R132+0x063C matched
    private static void FightTheGuards(CardContext c)
    {
        // C: checked against the asm: the arguments are worked out from the last one back: party_lore_level (7th),
        //   then rand_mod(3) of the 5th, then rand_mod(3) and party_lore_level of the 4th (signed division by 4).
        var third = c.PartyLoreLevel() > 6 ? 2 : 1;
        var second = c.Random(3) + 3;
        var first = c.Random(3) + c.PartyLoreLevel() / 4 + 1;
        switch (c.RunBattle(0x2E, -1, 3, first, second, 0x21, third, 3, -1, 0, 0))
        {
            case 0:
                c.PassHours(1);
                c.AdjustReputation(c.NearestCity(c.State.MapPosition.X, c.State.MapPosition.Y), -20, -30);
                c.Screen.Text(11);
                OnToTheRoad(c);
                break;
            case 1 or 2:
                for (var who = 0; who < c.PartySize; who++)
                {
                    if (c.Status(who) == CharacterStatus.Away)
                    {
                        c.SetStatus(who, CharacterStatus.Able);
                        c.LoseItems(who);
                    }
                }
                c.Screen.Text(15);
                OnToTheRoad(c);
                break;
            case 3:
                c.PassHours(c.Random(3) + 3);
                c.LoseItems(-2);
                c.Screen.Text(12);
                OnToTheRoad(c);
                break;
            case 4:
                c.PassHours(1);
                c.LoseItems(-2);
                c.Screen.Text(8);
                OnToTheRoad(c);
                break;
        }
    }
}
