using Darklands.Core.Characters;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$MeetS00": the "trees" ahead are schrats — hairy forest giants, violent but easily charmed
/// by beautiful women. The scout may notice them; then the party can press on, plead, use a
/// potion, pray, surrender, sneak around or scout them. The schrats may ask for a woman of the
/// party in exchange for free passage.
/// </summary>
// orig: R112+0x2760 (1AAE:0000) matched; tests: CardSmokeTests
// orig: R112+0x37A8 (1AAE:1048) matched (registration: item pop-ups R112+0x3876/1168/11ba name the member; saint pop-ups do not)
// DORMANT: R112+0x396C is never called.
public sealed class SchratsInTheForest : Card
{
    private const int RoadPicture = 286;
    private const int SchratsPicture = 287;
    private const int OfferPicture = 125;
    private const int CampPicture = 61;

    public override int Number => 275;

    public override int Run(CardContext c)
    {
        c.PlayMusic(0x20);
        c.Next = Number;
        var menu = Menu(c, 0);
        c.PartyMaxAttributePlusSkill((int)Attribute.Perception, (int)Skill.Woodwise);
        var scout = c.BestMember;
        c.Variables.Chosen[0] = scout;
        var watchfulness = c.Attribute(scout, (int)Attribute.Perception) + c.Random(20) + c.Skill(scout, (int)Skill.Woodwise) / 2;
        if (c.Random(100) <= watchfulness)
        {
            Show(c, RoadPicture);
            menu.Set(OptionState.Enabled, 0, 1, 2, 3, 4, 5, 7);
            menu.Block = 0;
        }
        else
        {
            menu.Set(OptionState.Enabled, 1, 2, 3, 4, 6, 8);
            c.PartyMinMountedAgility();
            c.Variables.Chosen[0] = c.BestMember;
            Show(c, SchratsPicture);
            menu.Block = 1;
        }
        c.SetCardItems(0x65, 0x66, 0x67);
        menu[2] = c.ItemsUsable();
        c.SetCardSaints(0x36, 0x3D, 0x83, 0x45);
        menu[3] = c.SaintsUsable();
        menu.Popup = () => c.SaintPopupChanceHint();

        return menu.Run(
            // orig: R112+0x2D00 (1AAE:05A0) matched; hint R112+0x2D76
            new MenuLine(_ => TheSchratsCome(c, menu), Unknown),
            // orig: R112+0x2DAE (line 1); hint R112+0x2EE4 (1AAE:0784) matched
            new MenuLine(p => Plead(c, menu, p), () => PleadHint(c)),
            // orig: R112+0x2F66 (line 2); hint R112+0x30C4 (1AAE:0964) matched
            new MenuLine(p => ThrowAPotion(c, menu, p), () => OptionHint.Say("Select a person and a potion."), () => PotionHint(c)),
            // orig: R112+0x30FC (line 3); hint R112+0x3228 (1AAE:0AC8) matched
            new MenuLine(_ => PrayForDeliverance(c, menu), () => OptionHint.Say("Select a person and a saint.")),
            // orig: R112+0x3260 (line 4); hint R112+0x334E (1AAE:0BEE) matched
            new MenuLine(_ => Surrender(c, menu), () => OptionHint.Say(SuccessAssured)),
            // orig: R112+0x3386 (line 5); hint R112+0x3488 (1AAE:0D28) matched
            new MenuLine(p => SneakAround(c, menu, p), () => WoodcraftHint(c)),
            // orig: R112+0x34D6 (1AAE:0D76) matched (hint); action R112+0x34C0: fight (0316) with 1 when options_block is set, else 2
            new MenuLine(_ => FightTheSchrats(c, menu.Block != 0 ? 1 : 2), () => OptionHint.Say("Enter Combat")),
            // orig: R112+0x350E (line 7); hint R112+0x362A (1AAE:0ECA) matched
            new MenuLine(p => ScoutThem(c, menu, p), () => WoodcraftHint(c)),
            // orig: R112+0x3662 (line 8); hint R112+0x376C (1AAE:100C) matched
            new MenuLine(p => RunAway(c, menu, p), () => OptionHint.ChanceOf(Math.Clamp(
                c.PartyGet(5) != 0 ? 100 : c.PartyMinEffectiveAgility(), 10, 95), OptionHint.WholeParty)),
            // orig: R112+0x37A4 (1AAE:1044) matched (empty); hint R112+0x37A6 (empty)
            // DORMANT: line 9 is never offered. UNCERTAIN: its hint returns AX/DX; a neutral hint is used.
            new MenuLine(_ => { }));
    }

    private static OptionHint Unknown() => OptionHint.Say("You can always encounter the unknown");

    private static OptionHint WoodcraftHint(CardContext c) => OptionHint.ChanceOf(Math.Clamp(
        c.PartyAverageSkill((int)Skill.Woodwise) + c.PartyAverageEffectiveAgility() * 2 / 3, 5, 85), OptionHint.WholeParty);

    /// <summary>Hurling a potion: Perception plus quality plus 19 / 7 / 21 by slot.</summary>
    // orig: R112+0x3876 (1AAE:1116) matched; slots 1, 2 R112+0x38C8, R112+0x391A
    private static OptionHint PotionHint(CardContext c)
    {
        var m = c.MenuMember;
        c.Variables.Chosen[0] = m;
        var quality = c.ItemQuality(m, c.CardItems[c.MenuSlot]);
        var perception = c.Attribute(m, (int)Attribute.Perception);
        var chance = c.MenuSlot switch
        {
            0 => Math.Clamp(perception + quality + 19, 1, 95),
            1 => Math.Clamp(perception + quality + 7, 1, 95),
            _ => Math.Clamp(perception + quality + 21, 1, 99),
        };
        return OptionHint.ChanceOf(chance, m);
    }

    /// <summary>The fairest woman charms them (four times her Charisma); without women, the most charming member's words.</summary>
    // C: a side effect of the hint: the charmer becomes $ChosenOneName.
    private static OptionHint PleadHint(CardContext c)
    {
        int chance;
        if (c.AbleWomen() != 0)
        {
            chance = c.PartyMaxAttributeOfWomen((int)Attribute.Charisma) * 4;
            c.Variables.Chosen[0] = c.BestMember;
        }
        else
        {
            c.PartyMaxAttribute((int)Attribute.Charisma);
            var m = c.BestMember;
            c.Variables.Chosen[0] = m;
            chance = (c.Attribute(m, (int)Attribute.Charisma) / 2 + c.Skill(m, (int)Skill.SpeakCommon)) / 3;
        }
        return OptionHint.ChanceOf(Math.Clamp(chance, 1, 99), c.Variables.Chosen[0]);
    }

    /// <summary>The schrats lurch out of the trees: plead, potion, prayer, surrender, fight or flight.</summary>
    // orig: R112+0x2D00 (1AAE:05A0) matched (also inlined in 064e, 0806, 099c, 0c26, 0dae)
    private void TheSchratsCome(CardContext c, CardMenu menu)
    {
        menu[0] = OptionState.Hidden;
        menu[1] = OptionState.Enabled;
        menu[2] = c.ItemsUsable();
        menu[3] = c.SaintsUsable();
        menu.Set(OptionState.Enabled, 4, 6, 8);
        menu.Set(OptionState.Hidden, 5, 7, 9);
        c.PartyMinMountedAgility();
        c.Variables.Chosen[0] = c.BestMember;
        Show(c, SchratsPicture);
        menu.Block = 1;
        c.Next = 275;
    }

    /// <summary>They want $ChosenOneName to go with them; without an able woman, surrender is not offered.</summary>
    private void TheSchratsWantAWoman(CardContext c, CardMenu menu)
    {
        menu.Set(OptionState.Hidden, 0, 1);
        menu[2] = c.ItemsUsable();
        menu[3] = c.SaintsUsable();
        menu.Set(OptionState.Enabled, 4, 6, 8);
        menu.Set(OptionState.Hidden, 5, 7, 9);
        if (c.AbleWomen() == 0)
        {
            menu[4] = OptionState.Shown;
        }
        Show(c, OfferPicture);
        menu.Block = 13;
    }

    // orig: R112+0x2DAE (1AAE:064E) matched; tests: CardSmokeTests
    private void Plead(CardContext c, CardMenu menu, int chance)
    {
        if (c.Random(100) <= chance)
        {
            TheSchratsWantAWoman(c, menu);
            return;
        }
        if (menu.Block != 0)
        {
            Show(c, OfferPicture);
            c.Screen.Text(8);
            FightTheSchrats(c, 2);
            return;
        }
        TheSchratsCome(c, menu);
    }

    // orig: R112+0x2F66 (1AAE:0806) matched; tests: CardSmokeTests
    // C: `menu_slot == 0x67` compares the slot (0..3) with an item code, so the fire-potion text (15) never shows.
    private void ThrowAPotion(CardContext c, CardMenu menu, int chance)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        c.TakeItem(c.MenuMember, c.CardItems[c.MenuSlot]);
        if (c.Random(100) <= chance)
        {
            if (c.MenuSlot == 0x67)
            {
                // DORMANT: never true (see above).
                Show(c, 332);
                c.Screen.Text(15);
            }
            else
            {
                Show(c, 189);
                c.Screen.Text(3);
            }
            c.LeaveForTheMap();
            return;
        }
        if (menu.Block != 0)
        {
            Show(c, CampPicture);
            c.Screen.Text(14);
            FightTheSchrats(c, 1);
            return;
        }
        TheSchratsCome(c, menu);
    }

    // orig: R112+0x30FC (1AAE:099C) matched; tests: CardSmokeTests
    private void PrayForDeliverance(CardContext c, CardMenu menu)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (answer == -1)
        {
            return;
        }
        if (answer == 1)
        {
            Show(c, 289);
            c.Screen.Text(4);
            c.LeaveForTheMap();
            return;
        }
        Show(c, 16);
        c.Screen.Text(5);
        if (menu.Block != 0)
        {
            FightTheSchrats(c, 1);
            return;
        }
        TheSchratsCome(c, menu);
    }

    /// <summary>Surrendering to schrats: everybody is beaten down and robbed. Before they appear, it is the offer of a woman.</summary>
    // orig: R112+0x3260 (1AAE:0B00) matched; tests: CardSmokeTests
    private void Surrender(CardContext c, CardMenu menu)
    {
        if (menu.Block != 0)
        {
            c.SetAttribute(Who.Everybody, (int)Attribute.Endurance, 1);
            c.SetAttribute(Who.Everybody, (int)Attribute.Strength, c.Random(5) + 1);
            c.LoseItems(Who.Everybody);
            Show(c, OfferPicture);
            c.Screen.Text(10);
            c.LeaveForTheMap();
            return;
        }
        TheSchratsWantAWoman(c, menu);
        c.Next = 275;
    }

    // orig: R112+0x3386 (1AAE:0C26) matched; tests: CardSmokeTests
    private void SneakAround(CardContext c, CardMenu menu, int chance)
    {
        if (c.Random(100) <= chance)
        {
            c.PassHours(c.HoursUntil(18));
            Show(c, RoadPicture);
            c.Screen.Text(6);
            c.PracticeSkill(Who.Everybody, (int)Skill.Woodwise, 7, 10, 0);
            c.LeaveForTheMap();
            return;
        }
        c.PassHours(1);
        TheSchratsCome(c, menu);
        c.PracticeSkill(Who.Everybody, (int)Skill.Woodwise, 0, 10, 0);
    }

    // orig: R112+0x350E (1AAE:0DAE) matched; tests: CardSmokeTests
    private void ScoutThem(CardContext c, CardMenu menu, int chance)
    {
        if (c.Random(100) <= chance)
        {
            c.PartyMaxSkill((int)Skill.Woodwise);
            c.Variables.Chosen[0] = c.BestMember;
            c.PracticeSkill(Who.Everybody, (int)Skill.Woodwise, 7, 10, 0);
            Show(c, RoadPicture);
            menu.Block = 2;
            menu[0] = OptionState.Hidden;
            menu[1] = OptionState.Enabled;
            menu[2] = c.ItemsUsable();
            menu[3] = c.SaintsUsable();
            menu.Set(OptionState.Enabled, 5, 6, 8);
            menu.Set(OptionState.Hidden, 4, 7, 9);
            c.Next = 275;
            return;
        }
        TheSchratsCome(c, menu);
        c.PracticeSkill(Who.Everybody, (int)Skill.Woodwise, 0, 10, 0);
    }

    /// <summary>Running: a quick party escapes; a slow one is caught and may only sometimes still plead.</summary>
    // orig: R112+0x3662 (1AAE:0F02) matched; tests: CardSmokeTests
    private void RunAway(CardContext c, CardMenu menu, int chance)
    {
        if (c.Random(100) <= chance)
        {
            if (c.PartyGet(5) != 0)
            {
                c.PracticeSkill(Who.Everybody, (int)Skill.Riding, 7, 10, 0);
            }
            c.PassHours(3);
            Show(c, CampPicture);
            c.Screen.Text(9);
            c.LeaveForTheMap();
            return;
        }
        if (c.PartyGet(5) != 0)
        {
            c.PracticeSkill(Who.Everybody, (int)Skill.Riding, 0, 10, 0);
        }
        Show(c, 145);
        menu.Block = 7;
        menu[0] = OptionState.Hidden;
        menu[1] = c.Random(2) != 0 ? OptionState.Enabled : OptionState.Hidden;
        menu[2] = c.ItemsUsable();
        menu[3] = c.SaintsUsable();
        menu[6] = OptionState.Enabled;
        menu.Set(OptionState.Hidden, 4, 5, 7, 8, 9);
        c.Next = 275;
    }

    /// <summary>
    /// The schrats: won, the forest is safe; fled, those away come back; beaten, they carry off the
    /// fairest woman (or, without one, everybody is left robbed and half dead).
    /// </summary>
    // orig: R112+0x2A76 (1AAE:0316) matched; tests: CardSmokeTests
    // C: arguments right to left: rand_mod(5) + 3 is drawn before rand_mod(3); run_battle then draws its seed. The kind
    // argument is always 1 or 2, so the battle is always 0x2E.
    // C: party_x is declared `extern long` in the original, so the call pushes party_y and party_x: nearest_city(party_x) receives the party's (x, y); nothing is undefined (checked against the asm).
    private void FightTheSchrats(CardContext c, int kind)
    {
        var lore = c.PartyLoreLevel();
        var count = c.Random(5) + 3;
        var groups = c.Random(3) + lore / 4 + 1;
        switch (c.RunBattle(kind != 0 ? 0x2E : 0x2F, -1, 0x3E, groups, count, -1, 0, 0, -1, 0, 0))
        {
            case 0:
                Show(c, SchratsPicture);
                c.Screen.Text(11, TextFrame.BlankPage);
                c.PassHours(1);
                c.AdjustReputation(c.NearestCity(c.State.MapPosition.X, c.State.MapPosition.Y), 1, 1);
                c.LeaveForTheMap();
                break;
            case 1:
            case 2:
                var back = 0;
                for (var i = 0; i < Party.MaxMembers; i++)
                {
                    if (c.Status(i) == CharacterStatus.Away)
                    {
                        c.SetStatus(i, CharacterStatus.Able);
                        back++;
                    }
                }
                if (back != 0)
                {
                    Show(c, CampPicture);
                    c.Screen.Text(18);
                }
                else
                {
                    Show(c, RoadPicture);
                    c.Screen.Text(6);
                }
                c.PassHours(c.HoursUntil(5));
                c.LeaveForTheMap();
                break;
            case 3:
            case 4:
                for (var i = 0; i < Party.MaxMembers; i++)
                {
                    if (c.Status(i) == CharacterStatus.Away)
                    {
                        c.SetStatus(i, CharacterStatus.Able);
                    }
                }
                if (c.AbleWomen() != 0)
                {
                    c.PartyMaxAttributeOfWomen((int)Attribute.Charisma);
                    c.Variables.Chosen[0] = c.BestMember;
                }
                else
                {
                    c.Variables.Chosen[0] = -1;
                }
                var taken = c.Variables.Chosen[0];
                for (var i = 0; i < Party.MaxMembers; i++)
                {
                    if (c.Status(i) != CharacterStatus.Empty && taken != i)
                    {
                        c.KeepItems(i, 20);
                        c.SetAttribute(i, (int)Attribute.Endurance, 1);
                        c.SetAttribute(i, (int)Attribute.Strength, 1);
                    }
                }
                c.PassHours(c.HoursUntil(18));
                if (taken != -1)
                {
                    c.LoseItems(taken);
                    Show(c, OfferPicture);
                    c.Screen.Text(10);
                }
                else
                {
                    Show(c, CampPicture);
                    c.Screen.Text(18);
                }
                c.LeaveForTheMap();
                break;
        }
    }
}
