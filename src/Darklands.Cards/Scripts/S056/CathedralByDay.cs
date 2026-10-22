using Darklands.Core.Characters;
using Darklands.Core.Economy;
using Darklands.Core.Time;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Cathe00": the city's cathedral by day. The party attends Mass (at the canonical hours,
/// restoring Divine Favor), talks to a priest, donates a third of its money, asks about the
/// patron saint (and may learn of him), asks the canon for a relic (an errand), donates a
/// relic of its own, asks for sanctuary, accepts a relic as a reward, or leaves.
/// (Ported from the assembly: the original has no C.)
/// </summary>
// orig: R056+0x18C8 (19C4:0008) asm; tests: CardSmokeTests
// UNCERTAIN: 0354 and 0514 are read from the disassembly only.
// DORMANT: R056+0x27C8 (19C4:0F08), the init, closes all lines with the "screwed up." handler
//   R056+0x2800 and clears card_items/card_saints; line 9 (flag 0, no handler of its own) stays closed.
public sealed class CathedralByDay : Card
{
    public const int CardNumber = 0x32;

    private const string MsgName = "$Cathe00";

    private const int Picture = 18, MassPicture = 272, NavePicture = 268, AltarPicture = 270, ChapelPicture = 271, CanonPicture = 243;

    private const int CathedralAtNight = 0x33, Church = 0x13, Sanctuary = 0x81, ErrandOffered = 0x90, Errand10Offered = 0x151, CanonsErrand = 0x6F;

    /// <summary>A relic (item flag 0x1000).</summary>
    private const uint Relic = 0x1000;

    /// <summary>The canon's errands (owner 3 of quest tags 3 and 10) and his "asked lately" mark (kind 7, type 3).</summary>
    private const int Canon = 3, MerchantBusy = 7, Politics = 2;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        c.Screen.Show(MsgName, PictureNames.Of(Picture));
        var states = new[] { OptionState.Enabled, OptionState.Enabled, OptionState.Enabled, OptionState.Enabled, OptionState.Enabled,
            OptionState.Shown, OptionState.Enabled, OptionState.Enabled, OptionState.Hidden, OptionState.Hidden };
        c.StandingSeen = c.Reputation(c.Here);
        // C: the purse in pfennigs is a long; the third and the rest are cut to 16-bit words.
        var purse = c.State.Party.Purse.TotalPfennigs;
        if (purse < 0x1C20)
        {
            states[2] = OptionState.Hidden;
        }
        var third = (short)(purse / 3);
        var rest = (short)((short)purse - third);
        c.Variables.Money[0] = Money.FromPfennigs(third);
        c.Variables.Money[1] = Money.FromPfennigs(rest);
        // C: loc_query 38 is the cathedral's patron saint, 37 the relic it keeps.
        if (c.HereQuery(0x26) == 0)
        {
            states[3] = OptionState.Hidden;
        }
        if (c.HereQuery(0x25) == 0)
        {
            states[4] = OptionState.Hidden;
        }
        if (c.CountItemsWithFlags(Who.Everybody, Relic) != 0)
        {
            states[5] = OptionState.Enabled;
        }
        if (!c.Hostile() && c.StandingSeen > -10)
        {
            states[6] = OptionState.Shown;
        }
        if (c.Events.ResolvedQuestAt(0x0A, Canon, c.Here) || c.Events.ResolvedQuestAt(3, Canon, c.Here))
        {
            states[8] = OptionState.Enabled;
            states[4] = OptionState.Hidden;
        }
        if (c.Events.QuestWithValue(0x0A, Canon, c.Here) || c.Events.QuestWithValue(3, Canon, c.Here) || c.EventTypeAtHere(MerchantBusy, Canon))
        {
            states[4] = OptionState.Hidden;
        }
        c.SetCardItems();
        c.SetCardSaints();

        var menu = Menu(c, 0);
        void Set(int line, OptionState state) => menu[line] = state;
        menu.Enable(0, 1, 2, 3, 4, 5, 6, 7, 8);
        for (var k = 0; k <= 8; k++)
        {
            menu[k] = states[k];
        }
        c.Next = Number;
        return menu.Run(
            // orig: R056+0x1D9C (19C4:04DC) + R056+0x1C14 (19C4:0354) R056 seg 19C4 asm
            new MenuLine(() => AttendMass(c), OptionHint.Saying(SuccessAssured)),
            // orig: R056+0x2044 (19C4:0784) + R056+0x1DD4 (19C4:0514) R056 seg 19C4 asm
            new MenuLine(chance => TalkToAPriest(c, chance), OptionHint.Saying(SuccessAssured)),
            // orig: R056+0x21D8 (19C4:0918) + R056+0x207C (19C4:07BC) R056 seg 19C4 asm
            new MenuLine(() => Donate(c), () => OptionHint.Say(SuccessAssured, c.Leader)),
            // orig: R056+0x22F8 (19C4:0A38) + R056+0x2212 (19C4:0952) R056 seg 19C4 asm
            new MenuLine(chance => AskAboutThePatron(c, chance), () => PatronChance(c)),
            // orig: R056+0x2508 (19C4:0C48) + R056+0x2382 (19C4:0AC2) R056 seg 19C4 asm
            new MenuLine(chance => { AskForARelic(c, chance); Set(4, OptionState.Hidden); }, () => RelicChance(c)),
            // orig: R056+0x25FE (19C4:0D3E) + R056+0x252E (19C4:0C6E) R056 seg 19C4 asm
            new MenuLine(() => Set(5, DonateARelic(c)), OptionHint.Saying(SuccessAssured)),
            // orig: R056+0x2648 (19C4:0D88) + R056+0x2636 (19C4:0D76) R056 seg 19C4 asm
            new MenuLine(() => { c.PassHours(0); c.Next = Sanctuary; }, OptionHint.Saying(SuccessAssured)),
            // orig: R056+0x2692 (19C4:0DD2) + R056+0x2680 (19C4:0DC0) R056 seg 19C4 asm
            new MenuLine(() => { c.PassHours(0); c.Next = Church; }, OptionHint.Saying(SuccessAssured)),
            // orig: R056+0x2782 (19C4:0EC2) + R056+0x26CA (19C4:0E0A) R056 seg 19C4 asm
            new MenuLine(() => AcceptTheRelic(c), () => AcceptHint(c)));
    }

    private static int CathedralForTheHour(CardContext c) => c.IsDaylight ? CardNumber : CathedralAtNight;

    private static void BackToTheNave(CardContext c) => ShowPicture(c, Picture);

    /// <summary>
    /// Mass is held at the canonical hours, depending on the city's size: in the small hours
    /// always; later in the day only in bigger cities. When it is held each member regains
    /// Divine Favor (a sixtieth of Religion plus a fortieth of Latin, +1), the party waits for
    /// the end of the hour band, and leaves; otherwise a priest names the next Mass's hour.
    /// </summary>
    private static void AttendMass(CardContext c)
    {
        var held = false;
        var size = c.HereQuery(2);
        var nextMass = 6;
        switch (TimeOfDay.BandFromOne(c.State.Date.Hour))
        {
            case 2:
                held = true;
                break;
            case 3:
            case 4:
            case 5:
                var needed = TimeOfDay.BandFromOne(c.State.Date.Hour) switch { 3 => 6, 4 => 7, _ => 8 };
                if (size >= needed)
                {
                    held = true;
                }
                else if (size > 3)
                {
                    nextMass = 0x12;
                }
                break;
            case 6:
                held = size >= 5;
                break;
            case 7:
                held = size >= 7;
                break;
        }
        if (held)
        {
            for (var i = 0; i < c.PartySize; i++)
            {
                c.AddAttribute(i, 6, c.Skill(i, 8) / 60 + c.Skill(i, 0x0B) / 40 + 1);
            }
            c.WaitUntilHour((TimeOfDay.BandFromOne(c.State.Date.Hour) + 1) * 3);
            ShowPicture(c, MassPicture);
            c.Screen.Text(2);
            BackToTheNave(c);
            c.Next = CathedralForTheHour(c);
            return;
        }
        c.PassHours(0);
        c.NameToSlot(3, nextMass, 0);
        ShowPicture(c, NavePicture);
        c.Screen.Text(1);
        BackToTheNave(c);
        c.Next = CardNumber;
    }

    /// <summary>
    /// A priest talks of the cathedral's relic ($Number1, named) and patron saint. Where the
    /// city's politics are astir he may instead send the party on the canon's errand.
    /// </summary>
    private static void TalkToAPriest(CardContext c, int chance)
    {
        c.PassHours(1);
        var relic = c.HereQuery(0x25);
        c.Variables.Numbers[0] = relic;
        if (relic != 0)
        {
            c.NameToSlot(0, relic, 0);
        }
        if (c.Events.QuestAt(Politics, c.Here) && c.Events.FactValue(Politics, 0, c.Here) != 0)
        {
            // C: the chance is the hint's −1, so the roll is never under it: the errand branch is dormant.
            if (c.Random(100) < chance)
            {
                ShowPicture(c, CanonPicture);
                c.Screen.Text(0x14);
                c.State.SavedCard = CathedralForTheHour(c);
                c.Next = CanonsErrand;
                return;
            }
            ShowPicture(c, NavePicture);
            c.Screen.Text(0x15);
        }
        else if (c.HereQuery(0x26) == 0)
        {
            ShowPicture(c, relic == 0 ? AltarPicture : ChapelPicture);
            c.Screen.Text(relic == 0 ? 3 : relic == 1 ? 4 : 5);
        }
        else
        {
            c.NameToSlot(1, c.HereQuery(0x26), 1);
            ShowPicture(c, relic == 0 ? NavePicture : ChapelPicture);
            c.Screen.Text(relic == 0 ? 6 : relic == 1 ? 7 : 8);
        }
        BackToTheNave(c);
        c.Next = CathedralForTheHour(c);
    }

    /// <summary>
    /// Gives a third of the party's money: the leader gains Divine Favor (a two-hundredth of
    /// the gift, at most 30) and practises Virtue; the priests' thanks grow with the gift.
    /// </summary>
    // C: the gift is the 16-bit low word of the third; the thresholds compare it as a signed number.
    private static void Donate(CardContext c)
    {
        var gift = (short)(c.State.Party.Purse.TotalPfennigs / 3);
        c.State.Party.Purse = c.State.Party.Purse.Subtract(0, 0, gift);
        c.AddAttribute(Who.Leader, 6, Math.Clamp(gift / 200, 0, 30));
        c.PracticeSkill(Who.Leader, 9, 7, 10, 0);
        if (gift < 0xF0)
        {
            ShowPicture(c, NavePicture);
            c.Screen.Text(9);
        }
        else if (gift < 0x5A0)
        {
            ShowPicture(c, NavePicture);
            c.Screen.Text(0x0A);
        }
        else if (gift < 0xE10)
        {
            ShowPicture(c, NavePicture);
            c.Screen.Text(0x0B);
        }
        else
        {
            ShowPicture(c, ChapelPicture);
            c.Screen.Text(0x0C);
        }
        c.PassHours(1);
        BackToTheNave(c);
        c.Next = CathedralForTheHour(c);
    }

    /// <summary>A third of the leader's half Religion, half Virtue, Read &amp; Write and the standing (1..99); nothing in bad standing.</summary>
    private static OptionHint PatronChance(CardContext c)
    {
        int chance;
        if (c.StandingSeen < 0)
        {
            chance = 0;
        }
        else
        {
            var virtue = c.Skill(Who.Leader, 9);
            var religion = c.Skill(Who.Leader, 8);
            var letters = c.Skill(Who.Leader, 0x0C);
            chance = (religion / 2 + virtue / 2 + letters + c.StandingSeen) / 3;
        }
        return OptionHint.ChanceOf(Math.Clamp(chance, 1, 99), c.Leader);
    }

    /// <summary>The leader learns of the patron saint (he now knows him), after a time that is shorter the more religious he is.</summary>
    private static void AskAboutThePatron(CardContext c, int chance)
    {
        var saint = c.HereQuery(0x26);
        if (c.Random(100) < chance)
        {
            c.PassHours((100 - c.Skill(Who.Leader, 8)) / 10);
            c.LearnSaint(Who.Leader, saint);
            c.Variables.Chosen[0] = c.Leader;
            ShowPicture(c, NavePicture);
            c.Screen.Text(0x0D);
        }
        else
        {
            c.PassHours(1);
            ShowPicture(c, NavePicture);
            c.Screen.Text(0x0E);
        }
        BackToTheNave(c);
        c.Next = CathedralForTheHour(c);
    }

    /// <summary>The standing − 50 (1..99).</summary>
    private static OptionHint RelicChance(CardContext c) => OptionHint.ChanceOf(Math.Clamp(c.StandingSeen - 50, 1, 99), c.Leader);

    /// <summary>
    /// Asks the canon for the relic: under half the chance he sets the party an errand (quest 3
    /// to a nearby site, the relic its reward); under the chance a quest-10 errand; else he
    /// refuses and is not asked again for 3 days. The line closes either way.
    /// </summary>
    private static void AskForARelic(CardContext c, int chance)
    {
        var roll = c.Random(100);
        if (roll <= chance / 2)
        {
            var here = c.State.Locations[c.Here];
            c.Variables.Location = c.NearestLocationOfType(here.X, here.Y, 2);
            c.StartQuest3(Canon, c.Variables.Location, c.City.Ordinal + 0x65, 0x0F, 2, c.HereQuery(0x25));
            c.PassHours(6);
            c.State.SavedCard = CathedralForTheHour(c);
            c.State.SavedCard2 = c.Variables.Location;
            c.Next = ErrandOffered;
            return;
        }
        if (roll <= chance)
        {
            ShowPicture(c, CanonPicture);
            c.Screen.Text(0x0F);
            // BUG: the errand is made for giver 0, the town's merchants, where the card's own tests (an errand open, one done: option
            // 8) look for 3, the bishop: the merchants pay for it and the canon gives a new errand at every visit.
            c.State.SavedCard = c.StartQuest10(0);
            c.State.SavedCard2 = CathedralForTheHour(c);
            c.Next = Errand10Offered;
            return;
        }
        ShowPicture(c, NavePicture);
        c.Screen.Text(0x10);
        c.PassHours(1);
        c.Events.Create(-2, c.Here, 0x5F, MerchantBusy, Canon, 0, 0, 72, 0, 0);
        BackToTheNave(c);
        c.Next = CathedralForTheHour(c);
    }

    /// <summary>
    /// Gives one of the party's relics (named): the standing rises by 30 and everyone's Divine
    /// Favor is restored. Returns the relic line's new state (enabled while relics remain).
    /// </summary>
    private static OptionState DonateARelic(CardContext c)
    {
        c.AdjustReputation(c.Here, 30, 30);
        c.AddAttribute(Who.Everybody, 6, 99);
        c.Variables.Chosen[0] = c.Leader;
        c.NameToSlot(0, c.FirstItemWithFlags(Who.Everybody, Relic), 0);
        c.TakeItemWithFlags(Who.Everybody, Relic);
        var state = c.CountItemsWithFlags(Who.Everybody, Relic) != 0 ? OptionState.Enabled : OptionState.Shown;
        ShowPicture(c, ChapelPicture);
        c.Screen.Text(0x12);
        BackToTheNave(c);
        c.PassHours(3);
        c.Next = CathedralForTheHour(c);
        return state;
    }

    private static OptionHint AcceptHint(CardContext c) =>
        c.StandingSeen > -10 ? OptionHint.Say(SuccessAssured) : OptionHint.ChanceOf(0, OptionHint.WholeParty);

    /// <summary>The errand done, the canon gives the cathedral's relic to the leader (not in bad standing).</summary>
    private static void AcceptTheRelic(CardContext c)
    {
        var relic = c.HereQuery(0x25);
        c.Variables.Item = relic;
        if (c.StandingSeen > -10)
        {
            // BUG: neither the thing fetched is taken nor the reward event ended: the relic can be taken again and again.
            c.AddItem(Who.Leader, relic);
            ShowPicture(c, CanonPicture);
            c.Screen.Text(0x11);
            c.PassHours(2);
        }
        else
        {
            ShowPicture(c, CanonPicture);
            c.Screen.Text(0x13);
            c.PassHours(1);
        }
        BackToTheNave(c);
        c.Next = CathedralForTheHour(c);
    }
}
