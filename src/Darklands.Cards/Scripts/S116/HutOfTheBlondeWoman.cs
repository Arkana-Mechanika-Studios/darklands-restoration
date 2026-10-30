using Darklands.Core.Characters;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$MeetW01": a lonely sod hut where a blonde woman lives with half-tame wolves. When the
/// witch quest is under way (quest 1, or fact 1/1 somewhere), every other map square holds a
/// witch, whose wolves attack (on to card 291 when they are beaten); otherwise she is an honest
/// widow, and violence against her costs Virtue and Divine Favour.
/// </summary>
// orig: R116+0x0E3C (191B:000C) matched; tests: CardSmokeTests
// orig: R116+0x1AB0 (191B:0C80) matched (registration: item pop-ups R116+0x1B7E/0d7a/0db4 name the member; saint pop-ups do not)
public sealed class HutOfTheBlondeWoman : Card
{
    private const int HutPicture = 287;
    private const int WolvesPicture = 139;
    private const int WomanPicture = 356;
    private const int WidowPicture = 218;

    // C: DS:EEB6, a DGROUP word only this card uses: is she a witch? Set on every arrival.
    private bool _witch;

    public override int Number => 290;

    public override int Run(CardContext c)
    {
        Show(c, HutPicture);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Set(OptionState.Enabled, 0, 1, 3, 4, 9);
        c.Variables.Chosen[0] = c.Leader;
        // C: (party-map-row + party-map-column) % 2, a signed remainder; only its non-zero-ness counts.
        _witch = (c.Events.QuestExists(1) || c.Events.FactAnywhere(1, 1))
            && (c.State.MapPosition.Y + c.State.MapPosition.X) % 2 != 0;
        c.SetCardItems(0x65, 0x66, 0x67);
        c.SetCardSaints(0x25, 0x42, 0x43, 0x5D);
        menu[3] = c.SaintsUsable();
        menu.Popup = () => c.SaintPopupChanceHint();

        // C: after each choice the loop sets $ChosenOneName to the leader, then calls the handler (a box handler may name its member), then
        // C: the action.
        menu.OnChoice = () => c.Variables.Chosen[0] = c.Leader;
        return menu.Run(
            // orig: R116+0x12E6 (191B:04B6) matched; hint R116+0x1352
            new MenuLine(_ => { KnockOnTheDoor(c, menu); }, Unknown),
            // orig: R116+0x138A (line 1); hint R116+0x14E6 (191B:06B6) matched
            new MenuLine(p => { WatchTheHut(c, menu, p); }, () => StealthHint(c)),
            // orig: R116+0x1526 (line 2); hint R116+0x16E8 (191B:08B8) matched
            new MenuLine(p => { ClimbInAtTheWindow(c, menu, p); }, () => StealthHint(c)),
            // orig: R116+0x1728 (line 3); hint R116+0x17EA (191B:09BA) matched
            new MenuLine(_ => { PrayForInsight(c); }, () => OptionHint.Say("Select a person and a saint.")),
            // orig: R116+0x1822 (line 4); hint R116+0x18F4 (191B:0AC4) matched
            new MenuLine(_ => { StormTheHut(c); }, Unknown),
            // orig: R116+0x192C (line 5); hint R116+0x1A26 (191B:0BF6) matched
            new MenuLine(p => { BurnTheHut(c, p); }, () => OptionHint.Say("Select a person and a potion."), () => PotionHint(c)),
            // DORMANT: lines 6..8 are never offered; actions R116+0x1A5E/0c32/0c36 and hints R116+0x1A60/0c34/0c38
            // C: are empty; the hints return AX/DX; neutral hints are used.
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            // orig: R116+0x1A6A (191B:0C3A) matched; hint R116+0x1A78
            new MenuLine(_ => { c.LeaveForTheMap(); }, () => OptionHint.Say(SuccessAssured)));
    }

    private static OptionHint Unknown() => OptionHint.Say("You can always encounter the unknown");

    private static OptionHint StealthHint(CardContext c) => OptionHint.ChanceOf(Math.Clamp(
        c.PartyAverageSkill((int)Skill.Woodwise) / 2 + c.PartyAverageSkill((int)Skill.Stealth) / 2, 1, 99), OptionHint.WholeParty);

    /// <summary>The potion's chance by slot: its quality, twice it, or two and a half times it.</summary>
    // orig: R116+0x1B7E (191B:0D4E) matched; slots 1, 2 R116+0x1BAA, R116+0x1BE4
    private static OptionHint PotionHint(CardContext c)
    {
        var m = c.MenuMember;
        c.Variables.Chosen[0] = m;
        var quality = c.ItemQuality(m, c.CardItems[c.MenuEntrySlot]);
        var chance = c.MenuSlot switch { 0 => quality, 1 => Math.Clamp(quality * 2, 1, 99), _ => Math.Clamp(quality * 5 / 2, 1, 99) };
        return OptionHint.ChanceOf(chance, m);
    }

    /// <summary>Near the hut at dusk: knock, climb in, pray, storm, burn, or leave.</summary>
    private static void AtDusk(CardContext c, CardMenu menu)
    {
        menu[1] = OptionState.Hidden;
        menu.Set(OptionState.Enabled, 0, 2, 4, 9);
        menu[3] = c.SaintsUsable();
        menu[5] = c.ItemsUsable();
        menu.Set(OptionState.Hidden, 6, 7, 8);
    }

    /// <summary>She sends the party away from her window (knocking again is not offered after that).</summary>
    // orig: R116+0x12E6 (191B:04B6) matched; tests: CardSmokeTests
    private static void KnockOnTheDoor(CardContext c, CardMenu menu)
    {
        menu[0] = menu.Block != 0 ? OptionState.Hidden : OptionState.Enabled;
        menu.Set(OptionState.Hidden, 1, 2, 3, 6, 7, 8);
        menu[4] = OptionState.Enabled;
        menu[5] = c.ItemsUsable();
        menu[9] = OptionState.Enabled;
        ShowPicture(c, WomanPicture);
        menu.Block = 1;
        c.Next = 290;
    }

    /// <summary>Watching till dusk; a witch's wolves catch a clumsy watcher.</summary>
    // orig: R116+0x138A (191B:055A) matched; tests: CardSmokeTests
    // C: pass-hours(hours-remaining-until(18) - 1): at 18:00 this passes -1 hours, as the original does.
    private void WatchTheHut(CardContext c, CardMenu menu, int chance)
    {
        c.PassHours(c.HoursUntil(18) - 1);
        int picture;
        if (c.Random(100) <= chance)
        {
            c.PracticeSkill(Who.Everybody, (int)Skill.Stealth, 1, 5, 0);
            c.PracticeSkill(Who.Everybody, (int)Skill.Woodwise, 1, 5, 0);
            AtDusk(c, menu);
            picture = 88;
        }
        else
        {
            c.PracticeSkill(Who.Everybody, (int)Skill.Stealth, 0, 5, 0);
            c.PracticeSkill(Who.Everybody, (int)Skill.Woodwise, 0, 5, 0);
            if (_witch)
            {
                ShowPicture(c, WolvesPicture);
                c.Screen.Text(3);
                FightTheWitch(c);
                return;
            }
            AtDusk(c, menu);
            picture = WidowPicture;
        }
        ShowPicture(c, picture);
        menu.Block = 2;
        c.Next = 290;
    }

    /// <summary>Surprising her at knife-point: a witch is taken (card 291), a widow is shamefully frightened.</summary>
    // orig: R116+0x1526 (191B:06F6) matched; tests: CardSmokeTests
    private void ClimbInAtTheWindow(CardContext c, CardMenu menu, int chance)
    {
        if (c.Random(100) <= chance)
        {
            c.PracticeSkill(Who.Everybody, (int)Skill.Stealth, 1, 5, 0);
            c.PracticeSkill(Who.Everybody, (int)Skill.Woodwise, 1, 5, 0);
            if (_witch)
            {
                ShowPicture(c, WomanPicture);
                c.Screen.Text(8);
                c.Next = 291;
                return;
            }
            ShowPicture(c, WomanPicture);
            c.Screen.Text(9);
            ShowPicture(c, WidowPicture);
            c.Screen.Text(11);
            c.AddSkill(Who.Everybody, (int)Skill.Virtue, -1);
            c.AddAttribute(Who.Everybody, (int)Attribute.DivineFavor, -20);
            // The original leaves without clearing location-now here.
            c.Next = CardContext.MapCard;
            return;
        }
        c.PracticeSkill(Who.Everybody, (int)Skill.Stealth, 0, 5, 0);
        c.PracticeSkill(Who.Everybody, (int)Skill.Woodwise, 0, 5, 0);
        if (_witch)
        {
            ShowPicture(c, WolvesPicture);
            c.Screen.Text(3);
            FightTheWitch(c);
            return;
        }
        AtDusk(c, menu);
        ShowPicture(c, WidowPicture);
        menu.Block = 2;
        c.Next = 290;
    }

    /// <summary>The prayer shows the truth (a witch-crone, or an honest widow); the card goes on either way.</summary>
    // orig: R116+0x1728 (191B:08F8) matched; tests: CardSmokeTests
    private void PrayForInsight(CardContext c)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuEntrySlot]);
        if (answer == -1)
        {
            return;
        }
        if (answer == 1)
        {
            ShowPicture(c, _witch ? 138 : WidowPicture);
            c.Screen.Text(_witch ? 4 : 10);
            return;
        }
        ShowPicture(c, 16);
        c.Screen.Text(5);
    }

    // orig: R116+0x1822 (191B:09F2) matched; tests: CardSmokeTests
    private void StormTheHut(CardContext c)
    {
        if (_witch)
        {
            ShowPicture(c, WolvesPicture);
            c.Screen.Text(3);
            FightTheWitch(c);
            return;
        }
        ShowPicture(c, WomanPicture);
        c.Screen.Text(9);
        ShowPicture(c, WidowPicture);
        c.Screen.Text(11);
        c.AddSkill(Who.Everybody, (int)Skill.Virtue, -1);
        c.AddAttribute(Who.Everybody, (int)Attribute.DivineFavor, -20);
        c.LeaveForTheMap();
    }

    /// <summary>The potion is used up; it sets the hut afire — a witch rushes out with her wolves, a widow flees in terror.</summary>
    // orig: R116+0x192C (191B:0AFC) matched; tests: CardSmokeTests
    private void BurnTheHut(CardContext c, int chance)
    {
        c.TakeItem(c.MenuMember, c.CardItems[c.MenuSlot]);
        if (c.Random(100) <= chance)
        {
            if (_witch)
            {
                ShowPicture(c, WolvesPicture);
                c.Screen.Text(6);
                FightTheWitch(c);
                return;
            }
            ShowPicture(c, 19);
            c.Screen.Text(12);
            c.AddSkill(Who.Everybody, (int)Skill.Virtue, -4);
            c.AddAttribute(Who.Everybody, (int)Attribute.DivineFavor, -50);
            c.LeaveForTheMap();
            return;
        }
        ShowPicture(c, 286);
        c.Screen.Text(7);
        c.Next = 290;
    }

    /// <summary>
    /// The witch and her wolves. Won, on to the witch (card 291); fled, those away are lost;
    /// beaten, robbed, and one member without heavy body armour is devoured (none: the wolves
    /// cannot bite through). An hour passes after any result.
    /// </summary>
    // orig: R116+0x10CC (191B:029C) matched; tests: CardSmokeTests
    // C: the only draw among battle-driver's arguments is roll-below(5) + 3 (the eighth); then battle-driver draws its seed.
    // C: party[v]+0x37 is the vital armour's item type; types 0x49..0x4B protect from the wolves.
    private static void FightTheWitch(CardContext c)
    {
        var lore = c.PartyLoreLevel();
        var wolves = c.Random(5) + 3;
        switch (c.RunBattle(0x34, -1, 0x27, lore / 5 + 1, 1, 0x2A, lore / 4 + 1, wolves, -1, 0, 0))
        {
            case 0:
                c.Next = 291;
                break;
            case 1:
            case 2:
                ShowPicture(c, 286);
                c.Screen.Text(c.DropAway() != 0 ? 15 : 13);
                c.PassHours(c.HoursUntil(5));
                c.LeaveForTheMap();
                break;
            case 3:
            case 4:
                c.LoseItems(Who.Everybody);
                var unprotected = 0;
                for (var i = 0; i < Party.MaxMembers; i++)
                {
                    var armour = (sbyte)c.State.Party.Slot(i).VitalArmor.Type;
                    if (c.Status(i) != CharacterStatus.Empty && armour != 0x4B && armour != 0x49 && armour != 0x4A)
                    {
                        unprotected++;
                        c.Variables.Chosen[0] = i;
                    }
                }
                var eaten = c.Variables.Chosen[0];
                if (unprotected == 0)
                {
                    c.PassHours(4);
                    ShowPicture(c, 286);
                    c.Screen.Text(17);
                }
                else if (unprotected == 1)
                {
                    c.SetStatus(eaten, CharacterStatus.Empty);
                    c.DeleteMember(eaten);
                    ShowPicture(c, WolvesPicture);
                    c.Screen.Text(14);
                    c.PassHours(0);
                }
                else
                {
                    c.SetStatus(eaten, CharacterStatus.Empty);
                    c.DeleteMember(eaten);
                    ShowPicture(c, 286);
                    c.Screen.Text(16);
                    c.PassHours(4);
                }
                c.LeaveForTheMap();
                break;
        }
        c.PassHours(1);
    }
}
