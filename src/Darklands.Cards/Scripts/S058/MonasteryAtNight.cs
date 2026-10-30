using Darklands.Core.Characters;
using Darklands.Core.Economy;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$CityM01": the monastery at night. The party knocks: it can buy prayers that restore divine
/// favor, cry out for help when wounded, or ask for sanctuary. A party in bad standing, or one
/// whose fame and virtue are high (see <see cref="Run"/>), is turned away at once.
/// </summary>
// orig: R058+0x0000 (1838:0000) matched; tests: CardSmokeTests
// DORMANT: text block 9 ("The monastery is almost completely dark ...") is never used by this code: the
//   turning-away test shows block 1 instead.
public sealed class MonasteryAtNight : Card
{
    public const int CardNumber = 0x38;

    private const int Picture = 64, TurnedAwayPicture = 313, PrayersPicture = 233, DoorPicture = 292,
        WelcomePicture = 293, SanctuaryPicture = 260;

    private const int MonasteryByDay = 0x36, ChurchDistrict = 0x13, Sanctuary = 0xB4, NoGo = 0x62;

    /// <summary>Events at a city: prayers bought, (never set here), the monks rebuffed the party.</summary>
    private const int PrayersBought = 0x30, Event31 = 0x31, Rebuffed = 0x35;

    private const int Strength = 1, Charisma = 5, DivineFavor = 6, Religion = 8, Virtue = 9;

    /// <summary>
    /// The hint of lines 4, 5 and 6: the original copies the EXE string at seg 290E:062e, which is
    /// the heraldry menu label (bytes 8C ' ' 'H' FF "eraldry", control codes left out here).
    /// </summary>
    // C: R058+0x06D4 etc. read R136+0x8690+0x1EDF (the word pair at ES:1EDF); the table
    //   entry just after it is "It is closed at night", which looks like what was meant, but the
    //   EXE bytes and the asm say 1EDF.
    private const string HeraldryLabel = "Heraldry";

    public override int Number => CardNumber;

    // orig: R058+0x0000 (1838:0000) matched (entry: option flags, the offering, the test that turns the party away, the option table)
    // orig: R058+0x08CE (1838:08CE) matched (init helper: disables all 20 option lines and gives them the "screwed up." handler; the card then enables lines 0, 3, 4 and 8)
    // DORMANT: R058+0x08F0 (1838:08F0): the "screwed up." handler the init installs on all 20 lines; lines 1, 2, 5, 6, 7, 9 keep their own handlers and 10..19 are disabled, so it never runs.
    // DORMANT: the else branch of "if (following-card == 0x38)" (a plain "return following-card") cannot run: following-card was just set to 0x38 and the init changes nothing.
    // C: the turning-away test sits at the top of every round of the menu loop, but nothing a round
    //   that stays on the card does (only the hidden lines 1 and 2 stay) changes the standing, the
    //   fame or the Virtue, so it is tested once, before the first menu.
    public override int Run(CardContext c)
    {
        Show(c, Picture);
        var line0 = OptionState.Enabled;
        var line3 = OptionState.Enabled;
        c.StandingSeen = c.HereQuery(0);
        if (c.EventAtHere(PrayersBought))
        {
            line0 = OptionState.Shown;
        }
        if (c.EventAtHere(Event31))
        {
            line3 = OptionState.Shown;
        }
        c.Variables.Money[0] = Money.From(0, 0, c.PartySize * 60);
        if (c.State.Party.Purse < c.Variables.Money[0])
        {
            line0 = OptionState.Shown;
        }

        if (c.StandingSeen <= -0x28 || c.PartyGet(4) / 0x14 + c.PartyMaxSkill(Virtue) >= 0x14)
        {
            ShowPicture(c, TurnedAwayPicture);
            c.Screen.Text(1);
            c.Next = ChurchDistrict;
            return c.Next;
        }

        var menu = Menu(c, 0);
        menu.Enable(0, 3, 4, 8);
        menu[0] = line0;
        menu[3] = line3;
        c.Next = Number;
        return menu.Run(
            // orig: R058+0x02EE (1838:02EE) matched (option 0 action, see OfferForPrayers); chance handler R058+0x0434 (see OfferChance)
            new MenuLine(chance => OfferForPrayers(c, chance), () => OfferChance(c)),
            // DORMANT: line 1 is disabled (flag 0) and has no text. orig: R058+0x04A2 (1838:04A2) matched (action: stay here at night, to the day card by day); hint R058+0x04B6 (1838:04B6) "screwed up."
            new MenuLine(() => c.Next = c.IsDaylight ? MonasteryByDay : CardNumber, OptionHint.Saying(CardTexts.ScrewedUp)),
            // DORMANT: line 2 is disabled (flag 0) and has no text. orig: R058+0x04EE (1838:04EE) matched (same action as line 1); hint R058+0x0502 (1838:0502) "screwed up."
            new MenuLine(() => c.Next = c.IsDaylight ? MonasteryByDay : CardNumber, OptionHint.Saying(CardTexts.ScrewedUp)),
            // orig: R058+0x053A (1838:053A) matched (option 3 action, see CryOutForHelp); chance handler R058+0x0606 (see CryOutChance)
            new MenuLine(chance => CryOutForHelp(c, chance), () => CryOutChance(c)),
            // orig: R058+0x068C (1838:068C) matched (option 4 action: ask for sanctuary, refused); hint R058+0x06D4 (1838:06D4)
            new MenuLine(() => SeekSanctuary(c), () => OptionHint.Say(HeraldryLabel, c.Leader)),
            // DORMANT: line 5 is disabled (flag 0). orig: R058+0x0704 (1838:0704) matched (action, see SleepingAbbot); hint R058+0x074C (1838:074C)
            new MenuLine(() => SleepingAbbot(c), () => OptionHint.Say(HeraldryLabel, c.Leader)),
            // DORMANT: line 6 is disabled (flag 0). orig: R058+0x077C (1838:077C) matched (action, same as line 5); hint R058+0x07C4 (1838:07C4)
            new MenuLine(() => SleepingAbbot(c), () => OptionHint.Say(HeraldryLabel, c.Leader)),
            // DORMANT: line 7 is disabled (flag 0). orig: R058+0x07F4 (1838:07F4) matched (action: to the church district); hint R058+0x07FC (1838:07FC) "screwed up."
            new MenuLine(() => c.Next = ChurchDistrict, OptionHint.Saying(CardTexts.ScrewedUp)),
            // orig: R058+0x0834 (1838:0834) matched (option 8 action "go elsewhere": to the church district, no time passes); hint R058+0x083C (1838:083C) "success is assured"
            new MenuLine(() => c.Next = ChurchDistrict, OptionHint.Saying(SuccessAssured)),
            // DORMANT: line 9 is disabled (flag 0) and has no text. orig: R058+0x0874 (1838:0874) matched (action: show the first text block, go to the "no go" card 0x62); hint R058+0x0892 (1838:0892) "success is assured" (its member r.b = leader is overwritten by -2)
            new MenuLine(() => { c.Screen.Text(0); c.PassHours(0); c.Next = NoGo; }, OptionHint.Saying(SuccessAssured)));
    }

    /// <summary>The leader's Virtue and Charisma, plus half the standing.</summary>
    // orig: R058+0x0434 (1838:0434) matched
    private static OptionHint OfferChance(CardContext c)
    {
        var leader = c.Leader;
        var chance = Math.Clamp(c.StandingSeen / 2 + (c.Skill(leader, Virtue) / 4 + c.Attribute(leader, Charisma) / 2), 0, 99);
        return OptionHint.ChanceOf(chance, c.Leader);
    }

    /// <summary>Pays for prayers: every member gains divine favor; failing makes the monks cross.</summary>
    // orig: R058+0x02EE (1838:02EE) matched
    private static void OfferForPrayers(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            c.Variables.Money[0] = Money.From(0, 0, c.PartySize * 60);
            c.State.Party.Purse = c.State.Party.Purse.Subtract(c.Variables.Money[0]);
            c.Variables.Chosen[0] = c.Leader;
            ShowPicture(c, PrayersPicture);
            c.Screen.Text(2);
            ShowPicture(c, Picture);
            for (var member = 0; member < c.PartySize; member++)
            {
                // C: party[member]+0x00 is tested for non-zero: any occupied slot, not only able members.
                if (c.Status(member) != CharacterStatus.Empty)
                {
                    c.AddAttribute(member, DivineFavor, c.Skill(member, Religion) / 10 + 1);
                }
            }
            c.EventHere(PrayersBought, 0xA8);
            // C: card-option-flag-0 = 2 here; the card ends, so it is never shown again.
        }
        else
        {
            if (c.EventAtHere(Rebuffed))
            {
                c.StandingSeen--;
                c.LocationSet(c.Here, 0, c.StandingSeen);
            }
            else
            {
                c.EventHere(Rebuffed, 8);
            }
            ShowPicture(c, PrayersPicture);
            c.Screen.Text(3);
        }
        c.Next = ChurchDistrict;
    }

    /// <summary>
    /// Zero while every member's Strength is above 80% of its maximum (nobody looks hurt),
    /// else the party's average Virtue + 10.
    /// </summary>
    // orig: R058+0x0606 (1838:0606) matched
    private static OptionHint CryOutChance(CardContext c)
    {
        var chance = c.PartyAverageSkill(Virtue) + 10;
        var allWell = true;
        for (var member = 0; member < c.PartySize; member++)
        {
            if (c.Status(member) != CharacterStatus.Empty
                && c.Attribute(member, Strength) <= c.AttributeMaximum(member, Strength) * 8 / 10)
            {
                allWell = false;
            }
        }
        if (allWell)
        {
            chance = 0;
        }
        return OptionHint.ChanceOf(Math.Clamp(chance, 0, 99), OptionHint.WholeParty);
    }

    /// <summary>Cries out: a monk may let the party in (the sanctuary card), else the monks turn it away.</summary>
    // orig: R058+0x053A (1838:053A) matched
    private static void CryOutForHelp(CardContext c, int chance)
    {
        ShowPicture(c, DoorPicture);
        c.Screen.Text(4);
        c.PassHours(1);
        if (c.Random(100) <= chance)
        {
            ShowPicture(c, WelcomePicture);
            c.Screen.Text(5);
            c.State.SavedCard = ChurchDistrict;
            c.Next = Sanctuary;
            return;
        }
        c.EventHere(Rebuffed, 8);
        ShowPicture(c, TurnedAwayPicture);
        c.Screen.Text(6);
        c.Next = ChurchDistrict;
    }

    /// <summary>No tradition of sanctuary here: the monk sends the party to the church.</summary>
    // orig: R058+0x068C (1838:068C) matched
    private static void SeekSanctuary(CardContext c)
    {
        c.EventHere(Rebuffed, 8);
        ShowPicture(c, SanctuaryPicture);
        c.Screen.Text(8);
        c.Next = ChurchDistrict;
    }

    /// <summary>The abbot sleeps: "return tomorrow" (only reachable through the disabled lines 5 and 6).</summary>
    // orig: R058+0x0704 (1838:0704) matched
    // orig: R058+0x077C (1838:077C) matched (byte-identical body to 0704)
    private static void SleepingAbbot(CardContext c)
    {
        c.EventHere(Rebuffed, 8);
        ShowPicture(c, DoorPicture);
        c.Screen.Text(7);
        c.State.SavedCard = ChurchDistrict;
    }
}
