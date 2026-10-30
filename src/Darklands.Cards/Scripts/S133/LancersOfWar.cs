using Darklands.Core.Characters;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$LastC13": the sixth gate of the last castle. Unless the Sixth Seal has been opened it stays
/// shut. Inside, an army of giant lancers on fire-breathing goblin-beasts guards the Sword of
/// War: a champion may fight one of them, the whole party may attack, or a saint's peace lets
/// the party buy the sword with its blood. Whoever wins the sword is weakened for ever. Every way
/// out returns to the courtyard (−2, the castle's battle 0x57).
/// </summary>
// orig: R133+0x074A (18AC:000A) matched; tests: CardSmokeTests
//   option 0: action R133+0x096E; hint R133+0x0B5A
//   option 1: action R133+0x0B7E; hint R133+0x0C38
//   option 2: action R133+0x0C70; hint R133+0x0E48
//   option 3: action R133+0x0E78; hint R133+0x0F0C
//   option 4: action R133+0x0F44; hint R133+0x0F6C
//   saint pop-up: R133+0x103E, R133+0x10C8, R133+0x1152
//   other: R133+0x0FA4, R133+0x1006
// DORMANT (generated): option slots 10..19 keep the default handler R133+0x1006 ("screwed up.") and are never shown.
// C (generated): card-option-flag-5/6/7/8/9 are never assigned or read here: the card's init zeroes card-option-enabled[0..19], so those lines stay hidden, as in the C#.
// DORMANT (generated): card-item-lines stays all -1, so no line opens an item pop-up.
// C: the card-engine globals DS:EE70 / DS:EE72 the C sets are the pop-up boxes' drawing flags (R043+0x2ACA, see DIVERGENCE_REVIEW); presentation only, not modelled.
public sealed class LancersOfWar : Card
{
    public override int Number => 357;

    private const int Picture = 0;
    private const int ClosedGate = 231;
    private const int Amphitheatre = 251;
    private const int Bleeding = 252;
    private const int Lancers = 253;
    private const int Beaten = 254;
    private const int Victory = 255;

    /// <summary>The sixth Key of the Apocalypse, the Sword of War.</summary>
    private const int SwordOfWar = 0x91;

    public override int Run(CardContext c)
    {
        // C: every picture change of this card is a card-run (the whole card, Show), not card-run-near.
        Show(c, Picture);
        if (!c.Events.FactAnywhere(1, 0x16))
        {
            Show(c, ClosedGate);
            c.Screen.Text(0);
            // C: this early return runs before the table set-up (card-item-lines / card-saint-lines keep the last card's values).
            BackToTheCourtyard(c);
            return c.Next;
        }
        Show(c, Amphitheatre);
        var background = Amphitheatre;
        c.Next = Number;
        var menu = Menu(c, 1);
        menu.Enable(0, 1, 2, 4);
        c.SetCardItems();
        c.SetCardSaints(0x0C, 0x36, 0x3D);
        menu[1] = c.SaintsUsable();

        // C: background is image-buffer: the handlers read its current value (the saint's success changes it).
        return menu.Run(
            // orig: R133+0x096E / R133+0x0B5A matched
            new MenuLine(chance => SingleCombat(c, chance, background), () => ChampionsChance(c)),
            // orig: R133+0x0B7E / R133+0x0C38 matched; pop-up R133+0x103E / _0988 / _0a12 matched
            new MenuLine(_ =>
            {
                if (CallOnASaint(c, menu, background))
                {
                    background = Lancers;
                }
            }, OptionHint.Saying(CardTexts.SelectSaint), () => SaintHint(c)),
            // orig: R133+0x0C70 / R133+0x0E48 matched
            new MenuLine(chance => AttackThemAll(c, chance, background), () => PartysChance(c)),
            // orig: R133+0x0E78 / R133+0x0F0C matched
            new MenuLine(_ => GiveBlood(c), OptionHint.Saying(CardTexts.Unknown)),
            // orig: R133+0x0F44 / R133+0x0F6C matched
            new MenuLine(_ => BackToTheCourtyard(c), OptionHint.Saying(SuccessAssured)));
    }

    /// <summary>The best hand-weapon fighter (half the skill) becomes the champion.</summary>
    // orig: R133+0x0B5A matched
    // C: the hint sets text-slot-chosen-one to top-scoring-member (unsigned char), the champion of line 0.
    private static OptionHint ChampionsChance(CardContext c)
    {
        var chance = c.PartyBestHandWeaponSkill() / 2;
        c.Variables.Chosen[0] = c.BestMember;
        return OptionHint.ChanceOf(chance, c.BestMember);
    }

    /// <summary>Half the party's average Edged Weapons plus half its best hand-weapon skill.</summary>
    // orig: R133+0x0E48 matched
    // C: both queries move top-scoring-member, which this hint does not use (r.b = −2); their order draws nothing.
    private static OptionHint PartysChance(CardContext c)
    {
        var chance = c.PartyAverageSkill((int)Skill.EdgedWeapons) / 2 + c.PartyBestHandWeaponSkill() / 2;
        return OptionHint.ChanceOf(chance, OptionHint.WholeParty);
    }

    /// <summary>The saint pop-up: the member chosen in the box becomes $ChosenOneName.</summary>
    // orig: R133+0x103E matched (also _0988, _0a12)
    private static OptionHint SaintHint(CardContext c)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        var low = c.SaintChanceLow(c.MenuMember, c.CardSaints[c.MenuEntrySlot]);
        var high = c.SaintChanceHigh(c.MenuMember, c.CardSaints[c.MenuEntrySlot]);
        return OptionHint.Say(c.ChanceText(low, high));
    }

    /// <summary>The champion wins the sword and loses half of Strength and Endurance, or loses and a quarter.</summary>
    // orig: R133+0x096E matched
    // C: the roll must be strictly below the chance.
    private void SingleCombat(CardContext c, int chance, int background)
    {
        var champion = c.Variables.Chosen[0];
        if (c.Random(100) < chance)
        {
            Show(c, Victory);
            c.Screen.Text(2);
            Weaken(c, champion, champion, 2);
            c.AddItem(Who.Leader, SwordOfWar);
            BackToTheCourtyard(c);
            return;
        }
        Show(c, Beaten);
        c.Screen.Text(3);
        Weaken(c, champion, champion, 4);
        Show(c, background);
        c.Next = Number;
    }

    /// <summary>
    /// The whole party fights: everybody loses a quarter (victory) or a sixth (defeat) of
    /// Strength and Endurance, both measured by $ChosenOneName's maximum.
    /// </summary>
    // orig: R133+0x0C70 matched
    // C: the measure is text-slot-chosen-one as it stands: whoever the champion hint, a saint box or an earlier card left
    //   there (this line's own hint does not set it). On a defeat the C divides by −6: truncation toward zero, the
    //   same as −(max / 6).
    private void AttackThemAll(CardContext c, int chance, int background)
    {
        var measure = c.Variables.Chosen[0];
        if (c.Random(100) < chance)
        {
            Show(c, Victory);
            c.Screen.Text(7);
            Weaken(c, Who.Everybody, measure, 4);
            c.AddItem(Who.Leader, SwordOfWar);
            BackToTheCourtyard(c);
            return;
        }
        Show(c, Lancers);
        c.Screen.Text(8);
        Weaken(c, Who.Everybody, measure, 6);
        Show(c, background);
        c.Next = Number;
    }

    /// <summary>
    /// Lowers the Strength and Endurance maxima and values of <paramref name="who"/> by the
    /// part 1/<paramref name="part"/> of <paramref name="measure"/>'s maximum, read again before each change.
    /// </summary>
    // orig: the four changes repeated inline in R133+0x096E / _0530 matched
    private static void Weaken(CardContext c, int who, int measure, int part)
    {
        c.AddAttributeMaximum(who, (int)Attribute.Strength, -(c.AttributeMaximum(measure, (int)Attribute.Strength) / part));
        c.AddAttributeMaximum(who, (int)Attribute.Endurance, -(c.AttributeMaximum(measure, (int)Attribute.Endurance) / part));
        c.AddAttribute(who, (int)Attribute.Strength, -(c.AttributeMaximum(measure, (int)Attribute.Strength) / part));
        c.AddAttribute(who, (int)Attribute.Endurance, -(c.AttributeMaximum(measure, (int)Attribute.Endurance) / part));
    }

    /// <summary>
    /// A saint's peace disarms the lancers: only "give blood" and "refuse" remain (text 4).
    /// Returns true when the picture behind the texts became the lancers.
    /// </summary>
    // orig: R133+0x0B7E matched
    // C: returns 0 after a cancelled box, otherwise returns without a value (ignored).
    private bool CallOnASaint(CardContext c, CardMenu menu, int background)
    {
        var result = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuEntrySlot]);
        if (result == -1)
        {
            return false;
        }
        menu[1] = OptionState.Shown;
        if (result != 0)
        {
            menu.Set(OptionState.Hidden, 0, 1, 2);
            menu[3] = OptionState.Enabled;
            menu.Block = 4;
            Show(c, Lancers);
            return true;
        }
        Show(c, Amphitheatre);
        c.Screen.Text(5);
        Show(c, background);
        c.Next = Number;
        return false;
    }

    /// <summary>A quart of blood each: Strength maximum −5, Strength −20 then +1.</summary>
    // orig: R133+0x0E78 matched
    private void GiveBlood(CardContext c)
    {
        c.AddAttributeMaximum(Who.Everybody, (int)Attribute.Strength, -5);
        c.AddAttribute(Who.Everybody, (int)Attribute.Strength, -20);
        c.AddAttribute(Who.Everybody, (int)Attribute.Strength, 1);
        Show(c, Bleeding);
        c.Screen.Text(6);
        c.AddItem(Who.Leader, SwordOfWar);
        BackToTheCourtyard(c);
    }

    // orig: the ending repeated inline in R133+0x074A / _022e / _0530 / _0738 / _0804 matched
    private static void BackToTheCourtyard(CardContext c)
    {
        c.LastCastleFlags |= 0x2020;
        c.BattleMode = 0;
        c.BattleEnemy = 0x57;
        c.BattleSeed = c.Random(1000);
        c.Next = -2;
    }
}
