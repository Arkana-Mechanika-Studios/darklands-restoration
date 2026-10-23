using Darklands.Core.Characters;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$MeetC00": a cave in a snowy mountain pass. A brown bear sleeps inside beside a thief's
/// locked chest; the party can scout, sneak in, explore, carry the chest off, camp in the
/// mouth of the cave or move on. Once seen, the place cannot be entered again.
/// </summary>
// orig: R104+0x0000 (1838:0000) matched; tests: CardSmokeTests
// orig: R104+0x0970 (1838:0970) matched (registration: default handler R104+0x0A12 "screwed up.", standard item/saint pop-ups that name the member)
public sealed class BearCave : Card
{
    private const int CavePicture = 331;
    private const int BearPicture = 199;
    private const int BattlePicture = 232;
    private const int ChestPicture = 195;

    private const int ChestFallsBlock = 10;
    private const int TunnelBlock = 17;

    public override int Number => 240;

    public override int Run(CardContext c)
    {
        Show(c, CavePicture);
        // C: locations[cur_loc] + 0x0C (the arrival card) only when not in the wilderness.
        if (c.State.Here is { } place)
        {
            place.ArrivalCard = CardContext.ClosedCard;
        }
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Set(OptionState.Enabled, 0, 1, 2, 4, 5);
        if (c.IsDaylight)
        {
            menu[4] = OptionState.Hidden;
        }
        c.SetCardSaints(0, 0x59, 0x61, 0x80);
        c.PartyMaxSkill((int)Skill.Woodwise);
        c.Variables.Chosen[0] = c.BestMember;
        c.PartyMaxSkill((int)Skill.Stealth);
        c.Variables.Chosen[1] = c.BestMember;

        // C: the registration installs the standard pop-up handlers, which name the member picked in the box.
        menu.Popup = () => c.MemberPopupHint();
        // DORMANT: in text block 17 the option lines 0..2, 4, 5 are never offered (the switch to this block hides them); they can be offered in other blocks only.
        return menu.Run(
            // orig: R104+0x0318 (line 0); hint R104+0x0342 (1838:0342) matched
            new MenuLine(p => SearchForTracks(c, menu, p),
                () => OptionHint.ChanceOf(Math.Clamp(c.PartyMaxSkill((int)Skill.Woodwise) + 15, 0, 99), c.BestMember)),
            // orig: R104+0x0370 (line 1); hint R104+0x0434 (1838:0434) matched
            new MenuLine(p => SneakIntoTheCave(c, menu, p),
                () => OptionHint.ChanceOf(c.PartyMaxSkill((int)Skill.Stealth), c.BestMember)),
            // orig: R104+0x0452 (line 2); hint R104+0x04A6 (1838:04A6) matched
            new MenuLine(_ => FaceTheBear(c, menu, TunnelBlock), Unknown),
            // orig: R104+0x04DE (line 3); hint R104+0x0576 (1838:0576) matched
            new MenuLine(p => CarryOffTheChest(c, menu, p),
                () => OptionHint.ChanceOf(c.PartyAverageSkill((int)Skill.Stealth), OptionHint.WholeParty)),
            // orig: R104+0x058C (line 4); hint R104+0x06A8 (1838:06A8) matched
            new MenuLine(_ => WaitForDaylight(c), Unknown),
            // orig: R104+0x06E0 (1838:06E0) matched; hint R104+0x06FA
            new MenuLine(_ => { c.Screen.Text(8); c.LeaveForTheMap(); }, Assured),
            // orig: R104+0x0732 (line 6); hint R104+0x084E (1838:084E) matched
            new MenuLine(_ => PrayToASaint(c), OptionHint.Saying(CardTexts.SelectSaint)),
            // orig: R104+0x0886 (1838:0886) matched; hint R104+0x08C4
            new MenuLine(_ => { ShowPicture(c, ChestPicture); FightTheBear(c, 16); c.LeaveForTheMap(); }, Assured),
            // orig: R104+0x08FC (line 8); hint R104+0x0916 (1838:0916) matched
            new MenuLine(p => Flee(c, p),
                () => OptionHint.ChanceOf(c.PartyAverageAttribute((int)Attribute.Agility), OptionHint.WholeParty)),
            // orig: R104+0x092C (1838:092C) matched; hint R104+0x0938
            // DORMANT: line 9 is never offered (opt_flag_9 = 0); it would only let no time pass and stay.
            new MenuLine(_ => c.PassHours(0), Assured));
    }

    private static OptionHint Unknown() => OptionHint.Say(CardTexts.Unknown);

    private static OptionHint Assured() => OptionHint.Say(SuccessAssured);

    // orig: R104+0x0318 (1838:0318) matched; tests: CardSmokeTests
    private static void SearchForTracks(CardContext c, CardMenu menu, int chance)
    {
        menu[0] = OptionState.Shown;
        c.Screen.Text(c.Random(100) < chance ? 1 : 2);
    }

    /// <summary>A quiet scout finds the chest; a clumsy one may still come back, or else wakes the bear.</summary>
    // orig: R104+0x0370 (1838:0370) matched; tests: CardSmokeTests
    private static void SneakIntoTheCave(CardContext c, CardMenu menu, int chance)
    {
        menu[1] = OptionState.Shown;
        if (c.Random(100) < chance)
        {
            c.Screen.Text(5);
            menu[3] = OptionState.Enabled;
            return;
        }
        // C: the chance is doubled in place (v_si <<= 1) for the second roll; 16-bit, but at most 198.
        if (c.Random(100) < chance * 2)
        {
            c.Screen.Text(3);
            return;
        }
        ShowPicture(c, BearPicture);
        c.Screen.Text(4);
        ShowPicture(c, BattlePicture);
        FightTheBear(c, 15);
        c.LeaveForTheMap();
    }

    /// <summary>The chest can be carried past the sleeping bear, or it slips and the bear wakes.</summary>
    // orig: R104+0x04DE (1838:04DE) matched; tests: CardSmokeTests
    private static void CarryOffTheChest(CardContext c, CardMenu menu, int chance)
    {
        if (c.Random(100) < chance)
        {
            // The line stays enabled after the chest is taken: choosing it again rolls again.
            ShowPicture(c, ChestPicture);
            c.Screen.Text(9);
            return;
        }
        FaceTheBear(c, menu, ChestFallsBlock);
    }

    /// <summary>The bear charges: pray, fight or flee (the lines of <paramref name="block"/>).</summary>
    // orig: R104+0x0452 (1838:0452) matched (also the tail of R104+0x04DE); tests: CardSmokeTests
    private static void FaceTheBear(CardContext c, CardMenu menu, int block)
    {
        // C: the original copies the bear picture into pic_buf (the card's own picture buffer);
        // nothing in this card reloads pic_buf afterwards, so it has no further effect.
        ShowPicture(c, BearPicture);
        menu.Block = block;
        menu.Set(OptionState.Hidden, 0, 1, 2, 3, 4, 5);
        menu[6] = c.SaintsUsable();
        menu.Set(OptionState.Enabled, 7, 8);
    }

    /// <summary>A random member keeps watch; a perceptive one raises the alarm in time, another is knocked senseless.</summary>
    // orig: R104+0x058C (1838:058C) matched; tests: CardSmokeTests
    private static void WaitForDaylight(CardContext c)
    {
        var watch = c.Random(c.PartySize);
        c.Variables.Chosen[0] = watch;
        // C: member_attr(...) > rand_mod(100): member_attr draws nothing, so the order does not matter.
        if (c.Attribute(watch, (int)Attribute.Perception) > c.Random(100))
        {
            ShowPicture(c, BearPicture);
            c.Screen.Text(6);
            ShowPicture(c, BattlePicture);
            FightTheBear(c, 15);
            c.PartyMaxSkill((int)Skill.Woodwise);
            c.Variables.Chosen[0] = c.BestMember;
        }
        else
        {
            ShowPicture(c, BearPicture);
            c.Screen.Text(7);
            c.SetStatus(watch, CharacterStatus.Unconscious);
            ShowPicture(c, BattlePicture);
            FightTheBear(c, 15);
        }
        // A battle result other than 0..4 leaves the party on this card, as in the original.
    }

    /// <summary>A saint may tame the bear; if not, the most agile member may divert it before the fight.</summary>
    // orig: R104+0x0732 (1838:0732) matched; tests: CardSmokeTests
    private static void PrayToASaint(CardContext c)
    {
        var saint = c.CardSaints[c.MenuSlot];
        var answer = c.InvokeSaint(c.MenuMember, saint);
        if (answer == -1)
        {
            return;
        }
        c.NameToSlot(1, saint, 0);
        if (answer != 0)
        {
            ShowPicture(c, ChestPicture);
            c.Screen.Text(14);
        }
        else
        {
            ShowPicture(c, BearPicture);
            c.Screen.Text(11);
            var chance = c.PartyMaxAttribute((int)Attribute.Agility);
            if (c.Random(100) < chance)
            {
                c.Variables.Chosen[0] = c.BestMember;
                c.Screen.Text(12);
            }
            else
            {
                c.Screen.Text(13);
            }
            ShowPicture(c, ChestPicture);
            FightTheBear(c, 16);
        }
        c.LeaveForTheMap();
    }

    /// <summary>Fleeing always gets away (the roll is made but does not matter).</summary>
    // orig: R104+0x08FC (1838:08FC) matched; tests: CardSmokeTests
    private static void Flee(CardContext c, int chance)
    {
        // C: both arms of `rand_mod(100) <= p` are the same; the draw is kept for the random sequence.
        _ = c.Random(100) <= chance;
        c.LeaveForTheMap();
    }

    /// <summary>
    /// The fight with the bear: won, <paramref name="victoryText"/> is told and an hour passes;
    /// beaten or fled, those who went away are lost; worse, everybody is hurt. Then the party moves on.
    /// </summary>
    // orig: R104+0x0288 (1838:0288) matched; tests: CardSmokeTests
    private static void FightTheBear(CardContext c, int victoryText)
    {
        // C: run_battle's arguments are evaluated right to left; the only draw among them is the
        // bear count, made before run_battle draws its own seed (-1 = random).
        var bears = c.Random(3) + c.PartyLoreLevel() / 5 + 1;
        switch (c.RunBattle(0x68, -1, 0x3A, bears, 1, -1, 0, 0, -1, 0, 0))
        {
            case 0:
                c.Screen.Text(victoryText);
                c.PassHours(1);
                c.LeaveForTheMap();
                break;
            case 1:
            case 2:
                c.DropAway();
                c.LeaveForTheMap();
                break;
            case 3:
            case 4:
                c.Injure(Who.Everybody, 1, 1, 10);
                c.LeaveForTheMap();
                break;
        }
    }
}
