using Darklands.Core.Characters;
using Darklands.Core.Events;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$jewel01": the jewelers' guild at night. A card the game left half finished: it is cut from the
/// other guilds' night cards, but has no secrets to watch for, no uprising and no shop, and several of
/// its texts do not fit what the option does. Knock to be let in, knock for the guild master, wait
/// outside, or try to get into the guildhouse.
/// </summary>
// orig: R067+0x11C4 (1954:0004) matched; tests: CardSmokeTests
// orig: handler setup R067+0x1B7C (1954:09BC) is empty: the item/saint handlers and card-option-enabled[10..19]
// of the previous card stay. The card lists the same items and saints as the unfinished shop cards but
// has no pop-up line, so nothing reads them.
public sealed class JewelersGuildAtNight : Card
{
    private const int PoliticsQuest = 2;
    private const int FactKind = 0x1C;
    private const int Picture = 0;
    private const int Guild = 0x80;
    private const int ThisCard = 0x5E;
    private const int GuildByDay = 0x5D;
    private const int CityEntry = 4;

    public override int Number => ThisCard;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Enable(1, 2, 3, 4, 5, 6, 9);
        menu[0] = OptionState.Shown;
        // C: `(char)fact & -0x80` is the sign test of the byte: bit 7 of the fact.
        if ((Fact(c, 3) & -0x80) != 0)
        {
            menu[0] = menu[1] = OptionState.Shown;
        }
        // C: written `open-quest-at(2, location-now) != 1`; the function answers 0 or 1.
        if (!c.Events.QuestAt(PoliticsQuest, c.Here))
        {
            menu[1] = menu[2] = menu[3] = menu[4] = menu[5] = menu[6] = OptionState.Hidden;
        }
        else if ((Fact(c, 0xB) & -0x80) != 0)
        {
            menu[6] = OptionState.Shown;
        }
        // C: place-standing(location-now), the same value as place-query(location-now, 0).
        var standing = c.Reputation(c.Here);
        c.SetCardItems(0x5F, 0x60, 0x65, 0x61);
        c.SetCardSaints(0, 0x59, 0x61, 0x80);

        return menu.Run(
            new MenuLine(chance => KnockToBuy(c, ref standing, chance), () => KnockChance(c, standing)),
            new MenuLine(chance => KnockForTheMaster(c, chance), () => KnockChance(c, standing)),
            new MenuLine(_ => WatchTheHouse(c), OptionHint.Saying(CardTexts.Unknown)),
            new MenuLine(_ => WatchTheHouse(c), OptionHint.Saying(CardTexts.Unknown)),
            new MenuLine(chance => PickTheLock(c, chance), () => BreakInChance(c)),
            new MenuLine(chance => BreakInAndLeave(c, chance), () => BreakInChance(c)),
            new MenuLine(chance => BreakInAndNoteIt(c, chance), () => BreakInChance(c)),
            // DORMANT: lines 7 and 8 are never offered (card-option-flag-7 = card-option-flag-8 = 0); their actions are the same as line 9's.
            // orig: R067+0x1A96 (1954:08D6) hint (100 %, the leader) with action 08b8 R067+0x1A78;
            // hint R067+0x1AEE "Success is assured." with action 0910 R067+0x1AD0.
            new MenuLine(_ => LeaveForTheCity(c), () => new OptionHint(100, SuccessAssured, c.Leader)),
            new MenuLine(_ => LeaveForTheCity(c), OptionHint.Saying(SuccessAssured)),
            // orig: R067+0x1B26 (1954:0966) (hint R067+0x1B44 "Success is assured.")
            new MenuLine(_ => LeaveForTheCity(c), OptionHint.Saying(SuccessAssured)));
    }

    private static int Fact(CardContext c, int type) => (sbyte)c.Events.FactValue(PoliticsQuest, type, c.Here);

    // orig: R067+0x14F0 (1954:0330), and R067+0x158A (1954:03CA) (identical)
    private static OptionHint KnockChance(CardContext c, int standing)
    {
        var chance = c.Attribute(c.Leader, (int)Attribute.Charisma) + c.Skill(c.Leader, (int)Skill.SpeakCommon) / 2 + standing;
        // C: larger-of(chance, 90) where the other guilds clamp to 0..90, so the chance is always 90..99.
        chance = Math.Max(chance, 0x5A);
        return new OptionHint(Math.Clamp(chance, 0, 99), "", c.Leader);
    }

    /// <summary>Wake the guild to be let in: no shop opens here. Either way the city likes it less.</summary>
    // orig: R067+0x1474 (1954:02B4)
    private static void KnockToBuy(CardContext c, ref int standing, int chance)
    {
        if (c.Succeeds(chance))
        {
            c.Screen.Text(1);
            c.PassHours(1);
            c.Next = c.IsDaylight ? GuildByDay : ThisCard;
        }
        else
        {
            c.PassHours(1);
            c.Screen.Text(2, TextFrame.BlankPage);
        }
        c.Events.Update(-2, c.Here, FactKind, 3, Guild, PoliticsQuest, EventUpdate.Or);
        // C: reputation-now, the card's copy of the reputation, is lowered and written back.
        standing--;
        c.LocationSet(c.Here, 0, standing);
    }

    /// <summary>The master may receive the party (no uprising is looked at here and the fact is not noted).</summary>
    // orig: R067+0x1548 (1954:0388)
    private static void KnockForTheMaster(CardContext c, int chance)
    {
        if (c.Succeeds(chance))
        {
            c.Screen.Text(1);
            c.PassHours(1);
            c.Next = 0x6A;
            return;
        }
        c.PassHours(1);
        c.Screen.Text(2, TextFrame.BlankPage);
    }

    /// <summary>Wait outside until dawn; one night in twelve ends in an encounter. Lines 2 and 3 do the same.</summary>
    // orig: R067+0x15E2 (1954:0422) and R067+0x169E (1954:04DE) (identical bodies;
    // hints R067+0x1666 and R067+0x1722 "You can always encounter the unknown")
    private static void WatchTheHouse(CardContext c)
    {
        var untilDawn = Core.Time.TimeOfDay.HoursUntil(c.State.Date.Hour, 5);
        if (c.Random(100) < 8)
        {
            // C: larger-of(2, roll-below(untilDawn - 1)) is computed and dropped; the draw is made.
            c.Random(untilDawn - 1);
            c.Variables.Numbers[0] = 1;
            c.Screen.Text(3);
            c.PassHours(c.Variables.Numbers[0]);
            c.GuildCode = Guild;
            c.SavedCard = GuildByDay;
            c.State.SavedCard2 = ThisCard;
            c.Next = 0x6B;
            return;
        }
        c.Screen.Text(5);
        c.PassHours(untilDawn);
    }

    // orig: R067+0x1838 (1954:0678), R067+0x1932 and R067+0x1A48 (identical)
    private static OptionHint BreakInChance(CardContext c)
    {
        var stealth = c.PartyAverageSkill((int)Skill.Stealth);
        // C: unlike the other guilds there is no quarter-skill rule without lockpicks.
        return new OptionHint(Math.Min(stealth, c.PartyMaxSkill((int)Skill.Artifice)), "", OptionHint.WholeParty);
    }

    /// <summary>Pick the lock: in (no text; on to card 0x6C), or caught.</summary>
    // orig: R067+0x175A (1954:059A)
    private static void PickTheLock(CardContext c, int chance)
    {
        var roll = c.Random(100);
        c.Events.Update(-2, c.Here, FactKind, 5, Guild, PoliticsQuest, EventUpdate.Or);
        if (roll <= chance)
        {
            c.PracticeSkill(Who.Everybody, (int)Skill.Stealth, 1, 10, 0);
            c.PassHours(1);
            c.SavedCard = c.IsDaylight ? GuildByDay : ThisCard;
            c.Next = 0x6C;
            return;
        }
        FailedToGetIn(c);
        c.PassHours(2);
        c.Next = 0x14;
    }

    /// <summary>On success only skills are practised (no text); two hours pass and the party is back in the city.</summary>
    // orig: R067+0x1868 (1954:06A8)
    private static void BreakInAndLeave(CardContext c, int chance)
    {
        c.Events.Update(-2, c.Here, FactKind, 5, Guild, PoliticsQuest, EventUpdate.Or);
        if (c.Succeeds(chance))
        {
            c.PracticeSkill(Who.Everybody, (int)Skill.Stealth, 1, 10, 0);
            c.PartyMaxSkill((int)Skill.Artifice);
            c.Variables.Chosen[0] = c.BestMember;
        }
        else
        {
            FailedToGetIn(c);
        }
        c.PassHours(2);
        c.Next = 0x14;
    }

    /// <summary>As the one before, but a success also notes the fact (type 0xB) and takes four hours.</summary>
    // orig: R067+0x1962 (1954:07A2)
    private static void BreakInAndNoteIt(CardContext c, int chance)
    {
        c.Events.Update(-2, c.Here, FactKind, 5, Guild, PoliticsQuest, EventUpdate.Or);
        if (c.Succeeds(chance))
        {
            c.Events.Update(-2, c.Here, FactKind, 0xB, Guild, PoliticsQuest, EventUpdate.Or);
            c.PracticeSkill(Who.Everybody, (int)Skill.Stealth, 1, 10, 0);
            c.PartyMaxSkill((int)Skill.Artifice);
            c.Variables.Chosen[0] = c.BestMember;
            c.PassHours(4);
        }
        else
        {
            FailedToGetIn(c);
            c.PassHours(2);
        }
        c.Next = 0x14;
    }

    /// <summary>The lock defeats the best picker, or the clumsiest sneak gives the party away (the same tail in lines 4..6).</summary>
    private static void FailedToGetIn(CardContext c)
    {
        var stealth = c.PartyAverageSkill((int)Skill.Stealth);
        if (c.PartyMaxSkill((int)Skill.Artifice) < stealth)
        {
            c.Variables.Chosen[0] = c.BestMember;
            c.PracticeSkill(c.BestMember, (int)Skill.Artifice, 0, 10, 0);
            c.Screen.Text(7);
        }
        else
        {
            // C: text-slot-chosen-one takes the value party-ability-low returns, the lowest skill itself (the
            // other guilds' cards use top-scoring-member): that number is then passed as the member.
            // orig: R067+0x1A0A party-ability-low(0xF) (R029+0x1396), DS:EE8C = AX, exercise-skill(AX, 0xF, 0, 10, 0) through thunk 09C0:1F63 (R039+0x0132)
            // BUG: the lowest Stealth (1..99) is the member number: exercise-skill always draws its chance, then member-ability and raise-skill-by
            // address record DS:9C69 + n*0x80. Below 5 that is a party slot (a member, perhaps not the clumsy one, practises); 20 is the place
            // actions of the last battle (RecordOverrun: its skill byte DS:A6CF is Place[11] of the .BSV's variables, written when slot 0 has a
            // figure image, the message drawn below the screen and waited for); the others are program memory (RecordOverrun's DIVERGENCE).
            // Not data-damaging: Place[11] is a finished battle's byte that a new battle sets to 0 for all 13 combatants (R002+0x19FA) before
            // reading it, and the .SAV never changes (direct calls j_prac.py / j_call.py / j_bsv.py, local/batch/evid1o), so it is faithful
            // in both behaviours.
            var lowest = c.PartyMinSkill((int)Skill.Stealth);
            c.Variables.Chosen[0] = lowest;
            c.PracticeSkill(lowest, (int)Skill.Stealth, 0, 10, 0);
            c.Screen.Text(6, TextFrame.BlankPage);
        }
    }

    /// <summary>The last line: the first text once more, no time passes, and out to the city entry card.</summary>
    // orig: R067+0x1A78, 0910 R067+0x1AD0 and 0966 R067+0x1B26 (identical)
    private static void LeaveForTheCity(CardContext c)
    {
        c.Screen.Text(0);
        c.PassHours(0);
        c.Next = CityEntry;
    }
}
