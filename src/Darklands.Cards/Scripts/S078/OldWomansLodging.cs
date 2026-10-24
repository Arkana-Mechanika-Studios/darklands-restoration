using Darklands.Core.Characters;
using Darklands.Core.Economy;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$villa05": lodging at an old woman's home in a village. Hear local rumours, have a meal
/// and a night's sleep, take up residence, trade at her son-in-law's stable, or leave. In a
/// village under evil, the night ends with the cultist villagers attacking.
/// </summary>
// orig: R078+0x0244 (185C:0004) matched; tests: CardSmokeTests
// orig: R078+0x0A30 (185C:07F0) matched; R078+0x0A32 (185C:07F2) matched; tests: CardSmokeTests
// C: the registration routine is empty, so the "screwed up." default is never installed: lines 8..19 keep the handlers (and
// C: opt_on 8..19 the states) an earlier card left; block 0 has eight option lines, so nothing shows them.
// C: checked against the asm: the loop installs the handlers of all eight lines (opt_fn_1..opt_fn_7 are DS:EA18..EA30, opt_fn[1..7]).
public sealed class OldWomansLodging : Card
{
    public const int CardNumber = 0x89;

    private const int Picture = 27;
    private const int VillageEvil = 0x3D;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = CardNumber;
        // C: the picture buffer is the card's: the night attack changes it for later redraws.
        var picture = Picture;
        c.Variables.Location = c.Here;
        // C: the meal costs 1 pfennig in a friendly village, else one per riding animal
        // (item flag 0x2000) plus one per party slot.
        var meal = Money.FromPfennigs(c.LocationQuery(c.Here, 0) >= 50 ? 1 : c.CountItemsWithFlags(Who.Everybody, 0x2000) + c.PartySize);
        c.Variables.Money[0] = meal;
        var menu = Menu(c, resetsHandlers: false);
        menu.Enable(0, 2, 3, 7);
        menu[1] = c.State.Party.Purse < meal ? OptionState.Shown : OptionState.Enabled;
        void CheckThePurse()
        {
            if (c.State.Party.Purse < meal)
            {
                menu[1] = OptionState.Shown;
            }
        }

        return menu.Run(
            // orig: R078+0x05DA (185C:039A) matched; R078+0x064C (185C:040C) matched; tests: CardSmokeTests
            new MenuLine(() =>
            {
                c.PassHours(2);
                c.Events.Create(-2, c.Here, 0x5F, 7, 0x0F, 0, 0, 0x48, 0, 0);
                ShowPicture(c, 233);
                c.Screen.Text(1);
                ShowPicture(c, picture);
                c.Next = CardNumber;
            }, OptionHint.Saying(SuccessAssured)),
            // orig: R078+0x0684 (185C:0444) matched; R078+0x072C (185C:04EC) matched; tests: CardSmokeTests
            new MenuLine(() =>
            {
                // C: && short-circuits: hours_until(0) is only asked in a village under evil.
                if (c.EventAtHere(VillageEvil) && Core.Time.TimeOfDay.HoursUntil(c.State.Date.Hour, 0) <= 9)
                {
                    picture = TheCultistsAttack(c);
                }
                else
                {
                    ShowPicture(c, 196);
                    c.Screen.Text(2);
                    ShowPicture(c, picture);
                    // C: the original never takes the meal's price from the purse.
                    c.AddAttribute(Who.Everybody, 0, 99);
                    c.AddAttribute(Who.Everybody, 1, 1);
                    c.PassHours(9);
                }
                CheckThePurse();
            }, OptionHint.Saying(SuccessAssured)),
            // orig: R078+0x0764 (185C:0524) matched; R078+0x07C4 (185C:0584) matched; tests: CardSmokeTests
            new MenuLine(() =>
            {
                c.Screens.Camp(2);
                if (c.EventAtHere(VillageEvil))
                {
                    c.WaitUntilHour(0);
                    picture = TheCultistsAttack(c);
                }
                else
                {
                    c.Next = c.EventAtHere(0x51) ? 0xB5 : CardNumber;
                }
                CheckThePurse();
            }, OptionHint.Saying(SuccessAssured)),
            // orig: R078+0x07FC (185C:05BC) matched; R078+0x082C (185C:05EC) matched; tests: CardSmokeTests
            new MenuLine(() =>
            {
                c.Screen.Text(5);
                c.PassHours(1);
                c.OpenShop(-1, "Village Stable", 0x2000, 0);
                c.Next = CardNumber;
            }, OptionHint.Saying(SuccessAssured)),
            // orig: R078+0x0864 (185C:0624) matched; R078+0x08C4 (185C:0684) matched; tests: CardSmokeTests
            // DORMANT: storing items: the start-up hides the line (flag 0).
            new MenuLine(() => UseTheTrunk(c, 169, 3, picture), OptionHint.Saying(SuccessAssured)),
            // orig: R078+0x08FC (185C:06BC) matched; R078+0x095C (185C:071C) matched; tests: CardSmokeTests
            // DORMANT: recovering items: hidden as well.
            new MenuLine(() => UseTheTrunk(c, 170, 4, picture), OptionHint.Saying(SuccessAssured)),
            // orig: R078+0x0994 (185C:0754) matched; R078+0x09AE (185C:076E) matched; tests: CardSmokeTests
            // DORMANT: adding an old friend to the party: hidden (flag 0).
            new MenuLine(() =>
            {
                c.PassHours(2);
                c.State.SavedCard = CardNumber;
                c.State.SavedCard2 = CardNumber;
                c.Next = 0x72;
            }, OptionHint.Saying(SuccessAssured)),
            // orig: R078+0x09E6 (185C:07A6) matched; R078+0x09F8 (185C:07B8) matched; tests: CardSmokeTests
            new MenuLine(() => { c.PassHours(0); c.Next = 0x85; }, OptionHint.Saying(SuccessAssured)));
    }

    private static void UseTheTrunk(CardContext c, int trunkPicture, int text, int picture)
    {
        ShowPicture(c, trunkPicture);
        c.Screen.Text(text);
        ShowPicture(c, picture);
        c.Screens.ItemStore();
        c.PassHours(1);
        c.Next = CardNumber;
    }

    /// <summary>
    /// At midnight the cultist villagers storm the house. Escaping leads to their sabbat on the
    /// hill; being beaten, to their altar. Either way the village greets the party with the
    /// cultists from now on. Returns the card's picture afterwards.
    /// </summary>
    // orig: R078+0x0478 (185C:0238) matched; tests: CardSmokeTests
    private static int TheCultistsAttack(CardContext c)
    {
        if (c.State.Date.Hour != 0)
        {
            c.WaitUntilHour(0);
        }
        var picture = 177;
        ShowPicture(c, picture);
        c.Screen.Text(6);
        // C: arguments are evaluated right to left: rand_mod(4) (second group) before rand_mod(3).
        var third = c.PartyLoreLevel() / 4 + 1;
        var second = c.Random(4) + c.PartyLoreLevel() / 3 + 1;
        var first = c.Random(3) + c.PartyLoreLevel() / 4 + 3;
        switch (c.RunBattle(0x18, c.Here, 0x0B, first, second, 4, third, 1, -1, 0, 0))
        {
            case 0:
            case 1:
            case 2:
                c.ReportAway();
                picture = Picture;
                ShowPicture(c, picture);
                c.Screen.Text(7);
                c.PassHours(1);
                c.Next = 0x8A;
                break;
            case 3:
            case 4:
                c.Screen.Text(c.Events.QuestExists(1) ? 9 : 8);
                c.DropAway();
                c.PassHours(3);
                c.Next = 0x8F;
                break;
            default:
                // COVERAGE GAP (reachable: result 5 with a member left Away by an earlier flight; no oracle run ended so): other battle results leave the party at the lodging (with the attack's picture).
                break;
        }
        c.State.Locations[c.Here].ArrivalCard = 0x8A;
        return picture;
    }
}
