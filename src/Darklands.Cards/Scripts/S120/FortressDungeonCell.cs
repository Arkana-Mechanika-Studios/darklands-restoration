using Darklands.Core.Characters;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Witcd08": the party, stripped of weapons and potions, wakes in a reeking dungeon cell
/// with a locked door and a Latin plaque. Reading the plaque, picking the lock, a saint or a potion
/// can open the way out; waiting brings the Templar jailers (card 306). Saint Gregory's earthquake
/// and the explosive potion cost everybody some Endurance; one saint carries the party out into
/// the countryside.
/// </summary>
// orig: R120+0x0970 (18CF:0000) matched; tests: CardSmokeTests
//   option 0: action R120+0x0BE8; hint R120+0x0C10
//   option 1: action R120+0x0C48; hint R120+0x0C50
//   option 2: action R120+0x0C88; hint R120+0x0CC4
//   option 3: action R120+0x0CF8; hint R120+0x0E1A
//   option 4: action R120+0x0E52; hint R120+0x0EF4
//   option 5: action R120+0x0F2C; hint R120+0x0F8E
//   option 6: action R120+0x0FAC; hint R120+0x0FD4
//   option 7: action R120+0x100C; hint R120+0x1014
//   option 8: action R120+0x104C; hint R120+0x106A
//   option 9: action R120+0x10A6; hint R120+0x10C4
//   item pop-up: R120+0x11DA, R120+0x1216, R120+0x1252,
//     R120+0x128E
//   saint pop-up: R120+0x12CA, R120+0x1352, R120+0x13DA,
//     R120+0x1462
//   other: R120+0x1100, R120+0x11A2
// DORMANT (generated): option slots 10..19 keep the default handler R120+0x11A2 ("screwed up.") and are never shown.
// DORMANT (generated): option line(s) 8, 9 are never enabled (every assignment sets their flag to 0).
// UNCERTAIN (generated): the card-engine globals DS:EE70 / DS:EE72 the C sets (meaning unknown) are not reproduced.
public sealed class FortressDungeonCell : Card
{
    public override int Number => 305;

    private const int Picture = 164;
    private const int CountrysidePicture = 88;
    private const int SecretCatchPicture = 186;
    private const int BurstDoorPicture = 187;

    private const int Jailers = 0x132;

    /// <summary>The card after the saint carries the party away.</summary>
    private const int Countryside = 0x0C;

    /// <summary>Lockpicks: they double the chance of picking the lock.</summary>
    private const int Lockpicks = 0x40;

    private const int UnusedLineCard = 0x62;

    public override int Run(CardContext c)
    {
        // The captors take weapons and potions (item kinds 0x04000040 and 0x3F).
        c.RemoveItemsWithFlags(Who.Everybody, 0x0400_0040);
        c.RemoveItemsWithFlags(Who.Everybody, 0x3F);
        Show(c, Picture);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2, 3, 4);
        c.PassHours(2);
        c.SetCardItems(0x66, 0x63);
        c.SetCardSaints(0x23, 0x30, 0x3F, 0x72);
        menu[4] = c.ItemsUsable();
        menu[3] = c.SaintsUsable();

        var unknown = OptionHint.Saying(CardTexts.Unknown);
        return menu.Run(
            new MenuLine(_ => menu.Offer(1, 5, 6, 7), unknown),
            new MenuLine(_ => c.Next = Jailers, unknown),
            new MenuLine(chance => PickTheLock(c, chance), () => LockChance(c)),
            new MenuLine(_ => Pray(c), OptionHint.Saying(CardTexts.SelectSaint), c.SaintPopupChanceHint),
            new MenuLine(_ => UsePotion(c), OptionHint.Saying(CardTexts.SelectPotion), () => c.MemberPopupHint(CardTexts.Unknown)),
            new MenuLine(chance => ReadThePlaque(c, menu, chance), () => PlaqueChance(c)),
            new MenuLine(_ => BackToTheCell(menu), unknown),
            new MenuLine(_ => c.Next = Jailers, unknown),
            new MenuLine(_ => RepeatOpening(c), unknown),
            new MenuLine(_ => RepeatOpening(c), unknown));
    }

    /// <summary>Out of the cell and back into the fortress (battle mode 3, enemy 8).</summary>
    // orig: the common tail of R120+0x0C88 / _0388 / _04e2 / _05bc matched
    private static void Escape(CardContext c)
    {
        c.BattleMode = 3;
        c.BattleEnemy = 8;
        c.Next = -2;
    }

    // orig: R120+0x0FAC matched (also the tail of R120+0x0F2C)
    private static void BackToTheCell(CardMenu menu) => menu.Offer(0, 0, 1, 2, 3, 4);

    /// <summary>The best Artifice, doubled with lockpicks.</summary>
    // orig: R120+0x0CC4 matched
    // C: party_max_skill sets best_member, which the hint reads (as unsigned char); the doubling cannot pass 32767.
    private static OptionHint LockChance(CardContext c)
    {
        var chance = c.PartyMaxSkill((int)Skill.Artifice);
        if (c.CountItems(Who.Everybody, Lockpicks) != 0)
        {
            chance <<= 1;
        }
        return OptionHint.ChanceOf(chance, c.BestMember);
    }

    // orig: R120+0x0C88 matched
    private static void PickTheLock(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            c.Screen.Text(4);
            Escape(c);
            return;
        }
        c.Screen.Text(5);
    }

    // orig: R120+0x0CF8 matched
    // C: an int function that falls off its end without a value (except `return 0` after a cancelled prayer); the
    //   menu ignores it. A menu_slot outside 0..3 shows nothing and stays on the card (no slot beyond 3 exists).
    private static void Pray(CardContext c)
    {
        var result = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (result == -1)
        {
            return;
        }
        if (result == 0)
        {
            c.Screen.Text(10);
            return;
        }
        switch (c.MenuSlot)
        {
            case 0:
                c.Screen.Text(6);
                Escape(c);
                break;
            case 1:
                ShowPicture(c, CountrysidePicture);
                c.Screen.Text(7);
                c.Next = Countryside;
                break;
            case 2:
                c.AddAttribute(Who.Everybody, (int)Core.Characters.Attribute.Endurance, -2);
                ShowPicture(c, BurstDoorPicture);
                c.Screen.Text(8);
                Escape(c);
                break;
            case 3:
                ShowPicture(c, SecretCatchPicture);
                c.Screen.Text(9);
                Escape(c);
                break;
        }
    }

    /// <summary>The first potion blasts the door open (−2 Endurance for all); the other eats the lock away.</summary>
    // orig: R120+0x0E52 matched
    private static void UsePotion(CardContext c)
    {
        c.TakeItem(c.MenuMember, c.CardItems[c.MenuSlot]);
        if (c.MenuSlot != 0)
        {
            ShowPicture(c, SecretCatchPicture);
            c.Screen.Text(12);
        }
        else
        {
            c.AddAttribute(Who.Everybody, (int)Core.Characters.Attribute.Endurance, -2);
            ShowPicture(c, BurstDoorPicture);
            c.Screen.Text(11);
        }
        Escape(c);
    }

    /// <summary>The best Latin.</summary>
    // orig: R120+0x0F8E matched
    private static OptionHint PlaqueChance(CardContext c) =>
        OptionHint.ChanceOf(c.PartyMaxSkill((int)Skill.SpeakLatin), c.BestMember);

    // orig: R120+0x0F2C matched
    // C: an int function: the success branch returns without a value, the failure returns options_block (0); ignored.
    private static void ReadThePlaque(CardContext c, CardMenu menu, int chance)
    {
        if (c.Random(100) < chance)
        {
            c.Screen.Text(2);
            Escape(c);
            return;
        }
        c.Screen.Text(3);
        BackToTheCell(menu);
    }

    /// <summary>The handler of the option lines the text does not have: the opening text again, then card 0x62.</summary>
    // orig: R120+0x104C / _0736 matched
    private static void RepeatOpening(CardContext c)
    {
        c.Screen.Text(0);
        c.PassHours(0);
        c.Next = UnusedLineCard;
    }
}
