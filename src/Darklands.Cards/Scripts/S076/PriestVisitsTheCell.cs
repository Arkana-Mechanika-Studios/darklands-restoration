namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Dunge01": a priest visits the party in its cell. Confess, ask him to help you escape,
/// ask for a good word with the magistrate, or send him away.
/// </summary>
// orig: R076+0x0C9C (1901:000C) matched; tests: CardSmokeTests
// orig: R076+0x11EE (1901:055E) matched; R076+0x1226 (1901:0596) matched; tests: CardSmokeTests
// DORMANT: the "screwed up." default handler of the unlisted lines 4..19 (never offered).
public sealed class PriestVisitsTheCell : Card
{
    public const int CardNumber = 0x83;

    private const int Picture = 244;
    private const int PriestPicture = 243;
    private const int Dungeon = 0x0D;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = CardNumber;
        var standing = c.Reputation(c.Here);
        c.SetCardSaints();
        c.SetCardItems();
        var menu = Menu(c);
        menu.Enable(0, 1, 2, 3);
        return menu.Run(
            // orig: R076+0x0DF4 (line 0); R076+0x0E74 (1901:01E4) matched; tests: CardSmokeTests
            new MenuLine(() => Confess(c, standing), () =>
            {
                c.Variables.Chosen[0] = c.Leader;
                return OptionHint.Say(SuccessAssured, c.Leader);
            }),
            new MenuLine(chance => AskForEscape(c, chance), () => OptionHint.ChanceOf(EscapeChance(c), c.Leader)),
            new MenuLine(() => AskForAGoodWord(c), OptionHint.Saying(NoRisk)),
            // orig: R076+0x11A4 (1901:0514) matched; R076+0x11B6 (1901:0526) matched; tests: CardSmokeTests
            new MenuLine(() => { c.PassHours(0); c.Next = Dungeon; }, OptionHint.Saying(SuccessAssured)));
    }

    // orig: R076+0x125E (1901:05CE) matched; tests: CardSmokeTests
    // DORMANT: a stub that always answers 0, so the escape chance's squared term and the good
    // word's bonus never count.
    /// <summary>The party's standing with the Church: always 0 in the original (an unfinished hook).</summary>
    private static int ChurchConnections() => 0;

    // C: the city flag byte is read as a signed char before the 0x40 test.
    private static bool CityHasAChurchConnection(CardContext c) => ((sbyte)c.LocationQuery(c.Here, 0x20) & 0x40) != 0;

    // orig: R076+0x0DF4 (1901:0164) matched; tests: CardSmokeTests
    private static void Confess(CardContext c, int standing)
    {
        c.PracticeSkill(Core.Characters.Who.Leader, 9, 1, 10, -1);
        var favour = c.Random(10) + standing / 20;
        c.AddAttribute(c.Leader, 6, favour);
        c.PassHours(3);
        ShowPicture(c, PriestPicture);
        c.Screen.Text(1);
        c.Next = Dungeon;
    }

    // orig: R076+0x0FC6 (1901:0336) matched; tests: CardSmokeTests
    private static int EscapeChance(CardContext c)
    {
        var piety = c.Skill(Core.Characters.Who.Leader, 8) + c.Attribute(Core.Characters.Who.Leader, 5)
            + c.Skill(Core.Characters.Who.Leader, 0x0B) + c.Skill(Core.Characters.Who.Leader, 9);
        var chance = ChurchConnections() * ChurchConnections() * 5 + piety / 10;
        if (CityHasAChurchConnection(c))
        {
            chance += 20;
        }
        return chance <= 0 ? 0 : Math.Clamp(chance, 1, 99);
    }

    // orig: R076+0x0EB0 (1901:0220) matched; tests: CardSmokeTests
    // C: party-map-column (CityMark) as the jail's note of a refused escape (0 becomes 2, else 1).
    /// <summary>A good chance means the abbot has the party released; a slim one, a tool smuggled in.</summary>
    private static void AskForEscape(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            c.PassHours(24);
            if (chance > 25)
            {
                ShowPicture(c, 24);
                c.Screen.Text(2);
                c.Next = c.IsDaylight ? 0x34 : 0x35;
                return;
            }
            var tool = c.Random(2) == 1 ? 0x40 : 0x63;
            c.AddItem(c.Leader, tool);
            c.NameToSlot(0, tool, 0);
            c.Screen.Text(3);
        }
        else
        {
            c.PassHours(1);
            ShowPicture(c, 288);
            c.Screen.Text(4);
            c.CityMark = c.CityMark != 0 ? 1 : 2;
        }
        c.Next = Dungeon;
    }

    // orig: R076+0x106C (1901:03DC) matched; R076+0x116C (1901:04DC) matched; tests: CardSmokeTests
    /// <summary>Half a day later the guards may come to take the party before the magistrate.</summary>
    private static void AskForAGoodWord(CardContext c)
    {
        // C: the original computes the leader's goodwill (skills 8, 11, 9 / 10 + reputation + 0 + 20
        // with the city flag) and never uses it; nothing is drawn, so it is left out.
        ShowPicture(c, PriestPicture);
        c.Screen.Text(5);
        c.PassHours(12);
        // C: && short-circuits: the roll is only drawn after noon.
        if (c.State.Date.Hour > 12 && c.Random(10) == 1)
        {
            ShowPicture(c, 279);
            c.Screen.Text(6);
            c.Next = 0x8C;
            return;
        }
        ShowPicture(c, Picture);
        c.Next = Dungeon;
    }
}
