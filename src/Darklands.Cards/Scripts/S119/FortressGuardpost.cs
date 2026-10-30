using Darklands.Core.Characters;
using Darklands.Core.Events;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Witcd05": the guardpost of Baphomet's fortress. The party may bluff (three ruses, easier once
/// the guards know them), fight, pray or throw a potion. A heard prayer or a friendly chat makes the
/// guards recognise the party from then on; the boldest ruse sends the guards away and reveals the
/// passwords and the name of the fortress.
/// </summary>
// orig: R119+0x150C (1988:000C) matched; tests: CardSmokeTests
//   option 0: action R119+0x176E; hint R119+0x17C0
//   option 1: action R119+0x17F8; hint R119+0x180A
//   option 2: action R119+0x1842; hint R119+0x1936
//   option 3: action R119+0x196E; hint R119+0x19CC
//   option 4: action R119+0x1A04; hint R119+0x1AA4
//   option 5: action R119+0x1B5C; hint R119+0x1C50
//   option 6: action R119+0x1D18; hint R119+0x1DC6
//   option 7: action R119+0x1E96; hint R119+0x1EB4
//   option 8: action R119+0x1EF0; hint R119+0x1F0E
//   option 9: action R119+0x1F4A; hint R119+0x1F68
//   item pop-up: R119+0x207E, R119+0x20BA, R119+0x20F6,
//     R119+0x2132
//   saint pop-up: R119+0x216E, R119+0x21F6, R119+0x227E,
//     R119+0x2306
//   other: R119+0x1FA4, R119+0x2046
// DORMANT (generated): option slots 10..19 keep the default handler R119+0x2046 ("screwed up.") and are never shown.
// DORMANT (generated): option line(s) 7, 8, 9 are never enabled (every assignment sets their flag to 0).
// C: the card-engine globals DS:EE70 / DS:EE72 the C sets are the pop-up boxes' drawing flags (R043+0x2ACA, see DIVERGENCE_REVIEW); presentation only, not modelled.
// C (generated): DS:270F as an argument is the immediate 0x270F = 9999 (forever, EventLog.Forever), printed as a symbol.
public sealed class FortressGuardpost : Card
{
    public override int Number => 302;

    private const int Picture = 168;
    private const int GuardsPicture = 167;
    private const int DeskPicture = 166;
    private const int PotionPicture = 185;

    /// <summary>The fact (tag 1) that the guards know the party.</summary>
    private const int KnownFact = 0x14;

    private const int UnusedLineCard = 0x62;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = Number;
        var menu = Menu(c, GuardsKnowUs(c) ? 1 : 0);
        menu.Enable(0, 1, 2, 3);
        c.SetCardItems(0x60, 0x65, 0x68);
        c.SetCardSaints(2, 6, 0x12);
        menu[3] = c.ItemsUsable();
        menu[2] = c.SaintsUsable();

        // DORMANT: R119+0x2046 ("screwed up.") is the handler of slots 10..19, never shown.
        // orig: R119+0x207E / _0bba / _0bf6 / _0c32 R119+0x207E..0x2132 matched — item pop-up ($ChosenOneName = member in the box).
        // orig: R119+0x216E / _0cf6 / _0d7e / _0e06 matched — saint pop-up chance text.
        var unknown = OptionHint.Saying(CardTexts.Unknown);
        // DORMANT: in text block 6 the option lines 0..3 are never offered (an Offer for this block lists other lines); they can be offered in other blocks only.
        return menu.Run(
            new MenuLine(_ => { menu.Offer(6, 4, 5, 6); ShowPicture(c, GuardsPicture); }, unknown),
            new MenuLine(_ => { c.CombatFlags |= 8; Leave(c, -3); }, unknown),
            new MenuLine(_ => Pray(c, menu), OptionHint.Saying(CardTexts.SelectSaint), c.SaintPopupChanceHint),
            new MenuLine(_ => ThrowPotion(c), OptionHint.Saying(CardTexts.SelectPotion), () => c.MemberPopupHint(CardTexts.Unknown)),
            new MenuLine(chance => CheckTheirEfficiency(c, chance), () => EfficiencyChance(c)),
            new MenuLine(chance => TrickThem(c, menu, chance), () => TrickChance(c)),
            new MenuLine(chance => RelieveThem(c, chance), () => RelieveChance(c)),
            // DORMANT: lines 7..9 are never enabled (no handler sets their flags to 1); their handlers repeat text 0 and go to 0x62.
            new MenuLine(_ => RepeatOpening(c), unknown),
            new MenuLine(_ => RepeatOpening(c), unknown),
            // C: the hints of lines 7..9 set r.b = leader, then −2; only −2 remains.
            new MenuLine(_ => RepeatOpening(c), unknown));
    }

    private static bool GuardsKnowUs(CardContext c) => c.Events.FactAnywhere(1, KnownFact);

    private static void BecomeKnown(CardContext c) =>
        c.Events.Create(Who.Everybody, c.Here, 0, 0x1C, KnownFact, 0, 0, EventLog.Forever, 0, 1);

    private static void Leave(CardContext c, int next)
    {
        c.BattleMode = 1;
        c.Next = next;
    }

    // orig: R119+0x1842 matched
    /// <summary>A heard prayer makes the captain smile (and know the party); otherwise the guards attack (−4).</summary>
    private static void Pray(CardContext c, CardMenu menu)
    {
        var result = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuEntrySlot]);
        if (result == -1)
        {
            return;
        }
        if (result != 0)
        {
            ShowPicture(c, Picture);
            c.Screen.Text(4);
            BecomeKnown(c);
            ShowPicture(c, GuardsPicture);
            // C: only settings-block changes; the option flags stay as they were.
            menu.Block = 1;
            return;
        }
        ShowPicture(c, GuardsPicture);
        c.Screen.Text(5);
        c.CombatFlags |= 8;
        Leave(c, -4);
    }

    // orig: R119+0x196E matched
    /// <summary>The potion covers the escape to the main building (battle mode 3, enemy 4).</summary>
    private static void ThrowPotion(CardContext c)
    {
        c.TakeItem(c.MenuMember, c.CardItems[c.MenuSlot]);
        ShowPicture(c, PotionPicture);
        c.Screen.Text(3);
        c.BattleMode = 3;
        c.BattleEnemy = 4;
        c.Next = -2;
    }

    private static int LeaderSkill(CardContext c, Skill skill) => c.Skill(Who.Leader, (int)skill);

    private static int LeaderAttribute(CardContext c, Core.Characters.Attribute attribute) => c.Attribute(Who.Leader, (int)attribute);

    // orig: R119+0x1AA4 matched
    // C: all chance sums stay far below 32767 (attributes and skills are at most 99).
    /// <summary>"Just checking their efficiency": the leader's Streetwise, Speak Common and Charisma (and wits once known).</summary>
    private static OptionHint EfficiencyChance(CardContext c)
    {
        int chance;
        if (GuardsKnowUs(c))
        {
            chance = (LeaderSkill(c, Skill.Streetwise) + LeaderAttribute(c, Core.Characters.Attribute.Perception)
                    + LeaderAttribute(c, Core.Characters.Attribute.Intelligence)) / 3
                + LeaderAttribute(c, Core.Characters.Attribute.Charisma) + LeaderSkill(c, Skill.SpeakCommon);
        }
        else
        {
            chance = LeaderSkill(c, Skill.SpeakCommon) / 2 + LeaderSkill(c, Skill.Streetwise)
                + LeaderAttribute(c, Core.Characters.Attribute.Charisma);
        }
        return OptionHint.ChanceOf(chance, c.Leader);
    }

    // orig: R119+0x1A04 matched
    /// <summary>The captain act: success or not the party walks on (text 7); success makes the guards know them.</summary>
    private static void CheckTheirEfficiency(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            ShowPicture(c, Picture);
            c.Screen.Text(7);
            BecomeKnown(c);
        }
        else
        {
            ShowPicture(c, Picture);
            c.Screen.Text(7);
        }
        Leave(c, -2);
    }

    // orig: R119+0x1C50 matched
    private static OptionHint TrickChance(CardContext c)
    {
        int chance;
        if (GuardsKnowUs(c))
        {
            chance = (LeaderSkill(c, Skill.Streetwise) + LeaderAttribute(c, Core.Characters.Attribute.Intelligence)) / 3
                + LeaderSkill(c, Skill.SpeakCommon) * 3 / 4 + LeaderAttribute(c, Core.Characters.Attribute.Charisma);
        }
        else
        {
            chance = LeaderSkill(c, Skill.Streetwise) / 2 + LeaderAttribute(c, Core.Characters.Attribute.Charisma) / 2
                + LeaderSkill(c, Skill.SpeakCommon) / 2;
        }
        return OptionHint.ChanceOf(chance, c.Leader);
    }

    // orig: R119+0x1B5C matched
    /// <summary>
    /// A friendly chat: the guard tells what they watch for and knows the party from now on.
    /// Failing, the party walks on (text 7 again) but is ambushed.
    /// </summary>
    private static void TrickThem(CardContext c, CardMenu menu, int chance)
    {
        if (c.Random(100) <= chance)
        {
            ShowPicture(c, Picture);
            c.Screen.Text(9);
            BecomeKnown(c);
            ShowPicture(c, GuardsPicture);
            menu.Offer(1, 0, 1, 2, 3);
            return;
        }
        ShowPicture(c, Picture);
        c.Screen.Text(7);
        c.CombatFlags |= 8;
        Leave(c, -2);
    }

    // orig: R119+0x1DC6 matched
    private static OptionHint RelieveChance(CardContext c)
    {
        int chance;
        if (GuardsKnowUs(c))
        {
            chance = LeaderSkill(c, Skill.Streetwise) / 3 + LeaderAttribute(c, Core.Characters.Attribute.Charisma) * 3 / 4
                + LeaderSkill(c, Skill.SpeakCommon) / 2;
        }
        else
        {
            chance = LeaderSkill(c, Skill.Streetwise) / 4 + LeaderAttribute(c, Core.Characters.Attribute.Charisma) / 4
                + LeaderSkill(c, Skill.SpeakCommon) / 4;
        }
        return OptionHint.ChanceOf(chance, c.Leader);
    }

    // orig: R119+0x1D18 matched
    /// <summary>"You are relieved": the guards leave, and their desk shows the passwords.</summary>
    private static void RelieveThem(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            ShowPicture(c, Picture);
            c.Screen.Text(10);
            ShowPicture(c, DeskPicture);
            c.Screen.Text(11);
        }
        else
        {
            ShowPicture(c, Picture);
            c.Screen.Text(7);
        }
        Leave(c, -2);
    }

    // orig: R119+0x1E96 / R119+0x1EF0 / R119+0x1F4A matched
    /// <summary>The handler of the option lines the text does not have: the opening text again, then card 0x62.</summary>
    private static void RepeatOpening(CardContext c)
    {
        c.Screen.Text(0);
        c.PassHours(0);
        c.Next = UnusedLineCard;
    }
}
