using Darklands.Core.Economy;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$chall00": the city watch recognises the party as wanted and charges. The party fights,
/// runs down a side street, talks its way out, offers a bribe, throws an alchemical bomb,
/// implores a saint, or surrenders. (Ported from the assembly: the original has no C.)
/// </summary>
// orig: R044+0x099E (18D1:000E) asm (no C source; ported from the disassembly); tests: CardSmokeTests
public sealed class ChallengedByTheWatch : Card
{
    public const int CardNumber = 0x01;

    private const int Picture = 10, CrowdPicture = 273, WeaponPicture = 8, ArrestPicture = 236, BlastPicture = 20,
        SmokePicture = 19, GrovePicture = 47, WatchPicture = 45, RooftopPicture = 290, StairsPicture = 13,
        AlleyPicture = 3, ChasePicture = 46;

    /// <summary>Events at a city: a crime reported, the watch alerted after a fight, banned.</summary>
    private const int CrimeReported = 0x13, WatchAlerted = 0x12, Banned = 0x11;

    private const int MainStreet = 0x06, MainStreetAtNight = 0x08, SideStreet = 0x09, SideStreetAtNight = 0x0A,
        OverlandMap = 0x0C, Dungeon = 0x0D, Chase = 0x7A, Selection = 0x3A, Docks = 0x21, DocksAtNight = 0x22;

    public override int Number => CardNumber;

    // orig: R044+0x099E (18D1:000E) asm (entry: bribe amount, option states, the option table and the menu loop)
    // orig: R044+0x1362 (18D1:09D2) asm (init helper: enables all 20 option lines, installs the 4 bomb and 4 saint pop-up handlers for the 5 member rows and clears the 4 item and 4 saint slots)
    // DORMANT: R044+0x1404 (18D1:0A74): the init helper points card-option-handler[0..19] at this "screwed up." default; lines 7..19 stay enabled but the card's MSG has no line for them and the menu cannot select them, so it never runs.
    // DORMANT: the C# BombChance branch for slot outside 0..3 ("screwed up.") has no function behind it: item-handler-table and saint-handler-table hold 4 handlers per member row, all four slots used by the card's 4 items and 4 saints.
    // C: item-handler-table/saint-handler-table are set identically for all 5 member rows; the handlers read the member from menu-owner-member, so the C# keeps one handler per slot. The saint handlers 0c58/0ce2/0d6c/0df6 have byte-identical bodies.
    // checked against asm: option-to-handler pairing (card-option-handler table at 18d1:017f and the switch at 18d1:0254), the bribe amount and the bribe option state; the bodies of the other functions were not re-read in this annotation pass.
    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.PassHours(1);
        var florins = c.HereQuery(2) / 2;
        if (c.Reputation(c.Here) < 0)
        {
            florins += Math.Abs(c.Reputation(c.Here) / 20);
        }
        var bribe = Money.From(florins <= 0 ? 1 : florins, 0, 0);
        c.Variables.Money[0] = bribe;
        var canBribe = c.State.Party.Purse >= bribe && c.State.PreviousCard is SideStreet or SideStreetAtNight;
        c.StandingSeen = c.Reputation(c.Here);
        // Original bug: $Money1 is set again to one pfennig, so the text offers the guards 1 pfennig.
        c.Variables.Money[0] = Money.From(0, 0, 1);
        c.SetCardItems(0x60, 0x65, 0x61, 0x68);
        c.SetCardSaints(0x15, 0x36, 0x3D, 0x72);

        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2, 3, 4, 5, 6);
        menu[3] = canBribe ? OptionState.Enabled : OptionState.Shown;
        menu[4] = c.ItemsUsable();
        menu[5] = c.SaintsUsable();
        c.Next = Number;
        return menu.Run(
            // orig: R044+0x0C56 (18D1:02C6) asm (option 0 hint "enter combat"; action R044+0x0C50 (18D1:02C0) is a stub that calls R044+0x1810, see FightTheWatch)
            new MenuLine(() => FightTheWatch(c), OptionHint.Saying(CardTexts.EnterCombat)),
            // orig: R044+0x0CA0 (18D1:0310) asm (option 1 hint (unknown text); action R044+0x0C8E (18D1:02FE): one hour passes, next card 0x7A (the chase))
            new MenuLine(() => { c.PassHours(1); c.Next = Chase; }, OptionHint.Saying(CardTexts.Unknown)),
            // orig: R044+0x0CD8 (18D1:0348) asm (option 2 action, see TalkYourWayOut; chance handler R044+0x0E18, see TalkChance)
            new MenuLine(chance => TalkYourWayOut(c, chance), () => TalkChance(c)),
            // orig: R044+0x0E80 (18D1:04F0) asm (option 3 action, see OfferABribe; chance handler R044+0x0F0C, see BribeChance)
            new MenuLine(chance => OfferABribe(c, chance), () => BribeChance(c, bribe)),
            // orig: R044+0x10CA (18D1:073A) asm (option 4 hint "select a potion"; action R044+0x0F62, see ThrowABomb; pop-up handlers 0aac/0b1e/0b90/0c02, see BombChance)
            new MenuLine(chance => ThrowABomb(c, chance), OptionHint.Saying(CardTexts.SelectPotion), () => BombChance(c, c.MenuSlot)),
            // orig: R044+0x12AC (18D1:091C) asm (option 5 hint "select a saint"; action R044+0x1102, see ImploreASaint; pop-up handlers 0c58/0ce2/0d6c/0df6, see SaintChance)
            new MenuLine(_ => ImploreASaint(c), OptionHint.Saying(CardTexts.SelectSaint), () => SaintChance(c)),
            // orig: R044+0x132A (18D1:099A) asm (option 6 hint "success assured"; action R044+0x12E4, see Surrender)
            new MenuLine(() => Surrender(c), OptionHint.Saying(SuccessAssured)));
    }

    /// <summary>The leader's Speak Common and Charisma plus the standing (less 25 after a reported crime).</summary>
    // orig: R044+0x0E18 (18D1:0488) asm
    private static OptionHint TalkChance(CardContext c)
    {
        var chance = c.Skill(c.Leader, 0x0A) + c.Attribute(c.Leader, 5);
        if (c.EventAtHere(CrimeReported))
        {
            chance -= 25;
        }
        chance = Math.Clamp(c.Reputation(c.Here) + chance, 0, 100);
        c.Variables.Chosen[0] = c.Leader;
        return OptionHint.ChanceOf(chance, c.Leader);
    }

    /// <summary>
    /// Talks the guards round in one of three ways (the third one twirls the leader's weapon,
    /// named in the text; unarmed it is one of the other two); else the guards attack.
    /// </summary>
    // orig: R044+0x0CD8 (18D1:0348) asm
    private static void TalkYourWayOut(CardContext c, int chance)
    {
        if (c.Random(100) > chance)
        {
            ShowPicture(c, ArrestPicture);
            c.Screen.Text(8);
            c.PracticeSkill(c.Leader, 0x0A, 0, 10, 0);
            FightTheWatch(c);
            return;
        }
        c.PracticeSkill(c.Leader, 0x0A, 1, 10, 0);
        switch (c.Random(3))
        {
            case 0:
                c.Screen.Text(5);
                break;
            case 1:
                ShowPicture(c, CrowdPicture);
                c.Screen.Text(6);
                break;
            default:
                var weapon = c.EquippedItem(c.Leader, 0);
                if (weapon == -1)
                {
                    weapon = 7;
                }
                c.NameToSlot(0, weapon, 0);
                if (weapon != 0)
                {
                    ShowPicture(c, WeaponPicture);
                    c.Screen.Text(7);
                }
                else if (c.Random(2) == 0)
                {
                    ShowPicture(c, CrowdPicture);
                    c.Screen.Text(6);
                }
                else
                {
                    c.Screen.Text(5);
                }
                break;
        }
        c.PassHours(1);
        c.Next = c.State.PreviousCard;
    }

    /// <summary>The standing, the leader's Charisma and a twelfth of the bribe in pfennigs.</summary>
    // orig: R044+0x0F0C (18D1:057C) asm
    private static OptionHint BribeChance(CardContext c, Money bribe)
    {
        var share = (int)(bribe.TotalPfennigs / 12);
        var chance = c.Reputation(c.Here) + c.Attribute(c.Leader, 5) + share;
        // C: limit-to-0-100(chance) (R029+0x4F38 matched: above 100 gives 100, below 0 gives 0) = Math.Clamp(chance, 0, 100).
        return OptionHint.ChanceOf(Math.Clamp(chance, 0, 100), OptionHint.WholeParty);
    }

    /// <summary>
    /// Offers the bribe. Original bugs: a roll under the chance makes the guards refuse (and
    /// fight), the other one makes them accept; and no money is ever paid.
    /// </summary>
    // orig: R044+0x0E80 (18D1:04F0) asm
    private static void OfferABribe(CardContext c, int chance)
    {
        if (c.Random(100) < chance)
        {
            ShowPicture(c, Picture);
            c.Screen.Text(0x0A);
            FightTheWatch(c);
            return;
        }
        c.PassHours(1);
        ShowPicture(c, ArrestPicture);
        c.Screen.Text(9);
        c.Next = c.State.PreviousCard;
    }

    /// <summary>Each bomb has its own base, lower in bigger cities and 20 lower while the watch is alerted; the first three add the party's Agility and Stealth.</summary>
    // orig: R044+0x143C (18D1:0AAC) asm (bomb slot 0: base 4, adds Agility and Stealth)
    // orig: R044+0x14AE (18D1:0B1E) asm (slot 1: base 0x10, adds Agility and Stealth)
    // orig: R044+0x1520 (18D1:0B90) asm (slot 2: base 0x0A, adds Agility and Stealth)
    // orig: R044+0x1592 (18D1:0C02) asm (slot 3: base 0x18, nothing added)
    private static OptionHint BombChance(CardContext c, int slot)
    {
        int[] bases = [4, 0x10, 0x0A, 0x18];
        if (slot is < 0 or > 3)
        {
            return OptionHint.Say(CardTexts.ScrewedUp);
        }
        var chance = (bases[slot] - c.HereQuery(2)) * 5;
        if (c.EventAtHere(WatchAlerted))
        {
            chance -= 20;
        }
        if (slot < 3)
        {
            var agility = c.PartyAverageEffectiveAgility();
            chance += (agility + c.PartyAverageSkill(0x0F)) / 2;
        }
        c.Variables.Chosen[0] = c.MenuMember;
        return OptionHint.ChanceOf(Math.Clamp(chance, 0, 100), c.MenuMember);
    }

    /// <summary>Throws the bomb. Original bug: a roll under the chance fails, the other one works.</summary>
    // orig: R044+0x0F62 (18D1:05D2) asm
    private static void ThrowABomb(CardContext c, int chance)
    {
        c.TakeItem(c.MenuMember, c.CardItems[c.MenuSlot]);
        if (c.Random(100) < chance)
        {
            int[] bombs = [0x60, 0x65, 0x61, 0x68];
            if (c.MenuSlot is >= 0 and <= 3)
            {
                c.NameToSlot(0, bombs[c.MenuSlot], 0);
            }
            ShowPicture(c, ArrestPicture);
            c.Screen.Text(0x0E);
            FightTheWatch(c);
            return;
        }
        c.AdjustReputation(c.Here, 0, -3);
        switch (c.MenuSlot)
        {
            case 0 or 1:
                ShowPicture(c, BlastPicture);
                c.Screen.Text(0x0B);
                break;
            case 2:
                ShowPicture(c, SmokePicture);
                c.Screen.Text(0x0C);
                break;
            case 3:
                ShowPicture(c, Picture);
                c.Screen.Text(0x0D);
                break;
        }
        c.PassHours(1);
        c.Next = c.IsDaylight ? SideStreet : SideStreetAtNight;
    }

    // orig: R044+0x15E8 (18D1:0C58) asm (saint slot 0)
    // orig: R044+0x1672 (18D1:0CE2) asm (saint slot 1)
    // orig: R044+0x16FC (18D1:0D6C) asm (saint slot 2)
    // orig: R044+0x1786 (18D1:0DF6) asm (saint slot 3)
    private static OptionHint SaintChance(CardContext c)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        var saint = c.CardSaints[c.MenuEntrySlot];
        var low = c.SaintChanceLow(c.MenuMember, saint);
        return OptionHint.Say(c.ChanceText(low, c.SaintChanceHigh(c.MenuMember, saint)));
    }

    // orig: R044+0x1102 (18D1:0772) asm
    private static void ImploreASaint(CardContext c)
    {
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuEntrySlot]);
        if (answer == -1)
        {
            return;
        }
        if (answer == 0)
        {
            ShowPicture(c, ArrestPicture);
            c.Screen.Text(0x0F);
            FightTheWatch(c);
            return;
        }
        c.PassHours(1);
        switch (c.MenuSlot)
        {
            case 0:
                if (c.StandingSeen > -10)
                {
                    c.AdjustReputation(c.Here, 3, 9);
                }
                else
                {
                    c.AdjustReputation(c.Here, -3, -9);
                }
                ShowPicture(c, GrovePicture);
                c.Screen.Text(0x10);
                c.Next = OverlandMap;
                break;
            case 1 or 2:
                c.NameToSlot(1, c.MenuSlot == 1 ? 0x36 : 0x3D, 0);
                c.AdjustReputation(c.Here, 2, 6);
                ShowPicture(c, WatchPicture);
                c.Screen.Text(0x11);
                c.Next = c.State.PreviousCard;
                break;
            case 3:
                if (c.StandingSeen > -10)
                {
                    c.AdjustReputation(c.Here, 2, 6);
                }
                else
                {
                    c.AdjustReputation(c.Here, -2, -6);
                }
                ShowPicture(c, RooftopPicture);
                c.Screen.Text(0x12);
                c.Next = c.IsDaylight ? SideStreet : SideStreetAtNight;
                break;
        }
    }

    // orig: R044+0x12E4 (18D1:0954) asm
    private static void Surrender(CardContext c)
    {
        ShowPicture(c, StairsPicture);
        c.Screen.Text(4);
        c.PassHours(3);
        c.Next = Dungeon;
    }

    /// <summary>
    /// The fight with the watch: the kind of guards depends on where the party came from,
    /// their number on the party's size. The standing falls, the watch stays alerted for
    /// longer in small cities, and the city bans the party for 5 or 10 days.
    /// </summary>
    // orig: R044+0x1810 (18D1:0E80) asm
    private static void FightTheWatch(CardContext c)
    {
        var from = c.State.PreviousCard;
        var guards = from switch
        {
            MainStreet or MainStreetAtNight => 0x13,
            SideStreet or SideStreetAtNight => 0x11,
            Docks or DocksAtNight => 0x15,
            _ => 0x56,
        };
        var count = Math.Clamp(c.Random(7) + 2, c.PresentCount(), 7);
        var result = c.RunBattle(guards, from + c.Here, 3, 1, count, 0, 2, 1, -1, 0, 0);
        c.AdjustReputation(c.Here, -15, -25);
        var alerted = 2000 / c.HereQuery(2);
        switch (result)
        {
            case 0:
                ShowPicture(c, AlleyPicture);
                c.Screen.Text(1);
                c.ExtendEventHere(WatchAlerted, alerted);
                c.PassHours(0);
                c.Next = c.State.PreviousCard == Selection ? OverlandMap : c.State.PreviousCard;
                break;
            case 1:
                c.ReportAway();
                ShowPicture(c, ChasePicture);
                c.Screen.Text(2);
                c.ExtendEventHere(WatchAlerted, alerted);
                c.PassHours(1);
                c.Next = c.State.PreviousCard == Selection ? OverlandMap : c.IsDaylight ? SideStreet : SideStreetAtNight;
                break;
            case 2:
                c.ReportAway();
                ShowPicture(c, ChasePicture);
                c.Screen.Text(3);
                c.PassHours(1);
                c.Next = c.State.PreviousCard;
                break;
            case 3 or 4:
                ShowPicture(c, StairsPicture);
                c.Screen.Text(4, TextFrame.BlankPage);
                c.PassHours(3);
                c.Next = Dungeon;
                break;
        }
        c.ExtendEventHere(Banned, c.StandingSeen <= -75 ? 240 : 120);
    }
}
