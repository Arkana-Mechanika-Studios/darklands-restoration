using Darklands.Core.Characters;
using Darklands.Core.Economy;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$CityG01": the city gate by day. The party pays the toll, talks or sneaks its way past
/// the guards, uses a potion or calls upon a saint, fights its way in, or walks away.
/// </summary>
// orig: R044+0x1A50 (19DD:0000) matched; tests: CardSmokeTests
public sealed class CityGate : Card
{
    public const int CardNumber = 0x02;

    private const int Picture = 4;
    private const int SmokePicture = 20;
    private const int FlashPicture = 19;

    /// <summary>Events at a city: turned away at the gate, caught sneaking, fled a potion, banned, the gate fought.</summary>
    private const int TurnedAway = 0x0A, CaughtSneaking = 0x0B, PotionPanic = 0x0E, Banned = 0x11, GateStormed = 0x12, Fought = 0x23;

    /// <summary>
    /// Set once the party is seen banned from a city (the original's module flag): it is never
    /// cleared again while the game runs, so talking stays impossible at every gate.
    /// </summary>
    private bool _seenBanned;

    public override int Number => CardNumber;

    // orig: R044+0x1A50 (19DD:0000) matched (entry: toll, ban and gate-guard set-up, the option table and the menu loop)
    // orig: R044+0x253C (19DD:0AEC) matched (init helper: enables all 20 option lines and clears the item and saint slots; the card then fills lines 0..6, 3 items and 2 saints)
    // DORMANT: R044+0x25BA (19DD:0B6A): the init helper points opt_fn[0..19] at this "screwed up." default; lines 7..19 stay enabled but the card's MSG has no line for them and the menu cannot select them, so it never runs.
    // DORMANT: R044+0x272C (19DD:0CDC), R044+0x2878 (19DD:0E28) and R044+0x28B0 (19DD:0E60): further "screwed up." handlers that nothing in this file installs; the pop-ups only hold 3 items (item_fn slots 0..2) and 2 saints (saint_fn slots 0..1), the other slots stay empty (-1).
    // C: the C# PotionChance maps slot > 2 to the "screwed up." text as if it were R044+0x272C; the C never installs that handler for item_fn[k][3], and slot 3 is empty anyway.
    // C: item_fn/saint_fn are set identically for all 5 member rows (k = 0..4); the handler reads the member from menu_member, so the C# keeps one handler per slot.
    public override int Run(CardContext c)
    {
        Show(c, Picture);
        var toll = Money.FromPfennigs((c.HereQuery(2) / 3 + 1) * c.PresentCount());
        c.Variables.Money[0] = toll;
        var canPay = c.State.Party.Purse >= toll;
        c.Variables.Money[1] = canPay ? c.State.Party.Purse.Subtract(toll) : Money.Zero;
        var canTalk = !(c.EventAtHere(TurnedAway) || c.EventAtHere(Banned));
        var canSneak = !c.EventAtHere(CaughtSneaking);
        if (c.EventAtHere(Banned))
        {
            _seenBanned = true;
        }
        c.SetCardItems(0x5F, 0x65, 0x61);
        c.SetCardSaints(0x59, 0x61);

        var menu = Menu(c, c.EventAtHere(GateStormed) ? 0x12 : 0);
        menu.Enable(0, 1, 2, 3, 4, 5, 6);
        menu[0] = canPay ? OptionState.Enabled : OptionState.Shown;
        menu[1] = canTalk ? OptionState.Enabled : OptionState.Shown;
        menu[2] = canSneak ? OptionState.Enabled : OptionState.Shown;
        menu[3] = c.ItemsUsable();
        menu[4] = c.SaintsUsable();
        c.Next = Number;
        return menu.Run(
            // orig: R044+0x1D58 (19DD:0308) matched (option 0 action, see PayTheToll); chance handler R044+0x1DB6 (see TollChance)
            new MenuLine(chance => PayTheToll(c, toll, chance), () => TollChance(c)),
            // orig: R044+0x1E2A (19DD:03DA) matched (option 1 action, see TalkYourWayIn); chance handler R044+0x1F02 (see TalkChance)
            new MenuLine(chance =>
                {
                    if (TalkYourWayIn(c, chance))
                    {
                        menu[1] = OptionState.Shown;
                    }
                },
                () => TalkChance(c)),
            // orig: R044+0x1F7E (19DD:052E) matched (option 2 action, see SneakIn); chance handler R044+0x202A (see SneakChance)
            new MenuLine(chance => SneakIn(c, chance), () => SneakChance(c)),
            // orig: R044+0x21A8 (19DD:0758) matched (option 3 hint "select a potion"); action R044+0x206C (see UseAPotion), pop-up handlers 0ba2/0c0c/0c74 (see PotionChance)
            new MenuLine(chance => UseAPotion(c, chance), OptionHint.Saying(CardTexts.SelectPotion), () => PotionChance(c, c.MenuSlot)),
            // orig: R044+0x2284 (19DD:0834) matched (option 4 hint "select a saint"); action R044+0x21E0 (see CallUponASaint), pop-up handlers 0d14/0d9e (see SaintChance)
            new MenuLine(_ => CallUponASaint(c), OptionHint.Saying(CardTexts.SelectSaint), () => SaintChance(c)),
            // orig: R044+0x24BA (19DD:0A6A) matched (option 5 hint "enter combat"); action R044+0x22BC (see StormTheGate)
            new MenuLine(() => StormTheGate(c), OptionHint.Saying(CardTexts.EnterCombat)),
            // orig: R044+0x2504 (19DD:0AB4) matched (option 6 hint "no risk"); action R044+0x24F2 (19DD:0AA2): next card 4, no time passes
            new MenuLine(() => { c.Next = CityOutskirts; c.PassHours(0); }, OptionHint.Saying(NoRisk)));
    }

    private const int Outside = 0x01, NightGate = 0x03, CityOutskirts = 0x04, MainStreet = 0x06, MainStreetAtNight = 0x08,
        SideStreet = 0x09, SideStreetAtNight = 0x0A, OverlandMap = 0x0C, Dungeon = 0x0D;

    private int StreetForTheHour(CardContext c) => c.IsDaylight ? MainStreet : MainStreetAtNight;

    /// <summary>Certain unless the party is in bad standing; doubly hard while the gate is guarded after a fight.</summary>
    // orig: R044+0x1DB6 (19DD:0366) matched
    private static OptionHint TollChance(CardContext c)
    {
        var standing = c.StandingSeen;
        int chance;
        if (standing > -10 && !c.EventAtHere(Banned))
        {
            var factor = c.EventAtHere(GateStormed) ? 2 : 1;
            chance = standing < 0 ? Math.Clamp(factor * standing + 100, 1, 99) : 100;
        }
        else
        {
            chance = 0;
        }
        return OptionHint.ChanceOf(chance, OptionHint.WholeParty);
    }

    // orig: R044+0x1D58 (19DD:0308) matched
    private void PayTheToll(CardContext c, Money toll, int chance)
    {
        if (c.Random(100) <= chance && !c.EventAtHere(Banned))
        {
            c.State.Party.Purse = c.State.Party.Purse.Subtract(toll);
            c.PassHours(1);
            c.Next = StreetForTheHour(c);
            return;
        }
        c.PassHours(0);
        c.Next = Outside;
    }

    /// <summary>The leader's Charisma or Speak Common, whichever is better, plus half the standing.</summary>
    // orig: R044+0x1F02 (19DD:04B2) matched
    private OptionHint TalkChance(CardContext c)
    {
        var chance = 0;
        if (c.StandingSeen > -10 && !_seenBanned && !c.Hostile())
        {
            var charisma = c.Attribute(c.Leader, 5);
            chance = Math.Clamp(c.StandingSeen / 2 + Math.Max(charisma, c.Skill(c.Leader, 0x0A)), 1, 99);
        }
        return OptionHint.ChanceOf(chance, c.Leader);
    }

    /// <summary>Talks past the guards; returns true when the guards refuse and the option is closed.</summary>
    // orig: R044+0x1E2A (19DD:03DA) matched
    private bool TalkYourWayIn(CardContext c, int chance)
    {
        if (c.StandingSeen > -10 && !_seenBanned && !c.Hostile())
        {
            if (c.Random(100) <= chance)
            {
                c.Screen.Text(2);
                c.PracticeSkill(c.Leader, 0x0A, 1, 10, 0);
                c.AdjustReputation(c.Here, 1, 1);
                c.PassHours(2);
                c.Next = StreetForTheHour(c);
                return false;
            }
            c.EventHere(TurnedAway, 12);
            c.Screen.Text(3);
            c.PracticeSkill(c.Leader, 0x0A, 0, 10, 0);
            c.PassHours(1);
            c.Next = c.IsDaylight ? CardNumber : NightGate;
            return true;
        }
        c.Next = Outside;
        c.PassHours(0);
        return false;
    }

    /// <summary>The party's average Streetwise and burdened Agility; halved once the party is known to be banned.</summary>
    // orig: R044+0x202A (19DD:05DA) matched
    private OptionHint SneakChance(CardContext c)
    {
        // C: the burdened Agility is asked first: the Streetwise average is the best_value the hint leaves.
        var chance = (c.PartyAverageEffectiveAgility() + c.PartyAverageSkill(0x10)) / 2;
        if (_seenBanned)
        {
            chance /= 2;
        }
        return OptionHint.ChanceOf(Math.Clamp(chance, 1, 99), OptionHint.WholeParty);
    }

    // orig: R044+0x1F7E (19DD:052E) matched
    private void SneakIn(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            c.Screen.Text(4);
            c.PracticeSkill(Who.Everybody, 0x10, 1, 10, 0);
            c.PassHours(1);
            c.Next = StreetForTheHour(c);
            return;
        }
        c.EventHere(CaughtSneaking, 12);
        if (_seenBanned)
        {
            c.PracticeSkill(Who.Everybody, 0x10, 0, 10, 0);
            c.PassHours(1);
            c.Next = Outside;
            return;
        }
        c.Screen.Text(5);
        c.PracticeSkill(Who.Everybody, 0x10, 0, 10, 0);
        c.PassHours(0);
        c.Next = CityOutskirts;
    }

    /// <summary>Potion chances by pop-up slot: the first is the strongest; all suffer after a panic or a fight.</summary>
    // orig: R044+0x25F2 (19DD:0BA2) matched (slot 0: 2 x agility + 55)
    // orig: R044+0x265C (19DD:0C0C) matched (slot 1: agility + 50)
    // orig: R044+0x26C4 (19DD:0C74) matched (slot 2: agility + 40)
    private static OptionHint PotionChance(CardContext c, int slot)
    {
        var agility = c.PartyAverageEffectiveAgility();
        var chance = slot switch
        {
            0 => agility * 2 + 55,
            1 => agility + 50,
            2 => agility + 40,
            _ => -1,
        };
        if (slot > 2)
        {
            return OptionHint.Say(CardTexts.ScrewedUp);
        }
        if (c.EventAtHere(PotionPanic))
        {
            chance = chance * 2 / 3;
        }
        if (c.EventAtHere(GateStormed))
        {
            chance = chance * 2 / 3;
        }
        return OptionHint.ChanceOf(Math.Clamp(chance, 0, 100), c.MenuMember);
    }

    // orig: R044+0x206C (19DD:061C) matched
    private static void UseAPotion(CardContext c, int chance)
    {
        c.TakeItem(c.MenuMember, c.CardItems[c.MenuSlot]);
        if (c.Random(100) > chance)
        {
            c.Screen.Text(9);
            c.ExtendEventHere(PotionPanic, 30);
            c.ExtendEventHere(Banned, 240);
            c.PassHours(0);
            c.Next = CardNumber;
            return;
        }
        switch (c.MenuSlot)
        {
            case 0:
                ShowPicture(c, SmokePicture);
                c.Screen.Text(6);
                c.AdjustReputation(c.Here, -1, -4);
                break;
            case 1:
                ShowPicture(c, SmokePicture);
                c.Screen.Text(7);
                c.AdjustReputation(c.Here, 0, -3);
                break;
            case 2:
                ShowPicture(c, FlashPicture);
                c.Screen.Text(8);
                c.AdjustReputation(c.Here, 0, -2);
                break;
        }
        c.PassHours(0);
        c.Next = MainStreet;
    }

    /// <summary>The saint pop-up's hint: the member's chance with the saint (it also names the member in the texts).</summary>
    // orig: R044+0x2764 (19DD:0D14) matched (saint slot 0)
    // orig: R044+0x27EE (19DD:0D9E) matched (saint slot 1; byte-identical body to slot 0)
    private static OptionHint SaintChance(CardContext c)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        var saint = c.CardSaints[c.MenuSlot];
        var low = c.SaintChanceLow(c.MenuMember, saint);
        return OptionHint.Say(c.ChanceText(low, c.SaintChanceHigh(c.MenuMember, saint)));
    }

    // orig: R044+0x21E0 (19DD:0790) matched
    private static void CallUponASaint(CardContext c)
    {
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (answer == -1)
        {
            return;
        }
        if (answer != 0)
        {
            if (c.MenuSlot == 0)
            {
                c.Screen.Text(0x0A);
            }
            if (c.MenuSlot == 1)
            {
                c.Screen.Text(0x0B);
            }
            if (c.Reputation(c.Here) > -10)
            {
                c.AdjustReputation(c.Here, 4, 12);
            }
            else
            {
                c.AdjustReputation(c.Here, -3, -9);
            }
            c.PassHours(0);
            c.Next = MainStreet;
            return;
        }
        c.Screen.Text(0x0C);
        c.PassHours(0);
        c.Next = CardNumber;
    }

    /// <summary>
    /// Fights the gate guards: their number grows with the party's lore and a random draw
    /// (fixed while the gate is still guarded after the last fight). The standing falls by 40
    /// (when it is already −40 or worse the original rolls a change it then ignores).
    /// </summary>
    // orig: R044+0x22BC (19DD:086C) matched
    private static void StormTheGate(CardContext c)
    {
        var archers = c.Random(5);
        var guards = c.Random(3);
        var sergeants = c.Random(3);
        var seed = c.Here + 0x6F;
        var result = c.EventAtHere(GateStormed)
            ? c.RunBattle(0x53, seed, 3, 5, 7, 0, 5, 1, -1, 0, 0)
            : c.RunBattle(0x53, seed, 3, c.PartyLoreLevel() / 4 + guards + 1, archers + 4, 0, sergeants + 1, 1, -1, 0, 0);
        var size = c.HereQuery(2);
        var standing = c.HereQuery(0);
        if (standing <= -40)
        {
            c.ImproveRoll(standing, -3, -9);
        }
        else
        {
            standing -= 40;
        }
        c.LocationSet(c.Here, 0, standing);
        var guardedHours = 2000 / size;
        switch (result)
        {
            case 0:
                c.ExtendEventHere(GateStormed, guardedHours);
                c.ExtendEventHere(Fought, 1);
                c.Screen.Text(0x0D);
                c.PassHours(0);
                c.Next = SideStreet;
                break;
            case 1:
                c.ExtendEventHere(GateStormed, guardedHours);
                c.ReportAway();
                c.Screen.Text(0x0F);
                c.PassHours(1);
                c.Next = c.IsDaylight ? SideStreet : SideStreetAtNight;
                break;
            case 2:
                c.ReportAway();
                c.Screen.Text(0x10);
                c.PassHours(1);
                c.Next = CityOutskirts;
                break;
            case 3:
                c.Screen.Text(0x0E);
                c.PassHours(0);
                c.State.CurrentLocation = -1;
                c.Next = OverlandMap;
                break;
            case 4:
                c.Screen.Text(0x11);
                c.PassHours(3);
                c.Next = Dungeon;
                break;
        }
        c.EventHere(Banned, standing <= -75 ? 240 : 120);
        // After a lost fight the party is already outside (no location): the original then
        // writes the standing before the start of the location table; that write is left out.
        if (c.Here >= 0)
        {
            c.LocationSet(c.Here, 0, standing);
        }
    }
}
