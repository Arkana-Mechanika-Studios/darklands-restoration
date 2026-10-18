using Darklands.Core.Characters;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$McGuf09": paid assassins lie in ambush on the road of a commission. A keen-eyed member may
/// sense them first (then pray for insight, flush them with a potion, or walk on); otherwise, or
/// afterwards, the knight's band charges: fight, pray, or flee on horseback.
/// </summary>
// orig: R070+0x090E (18C8:000E) matched; tests: CardSmokeTests
// orig: handler setup R070+0x12F0 (unused-line handler 0a92 "screwed up.")
public sealed class AssassinsAmbush : Card
{
    private const int OverlandMap = 0x0C;

    public override int Number => 0x69;

    public override int Run(CardContext c)
    {
        Show(c, 0);
        c.Next = Number;
        var menu = Menu(c, 0);
        var keenest = c.PartyMaxAttributePlusSkill((int)Attribute.Perception, (int)Skill.Woodwise);
        c.Variables.Chosen[0] = c.BestMember;
        // C: 3 * value / 4 with signed division.
        if (c.Random(100) <= ((keenest << 1) + keenest) / 4)
        {
            // The ambush is sensed in time.
            menu.Block = 0;
            c.SetCardItems(0x5F, 0x65, 0x62, 0x66);
            c.SetCardSaints(0x11, 0x35, 0x44, 0x83);
            menu[0] = OptionState.Enabled;
            menu[1] = c.SaintsUsable();
            menu[2] = c.ItemsUsable();
            menu[3] = OptionState.Enabled;
            menu[9] = OptionState.Enabled;
        }
        else
        {
            c.SetCardItems(0x5F, 0x65, 0x62, 0x66);
            TheyCharge(c, menu);
        }

        return menu.Run(
            new MenuLine(_ => TheyCharge(c, menu), OptionHint.Saying(CardTexts.Unknown)),
            new MenuLine(_ => PrayForInsight(c), OptionHint.Saying(CardTexts.SelectSaint), () => SaintHint(c)),
            new MenuLine(_ => FlushThemOut(c, menu), OptionHint.Saying(CardTexts.SelectPotion), () => PotionHint(c, menu)),
            new MenuLine(_ => TheyHaveSeenYou(c, menu, 5), OptionHint.Saying(CardTexts.Unknown)),
            new MenuLine(_ => Fight(c, menu), OptionHint.Saying(CardTexts.EnterCombat)),
            new MenuLine(_ => PrayInTheFight(c, menu), OptionHint.Saying(CardTexts.SelectSaint), () => SaintHint(c)),
            new MenuLine(_ => Fight(c, menu), OptionHint.Saying(CardTexts.EnterCombat)),
            new MenuLine(chance => RideAway(c, menu, chance), () => MountedChance(c)),
            new MenuLine(chance => { c.TakeItem(c.MenuMember, c.CardItems[c.MenuSlot]); RideAway(c, menu, chance); }, OptionHint.Saying(CardTexts.SelectPotion),
                () => PotionHint(c, menu)),
            new MenuLine(chance => { c.Screen.Text(0x0F); RideAway(c, menu, chance); }, () => MountedChance(c)));
    }

    /// <summary>
    /// The knight's band charges (block 1): fight, pray (the saint line is greyed out when nobody
    /// notices in time, by the best Perception), or nothing else.
    /// </summary>
    // orig: R070+0x0D34 (line 0, hint 04a2) and the same setup in R070+0x090E
    private void TheyCharge(CardContext c, CardMenu menu)
    {
        c.SetCardSaints(0x36, 0x3D, 0x3F, 0x50);
        menu[0] = menu[1] = menu[2] = menu[3] = OptionState.Hidden;
        menu.Block = 1;
        menu[4] = OptionState.Enabled;
        // C: both operands are calls; party_max_attr draws nothing, so their order does not matter.
        menu[5] = c.PartyMaxAttribute((int)Attribute.Perception) < c.Random(100) ? OptionState.Shown : c.SaintsUsable();
        menu[6] = menu[7] = menu[8] = menu[9] = OptionState.Hidden;
        c.Next = Number;
    }

    /// <summary>The fight is joined: fight, pray, flee (with a potion) or ride (block 5 or 6).</summary>
    private void TheyHaveSeenYou(CardContext c, CardMenu menu, int block)
    {
        menu.Block = block;
        c.SetCardItems(0x5F, 0x65, 0x61, 0x66);
        menu[0] = menu[1] = menu[2] = menu[3] = OptionState.Hidden;
        menu[4] = OptionState.Enabled;
        menu[5] = c.SaintsUsable();
        menu[6] = OptionState.Hidden;
        menu[7] = OptionState.Enabled;
        menu[8] = c.ItemsUsable();
        menu[9] = OptionState.Hidden;
        c.Next = Number;
    }

    /// <summary>Caught on the run: only the fight or a prayer is left (block 10).</summary>
    private void StandAndFight(CardContext c, CardMenu menu)
    {
        menu.Block = 0x0A;
        menu[0] = menu[1] = menu[2] = menu[3] = OptionState.Hidden;
        menu[4] = OptionState.Enabled;
        menu[5] = c.SaintsUsable();
        menu[6] = menu[7] = menu[8] = menu[9] = OptionState.Hidden;
        c.Next = Number;
    }

    /// <summary>This card's saint pop-up names the praying member but does not make $ChosenOneName of them.</summary>
    // orig: R070+0x155A, 0ce2, 0d6a, 0df2
    private static OptionHint SaintHint(CardContext c)
    {
        var saint = c.CardSaints[c.MenuSlot];
        var low = c.SaintChanceLow(c.MenuMember, saint);
        var high = c.SaintChanceHigh(c.MenuMember, saint);
        return new OptionHint(-1, c.ChanceText(low, high), c.MenuMember);
    }

    /// <summary>The potion pop-up: a chance only while fleeing (blocks 5 and 6), the slowest rider's.</summary>
    // orig: R070+0x13CA, 0b2e, 0b92, 0bf6
    private static OptionHint PotionHint(CardContext c, CardMenu menu)
    {
        if (menu.Block != 5 && menu.Block != 6)
        {
            return new OptionHint(-1, CardTexts.Unknown, c.MenuMember);
        }
        return new OptionHint(c.PartyGet(5) != 0 ? c.PartyMinMountedAgility() : 0, "", c.MenuMember);
    }

    // orig: R070+0x113E, R070+0x12CA
    private static OptionHint MountedChance(CardContext c) =>
        new(c.PartyGet(5) != 0 ? c.PartyMinMountedAgility() : 0, "", OptionHint.WholeParty);

    /// <summary>A prayer for insight before the ambush.</summary>
    // orig: R070+0x0DDA (hint 051e)
    private void PrayForInsight(CardContext c)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (answer == -1)
        {
            return;
        }
        // C: answer - 1 == 0: only an answer of exactly 1 works.
        c.Screen.Text(answer == 1 ? 2 : 4, TextFrame.BlankPage);
        c.Next = Number;
    }

    /// <summary>A potion thrown among the hidden assassins: they charge (block 6).</summary>
    // orig: R070+0x0E56 (hint 05c8); "walk on" R070+0x0F00 (block 5, hint 065c)
    private void FlushThemOut(CardContext c, CardMenu menu)
    {
        c.TakeItem(c.MenuMember, c.CardItems[c.MenuSlot]);
        TheyHaveSeenYou(c, menu, 6);
    }

    /// <summary>A prayer in the fight: a blinding light drives them off, or the fight goes on.</summary>
    // orig: R070+0x0FD6 (hint 073c)
    private void PrayInTheFight(CardContext c, CardMenu menu)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (answer == -1)
        {
            return;
        }
        if (answer == 1)
        {
            c.Screen.Text(7);
            c.PassHours(1);
            BackToTheMap(c);
            return;
        }
        c.Screen.Text(4, TextFrame.BlankPage);
        Fight(c, menu);
    }

    /// <summary>Ride for it (by the slowest rider); caught, the party must stand and fight.</summary>
    // orig: R070+0x10B6 (line 7), R070+0x1164 (line 8, with a potion), R070+0x1238 (line 9)
    private void RideAway(CardContext c, CardMenu menu, int chance)
    {
        if (c.Succeeds(chance))
        {
            c.PassHours(Core.Time.TimeOfDay.HoursUntil(c.State.Date.Hour, 18));
            c.Screen.Text(9);
            c.PassHours(Core.Time.TimeOfDay.HoursUntil(c.State.Date.Hour, 5) + 24);
            BackToTheMap(c);
            return;
        }
        StandAndFight(c, menu);
    }

    // orig: R070+0x0C24 (lines 4 and 6: 0694, 0774; hints 069e, 077e)
    private void Fight(CardContext c, CardMenu menu)
    {
        var troops = c.Random(5) + 3;
        switch (c.RunBattle(9, -2, 7, 2, troops, 3, 3, 1, -1, 0, 0))
        {
            case 0:
                c.Variables.Chosen[0] = c.Leader;
                c.Screen.Text(0x0C);
                BackToTheMap(c);
                break;
            case 1:
            case 2:
                c.DropAway();
                StandAndFight(c, menu);
                break;
            case 3:
                c.PassHours(Core.Time.TimeOfDay.HoursUntil(c.State.Date.Hour, 18));
                c.Screen.Text(0x0D);
                BackToTheMap(c);
                break;
            case 4:
                c.Screen.Text(0x0E);
                c.LoseItems(Who.Everybody);
                // C: the attributes are raised, not lowered (add_attr with rand_mod + 1).
                c.AddAttribute(Who.Everybody, (int)Attribute.Endurance, c.Random(10) + 1);
                c.AddAttribute(Who.Everybody, (int)Attribute.Strength, c.Random(5) + 1);
                c.PassHours(Core.Time.TimeOfDay.HoursUntil(c.State.Date.Hour, 18));
                BackToTheMap(c);
                break;
            default:
                // DORMANT: other battle results leave the menu as it is.
                break;
        }
    }

    private static void BackToTheMap(CardContext c)
    {
        c.State.CurrentLocation = -1;
        c.Next = OverlandMap;
    }
}
