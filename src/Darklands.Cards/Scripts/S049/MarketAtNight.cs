using Darklands.Core.Characters;
using Darklands.Core.Economy;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Marke01": the marketplace at night, closed and watched. The party sneaks past the watch
/// into the stalls (card 0x101), pays the watchmen, uses a potion, calls upon a saint, fights,
/// or slips back to the main street or the side streets.
/// (Ported from the assembly: the original has no C.)
/// </summary>
// orig: R049+0x1BF2 (19F7:0002) asm; tests: CardSmokeTests
// DORMANT: R049+0x29E2 (19F7:0DF2), the init, enables lines 10..19 with the "screwed up."
//   handler R049+0x2A84 and installs the pop-ups below; lines 7..9 (text 0 again, then the
//   outskirts: R049+0x28FC + R049+0x28DE, R049+0x2954 +
//   R049+0x2936, R049+0x29AA + R049+0x298C) have flag 0:
//   they are kept below, hidden.
public sealed class MarketAtNight : Card
{
    public const int CardNumber = 0x16;

    private const string MsgName = "$Marke01";

    private const int Picture = 49, PatrolPicture = 46, WatchPicture = 44, ShadowPicture = 8, TunnelPicture = 16,
        AlleyPicture = 3, GuardPicture = 10, SidePicture = 273;

    private const int CityOutskirts = 0x04, MainStreet = 0x06, MainStreetAtNight = 0x08, SideStreet = 0x09,
        SideStreetAtNight = 0x0A, Dungeon = 0x0D, NightEncounter = 0x3C, Stalls = 0x101;

    /// <summary>Events at the city: the watch at the market alerted (0x17, typed by what happened), turned away (0x1A), the watchmen not to be bought (0x19), the watch alerted (0x12), banned (0x11), a crime reported (0x13).</summary>
    private const int MarketAlert = 0x17, TurnedAway = 0x1A, NoBribes = 0x19, WatchAlerted = 0x12, Banned = 0x11, CrimeReported = 0x13;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        c.PlayMusic(6);
        c.Screen.Show(MsgName, PictureNames.Of(Picture));
        var states = new[] { OptionState.Enabled, OptionState.Enabled, OptionState.Enabled, OptionState.Enabled, OptionState.Enabled,
            OptionState.Enabled, OptionState.Enabled, OptionState.Hidden, OptionState.Hidden, OptionState.Hidden };
        // C: the bribe is reckoned with cur_rep as the card before left it (it is set again only below).
        var perMember = c.HereQuery(2) - c.StandingSeen / 10;
        if (perMember < 4)
        {
            perMember = 4;
        }
        // C: 16-bit products; for any real party the value stays positive (the 0x30 fallback is then dormant).
        var amount = (short)(c.PartySize * perMember * 0x18);
        var bribe = Money.FromPfennigs(amount > 0 ? amount : 0x30);
        c.Variables.Money[0] = bribe;
        if (c.State.Party.Purse < bribe)
        {
            states[1] = OptionState.Shown;
        }
        if (c.EventAtHere(TurnedAway))
        {
            states[0] = OptionState.Shown;
        }
        if (c.EventAtHere(NoBribes))
        {
            states[1] = OptionState.Shown;
        }
        c.StandingSeen = c.Reputation(c.Here);
        c.SetCardItems(0x5F, 0x60, 0x61, 0x64);
        c.SetCardSaints(0x15, 0x23, 0x3F, 0x50);
        states[2] = c.ItemsUsable();
        states[3] = c.SaintsUsable();

        var menu = Menu(c, c.EventAtHere(MarketAlert) ? 0x13 : 0);
        menu.Enable(0, 1, 2, 3, 4, 5, 6, 7, 8, 9);
        for (var k = 0; k <= 9; k++)
        {
            menu[k] = states[k];
        }
        c.Next = Number;
        return menu.Run(
            // orig: R049+0x202A (19F7:043A) + R049+0x1F26 (19F7:0336) R049 seg 19F7 asm
            new MenuLine(chance => SneakIn(c, chance), () => StealthChance(c, 30)),
            // orig: R049+0x2126 (19F7:0536) + R049+0x208C (19F7:049C) R049 seg 19F7 asm
            new MenuLine(_ => PayTheWatch(c), () => BribeHint(c)),
            // orig: R049+0x2368 (19F7:0778) + R049+0x2184 (19F7:0594) R049 seg 19F7 asm
            new MenuLine(chance => UseAPotion(c, chance), OptionHint.Saying(CardTexts.SelectPotion), () => PotionChance(c, c.MenuSlot)),
            // orig: R049+0x24AA (19F7:08BA) + R049+0x23A0 (19F7:07B0) R049 seg 19F7 asm
            new MenuLine(_ => CallUponASaint(c), OptionHint.Saying(CardTexts.SelectSaint), () => SaintChance(c)),
            // orig: R049+0x269A (19F7:0AAA) + R049+0x24E2 (19F7:08F2) R049 seg 19F7 asm
            new MenuLine(() => FightTheWatch(c), OptionHint.Saying(CardTexts.EnterCombat)),
            // orig: R049+0x276C (19F7:0B7C) + R049+0x26D2 (19F7:0AE2) R049 seg 19F7 asm
            new MenuLine(chance => SlipAway(c, chance, () => c.IsDaylight ? MainStreet : MainStreetAtNight, MainStreetAtNight),
                () => StealthChance(c, 50)),
            // orig: R049+0x2872 (19F7:0C82) + R049+0x27CE (19F7:0BDE) R049 seg 19F7 asm
            new MenuLine(chance => SlipAway(c, chance, () => c.IsDaylight ? SideStreet : SideStreetAtNight, SideStreetAtNight, passTimeWhenSeen: true),
                () => LeastStealthChance(c)),
            BackOutside(c, () => OptionHint.ChanceOf(100, c.Leader)),
            BackOutside(c, OptionHint.Saying(SuccessAssured)),
            BackOutside(c, OptionHint.Saying(SuccessAssured)));
    }

    /// <summary>The watch's attention this night: the price events (exactly the first of them) or an alert at the market.</summary>
    private static bool Alert(CardContext c) => c.PriceEvents() == 1 || c.EventAtHere(MarketAlert);

    /// <summary>
    /// A running value from 100: each member in turn lowers it to his Stealth when that is
    /// lower (and is named), and it grows by <paramref name="step"/> after each member; 20 less
    /// when the watch is alert.
    /// </summary>
    // C: the step is added inside the loop (after every member), and no member's status is checked.
    private static OptionHint StealthChance(CardContext c, int step)
    {
        var value = 100;
        for (var i = 0; i < c.PartySize; i++)
        {
            var stealth = c.Skill(i, 0x0F);
            if (stealth < value)
            {
                value = stealth;
                c.Variables.Chosen[0] = i;
            }
            value += step;
        }
        if (Alert(c))
        {
            value -= 20;
        }
        return OptionHint.ChanceOf(Math.Clamp(value, 0, 99), OptionHint.WholeParty);
    }

    /// <summary>The lowest Stealth in the party (that member named) + 55; 20 less when the watch is alert.</summary>
    private static OptionHint LeastStealthChance(CardContext c)
    {
        var value = 100;
        if (c.PartySize > 0)
        {
            var lowest = 100;
            for (var i = 0; i < c.PartySize; i++)
            {
                var stealth = c.Skill(i, 0x0F);
                if (stealth < lowest)
                {
                    lowest = stealth;
                    c.Variables.Chosen[0] = i;
                }
            }
            value = lowest;
        }
        value += 0x37;
        if (Alert(c))
        {
            value -= 20;
        }
        return OptionHint.ChanceOf(Math.Clamp(value, 0, 99), OptionHint.WholeParty);
    }

    /// <summary>In among the stalls; a near miss (under twice the chance, and under 95) is chased off to the side streets; else the watch catches the party.</summary>
    private static void SneakIn(CardContext c, int chance)
    {
        var roll = c.Random(100);
        if (roll <= chance)
        {
            c.PracticeSkill(Who.Everybody, 0x0F, 1, 10, 0);
            c.Next = Stalls;
            return;
        }
        ShowPicture(c, PatrolPicture);
        if (chance * 2 > roll && roll < 0x5F)
        {
            c.Screen.Text(2);
            c.EventHere(MarketAlert, 0, 72);
            c.PassHours(1);
            c.Next = SideStreetAtNight;
        }
        else
        {
            c.Screen.Text(1);
            c.EventHere(TurnedAway, 12);
            c.EventHere(MarketAlert, 0, 32);
            c.State.SavedCard = Stalls;
            c.Next = NightEncounter;
        }
        c.PracticeSkill(Who.Everybody, 0x0F, 0, 10, 0);
    }

    /// <summary>Certain in good standing in a friendly city ("Success is assured."), else no chance.</summary>
    private static OptionHint BribeHint(CardContext c)
    {
        c.StandingSeen = c.HereQuery(0);
        return !c.Hostile() && c.StandingSeen > -10 ? OptionHint.Say(SuccessAssured) : OptionHint.ChanceOf(0, OptionHint.WholeParty);
    }

    private static void PayTheWatch(CardContext c)
    {
        c.StandingSeen = c.HereQuery(0);
        if (c.StandingSeen > -10 && !c.Hostile())
        {
            c.State.Party.Purse = c.State.Party.Purse.Subtract(c.Variables.Money[0]);
            c.Screen.Text(9);
            c.PassHours(0);
            c.Next = Stalls;
            return;
        }
        ShowPicture(c, WatchPicture);
        c.Screen.Text(0x0A);
        c.EventHere(MarketAlert, 0, 72);
        c.State.SavedCard = Stalls;
        c.Next = NightEncounter;
    }

    /// <summary>
    /// The pop-up's potion chances: the first three like the sneaking chance with steps of 70,
    /// 70 and 75 (10 less when alert), the fourth 99. Each names the member using it.
    /// </summary>
    // orig: R049+0x2ABC (19F7:0ECC), R049+0x2B20 (19F7:0F30),
    //   R049+0x2B84 (19F7:0F94), R049+0x2BE8 (19F7:0FF8) R049 seg 19F7 asm
    private static OptionHint PotionChance(CardContext c, int slot)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        if (slot == 3)
        {
            return OptionHint.ChanceOf(99, OptionHint.WholeParty);
        }
        if (slot is < 0 or > 3)
        {
            return OptionHint.Say(CardTexts.ScrewedUp);
        }
        var step = slot == 2 ? 0x4B : 0x46;
        var value = 100;
        for (var i = 0; i < c.PartySize; i++)
        {
            var stealth = c.Skill(i, 0x0F);
            if (stealth < value)
            {
                value = stealth;
            }
            value += step;
        }
        if (Alert(c))
        {
            value -= 10;
        }
        return OptionHint.ChanceOf(Math.Clamp(value, 0, 99), OptionHint.WholeParty);
    }

    private static void UseAPotion(CardContext c, int chance)
    {
        c.TakeItem(c.MenuMember, c.CardItems[c.MenuSlot]);
        if (c.Random(100) > chance)
        {
            c.PracticeSkill(Who.Everybody, 0x0F, 0, 10, 0);
            ShowPicture(c, WatchPicture);
            c.Screen.Text(8);
            c.EventHere(MarketAlert, 0, 72);
            c.State.SavedCard = Stalls;
            c.Next = NightEncounter;
            return;
        }
        switch (c.MenuSlot)
        {
            case 0:
                ShowPicture(c, ShadowPicture);
                c.Screen.Text(3);
                break;
            case 1:
                ShowPicture(c, ShadowPicture);
                c.Screen.Text(4);
                break;
            case 2:
                ShowPicture(c, TunnelPicture);
                c.Screen.Text(5);
                break;
            case 3:
                ShowPicture(c, AlleyPicture);
                c.Screen.Text(6);
                c.EventHere(WatchAlerted, 100);
                break;
        }
        c.PracticeSkill(Who.Everybody, 0x0F, 1, 10, 0);
        ShowPicture(c, PatrolPicture);
        c.Screen.Text(7);
        c.EventHere(WatchAlerted, 16);
        c.EventHere(MarketAlert, c.MenuSlot == 3 ? 1 : 0, 72);
        c.Next = Stalls;
    }

    // orig: R049+0x2C04 (19F7:1014), R049+0x2C8E (19F7:109E),
    //   R049+0x2D18 (19F7:1128), R049+0x2DA2 (19F7:11B2) R049 seg 19F7 asm
    private static OptionHint SaintChance(CardContext c)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        var saint = c.CardSaints[c.MenuSlot];
        var low = c.SaintChanceLow(c.MenuMember, saint);
        return OptionHint.Say(c.ChanceText(low, c.SaintChanceHigh(c.MenuMember, saint)));
    }

    private static void CallUponASaint(CardContext c)
    {
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (answer == -1)
        {
            return;
        }
        if (answer == 0)
        {
            c.PassHours(1);
            ShowPicture(c, TunnelPicture);
            c.Screen.Text(0x0E);
            c.Next = CardNumber;
            return;
        }
        switch (c.MenuSlot)
        {
            case 0:
                ShowPicture(c, TunnelPicture);
                c.Screen.Text(0x0B);
                break;
            case 1:
                c.Screen.Text(0x0C);
                break;
            case 2 or 3:
                ShowPicture(c, ShadowPicture);
                c.Screen.Text(0x0D);
                break;
        }
        c.PassHours(0);
        c.Next = Stalls;
        c.EventHere(MarketAlert, 3, 72);
    }

    /// <summary>
    /// Fights the night watch (3..7 of them): the standing falls by 40 (at −40 or worse the
    /// original rolls a change it ignores), the city bans the party for 6 or 10 days.
    /// </summary>
    private static void FightTheWatch(CardContext c)
    {
        // C: arguments right to left: the one random draw is the watch's number.
        var watchmen = c.Random(5) + 3;
        var result = c.RunBattle(0x1D, -2, 3, 2, watchmen, 0, 3, 1, -1, 0, 0);
        var standing = c.HereQuery(0);
        if (standing <= -40)
        {
            c.ImproveRoll(standing, -3, -9);
        }
        else
        {
            standing -= 40;
        }
        c.SetReputation(c.Here, standing);
        switch (result)
        {
            case 0:
                c.EventHere(MarketAlert, 2, 72);
                ShowPicture(c, AlleyPicture);
                c.Screen.Text(0x10);
                c.PassHours(0);
                c.Next = Stalls;
                break;
            case 1 or 2:
                c.ReportAway();
                c.EventHere(MarketAlert, 0, 72);
                c.Screen.Text(0x11);
                c.PassHours(1);
                c.Next = c.IsDaylight ? SideStreet : SideStreetAtNight;
                break;
            case 3:
                ShowPicture(c, GuardPicture);
                c.Screen.Text(0x12);
                c.PassHours(0);
                c.Next = Dungeon;
                break;
            case 4:
                ShowPicture(c, GuardPicture);
                c.Screen.Text(0x12);
                c.PassHours(3);
                c.Next = Dungeon;
                break;
            // DORMANT: other battle results leave the card as it is.
        }
        c.EventHere(Banned, standing <= -75 ? 240 : 144);
        c.LocationSet(c.Here, 0, standing);
    }

    /// <summary>Slips away to the streets; seen, the party meets the night encounter, which then leads to the night street.</summary>
    private static void SlipAway(CardContext c, int chance, Func<int> street, int streetAfterEncounter, bool passTimeWhenSeen = false)
    {
        if (c.Random(100) <= chance)
        {
            c.PracticeSkill(Who.Everybody, 0x0F, 1, 10, -1);
            c.PassHours(1);
            c.Next = street();
            return;
        }
        ShowPicture(c, SidePicture);
        c.Screen.Text(0x0F);
        c.Events.Renew(-2, c.Here, 0x0A, CrimeReported, 1, 0x32, 0, 0x20, 0, 0);
        if (passTimeWhenSeen)
        {
            c.PassHours(0);
        }
        c.State.SavedCard = streetAfterEncounter;
        c.Next = NightEncounter;
    }

    private static MenuLine BackOutside(CardContext c, Func<OptionHint> hint) =>
        new(() =>
            {
                c.Screen.Text(0);
                c.PassHours(0);
                c.Next = CityOutskirts;
            },
            hint);
}
