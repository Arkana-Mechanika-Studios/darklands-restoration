using Darklands.Core.Characters;
using Darklands.Core.Events;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$MineH00": a friendly dwarf meets the party past a riddle door, says what he says
/// according to the door the party came through, and the mine's foes attack (battle −3).
/// Doors 0x5B..0xC9: he warns of the menace below. Doors 0xCA..0xD0: a weapon fit for the
/// party's best hand-weapon skill, unless they have a good one, in which case he heals.
/// Doors 0xD1..0xD7: he heals. Doors 0xD8..0xDE: a magic item, once. The last doors and the
/// cards 0x153..0x156: a crowd of joyful dwarfs.
/// </summary>
/// <remarks>The card has no menu: it ends at once.</remarks>
// orig: R101+0x11F4 (1957:0004) matched; tests: CardSmokeTests
// DORMANT: R101+0x16DA (1957:04EA), the usual option reset, is in the file but never called.
public sealed class FriendlyDwarf_MineH00 : Card
{
    private const int Picture = 207;
    private const int DoorPicture = 200;
    private const int CrowdPicture = 203;
    private const int MineQuestTag = 0x0C;

    /// <summary>The magic item was given here (fact 7, forever).</summary>
    private const int GiftGiven = 7;

    public override int Number => 0xE7;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = Number;
        var door = c.State.PreviousCard;
        if (door is 0x5C or 0x71 or 0xC5 or 0xC6 or 0xC7 or 0xC8 or 0xC9 or 0x5B)
        {
            c.Screen.Text(0);
            // C: % keeps the sign; a negative detail shows no second text.
            switch (c.MineDetail() % 3)
            {
                case 0:
                    c.Screen.Text(1);
                    break;
                case 1:
                    c.Screen.Text(2);
                    break;
                case 2:
                    c.Screen.Text(3);
                    break;
            }
            Attack(c);
        }
        else if (door is 0xCA or 0xCB or 0xCC or 0xCD or 0xCE or 0xCF or 0xD0)
        {
            GiveAWeapon(c);
            Attack(c);
        }
        else if (door is 0xD1 or 0xD2 or 0xD3 or 0xD4 or 0xD5 or 0xD6 or 0xD7)
        {
            Heal(c);
            Attack(c);
        }
        else if (door is 0xD8 or 0xD9 or 0xDA or 0xDB or 0xDC or 0xDD or 0xDE)
        {
            if (!c.Events.Fact(MineQuestTag, GiftGiven, c.Here))
            {
                // Up to ten draws of a magic item (flag 0x1000) the party does not carry yet.
                // C: while (count(item) && tries--): post-decrement, so ten draws at most.
                var tries = 9;
                int item;
                do
                {
                    item = c.RandomItemWithFlags(0x1000);
                }
                while (c.CountItems(Who.Everybody, item) != 0 && tries-- != 0);
                c.AddItem(c.Leader, item);
                c.Events.Create(Who.Everybody, c.Here, 0x5A, EventKinds.LocationFact, GiftGiven, 0x63, 0, 0x270F, 0, MineQuestTag);
                c.Screen.Text(7);
            }
            else
            {
                ShowPicture(c, DoorPicture);
                c.Screen.Text(8);
            }
            Attack(c);
        }
        else if (door is 0xDF or 0xE0 or 0xE1 or 0xE2 or 0x153 or 0x154 or 0x155 or 0x156)
        {
            ShowPicture(c, CrowdPicture);
            switch (c.MineDetail() % 3)
            {
                case 0:
                    c.Screen.Text(9);
                    break;
                case 1:
                    c.Screen.Text(10);
                    break;
                case 2:
                    c.Screen.Text(11);
                    break;
            }
            Attack(c);
        }
        // Coming from any other card the dwarf says nothing and the card returns its own
        // number (the game shows it again), as the original does.
        return c.Next;
    }

    private static void Attack(CardContext c)
    {
        c.BattleMode = 1;
        c.Next = -3;
    }

    /// <summary>Endurance back to full for everybody, if anybody in a slot lacks some.</summary>
    private static void Heal(CardContext c)
    {
        int i;
        for (i = 0; i < 5; i++)
        {
            if (c.Status(i) != CharacterStatus.Empty && c.Attribute(i, (int)Attribute.Endurance) < c.AttributeMaximum(i, (int)Attribute.Endurance))
            {
                c.Screen.Text(5);
                c.AddAttribute(Who.Everybody, (int)Attribute.Endurance, 0x63);
                break;
            }
        }
        if (i >= 5)
        {
            c.Screen.Text(6);
        }
    }

    /// <summary>
    /// A weapon of quality 45–54 for the skill the party's best hand-weapon fighter uses (a bow
    /// and 2 × 25-quality arrows otherwise), unless the party already has one of quality 35.
    /// </summary>
    private static void GiveAWeapon(CardContext c)
    {
        c.PartyBestHandWeaponSkill();
        c.Variables.Chosen[0] = (byte)c.BestMember;
        // C: the skill index is read as an unsigned byte.
        var (kind, weapon) = (byte)c.BestSkillFound switch
        {
            0 => (1u, 0x08),
            1 => (2u, 0x0A),
            2 => (8u, 0x10),
            3 => (4u, 0x14),
            // DORMANT: the best hand-weapon skill is one of 0..3; 4 (thrown) and others are not found.
            4 => (0x10u, 0x17),
            _ => (0x20u, 0x1D),
        };
        if (c.BestQualityWithFlags(Who.Everybody, kind) >= 0x23)
        {
            Heal(c);
            return;
        }
        c.Variables.Chosen[0] = c.Leader;
        c.Screen.Text(4);
        GiveStack(c, weapon, c.Random(10) + 0x2D, kind == 0x10 ? 0x14 : 1);
        if (kind == 0x20)
        {
            // C: kind is 0x20 here, so the count expression gives 1.
            GiveStack(c, 0x22, 0x19, kind == 0x10 ? 0x14 : 1);
            GiveStack(c, 0x22, 0x19, kind == 0x10 ? 0x14 : 1);
        }
    }

    /// <summary>A stack built from the item's definition (type, weight) with a given quality and count.</summary>
    private static void GiveStack(CardContext c, int code, int quality, int count)
    {
        var item = c.State.Item(code);
        c.AddItemStack(c.Leader, new ItemStack(code, (byte)item.Type, (byte)quality, (byte)count, (byte)item.Weight));
    }
}
