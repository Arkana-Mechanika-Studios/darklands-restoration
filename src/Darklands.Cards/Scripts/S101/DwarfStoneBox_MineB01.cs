using Darklands.Core.Characters;
using Darklands.Core.Events;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$MineB01": a locked stone box of the dwarf. The party may hammer it open with their weapons
/// (which suffer for it), blow it open with a potion, pick its lock, force it with muscle, or
/// fight. Once it is open the dwarves attack (battle −3).
/// </summary>
// orig: R101+0x0000 (1838:0000) matched; tests: CardSmokeTests
public sealed class DwarfStoneBox_MineB01 : Card
{
    private const int Picture = 200;
    private const int BoxOpensPicture = 201;
    private const int PickingPicture = 202;
    private const int BlastPicture = 203;
    private const int Lockpicks = 0x40;
    private const int MineQuestTag = 0x0C;

    public override int Number => 0xE4;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = Number;
        var menu = Menu(c);
        menu.Enable(0, 2, 3, 4);
        c.PartyMaxSkill((int)Skill.Artifice);
        c.Variables.Chosen[0] = (byte)c.BestMember;
        // UNCERTAIN: as the original has it, carrying item 0x40 greys out the lock picking.
        if (c.CountItems(Who.Everybody, Lockpicks) != 0)
        {
            menu[2] = OptionState.Shown;
        }
        // orig: R101+0x055E (1838:055E) matched
        c.SetCardSaints();
        c.SetCardItems(0x66, 0x63);
        menu[1] = c.ItemsUsable();
        return menu.Run(
            new MenuLine(() => Hammer(c, HammerChance(c)), () => OptionHint.ChanceOf(HammerChance(c), Who.Everybody)),
            new MenuLine(() => BlowItOpen(c, PotionChance(c)), OptionHint.Saying(CardTexts.SelectPotion)),
            new MenuLine(() => PickTheLock(c, -1, menu), OptionHint.Saying(SuccessAssured)),
            new MenuLine(() => ForceIt(c, ForceChance(c), menu), () => OptionHint.ChanceOf(ForceChance(c), Who.Everybody)),
            // orig: R101+0x0504 (1838:0504) matched
            new MenuLine(() =>
                {
                    c.BattleMode = 1;
                    c.BattleEnemy = 0x0B;
                    c.BattleSeed = c.Random(1000);
                    c.Next = -2;
                }, OptionHint.Saying(NoRisk)));
    }

    /// <summary>The box is open: remembered as the mine fact 0x0B (99 hours), and the dwarves attack.</summary>
    // orig: R101+0x01A6 (1838:01A6) matched
    private static void Opened(CardContext c)
    {
        c.Events.Create(Who.Everybody, c.Here, 0, EventKinds.LocationFact, 0x0B, 0, 0, 0x63, 0, MineQuestTag);
        c.BattleMode = 1;
        c.BattleEnemy = 0x0B;
        c.BattleSeed = c.Random(1000);
        c.Next = -3;
    }

    /// <summary>A third of the equipped weapons' qualities of everybody in a slot.</summary>
    // orig: R101+0x0298 (1838:0298) matched
    private static int HammerChance(CardContext c)
    {
        var total = 0;
        for (var i = 0; i < 5; i++)
        {
            var m = c.State.Party.Slot(i);
            if (m.Status != CharacterStatus.Empty)
            {
                // C: the record's weapon quality byte (+0x44) is signed.
                total += (sbyte)m.Weapon.Quality;
            }
        }
        return Math.Clamp(total / 3, 1, 99);
    }

    // orig: R101+0x01E6 (1838:01E6) matched
    private static void Hammer(CardContext c, int chance)
    {
        int damage;
        if (c.Random(100) <= chance)
        {
            c.Screen.Text(1);
            damage = c.Random(15);
            Opened(c);
        }
        else
        {
            c.Screen.Text(2);
            damage = c.Random(20);
            c.Next = 0xE4;
        }
        for (var i = 0; i < 5; i++)
        {
            var m = c.State.Party.Slot(i);
            if (m.Status == CharacterStatus.Empty)
            {
                continue;
            }
            // C: signed byte; at 0 or below it becomes 1.
            var quality = (sbyte)(m.Weapon.Quality - damage);
            m.Weapon = m.Weapon with { Quality = quality <= 0 ? 1 : quality };
            // The equipped weapon's pack stack is found (the search leaves its index) and worn too.
            c.EquippedItem(i, 0);
            var index = c.State.Party.Last.Skill;
            // C: with no weapon equipped the original wears the entry at the index the last search left (best_skill, DS:99EB:
            // another member's search, or an index past this member's stacks): that entry of this member's 64 (a stale one past
            // the last stack, or the memory after the pack for an index outside it: Pack's flat model) loses the quality
            // orig: R101+0x01E6 matched: `inventories[i][best_skill].f3 -= d; if (!...f3) ...f3 = 1;`; tests: OriginalMemoryTests
            // C: the stack's quality is an unsigned byte; only an exact 0 becomes 1 (it may wrap to 255…).
            var worn = (byte)(m.Pack[index].Quality - damage);
            m.Pack[index] = m.Pack[index] with { Quality = worn == 0 ? 1 : worn };
        }
    }

    /// <summary>A potion chosen in the pop-up has a 35 % chance (and names its user); chosen any other way none.</summary>
    // orig: R101+0x05EE (1838:05EE); R101+0x0608 (1838:0608) matched
    private static int PotionChance(CardContext c)
    {
        if (!c.MenuPopup)
        {
            return -1;
        }
        c.Variables.Chosen[0] = c.MenuMember;
        return 0x23;
    }

    // orig: R101+0x02DC (1838:02DC) matched
    private void BlowItOpen(CardContext c, int chance)
    {
        c.TakeItem(c.MenuMember, c.CardItems[c.MenuSlot]);
        if (c.Random(100) <= chance)
        {
            ShowPicture(c, BoxOpensPicture);
            c.Screen.Text(4);
            Opened(c);
            return;
        }
        ShowPicture(c, BlastPicture);
        c.Screen.Text(3);
        ShowPicture(c, Picture);
        c.Next = 0xE4;
    }

    /// <summary>
    /// The lock: its hint is "Success is assured." with no chance (−1), and every roll is at
    /// least 0, so picking always fails.
    /// </summary>
    // orig: R101+0x03B6 (1838:03B6) matched
    private void PickTheLock(CardContext c, int chance, CardMenu menu)
    {
        if (c.Random(100) <= chance)
        {
            // DORMANT: never reached (chance −1).
            ShowPicture(c, PickingPicture);
            c.Screen.Text(5);
            Opened(c);
            return;
        }
        ShowPicture(c, PickingPicture);
        c.Screen.Text(6);
        menu[2] = OptionState.Shown;
        ShowPicture(c, Picture);
        c.Next = 0xE4;
    }

    // orig: R101+0x04E0 (1838:04E0) matched
    private static int ForceChance(CardContext c) => Math.Clamp(c.PartyAverageAttribute((int)Attribute.Strength) << 1, 1, 99);

    /// <summary>Muscle: if it fails, the party reconsiders (0xE6).</summary>
    // orig: R101+0x0480 (1838:0480) matched
    private void ForceIt(CardContext c, int chance, CardMenu menu)
    {
        if (c.Random(100) <= chance)
        {
            ShowPicture(c, BoxOpensPicture);
            c.Screen.Text(4);
            Opened(c);
            return;
        }
        c.Screen.Text(7);
        menu[3] = OptionState.Shown;
        c.Next = 0xE6;
    }
}
