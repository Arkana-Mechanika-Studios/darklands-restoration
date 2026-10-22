using Darklands.Core.Characters;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Selec00": leaving the city by day. The party walks, sneaks, throws an alchemical
/// distraction, calls upon a saint or fights its way past the gate guards, goes to look at the
/// wall instead, or stays.
/// </summary>
// orig: R058+0x0E24 (191A:0004) matched; tests: CardSmokeTests
public sealed class LeavingByTheGate : Card
{
    public const int CardNumber = 0x3A;

    private const int Picture = 266, ApproachPicture = 4, ChasePicture = 46, BoomPicture = 20, ExplosionPicture = 189,
        FumesPicture = 333, EludePicture = 59, BanditsPicture = 177, StairsPicture = 13, DoubtPicture = 16;

    private const int OverlandMap = 0x0C, Dungeon = 0x0D, Outside = 0x01, TheWall = 0x3B;

    /// <summary>Events at a city: the gate fought, (the party seen banned), more watchful guards.</summary>
    private const int Banned = 0x11, GateStormed = 0x12, Unrest = 0x13, PotionPanic = 0x0E;

    private const int Agility = 2, Streetwise = 0x10;

    public override int Number => CardNumber;

    // orig: R058+0x0E24 (191A:0004) matched (entry: the standing, the items and saints of the pop-ups, the option table)
    // orig: R058+0x1ADC (191A:0CBC) matched (init helper: enables all 20 lines with "screwed up.", installs the three potion and three saint pop-up handlers, clears the slots)
    // DORMANT: R058+0x1B66 (191A:0D46): the "screwed up." handler the init installs on all 20 lines; lines 0..6 get their own and 7..19 are enabled but the MSG has no line for them, so it never runs.
    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.StandingSeen = c.Reputation(c.Here);
        c.SetCardItems(0x5F, 0x65, 0x61);
        c.SetCardSaints(0x15, 0x59, 0x61);

        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2, 3, 4, 5, 6);
        menu[2] = c.ItemsUsable();
        menu[3] = c.SaintsUsable();
        c.Next = Number;
        return menu.Run(
            // orig: R058+0x1290 (191A:0470) matched (option 0 action, see WalkOut); hint R058+0x1306 (191A:04E6) "You can always encounter the unknown"
            new MenuLine(() => WalkOut(c), OptionHint.Saying(CardTexts.Unknown)),
            // orig: R058+0x133E (191A:051E) matched (option 1 action, see SneakOut); chance handler R058+0x141E (see SneakChance)
            new MenuLine(chance => SneakOut(c, chance), () => SneakChance(c)),
            // orig: R058+0x1496 (191A:0676) matched (option 2 action, see ThrowAPotion); hint R058+0x1666 (191A:0846) "select a potion", pop-up handlers 0d7e/0de8/0e50 (see PotionChance)
            new MenuLine(chance => ThrowAPotion(c, chance), OptionHint.Saying(CardTexts.SelectPotion), () => PotionChance(c, c.MenuSlot)),
            // orig: R058+0x169E (191A:087E) matched (option 3 action, see CallUponASaint); hint R058+0x17A2 (191A:0982) "select a saint", pop-up handlers 0eb8/0f42/0fcc (see SaintChance)
            new MenuLine(_ => CallUponASaint(c), OptionHint.Saying(CardTexts.SelectSaint), () => SaintChance(c)),
            // orig: R058+0x17DA (191A:09BA) matched (option 4 action, see FightTheGuards); hint R058+0x1A24 (191A:0C04) "enter combat"
            new MenuLine(() => FightTheGuards(c), OptionHint.Saying(CardTexts.EnterCombat)),
            // orig: R058+0x1A5C (191A:0C3C) matched (option 5 action: to the wall card); hint R058+0x1A64 (191A:0C44) "success is assured"
            new MenuLine(() => c.Next = TheWall, OptionHint.Saying(SuccessAssured)),
            // orig: R058+0x1A9C (191A:0C7C) matched (option 6 action: back to the card before); hint R058+0x1AA4 (191A:0C84) "success is assured"
            new MenuLine(() => c.Next = c.State.PreviousCard, OptionHint.Saying(SuccessAssured)));
    }

    /// <summary>Simply walks out: unless the city is hostile or on alert (then only 60 times in 100) nothing happens.</summary>
    // orig: R058+0x1290 (191A:0470) matched
    // C: the roll is only made when the guards are on alert (&& short-circuits) and the city is not hostile.
    private static void WalkOut(CardContext c)
    {
        if (!c.Hostile() && (!c.EventAtHere(Unrest) || c.Random(100) >= 0x28))
        {
            c.PassHours(1);
            c.Next = OverlandMap;
            return;
        }
        ShowPicture(c, ChasePicture);
        c.Screen.Text(1);
        BrawlAtTheGate(c);
    }

    /// <summary>The party's mean Agility + Streetwise (summed over able members, divided by the whole party); less when the guards are on alert.</summary>
    // orig: R058+0x141E (191A:05FE) matched
    private static OptionHint SneakChance(CardContext c)
    {
        var sum = 0;
        for (var member = 0; member < c.PartySize; member++)
        {
            if (c.Status(member) == CharacterStatus.Able)
            {
                sum += c.Attribute(member, Agility) + c.Skill(member, Streetwise);
            }
        }
        var chance = sum / c.PartySize;
        if (c.EventAtHere(Unrest))
        {
            chance -= 0x19;
        }
        return OptionHint.ChanceOf(Math.Clamp(chance, 0, 100), OptionHint.WholeParty);
    }

    /// <summary>Sneaks out among the crowd: failing draws the guards' notice (a fight when the city is hostile).</summary>
    // orig: R058+0x133E (191A:051E) matched
    private static void SneakOut(CardContext c, int chance)
    {
        if (c.Random(100) > chance)
        {
            c.PracticeSkill(Who.Everybody, Streetwise, 0, 5, 0);
            if (c.Hostile())
            {
                c.Next = Outside;
                return;
            }
            c.PassHours(1);
            ShowPicture(c, ChasePicture);
            c.Screen.Text(1);
            ShowPicture(c, Picture);
            c.AdjustReputation(c.Here, -1, -4);
            return;
        }
        ShowPicture(c, ApproachPicture);
        c.Screen.Text(2);
        c.PracticeSkill(Who.Everybody, Streetwise, 1, 5, 0);
        c.PassHours(1);
        c.Next = OverlandMap;
    }

    /// <summary>Potion chances by pop-up slot: the first is the strongest; all suffer after a panic or a fight.</summary>
    // orig: R058+0x1B9E (191A:0D7E) matched (slot 0: 2 x agility + 55)
    // orig: R058+0x1C08 (191A:0DE8) matched (slot 1: agility + 50)
    // orig: R058+0x1C70 (191A:0E50) matched (slot 2: agility + 40)
    // C: a slot above 2 is empty and has no handler installed; it is reported as "screwed up." (as the default handler would).
    private static OptionHint PotionChance(CardContext c, int slot)
    {
        var agility = c.PartyAverageEffectiveAgility();
        int chance;
        switch (slot)
        {
            case 0:
                chance = (agility << 1) + 0x37;
                break;
            case 1:
                chance = agility + 0x32;
                break;
            case 2:
                chance = agility + 0x28;
                break;
            default:
                return OptionHint.Say(CardTexts.ScrewedUp);
        }
        if (c.EventAtHere(PotionPanic))
        {
            chance = (chance << 1) / 3;
        }
        if (c.EventAtHere(GateStormed))
        {
            chance = (chance << 1) / 3;
        }
        return OptionHint.ChanceOf(Math.Clamp(chance, 0, 100), c.MenuMember);
    }

    /// <summary>Uses up the potion; a miss leaves the guards on alert, a hit gets the party out and costs some standing.</summary>
    // orig: R058+0x1496 (191A:0676) matched
    private static void ThrowAPotion(CardContext c, int chance)
    {
        c.TakeItem(c.MenuMember, c.CardItems[c.MenuSlot]);
        if (c.Random(100) > chance)
        {
            ShowPicture(c, ExplosionPicture);
            c.Screen.Text(6);
            ShowPicture(c, Picture);
            c.Events.Renew(-2, c.Here, 0, Unrest, 1, 0x63, 0, 0x18, 0, 0);
            c.PassHours(0);
            c.Next = CardNumber;
            return;
        }
        ShowPicture(c, BoomPicture);
        switch (c.MenuSlot)
        {
            case 0:
                ShowPicture(c, FumesPicture);
                c.Screen.Text(3);
                ShowPicture(c, Picture);
                c.AdjustReputation(c.Here, -1, -4);
                break;
            case 1:
                ShowPicture(c, ExplosionPicture);
                c.Screen.Text(4);
                ShowPicture(c, Picture);
                c.AdjustReputation(c.Here, 0, -3);
                break;
            case 2:
                ShowPicture(c, FumesPicture);
                c.Screen.Text(5);
                ShowPicture(c, Picture);
                c.AdjustReputation(c.Here, 0, -2);
                break;
        }
        c.PassHours(1);
        c.Next = OverlandMap;
    }

    /// <summary>The saint pop-up's hint: the member's chance with the saint (it also names the member in the texts).</summary>
    // orig: R058+0x1CD8 (191A:0EB8) matched (saint slot 0)
    // orig: R058+0x1D62 (191A:0F42) matched (saint slot 1; byte-identical body)
    // orig: R058+0x1DEC (191A:0FCC) matched (saint slot 2; byte-identical body)
    private static OptionHint SaintChance(CardContext c)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        var saint = c.CardSaints[c.MenuSlot];
        var low = c.SaintChanceLow(c.MenuMember, saint);
        return OptionHint.Say(c.ChanceText(low, c.SaintChanceHigh(c.MenuMember, saint)));
    }

    /// <summary>A saint may lift the party out of the city; else the party reconsiders.</summary>
    // orig: R058+0x169E (191A:087E) matched
    private static void CallUponASaint(CardContext c)
    {
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (answer == -1)
        {
            return;
        }
        switch (c.MenuSlot)
        {
            case 0:
                c.NameToSlot(1, 0x15, 0);
                break;
            case 1:
                c.NameToSlot(1, 0x59, 0);
                break;
            default:
                c.NameToSlot(1, 0x61, 0);
                break;
        }
        if (answer != 0)
        {
            ShowPicture(c, ApproachPicture);
            c.Screen.Text(7);
            c.PassHours(1);
            if (c.StandingSeen <= -0x0A)
            {
                c.AdjustReputation(c.Here, -2, -8);
            }
            else
            {
                c.AdjustReputation(c.Here, 2, 8);
            }
            c.Next = OverlandMap;
            return;
        }
        ShowPicture(c, DoubtPicture);
        c.Screen.Text(8);
        ShowPicture(c, Picture);
        c.PassHours(1);
        c.Next = CardNumber;
    }

    /// <summary>
    /// Attacks the guards with the numbers the party's lore calls for. The standing falls by 40
    /// (when it is already -40 or worse the original rolls a change it then ignores); the party is
    /// banned from the city for a while.
    /// </summary>
    // orig: R058+0x17DA (191A:09BA) matched
    // C: unlike R058+0x1014 this one never takes the "gate already stormed" fixed numbers, and a won
    //   or fled fight (results 0 and 1) does not extend the gate-stormed event.
    private static void FightTheGuards(CardContext c)
    {
        var result = RandomGuards(c);
        var standing = SettleStanding(c, result);
        AfterTheFight(c, result, standing, extendGateStormed: false);
    }

    /// <summary>
    /// The fight at the gate after the party was seen sneaking out: against the gate's stronger
    /// guards when it has been stormed before, else against the usual numbers.
    /// </summary>
    // orig: R058+0x1014 (191A:01F4) matched (reached through WalkOut only)
    private static void BrawlAtTheGate(CardContext c)
    {
        var result = c.EventAtHere(GateStormed)
            ? c.RunBattle(0x2B, c.Here + 0x70, 3, 5, 7, 0, 5, 1, -1, 0, 0)
            : RandomGuards(c);
        var standing = SettleStanding(c, result);
        AfterTheFight(c, result, standing, extendGateStormed: true);
    }

    private static int RandomGuards(CardContext c)
    {
        // C: arguments are evaluated right to left, so the sergeants' draw comes before the archers'.
        var sergeants = c.Random(3);
        var archers = c.Random(4);
        return c.RunBattle(0x2B, c.Here + 0x70, 3, c.PartyLoreLevel() / 4 + 1, archers + c.PartyLoreLevel() / 4 + 3, 0, sergeants + 1, 1, -1, 0, 0);
    }

    /// <summary>The standing falls by 40 and the guards stay on alert; returns the new standing.</summary>
    private static int SettleStanding(CardContext c, int result)
    {
        // orig: R058+0x17DA
        // BUG: a second fight at the gate does not lower the standing further: the standing is the one read when the
        // card was entered (R058+0x0E24), and after a fight that keeps the party at the gate the menu runs again with
        // it, so the second fight sets it to that value - 40 again (R058+0x17DA).
        var standing = c.StandingSeen;
        if (standing <= -0x28)
        {
            c.ImproveRoll(standing, -3, -9);
        }
        else
        {
            standing -= 0x28;
        }
        c.SetReputation(c.Here, standing);
        if (result < 2)
        {
            c.Events.Renew(-2, c.Here, 0x1E, GateStormed, 1, 0x64, 0, 0x24, 0, 0);
        }
        c.Events.Renew(-2, c.Here, 0, Unrest, 1, 0x63, 0, 0x18, 0, 0);
        return standing;
    }

    private static void AfterTheFight(CardContext c, int result, int standing, bool extendGateStormed)
    {
        switch (result)
        {
            case 0:
            case 1:
                if (extendGateStormed)
                {
                    c.ExtendEventHere(GateStormed, 0x78);
                }
                ShowPicture(c, EludePicture);
                c.Screen.Text(9);
                c.PassHours(1);
                c.Next = OverlandMap;
                break;
            case 2:
                c.ReportAway();
                ShowPicture(c, EludePicture);
                c.Screen.Text(0x0A);
                ShowPicture(c, Picture);
                c.PassHours(1);
                c.Next = CardNumber;
                break;
            case 3:
                ShowPicture(c, BanditsPicture);
                c.Screen.Text(0x0B);
                ShowPicture(c, Picture);
                c.PassHours(0);
                c.Next = CardNumber;
                break;
            case 4:
                ShowPicture(c, StairsPicture);
                c.Screen.Text(0x0C);
                ShowPicture(c, Picture);
                c.PassHours(3);
                c.Next = Dungeon;
                break;
        }
        c.EventHere(Banned, standing <= -0x4B ? 0xF0 : 0x78);
    }
}
