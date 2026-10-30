using Darklands.Core.Characters;
using Darklands.Core.Events;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Armor01": the armorers' guild at night. Knock to buy something anyway, knock to see the
/// guild master (the politics quest), keep watch on the guildhouse for the leader's secret, or
/// break in.
/// </summary>
// orig: R064+0x1FF4 (1A37:0004) matched; tests: CardSmokeTests
// orig: handler setup R064+0x29E6 (1A37:09F6) is empty: the item/saint handlers and card-option-enabled[10..19]
// of the previous card stay (no pop-up line here, so nothing reads them).
public sealed class ArmorersGuildAtNight : Card
{
    private const int Guild = 0x10;
    private const int PoliticsQuest = 2;
    private const int FactKind = 0x1C;
    private const int Picture = 347;
    private const int CityStreets = 0x17;
    private const int GuildByDay = 0x51;
    private const int ThisCard = 0x52;

    public override int Number => ThisCard;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2, 3, 9);
        if ((Fact(c, 3) & Guild) != 0)
        {
            menu[0] = menu[1] = OptionState.Shown;
        }
        if ((Fact(c, 5) & Guild) != 0)
        {
            menu[3] = OptionState.Shown;
        }
        if (!c.Events.QuestAt(PoliticsQuest, c.Here))
        {
            menu[1] = menu[2] = menu[3] = OptionState.Hidden;
        }
        else if (c.Events.FactValue(PoliticsQuest, 0x17, c.Here) == Guild && c.Events.FactValue(PoliticsQuest, 0, c.Here) != 1)
        {
            menu[2] = menu[3] = OptionState.Hidden;
        }
        var standing = c.HereQuery(0);

        return menu.Run(
            new MenuLine(chance => KnockToBuy(c, ref standing, chance), () => KnockChance(c, standing)),
            new MenuLine(chance => KnockForTheMaster(c, chance), () => MasterChance(c, standing)),
            new MenuLine(_ => WatchTheGuildhouse(c), OptionHint.Saying(CardTexts.Unknown)),
            new MenuLine(chance => BreakIn(c, menu, chance), () => BreakInChance(c)),
            // DORMANT: lines 4..8 are never offered (card-option-flag-4..8 = 0); their actions do nothing.
            // orig: R064+0x295C (1A37:096C) (hint R064+0x295E (1A37:096E) "Success is assured."), R064+0x2996 (1A37:09A6) and the
            // empty functions up to R064+0x29A4 (1A37:09B4).
            // C: lines 5..8 use empty void functions as their hint handlers: their struct res would be
            // whatever was left in DX:AX. Here they say "screwed up.".
            new MenuLine(_ => { }, OptionHint.Saying(SuccessAssured)),
            new MenuLine(_ => { }, OptionHint.Saying(CardTexts.ScrewedUp)),
            new MenuLine(_ => { }, OptionHint.Saying(CardTexts.ScrewedUp)),
            new MenuLine(_ => { }, OptionHint.Saying(CardTexts.ScrewedUp)),
            new MenuLine(_ => { }, OptionHint.Saying(CardTexts.ScrewedUp)),
            // orig: R064+0x29A6 (1A37:09B6) (hint R064+0x29AE (1A37:09BE)): back to the streets.
            new MenuLine(_ => c.Next = CityStreets, OptionHint.Saying(SuccessAssured)));
    }

    private static int Fact(CardContext c, int type) => (sbyte)c.Events.FactValue(PoliticsQuest, type, c.Here);

    private static int Persuasion(CardContext c, int standing) =>
        Math.Clamp(c.Attribute(c.Leader, (int)Attribute.Charisma) + c.Skill(c.Leader, (int)Skill.SpeakCommon) / 2 + standing, 0, 90);

    // orig: R064+0x2324 (1A37:0334)
    private static OptionHint KnockChance(CardContext c, int standing) => new(Persuasion(c, standing), "", c.Leader);

    /// <summary>Wake the guild to buy something: they may open the shop; either way the city likes it less.</summary>
    // orig: R064+0x2296 (1A37:02A6)
    private static void KnockToBuy(CardContext c, ref int standing, int chance)
    {
        if (c.Succeeds(chance))
        {
            c.Screen.Text(1);
            c.OpenShop(3, null, 0xFF, 0x400);
            ShowPicture(c, Picture);
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

    // orig: R064+0x2438 (1A37:0448)
    private static OptionHint MasterChance(CardContext c, int standing)
    {
        if (c.Events.FactValue(PoliticsQuest, 0x17, c.Here) == Guild && c.Events.FactValue(PoliticsQuest, 0, c.Here) != 1)
        {
            return OptionHint.Say(SuccessAssured);
        }
        var chance = Persuasion(c, standing);
        if ((Fact(c, 3) & Guild) != 0)
        {
            chance = 0;
        }
        return new OptionHint(chance, "", c.Leader);
    }

    /// <summary>During an uprising the rebels or loyalists take the party in; otherwise the master may receive it.</summary>
    // orig: R064+0x2372 (1A37:0382)
    private static void KnockForTheMaster(CardContext c, int chance)
    {
        var side = c.Events.FactValue(PoliticsQuest, 0, c.Here);
        if (side == -1)
        {
            // C: party-map-column is not set on this path (the other guilds' cards clear it first).
            c.SavedCard = CityStreets;
            c.Next = 0x79;
            return;
        }
        if (side == 0)
        {
            c.Events.Update(-2, c.Here, FactKind, 3, Guild, PoliticsQuest, EventUpdate.Or);
            c.GuildCode = 0;
            c.SavedCard = CityStreets;
            c.Next = 0x74;
            return;
        }
        c.Events.Update(-2, c.Here, FactKind, 3, Guild, PoliticsQuest, EventUpdate.Or);
        c.AdjustReputation(c.Here, -1, -3);
        if (c.Succeeds(chance))
        {
            c.Screen.Text(1);
            c.PassHours(1);
            // C: party-map-column is not set here: the guild hall card gets whatever guild was handed on last.
            c.Next = 0x6A;
            return;
        }
        c.PassHours(1);
        c.Screen.Text(2, TextFrame.BlankPage);
    }

    /// <summary>
    /// A night's watch on the guildhouse. If the party is not caught, it may witness the leader's
    /// secret (which secret is the city's; how likely depends on it).
    /// </summary>
    // orig: R064+0x24F4 (1A37:0504) (hint R064+0x27B4 (1A37:07C4) "You can always encounter the unknown")
    private static void WatchTheGuildhouse(CardContext c)
    {
        // C: the option's chance (−1) is ignored; the roll is against the party's average Streetwise + Stealth.
        if (c.Random(100) <= c.PartyAverageSkill((int)Skill.Streetwise) + c.PartyAverageSkill((int)Skill.Stealth))
        {
            // C: % keeps the sign of a negative value; such a remainder matches no secret.
            var secret = (c.LocationQuery(c.Here, 0x21) + 0x10) % 7;
            // C: && stops early: the second roll (roll-below(1000)) is made only for the matching secret.
            if (secret == 0 && c.Random(1000) % 2 != 0)
            {
                Witnessed(c, 4, 0x78, 8);
                // C: card-text-block(8, local-in-si) passes the secret (0) as the frame.
            }
            else if (secret == 1 && c.Random(1000) % 5 == 0)
            {
                Witnessed(c, 8, 0x79, 9);
            }
            else if (secret == 2 && c.Random(1000) % 10 == 0)
            {
                // C: the reputation change is written with the constants 1, 2; the fact is kept with operation 2 (and) like the others.
                c.AdjustReputation(c.Here, 1, 2);
                NoteWitnessed(c);
                c.Screen.Text(0x0A);
                c.Screen.Text(0x0B, TextFrame.BlankPage);
                c.Next = 0x14;
            }
            else if (secret == 3 && c.Random(1000) % 4 == 0)
            {
                c.AdjustReputation(c.Here, 4, 4);
                c.AddItem(Who.Leader, 0x7C);
                NoteWitnessed(c);
                c.Screen.Text(0x0A);
                c.Screen.Text(0x0C, TextFrame.BlankPage);
                c.Next = 0x14;
            }
            else if (secret == 4 && c.Random(1000) % 5 == 0)
            {
                c.AdjustReputation(c.Here, 8, 8);
                NoteWitnessed(c);
                c.Screen.Text(0x0D);
                c.Next = c.PreviousCard;
            }
            else if (secret == 5 && c.Random(1000) % 5 == 0)
            {
                Witnessed(c, 4, 0x7F, 0x0E);
            }
            else
            {
                c.Screen.Text(3, TextFrame.BlankPage);
                c.Next = c.PreviousCard;
            }
            return;
        }
        // Caught by the watch after a few hours (but not past dawn).
        var hours = c.Random(4) + 1;
        hours = Math.Clamp(Math.Min(hours, Core.Time.TimeOfDay.HoursUntil(c.State.Date.Hour, 5)), 2, 12);
        c.Variables.Numbers[0] = hours;
        c.PassHours(hours);
        c.Screen.Text(7, TextFrame.BlankPage);
        c.Next = c.IsDaylight ? 9 : 10;
    }

    /// <summary>The secret seen: reputation, a piece of evidence for the leader, the fact noted, its text, off to the city centre.</summary>
    private static void Witnessed(CardContext c, int reputation, int evidence, int text)
    {
        c.AdjustReputation(c.Here, reputation, reputation);
        c.AddItem(Who.Leader, evidence);
        NoteWitnessed(c);
        c.Screen.Text(text);
        c.Next = 0x14;
    }

    // C: operation 2 is "and", so the value 2 keeps only that bit of the fact (Black00 uses "or").
    private static void NoteWitnessed(CardContext c) =>
        c.Events.Update(-2, c.Here, FactKind, 9, Guild, PoliticsQuest, EventUpdate.And);

    // orig: R064+0x290E (1A37:091E)
    private static OptionHint BreakInChance(CardContext c)
    {
        var stealth = c.PartyAverageSkill((int)Skill.Stealth);
        var artifice = c.PartyMaxSkill((int)Skill.Artifice);
        if (c.CountItems(Who.Everybody, 0x40) == 0)
        {
            // Without lockpicks a quarter of the skill.
            artifice /= 4;
        }
        return new OptionHint(Math.Min(stealth, artifice), "", OptionHint.WholeParty);
    }

    /// <summary>Climb in by night: into the rebels' meeting during an uprising, else the guild hall.</summary>
    // orig: R064+0x27EC (1A37:07FC)
    private static void BreakIn(CardContext c, CardMenu menu, int chance)
    {
        var roll = c.Random(100);
        c.Events.Update(-2, c.Here, FactKind, 5, Guild, PoliticsQuest, EventUpdate.Or);
        menu[3] = OptionState.Shown;
        if (roll <= chance)
        {
            c.PracticeSkill(Who.Everybody, (int)Skill.Stealth, 7, 10, 0);
            c.PassHours(1);
            c.SavedCard = c.IsDaylight ? GuildByDay : ThisCard;
            c.Screen.Text(6, TextFrame.BlankPage);
            if (c.Events.FactValue(PoliticsQuest, 0x17, c.Here) == Guild)
            {
                c.SavedCard = CityStreets;
                c.GuildCode = Guild;
                c.Next = 0x5F;
                return;
            }
            c.SavedCard = CityStreets;
            c.Next = 0x6C;
            return;
        }
        var stealth = c.PartyAverageSkill((int)Skill.Stealth);
        if (c.PartyMaxSkill((int)Skill.Artifice) < stealth)
        {
            // The lock defeats the best picker.
            c.Variables.Chosen[0] = c.BestMember;
            c.PracticeSkill(c.BestMember, (int)Skill.Artifice, 0, 10, 0);
            c.Screen.Text(5);
        }
        else
        {
            // The clumsiest sneak gives the party away.
            c.PartyMinSkill((int)Skill.Stealth);
            c.Variables.Chosen[0] = c.BestMember;
            c.PracticeSkill(c.BestMember, (int)Skill.Stealth, 0, 10, 0);
            c.Screen.Text(4, TextFrame.BlankPage);
        }
        c.PassHours(2);
        c.Next = 0x14;
    }
}
