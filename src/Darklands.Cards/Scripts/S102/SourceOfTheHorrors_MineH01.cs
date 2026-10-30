using Darklands.Core.Characters;
using Darklands.Core.Events;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$MineH01": the source of the horrors in the mine, by the quest's detail: a firespout of
/// vulcans (block 0), a tomb of the undead (1) or a hellgate of demons (2). The party may bury
/// it under rocks, build a barricade, pray, use a potion (Firewall 0x74 for the firespout,
/// Transformation 0x6E for the hellgate), or fight on. A failed try lets new horrors through
/// (battle −6) and is not tried again for a week.
/// </summary>
// orig: R102+0x06E8 (18A6:0008) matched; tests: CardSmokeTests
public sealed class SourceOfTheHorrors_MineH01 : Card
{
    private const int Picture = 0;
    private const int MineQuestTag = 0x0C;
    private const int Firewall = 0x74;
    private const int Transformation = 0x6E;

    public override int Number => 0xE9;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = Number;
        var menu = Menu(c);
        menu.Enable(0, 1, 3, 4);
        if (c.Events.Fact(MineQuestTag, 8, c.Here))
        {
            menu[0] = OptionState.Shown;
        }
        if (c.Events.Fact(MineQuestTag, 9, c.Here))
        {
            menu[1] = OptionState.Shown;
        }
        // orig: R102+0x0BA2 (18A6:04C2) matched
        c.SetCardSaints();
        c.SetCardItems();
        // C: the detail is fetched anew for each test (it does not change); % keeps its sign.
        var horror = c.MineDetail() % 3;
        if (horror == 0)
        {
            c.SetCardItems(Firewall);
        }
        if (horror == 1)
        {
            menu[3] = OptionState.Hidden;
        }
        if (horror == 2)
        {
            menu[1] = OptionState.Hidden;
            c.SetCardItems(Transformation);
        }
        c.SetCardSaints(0x0F, 0x2B, 0x6F);
        if (menu[3] == OptionState.Enabled)
        {
            menu[3] = c.ItemsUsable();
        }
        menu[2] = c.SaintsUsable();

        // DIVERGENCE: for a negative detail the original shows no option block at all (the
        // menu keeps whatever was shown); here the block the last card left is used.
        menu.EachRound = () => menu.Block = horror is >= 0 and <= 2 ? horror : c.OptionsBlock;
        return menu.Run(
            // orig: R102+0x0916 (line 0); hint R102+0x0982 (18A6:02A2) matched
            new MenuLine(chance => Bury(c, chance),
                () => OptionHint.ChanceOf(c.PartyAverageAttribute((int)Attribute.Strength), Who.Everybody)),
            new MenuLine(chance => BuildABarricade(c, chance),
                () => OptionHint.ChanceOf(BarricadeChance(c), c.Variables.Chosen[0])),
            // orig: R102+0x0A26 (line 2); saint pop-up hints R102+0x0CB8, R102+0x0D42, R102+0x0DCC (CardMenu.SaintPopupHint)
            new MenuLine(() => Pray(c), OptionHint.Saying(CardTexts.SelectSaint), c.SaintPopupHint),
            new MenuLine(() => PourThePotion(c, horror), OptionHint.Saying(CardTexts.SelectPotion), () => PotionHint(c)),
            // orig: R102+0x0B5C (18A6:047C) matched
            new MenuLine(() => { c.BattleMode = 1; c.Next = -2; }));
    }

    private static void Sealed(CardContext c)
    {
        c.Screen.Text(4, TextFrame.BlankPage);
        c.BattleResult = 0;
        c.Next = 0x2E;
    }

    /// <summary>New horrors pour through (battle −6); the same way is barred for 168 hours.</summary>
    private static void Failed(CardContext c, int fact)
    {
        c.Screen.Text(5, TextFrame.BlankPage);
        c.Events.Create(Who.Everybody, c.Here, 0x5A, EventKinds.LocationFact, fact, 0x63, 0, 0xA8, 0, MineQuestTag);
        c.BattleMode = 1;
        c.Next = -6;
    }

    // orig: R102+0x0916 (18A6:0236) matched (hint 02a2, on the menu line)
    private static void Bury(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            c.Screen.Text(3, TextFrame.BlankPage);
            Sealed(c);
            return;
        }
        Failed(c, 8);
    }

    /// <summary>The best Artifice; the best mechanic is named $ChosenOneName.</summary>
    // orig: R102+0x0A04 (18A6:0324) matched
    private static int BarricadeChance(CardContext c)
    {
        var chance = c.PartyMaxSkill((int)Skill.Artifice);
        c.Variables.Chosen[0] = (byte)c.BestMember;
        return chance;
    }

    // orig: R102+0x0998 (18A6:02B8) matched
    private static void BuildABarricade(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            c.Screen.Text(6, TextFrame.BlankPage);
            Sealed(c);
            return;
        }
        Failed(c, 9);
    }

    /// <summary>A prayer always works here: the card asks no saint (Saint 0x2B in slot 1 brings the roof down, the others angels).</summary>
    // orig: R102+0x0A26 (18A6:0346) matched
    // C: no call-on-saint is made; the pop-up's hint only names the one who prays.
    private static void Pray(CardContext c)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        c.Screen.Text(c.MenuSlot == 1 ? 7 : 8, TextFrame.BlankPage);
        Sealed(c);
    }

    /// <summary>The potion's pop-up: on the firespout 20, or 40 once it has flared up; elsewhere "Success is assured."</summary>
    // orig: R102+0x0C4C (18A6:056C) matched
    private static OptionHint PotionHint(CardContext c)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        if (c.MineDetail() % 3 == 0)
        {
            return OptionHint.ChanceOf(c.Events.Fact(MineQuestTag, 0x0A, c.Here) ? 40 : 20, c.MenuMember);
        }
        return OptionHint.Say(CardTexts.SuccessAssured, c.MenuMember);
    }

    /// <summary>Firewall on the firespout works four times in five (else it flares up again, and the
    /// potion's hint chance becomes 40 instead of 20); Transformation on the hellgate always works.</summary>
    // orig: R102+0x0A92 (18A6:03B2)
    // C: the item pop-up's chance (20, or 40 after a flare-up) is shown but not used: the action rolls against 80.
    private static void PourThePotion(CardContext c, int horror)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        c.TakeItem(c.MenuMember, c.CardItems[c.MenuSlot]);
        if (horror == 0)
        {
            if (c.Random(100) <= 0x50)
            {
                c.Screen.Text(9, TextFrame.BlankPage);
                Sealed(c);
                return;
            }
            c.Screen.Text(10, TextFrame.BlankPage);
            c.Events.Create(Who.Everybody, c.Here, 0x5A, EventKinds.LocationFact, 0x0A, 0x63, 0, 0x2D0, 0, MineQuestTag);
            c.Next = 0xE9;
            return;
        }
        c.Screen.Text(11, TextFrame.BlankPage);
        Sealed(c);
    }
}
