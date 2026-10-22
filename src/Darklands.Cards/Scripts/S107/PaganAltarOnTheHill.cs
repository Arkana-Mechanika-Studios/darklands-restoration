using Darklands.Core.Characters;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$McGuf06": a pagan stone circle on a hillock; storm and wolf-howls rise as the party nears
/// the altar. It can overturn the stones (or take the quest's item from beneath them), pray,
/// cleanse the altar with a potion, or leave. Disturbing the altar calls up the place's
/// guardians — wolves, beasts, the Wild Hunter or tree-giants, by the party's seed and the place.
/// </summary>
// orig: R107+0x1962 (19CE:0002) matched; tests: CardSmokeTests
// orig: R107+0x2A36 (19CE:10D6) matched (registration: item pop-ups R107+0x2B04/11be/11d8/11f2 name the member; saint pop-ups do not)
// DORMANT: R107+0x2D26 is an empty function nothing calls.
public sealed class PaganAltarOnTheHill : Card
{
    private const int CirclePicture = 61;
    private const int StormPicture = 139;

    public override int Number => 254;

    public override int Run(CardContext c)
    {
        Show(c, CirclePicture);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Set(OptionState.Enabled, 0, 1, 2, 5);
        if (c.Events.QuestAt(10, c.Here))
        {
            // C: event_find does not set last_event; event_f2e_of(last_event) reads the stale global.
            _ = c.Events.Find(8, -1, c.Here, -1, 10, -1);
            c.NameToSlot(0, c.EventDetail(c.LastEvent), 0);
            c.Variables.Texts[0] = "touch the " + c.Variables.Named[0];
            menu.Block = 20;
        }
        else
        {
            c.Variables.Texts[0] = "turn the stone";
            c.Variables.Named[0] = "heart";
        }
        // C: card_saints[2] = card_items[1] = 0x6F assigns right to left: the item slot first.
        c.SetCardItems(0x66, 0x6F, 0x73, 0x6E);
        c.SetCardSaints(0x0F, 0x2B, 0x6F);
        menu[2] = c.ItemsUsable();
        menu[1] = c.SaintsUsable();
        menu.Popup = () => c.SaintPopupChanceHint();

        return menu.Run(
            // orig: R107+0x22F4 (line 0); hint R107+0x24A0 (19CE:0B40) matched
            new MenuLine(_ => DisturbTheAltar(c, menu), Unknown),
            // orig: R107+0x24D8 (line 1); hint R107+0x25F0 (19CE:0C90) matched
            new MenuLine(_ => PrayAgainstThePaganPowers(c), () => OptionHint.Say("Select a person and a saint.")),
            // orig: R107+0x2628 (line 2); hint R107+0x277E (19CE:0E1E) matched
            new MenuLine(_ => CleanseTheAltar(c), () => OptionHint.Say("Select a person and a potion."), () => PotionPopupHint(c, menu)),
            // orig: R107+0x27B6 (19CE:0E56) matched (empty); hint R107+0x27B8 (empty)
            // DORMANT: lines 3 and 4 are never offered; their actions and hints (R107+0x27BA, R107+0x27BC) are empty.
            // UNCERTAIN: those hints are void functions cast to option handlers: the chance is whatever is in AX/DX; neutral hints are used.
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            // orig: R107+0x27BE (19CE:0E5E) matched; hint R107+0x27D6
            new MenuLine(_ => { c.PassHours(1); c.LeaveForTheMap(); }, () => OptionHint.Say(SuccessAssured)),
            // orig: R107+0x280E (19CE:0EAE) matched; hint R107+0x283A
            new MenuLine(_ => FightTheBeasts(c), () => OptionHint.Say("Enter Combat")),
            // orig: R107+0x2872 (line 7); hint R107+0x2946 (19CE:0FE6) matched
            new MenuLine(_ => PrayAgainstTheBeasts(c), () => OptionHint.Say("Select a person and a saint.")),
            // orig: R107+0x297E (line 8); hint R107+0x29FA (19CE:109A) matched
            new MenuLine(_ => DriveThemOffWithAPotion(c), () => OptionHint.Say("Select a person and a potion."), () => PotionPopupHint(c, menu)),
            // orig: R107+0x2A32 (19CE:10D2) matched (empty); hint R107+0x2A34 (empty)
            // DORMANT: line 9 is never offered.
            new MenuLine(_ => { }));
    }

    private static OptionHint Unknown() => OptionHint.Say("You can always encounter the unknown");

    /// <summary>The potions' chance: none for the first three; the fourth by twice its quality, except against the beasts.</summary>
    // orig: R107+0x2B52 (19CE:11F2) matched (slot 3; slots 0..2 R107+0x2B04/11be/11d8 give 0)
    private static OptionHint PotionPopupHint(CardContext c, CardMenu menu)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        var chance = 0;
        if (c.MenuSlot == 3)
        {
            chance = c.ItemQuality(c.MenuMember, c.CardItems[c.MenuSlot]) * 2;
            if (menu.Block == 13)
            {
                chance = 0;
            }
        }
        return OptionHint.ChanceOf(chance, c.MenuMember);
    }

    /// <summary>The guardians of this circle (0 wolves, 1 beasts, 2 the Wild Hunter, 3 tree-giants).</summary>
    // C: (party_seed + cur_loc) & 3 on 16-bit ints: two's complement keeps it 0..3 for negative sums too.
    private static int Guardians(CardContext c) => (c.State.Party.Seed + c.Here) & 3;

    // DIVERGENCE: the original writes locations[cur_loc] unguarded; in the wilderness (-1) nothing is written here.
    private static void CloseThePlace(CardContext c)
    {
        if (c.State.Here is { } place)
        {
            place.ArrivalCard = CardContext.ClosedCard;
            place.EncounterCard = CardContext.ClosedCard;
        }
    }

    /// <summary>The stones are overturned (the item taken); the storm breaks and the guardians come.</summary>
    // orig: R107+0x22F4 (19CE:0994) matched; tests: CardSmokeTests
    private static void DisturbTheAltar(CardContext c, CardMenu menu)
    {
        c.PracticeSkill(Who.Everybody, (int)Skill.Virtue, 1, 10, 0);
        c.FinishQuest10(c.Here);
        ShowPicture(c, StormPicture);
        menu.Block = 13;
        menu.Set(OptionState.Enabled, 6, 7, 8);
        menu.Set(OptionState.Hidden, 0, 1, 2, 3, 4, 5, 9);
        c.SetCardItems(0x5F, 0x65, 0x66, 0x67);
        c.SetCardSaints(3, 0x45, 0x4A);
        menu[8] = c.ItemsUsable();
        menu[7] = c.SaintsUsable();
        switch (Guardians(c))
        {
            case 0:
                ShowPicture(c, StormPicture);
                c.Screen.Text(1);
                return;
            case 1:
                ShowPicture(c, 126);
                c.Screen.Text(2);
                return;
            case 2:
                ShowPicture(c, 130);
                c.Screen.Text(3);
                while (FightTheWildHunter(c))
                {
                }
                return;
            default:
                ShowPicture(c, 125);
                c.Screen.Text(18);
                while (FightTheTreeGiants(c))
                {
                }
                return;
        }
    }

    /// <summary>An answered prayer shatters the altar (and the quest's item can be taken); the place is closed.</summary>
    // orig: R107+0x24D8 (19CE:0B78) matched; tests: CardSmokeTests
    private static void PrayAgainstThePaganPowers(CardContext c)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (answer == -1)
        {
            return;
        }
        if (answer == 1)
        {
            ShowPicture(c, 90);
            c.Screen.Text(4);
            if (c.Events.QuestAt(10, c.Here))
            {
                c.FinishQuest10(c.Here);
                c.Screen.Text(24, TextFrame.BlankPage);
            }
            c.PracticeSkill(c.MenuMember, (int)Skill.Virtue, 1, 20, 0);
            c.GiveReward(1);
            CloseThePlace(c);
            c.LeaveForTheMap();
            return;
        }
        ShowPicture(c, 16);
        c.Screen.Text(5);
        c.Next = 254;
    }

    /// <summary>The potion is used up; three times in four it cleanses the altar (the hint's chance is not used).</summary>
    // orig: R107+0x2628 (19CE:0CC8) matched; tests: CardSmokeTests
    private static void CleanseTheAltar(CardContext c)
    {
        c.TakeItem(c.MenuMember, c.CardItems[c.MenuSlot]);
        c.Variables.Chosen[0] = c.MenuMember;
        if (c.Random(100) <= 75)
        {
            ShowPicture(c, 189);
            c.Screen.Text(6);
            if (c.Events.QuestAt(10, c.Here))
            {
                c.FinishQuest10(c.Here);
                c.Screen.Text(24);
            }
            c.PracticeSkill(c.MenuMember, (int)Skill.Virtue, 1, 20, 0);
            c.GiveReward(1);
            CloseThePlace(c);
            c.LeaveForTheMap();
            return;
        }
        if (c.CardItems[c.MenuSlot] == 0x6E)
        {
            ShowPicture(c, 20);
            c.Screen.Text(16);
        }
        else
        {
            ShowPicture(c, 16);
            c.Screen.Text(17);
        }
        c.Next = 254;
    }

    // orig: R107+0x280E (19CE:0EAE) matched; tests: CardSmokeTests
    private static void FightTheBeasts(CardContext c)
    {
        switch (Guardians(c))
        {
            case 0:
            case 2:
                while (FightTheWolves(c))
                {
                }
                break;
            default:
                while (FightTheWildBeasts(c))
                {
                }
                break;
        }
    }

    // orig: R107+0x2872 (19CE:0F12) matched; tests: CardSmokeTests
    private static void PrayAgainstTheBeasts(CardContext c)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (answer == -1)
        {
            return;
        }
        if (answer == 1)
        {
            ShowPicture(c, 16);
            c.Screen.Text(14);
            c.LeaveForTheMap();
            return;
        }
        ShowPicture(c, StormPicture);
        c.Screen.Text(5);
        FightTheBeasts(c);
    }

    /// <summary>The potion is spent in vain: the beasts come on.</summary>
    // orig: R107+0x297E (19CE:101E) matched; tests: CardSmokeTests
    private static void DriveThemOffWithAPotion(CardContext c)
    {
        c.TakeItem(c.MenuMember, c.CardItems[c.MenuSlot]);
        ShowPicture(c, 126);
        c.Screen.Text(15);
        FightTheBeasts(c);
    }

    /// <summary>The members lost to beasts: without armour (items 0x2B, 0x2C), Strength 29 or less and no Endurance left, they die; the others are mauled.</summary>
    private static void MauledByBeasts(CardContext c)
    {
        ShowPicture(c, 123);
        c.Screen.Text(10);
        for (var i = 0; i < Party.MaxMembers; i++)
        {
            if (c.Status(i) == CharacterStatus.Empty)
            {
                continue;
            }
            // C: && short-circuits; none of the tests draws a random number.
            if (c.CountItems(i, 0x2C) == 0 && c.CountItems(i, 0x2B) == 0
                && c.Attribute(i, (int)Attribute.Strength) <= 29 && c.Attribute(i, (int)Attribute.Endurance) == 0)
            {
                c.SetStatus(i, CharacterStatus.Empty);
                c.DeleteMember(i);
            }
            else
            {
                c.AddAttribute(i, (int)Attribute.Endurance, -(c.Random(20) + 10));
                c.SetAttribute(i, (int)Attribute.Strength, c.Attribute(i, (int)Attribute.Strength) / 7 + 1);
            }
        }
        c.LeaveForTheMap();
        c.PassHours(0);
    }

    /// <summary>Returns true when the party fled and must fight again.</summary>
    // orig: R107+0x1C40 (19CE:02E0) matched; tests: CardSmokeTests
    // C: right-to-left arguments: the pack size is drawn before run_battle draws its random seed (-1).
    private static bool FightTheWolves(CardContext c)
    {
        var wolves = c.Random(5) + 3;
        switch (c.RunBattle(0x3B, -1, 0x2A, 1, wolves, -1, 0, 0, -1, 0, 0))
        {
            case 0:
                c.PassHours(1);
                ShowPicture(c, 100);
                c.Screen.Text(7);
                c.PracticeSkill(Who.Everybody, (int)Skill.Virtue, 1, 10, 2);
                c.GiveReward(1);
                CloseThePlace(c);
                c.LeaveForTheMap();
                break;
            case 1:
            case 2:
                c.DropAway();
                c.PassHours(1);
                ShowPicture(c, 130);
                c.Screen.Text(9);
                return true;
            case 3:
            case 4:
                MauledByBeasts(c);
                break;
        }
        return false;
    }

    /// <summary>Returns true when the party fled and must fight again.</summary>
    // orig: R107+0x1E48 (19CE:04E8) matched; tests: CardSmokeTests
    private static bool FightTheWildBeasts(CardContext c)
    {
        var beasts = c.Random(6) + 2;
        switch (c.RunBattle(0x3B, -1, 0x3A, 1, beasts, -1, 0, 0, -1, 0, 0))
        {
            case 0:
                c.PassHours(1);
                ShowPicture(c, 100);
                c.Screen.Text(7);
                c.PracticeSkill(Who.Everybody, (int)Skill.Virtue, 1, 10, 2);
                CloseThePlace(c);
                c.GiveReward(1);
                c.LeaveForTheMap();
                break;
            case 1:
            case 2:
                c.DropAway();
                c.PassHours(1);
                ShowPicture(c, 130);
                c.Screen.Text(9);
                return true;
            case 3:
            case 4:
                MauledByBeasts(c);
                break;
        }
        return false;
    }

    /// <summary>The Wild Hunter: beaten, he rides off; victorious, he spears the leader. Returns true to fight again.</summary>
    // orig: R107+0x2058 (19CE:06F8) matched; tests: CardSmokeTests
    private static bool FightTheWildHunter(CardContext c)
    {
        switch (c.RunBattle(0x3B, -1, 0x3C, 1, 1, -1, 0, 0, -1, 0, 0))
        {
            case 0:
                c.PassHours(1);
                ShowPicture(c, 130);
                c.Screen.Text(12);
                c.PracticeSkill(Who.Everybody, (int)Skill.Virtue, 1, 10, 2);
                c.GiveReward(1);
                CloseThePlace(c);
                c.LeaveForTheMap();
                return false;
            case 1:
            case 2:
                c.DropAway();
                c.PassHours(1);
                ShowPicture(c, 130);
                c.Screen.Text(9);
                return true;
            case 3:
            case 4:
                c.Variables.Chosen[0] = c.Leader;
                var leader = c.Leader;
                c.SetStatus(leader, CharacterStatus.Empty);
                c.DeleteMember(leader);
                c.PassHours(1);
                ShowPicture(c, 130);
                c.Screen.Text(11);
                c.LeaveForTheMap();
                return false;
            default:
                return false;
        }
    }

    /// <summary>The tree-giants: beaten, they fall; otherwise they beat the party and move off.</summary>
    // orig: R107+0x21CC (19CE:086C) matched; tests: CardSmokeTests
    private static bool FightTheTreeGiants(CardContext c)
    {
        var giants = c.Random(5) + 3;
        switch (c.RunBattle(0x3B, -1, 0x3E, 1, giants, -1, 0, 0, -1, 0, 0))
        {
            case 0:
                c.PassHours(1);
                ShowPicture(c, 100);
                c.Screen.Text(7);
                c.PracticeSkill(Who.Everybody, (int)Skill.Virtue, 1, 10, 2);
                c.GiveReward(1);
                CloseThePlace(c);
                c.LeaveForTheMap();
                break;
            case 1:
            case 2:
            case 3:
            case 4:
                c.RejoinAway();
                c.Injure(Who.Everybody, 4, 8, 12);
                c.PassHours(1);
                ShowPicture(c, CirclePicture);
                c.Screen.Text(19);
                c.LeaveForTheMap();
                break;
        }
        return false;
    }
}
