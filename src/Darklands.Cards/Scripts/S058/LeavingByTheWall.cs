using Darklands.Core.Characters;
using Darklands.Core.Economy;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Selec01": leaving the city over its outer wall. The party crawls through a sewer, bribes a
/// guard, climbs down a rope or sneaks over the wall after dark (with or without its horses),
/// calls upon a saint, looks for a gate instead, or returns to the streets.
/// </summary>
// orig: R058+0x1E76 (1A1F:0006) matched; tests: CardSmokeTests
public sealed class LeavingByTheWall : Card
{
    public const int CardNumber = 0x3B;

    private const int Picture = 280, ApproachPicture = 4, ChasePicture = 46, DoubtPicture = 16, TowerPicture = 32,
        SewerGratePicture = 42, SewerStuckPicture = 43, WallPicture = 65, WaitPicture = 281, WallWatchedPicture = 296,
        AllWellPicture = 286;

    private const int OverlandMap = 0x0C, Outside = 0x01, LeavingByTheGate = 0x3A, SideStreet = 0x09, SideStreetAtNight = 0x0A;

    /// <summary>Events at a city: the sewer grate known to be stuck, the wall watched, the bribe refused, the party banned, the guards on alert.</summary>
    private const int SewerStuck = 0x10, Banned = 0x11, WallWatched = 0x0F, BribeRefused = 0x22, Unrest = 0x13;

    private const int Strength = 1, Agility = 2, Stealth = 0x0F;

    private const int Rope = 0x3B;

    /// <summary>Item flag of a mount (the horses the party abandons).</summary>
    private const uint Mounts = 0x2000;

    private const int DarkHour = 0x13;

    public override int Number => CardNumber;

    // orig: R058+0x1E76 (1A1F:0006) matched (entry: option flags, the bribe, the saints, the option table)
    // orig: R058+0x2ED4 (1A1F:1064) matched (init helper: enables all 20 lines with "screwed up.", installs the three saint pop-up handlers, clears the slots)
    // DORMANT: R058+0x2F36 (1A1F:10C6): the "screwed up." handler the init installs on all 20 lines; lines 0..9 get their own, 10..19 are enabled but the MSG has no line for them, so it never runs.
    // C: the text has ten option lines (0..9); lines 0, 1 and 2 of the item pop-up do not exist here (no card_items).
    public override int Run(CardContext c)
    {
        Show(c, Picture);
        var lines = new OptionState[10];
        Array.Fill(lines, OptionState.Enabled);
        // C: cur_rep = cur_loc_query(0) is overwritten a few lines further down before anything reads it.
        c.StandingSeen = c.HereQuery(0);
        if (c.EventAtHere(SewerStuck))
        {
            lines[0] = OptionState.Shown;
            lines[1] = OptionState.Shown;
        }
        if (c.EventAtHere(BribeRefused))
        {
            lines[2] = OptionState.Shown;
        }
        if (!c.PartyAllWell())
        {
            lines[3] = OptionState.Shown;
        }
        c.StandingSeen = c.Reputation(c.Here);
        var cost = c.PartySize * (c.HereQuery(2) / 3 + 1) * 2;
        if (c.StandingSeen < 0)
        {
            cost = (100 - c.StandingSeen) / 0x21;
        }
        if (c.EventAtHere(Unrest))
        {
            cost <<= 1;
        }
        var bribe = Money.From(0, 0, cost);
        c.Variables.Money[0] = bribe;
        if (c.State.Party.Purse < bribe)
        {
            // BUG: the line that is closed for lack of money is 1 ("abandon the horses and crawl out through a sewer"),
            //   not the bribe line 2; kept as in the original.
            lines[1] = OptionState.Shown;
        }
        if (c.CountItems(Who.Everybody, Rope) == 0)
        {
            lines[3] = OptionState.Shown;
            lines[4] = OptionState.Shown;
        }
        if (c.EventAtHere(WallWatched))
        {
            lines[2] = OptionState.Shown;
            lines[3] = OptionState.Shown;
            lines[4] = OptionState.Shown;
            lines[5] = OptionState.Shown;
        }
        if (c.CountItemsWithFlags(Who.Everybody, Mounts) != 0)
        {
            // With horses only the "abandon the horses" lines are offered.
            lines[0] = OptionState.Hidden;
            lines[3] = OptionState.Hidden;
            lines[5] = OptionState.Hidden;
        }
        else
        {
            lines[1] = OptionState.Hidden;
            lines[4] = OptionState.Hidden;
            lines[6] = OptionState.Hidden;
        }
        c.SetCardItems();
        c.SetCardSaints(0x15, 0x59, 0x61);
        lines[8] = c.SaintsUsable();

        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2, 3, 4, 5, 6, 7, 8, 9);
        for (var k = 0; k <= 9; k++)
        {
            menu[k] = lines[k];
        }
        c.Next = Number;
        return menu.Run(
            // orig: R058+0x21E8 (1A1F:0378) matched (option 0 action: sewer, with the horses left in the city); chance handler R058+0x22FE (see StrongestChance)
            new MenuLine(chance =>
                {
                    if (CrawlThroughTheSewer(c, chance))
                    {
                        menu[0] = OptionState.Shown;
                    }
                },
                () => StrongestChance(c)),
            // orig: R058+0x237A (1A1F:050A) matched (option 1 action: the same sewer, abandoning the horses); chance handler R058+0x247E (see StrongestChance)
            new MenuLine(chance =>
                {
                    if (CrawlThroughTheSewer(c, chance))
                    {
                        menu[1] = OptionState.Shown;
                    }
                },
                () => StrongestChance(c)),
            // orig: R058+0x24FA (1A1F:068A) matched (option 2 action, see BribeAGuard); hint R058+0x25E0 (1A1F:0770) "You can always encounter the unknown"
            new MenuLine(() =>
                {
                    if (BribeAGuard(c, bribe))
                    {
                        menu[2] = OptionState.Shown;
                    }
                },
                OptionHint.Saying(CardTexts.Unknown)),
            // orig: R058+0x2618 (1A1F:07A8) matched (option 3 action, see ClimbDownARope); chance handler R058+0x2708 (see StealthChance)
            new MenuLine(chance =>
                {
                    if (ClimbDownARope(c, chance))
                    {
                        menu[3] = OptionState.Shown;
                    }
                },
                () => StealthChance(c)),
            // orig: R058+0x274C (1A1F:08DC) matched (option 4 action, see WaitAndClimbDownARope); chance handler R058+0x2876 (see StealthChance)
            new MenuLine(chance =>
                {
                    if (WaitAndClimbDownARope(c, chance))
                    {
                        menu[4] = OptionState.Shown;
                    }
                },
                () => StealthChance(c)),
            // orig: R058+0x28BA (1A1F:0A4A) matched (option 5 action, see SneakOverTheWall); chance handler R058+0x2A7E (see StealthChance)
            new MenuLine(chance =>
                {
                    if (SneakOverTheWall(c, chance, waitsForDark: false))
                    {
                        menu[5] = OptionState.Shown;
                    }
                },
                () => StealthChance(c)),
            // orig: R058+0x2AC2 (1A1F:0C52) matched (option 6 action, see SneakOverTheWall); chance handler R058+0x2CD2 (see StealthChance)
            new MenuLine(chance =>
                {
                    if (SneakOverTheWall(c, chance, waitsForDark: true))
                    {
                        menu[6] = OptionState.Shown;
                    }
                },
                () => StealthChance(c)),
            // orig: R058+0x2D16 (1A1F:0EA6) matched (option 7 action: look for a gate, an hour passes, to the gate card); hint R058+0x2D28 (1A1F:0EB8) "success is assured"
            new MenuLine(() => { c.PassHours(1); c.Next = LeavingByTheGate; }, OptionHint.Saying(SuccessAssured)),
            // orig: R058+0x2D60 (1A1F:0EF0) matched (option 8 action, see CallUponASaint); hint R058+0x2E50 (1A1F:0FE0) "select a saint", pop-up handlers 10fe/1188/1212 (see SaintChance)
            new MenuLine(_ => CallUponASaint(c), OptionHint.Saying(CardTexts.SelectSaint), () => SaintChance(c)),
            // orig: R058+0x2E88 (1A1F:1018) matched (option 9 action: back to the side street for the time of day, no time passes); hint R058+0x2E9C (1A1F:102C) "success is assured"
            new MenuLine(() => c.Next = c.IsDaylight ? SideStreet : SideStreetAtNight, OptionHint.Saying(SuccessAssured)));
    }

    /// <summary>
    /// The strongest member's (Agility + Strength) x 0.8; that member is the one named in the texts
    /// and the owner of the "sewer grate stuck" event.
    /// </summary>
    // orig: R058+0x22FE (1A1F:048E) matched
    // orig: R058+0x247E (1A1F:060E) matched (byte-identical body)
    // C: the handler sets var_chosen1 whenever it is called (before each choice); when nobody counts it keeps its old value.
    private static OptionHint StrongestChance(CardContext c)
    {
        var best = 0;
        for (var member = 0; c.PartySize > member; member++)
        {
            if (c.Attribute(member, Agility) + c.Attribute(member, Strength) > best)
            {
                best = c.Attribute(member, Agility) + c.Attribute(member, Strength);
                c.Variables.Chosen[0] = member;
            }
        }
        return OptionHint.ChanceOf(Math.Clamp((best << 3) / 10, 0, 99), c.Variables.Chosen[0]);
    }

    /// <summary>Pries open the grate: out to the map (the horses are lost), or the grate holds and the way back costs hours.</summary>
    // orig: R058+0x21E8 (1A1F:0378) matched (line 0; it also sets the presentation word DS:EE70)
    // orig: R058+0x237A (1A1F:050A) matched (line 1: the same without DS:EE70)
    // Returns true when the grate holds and the line is closed.
    private static bool CrawlThroughTheSewer(CardContext c, int chance)
    {
        if (c.Random(100) < chance)
        {
            ShowPicture(c, SewerGratePicture);
            c.Screen.Text(1);
            c.RemoveItemsWithFlags(Who.Everybody, Mounts);
            c.Next = OverlandMap;
            return false;
        }
        c.Events.Create(c.Variables.Chosen[0], c.Here, 0, SewerStuck, 3, 0x63, 0, 0x1F4, 0, 0);
        if (c.StandingSeen > 0)
        {
            c.AdjustReputation(c.Here, -2, -4);
        }
        c.PassHours(c.HereQuery(2) / 3);
        ShowPicture(c, SewerStuckPicture);
        c.Screen.Text(2);
        ShowPicture(c, Picture);
        c.Next = CardNumber;
        return true;
    }

    /// <summary>
    /// Pays the guard if the party is in fair standing and not banned; else the guard raises the alarm
    /// (the bribe is refused for two days and the party fights the watch at the outside card).
    /// Returns true when the line is closed.
    /// </summary>
    // orig: R058+0x24FA (1A1F:068A) matched
    // C: the payment is the bribe worked out on arrival; the original never checks the purse here (see the BUG in Run).
    private static bool BribeAGuard(CardContext c, Money bribe)
    {
        if (c.StandingSeen > -0x0A && !c.EventAtHere(Banned))
        {
            c.State.Party.Purse = c.State.Party.Purse.Subtract(bribe);
            ShowPicture(c, ApproachPicture);
            c.Screen.Text(3);
            c.PassHours(1);
            c.RemoveItemsWithFlags(Who.Everybody, Mounts);
            c.Next = OverlandMap;
            return false;
        }
        ShowPicture(c, ChasePicture);
        c.Screen.Text(4);
        c.Events.Create(Who.Everybody, c.Here, 0, BribeRefused, 1, 0x63, 0, 0x30, 0, 0);
        c.PassHours(0);
        c.Next = Outside;
        return true;
    }

    /// <summary>The party's weakest Stealth, halved when the guards are on alert; the member with it is the one named.</summary>
    // orig: R058+0x2708 (1A1F:0898) matched
    // orig: R058+0x2876 (1A1F:0A06) matched (byte-identical body)
    // orig: R058+0x2A7E (1A1F:0C0E) matched (byte-identical body)
    // orig: R058+0x2CD2 (1A1F:0E62) matched (byte-identical body)
    // C: var_chosen1 = (unsigned char)best_member; party_min_skill leaves the member it found in best_member.
    private static OptionHint StealthChance(CardContext c)
    {
        var chance = c.PartyMinSkill(Stealth);
        c.Variables.Chosen[0] = (byte)c.BestMember;
        if (c.EventAtHere(Unrest))
        {
            chance /= 2;
        }
        return OptionHint.ChanceOf(chance, c.Variables.Chosen[0]);
    }

    /// <summary>Sneaks up the wall and lowers the party on a rope (using the rope up); a miss makes the wall watched.</summary>
    // orig: R058+0x2618 (1A1F:07A8) matched
    // Returns true when the line is closed.
    private static bool ClimbDownARope(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            c.TakeItem(Who.Everybody, Rope);
            c.PracticeSkill(Who.Everybody, Stealth, 7, 10, 0);
            ShowPicture(c, TowerPicture);
            c.Screen.Text(5);
            c.RemoveItemsWithFlags(Who.Everybody, Mounts);
            c.PassHours(3);
            c.Next = OverlandMap;
            return false;
        }
        return WallWatchedBack(c);
    }

    /// <summary>The same by night: waits for dark if it is day first (and, unlike the line above, keeps the rope).</summary>
    // orig: R058+0x274C (1A1F:08DC) matched
    // C: the text speaks of tying a rope, but this line (unlike line 3) does not use a rope up.
    // Returns true when the line is closed.
    private static bool WaitAndClimbDownARope(CardContext c, int chance)
    {
        if (c.IsDaylight)
        {
            ShowPicture(c, WaitPicture);
            c.Screen.Text(0x0C);
            c.WaitUntilHour(DarkHour);
        }
        if (c.Random(100) <= chance)
        {
            c.PracticeSkill(Who.Everybody, Stealth, 7, 10, 0);
            ShowPicture(c, ChasePicture);
            c.Screen.Text(5);
            c.PassHours(3);
            c.RemoveItemsWithFlags(Who.Everybody, Mounts);
            c.Next = OverlandMap;
            return false;
        }
        return WallWatchedBack(c);
    }

    /// <summary>A guard spots the party: it slips back into the city, and the wall is watched for a day.</summary>
    private static bool WallWatchedBack(CardContext c)
    {
        ShowPicture(c, WallWatchedPicture);
        c.Screen.Text(6);
        c.PassHours(1);
        c.Events.Renew(Who.Everybody, c.Here, 0, WallWatched, 1, 0x63, 0, 0x18, 0, 0);
        ShowPicture(c, Picture);
        c.Next = CardNumber;
        return true;
    }

    /// <summary>
    /// Climbs over the wall without a rope. A good roll gets everyone down; the roll is also the
    /// bar members' Agility x 3 must reach to avoid a fall (5 times the damage). Line 6 waits for dark
    /// after rolling; line 5 does not wait at all. Returns true when the line is closed.
    /// </summary>
    // orig: R058+0x28BA (1A1F:0A4A) matched (line 5, no wait)
    // orig: R058+0x2AC2 (1A1F:0C52) matched (line 6, waits for dark after rolling)
    // C: the roll l_4 is made first and is used both against the chance and as the fall bar.
    private static bool SneakOverTheWall(CardContext c, int chance, bool waitsForDark)
    {
        var injured = 0;
        var roll = c.Random(100);
        if (waitsForDark && c.IsDaylight)
        {
            ShowPicture(c, WaitPicture);
            c.Screen.Text(0x0C);
            c.WaitUntilHour(DarkHour);
        }
        if (roll <= chance)
        {
            c.PracticeSkill(Who.Everybody, Stealth, 1, 10, 0);
            for (var member = 0; member < c.PartySize; member++)
            {
                if (c.Status(member) == CharacterStatus.Able && c.EffectiveAgility(member) * 3 < roll)
                {
                    c.Injure(member, 0, 0, 5);
                    injured++;
                }
            }
            if (injured == 0)
            {
                ShowPicture(c, AllWellPicture);
                c.Screen.Text(7);
            }
            else if (injured == 1)
            {
                ShowPicture(c, WallPicture);
                c.Screen.Text(8);
            }
            else
            {
                ShowPicture(c, WallPicture);
                c.Screen.Text(9);
            }
            c.RemoveItemsWithFlags(Who.Everybody, Mounts);
            c.PassHours(3);
            c.Next = OverlandMap;
            return false;
        }
        ShowPicture(c, WallWatchedPicture);
        c.Screen.Text(6);
        c.PassHours(1);
        c.PracticeSkill(Who.Everybody, Stealth, 0, 10, 0);
        c.Events.Renew(Who.Everybody, c.Here, 0, WallWatched, 1, 0x63, 0, 0x18, 0, 0);
        ShowPicture(c, Picture);
        c.Next = CardNumber;
        return true;
    }

    /// <summary>The saint pop-up's hint: the member's chance with the saint (it also names the member in the texts).</summary>
    // orig: R058+0x2F6E (1A1F:10FE) matched (saint slot 0)
    // orig: R058+0x2FF8 (1A1F:1188) matched (saint slot 1; byte-identical body)
    // orig: R058+0x3082 (1A1F:1212) matched (saint slot 2; byte-identical body)
    private static OptionHint SaintChance(CardContext c)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        var saint = c.CardSaints[c.MenuSlot];
        var low = c.SaintChanceLow(c.MenuMember, saint);
        return OptionHint.Say(c.ChanceText(low, c.SaintChanceHigh(c.MenuMember, saint)));
    }

    /// <summary>A saint may carry the party over the wall; else the party reconsiders.</summary>
    // orig: R058+0x2D60 (1A1F:0EF0) matched
    // C: the other saint of this card, for text block 13 ("Saint Reinold"), is never named in the code.
    private static void CallUponASaint(CardContext c)
    {
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (answer == -1)
        {
            return;
        }
        if (c.MenuSlot == 0)
        {
            c.NameToSlot(1, 0x15, 0);
        }
        else if (c.MenuSlot == 1)
        {
            c.NameToSlot(1, 0x59, 0);
        }
        else if (c.MenuSlot == 2)
        {
            c.NameToSlot(1, 0x61, 0);
        }
        if (answer != 0)
        {
            ShowPicture(c, ApproachPicture);
            c.Screen.Text(0x0A);
            c.PassHours(1);
            c.Next = OverlandMap;
            return;
        }
        ShowPicture(c, DoubtPicture);
        c.Screen.Text(0x0B);
        c.PassHours(1);
        ShowPicture(c, Picture);
        c.Next = CardNumber;
    }
}
