using Darklands.Core.Characters;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Count00": a castle keep on a knoll among farmland. The party can ask at the gate to see
/// the lord, talk to the farmers, pray for guidance about the place, or wait for midnight to
/// break in. Whether the lord is cruel is fixed for each place by the party's seed; the
/// farmers' and the saints' answers depend on it.
/// </summary>
// orig: R104+0x14A0 (1982:0000) matched; tests: CardSmokeTests
// orig: R104+0x1BE6 (1982:0746) matched (registration; its saint pop-ups R104+0x1DB0.. do not name the member)
public sealed class CastleOnTheKnoll : Card
{
    private const int CastlePicture = 67;
    private const int GuardPicture = 287;
    private const int FarmersPicture = 160;

    public override int Number => 242;

    public override int Run(CardContext c)
    {
        Show(c, CastlePicture);
        c.Next = Number;
        // C: the block is chosen anew each round (party-random-seed + (char)location-now & 1); it cannot change.
        var menu = Menu(c, LordIsCruel(c) ? 0 : 10);
        menu.Set(OptionState.Enabled, 0, 1, 2, 3, 9);
        if (c.EventAtHere(0x5A))
        {
            menu[0] = OptionState.Shown;
        }
        if (c.EventAtHere(0x5B))
        {
            menu[1] = OptionState.Shown;
        }
        c.SetCardSaints(0x35, 0x44, 0x47);
        menu[2] = c.SaintsUsable();
        menu.Popup = () => c.SaintPopupChanceHint(); // this card's saint pop-up does not name the member

        return menu.Run(
            // orig: R104+0x1714 (line 0); hint R104+0x1794 (1982:02F4) matched
            new MenuLine(p => AskAtTheGate(c, menu, p), () => GateHint(c)),
            // orig: R104+0x1804 (line 1); hint R104+0x18E8 (1982:0448) matched
            new MenuLine(p => TalkToTheFarmers(c, p), () => FarmersHint(c)),
            new MenuLine(_ => PrayForGuidance(c), () => OptionHint.Say("Select a person and a saint.")),
            // orig: R104+0x19F2 (1982:0552) matched
            new MenuLine(_ => { c.PassHours(c.HoursUntil(18) + 6); c.Next = 244; }, Assured),
            // DORMANT: lines 4..8 are never offered (card-option-flag-4..8 = 0); each only lets no time pass.
            // orig: R104+0x1A48 (line 4)
            new MenuLine(_ => c.PassHours(0), Assured),
            // orig: R104+0x1A8C (line 5)
            new MenuLine(_ => c.PassHours(0), Assured),
            // orig: R104+0x1AD0 (line 6)
            new MenuLine(_ => c.PassHours(0), Assured),
            // orig: R104+0x1B14 (line 7)
            new MenuLine(_ => c.PassHours(0), Assured),
            // orig: R104+0x1B58 (line 8)
            new MenuLine(_ => c.PassHours(0), Assured),
            // orig: R104+0x1B9C (1982:06FC) matched (leaves without clearing location-now)
            new MenuLine(_ => { c.PassHours(0); c.Next = CardContext.MapCard; }, Assured));
    }

    private static OptionHint Assured() => OptionHint.Say(SuccessAssured);

    /// <summary>Each castle's lord is either just or cruel, by the party's seed and the place.</summary>
    // C: (char)location-now keeps bit 0, so the parity is that of party-random-seed + location-now.
    private static bool LordIsCruel(CardContext c) => ((c.State.Party.Seed + (sbyte)c.Here) & 1) != 0;

    /// <summary>The leader's speech, charm and fame; a cruel lord's guards are harder to win over.</summary>
    // C: no clamp on this chance; integer division of a non-negative sum.
    private static OptionHint GateHint(CardContext c)
    {
        var chance = (c.Skill(Who.Leader, (int)Skill.SpeakCommon) + c.Attribute(Who.Leader, (int)Attribute.Charisma) + c.PartyGet(4) / 40) / 3;
        if (LordIsCruel(c))
        {
            chance = chance * 7 / 10;
        }
        return OptionHint.ChanceOf(chance, c.Leader);
    }

    private static OptionHint FarmersHint(CardContext c) =>
        OptionHint.ChanceOf(Math.Clamp(
            (c.Skill(Who.Leader, (int)Skill.Woodwise) / 2 + c.Attribute(Who.Leader, (int)Attribute.Charisma) + c.Skill(Who.Leader, (int)Skill.SpeakCommon)) * 6 / 10,
            0, 99), c.Leader);

    /// <summary>Admitted, the party goes on to the gate; turned away, it may not ask again for a week.</summary>
    // orig: R104+0x1714 (1982:0274) matched; tests: CardSmokeTests
    private static void AskAtTheGate(CardContext c, CardMenu menu, int chance)
    {
        c.Variables.Chosen[0] = c.Leader;
        menu[0] = OptionState.Shown;
        if (c.Random(100) <= chance)
        {
            c.PassHours(1);
            c.Next = 243;
            return;
        }
        ShowPicture(c, GuardPicture);
        c.Screen.Text(1);
        ShowPicture(c, CastlePicture);
        c.EventHere(0x5A, 168);
        c.Next = 242;
    }

    /// <summary>The farmers say what their lord is like, if they open up (two hours either way).</summary>
    // orig: R104+0x1804 (1982:0364) matched; tests: CardSmokeTests
    // The line is not greyed after use: the event only counts on the next visit.
    private static void TalkToTheFarmers(CardContext c, int chance)
    {
        c.Variables.Chosen[0] = c.Leader;
        c.EventHere(0x5B, 168);
        // C: the roll comes before the two hours pass (time passing may draw random numbers).
        if (c.Random(100) <= chance)
        {
            c.PassHours(2);
            ShowPicture(c, FarmersPicture);
            c.Screen.Text(LordIsCruel(c) ? 11 : 3);
        }
        else
        {
            c.PassHours(2);
            ShowPicture(c, FarmersPicture);
            c.Screen.Text(2);
        }
        ShowPicture(c, CastlePicture);
        c.Next = 242;
    }

    /// <summary>A vision of the lord's nature when the saint answers.</summary>
    // orig: R104+0x1952 (1982:04B2) matched; tests: CardSmokeTests
    private static void PrayForGuidance(CardContext c)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuEntrySlot]);
        if (answer == -1)
        {
            return;
        }
        if (answer == 1)
        {
            c.PassHours(2);
            c.Screen.Text(LordIsCruel(c) ? 12 : 6);
        }
        else
        {
            c.Screen.Text(9);
        }
        c.Next = 242;
    }
}
