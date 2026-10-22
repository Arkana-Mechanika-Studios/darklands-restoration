using Darklands.Core.Characters;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$WildH01": the Wild Hunt's hellhounds bay close behind a fleeing figure (a schrat or a
/// holzfrau). Stay hidden, attack the hounds, lead them off on a false trail with woodcraft,
/// or hide the quarry with stealth. A schrat saved (or lost) is remembered as a fact of the
/// place (tag 7: type 1 a first meeting, type 2 the second).
/// </summary>
// orig: R086+0x0000 (1838:0000) matched; tests: CardSmokeTests
// orig: R086+0x084E (1838:084E) matched; R086+0x08F0 (1838:08F0) matched; tests: CardSmokeTests
// DORMANT: the "screwed up." default handler of the unlisted lines 4..19 (never offered).
public sealed class WildHuntQuarry : Card
{
    public const int CardNumber = 0xA8;

    private const int Picture = 125;
    private const int ForestPicture = 131;
    private const int HoundsPicture = 61;
    private const int SchratDeadPicture = 130;
    private const int HiddenPicture = 145;

    /// <summary>The fact tag of the Hunt's quarry.</summary>
    private const int QuarryTag = 7;

    /// <summary>The card that gives the rescued schrat's present.</summary>
    private const int SchratGratitude = 0xAF;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        c.PlaySound(0x25);
        Show(c, Picture);
        c.Next = CardNumber;
        var menu = Menu(c);
        menu.Enable(0, 1, 2, 3);
        c.SetCardSaints();
        c.SetCardItems();
        // DORMANT: no items and no saints are offered, so the pop-up handlers of the four lines never run.
        // (the item pop-up handler is the menu's default pop-up hint, the saint pop-up handler is c.SaintPopupHint)
        menu.Popup = () => PopupHint(c, c.MenuMember);
        // orig: R086+0x0A18 (1838:0A18) matched; R086+0x0AA2 (1838:0AA2) matched; R086+0x0B2C (1838:0B2C) matched; R086+0x0BB6 (1838:0BB6) matched; tests: CardSmokeTests
        // DORMANT: the saint pop-up handler (c.SaintPopupHint) is not carried by any line either.
        if (c.Next != CardNumber)
        {
            // DORMANT: the start-up never leaves this card.
            return c.Next;
        }
        return menu.Run(
            // orig: R086+0x0532 (1838:0532) matched; R086+0x05E0 (1838:05E0) matched; tests: CardSmokeTests
            new MenuLine(() => RemainHidden(c), OptionHint.Saying("Endurance")),
            // orig: R086+0x060E (1838:060E) matched; R086+0x0614 (1838:0614) matched; tests: CardSmokeTests
            new MenuLine(() => FightTheHounds(c), OptionHint.Saying(CardTexts.EnterCombat)),
            // orig: R086+0x064C (1838:064C) matched; R086+0x0724 (1838:0724) matched; tests: CardSmokeTests
            new MenuLine(chance => LeadThemOff(c, chance), () => LeadChance(c)),
            // orig: R086+0x0742 (1838:0742) matched; R086+0x082C (1838:082C) matched; tests: CardSmokeTests
            new MenuLine(chance => HideTheQuarry(c, chance), () => HideChance(c)));
    }

    /// <summary>The pop-up line hint of items: success assured for the member, who becomes $ChosenOneName.</summary>
    // orig: R086+0x0928 (1838:0928) matched; R086+0x0964 (1838:0964) matched; R086+0x09A0 (1838:09A0) matched; R086+0x09DC (1838:09DC) matched; tests: CardSmokeTests
    // DORMANT: nothing in this card offers an item pop-up.
    private static OptionHint PopupHint(CardContext c, int member)
    {
        c.Variables.Chosen[0] = member;
        return OptionHint.Say(SuccessAssured, member);
    }

    private static void LeaveTheScene(CardContext c)
    {
        c.State.CurrentLocation = -1;
        c.Next = 0x0C;
    }

    private static void RememberQuarry(CardContext c, int type) =>
        c.Events.Create(-2, c.Here, 0, 0x1C, type, 0, 0, 9999, 0, QuarryTag);

    private static void RemainHidden(CardContext c)
    {
        c.PassHours(1);
        if (c.Events.FactAnywhere(QuarryTag, 1))
        {
            RememberQuarry(c, 2);
            c.Events.Remove(1, -1, -1, -1, QuarryTag);
        }
        else
        {
            RememberQuarry(c, 1);
        }
        ShowPicture(c, HiddenPicture);
        c.Screen.Text(1);
        LeaveTheScene(c);
    }

    /// <summary>The battle against the hounds (battle size from lore), and what follows it.</summary>
    // orig: R086+0x0152 (1838:0152) matched; tests: CardSmokeTests
    private static void FightTheHounds(CardContext c)
    {
        if (c.Events.FactAnywhere(QuarryTag, 1) || c.PartySize >= 5)
        {
            // The quarry is the holzfrau (second meeting, or a party too large to hide anybody).
            ShowPicture(c, ForestPicture);
            c.Screen.Text(0x0D);
            switch (HoundBattle(c))
            {
                case 0:
                case 1:
                case 2:
                    c.DropAway();
                    ShowPicture(c, HoundsPicture);
                    c.Screen.Text(0x0F);
                    LeaveTheScene(c);
                    break;
                case 3:
                case 4:
                    TheHunterTakesOne(c);
                    break;
                default:
                    // DORMANT: other battle results leave the party where it is.
                    break;
            }
        }
        else if (c.PartySize < 5)
        {
            ShowPicture(c, Picture);
            c.Screen.Text(9);
            var result = HoundBattle(c);
            switch (result)
            {
                case 0:
                case 1:
                case 2:
                    c.DropAway();
                    if (c.Random(100) % 2 != 0)
                    {
                        RememberQuarry(c, 1);
                        ShowPicture(c, HoundsPicture);
                        c.Screen.Text(0x0A);
                        LeaveTheScene(c);
                    }
                    else
                    {
                        if (result > 0)
                        {
                            ShowPicture(c, Picture);
                            c.Screen.Text(0x0B);
                        }
                        c.Next = SchratGratitude;
                    }
                    break;
                case 3:
                case 4:
                    RememberQuarry(c, 1);
                    ShowPicture(c, SchratDeadPicture);
                    c.Screen.Text(0x0C);
                    LeaveTheScene(c);
                    break;
                default:
                    // DORMANT: other battle results leave the party where it is.
                    break;
            }
        }
        else
        {
            // DORMANT: a party of five or more took the first arm; this arm is reached by no party size.
            switch (HoundBattle(c))
            {
                case 0:
                case 1:
                case 2:
                    c.DropAway();
                    ShowPicture(c, Picture);
                    c.Screen.Text(0x10);
                    c.Next = SchratGratitude;
                    break;
                case 3:
                case 4:
                    TheHunterTakesOne(c);
                    break;
                default:
                    break;
            }
        }
    }

    private static int HoundBattle(CardContext c)
    {
        // C: the hounds' count is drawn as an argument of the battle (one random draw).
        var hounds = c.Random(3) + c.PartyLoreLevel() / 4 + 2;
        return c.RunBattle(0x2F, -1, 0x3D, 2, hounds, -1, 0, 0, -1, 0, 0);
    }

    /// <summary>Beaten, one of the party (picked at random among those standing or unconscious) is devoured.</summary>
    private static void TheHunterTakesOne(CardContext c)
    {
        var victim = c.Random(c.PartySize);
        var looked = 0;
        // C: the original loops until it finds a member who is able or unconscious.
        while (c.Status(victim) != CharacterStatus.Able && c.Status(victim) != CharacterStatus.Unconscious)
        {
            if (++victim >= c.PartySize)
            {
                victim = 0;
            }
            if (++looked > c.PartySize)
            {
                // DIVERGENCE: the original would loop forever with nobody standing; nobody is devoured here.
                victim = -1;
                break;
            }
        }
        if (victim >= 0)
        {
            c.SetStatus(victim, CharacterStatus.Dead);
            c.Variables.Chosen[0] = victim;
        }
        ShowPicture(c, ForestPicture);
        c.Screen.Text(0x0E);
        c.Events.ClearFacts(QuarryTag, -1);
        LeaveTheScene(c);
    }

    private static OptionHint LeadChance(CardContext c)
    {
        var chance = c.PartyMaxSkill(0x12);
        return OptionHint.ChanceOf(chance, (byte)c.BestMember);
    }

    private static void LeadThemOff(CardContext c, int chance)
    {
        c.PartyMaxSkill(0x12);
        c.Variables.Chosen[0] = (byte)c.BestMember;
        if (c.Random(100) <= chance)
        {
            c.WaitUntilHour(5);
            ShowPicture(c, ForestPicture);
            c.Screen.Text(2);
            ShowPicture(c, Picture);
            c.PracticeSkill(c.Variables.Chosen[0], 0x12, 7, 10, 0);
            c.Next = 0x41;
            return;
        }
        c.PassHours(2);
        ShowPicture(c, ForestPicture);
        c.Screen.Text(3);
        c.PracticeSkill(c.Variables.Chosen[0], 0x12, 0, 10, 0);
        FightTheHounds(c);
    }

    private static OptionHint HideChance(CardContext c)
    {
        var chance = c.PartyMaxSkill(0x0F);
        c.Variables.Chosen[0] = (byte)c.BestMember;
        return OptionHint.ChanceOf(chance, (byte)c.BestMember);
    }

    private static void HideTheQuarry(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            if (c.Events.FactAnywhere(QuarryTag, 1))
            {
                // The holzfrau.
                c.Screen.Text(4, TextFrame.BlankPage);
                c.Screen.Text(5, TextFrame.BlankPage);
                LeaveTheScene(c);
            }
            else
            {
                // The schrat.
                ShowPicture(c, Picture);
                c.Screen.Text(6);
                c.Screen.Text(7);
                c.Next = SchratGratitude;
            }
            c.PracticeSkill(c.Variables.Chosen[0], 0x0F, 1, 10, 0);
            return;
        }
        ShowPicture(c, ForestPicture);
        c.Screen.Text(8);
        c.PracticeSkill(c.Variables.Chosen[0], 0x0F, 0, 10, 0);
        FightTheHounds(c);
    }
}
