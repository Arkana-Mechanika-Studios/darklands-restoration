using Darklands.Core.Economy;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$MeetR00": camping ashore beside a moored riverboat, the party is attacked by river pirates
/// at night. It can fight, pray, surrender, slip away into the dark, or cover its escape with
/// a potion. At dawn the voyage goes on (card 0x5A).
/// </summary>
// orig: R114+0x15D4 (1995:0004) matched; tests: CardSmokeTests
// orig: R114+0x1CF8 (1995:0728) matched (registration: item pop-ups R114+0x1DC6/083a/0880/08c8 name the member; saint pop-ups do not)
public sealed class RiverPiratesAtNight : Card
{
    private const int CampPicture = 33;
    private const int DawnPicture = 220;

    public override int Number => 282;

    public override int Run(CardContext c)
    {
        Show(c, CampPicture);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Set(OptionState.Enabled, 0, 1, 2, 3, 4);
        c.Variables.Chosen[0] = c.Leader;
        c.SetCardItems(0x5F, 0x60, 0x65, 0x61);
        c.SetCardSaints(0x36, 0x3D, 0x51);
        menu[4] = c.ItemsUsable();
        menu[1] = c.SaintsUsable();
        menu.Popup = () => c.SaintPopupChanceHint();

        // C: after every choice the loop sets var_chosen1 = leader before the handler; a pop-up
        // handler then names its own member. Each action does this first for line choices.
        // DORMANT: in text block 5 the option lines 1, 3, 4 are never offered (the switch to this block hides them); they can be offered in other blocks only.
        // DORMANT: in text block 6 the option lines 1, 3, 4 are never offered (the switch to this block hides them); they can be offered in other blocks only.
        // DORMANT: in text block 8 the option lines 1, 3, 4 are never offered (the switch to this block hides them); they can be offered in other blocks only.
        return menu.Run(
            // orig: R114+0x19A2 (1995:03D2) matched; hint R114+0x19A8
            new MenuLine(_ => { LeaderSpeaks(c); FightThePirates(c); }, () => OptionHint.Say("Enter Combat")),
            // orig: R114+0x19E0 (line 1); hint R114+0x1A98 (1995:04C8) matched
            new MenuLine(_ => { LeaderSpeaks(c); PrayForAid(c, menu); }, () => OptionHint.Say("Select a person and a saint.")),
            // orig: R114+0x1AD0 (1995:0500) matched; hint R114+0x1B2A
            new MenuLine(_ => { LeaderSpeaks(c); c.LoseItems(Core.Characters.Who.Everybody); AtDawn(c, 3); }, () => OptionHint.Say(SuccessAssured)),
            // orig: R114+0x1B62 (line 3); hint R114+0x1BFC (1995:062C) matched
            new MenuLine(p => { LeaderSpeaks(c); EscapeIntoTheDark(c, menu, p); },
                () => OptionHint.ChanceOf(c.PartyAverageEffectiveAgility() * 2, OptionHint.WholeParty)),
            // orig: R114+0x1C12 (line 4); hint R114+0x1CAC (1995:06DC) matched
            new MenuLine(p => { LeaderSpeaks(c); CoverTheEscape(c, menu, p); }, () => OptionHint.Say("Select a person and a potion."), () => PotionHint(c)),
            // DORMANT: lines 5..9 are never offered; actions R114+0x1CE4.. and hints R114+0x1CE6.. are empty
            // C: the hints return AX/DX; neutral hints are used.
            new MenuLine(_ => LeaderSpeaks(c)),
            new MenuLine(_ => LeaderSpeaks(c)),
            new MenuLine(_ => LeaderSpeaks(c)),
            new MenuLine(_ => LeaderSpeaks(c)),
            new MenuLine(_ => LeaderSpeaks(c)));
    }

    private static void LeaderSpeaks(CardContext c)
    {
        if (!c.MenuPopup)
        {
            c.Variables.Chosen[0] = c.Leader;
        }
    }

    /// <summary>The potion's chance: the party's average Agility plus its quality × 3/2, 5/3 or 7/3 by slot.</summary>
    // orig: R114+0x1DC6 (1995:07F6) matched; slots 1..3 R114+0x1E0A, R114+0x1E50, R114+0x1E98
    private static OptionHint PotionHint(CardContext c)
    {
        var m = c.MenuMember;
        c.Variables.Chosen[0] = m;
        var quality = c.ItemQuality(m, c.CardItems[c.MenuSlot]);
        var bonus = c.MenuSlot switch { 0 => quality * 3 / 2, 1 => quality * 5 / 3, _ => quality * 7 / 3 };
        return OptionHint.ChanceOf(c.PartyAverageEffectiveAgility() + bonus, m);
    }

    /// <summary>Morning on the shore: the text, the hours till dawn, and on with the voyage.</summary>
    // C: card_load(card_name, pic_buf2): a full card load with the dawn picture; pic_buf itself is left alone.
    private void AtDawn(CardContext c, int text)
    {
        Show(c, DawnPicture);
        c.Screen.Text(text);
        c.PassHours(c.HoursUntil(5));
        c.Next = 0x5A;
    }

    /// <summary>"There are only two choices now": fight or surrender (lines 1, 3 and 4 are withdrawn).</summary>
    private static void Cornered(CardContext c, CardMenu menu, int picture, int block)
    {
        ShowPicture(c, picture);
        menu.Block = block;
        menu.Set(OptionState.Hidden, 1, 3, 4);
        c.Next = 282;
    }

    // orig: R114+0x19E0 (1995:0410) matched; tests: CardSmokeTests
    // C: this prayer does not make the praying member $ChosenOneName (the leader stays named).
    private void PrayForAid(CardContext c, CardMenu menu)
    {
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (answer == -1)
        {
            return;
        }
        if (answer == 1)
        {
            AtDawn(c, 4);
            return;
        }
        Cornered(c, menu, CampPicture, 5);
    }

    // orig: R114+0x1B62 (1995:0592) matched; tests: CardSmokeTests
    private void EscapeIntoTheDark(CardContext c, CardMenu menu, int chance)
    {
        if (c.Random(100) <= chance)
        {
            AtDawn(c, 2);
            return;
        }
        Cornered(c, menu, CampPicture, 6);
    }

    /// <summary>The potion (not taken: the original never takes it) confuses the pirates, or goes off too far away.</summary>
    // orig: R114+0x1C12 (1995:0642) matched; tests: CardSmokeTests
    private static void CoverTheEscape(CardContext c, CardMenu menu, int chance)
    {
        if (c.Random(100) <= chance)
        {
            ShowPicture(c, 189);
            c.Screen.Text(7);
            c.PassHours(c.HoursUntil(5));
            c.Next = 0x5A;
            return;
        }
        Cornered(c, menu, 20, 8);
    }

    /// <summary>The pirates: won, the captain's florin; fled, those away come back; beaten, robbed down to a few pfennigs.</summary>
    // orig: R114+0x1836 (1995:0266) matched; tests: CardSmokeTests
    // C: arguments right to left: rand_mod(4) (fifth) is drawn before rand_mod(3) (fourth); then run_battle draws its seed.
    // A result above 4 leaves the party on the card, as in the original.
    private void FightThePirates(CardContext c)
    {
        var lore = c.PartyLoreLevel();
        var count = c.Random(4) + lore / 3 + 1;
        var groups = c.Random(3) + lore / 4 + 1;
        switch (c.RunBattle(0x55, -1, 0x0A, groups, count, 0x12, lore / 4 + 1, 1, -1, 0, 0))
        {
            case 0:
                c.State.Party.Purse = c.State.Party.Purse.Add(1, 0, 0);
                AtDawn(c, 1);
                break;
            case 1:
            case 2:
                c.RejoinAway();
                AtDawn(c, 2);
                break;
            case 3:
            case 4:
                c.LoseItems(Core.Characters.Who.Everybody);
                c.State.Party.Purse = Money.From(0, 0, c.Random(10) + 1);
                AtDawn(c, 3);
                break;
        }
    }
}
