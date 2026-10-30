using Darklands.Core.Characters;
using Darklands.Core.Events;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$cloth00": Clothmakers' Street by day, the home of the clothmakers' guild. The party may
/// shop (the line is never offered), ask about the guild's politics, ask for a meeting with the
/// guild's masters, reveal the guild leader's secret to his guild, sneak into the leader's home
/// (with stealth and streetwise, or with an alchemical diversion), or leave. All of these depend
/// on the city's politics (the guild is one of its factions).
/// </summary>
// orig: R053+0x0EC4 (1924:0004) matched; tests: CardSmokeTests
public sealed class ClothmakersStreet_Cloth00 : Card
{
    public const int CardNumber = 0x25;

    private const int Picture = 69;

    private const int ClothmakersAtNight = 0x26, Back = 0x1A, GuildMeeting = 0x6A, GuildAudience = 0x74, GuildHostile = 0x79,
        SneakIntoLeadersHome = 0x5F, SneakIntoLeadersHomeBy = 0x6C;

    /// <summary>Politics of a city: the quest events of tag 2 and the facts (kind 0x1C) of group 2 at the location.</summary>
    private const int Politics = 2;

    /// <summary>The guild's own faction bit in the side table (place-side-read/set).</summary>
    private const int GuildBit = 0x40;

    // the meaning of the fact numbers is a guess from how the cards use them:
    // 0 the guild's leaning (-1 hostile), 3 asked about the politics, 4 asked for the masters, 5 tried to sneak in,
    // 8 learnt the leader's secret, 9 holds the proof of it, 0x17 the guild's leader is in a special state (0x40).
    private const int Leaning = 0, AskedAbout = 3, AskedMasters = 4, TriedToSneak = 5, KnowsSecret = 8, HasProof = 9, Special = 0x17;

    public override int Number => CardNumber;

    // orig: R053+0x0EC4 (1924:0004) matched (entry: option states from the city's politics, the items and the menu loop)
    // orig: R053+0x1DAE (1924:0EEE) matched (init helper: enables all 20 option lines, installs the "screwed up." default and the pop-up handlers; the entry then fills lines 0..8, so lines 9..19 stay enabled without a text line)
    // DORMANT: R053+0x1E50 (1924:0F90): the "screwed up." default hint for card-option-handler[0..19]; lines 0..8 have their own hints and lines 9..19 have no text line.
    // DORMANT: R053+0x1F48 (1924:1088), R053+0x1FD2 (1924:1112), R053+0x205C (1924:119C), R053+0x20E6 (1924:1226): saint pop-up handlers (chance text of the chosen saint); card-saint-lines stay empty (-1) and no option uses saints.
    public override int Run(CardContext c)
    {
        Show(c, Picture);
        var shop = OptionState.Hidden;
        var ask = OptionState.Enabled;
        var meeting = OptionState.Enabled;
        var secret = OptionState.Hidden;
        var sneak = OptionState.Enabled;
        var diversion = OptionState.Enabled;
        if (!c.Events.QuestAt(Politics, c.Here))
        {
            ask = meeting = secret = sneak = diversion = OptionState.Hidden;
        }
        else
        {
            if (Flag(c, AskedAbout))
            {
                ask = OptionState.Shown;
            }
            if (Flag(c, AskedMasters))
            {
                meeting = OptionState.Shown;
            }
            if (Flag(c, TriedToSneak))
            {
                sneak = diversion = OptionState.Shown;
            }
            if (Flag(c, HasProof))
            {
                secret = OptionState.Enabled;
            }
        }
        c.StandingSeen = c.HereQuery(0);
        c.SetCardItems(0x5F, 0x65, 0x61, 0x66);
        if (diversion == OptionState.Enabled)
        {
            diversion = c.ItemsUsable();
        }

        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2, 3, 4, 5, 6, 8);
        menu[0] = shop;
        menu[1] = ask;
        menu[2] = meeting;
        menu[3] = secret;
        menu[4] = sneak;
        menu[5] = diversion;
        c.Next = Number;
        return menu.Run(
            // orig: R053+0x117E (1924:02BE) matched (option 0: shop at the guild's street shops); hint R053+0x11B0 (1924:02F0) (success assured)
            // DORMANT: the entry switches option 0 off.
            new MenuLine(() =>
            {
                c.Barter(8, 0x0400L << 16, null); // C: shop 8, no name, categories low word 0, high word 0x400
                c.PassHours(1);
                c.Next = ThisStreet(c);
            }),
            // orig: R053+0x11E8 (1924:0328) matched (option 1: ask about the guild, see AskAboutTheGuild); hint R053+0x13F6 (1924:0536) (see AskChance)
            new MenuLine(chance => AskAboutTheGuild(c, menu, chance), () => AskChance(c)),
            // orig: R053+0x145A (1924:059A) matched (option 2: ask for the guild's masters, see RequestMeeting); hint R053+0x1678 (1924:07B8) (see MeetingChance)
            new MenuLine(chance => RequestMeeting(c, menu, chance), () => MeetingChance(c)),
            // orig: R053+0x1722 (1924:0862) matched (option 3: reveal the leader's secret, see RevealTheSecret); hint R053+0x1860 (1924:09A0) (see RevealChance)
            new MenuLine(chance => RevealTheSecret(c, chance), () => RevealChance(c)),
            // orig: R053+0x191E (1924:0A5E) matched (option 4: sneak into the leader's home, see SneakIn); hint R053+0x1A90 (1924:0BD0) (see SneakChance)
            new MenuLine(chance => SneakIn(c, chance), () => SneakChance(c)),
            // orig: R053+0x1ABC (1924:0BFC) matched (option 5: a diversion with a potion, see CreateADiversion); hint R053+0x1C9E (1924:0DDE) ("Select a person and a potion."), pop-up handlers R053+0x1E88 (1924:0FC8), R053+0x1EB8 (1924:0FF8), R053+0x1EE8 (1924:1028), R053+0x1F18 (1924:1058) (see DiversionChance)
            new MenuLine(chance => CreateADiversion(c, chance), OptionHint.Saying(CardTexts.SelectPotion), () => DiversionChance(c, c.MenuSlot)),
            // orig: R053+0x1CD6 (1924:0E16) matched (option 6: leave); hint R053+0x1CE8 (1924:0E28) (success assured)
            new MenuLine(() => { c.PassHours(0); c.Next = Back; }),
            // DORMANT: option 7 is switched off by the entry: R053+0x1D20 (1924:0E60) passes no time and names no next card; hint R053+0x1D2C (1924:0E6C) (success assured).
            new MenuLine(() => c.PassHours(0)),
            // orig: R053+0x1D64 (1924:0EA4) matched (option 8: leave); hint R053+0x1D76 (1924:0EB6) (success assured)
            // DORMANT: the MSG has only seven option lines (0..6), so line 8, although enabled, is never shown.
            new MenuLine(() => { c.PassHours(0); c.Next = Back; }));
    }

    private static int ThisStreet(CardContext c) => c.IsDaylight ? CardNumber : ClothmakersAtNight;

    /// <summary>A fact of the city's politics has the guild bit (0x40) in its low byte.</summary>
    private static bool Flag(CardContext c, int fact) => (c.Events.FactValue(Politics, fact, c.Here) & GuildBit) != 0;

    private static void MarkFact(CardContext c, int fact) =>
        c.Events.Update(-2, c.Here, EventKinds.LocationFact, fact, GuildBit, Politics, EventUpdate.Or);

    private static void SetMapX(CardContext c, int x) => c.State.MapPosition = c.State.MapPosition with { X = x };

    /// <summary>Which of the guild leader's seven secrets this city has (the same as the letter of its name: 0 lover ... 6 coiner).</summary>
    private static int SecretKind(CardContext c) => (c.HereQuery(0x21) + 0x40) % 7;

    /// <summary>The proof item of a secret; −1 for kinds that have none.</summary>
    private static int ProofItem(int kind) => kind switch
    {
        0 or 2 => 0x78,
        1 => 0x79,
        3 => 0x7C,
        4 => 0x7E,
        5 => 0x7F,
        6 => 0x80,
        _ => -1,
    };

    /// <summary>Intelligence plus Perception of the leader, but no more than his Speak Common plus the standing.</summary>
    // orig: R053+0x13F6 (1924:0536) matched
    private static OptionHint AskChance(CardContext c)
    {
        var ceiling = c.Skill(c.Leader, 0x0A) + c.StandingSeen;
        var wits = c.Attribute(c.Leader, 4) + c.Attribute(c.Leader, 3);
        return OptionHint.ChanceOf(Math.Clamp(Math.Min(wits, ceiling), 0, 99), c.Leader);
    }

    /// <summary>Asks the guild members about their leaders; learns the guild's side or a secret.</summary>
    // orig: R053+0x11E8 (1924:0328) matched
    private void AskAboutTheGuild(CardContext c, CardMenu menu, int chance)
    {
        c.Variables.Chosen[0] = c.Leader;
        var roll = c.Random(100);
        // R053+0x120D (1924:034D): the guild's side goes to $Support (DS:ED5E), the shared text variable.
        c.Support[0] = c.SideOf(GuildBit);
        if (roll < chance)
        {
            chance -= roll;
            c.PassHours(Math.Min(4, 100 / chance));
            if (c.Events.FactValue(Politics, Special, c.Here) == GuildBit)
            {
                c.Screen.Text(0x16, TextFrame.BlankPage);
                ShowPicture(c, Picture);
                c.Screen.Text(2, TextFrame.BlankPage);
                ShowPicture(c, Picture);
                Done(c, menu);
                return;
            }
            if (Flag(c, KnowsSecret))
            {
                switch (SecretKind(c))
                {
                    case 0:
                        c.Screen.Text(0x0D, TextFrame.BlankPage);
                        break;
                    case 1:
                        ShowPicture(c, Picture);
                        c.Screen.Text(0x0E, TextFrame.BlankPage);
                        ShowPicture(c, Picture);
                        break;
                    case 2:
                        c.Screen.Text(0x0F, TextFrame.BlankPage);
                        break;
                    case 3:
                        c.Screen.Text(0x10, TextFrame.BlankPage);
                        break;
                    case 4:
                        c.Screen.Text(0x11, TextFrame.BlankPage);
                        break;
                    case 5:
                        c.Screen.Text(0x12, TextFrame.BlankPage);
                        break;
                    case 6:
                        c.Screen.Text(0x13, TextFrame.BlankPage);
                        break;
                }
                MarkFact(c, HasProof);
                c.PracticeSkill(c.Leader, 0x0A, 7, 10, 0);
                Done(c, menu);
                return;
            }
        }
        else
        {
            c.PracticeSkill(c.Leader, 0x0A, 0, 10, 0);
            c.PassHours(2);
        }
        ShowPicture(c, Picture);
        c.Screen.Text(1, TextFrame.BlankPage);
        ShowPicture(c, Picture);
        Done(c, menu);
    }

    /// <summary>After asking: the option is closed and the question is remembered.</summary>
    private static void Done(CardContext c, CardMenu menu)
    {
        menu[1] = OptionState.Shown;
        MarkFact(c, AskedAbout);
    }

    /// <summary>The leader's Speak Common and Charisma plus the standing here; sure when the guild is already hostile and holds the special state.</summary>
    // orig: R053+0x1678 (1924:07B8) matched
    private static OptionHint MeetingChance(CardContext c)
    {
        if (c.Events.FactValue(Politics, Special, c.Here) == GuildBit && c.Events.FactValue(Politics, Leaning, c.Here) == -1)
        {
            return OptionHint.Say(SuccessAssured);
        }
        var chance = c.HereQuery(0) + (c.Skill(c.Leader, 0x0A) + c.Attribute(c.Leader, 5));
        return OptionHint.ChanceOf(Math.Clamp(chance, 1, 99), c.Leader);
    }

    /// <summary>Asks for a meeting with the guild's masters; the outcome depends on the guild's leaning.</summary>
    // orig: R053+0x145A (1924:059A) matched
    private void RequestMeeting(CardContext c, CardMenu menu, int chance)
    {
        menu[2] = OptionState.Shown;
        // C: the original also asks for the hours until dawn here and drops the answer.
        MarkFact(c, AskedMasters);
        if (c.Events.QuestAt(Politics, c.Here))
        {
            if (c.Events.FactValue(Politics, Special, c.Here) == GuildBit)
            {
                if (c.Events.FactValue(Politics, Leaning, c.Here) == 0)
                {
                    SetMapX(c, 0);
                    c.State.SavedCard = Back;
                    c.Next = GuildAudience;
                    return;
                }
                if (c.Events.FactValue(Politics, Leaning, c.Here) == -1)
                {
                    SetMapX(c, 0);
                    c.State.SavedCard = Back;
                    c.Next = GuildHostile;
                    return;
                }
                c.Screen.Text(0x16, TextFrame.BlankPage);
                c.Screen.Text(3, TextFrame.BlankPage);
                c.PassHours(1);
                c.Next = ThisStreet(c);
                return;
            }
            if (c.Random(100) <= chance && c.Events.FactValue(Politics, Leaning, c.Here) != 0)
            {
                c.PracticeSkill(c.Leader, 0x0A, 1, 6, 0);
                c.PassHours(2);
                SetMapX(c, GuildBit);
                c.State.SavedCard = ThisStreet(c);
                c.Next = GuildMeeting;
                return;
            }
            c.PracticeSkill(c.Leader, 0x0A, 0, 6, 0);
            c.Screen.Text(3, TextFrame.BlankPage);
            c.PassHours(Math.Min(Core.Time.TimeOfDay.HoursUntil(c.State.Date.Hour, 19) / 2, 3));
            c.Next = ThisStreet(c);
            return;
        }

        // The city has no guild quest (any more): the option is only offered while it has one, so this
        // branch runs only when the quest ended while the party stood on the card.
        // It hands the party a small sum (80..150 pfennigs) and makes sure an event of kind 0x28 exists here.
        var spread = c.HereQuery(9) + c.HereQuery(0x21) % 0x0F - 5;
        if (!c.EventTypeAtHere(0x28, 0x0E))
        {
            c.Events.Create(-2, c.Here, spread, 0x28, 0x0E, 0x32, 0, 0xA8, 0, 0);
        }
        c.Variables.Money[0] = Core.Economy.Money.From(0, 0, Math.Clamp(c.Random(spread + 100), 0x50, 0x96));
        c.PassHours(1);
        c.Screen.Text(0x17);
        c.Next = Back;
    }

    /// <summary>Doubles when the party carries the proof of the secret; Charisma plus twice Speak Common plus the standing.</summary>
    // orig: R053+0x1860 (1924:09A0) matched
    private static OptionHint RevealChance(CardContext c)
    {
        c.Variables.Chosen[0] = c.Leader;
        var chance = c.Attribute(c.Leader, 5) + c.Skill(c.Leader, 0x0A) * 2 + c.HereQuery(0);
        if (Flag(c, KnowsSecret))
        {
            var proof = ProofItem(SecretKind(c));
            if (proof != -1 && c.CountItems(Who.Everybody, proof) != 0)
            {
                chance <<= 1;
            }
        }
        return OptionHint.ChanceOf(Math.Clamp(chance, 0, 99), OptionHint.WholeParty);
    }

    /// <summary>Tells the guild the leader's secret and hands over the proof; success moves the guild's side, failure costs the proof.</summary>
    // orig: R053+0x1722 (1924:0862) matched
    private void RevealTheSecret(CardContext c, int chance)
    {
        c.Events.Update(-2, c.Here, EventKinds.LocationFact, KnowsSecret, ~GuildBit, Politics, EventUpdate.And);
        c.Events.Update(-2, c.Here, EventKinds.LocationFact, HasProof, ~GuildBit, Politics, EventUpdate.And);
        var proof = ProofItem(SecretKind(c));
        if (proof != -1)
        {
            c.TakeItem(Who.Everybody, proof);
        }
        if (c.Random(100) <= chance)
        {
            c.PracticeSkill(c.Leader, 0x0A, 7, 10, 0);
            // R053+0x17D6 (1924:0916): the new side goes to $Support (DS:ED5E), which text 0x0B reads.
            c.Support[0] = c.Random(3) - 1;
            c.SetSide(GuildBit, c.Support[0]);
            c.Screen.Text(0x0B);
            c.StandingSeen--;
            c.LocationSet(c.Here, 0, c.StandingSeen);
            c.PassHours(3);
            c.Next = Back;
            return;
        }
        c.PracticeSkill(c.Leader, 0x0A, 0, 10, 0);
        c.Screen.Text(0x0C);
        c.PassHours(3);
        c.Next = Back;
        MarkFact(c, AskedMasters);
    }

    /// <summary>The weaker of the party's weakest Stealth and weakest Streetwise.</summary>
    // orig: R053+0x1A90 (1924:0BD0) matched
    private static OptionHint SneakChance(CardContext c)
    {
        var stealth = c.PartyMinSkill(0x0F);
        var streetwise = c.PartyMinSkill(0x10);
        return OptionHint.ChanceOf(streetwise > stealth ? stealth : streetwise, OptionHint.WholeParty);
    }

    /// <summary>The better of Stealth and Streetwise of a member, with Stealth on a tie (Streetwise 0x10, Stealth 0x0F).</summary>
    private static int SneakSkill(CardContext c, int member) => c.Skill(member, 0x10) > c.Skill(member, 0x0F) ? 0x10 : 0x0F;

    /// <summary>Sneaks into the leader's home: a high roll is a mishap that still teaches, a low one gets the party inside.</summary>
    // orig: R053+0x191E (1924:0A5E) matched
    private void SneakIn(CardContext c, int chance)
    {
        var roll = c.Random(100);
        // C: the original asks for the hours until nightfall here and drops the answer.
        MarkFact(c, TriedToSneak);
        if (chance < roll)
        {
            for (var member = 0; member < c.PartySize; member++)
            {
                if (c.Status(member) == CharacterStatus.Able)
                {
                    // BUG: practices everybody (who −2) once per able member, as the original does.
                    c.PracticeSkill(Who.Everybody, SneakSkill(c, member), 1, 10, 0);
                }
            }
            c.Screen.Text(5, TextFrame.BlankPage);
            c.PassHours(1);
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
        for (var member = 0; member < c.PartySize; member++)
        {
            if (c.Status(member) == CharacterStatus.Able)
            {
                c.PracticeSkill(Who.Everybody, SneakSkill(c, member), 0, 10, 0);
            }
        }
        c.Screen.Text(roll * 2 <= chance ? 4 : 0x14, TextFrame.BlankPage);
        c.PassHours(2);
        c.Next = ThisStreet(c);
    }

    /// <summary>The potion pop-up's hint: the weakest member's Stealth or Streetwise plus a bonus that grows with the potion's slot.</summary>
    // orig: R053+0x1E88 (1924:0FC8) matched (slot 0: +25)
    // orig: R053+0x1EB8 (1924:0FF8) matched (slot 1: +30)
    // orig: R053+0x1EE8 (1924:1028) matched (slot 2: +37)
    // orig: R053+0x1F18 (1924:1058) matched (slot 3: +45)
    private static OptionHint DiversionChance(CardContext c, int slot)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        int[] bonus = [25, 30, 37, 45];
        return OptionHint.ChanceOf(Math.Clamp(c.PartyMinSkill(0x0F, 0x10) + bonus[slot], 0, 99), c.MenuMember);
    }

    /// <summary>Stages a diversion with a potion (alchemy), then sneaks in; like <see cref="SneakIn"/> but the learning is a chance, the effect is bigger.</summary>
    // orig: R053+0x1ABC (1924:0BFC) matched
    private void CreateADiversion(CardContext c, int chance)
    {
        var roll = c.Random(100);
        // C: the original asks for the hours until nightfall here and drops the answer.
        MarkFact(c, TriedToSneak);
        if (chance < roll)
        {
            for (var member = 0; member < c.PartySize; member++)
            {
                if (c.Status(member) == CharacterStatus.Able)
                {
                    var skill = c.Skill(member, 0x0F) < c.Skill(member, 0x10) ? 0x10 : 0x0F;
                    if (c.Random(100) <= 0x10)
                    {
                        c.AddSkill(member, skill, c.ImproveRoll(c.Skill(member, skill), 1, 4));
                    }
                }
            }
            c.Screen.Text(6, TextFrame.BlankPage);
            c.PassHours(1);
            if (c.Events.FactValue(Politics, Special, c.Here) == GuildBit)
            {
                c.State.SavedCard = Back;
                SetMapX(c, GuildBit);
                c.Next = SneakIntoLeadersHome;
                return;
            }
            c.State.SavedCard = Back;
            SetMapX(c, GuildBit);
            c.Next = SneakIntoLeadersHomeBy;
            return;
        }
        for (var member = 0; member < c.PartySize; member++)
        {
            if (c.Status(member) == CharacterStatus.Able)
            {
                var skill = c.Skill(member, 0x0F) < c.Skill(member, 0x10) ? 0x10 : 0x0F;
                if (c.Random(100) <= 8)
                {
                    c.AddSkill(member, skill, c.ImproveRoll(c.Skill(member, skill), 1, 2));
                }
            }
        }
        if (roll * 2 <= chance)
        {
            switch (c.MenuSlot)
            {
                case 0:
                    c.Screen.Text(7, TextFrame.BlankPage);
                    break;
                case 1:
                    c.Screen.Text(8, TextFrame.BlankPage);
                    break;
                case 2:
                    c.Screen.Text(9, TextFrame.BlankPage);
                    break;
                case 3:
                    c.Screen.Text(0x0A, TextFrame.BlankPage);
                    break;
            }
        }
        else
        {
            c.Screen.Text(0x15, TextFrame.BlankPage);
        }
        c.PassHours(1);
        c.Next = ThisStreet(c);
    }
}
