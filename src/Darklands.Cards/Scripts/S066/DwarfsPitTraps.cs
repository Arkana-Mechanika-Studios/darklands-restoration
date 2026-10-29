using Darklands.Core.Characters;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$MineP01": the party has fallen into a mad dwarf's pit, and he sets off one trap after
/// another: water (0), heat (1), adders (2), quicklime (3) and closing walls (4). Climb out,
/// pray, use a potion, block the trap, or bargain; the snakes and the lye lead on to the next
/// trap, everything else ends in a fight with the dwarf's guards (card result −2).
/// </summary>
// orig: R066+0x2FDC (1B35:000C) matched; tests: CardSmokeTests
// orig: handler setup R066+0x3E80 (item pop-up 0f66 "Of course the potion will work!",
// standard saint pop-ups 0fa2/102c/10b6/1140, unused-line handler 0f2e "screwed up.")
public sealed class DwarfsPitTraps : Card
{
    private const int PitPicture = 210;
    private const int TrapPicture = 211;
    private const int RunPicture = 205;
    private const int Water = 0;
    private const int Heat = 1;
    private const int Adders = 2;
    private const int Quicklime = 3;
    private const int Walls = 4;

    public override int Number => 0x5B;

    public override int Run(CardContext c)
    {
        Show(c, PitPicture);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2, 3, 4);
        // The trap is options_block (DS:EE53), the text block of the option list.
        menu.Block = c.Random(5);
        SetUpTheTrap(c, menu);

        return menu.Run(
            new MenuLine(chance => ClimbOut(c, menu, chance), () => ClimbChance(c, menu.Block)),
            new MenuLine(_ => Pray(c, menu), OptionHint.Saying(CardTexts.SelectSaint), c.SaintPopupHint),
            new MenuLine(_ => UseAPotion(c, menu), OptionHint.Saying(CardTexts.SelectPotion), PotionHint(c)),
            new MenuLine(chance => BlockTheTrap(c, menu, chance), () => BlockChance(c)),
            new MenuLine(chance => Bargain(c, menu, chance), () => BargainChance(c)));
    }

    /// <summary>
    /// What can help against this trap: its saints, its potion, which lines are open.
    /// </summary>
    // orig: R066+0x3166
    // C: the lists are not cleared: a later trap only overwrites the slots it names, so items and
    // saints of an earlier trap can remain listed; lines it does not touch keep their state.
    private static void SetUpTheTrap(CardContext c, CardMenu menu)
    {
        menu.Block %= 5;
        switch (menu.Block)
        {
            default:
                menu[2] = OptionState.Hidden;
                c.CardSaints[0] = 0x0D;
                c.CardSaints[1] = 0x4F;
                menu[1] = c.SaintsUsable();
                break;
            case Water:
                menu[2] = OptionState.Hidden;
                c.CardSaints[0] = 0x12;
                break;
            case Heat:
                menu[3] = OptionState.Hidden;
                c.CardItems[0] = 0x74;
                c.CardSaints[0] = 0x14;
                c.CardSaints[1] = 0x5A;
                c.CardSaints[2] = 0x67;
                c.CardSaints[3] = 0x6E;
                menu[2] = c.ItemsUsable();
                menu[1] = c.SaintsUsable();
                break;
            case Adders:
                menu[2] = menu[3] = OptionState.Hidden;
                c.CardSaints[0] = 3;
                c.CardSaints[1] = 0x4A;
                c.CardSaints[2] = 0x6B;
                c.CardSaints[3] = 0x79;
                menu[1] = c.SaintsUsable();
                break;
            case Quicklime:
                menu[3] = OptionState.Hidden;
                c.CardItems[0] = 0x63;
                c.CardSaints[0] = 0x22;
                c.CardSaints[1] = 0x54;
                menu[2] = c.ItemsUsable();
                menu[1] = c.SaintsUsable();
                break;
        }
    }

    /// <summary>The fight with the dwarf's guards: the card returns −2 with battle mode 1.</summary>
    private static void ToTheFight(CardContext c)
    {
        c.Memory.Battle.Mode = 1;
        c.Next = -2;
    }

    /// <summary>Out of the pit: the escape text, then the fight.</summary>
    private static void OutAndFight(CardContext c, int text)
    {
        ShowPicture(c, TrapPicture);
        c.Screen.Text(text);
        ShowPicture(c, RunPicture);
        c.Screen.Text(0x18);
        ToTheFight(c);
    }

    /// <summary>The trap passed with harm, and the dwarf's next one.</summary>
    private static void NextTrap(CardContext c, CardMenu menu, int text, TextFrame gloatFrame = TextFrame.OnPicture)
    {
        ShowPicture(c, TrapPicture);
        c.Screen.Text(text);
        ShowPicture(c, PitPicture);
        c.Screen.Text(0x0A, gloatFrame);
        menu.Block++;
        SetUpTheTrap(c, menu);
    }

    /// <summary>The weakest (by <paramref name="attribute"/>) dies in the trap: gone from the party for good.</summary>
    private static void OneDies(CardContext c, int attribute)
    {
        c.PartyMinAttribute(attribute);
        var victim = c.BestMember;
        c.Variables.Chosen[0] = victim;
        c.SetStatus(victim, CharacterStatus.Empty);
        c.DeleteMember(victim);
    }

    private static void Weaken(CardContext c, int attribute, int loss) => c.AddAttribute(Who.Everybody, attribute, -loss);

    // orig: R066+0x3770
    private static OptionHint ClimbChance(CardContext c, int trap)
    {
        var chance = trap switch
        {
            Water => c.PartyAverageAttribute((int)Attribute.Agility),
            Adders => c.PartyBestHandWeaponSkill() * 2 / 3,
            Quicklime => c.PartyAverageSkill((int)Skill.Alchemy),
            _ => c.PartyAverageAttribute((int)Attribute.Strength),
        };
        return new OptionHint(chance, "", OptionHint.WholeParty);
    }

    /// <summary>Climb (or fight, or swim) out: success costs strength, failure may cost a life.</summary>
    // orig: R066+0x3216
    private void ClimbOut(CardContext c, CardMenu menu, int chance)
    {
        if (c.Succeeds(chance))
        {
            switch (menu.Block)
            {
                case Water:
                    // The armour stays at the bottom.
                    c.RemoveItemsWithFlags(Who.Everybody, 0x0400_0040);
                    OutAndFight(c, 5);
                    return;
                case Heat:
                    Weaken(c, (int)Attribute.Strength, c.Random(8) + 8);
                    OutAndFight(c, 6);
                    return;
                case Adders:
                    Weaken(c, (int)Attribute.Strength, c.Random(4) + 3);
                    NextTrap(c, menu, 7);
                    c.Next = Number;
                    return;
                case Quicklime:
                    Weaken(c, (int)Attribute.Strength, c.Random(4) + 3);
                    Weaken(c, (int)Attribute.Endurance, c.Random(8) + 8);
                    NextTrap(c, menu, 8);
                    c.Next = Number;
                    return;
                case Walls:
                    OutAndFight(c, 9);
                    return;
                default:
                    // DORMANT: the trap is always 0..4 here.
                    return;
            }
        }
        switch (menu.Block)
        {
            case Water:
                c.RemoveItemsWithFlags(Who.Everybody, 0x0400_0040);
                OneDies(c, (int)Attribute.Agility);
                c.PassHours(0);
                OutAndFight(c, 0x0B);
                return;
            case Heat:
                OneDies(c, (int)Attribute.Strength);
                Weaken(c, (int)Attribute.Strength, c.Random(10) + 1);
                c.PassHours(0);
                OutAndFight(c, 0x0C);
                return;
            case Adders:
                Weaken(c, (int)Attribute.Strength, c.Random(8) + 8);
                NextTrap(c, menu, 7);
                return;
            case Quicklime:
                Weaken(c, (int)Attribute.Strength, c.Random(5) + 6);
                Weaken(c, (int)Attribute.Endurance, c.Random(20) + 1);
                // C: this one gloat text is drawn with frame 0 instead of 2 (the same on the picture).
                NextTrap(c, menu, 8);
                return;
            case Walls:
                OneDies(c, (int)Attribute.Agility);
                c.PassHours(0);
                OutAndFight(c, 0x0D);
                return;
            default:
                // DORMANT: the trap is always 0..4 here.
                return;
        }
    }

    /// <summary>A saint saves the party from the trap (from the lye it only spares them, and the dwarf goes on).</summary>
    // orig: R066+0x37B2 (hint R066+0x399A)
    private void Pray(CardContext c, CardMenu menu)
    {
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuEntrySlot]);
        if (answer == -1)
        {
            return;
        }
        if (answer != 0)
        {
            switch (menu.Block)
            {
                case Water:
                    OutAndFight(c, 0x0E);
                    return;
                case Heat:
                    OutAndFight(c, 0x0F);
                    return;
                case Adders:
                    OutAndFight(c, 0x10);
                    return;
                case Quicklime:
                    ShowPicture(c, TrapPicture);
                    c.Screen.Text(0x11);
                    ShowPicture(c, PitPicture);
                    c.Next = Number;
                    return;
                case Walls:
                    OutAndFight(c, 0x16);
                    return;
                default:
                    // DORMANT: the trap is always 0..4 here.
                    return;
            }
        }
        ShowPicture(c, TrapPicture);
        c.Screen.Text(0x12);
        ShowPicture(c, PitPicture);
    }

    /// <summary>The potion pop-up: "Of course the potion will work!"</summary>
    // orig: R066+0x3F36
    private static Func<OptionHint> PotionHint(CardContext c) => () =>
    {
        c.Variables.Chosen[0] = c.MenuMember;
        return new OptionHint(-1, CardTexts.PotionWorks, c.MenuMember);
    };

    /// <summary>Firewall against the heat, Eater Water against the lye: the potion always works (no roll).</summary>
    // orig: R066+0x39D2 (hint R066+0x3A36 "Select a person and a potion.", member −2)
    private static void UseAPotion(CardContext c, CardMenu menu)
    {
        c.TakeItem(c.MenuMember, c.CardItems[c.MenuSlot]);
        ShowPicture(c, TrapPicture);
        c.Screen.Text(menu.Block == Heat ? 0x13 : 0x14);
        ToTheFight(c);
    }

    // orig: R066+0x3BF6
    private static OptionHint BlockChance(CardContext c)
    {
        var artifice = c.PartyMaxSkill((int)Skill.Artifice);
        c.Variables.Chosen[0] = c.BestMember;
        return new OptionHint(artifice, "", c.BestMember);
    }

    /// <summary>The best tinkerer blocks the water or the walls; failing that, one dies.</summary>
    // orig: R066+0x3A72
    private static void BlockTheTrap(CardContext c, CardMenu menu, int chance)
    {
        ShowPicture(c, TrapPicture);
        if (c.Succeeds(chance))
        {
            if (menu.Block == Water)
            {
                c.Screen.Text(0x15);
                ShowPicture(c, PitPicture);
                ToTheFight(c);
                return;
            }
            c.Screen.Text(0x16);
            ShowPicture(c, RunPicture);
            c.Screen.Text(0x18);
            ToTheFight(c);
            return;
        }
        switch (menu.Block)
        {
            case Water:
                OneDies(c, (int)Attribute.Agility);
                c.PassHours(0);
                c.Screen.Text(0x0B);
                ShowPicture(c, RunPicture);
                c.Screen.Text(0x18);
                ToTheFight(c);
                return;
            case Walls:
                OneDies(c, (int)Attribute.Agility);
                c.PassHours(0);
                c.Screen.Text(0x0D);
                ShowPicture(c, RunPicture);
                c.Screen.Text(0x18);
                ToTheFight(c);
                return;
            default:
                // C: against heat, adders and lime a failed attempt changes nothing (the line is hidden for
                // heat and lime and for the adders; it can only be reached there through stale states).
                return;
        }
    }

    // orig: R066+0x3E38
    private static OptionHint BargainChance(CardContext c)
    {
        var value = (c.Attribute(Who.Leader, (int)Attribute.Charisma) + c.Skill(Who.Leader, (int)Skill.SpeakCommon)) / 3;
        return new OptionHint(Math.Clamp(value, 15, 99), "", c.Leader);
    }

    /// <summary>Bargain with the dwarf: he lets the party up (to the fight), or the trap goes on.</summary>
    // orig: R066+0x3C1A
    private static void Bargain(CardContext c, CardMenu menu, int chance)
    {
        if (c.Succeeds(chance))
        {
            c.Screen.Text(0x17, TextFrame.BlankPage);
            ToTheFight(c);
            return;
        }
        switch (menu.Block)
        {
            case Water:
                OneDies(c, (int)Attribute.Agility);
                c.PassHours(0);
                c.Screen.Text(0x0B);
                ShowPicture(c, RunPicture);
                c.Screen.Text(0x18);
                ToTheFight(c);
                return;
            case Heat:
                OneDies(c, (int)Attribute.Strength);
                Weaken(c, (int)Attribute.Strength, c.Random(10) + 1);
                c.PassHours(0);
                c.Screen.Text(0x0C);
                ShowPicture(c, RunPicture);
                c.Screen.Text(0x18);
                ToTheFight(c);
                return;
            case Adders:
                Weaken(c, (int)Attribute.Strength, c.Random(8) + 8);
                c.Screen.Text(7);
                ShowPicture(c, PitPicture);
                c.Screen.Text(0x0A);
                menu.Block++;
                SetUpTheTrap(c, menu);
                ShowPicture(c, PitPicture);
                return;
            case Quicklime:
                Weaken(c, (int)Attribute.Strength, c.Random(10) + 1);
                Weaken(c, (int)Attribute.Endurance, c.Random(20) + 1);
                c.Screen.Text(8);
                ShowPicture(c, PitPicture);
                c.Screen.Text(0x0A);
                menu.Block++;
                SetUpTheTrap(c, menu);
                ShowPicture(c, PitPicture);
                return;
            case Walls:
                OneDies(c, (int)Attribute.Agility);
                c.PassHours(0);
                c.Screen.Text(0x0D);
                ShowPicture(c, RunPicture);
                c.Screen.Text(0x18);
                ToTheFight(c);
                return;
            default:
                // DORMANT: the trap is always 0..4 here.
                return;
        }
    }
}
