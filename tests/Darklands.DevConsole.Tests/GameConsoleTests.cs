using Darklands.Core;
using Darklands.Core.Characters;
using Darklands.Core.Economy;
using Darklands.Core.Events;
using Darklands.Core.Items;
using Darklands.Core.Randomness;
using Darklands.Core.Time;
using Darklands.Core.World;
using CharacterAttribute = Darklands.Core.Characters.Attribute;

namespace Darklands.DevConsole.Tests;

public sealed class GameConsoleTests
{
    private sealed class Items : IGameData
    {
        public IReadOnlyList<ItemDefinition> ItemDefinitions { get; } =
            Enumerable.Range(0, 10).Select(c => new ItemDefinition(c, c == 3 ? "Longsword" : $"item {c}", "", c, 0, 2, 25, 1)).ToList();

        public CityRecord LoadCity(int city) => throw new NotSupportedException();
    }

    private static (GameConsole Console, GameState State) Playing()
    {
        var state = new GameState(new MscRandom(1))
        {
            Data = new Items(),
            Party = DefaultParty.Create(7),
            Date = new GameDate(9, 10, 5, 1402),
            CurrentLocation = 0,
        };
        state.Locations.Add(new Location { Index = 0, Name = "Nurnberg" });
        var console = new GameConsole();
        GameCommands.Register(console, () => state);
        return (console, state);
    }

    // ---- the input line

    [Fact]
    public void TypingEditsTheLineAtTheCursor()
    {
        var console = new GameConsole();
        console.Type("shw time");
        console.MoveCursor(-6);
        console.Type("o");
        Assert.Equal("show time", console.Input);
        console.End();
        console.Backspace();
        console.Home();
        console.Delete();
        Assert.Equal("how tim", console.Input);
        Assert.Equal(0, console.Cursor);
    }

    [Fact]
    public void ControlCharactersAreNotTyped()
    {
        var console = new GameConsole();
        console.Type("a\tb\u0001c²é");
        Assert.Equal("abc²é", console.Input);
    }

    [Fact]
    public void UpAndDownRecallTheCommandsTypedBefore()
    {
        var console = new GameConsole();
        foreach (var line in new[] { "help", "clear", "help" })
        {
            console.Type(line);
            console.Submit();
        }
        console.Recall(-1);
        Assert.Equal("help", console.Input);
        console.Recall(-1);
        Assert.Equal("clear", console.Input);
        console.Recall(+1);
        console.Recall(+1);
        Assert.Equal("", console.Input);
    }

    [Fact]
    public void UnknownCommandsAndMissingGamesAreErrors()
    {
        var console = new GameConsole();
        GameCommands.Register(console, () => null);
        Assert.True(console.Execute("fly").IsError);
        Assert.True(console.Execute("show time").IsError);
        Assert.Equal(ConsoleLineKind.Error, console.Lines[^1].Kind);
    }

    [Fact]
    public void HelpListsEveryCommand()
    {
        var (console, _) = Playing();
        var help = console.Execute("help");
        Assert.Equal(console.Commands.Count, help.Lines.Count);
        Assert.Contains(help.Lines, l => l.StartsWith("money", StringComparison.Ordinal));
    }

    [Fact]
    public void TheScrollbackKeepsTheNewestLines()
    {
        var console = new GameConsole();
        for (var i = 0; i < GameConsole.ScrollbackLimit + 10; i++)
        {
            console.Write(i.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        Assert.Equal(GameConsole.ScrollbackLimit, console.Lines.Count);
        Assert.Equal((GameConsole.ScrollbackLimit + 9).ToString(System.Globalization.CultureInfo.InvariantCulture), console.Lines[^1].Text);
    }

    // ---- the commands

    [Fact]
    public void TimeAdvancesThroughTheGamesRules()
    {
        var (console, state) = Playing();
        console.Execute("time advance 20");
        Assert.Equal((5, 11), (state.Date.Hour, (int)state.Date.Day));
        console.Execute("time set 13");
        Assert.Equal(13, state.Date.Hour);
        Assert.True(console.Execute("time set 24").IsError);
    }

    [Fact]
    public void DateSetTakesTheMonthFromOne()
    {
        var (console, state) = Playing();
        console.Execute("date set 12 31 1405");
        Assert.Equal((11, 31, 1405), ((int)state.Date.Month, (int)state.Date.Day, (int)state.Date.Year));
        Assert.True(console.Execute("date set 2 30").IsError);
    }

    [Fact]
    public void MoneyIsAddedOrSet()
    {
        var (console, state) = Playing();
        console.Execute("money set 1 2 3");
        console.Execute("money add 0 0 10");
        Assert.Equal(Money.From(1, 3, 1), state.Party.Purse);
    }

    [Fact]
    public void ReputationIsThatOfThePlace()
    {
        var (console, state) = Playing();
        console.Execute("reputation set 250");
        Assert.Equal(250, state.Locations[0].Reputation);
        state.CurrentLocation = GameState.Wilderness;
        Assert.True(console.Execute("reputation set 1").IsError);
    }

    [Fact]
    public void CharacterSetsAttributesRaisingTheMaximumAndSkills()
    {
        var (console, state) = Playing();
        var m = state.Party.Members[0];
        console.Execute($"character 1 str {m.Maximum(CharacterAttribute.Strength) + 5}");
        Assert.Equal(m.Maximum(CharacterAttribute.Strength), m[CharacterAttribute.Strength]);
        console.Execute("character all streetwise 40");
        Assert.All(state.Party.Present, c => Assert.Equal(40, c[Skill.Streetwise]));
        console.Execute("character 1 edge 33");
        Assert.Equal(33, m[Skill.EdgedWeapons]);
        Assert.True(console.Execute("character 1 luck 3").IsError);
        Assert.True(console.Execute("character 9 str 3").IsError);
    }

    [Fact]
    public void HealRestoresEnduranceAndStrength()
    {
        var (console, state) = Playing();
        var m = state.Party.Members[1];
        m.Attributes[(int)CharacterAttribute.Endurance] = 1;
        console.Execute("heal 2");
        Assert.Equal(m.Maximum(CharacterAttribute.Endurance), m[CharacterAttribute.Endurance]);
    }

    [Fact]
    public void SaintsAreGrantedAndRemoved()
    {
        var (console, state) = Playing();
        console.Execute("saint grant all 17");
        Assert.All(state.Party.Present, c => Assert.Contains(17, c.Saints));
        console.Execute("saint remove 1 17");
        Assert.DoesNotContain(17, state.Party.Members[0].Saints);
    }

    [Fact]
    public void ItemsAreFoundAndGranted()
    {
        var (console, state) = Playing();
        Assert.Contains(console.Execute("item find sword").Lines, l => l.Contains("Longsword", StringComparison.Ordinal));
        var before = state.Party.Members[0].Pack.Count;
        console.Execute("item grant 1 3 30 2");
        var stack = state.Party.Members[0].Pack[before];
        Assert.Equal((3, 30, 2, 2), (stack.Code, stack.Quality, stack.Count, stack.Weight));
    }

    [Fact]
    public void QuestsAreListedWithTheirPlacesSeenFromHere()
    {
        var (console, state) = Playing();
        state.Locations.Add(new Location { Index = 1, Name = "Bremen", X = 30, Y = 0 });
        Assert.Equal(["no active quests"], console.Execute("quests active").Lines);
        state.Events.Create(0, 1, 0, EventKinds.Quest, 0, 0, 0, EventLog.OneYear, 0, 5);
        state.Events.Create(0, 0, 0, EventKinds.Quest, 0, 0, 0, EventLog.OneYear, 0, 6);
        state.Events.Create(0, 0, 0, EventKinds.ResolvedQuest, 0, 0, 0, EventLog.OneYear, 0, 5);
        state.Events.Create(0, 500, 0, EventKinds.ResolvedQuest, 0, 0, 0, EventLog.OneYear, 0, 7);
        Assert.Equal(["active quest 1 at Bremen (#1), east of here", "active quest 2 at Nurnberg (#0), here"],
            console.Execute("quests active").Lines);
        Assert.Equal(["resolved quest 1: the reward is at Nurnberg (#0), here", "resolved quest 2: the reward is at an unknown place (#500)"],
            console.Execute("quests resolved").Lines);
        Assert.Equal(4, console.Execute("quests all").Lines.Count);
        var wrong = console.Execute("quests open");
        Assert.True(wrong.IsError);
        Assert.Equal("quests active, resolved or all?", wrong.Lines[0]);
        Assert.True(console.Execute("quests").IsError);
    }
}
