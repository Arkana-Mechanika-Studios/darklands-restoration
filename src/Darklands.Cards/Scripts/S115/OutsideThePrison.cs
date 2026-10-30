namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Dunge03" (card 287, ported from the original code, which has no decompiled source):
/// outside a city's prison where companions are held. The party can charge the gate or sneak
/// in (into the cells, card 286); waiting does nothing yet, and the sewers and leaving are not
/// offered.
/// </summary>
// orig: R115+0x187A (19BF:000A) asm; tests: CardSmokeTests
// orig: R115+0x1E9C (19BF:062C) asm (registration: default R115+0x1F3E; standard item and saint pop-ups; card-item-lines and card-saint-lines all -1)
public sealed class OutsideThePrison : Card
{
    private const int PrisonPicture = 260;
    private const int GatePicture = 236;

    public override int Number => 287;

    public override int Run(CardContext c)
    {
        // The CardTable has no name for card 287; the code copies "$Dunge03" (DS:669C) into card-title.
        c.Screen.Show("$Dunge03", PictureNames.Of(PrisonPicture));
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Set(OptionState.Enabled, 0, 1, 2);
        c.SetCardItems();
        c.SetCardSaints();

        menu.Popup = () => c.MemberPopupHint();

        return menu.Run(
            // orig: R115+0x1B52 (19BF:02E2) asm; hint R115+0x1B8C
            new MenuLine(_ => { ShowPicture(c, PrisonPicture); c.Screen.Text(1); FightTheGuards(c); }),
            // orig: R115+0x1BC4 (line 1); hint R115+0x1C46 (19BF:03D6) asm
            new MenuLine(p => SneakIn(c, p), () => SneakHint(c)),
            // orig: R115+0x1C74 (19BF:0404) asm; hint R115+0x1C80
            // The wait for the companions to be led to the magistrate is unwritten: it lets no time pass and the card asks again.
            new MenuLine(_ => c.PassHours(0)),
            // orig: R115+0x1CB8 (19BF:0448) asm; hint R115+0x1CC4
            // DORMANT: line 3 (the sewers) is never offered; it would let no time pass.
            new MenuLine(_ => c.PassHours(0)),
            // orig: R115+0x1CFC (19BF:048C) asm; hint R115+0x1D10
            // DORMANT: line 4 (other business in the city) is never offered (card-option-flag-4 = 0).
            new MenuLine(_ => c.Next = c.IsDaylight ? 0x12 : 0x19),
            // DORMANT: lines 5..9 are never offered; each lets no time pass (R115+0x1D48, 051c, 0560, 05a4, 05e8).
            new MenuLine(_ => c.PassHours(0)),
            new MenuLine(_ => c.PassHours(0)),
            new MenuLine(_ => c.PassHours(0)),
            new MenuLine(_ => c.PassHours(0)),
            new MenuLine(_ => c.PassHours(0)));
    }

    // C: (party-ability-mean(0x10) + party-ability-mean(0xF)) / 2 by cwd/sar: a signed halving; Streetwise is queried first.
    private static OptionHint SneakHint(CardContext c)
    {
        var streetwise = c.PartyAverageSkill((int)Core.Characters.Skill.Streetwise);
        var stealth = c.PartyAverageSkill((int)Core.Characters.Skill.Stealth);
        return OptionHint.ChanceOf((streetwise + stealth) / 2, OptionHint.WholeParty);
    }

    /// <summary>Looking official, the party walks in (card 286), or the guards block the door.</summary>
    // orig: R115+0x1BC4 (19BF:0354) asm; tests: CardSmokeTests
    // C: the test is `roll-below(100) > p` → blocked, so success is a roll of p or less.
    private static void SneakIn(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            ShowPicture(c, GatePicture);
            c.Screen.Text(3);
            c.Next = 286;
            return;
        }
        ShowPicture(c, GatePicture);
        c.Screen.Text(2);
        FightTheGuards(c);
    }

    /// <summary>
    /// The gate guards (with the prison's fixed seed): won, into the cells (card 286); fled, those
    /// away are reported (twice when the battle result is 1) and the party is back at the start
    /// card, remembering the city's day or night card; beaten, prison (card 0x0D). The city then
    /// turns against the party as in card 286.
    /// </summary>
    // orig: R115+0x1A92 (19BF:0222) asm; tests: CardSmokeTests
    // C: checked against the asm: result 1 falls through into result 2's code (two reports); remembered-card = daylight-test() ? 9 : 10.
    // C: the function's case layout is unusual; the behaviour above is the disassembly's.
    private static void FightTheGuards(CardContext c)
    {
        var guards = c.Random(5) + 3;
        switch (c.RunBattle(0x13, c.Here + 0x6F, 3, 1, guards, 0, 2, 1, -1, 0, 0))
        {
            case 0:
                c.ExtendEventHere(0x12, 30);
                c.Next = 286;
                break;
            case 1:
                c.ReportAway();
                c.ExtendEventHere(0x12, 30);
                c.ReportAway();
                c.State.SavedCard = c.IsDaylight ? 9 : 10;
                c.Next = 1;
                break;
            case 2:
                c.ReportAway();
                c.State.SavedCard = c.IsDaylight ? 9 : 10;
                c.Next = 1;
                break;
            case 3:
            case 4:
                c.Next = 0x0D;
                break;
        }
        c.AdjustReputation(c.Here, -25, -15);
        c.EventHere(0x11, c.HereQuery(0) > -75 ? 120 : 240);
    }
}
