using Darklands.Core.Characters;
using Darklands.Core.Events;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Drago01": crossing the land a dragon has blighted. The worse the blight (the dragon
/// quest's fact 4/1, which grows on each crossing), the worse what befalls the party: rust,
/// corroded armour, spoilt potions, polluted souls, ruined outlaws, the dragon's spoor, and at
/// the worst the dragon itself, to hide from and follow, outride, fight with potions, saints or
/// weapons.
/// </summary>
// Reconstructed from the disassembly (no C):
// orig: R081+0x1848 (19BC:0008) asm; tests: CardSmokeTests
// orig: R081+0x2884 (19BC:1044) asm; R081+0x2902 (19BC:10C2) asm; tests: CardSmokeTests
// DORMANT: the "screwed up." default handler of the unlisted lines 5..19 (never offered).
public sealed class DragonBlightedLand : Card
{
    public const int CardNumber = 0x97;

    /// <summary>The card's MSG name (the card table has none for this card).</summary>
    private const string MsgName = "$Drago01";

    private const int Picture = 286;
    private const int WastePicture = 237;
    private const int DragonQuest = 4;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        c.Screen.Show(MsgName, PictureNames.Of(Picture));
        c.Next = CardNumber;
        var menu = Menu(c, null);
        menu.Enable(0, 4);
        menu[1] = OptionState.Shown;
        // The blighted land is open country.
        c.State.CurrentLocation = -1;
        if (c.PartyGet(5) != 0)
        {
            menu[1] = OptionState.Enabled;
        }
        var blight = c.Events.FactValueAnywhere(DragonQuest, 1);
        // C: the growth is drawn as an argument of event_update, before the event is chosen.
        var growth = c.Random(2);
        c.Events.Update(-1, -1, 0x1C, 1, growth, DragonQuest, EventUpdate.Add);
        var what = blight < 2 ? c.Random(3) : blight < 4 ? c.Random(8) : c.Random(10) + 3;
        // C: options_block (DS:EE53) is written only on the dragon's three ways in (R081+0x20B2 / 0x20E4 / 0x2114); when the ruined
        // outlaws' battle keeps the card, its menu shows the block the card before left. Direct call j_dragon.py (local/batch/evid1o):
        // R081+0x1848 with DS:EE53 = 0x2B, the blight query stubbed to 5, what = 7, run_battle stubbed to 5 -> card_options(0x2B).
        // C: options_block is CardContext.OptionsBlock, which every card that writes it keeps (OptionsBlockCards and the cards' own stores).
        switch (what)
        {
            case 0:
                c.Screen.Text(0);
                c.Next = 0x0C;
                break;
            case 1:
                ShowPicture(c, 287);
                c.Screen.Text(1);
                c.Next = 0x0C;
                break;
            case 2:
                c.Screen.Text(2);
                c.Next = 0x0C;
                break;
            case 3:
                ShowPicture(c, WastePicture);
                c.Screen.Text(3);
                // Acid rain rusts the weapons (not types 0x17..0x19, 0x40..0x42).
                Wear(c, type => type is not (0x40 or 0x41 or 0x42 or 0x17 or 0x18 or 0x19) && type <= 0x6B, blight + 1);
                c.Next = 0x0C;
                break;
            case 4:
                ShowPicture(c, 287);
                c.Screen.Text(4);
                // Corroding mists: the armour (types 0x43..0x54).
                Wear(c, type => type is >= 0x43 and <= 0x54, blight + 2);
                c.Next = 0x0C;
                break;
            case 5:
                if (!SpoilThePotions(c))
                {
                    ShowPicture(c, WastePicture);
                    c.Screen.Text(3);
                    Wear(c, type => type < 0x43, blight + 1);
                }
                else
                {
                    ShowPicture(c, 332);
                    c.Screen.Text(5);
                    // C: the grass fire spoils the potions a second time.
                    SpoilThePotions(c);
                }
                c.Next = 0x0C;
                break;
            case 6:
                ShowPicture(c, WastePicture);
                c.Screen.Text(6);
                PollutedSouls(c);
                c.Next = 0x0C;
                break;
            case 7:
                ShowPicture(c, WastePicture);
                c.Screen.Text(7);
                RuinedOutlaws(c);
                break;
            case 8:
                c.PartyMaxSkill(8);
                c.Variables.Chosen[0] = (byte)c.BestMember;
                ShowPicture(c, WastePicture);
                c.Screen.Text(8);
                c.Next = 0x0C;
                break;
            case 9:
                ShowPicture(c, 122);
                c.Screen.Text(9);
                c.Next = 0x0C;
                break;
            case 10:
                // The dragon descends from the sky.
                ShowPicture(c, WastePicture);
                c.OptionsBlock = 0x0A;
                break;
            case 11:
                // It bursts out of the earth.
                ShowPicture(c, 226);
                c.OptionsBlock = 0x0B;
                break;
            default:
                // It crawls out from behind the boulders (12). C: only the second picture
                // buffer is changed here, so later redraws keep the card's picture.
                ShowPicture(c, 226);
                c.OptionsBlock = 0x0C;
                break;
        }
        c.SetCardItems(0x67, 0x64);
        c.SetCardSaints(0x17, 0x1D, 0x6C);
        menu[2] = c.ItemsUsable();
        menu[3] = c.SaintsUsable();
        if (c.Next != CardNumber)
        {
            return c.Next;
        }
        return menu.Run(
            // orig: R081+0x2278 (19BC:0A38) asm; R081+0x2280 (19BC:0A40) asm; tests: CardSmokeTests
            // C: no roll: the hint's chance is shown, and the party always follows the dragon.
            new MenuLine(() => c.Next = 0x99, hint: () => OptionHint.ChanceOf(
                CardContext.ClampRange(0, 99, (c.Attribute(Who.Leader, 5) + c.Skill(Who.Leader, 0x0B)) / 2), (sbyte)c.Leader)),
            new MenuLine(chance => RideAway(c, chance), () => RidingChance(c)),
            // orig: R081+0x24E2 (line 2); R081+0x293A (19BC:10FA) asm; R081+0x29CE (19BC:118E) asm; tests: CardSmokeTests
            new MenuLine(chance => UseAPotion(c, chance), OptionHint.Saying(CardTexts.SelectPotion),
                () => PotionHint(c, c.MenuMember)),
            // orig: R081+0x25FA (line 3); R081+0x2A62 (19BC:1222) asm; R081+0x2AEC (19BC:12AC) asm; R081+0x2B76 (19BC:1336) asm;
            // tests: CardSmokeTests
            new MenuLine(() =>
            {
                if (!CallOnASaint(c))
                {
                    menu[3] = OptionState.Shown;
                }
            }, OptionHint.Saying(CardTexts.SelectSaint), c.SaintPopupHint),
            new MenuLine(() => Fight(c), OptionHint.Saying("It is never easy to fight a dragon")));
    }

    /// <summary>Each item of the chosen types in every member's pack loses up to <paramref name="below"/> − 1 quality.</summary>
    private static void Wear(CardContext c, Func<int, bool> affected, int below)
    {
        for (var m = 0; m < 5; m++)
        {
            if (c.Status(m) == CharacterStatus.Empty)
            {
                continue;
            }
            var pack = c.State.Party.Members[m].Pack;
            for (var i = 0; i < pack.Count; i++)
            {
                if (affected(pack[i].Type))
                {
                    var loss = c.Random(below);
                    pack[i] = pack[i] with { Quality = (byte)CardContext.ClampRange(0, 99, pack[i].Quality - loss) };
                }
            }
        }
    }

    /// <summary>
    /// The heat spoils potions (types 0x6D..0x83): one in ten is lost outright, and of the
    /// others the first and then one in twenty. Returns whether one was spoilt by the second rule.
    /// </summary>
    private static bool SpoilThePotions(CardContext c)
    {
        var spoilt = false;
        for (var m = 0; m < 5; m++)
        {
            if (c.Status(m) == CharacterStatus.Empty)
            {
                continue;
            }
            var pack = c.State.Party.Members[m].Pack;
            for (var i = 0; i < pack.Count; i++)
            {
                if (pack[i].Type is > 0x99 or < 0x6D or >= 0x84)
                {
                    continue;
                }
                // C: the index moves on after a stack is used up, so the next stack is skipped (as in the original).
                if (c.Random(100) <= 10)
                {
                    c.TakeItemAt(m, i);
                    continue;
                }
                if (c.Random(100) <= 5 || !spoilt)
                {
                    c.TakeItemAt(m, i);
                }
                spoilt = true;
            }
        }
        return spoilt;
    }

    /// <summary>The darkness halves divine favour where virtue (skill 9) is at least as high, else lowers it to the virtue.</summary>
    private static void PollutedSouls(CardContext c)
    {
        for (var m = 0; m < c.PartySize; m++)
        {
            if (c.Status(m) == CharacterStatus.Empty)
            {
                continue;
            }
            var favour = c.Attribute(m, 6);
            var virtue = c.Skill(m, 9);
            c.SetAttribute(m, 6, virtue >= favour ? favour / 2 : virtue);
        }
    }

    private static void RuinedOutlaws(CardContext c)
    {
        // C: arguments are evaluated right to left: rand_mod(4) (second group) before rand_mod(3).
        var third = c.PartyLoreLevel() / 4 + 1;
        var second = c.Random(4) + c.PartyLoreLevel() / 3 + 1;
        var first = c.Random(3) + c.PartyLoreLevel() / 4 + 3;
        switch (c.RunBattle(0x3F, c.Here, 0x0B, first, second, 4, third, 1, -1, 0, 0))
        {
            case 0:
            case 1:
            case 2:
                c.DropAway();
                ShowPicture(c, Picture);
                c.Screen.Text(0x0F);
                c.PassHours(1);
                c.Next = 0x0C;
                break;
            case 3:
            case 4:
                ShowPicture(c, 177);
                c.Screen.Text(0x10);
                c.PassHours(3);
                c.Injure(Who.Everybody, 2, 5, 10);
                c.Next = 0x0C;
                break;
            default:
                // COVERAGE GAP (reachable: result 5 with a member left Away by an earlier flight; no oracle run ended so): other battle results keep the card (its menu follows).
                break;
        }
    }

    // orig: R081+0x246A (19BC:0C2A) asm; tests: CardSmokeTests
    // C: each member's value replaces the previous one (no sum), then is divided by the number of members.
    private static OptionHint RidingChance(CardContext c)
    {
        var value = 0;
        var members = 0;
        for (var m = 0; m < 5; m++)
        {
            if (c.Status(m) == CharacterStatus.Empty)
            {
                continue;
            }
            members++;
            value = c.Skill(m, 0x11) + c.BestQualityWithFlags(m, 0x2000);
        }
        var chance = CardContext.ClampRange(1, 99, value / members);
        if (c.PartyGet(5) == 0)
        {
            chance = 0;
        }
        return OptionHint.ChanceOf(chance, OptionHint.WholeParty);
    }

    /// <summary>Outriding the dragon tires the horses (item type 0x6C); failing costs the worst rider his mount.</summary>
    // orig: R081+0x22C8 (19BC:0A88) asm; tests: CardSmokeTests
    private static void RideAway(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            ShowPicture(c, WastePicture);
            c.Screen.Text(0x0D);
            c.PassHours(3);
            for (var m = 0; m < 5; m++)
            {
                if (c.Status(m) == CharacterStatus.Empty)
                {
                    continue;
                }
                var pack = c.State.Party.Members[m].Pack;
                for (var i = 0; i < pack.Count; i++)
                {
                    if (pack[i].Type == 0x6C)
                    {
                        var q = pack[i].Quality;
                        // C: clamp_range(1, q - 1, q - rand): the upper bound wins when they cross.
                        pack[i] = pack[i] with { Quality = (byte)CardContext.ClampRange(1, q - 1, q - c.Random(10)) };
                    }
                }
            }
            c.Next = 0x0C;
            return;
        }
        var worst = 1000;
        for (var m = 0; m < c.PartySize; m++)
        {
            if (c.Status(m) == CharacterStatus.Empty)
            {
                continue;
            }
            var riding = c.Skill(m, 0x11) + c.BestQualityWithFlags(m, 0x2000);
            if (riding < worst)
            {
                worst = riding;
                c.Variables.Chosen[0] = m;
            }
        }
        ShowPicture(c, WastePicture);
        c.Screen.Text(0x0E);
        c.RemoveItemsWithFlags(c.Variables.Chosen[0], 0x2000);
        c.PracticeSkill(Who.Everybody, 0x11, 0, 10, 0);
        c.Injure(Who.Everybody, 2, 5, 20);
        c.PassHours(2);
        // C: next_card is not changed: the dragon is still there.
    }

    /// <summary>Mounted, the party's riding (each slot below party_size); on foot, its agility.</summary>
    // orig: R081+0x293A (19BC:10FA) asm; R081+0x29CE (19BC:118E) asm
    // C: an empty slot below party_size counts too.
    private static OptionHint PotionHint(CardContext c, int member)
    {
        c.Variables.Chosen[0] = member;
        var sum = 0;
        int chance;
        if (c.PartyGet(5) != 0)
        {
            for (var m = 0; m < c.PartySize; m++)
            {
                sum += c.Skill(m, 0x11) + c.BestQualityWithFlags(m, 0x2000);
            }
            chance = CardContext.ClampRange(1, 99, sum / c.PartySize);
        }
        else
        {
            for (var m = 0; m < c.PartySize; m++)
            {
                sum += c.Attribute(m, 2);
            }
            chance = sum / c.PartySize;
        }
        return OptionHint.ChanceOf(chance, member);
    }

    // orig: R081+0x24E2 (19BC:0CA2) asm; R081+0x25C2 (19BC:0D82) asm; tests: CardSmokeTests
    private static void UseAPotion(CardContext c, int chance)
    {
        // C: the chance is the menu's l_a.a: the pop-up hint's, or -1 from the option line itself (the potion then always fails).
        c.TakeItem(c.MenuMember, c.CardItems[c.MenuSlot]);
        if (c.Random(100) <= chance)
        {
            ShowPicture(c, 109);
            c.Screen.Text(c.CardItems[c.MenuSlot] == 0x67 ? 0x11 : 0x12);
            c.PassHours(2);
            c.Next = 0x0C;
            return;
        }
        var victim = c.Random(c.PartySize);
        c.Variables.Chosen[0] = victim;
        c.SetAttribute(victim, 0, 0);
        c.SetAttribute(victim, 1, 5);
        ShowPicture(c, WastePicture);
        c.Screen.Text(0x13);
        c.PassHours(1);
        c.Next = 0x0C;
    }

    /// <summary>Returns false when the saint does not answer (the line is then shown only).</summary>
    // orig: R081+0x25FA (19BC:0DBA) asm; R081+0x2738 (19BC:0EF8) asm; tests: CardSmokeTests
    private static bool CallOnASaint(CardContext c)
    {
        switch (c.MenuSlot)
        {
            case 0:
                c.NameToSlot(1, 0x17, 0);
                break;
            case 1:
                c.NameToSlot(1, 0x1D, 0);
                break;
            case 2:
                c.NameToSlot(1, 0x6C, 0);
                break;
            default:
                // DORMANT: slot 3 holds no saint: no name.
                break;
        }
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuEntrySlot]);
        if (answer == -1)
        {
            return true;
        }
        if (answer != 0)
        {
            if (c.Random(100) <= 90)
            {
                c.PassHours(2);
                ShowPicture(c, WastePicture);
                c.Screen.Text(0x15);
                c.Next = 0x0C;
                return true;
            }
            c.Injure(c.Variables.Chosen[0], 2, 5, 20);
            ShowPicture(c, WastePicture);
            c.Screen.Text(0x14);
            c.Next = 0x0C;
            return true;
        }
        c.Injure(c.Variables.Chosen[0], 2, 5, 35);
        ShowPicture(c, WastePicture);
        c.Screen.Text(0x16);
        return false;
    }

    /// <summary>Weapon skills and strength against a roll of 500: either way the dragon mauls the party and leaves.</summary>
    // orig: R081+0x2770 (19BC:0F30) asm; R081+0x2856 (19BC:1016) asm; tests: CardSmokeTests
    private static void Fight(CardContext c)
    {
        var might = 0;
        var strength = 0;
        for (var m = 0; m < c.PartySize; m++)
        {
            might += c.BestWeaponSkill(m);
            strength += c.Attribute(m, 1);
        }
        var roll = c.Random(500);
        c.PassHours(1);
        c.Injure(Who.Everybody, 2, 5, 10);
        if (might + strength < roll)
        {
            ShowPicture(c, WastePicture);
            c.Screen.Text(0x18);
        }
        else
        {
            ShowPicture(c, 287);
            c.Screen.Text(0x17);
        }
        c.Next = 0x0C;
    }
}
