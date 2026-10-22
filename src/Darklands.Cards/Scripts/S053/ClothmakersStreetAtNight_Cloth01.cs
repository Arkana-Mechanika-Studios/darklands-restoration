using Darklands.Core.Characters;
using Darklands.Core.Events;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$cloth01": Clothmakers' Street by night. The party may wake somebody to trade, wake the guild
/// leader for a private talk, hide and watch his home (to learn his secret), pick a lock and
/// sneak into his home, or leave. Which of these are open depends on the city's politics.
/// </summary>
// orig: R053+0x2170 (1A4F:0000) matched; tests: CardSmokeTests
public sealed class ClothmakersStreetAtNight_Cloth01 : Card
{
    public const int CardNumber = 0x26;

    private const int Picture = 0;

    private const int Clothmakers = 0x25, Back = 0x1A, GuildMeeting = 0x6A, GuildAudience = 0x74, GuildHostile = 0x79,
        SneakIntoLeadersHome = 0x5F, SneakIntoLeadersHomeBy = 0x6C, Market = 0x14, SideStreet = 0x09, SideStreetAtNight = 0x0A;

    /// <summary>Politics of a city: the quest events of tag 2 and the facts (kind 0x1C) of group 2 at the location.</summary>
    private const int Politics = 2;

    private const int GuildBit = 0x40;

    // fact numbers guessed from use, as in the day card: 0 the guild's leaning, 3 asked / woke the leader,
    // 5 tried to sneak in, 9 holds proof of the leader's secret, 0x17 a special state of the guild (0x40).
    private const int Leaning = 0, WokeTheLeader = 3, TriedToSneak = 5, HasProof = 9, Special = 0x17;

    public override int Number => CardNumber;

    // orig: R053+0x2170 (1A4F:0000) matched (entry: option states from the city's politics, items and saints, the menu loop)
    // orig: R053+0x2B92 (1A4F:0A22) matched (empty init helper: the entry does not clear option lines or the item and saint slots)
    // DORMANT: R053+0x2B94 (1A4F:0A24) (the "screwed up." default hint), R053+0x2BCC (1A4F:0A5C), R053+0x2C08 (1A4F:0A98), R053+0x2C44 (1A4F:0AD4), R053+0x2C80 (1A4F:0B10) (item pop-up handlers),
    // R053+0x2CBC (1A4F:0B4C), R053+0x2D46 (1A4F:0BD6), R053+0x2DD0 (1A4F:0C60), R053+0x2E5A (1A4F:0CEA) (saint pop-up handlers): the init helper is empty, so nothing installs them,
    // and no option opens a pop-up (the card still fills card_items and card_saints, but items_usable/saints_usable are never asked).
    // C: the entry does not install item_fn/saint_fn, so the pop-up handlers would be stale ones of the previous card.
    public override int Run(CardContext c)
    {
        Show(c, Picture);
        var shop = OptionState.Enabled;
        var leader = OptionState.Enabled;
        var watch = OptionState.Enabled;
        var lockpick = OptionState.Enabled;
        if (Flag(c, WokeTheLeader))
        {
            shop = leader = OptionState.Shown;
        }
        if (Flag(c, TriedToSneak))
        {
            lockpick = OptionState.Shown;
        }
        if (!c.Events.QuestAt(Politics, c.Here))
        {
            leader = watch = lockpick = OptionState.Hidden;
        }
        else if (c.Events.FactValue(Politics, Special, c.Here) == GuildBit && c.Events.FactValue(Politics, Leaning, c.Here) != 1)
        {
            watch = lockpick = OptionState.Hidden;
        }
        c.StandingSeen = c.Reputation(c.Here);
        c.SetCardItems(0x5F, 0x60, 0x65, 0x61);
        c.SetCardSaints(0, 0x59, 0x61, 0x80);

        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2, 3, 9);
        menu[0] = shop;
        menu[1] = leader;
        menu[2] = watch;
        menu[3] = lockpick;
        c.Next = Number;
        return menu.Run(
            // orig: R053+0x243E (1A4F:02CE) matched (option 0: wake somebody to trade, see WakeSomebody); hint R053+0x24CA (1A4F:035A) (see PersuasionChance)
            new MenuLine(chance => WakeSomebody(c, chance), () => PersuasionChance(c)),
            // orig: R053+0x2518 (1A4F:03A8) matched (option 1: wake the guild leader, see WakeTheLeader); hint R053+0x25E4 (1A4F:0474) (see LeaderChance)
            new MenuLine(chance => WakeTheLeader(c, chance), () => LeaderChance(c)),
            // orig: R053+0x26A0 (1A4F:0530) matched (option 2: hide and watch his home, see WatchTheHome); hint R053+0x2960 (1A4F:07F0) ("You can always encounter the unknown")
            new MenuLine(() => WatchTheHome(c), OptionHint.Saying(CardTexts.Unknown)),
            // orig: R053+0x2998 (1A4F:0828) matched (option 3: pick a lock and sneak in, see PickTheLock); hint R053+0x2ABA (1A4F:094A) (see LockChance)
            new MenuLine(chance => PickTheLock(c, menu, chance), () => LockChance(c)),
            // DORMANT: option 4 is switched off by the entry (the MSG has a line "guard the guild block from saboteurs" for it, and nothing else):
            // R053+0x2B08 (1A4F:0998) does nothing; hint R053+0x2B0A (1A4F:099A) (success assured).
            new MenuLine(() => { }),
            // DORMANT: options 5..8 are switched off by the entry (placeholder lines "5".."8" in the MSG); their actions
            // R053+0x2B42 (1A4F:09D2), R053+0x2B46 (1A4F:09D6), R053+0x2B4A (1A4F:09DA), R053+0x2B4E (1A4F:09DE) do nothing and their hints
            // R053+0x2B44 (1A4F:09D4), R053+0x2B48 (1A4F:09D8), R053+0x2B4C (1A4F:09DC), R053+0x2B50 (1A4F:09E0) return nothing (void functions).
            new MenuLine(() => { }),
            new MenuLine(() => { }),
            new MenuLine(() => { }),
            new MenuLine(() => { }),
            // orig: R053+0x2B52 (1A4F:09E2) matched (option 9: leave, no time passes); hint R053+0x2B5A (1A4F:09EA) (success assured)
            new MenuLine(() => c.Next = Back));
    }

    private static bool Flag(CardContext c, int fact) => (c.Events.FactValue(Politics, fact, c.Here) & GuildBit) != 0;

    private static void MarkFact(CardContext c, int fact, EventUpdate op, int value) =>
        c.Events.Update(-2, c.Here, EventKinds.LocationFact, fact, value, Politics, op);

    private static void SetMapX(CardContext c, int x) => c.State.MapPosition = c.State.MapPosition with { X = x };

    private static int ThisStreet(CardContext c) => c.IsDaylight ? Clothmakers : CardNumber;

    private static int SecretKind(CardContext c) => (c.HereQuery(0x21) + 0x40) % 7;

    /// <summary>The leader's Charisma plus half Speak Common plus the standing, at most 90.</summary>
    // orig: R053+0x24CA (1A4F:035A) matched
    private static OptionHint PersuasionChance(CardContext c) => OptionHint.ChanceOf(Persuasion(c), c.Leader);

    private static int Persuasion(CardContext c)
    {
        var chance = c.Attribute(c.Leader, 5) + c.Skill(c.Leader, 0x0A) / 2;
        chance += c.StandingSeen;
        return Math.Clamp(chance, 0, 0x5A);
    }

    /// <summary>Wakes somebody to trade; the standing falls either way.</summary>
    // orig: R053+0x243E (1A4F:02CE) matched
    private static void WakeSomebody(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            c.Screen.Text(1);
            c.Barter(8, 0x0400L << 16, null); // C: shop 8, no name, categories low word 0, high word 0x400
            c.PassHours(1);
            c.Next = ThisStreet(c);
        }
        else
        {
            c.PassHours(1);
            c.Screen.Text(2, TextFrame.BlankPage);
        }
        MarkFact(c, WokeTheLeader, EventUpdate.Or, GuildBit);
        c.StandingSeen--;
        c.LocationSet(c.Here, 0, c.StandingSeen);
    }

    /// <summary>Sure once the guild is hostile with the special state; else as persuasion, but nothing once the leader was woken.</summary>
    // orig: R053+0x25E4 (1A4F:0474) matched
    private static OptionHint LeaderChance(CardContext c)
    {
        if (c.Events.FactValue(Politics, Special, c.Here) == GuildBit && c.Events.FactValue(Politics, Leaning, c.Here) != 1)
        {
            return OptionHint.Say(SuccessAssured);
        }
        var chance = Persuasion(c);
        if (Flag(c, WokeTheLeader))
        {
            chance = 0;
        }
        return OptionHint.ChanceOf(chance, c.Leader);
    }

    /// <summary>Wakes the guild leader for a private talk: hostile guilds and open ones lead to other cards, else a roll decides.</summary>
    // orig: R053+0x2518 (1A4F:03A8) matched
    private static void WakeTheLeader(CardContext c, int chance)
    {
        if (c.Events.FactValue(Politics, Leaning, c.Here) == -1)
        {
            SetMapX(c, 0);
            c.State.SavedCard = Back;
            c.Next = GuildHostile;
            return;
        }
        if (c.Events.FactValue(Politics, Leaning, c.Here) == 0)
        {
            MarkFact(c, WokeTheLeader, EventUpdate.Or, GuildBit);
            SetMapX(c, 0);
            c.State.SavedCard = Back;
            c.Next = GuildAudience;
            return;
        }
        MarkFact(c, WokeTheLeader, EventUpdate.Or, GuildBit);
        c.AdjustReputation(c.Here, -1, -3);
        if (c.Random(100) <= chance)
        {
            c.Screen.Text(1);
            c.PassHours(1);
            c.Next = GuildMeeting;
            return;
        }
        c.PassHours(1);
        c.Screen.Text(2, TextFrame.BlankPage);
    }

    /// <summary>
    /// Hides near the leader's home and watches: with luck the party sees the leader's secret
    /// (which one depends on the city; the proof is recorded), else it is chased off.
    /// </summary>
    // orig: R053+0x26A0 (1A4F:0530) matched
    private static void WatchTheHome(CardContext c)
    {
        if (c.PartyAverageSkill(0x10) + c.PartyAverageSkill(0x0F) >= c.Random(100))
        {
            var kind = SecretKind(c);
            // C: the random draw is made only for the kind in question (short-circuit); each kind has its own odds.
            if (kind == 0 && c.Random(1000) % 2 != 0)
            {
                c.AdjustReputation(c.Here, 4, 4);
                c.AddItem(Who.Leader, 0x78);
                // C: the event update is an AND with 0x40 (not with ~0x40): it keeps only the guild bit of the fact, as in every case below.
                MarkFact(c, HasProof, EventUpdate.And, GuildBit);
                c.Screen.Text(8);
                c.Next = Market;
                return;
            }
            if (kind == 1 && c.Random(1000) % 5 == 0)
            {
                c.AdjustReputation(c.Here, 8, 8);
                c.AddItem(Who.Leader, 0x79);
                MarkFact(c, HasProof, EventUpdate.And, GuildBit);
                c.Screen.Text(9);
                c.Next = Market;
                return;
            }
            if (kind == 2 && c.Random(1000) % 10 == 0)
            {
                c.AdjustReputation(c.Here, 1, 2);
                MarkFact(c, HasProof, EventUpdate.And, GuildBit);
                c.Screen.Text(0x0A);
                c.Screen.Text(0x0B, TextFrame.BlankPage);
                c.Next = Market;
                return;
            }
            if (kind == 3 && c.Random(1000) % 4 == 0)
            {
                c.AdjustReputation(c.Here, 4, 4);
                c.AddItem(Who.Leader, 0x7C);
                MarkFact(c, HasProof, EventUpdate.And, GuildBit);
                c.Screen.Text(0x0A);
                c.Screen.Text(0x0C, TextFrame.BlankPage);
                c.Next = Market;
                return;
            }
            if (kind == 4 && c.Random(1000) % 5 == 0)
            {
                c.AdjustReputation(c.Here, 8, 8);
                MarkFact(c, HasProof, EventUpdate.And, GuildBit);
                c.Screen.Text(0x0D);
                c.Next = c.State.PreviousCard;
                return;
            }
            if (kind == 5 && c.Random(1000) % 5 == 0)
            {
                c.AdjustReputation(c.Here, 4, 4);
                c.AddItem(Who.Leader, 0x7F);
                MarkFact(c, HasProof, EventUpdate.And, GuildBit);
                c.Screen.Text(0x0E);
                c.Next = Market;
                return;
            }
            c.Screen.Text(3, TextFrame.BlankPage);
            c.Next = c.State.PreviousCard;
            return;
        }
        var hours = c.Random(4) + 1;
        hours = Math.Clamp(Math.Min(hours, Core.Time.TimeOfDay.HoursUntil(c.State.Date.Hour, 5)), 2, 12);
        c.Variables.Numbers[0] = hours;
        c.PassHours(hours);
        c.Screen.Text(7, TextFrame.BlankPage);
        c.Next = c.IsDaylight ? SideStreet : SideStreetAtNight;
    }

    /// <summary>The weaker of the party's average Stealth and best Artifice (a quarter of it without lock picks, item 0x40).</summary>
    // orig: R053+0x2ABA (1A4F:094A) matched
    private static OptionHint LockChance(CardContext c)
    {
        var stealth = c.PartyAverageSkill(0x0F);
        var artifice = c.PartyMaxSkill(0x0E);
        if (c.CountItems(Who.Everybody, 0x40) == 0)
        {
            artifice /= 4;
        }
        return OptionHint.ChanceOf(Math.Min(stealth, artifice), OptionHint.WholeParty);
    }

    /// <summary>Picks a lock and sneaks into the leader's home; a failure teaches Artifice or Stealth to the best member.</summary>
    // orig: R053+0x2998 (1A4F:0828) matched
    private static void PickTheLock(CardContext c, CardMenu menu, int chance)
    {
        var roll = c.Random(100);
        MarkFact(c, TriedToSneak, EventUpdate.Or, GuildBit);
        menu[3] = OptionState.Shown;
        if (roll <= chance)
        {
            c.PracticeSkill(Who.Everybody, 0x0F, 7, 10, 0);
            c.PassHours(1);
            c.State.SavedCard = c.IsDaylight ? Clothmakers : CardNumber;
            c.Screen.Text(6, TextFrame.BlankPage);
            if (c.Events.FactValue(Politics, Special, c.Here) == GuildBit)
            {
                c.State.SavedCard = Back;
                SetMapX(c, GuildBit);
                c.Next = SneakIntoLeadersHome;
                return;
            }
            c.State.SavedCard = Back;
            c.Next = SneakIntoLeadersHomeBy;
            return;
        }
        var average = c.PartyAverageSkill(0x0F);
        if (c.PartyMaxSkill(0x0E) < average)
        {
            // C: (unsigned char)best_member, from the party_max_skill call above.
            c.Variables.Chosen[0] = (byte)c.BestMember;
            c.PracticeSkill(c.Variables.Chosen[0], 0x0E, 0, 10, 0);
            c.Screen.Text(5);
        }
        else
        {
            _ = c.PartyMinSkill(0x0F); // C: only for best_member
            c.Variables.Chosen[0] = (byte)c.BestMember;
            c.PracticeSkill(c.Variables.Chosen[0], 0x0F, 0, 10, 0);
            c.Screen.Text(4, TextFrame.BlankPage);
        }
        c.PassHours(2);
        c.Next = Market;
    }
}
