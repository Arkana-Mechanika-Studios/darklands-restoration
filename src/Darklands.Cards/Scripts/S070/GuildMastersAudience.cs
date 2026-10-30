using Darklands.Core.Characters;
using Darklands.Core.Economy;
using Darklands.Core.Events;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Guild00": an audience with the masters of the guild handed on in party-map-column. Ask about
/// training, argue the city's politics with them, buy their support, or press them with the
/// guild leader's secret (and the evidence for it).
/// </summary>
// orig: R070+0x177A (19AF:000A) matched; tests: CardSmokeTests
// orig: handler setup R070+0x24A2 (standard pop-ups, none offered; unused-line handler 0dd4)
public sealed class GuildMastersAudience : Card
{
    private const int PoliticsQuest = 2;
    private const int FactKind = 0x1C;

    /// <summary>The evidence item for each of the seven secrets.</summary>
    private static readonly int[] EvidenceForSecret = [0x78, 0x79, 0x78, 0x7C, 0x7E, 0x7F, 0x80];

    public override int Number => 0x6A;

    public override int Run(CardContext c)
    {
        Show(c, 356);
        c.Next = Number;
        var guild = c.GuildCode;
        var menu = Menu(c, 0);
        menu.Enable(1, 2, 9);
        c.Support[0] = c.SideOf(guild);
        if (guild is 2 or 4 or 0x10 or 8 or 0x20)
        {
            menu[0] = OptionState.Enabled;
        }
        if (Fact(c, 0x17) == guild)
        {
            menu[1] = OptionState.Hidden;
        }
        else if ((Fact(c, 0x0D) & guild) != 0)
        {
            menu[1] = OptionState.Shown;
        }
        var side = Fact(c, 0);
        if (side == -1 && c.SideOf(guild) == 1)
        {
            menu[1] = menu[2] = menu[3] = OptionState.Hidden;
        }
        else if (side == 1 && c.SideOf(guild) == 2)
        {
            // DORMANT: place-side-read returns −1, 0 or 1, never 2.
            menu[1] = menu[2] = menu[3] = OptionState.Hidden;
        }
        else if (side == -1)
        {
            c.Variables.Named[0] = "rebel";
        }
        else if (side == 1)
        {
            c.Variables.Named[0] = "conservative";
        }
        c.Variables.Money[0] = Money.From(0, 0, Contribution(c));
        if (c.State.Party.Purse < c.Variables.Money[0])
        {
            menu[2] = OptionState.Shown;
        }
        if ((Fact(c, 9) & guild) != 0)
        {
            menu[3] = OptionState.Enabled;
        }

        return menu.Run(
            new MenuLine(chance => AskAboutTraining(c, menu, guild, chance), () => TrainingChance(c, guild)),
            new MenuLine(chance => ArgueThePolitics(c, menu, guild, chance),
                () => new OptionHint(Math.Clamp(Math.Max(Insight(c), Bearing(c)), 0, 99), "", c.Leader)),
            new MenuLine(_ => BuyTheirSupport(c, menu, guild), () => new OptionHint(-1, NoRisk, c.Leader)),
            new MenuLine(chance => PressTheLeader(c, guild, chance), () => BlackmailChance(c)),
            // DORMANT: line 4 (card-option-flag-4 = 0) does nothing. orig: R070+0x2314 (hint 0ba6 "No risk.", leader)
            new MenuLine(_ => { }, () => new OptionHint(-1, NoRisk, c.Leader)),
            // DORMANT: lines 5..8 (option-flag = 0): time passes. orig: R070+0x2350/0c24/0c68/0cac (hints 0bec/0c30/0c74/0cb8)
            new MenuLine(_ => c.PassHours(0), OptionHint.Saying(SuccessAssured)),
            new MenuLine(_ => c.PassHours(0), OptionHint.Saying(SuccessAssured)),
            new MenuLine(_ => c.PassHours(0), OptionHint.Saying(SuccessAssured)),
            new MenuLine(_ => c.PassHours(0), OptionHint.Saying(SuccessAssured)),
            // orig: R070+0x2460 (hint 0cf8): back to the card before.
            new MenuLine(_ => c.Next = c.PreviousCard, () => new OptionHint(-1, SuccessAssured, c.Leader)));
    }

    private static int Fact(CardContext c, int type) => c.Events.FactValue(PoliticsQuest, type, c.Here);

    /// <summary>The contribution that buys the guild ($Money1, in pfennigs): richer parties pay more, bigger cities more.</summary>
    // C: (unsigned)purse-in-pfennigs(): the purse in pfennigs cut to 16 bits; the rest is unsigned 16-bit arithmetic.
    private static int Contribution(CardContext c)
    {
        var pfennigs = (ushort)c.State.Party.Purse.TotalPfennigs;
        var perSize = (ushort)(pfennigs / 20 + 120);
        return (ushort)((ushort)(perSize * (ushort)c.LocationQuery(c.Here, 2)) / 6);
    }

    // orig: R070+0x1C50
    private static OptionHint TrainingChance(CardContext c, int guild)
    {
        var chance = Fact(c, 0x17) == guild && Fact(c, 0) == -1
            ? 100
            : c.Attribute(Who.Leader, (int)Attribute.Charisma) + (c.HereQuery(0) + c.PartyGet(4) / 20);
        return new OptionHint(Math.Clamp(chance, 0, 99), "", c.Leader);
    }

    /// <summary>A teacher of the guild's skills ($Money1 a day) for a week, or a sneer.</summary>
    // orig: R070+0x1B04
    private static void AskAboutTraining(CardContext c, CardMenu menu, int guild, int chance)
    {
        if (c.Succeeds(chance))
        {
            // C: (unsigned) location-now-query(0x21) % 15: the unsigned remainder; no "already there" test here.
            int Teacher(int skillQuery) => c.HereQuery(skillQuery) + (ushort)c.HereQuery(0x21) % 15 - 5;
            int skill;
            switch (guild)
            {
                case 2:
                case 0x10:
                    skill = Teacher(4);
                    Hire(c, skill, 0);
                    Hire(c, skill, 1);
                    break;
                case 4:
                    skill = Teacher(5);
                    Hire(c, skill, 0);
                    Hire(c, skill, 1);
                    break;
                case 8:
                    skill = Teacher(6);
                    Hire(c, skill, 0x80);
                    break;
                default:
                    skill = Teacher(8);
                    Hire(c, skill, 0x0E);
                    break;
            }
            c.Variables.Money[0] = Money.From(0, 0, Math.Clamp(c.Random(skill + 100), 80, 150));
            c.Screen.Text(2, TextFrame.BlankPage);
        }
        else
        {
            c.Screen.Text(1, TextFrame.BlankPage);
            c.Next = c.PreviousCard;
        }
        menu[0] = OptionState.Shown;
    }

    private static void Hire(CardContext c, int skill, int type) =>
        c.Events.Create(-2, c.Here, skill, 0x28, type, 0x32, 0, 0xA8, 0, 0);

    private static int Insight(CardContext c) =>
        c.Skill(Who.Leader, (int)Skill.SpeakCommon) / 10 + (c.HereQuery(0) / 2 + (c.PartyGet(4) / 20 + c.Attribute(Who.Leader, (int)Attribute.Charisma) / 4));

    private static int Bearing(CardContext c) =>
        c.Attribute(Who.Leader, (int)Attribute.Charisma) + (c.HereQuery(0) / 10 + (c.Skill(Who.Leader, (int)Skill.SpeakCommon) / 2 + c.PartyGet(4) / 100));

    /// <summary>
    /// Argue the politics: success moves the guild towards the party's cause (from a side to
    /// neutral, or from neutral to the cause); charm (text 4) or bearing (text 5) wins it.
    /// </summary>
    // orig: R070+0x1CCE (hint R070+0x1E8C)
    // C: the hint's larger-of(a, b) evaluates b (bearing) first; neither draws.
    private void ArgueThePolitics(CardContext c, CardMenu menu, int guild, int chance)
    {
        c.PassHours(1);
        if (c.Succeeds(chance))
        {
            c.Support[0] = c.SideOf(guild);
            c.SetSide(guild, c.Support[0] != 0 ? 0 : Fact(c, 0));
            c.Support[0] = c.SideOf(guild);
            if (Insight(c) <= Bearing(c))
            {
                c.AdjustReputation(c.Here, 1, 1);
                c.Screen.Text(4);
                c.PracticeSkill(c.Leader, (int)Skill.SpeakCommon, 1, 8, -1);
            }
            else
            {
                c.Screen.Text(5);
            }
        }
        else
        {
            c.AdjustReputation(c.Here, -1, -1);
            c.Screen.Text(3);
            c.PracticeSkill(c.Leader, (int)Skill.SpeakCommon, 0, 8, -1);
        }
        menu[1] = OptionState.Shown;
        c.Events.Update(-2, c.Here, FactKind, 0x0D, guild, PoliticsQuest, EventUpdate.Or);
        c.Next = Number;
    }

    /// <summary>A "contribution" buys the guild for the party's cause (no roll).</summary>
    // orig: R070+0x1F82 (hint 088e "No risk.", leader)
    // C: $Money1 is the contribution unless a training offer has replaced it meanwhile.
    private void BuyTheirSupport(CardContext c, CardMenu menu, int guild)
    {
        c.PassHours(1);
        c.AdjustReputation(c.Here, 1, 1);
        c.State.Party.Purse = c.State.Party.Purse.Subtract(c.Variables.Money[0]);
        c.Support[0] = c.SideOf(guild);
        c.SetSide(guild, Fact(c, 0));
        c.Support[0] = c.SideOf(guild);
        c.Screen.Text(0x0B);
        menu[2] = OptionState.Shown;
        c.Next = Number;
    }

    /// <summary>The evidence for the city's secret, or −1 when the remainder is negative.</summary>
    // C: a negative (q + 2) % 7 matches no case: the original then counts an uninitialised item code.
    private static int Evidence(CardContext c)
    {
        var secret = (c.LocationQuery(c.Here, 0x21) + 2) % 7;
        return secret is >= 0 and <= 6 ? EvidenceForSecret[secret] : -1;
    }

    // orig: R070+0x222C
    private static OptionHint BlackmailChance(CardContext c)
    {
        var chance = c.Skill(Who.Leader, (int)Skill.SpeakCommon) / 4 + (Math.Abs(c.HereQuery(0)) / 4 + c.PartyGet(4) / 50);
        // C: with no matching secret the original counts the item code k left uninitialised: a return offset of the R043+0x1BD2 call chain
        // (>= 0x100), never an item code (<= 0x80), so R029+0x0C76 gives 0, as nothing counted here.
        var evidence = Evidence(c);
        if (evidence != -1 && c.CountItems(Who.Everybody, evidence) != 0)
        {
            chance += 50;
        }
        return new OptionHint(Math.Clamp(chance, 0, 100), "", c.Leader);
    }

    /// <summary>
    /// Hint at the leader's secret: he gives way and the guild takes the party's side, or he
    /// laughs (and if the party shows its evidence, he takes it).
    /// </summary>
    // orig: R070+0x2038
    private void PressTheLeader(CardContext c, int guild, int chance)
    {
        var evidence = Evidence(c);
        if (c.Succeeds(chance))
        {
            c.AdjustReputation(c.Here, 5, 5);
            c.PassHours(2);
            c.Support[0] = c.SideOf(guild);
            c.SetSide(guild, Fact(c, 0));
            c.Support[0] = c.SideOf(guild);
            c.Screen.Text(7);
            ForgetTheSecret(c);
            // C: the success path jumps into the evidence switch of the failure path: the evidence is handed over.
            if (evidence != -1)
            {
                c.TakeItem(Who.Everybody, evidence);
            }
        }
        else
        {
            c.PassHours(1);
            if (evidence == -1 || c.CountItems(Who.Everybody, evidence) == 0)
            {
                c.Screen.Text(6);
            }
            else
            {
                c.Screen.Text(8);
                ForgetTheSecret(c);
                c.TakeItem(Who.Everybody, evidence);
            }
        }
        c.Next = Number;
    }

    private static void ForgetTheSecret(CardContext c)
    {
        c.Events.Update(-2, c.Here, FactKind, 8, -3, PoliticsQuest, EventUpdate.And);
        c.Events.Update(-2, c.Here, FactKind, 9, -3, PoliticsQuest, EventUpdate.And);
    }
}
