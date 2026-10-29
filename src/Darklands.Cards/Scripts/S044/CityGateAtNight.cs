using Darklands.Core.Economy;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$CityG00": the city gate at night, shut. The party asks to be let in, talks to the watch,
/// bribes it, uses a potion, calls upon a saint, or waits outside for the morning.
/// </summary>
// orig: R044+0x28E8 (1AC6:0008) matched; tests: CardSmokeTests
public sealed class CityGateAtNight : Card
{
    public const int CardNumber = 0x03;

    private const int Picture = 4, GuardPicture = 10, OtherGatePicture = 6;

    /// <summary>Events at a city: refused entry by night, refused talk, fled a potion, banned.</summary>
    private const int RefusedAtNight = 0x0C, RefusedTalk = 0x0D, PotionPanic = 0x0E, Banned = 0x11;

    private const int DayGate = 0x02, CityOutskirts = 0x04, MainStreetAtNight = 0x08, SideStreet = 0x09, SideStreetAtNight = 0x0A;

    private const int SleepingPotion = 0x66;

    public override int Number => CardNumber;

    // orig: R044+0x28E8 (1AC6:0008) matched (entry: music, the bribe amount, option states and the menu loop)
    // orig: R044+0x3148 (1AC6:0868) matched (init helper: enables all 20 option lines and clears the item and saint slots; the card then fills lines 0..5, 2 items and 2 saints)
    // DORMANT: R044+0x31BA (1AC6:08DA): the init helper points opt_fn[0..19] at this "screwed up." default; lines 6..19 stay enabled but the card's MSG has no line for them and the menu cannot select them, so it never runs.
    // DORMANT: item_fn[k][2..3] and saint_fn[k][2..3] are never installed by this file; the pop-ups hold only 2 items (slots 0..1) and 2 saints (slots 0..1), the other slots stay empty (-1). The C# PotionChance slot > 1 branch ("screwed up.") has no counterpart function here.
    // C: item_fn/saint_fn are set identically for all 5 member rows (k = 0..4); the handlers read the member from menu_member, so the C# keeps one handler per slot.
    public override int Run(CardContext c)
    {
        c.PlayMusic(6);
        Show(c, Picture);
        c.StandingSeen = c.Reputation(c.Here);
        var perMember = c.HereQuery(2) / 3 + 1;
        var bribeValue = c.PartySize * perMember * 18 / 10;
        if (c.StandingSeen < 0)
        {
            bribeValue = (100 - c.StandingSeen) / 33;
        }
        var bribe = Money.FromPfennigs(bribeValue);
        c.Variables.Money[0] = bribe;

        var states = new[] { OptionState.Enabled, OptionState.Enabled, OptionState.Enabled };
        if (c.State.Party.Purse < bribe)
        {
            states[2] = OptionState.Shown;
        }
        if (c.EventAtHere(RefusedAtNight))
        {
            states[0] = OptionState.Shown;
        }
        if (c.EventAtHere(RefusedTalk))
        {
            states[1] = OptionState.Shown;
        }
        c.SetCardItems(SleepingPotion, 0x63);
        c.SetCardSaints(0x59, 0x61);

        var menu = Menu(c, 0);
        void Close(params int[] lines)
        {
            foreach (var k in lines)
            {
                menu[k] = OptionState.Shown;
            }
        }
        menu.Enable(0, 1, 2, 3, 4, 5);
        menu[0] = states[0];
        menu[1] = states[1];
        menu[2] = states[2];
        menu[3] = c.ItemsUsable();
        menu[4] = c.SaintsUsable();
        c.Next = Number;
        return menu.Run(
            // orig: R044+0x2B60 (1AC6:0280) matched (option 0 action, see AskToBeLetIn; chance handler R044+0x2C48, see LetInChance)
            new MenuLine(chance => AskToBeLetIn(c, chance, Close), () => LetInChance(c)),
            // orig: R044+0x2CAA (1AC6:03CA) matched (option 1 action, see TalkToTheWatch; chance handler R044+0x2D8E, see TalkChance)
            new MenuLine(chance => TalkToTheWatch(c, chance, Close), () => TalkChance(c)),
            // orig: R044+0x2DE8 (1AC6:0508) matched (option 2 action, see BribeTheWatch; chance handler R044+0x2E60, see BribeChance)
            new MenuLine(_ => BribeTheWatch(c, bribe, Close), () => BribeChance(c)),
            // orig: R044+0x2F8C (1AC6:06AC) matched (option 3 hint "select a potion"; action R044+0x2E8C, see UseAPotion; pop-up handlers 0912/0970, see PotionChance)
            new MenuLine(chance => UseAPotion(c, chance), OptionHint.Saying(CardTexts.SelectPotion), () => PotionChance(c, c.MenuSlot)),
            // orig: R044+0x305A (1AC6:077A) matched (option 4 hint "select a saint"; action R044+0x2FC4, see CallUponASaint; pop-up handlers 09cc/0a56, see SaintChance)
            new MenuLine(_ => CallUponASaint(c), OptionHint.Saying(CardTexts.SelectSaint), () => SaintChance(c)),
            // orig: R044+0x3110 (1AC6:0830) matched (option 5 hint "no risk"; action R044+0x3092, see WaitOutside)
            new MenuLine(() => WaitOutside(c), OptionHint.Saying(NoRisk)));
    }

    private static int GateForTheHour(CardContext c) => c.IsDaylight ? DayGate : CardNumber;

    /// <summary>Half the standing plus the party's fame (at most 100 of it); nothing in bad standing or a hostile city.</summary>
    // orig: R044+0x2C48 (1AC6:0368) matched
    private static OptionHint LetInChance(CardContext c)
    {
        var standing = c.HereQuery(0);
        var fame = Math.Clamp(c.PartyGet(4), 0, 100);
        var chance = standing > -10 && !c.Hostile() ? Math.Max(0, standing / 2 + fame) : 0;
        return OptionHint.ChanceOf(chance, OptionHint.WholeParty);
    }

    // orig: R044+0x2B60 (1AC6:0280) matched
    private static void AskToBeLetIn(CardContext c, int chance, Action<int[]> close)
    {
        if (c.HereQuery(0) > -10 && !c.Hostile())
        {
            if (c.Random(100) < chance)
            {
                c.Screen.Text(1, TextFrame.BlankPage);
                c.PassHours(0);
                c.Next = MainStreetAtNight;
                return;
            }
            c.ExtendEventHere(RefusedAtNight, 12);
            c.Screen.Text(2, TextFrame.BlankPage);
            close([0]);
            c.PassHours(1);
            c.Next = c.IsDaylight ? DayGate : CardNumber;
            return;
        }
        ShowPicture(c, GuardPicture);
        c.Screen.Text(3);
        ShowPicture(c, Picture);
        close([0, 1, 2]);
        c.PassHours(0);
        c.Next = CardNumber;
    }

    /// <summary>The leader's Intelligence twice plus Speak Common, halved; nothing at a standing of −40 or worse.</summary>
    // orig: R044+0x2D8E (1AC6:04AE) matched
    private static OptionHint TalkChance(CardContext c)
    {
        c.StandingSeen = c.HereQuery(0);
        var chance = c.StandingSeen <= -40 ? 0 : (c.Attribute(c.Leader, 4) * 2 + c.Skill(c.Leader, 0x0A)) / 2;
        return OptionHint.ChanceOf(chance, c.Leader);
    }

    // orig: R044+0x2CAA (1AC6:03CA) matched
    private static void TalkToTheWatch(CardContext c, int chance, Action<int[]> close)
    {
        if (c.HereQuery(0) > -40 && !c.Hostile())
        {
            if (c.Random(100) < chance)
            {
                c.Screen.Text(4, TextFrame.BlankPage);
                c.PracticeSkill(c.Leader, 0x0A, 7, 10, 0);
                c.PassHours(1);
                c.Next = c.IsDaylight ? SideStreet : SideStreetAtNight;
                return;
            }
            c.ExtendEventHere(RefusedTalk, 12);
            c.Screen.Text(2, TextFrame.BlankPage);
            close([1]);
            c.PracticeSkill(c.Leader, 0x0A, 0, 10, 0);
            c.PassHours(1);
            c.Next = GateForTheHour(c);
            return;
        }
        c.Screen.Text(3, TextFrame.BlankPage);
        close([0, 1, 2]);
        c.PassHours(0);
        c.Next = CardNumber;
    }

    // orig: R044+0x2E60 (1AC6:0580) matched
    private static OptionHint BribeChance(CardContext c)
    {
        c.StandingSeen = c.HereQuery(0);
        return OptionHint.ChanceOf(c.StandingSeen <= -10 ? 0 : 100, OptionHint.WholeParty);
    }

    // orig: R044+0x2DE8 (1AC6:0508) matched
    private static void BribeTheWatch(CardContext c, Money bribe, Action<int[]> close)
    {
        c.StandingSeen = c.HereQuery(0);
        if (c.StandingSeen > -10)
        {
            c.State.Party.Purse = c.State.Party.Purse.Subtract(bribe);
            c.Screen.Text(5, TextFrame.BlankPage);
            c.PassHours(1);
            c.Next = c.IsDaylight ? SideStreet : SideStreetAtNight;
            return;
        }
        c.Screen.Text(3, TextFrame.BlankPage);
        close([0, 1, 2]);
        c.PassHours(0);
        c.Next = CardNumber;
    }

    /// <summary>The member's Stealth: one and a half times for the first potion, four thirds for the second; less after a panic.</summary>
    // orig: R044+0x31F2 (1AC6:0912) matched (slot 0: 3/2 x Stealth)
    // orig: R044+0x3250 (1AC6:0970) matched (slot 1: 4/3 x Stealth)
    private static OptionHint PotionChance(CardContext c, int slot)
    {
        if (slot > 1)
        {
            return OptionHint.Say(CardTexts.ScrewedUp);
        }
        c.Variables.Chosen[0] = c.MenuMember;
        var alchemy = c.Skill(c.MenuMember, 0x0F);
        var chance = slot == 0 ? alchemy * 3 / 2 : alchemy * 4 / 3;
        if (c.EventAtHere(PotionPanic))
        {
            chance = chance * 4 / 10;
        }
        return OptionHint.ChanceOf(Math.Clamp(chance, 0, 100), c.MenuMember);
    }

    // orig: R044+0x2E8C (1AC6:05AC) matched
    private static void UseAPotion(CardContext c, int chance)
    {
        var potion = c.CardItems[c.MenuSlot];
        c.TakeItem(c.MenuMember, potion);
        if (c.Random(100) < chance)
        {
            if (potion == SleepingPotion)
            {
                c.Screen.Text(7, TextFrame.BlankPage);
                c.PracticeSkill(c.MenuMember, 0x0F, 7, 10, 0);
                c.AdjustReputation(c.Here, -2, -6);
                c.PassHours(0);
                c.Next = SideStreetAtNight;
                return;
            }
            c.Screen.Text(8, TextFrame.BlankPage);
            c.PracticeSkill(c.MenuMember, 0x0F, 7, 10, 1);
            c.PassHours(1);
            c.Next = c.IsDaylight ? SideStreet : SideStreetAtNight;
            return;
        }
        c.ExtendEventHere(PotionPanic, 30);
        c.ExtendEventHere(Banned, 240);
        c.Screen.Text(9, TextFrame.BlankPage);
        c.PracticeSkill(c.MenuMember, 0x0F, 0, 10, 0);
        c.PassHours(1);
        c.Next = GateForTheHour(c);
    }

    // orig: R044+0x32AC (1AC6:09CC) matched (saint slot 0)
    // orig: R044+0x3336 (1AC6:0A56) matched (saint slot 1; byte-identical body to slot 0)
    private static OptionHint SaintChance(CardContext c)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        var saint = c.CardSaints[c.MenuEntrySlot];
        var low = c.SaintChanceLow(c.MenuMember, saint);
        return OptionHint.Say(c.ChanceText(low, c.SaintChanceHigh(c.MenuMember, saint)));
    }

    // orig: R044+0x2FC4 (1AC6:06E4) matched
    private static void CallUponASaint(CardContext c)
    {
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuEntrySlot]);
        if (answer == -1)
        {
            return;
        }
        if (answer != 0)
        {
            c.Screen.Text(0x0A);
            if (c.Reputation(c.Here) > -10)
            {
                c.AdjustReputation(c.Here, 2, 6);
            }
            else
            {
                c.AdjustReputation(c.Here, -2, -6);
            }
            c.PassHours(0);
            c.Next = MainStreetAtNight;
            return;
        }
        c.Screen.Text(0x0B, TextFrame.BlankPage);
        c.PassHours(1);
        c.Next = GateForTheHour(c);
    }

    /// <summary>Walks off to wait outside the walls (one of two gate pictures at random).</summary>
    // orig: R044+0x3092 (1AC6:07B2) matched
    private static void WaitOutside(CardContext c)
    {
        ShowPicture(c, c.Random(1000) % 2 != 0 ? Picture : OtherGatePicture);
        c.Screen.Text(0x0C);
        ShowPicture(c, Picture);
        c.PassHours(1);
        c.Next = CityOutskirts;
    }
}
