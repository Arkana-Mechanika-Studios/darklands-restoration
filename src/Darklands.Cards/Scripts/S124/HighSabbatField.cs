using Darklands.Core.Characters;
using Darklands.Core.Events;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Witcd26": the middle of the High Sabbat field, the hub of the sabbat: the water ritual
/// (card 324), the broom-riding (325), the feast (323, or the new cook's plea once the feast was
/// spoiled), the altar (326), a quiet prayer, waiting in a grove, or leaving in shame. The sabbat
/// facts (tag 1) decide where it goes: 8 counts the time spent (three spells → card 327), 7 the
/// alarm (5 or more → the cult turns on the party, card 335; 10 or more → card 336).
/// </summary>
// orig: R124+0x165C (199D:000C) matched; tests: CardSmokeTests
//   option 0: action R124+0x1976; hint R124+0x19F4
//   option 1: action R124+0x1A2C; hint R124+0x1A34
//   option 2: action R124+0x1A6C; hint R124+0x1A74
//   option 3: action R124+0x1AAC; hint R124+0x1AEE
//   option 4: action R124+0x1B26; hint R124+0x1B2E
//   option 5: action R124+0x1B66; hint R124+0x1BB2
//   option 6: action R124+0x1BEA; hint R124+0x1CCE
//   option 7: action R124+0x1D06; hint R124+0x1D5E
//   option 8: action R124+0x1D96; hint R124+0x1E30
//   option 9: action R124+0x1E68; hint R124+0x1E86
//   item pop-up: R124+0x1F9C, R124+0x1FD8, R124+0x2014,
//     R124+0x2050
//   saint pop-up: R124+0x208C, R124+0x2114, R124+0x219C,
//     R124+0x2224
//   other: R124+0x1EC2, R124+0x1F64
// DORMANT (generated): option slots 10..19 keep the default handler R124+0x1F64 ("screwed up.") and are never shown.
// DORMANT (generated): option line(s) 9 are never enabled (every assignment sets their flag to 0).
// DORMANT (generated): card_items stays all -1, so no line opens an item pop-up.
// C: the card-engine globals DS:EE70 / DS:EE72 the C sets are the pop-up boxes' drawing flags (R043+0x2ACA, see DIVERGENCE_REVIEW); presentation only, not modelled.
public sealed class HighSabbatField : Card
{
    public override int Number => 322;

    private const int FieldPicture = 144;
    private const int DrunkNoblePicture = 158;
    private const int ServingWenchPicture = 159;
    private const int CooksPicture = 138;

    /// <summary>Location facts of the sabbat (tag 1).</summary>
    private const int FactTag = 1;
    private const int AlarmFact = 7;
    private const int TimeFact = 8;
    private const int FeastSpoiledFact = 9;
    private const int PrayedFact = 0x0A;
    private const int OverFact = 0x0B;
    private const int UnmaskedFact = 0x11;
    private const int BaptismSeenFact = 0x1E;

    private const int CultTurnsOnThem = 0x14F;
    private const int AlarmRaised = 0x150;
    private const int SabbatEnds = 0x147;

    public override int Run(CardContext c)
    {
        // C: both early ways out return the card number without storing it in next_card (and before card_load).
        if (c.Events.FactAnywhere(FactTag, OverFact))
        {
            return 0x14A;
        }
        if (c.Events.FactValueAnywhere(FactTag, TimeFact) >= 3)
        {
            return SabbatEnds;
        }

        Show(c, FieldPicture);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2, 3, 4, 5, 6);
        if (!c.Events.FactAnywhere(FactTag, TimeFact))
        {
            c.Events.Create(0, c.Here, 0, 0x1C, TimeFact, 0, 0, 72, 0, FactTag);
        }
        c.SetCardItems();
        c.SetCardSaints(0x29, 0x35, 0x40, 0x87);
        menu[6] = c.SaintsUsable();
        if (c.Events.FactAnywhere(FactTag, PrayedFact))
        {
            menu[6] = OptionState.Shown;
        }
        if (c.Events.FactAnywhere(FactTag, BaptismSeenFact))
        {
            menu[1] = OptionState.Shown;
        }

        var assured = OptionHint.Saying(SuccessAssured);
        menu.Run(
            new MenuLine(_ => WasteSomeTime(c), OptionHint.Saying(CardTexts.Unknown)),
            // orig: R124+0x1A2C / R124+0x1A34 matched — the water ritual.
            new MenuLine(_ => c.Next = 324, assured),
            // orig: R124+0x1A6C / R124+0x1A74 matched — the broom-riding.
            new MenuLine(_ => c.Next = 325, assured),
            new MenuLine(_ => GoToTheFeast(c, menu), assured),
            // orig: R124+0x1B26 / R124+0x1B2E matched — the altar.
            new MenuLine(_ => c.Next = 326, assured),
            new MenuLine(_ => LeaveTheSabbat(c), assured),
            new MenuLine(_ => Pray(c, menu), OptionHint.Saying(CardTexts.SelectSaint), c.SaintPopupChanceHint),
            new MenuLine(_ => RefuseTheCook(c, menu), assured),
            new MenuLine(_ => HelpTheCook(c, menu), assured),
            new MenuLine(_ => ShowTheFieldAgain(c), assured));

        if (c.Next == CultTurnsOnThem)
        {
            return c.Next;
        }
        c.State.SavedCard = c.Next;
        // C: the original reads the alarm fact twice (once per test); the query has no side effect.
        var alarm = c.Events.FactValueAnywhere(FactTag, AlarmFact);
        if (alarm >= 5 && !c.Events.FactAnywhere(FactTag, UnmaskedFact))
        {
            c.Next = CultTurnsOnThem;
        }
        else if (alarm >= 10)
        {
            c.Next = AlarmRaised;
        }
        return c.Next;
    }

    // orig: loc_fact_value_any(1, 7) as the card's handlers call it matched
    private static int Alarm(CardContext c) => c.Events.FactValueAnywhere(FactTag, AlarmFact);

    // orig: the event_update(−1, −1, 0x1C, fact, n, 1, add) of the card's handlers matched
    private static void AddToFact(CardContext c, int fact, int amount) =>
        c.Events.Update(-1, -1, 0x1C, fact, amount, FactTag, EventUpdate.Add);

    /// <summary>Three hours in a shady grove; then the sabbat may move on without the party.</summary>
    // orig: R124+0x1976 matched
    private void WasteSomeTime(CardContext c)
    {
        AddToFact(c, TimeFact, 1);
        c.PassHours(3);
        c.State.SavedCard = Number;
        if (Alarm(c) >= 5 && !c.Events.FactAnywhere(FactTag, UnmaskedFact))
        {
            c.Next = CultTurnsOnThem;
        }
        else if (Alarm(c) >= 10)
        {
            c.Next = AlarmRaised;
        }
        else if (c.Events.FactValueAnywhere(FactTag, TimeFact) >= 3)
        {
            c.Next = SabbatEnds;
        }
    }

    /// <summary>The cooks (card 323), or, once the feast is spoiled, the new cook asking for help.</summary>
    // orig: R124+0x1AAC matched
    // C: an int function: `return;` without a value, or it falls off its end; ignored.
    private static void GoToTheFeast(CardContext c, CardMenu menu)
    {
        if (!c.Events.FactAnywhere(FactTag, FeastSpoiledFact))
        {
            c.Next = 323;
            return;
        }
        menu.Offer(6, 7, 8);
    }

    /// <summary>Giving up costs Virtue and Divine Favor; the place's arrival card becomes card 317.</summary>
    // orig: R124+0x1B66 matched
    private static void LeaveTheSabbat(CardContext c)
    {
        c.Screen.Text(1);
        c.AddSkill(Who.Everybody, (int)Skill.Virtue, -3);
        c.AddAttribute(Who.Everybody, (int)Attribute.DivineFavor, -20);
        // DIVERGENCE: the C writes locations[cur_loc] without a check; the C# skips it in the wilderness.
        if (c.HerePlace is { } here)
        {
            here.ArrivalCard = 317;
        }
        c.State.CurrentLocation = -1;
        c.Next = 0x0C;
    }

    /// <summary>A saint sends a talkative guest (a drunk noble or a serving wench) who tells of the sabbat.</summary>
    // orig: R124+0x1BEA matched
    // C: an int function that falls off its end without a value (except `return 0` after a cancelled prayer); ignored.
    private static void Pray(CardContext c, CardMenu menu)
    {
        var result = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (result == -1)
        {
            return;
        }
        if (result != 0)
        {
            switch (c.MenuSlot)
            {
                case 0:
                case 1:
                    ShowPicture(c, DrunkNoblePicture);
                    c.Screen.Text(2);
                    break;
                case 2:
                case 3:
                    ShowPicture(c, ServingWenchPicture);
                    c.Screen.Text(3);
                    break;
            }
            ShowPicture(c, FieldPicture);
            c.Screen.Text(5);
        }
        else
        {
            c.Screen.Text(4);
        }
        c.Events.Create(0, c.Here, 0, 0x1C, PrayedFact, 0, 0, 72, 0, FactTag);
        menu[6] = OptionState.Shown;
    }

    // orig: the common tail of R124+0x1D06 / _0746 matched
    private static void BackToTheField(CardContext c, CardMenu menu)
    {
        menu.Offer(0, 0, 1, 2, 3, 4, 5);
        menu[6] = c.SaintsUsable();
    }

    // orig: R124+0x1D06 matched
    private static void RefuseTheCook(CardContext c, CardMenu menu)
    {
        AddToFact(c, AlarmFact, 1);
        BackToTheField(c, menu);
        if (c.Events.FactAnywhere(FactTag, PrayedFact))
        {
            menu[6] = OptionState.Shown;
        }
    }

    /// <summary>Hours of honest cooking calm the cult a little (the prayer line is not checked here, as in the original).</summary>
    // orig: R124+0x1D96 matched
    // C: an int function returning options_block (0); ignored.
    private static void HelpTheCook(CardContext c, CardMenu menu)
    {
        AddToFact(c, TimeFact, 1);
        AddToFact(c, AlarmFact, -1);
        ShowPicture(c, CooksPicture);
        c.Screen.Text(7);
        ShowPicture(c, FieldPicture);
        BackToTheField(c, menu);
    }

    // orig: R124+0x1E68 matched
    private static void ShowTheFieldAgain(CardContext c)
    {
        c.Screen.Text(0);
        c.PassHours(0);
        c.Next = 0x62;
    }
}
