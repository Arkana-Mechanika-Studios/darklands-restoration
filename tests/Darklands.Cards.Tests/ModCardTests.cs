using Darklands.Cards.Scripts;
using Darklands.Content;
using Darklands.Persistence;
using Darklands.Simulation;
using Darklands.Simulation.Mods;

namespace Darklands.Cards.Tests;

/// <summary>
/// The card registry (the game's cards kept apart, mods' cards on top with a precedence rule), data cards read from a mod.json,
/// the example mod of docs/MODS.md, and the companion file of a game saved with mods.
/// </summary>
public sealed class ModCardTests
{
    private static readonly CardSource ModA = new("mod-a", "Mod A", "1");
    private static readonly CardSource ModB = new("mod-b", "Mod B", "2");

    /// <summary>A card that only returns a fixed number.</summary>
    private sealed class FixedCard(int number, int next) : Card
    {
        public override int Number => number;

        public override int Run(CardContext c) => next;
    }

    [Fact]
    public void WithoutModsTheRegistryIsTheGamesTable()
    {
        var registry = new CardRegistry();
        Assert.False(registry.HasModCards);
        Assert.Empty(registry.ModCards);
        // every card of the table has its script but the map, 12, which the main loop runs itself
        Assert.Equal(CardTable.All.Length - 1, registry.Original.Count);
        Assert.Equal(registry.Original.Count, registry.Count);
        Assert.All(registry.Original.Keys, n => Assert.Equal(n, CardTable.All[n].Number));
        Assert.False(registry.Contains(CardDispatcher.OverlandMap));
        foreach (var (number, card) in registry.Original)
        {
            Assert.Same(card, registry.Find(number));
            Assert.Equal(number, card.Number);
        }
        Assert.Null(registry.Find(CardRegistry.LastOriginalCard + 1));
        Assert.Equal(CardRegistry.LastOriginalCard, CardTable.All.Length - 1);
        Assert.Equal(CardRegistry.LastOriginalCard, OriginalSaves.LastOriginalCard);
    }

    [Fact]
    public void ANewCardIsAddedBesideTheTableWhichStaysAsItWas()
    {
        var registry = new CardRegistry();
        var before = CardTable.All.ToArray();
        Assert.True(registry.Register(new FixedCard(366, 2), ModA, savesAs: 2));
        Assert.True(registry.HasModCards);
        Assert.Equal(366, registry.Find(366)!.Number);
        Assert.Equal(2, registry.SavedNumberOf(366));
        Assert.Equal(5, registry.SavedNumberOf(5));
        Assert.Null(registry.SavedNumberOf(367));
        Assert.Equal(before, CardTable.All);
        Assert.False(registry.Original.ContainsKey(366));
        Assert.Equal(registry.Original.Count + 1, registry.Count);
        Assert.Contains(registry.Log, l => l.Contains("card 366 (0x16E): added by mod \"Mod A\" 1 (mod-a)") && l.Contains("card 2 (0x2)"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(366)]
    [InlineData(-1)]
    [InlineData(CardDispatcher.EndOfGame)]
    public void ANewCardNeedsAGameCardToBeSavedAs(int? savesAs)
    {
        var registry = new CardRegistry();
        Assert.False(registry.Register(new FixedCard(400, 2), ModA, savesAs: savesAs));
        Assert.False(registry.HasModCards);
        Assert.Contains(registry.Log, l => l.Contains("refused") && l.Contains("savesAs"));
    }

    [Fact]
    public void TheMapIsAValidPlaceToSaveAs()
    {
        var registry = new CardRegistry();
        Assert.True(registry.Register(new FixedCard(400, 2), ModA, savesAs: CardDispatcher.OverlandMap));
        Assert.Equal(CardDispatcher.OverlandMap, registry.SavedNumberOf(400));
    }

    [Fact]
    public void AGameCardIsReplacedOnlyByAnExplicitOverride()
    {
        var registry = new CardRegistry();
        Assert.False(registry.Register(new FixedCard(CityGate.CardNumber, 4), ModA));
        Assert.IsType<CityGate>(registry.Find(CityGate.CardNumber));
        Assert.Contains(registry.Log, l => l.Contains("refused") && l.Contains("override"));

        var replacement = new FixedCard(CityGate.CardNumber, 4);
        Assert.True(registry.Register(replacement, ModA, overrideOriginal: true));
        Assert.Same(replacement, registry.Find(CityGate.CardNumber));
        Assert.IsType<CityGate>(registry.OriginalOf(CityGate.CardNumber));
        Assert.IsType<CityGate>(registry.Original[CityGate.CardNumber]);
        Assert.Equal(CityGate.CardNumber, registry.SavedNumberOf(CityGate.CardNumber));
        Assert.Contains(registry.Log, l => l.Contains("overrides the game's card $CityG01"));
        // a new number is never an override
        Assert.False(registry.Register(new FixedCard(500, 4), ModA, overrideOriginal: true, savesAs: 2));
    }

    [Fact]
    public void TheModLoadedLaterWinsANumberAndTheReplacementIsLogged()
    {
        var registry = new CardRegistry();
        var first = new FixedCard(366, 2);
        var second = new FixedCard(366, 3);
        Assert.True(registry.Register(first, ModA, savesAs: 2));
        Assert.True(registry.Register(second, ModB, savesAs: 3));
        Assert.Same(second, registry.Find(366));
        Assert.Equal(3, registry.SavedNumberOf(366));
        Assert.Contains(registry.Log, l => l.Contains("mod \"Mod B\" 2 (mod-b) replaces the card of mod \"Mod A\" 1 (mod-a)"));
        Assert.Equal([ModB], registry.Mods);
    }

    [Theory]
    [InlineData(CardDispatcher.OverlandMap)]
    [InlineData(CardDispatcher.EndOfGame)]
    [InlineData(CardDispatcher.QuitGame)]
    [InlineData(-2)]
    [InlineData(CardRegistry.LastModCard + 1)]
    public void NumbersThatAreNoCardCannotBeRegistered(int number)
    {
        var registry = new CardRegistry();
        Assert.False(registry.Register(new FixedCard(number, 2), ModA, overrideOriginal: number <= CardRegistry.LastOriginalCard, savesAs: 2));
        Assert.False(registry.HasModCards);
    }

    [Fact]
    public void OnlyAModRegisters()
    {
        var registry = new CardRegistry();
        Assert.False(registry.Register(new FixedCard(366, 2), CardSource.Game, savesAs: 2));
    }

    [Fact]
    public void TheDispatcherRunsTheRegistrysCards()
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var registry = new CardRegistry();
        registry.Register(new FixedCard(366, 4), ModA, savesAs: 2);
        var dispatcher = new CardDispatcher(registry);
        var state = CardHarness.NewGame();
        state.CurrentCard = 366;
        state.PreviousCard = 2;
        var context = new CardContext(state, CardHarness.World!, new ScriptedScreen(0)) { PersonalNames = CardHarness.PersonalNames };
        Assert.Equal(4, dispatcher.RunCard(context));
        Assert.Equal(366, state.PreviousCard);
        Assert.Equal(4, state.CurrentCard);
        Assert.True(dispatcher.HasScript(366));
        Assert.False(new CardDispatcher().HasScript(366));
    }

    // ---- mod.json ----

    /// <summary>The repository's docs/mods folder (the example mod's source).</summary>
    private static string ExampleMod()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "Darklands.slnx")))
        {
            folder = folder.Parent;
        }
        Assert.NotNull(folder);
        return Path.Combine(folder.FullName, "docs", "mods", "wayside-shrine");
    }

    [Fact]
    public void TheExampleModReadsWithoutAProblem()
    {
        var mod = ModFolder.From(ModFolders.Read(ExampleMod()));
        Assert.Empty(mod.Problems);
        Assert.Equal("wayside-shrine", mod.Source.Id);
        Assert.Equal("The wayside shrine", mod.Source.Name);
        Assert.Equal("1.0", mod.Source.Version);
        Assert.Equal([2, 366], mod.Cards.Select(c => c.Number));
        var gate = mod.Cards[0];
        Assert.True(gate.Overrides);
        Assert.Empty(gate.Texts.Blocks);
        var shrine = mod.Cards[1];
        Assert.False(shrine.Overrides);
        Assert.Equal(2, shrine.SavesAs);
        Assert.Equal("aproch1", shrine.Picture);
        Assert.Equal(3, shrine.Texts.Blocks.Count);
        var first = shrine.Texts.Blocks[0];
        Assert.Equal((ModFolder.DefaultX, ModFolder.DefaultY, ModFolder.DefaultWidth), (first.X, first.Y, first.Width));
        // three option lines, each a line of its own starting with the plain option marker 0x15
        Assert.Equal(3, first.Text.Count(b => b == 0x15));
        Assert.All(Enumerable.Range(0, first.Text.Length).Where(i => first.Text[i] == 0x15), i => Assert.Equal((byte)'\n', first.Text[i - 1]));
        Assert.DoesNotContain(first.Text, b => b is < 0x20 and not (byte)'\n' and not 0x15 || b > 0x7E);

        var set = LoadMods(ExampleMod());
        Assert.True(set.Cards.HasModCards);
        Assert.Equal(2, set.Cards.SavedNumberOf(366));
        Assert.IsType<DataCard>(set.Cards.Find(2));
        Assert.IsType<CityGate>(((DataCard)set.Cards.Find(2)!).OriginalCard);
        Assert.Equal(["$ShrGt00", "$Shrin00"], set.Texts.Select(t => t.Name));
        Assert.Equal([new Core.ModMark("wayside-shrine", "The wayside shrine", "1.0")], set.Marks);
        Assert.DoesNotContain(set.Log, l => l.Contains("refused") || l.Contains("left out") || l.Contains("no script"));
    }

    private static ModFolder Parse(string json) => ModFolder.Parse("x", "test-mod", json);

    /// <summary>The mods of these folders, read as the hosts read them.</summary>
    private static ModSet LoadMods(params string[] folders) => ModSet.Load(folders.Select(ModFolders.Read));

    /// <summary>A mod folder holding this mod.json.</summary>
    private static string ModWith(string json, string name = "test-mod")
    {
        var folder = Path.Combine(TempCopies.Create("darklands-mods-"), name);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, ModFolder.ManifestName), json);
        return folder;
    }

    [Theory]
    [InlineData("""{ "cards": [ { "number": 366, "savesAs": 2, "pages": [ { "text": "hello", "next": "nowhere" } ] } ] }""", "not a page of this card")]
    [InlineData("""{ "cards": [ { "number": 366, "savesAs": 2, "pages": [ { "text": "hello", "next": "original" } ] } ] }""", "only for a card that overrides")]
    [InlineData("""{ "cards": [ { "number": 366, "savesAs": 2, "pages": [ { "text": "café", "next": 2 } ] } ] }""", "printable ASCII")]
    [InlineData("""{ "cards": [ { "number": 366, "savesAs": 2, "pages": [ { "text": "tab\there", "next": 2 } ] } ] }""", "printable ASCII")]
    [InlineData("""{ "cards": [ { "number": 366, "savesAs": 2, "pages": [ { "text": "pay", "options": [ { "text": "a", "if": { "groschen": 1 }, "next": 2 } ] } ] } ] }""", "at least one option")]
    [InlineData("""{ "cards": [ { "number": 366, "savesAs": 2, "pages": [ { "chance": 120, "next": 2, "otherwise": 3 } ] } ] }""", "0 to 100")]
    [InlineData("""{ "cards": [ { "number": 366, "savesAs": 2, "pages": [ { "text": "x", "do": { "fame": 3 }, "next": 2 } ] } ] }""", "not \"fame\"")]
    [InlineData("""{ "cards": [ { "number": 366, "savesAs": 2, "name": "$CityG01", "pages": [ { "text": "x", "next": 2 } ] } ] }""", "one of the game's cards")]
    [InlineData("""{ "cards": [ { "number": 366, "savesAs": 2, "pages": [] } ] }""", "at least one page")]
    [InlineData("""{ "cards": [ { "savesAs": 2, "pages": [ { "text": "x", "next": 2 } ] } ] }""", "whole \"number\"")]
    [InlineData("""{ "cards": [ { "number": 366, "savesAs": 2, "pages": [ { "text": "x" } ] } ] }""", "needs \"next\"")]
    public void ACardWithAProblemIsLeftOutWithAClearLine(string json, string problem)
    {
        var mod = Parse(json);
        Assert.Empty(mod.Cards);
        Assert.Contains(mod.Problems, p => p.Contains(problem));
        var set = ModSet.Load([new ModManifest("x", "test-mod", json, null)]);
        Assert.False(set.Cards.HasModCards);
        Assert.Contains(set.Log, l => l.Contains("is not played"));
    }

    [Theory]
    [InlineData("""{ "cards": [ { "number": "400", "savesAs": 2, "pages": [ { "text": "x", "next": 2 } ] } ] }""", "\"number\"")]
    [InlineData("""{ "cards": [ { "number": 400, "savesAs": "12", "pages": [ { "text": "x", "next": 2 } ] } ] }""", "\"savesAs\"")]
    [InlineData("""{ "cards": [ { "number": 400, "savesAs": 2, "pages": [ { "text": "x", "do": { "florins": "1" }, "next": 2 } ] } ] }""", "\"florins\"")]
    [InlineData("""{ "cards": [ { "number": 400, "savesAs": 2, "pages": [ { "text": "x", "do": { "hours": "1" }, "next": 2 } ] } ] }""", "\"hours\"")]
    [InlineData("""{ "cards": [ { "number": 400, "savesAs": 2, "overrides": "yes", "pages": [ { "text": "x", "next": 2 } ] } ] }""", "\"overrides\"")]
    [InlineData("""{ "cards": [ { "number": 400, "savesAs": 2, "pages": [ { "chance": "50", "next": 2, "otherwise": 2 } ] } ] }""", "\"chance\"")]
    [InlineData("""{ "cards": [ { "number": 400, "savesAs": 2, "pages": [ { "ifPreviousCard": ["2"], "next": 2, "otherwise": 2 } ] } ] }""", "ifPreviousCard")]
    [InlineData("""{ "cards": [ { "number": 400, "savesAs": 2, "pages": [ { "text": "x", "next": 2, "x": "5" } ] } ] }""", "\"x\"")]
    [InlineData("""{ "cards": [ { "number": 400, "savesAs": 2, "pages": [ { "text": "x", "options": [ { "text": "a", "if": { "groschen": "1" }, "next": 2 }, { "text": "b", "next": 2 } ] } ] } ] }""", "\"groschen\"")]
    [InlineData("""{ "cards": [ { "number": 1e3, "savesAs": 2, "pages": [ { "text": "x", "next": 2 } ] } ] }""", "\"number\"")]
    public void AValueOfTheWrongTypeNamesTheModAndTheFieldAndTheModIsNotPlayed(string json, string field)
    {
        var lines = new List<string>();
        var set = ModSet.Load([new ModManifest("x", "typed-mod", json, null)], lines.Add);
        Assert.False(set.Cards.HasModCards);
        Assert.Empty(set.Texts);
        Assert.Contains(lines, l => l.Contains("typed-mod") && l.Contains(field));
        Assert.Contains(lines, l => l.Contains("typed-mod") && l.Contains("is not played"));
    }

    [Fact]
    public void AModFolderWithoutAManifestOrWithBadJsonGivesNoCardsAndAProblem()
    {
        var empty = TempCopies.Create("darklands-mod-");
        var none = ModFolder.From(ModFolders.Read(empty));
        Assert.Empty(none.Cards);
        Assert.Contains(none.Problems, p => p.Contains("no mod.json"));
        File.WriteAllText(Path.Combine(empty, ModFolder.ManifestName), "{ \"cards\": [ ");
        var broken = ModFolder.From(ModFolders.Read(empty));
        Assert.Empty(broken.Cards);
        Assert.Contains(broken.Problems, p => p.Contains("cannot be read"));
        var set = LoadMods(empty);
        Assert.False(set.Cards.HasModCards);
    }

    [Fact]
    public void ANewCardWithoutSavesAsIsRefusedAndItsModNotPlayed()
    {
        // the mod's other card is sound, but a mod is played whole or not at all
        var folder = ModWith("""{ "cards": [ { "number": 366, "pages": [ { "text": "x", "next": 2 } ] }, { "number": 367, "savesAs": 2, "pages": [ { "text": "y", "next": 2 } ] } ] }""");
        var set = LoadMods(folder);
        Assert.False(set.Cards.HasModCards);
        Assert.Empty(set.Texts);
        Assert.Empty(set.Played);
        Assert.Contains(set.Log, l => l.Contains("refused") && l.Contains("savesAs"));
        Assert.Contains(set.Log, l => l.Contains("is not played"));
    }

    [Fact]
    public void AModThatLeadsToACardNobodyDefinesIsNotPlayed()
    {
        var folder = ModWith("""{ "cards": [ { "number": 366, "savesAs": 2, "pages": [ { "text": "x", "next": 999 } ] } ] }""");
        var set = LoadMods(folder);
        Assert.False(set.Cards.HasModCards);
        Assert.Contains(set.Log, l => l.Contains("leads to card 999") && l.Contains("no played mod defines"));
        Assert.Contains(set.Log, l => l.Contains("is not played"));
        // the game's cards, the map and the end of the game are always there
        var sound = ModWith("""{ "cards": [ { "number": 366, "savesAs": 2, "pages": [ { "text": "x", "options": [ { "text": "a", "next": 12 }, { "text": "b", "next": 98 }, { "text": "c", "next": 5 } ] } ] } ] }""");
        Assert.True(LoadMods(sound).Cards.HasModCards);
    }

    [Fact]
    public void AModWhoseLinkWasLeftOutWithAnotherModIsNotPlayedEither()
    {
        // mod a leads to 367, which only mod b defines; mod b has a problem (a card without savesAs), so neither is played
        var a = ModWith("""{ "cards": [ { "number": 366, "savesAs": 2, "pages": [ { "text": "x", "next": 367 } ] } ] }""", "mod-a");
        var b = ModWith("""{ "cards": [ { "number": 367, "savesAs": 2, "pages": [ { "text": "y", "next": 2 } ] }, { "number": 368, "pages": [ { "text": "z", "next": 2 } ] } ] }""", "mod-b");
        var set = LoadMods(a, b);
        Assert.False(set.Cards.HasModCards);
        Assert.Empty(set.Played);
        Assert.Contains(set.Log, l => l.Contains("(mod-a)") && l.Contains("leads to card 367"));
        // with a sound mod b, both are played
        var b2 = ModWith("""{ "cards": [ { "number": 367, "savesAs": 2, "pages": [ { "text": "y", "next": 2 } ] } ] }""", "mod-b");
        var both = LoadMods(a, b2);
        Assert.Equal(2, both.Played.Count);
        Assert.True(both.Cards.Contains(366) && both.Cards.Contains(367));
    }

    [Fact]
    public void ACardNameOfTheGamesMessageFilesOrOfAnotherModIsRefused()
    {
        var mod = ModWith("""{ "cards": [ { "number": 366, "name": "$Qzx00", "savesAs": 2, "pages": [ { "text": "x", "next": 2 } ] } ] }""");
        var lines = new List<string>();
        var set = ModSet.Load([ModFolders.Read(mod)], lines.Add, name => name == "$Qzx00");
        Assert.False(set.Cards.HasModCards);
        Assert.Contains(lines, l => l.Contains("\"$Qzx00\" is a message file of the game"));
        var first = ModWith("""{ "cards": [ { "number": 366, "name": "$Same00", "savesAs": 2, "pages": [ { "text": "x", "next": 2 } ] } ] }""", "first");
        var second = ModWith("""{ "cards": [ { "number": 367, "name": "$Same00", "savesAs": 2, "pages": [ { "text": "y", "next": 2 } ] } ] }""", "second");
        var two = LoadMods(first, second);
        Assert.Equal(["first"], two.Played.Select(m => m.Source.Id));
        Assert.Contains(two.Log, l => l.Contains("(second)") && l.Contains("already a card name"));
    }

    [Fact]
    public void TheGamesMessageFilesThatAreNoCardNamesAreRefusedToo()
    {
        if (CardHarness.Assets is not { } assets)
        {
            return;
        }
        // the MSGFILES entries that no card of the table names: a mod card may take none of them either
        var archive = new Content.Formats.Text.MsgFilesArchiveDecoder().Decode(assets.Files.Read("MSGFILES"));
        var cardNames = CardTable.All.Select(c => (c.Name + ".MSG").ToUpperInvariant()).ToHashSet();
        var others = archive.Entries.Select(e => e.Name).Where(n => !cardNames.Contains(n.ToUpperInvariant())).ToList();
        foreach (var name in others.Take(20))
        {
            var stem = name[..name.LastIndexOf('.')];
            Assert.True(assets.IsGameMessageFile(stem), name);
            var json = $$"""{ "cards": [ { "number": 366, "name": "{{stem}}", "savesAs": 2, "pages": [ { "text": "x", "next": 2 } ] } ] }""";
            var set = ModSet.Load([new ModManifest("x", "named-mod", json, null)], null, assets.IsGameMessageFile);
            Assert.False(set.Cards.HasModCards, name);
        }
        Assert.False(assets.IsGameMessageFile("$Shrin00"));
    }

    [Fact]
    public void AModIsFoundByNameInAModsFolderNextToTheGame()
    {
        var game = TempCopies.Create("darklands-game-");
        var mod = Path.Combine(game, "mods", "my-mod");
        Directory.CreateDirectory(mod);
        File.WriteAllText(Path.Combine(mod, ModFolder.ManifestName), "{ \"cards\": [] }");
        Assert.Equal(Path.GetFullPath(mod), ModFolders.Find("my-mod", game));
        Assert.Equal(Path.GetFullPath(mod), ModFolders.Find(mod, null));
        Assert.Null(ModFolders.Find("other-mod", game));
    }

    // ---- running the example ----

    private static (int Next, ScriptedScreen Screen) RunCard(Card card, Core.GameState state, int option)
    {
        var screen = new ScriptedScreen(option);
        var context = new CardContext(state, CardHarness.World!, screen) { PersonalNames = CardHarness.PersonalNames };
        state.CurrentCard = card.Number;
        return (card.Run(context), screen);
    }

    [Fact]
    public void TheShrineTakesAGroschenAndLeadsToTheGate()
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var set = LoadMods(ExampleMod());
        var state = CardHarness.NewGame();
        state.PreviousCard = 2;
        state.Party.Purse = Core.Economy.Money.From(1, 2, 3);
        var purse = state.Party.Purse;
        var (next, screen) = RunCard(set.Cards.Find(366)!, state, option: 0);
        Assert.Equal(2, next);
        Assert.Equal(["show $Shrin00 aproch1", "menu 0", "text 1"], screen.Log);
        Assert.Equal(purse.TotalPfennigs - 12, state.Party.Purse.TotalPfennigs);
    }

    [Fact]
    public void PrayingAtTheShrinePassesAnHour()
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var set = LoadMods(ExampleMod());
        var state = CardHarness.NewGame();
        var hour = state.Date.Hour;
        var purse = state.Party.Purse;
        var (next, screen) = RunCard(set.Cards.Find(366)!, state, option: 1);
        Assert.Equal(2, next);
        Assert.Contains("text 2", screen.Log);
        Assert.Equal(purse, state.Party.Purse);
        Assert.Equal((hour + 1) % 24, state.Date.Hour);
    }

    [Fact]
    public void WithoutMoneyTheGiftIsShownButCannotBeChosen()
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var set = LoadMods(ExampleMod());
        var state = CardHarness.NewGame();
        state.Party.Purse = Core.Economy.Money.Zero;
        var (next, screen) = RunCard(set.Cards.Find(366)!, state, option: 0);
        Assert.Equal([OptionState.Shown, OptionState.Enabled, OptionState.Enabled], screen.LastOptions);
        Assert.Equal(Core.Economy.Money.Zero, state.Party.Purse);
        Assert.NotEqual("text 1", screen.Log[^1]);
        Assert.Equal(2, next);
    }

    [Fact]
    public void BackFromTheShrineTheGateIsTheGamesOwn()
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var set = LoadMods(ExampleMod());
        var state = CardHarness.NewGame();
        state.PreviousCard = 366;
        state.Random.Seed(1);
        var (_, screen) = RunCard(set.Cards.Find(2)!, state, option: 6);
        Assert.StartsWith("show $CityG01", screen.Log[0]);
        // the previous-card branch draws nothing: the same as the game's card from here on
        var plain = CardHarness.NewGame();
        plain.PreviousCard = 366;
        plain.Random.Seed(1);
        var (_, plainScreen) = RunCard(new CityGate(), plain, option: 6);
        Assert.Equal(plainScreen.Log, screen.Log);
        Assert.Equal(plain.Random.NextRaw(), state.Random.NextRaw());
    }

    [Fact]
    public void AtTheGateThePilgrimIsMetSometimes()
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var set = LoadMods(ExampleMod());
        var met = 0;
        for (uint seed = 1; seed <= 40; seed++)
        {
            var state = CardHarness.NewGame();
            state.PreviousCard = CardDispatcher.OverlandMap;
            state.Random.Seed(seed);
            var (next, screen) = RunCard(set.Cards.Find(2)!, state, option: 6);
            if (next == 366)
            {
                met++;
                Assert.Empty(screen.Log);
            }
            else
            {
                Assert.StartsWith("show $CityG01", screen.Log[0]);
            }
        }
        Assert.InRange(met, 1, 39);
    }

    [Fact]
    public void AModCardsTextsCannotTakeAGameCardsName()
    {
        if (CardHarness.Assets is not { } assets)
        {
            return;
        }
        var set = LoadMods(ExampleMod());
        foreach (var texts in set.Texts)
        {
            assets.AddMessages(texts);
        }
        Assert.Same(set.Texts[1], assets.Messages("$Shrin00"));
        Assert.Throws<ArgumentException>(() => assets.AddMessages(new Content.MessageFile("$CityG01", [])));
        Assert.NotEmpty(assets.Messages("$CityG01").Blocks);
    }

    // ---- saved games ----

    private static SaveMods ModsOf(ModSet set) => new()
    {
        Active = set.Marks,
        SavedCardOf = n => set.Cards.ModCards.TryGetValue(n, out var r) ? r.SavesAs : null,
        HasCard = set.Cards.Contains,
    };

    private static int[] HeaderCards(byte[] sav) =>
        OriginalSaves.CardFieldOffsets.Select(at => (int)(short)(sav[at] | sav[at + 1] << 8)).ToArray();

    [Fact]
    public void AGameSavedOnAModCardNamesTheGameCardInItsSaveAndTheModCardInItsCompanion()
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var set = LoadMods(ExampleMod());
        var folder = new SaveFolder(TempCopies.Create("darklands-modsave-")) { Mods = ModsOf(set) };
        var state = CardHarness.NewGame();
        state.CurrentCard = 366;
        state.PreviousCard = 2;
        state.SavedCard = 366;
        var name = folder.Save(state, "at the shrine", music: true, soundEffects: true);

        var sav = File.ReadAllBytes(Path.Combine(folder.Path, name.ToUpperInvariant()));
        Assert.Equal([2, 2, 6, 2], HeaderCards(sav));
        Assert.All(HeaderCards(sav), n => Assert.InRange(n, 0, OriginalSaves.LastOriginalCard));
        var companion = Path.Combine(folder.Path, ModdedSaves.CompanionOf(name));
        Assert.Equal("DKSAVE0.MODS.JSON", Path.GetFileName(companion));
        var json = File.ReadAllText(companion);
        Assert.Contains("\"modded\": true", json);
        Assert.Contains("\"current\": 366", json);
        Assert.Contains("wayside-shrine", json);
        // the game being played still stands on its mod card, and is marked modded
        Assert.Equal(366, state.CurrentCard);
        Assert.Equal([new Core.ModMark("wayside-shrine", "The wayside shrine", "1.0")], ModdedSaves.ModsUsed(state));

        // with the mod: the mod's cards come back
        var withMod = folder.Load(name, CardHarness.World!);
        Assert.Empty(withMod.Mods.MissingMods);
        Assert.Null(withMod.Mods.Problem);
        Assert.Equal(366, withMod.State.CurrentCard);
        Assert.Equal(366, withMod.State.SavedCard);
        Assert.Equal(2, withMod.State.PreviousCard);
        Assert.Equal(ModdedSaves.ModsUsed(state), ModdedSaves.ModsUsed(withMod.State));

        // without it (Classic): the game's cards the .SAV names, and the mod reported missing
        var classic = new SaveFolder(folder.Path).Load(name, CardHarness.World!);
        Assert.Equal(2, classic.State.CurrentCard);
        Assert.Equal(2, classic.State.SavedCard);
        Assert.Equal(["The wayside shrine 1.0"], classic.Mods.MissingMods.Select(m => m.ToString()));
        Assert.Null(classic.Mods.Problem);
        // and the dispatcher of the game's cards runs on from there
        Assert.True(new CardDispatcher().HasScript(classic.State.CurrentCard));
        // the original reader takes the file as it takes any (no companion involved)
        var original = OriginalSaves.Read(sav, [], name, CardHarness.World!);
        Assert.Equal(2, original.State.CurrentCard);
    }

    [Fact]
    public void AGameLoadedWithoutItsModStaysMarkedModdedWhenSavedAgain()
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var set = LoadMods(ExampleMod());
        var path = TempCopies.Create("darklands-modsave-");
        var state = CardHarness.NewGame();
        state.CurrentCard = 366;
        var first = new SaveFolder(path) { Mods = ModsOf(set) }.Save(state, "modded", true, true);
        var classicFolder = new SaveFolder(path);
        var loaded = classicFolder.Load(first, CardHarness.World!);
        var second = classicFolder.Save(loaded.State, "again", true, true);
        var json = File.ReadAllText(Path.Combine(path, ModdedSaves.CompanionOf(second)));
        Assert.Contains("\"mods\": []", json);
        Assert.Contains("\"playedWith\"", json);
        Assert.Contains("wayside-shrine", json);
        var again = classicFolder.Load(second, CardHarness.World!);
        Assert.Empty(again.Mods.MissingMods);
        Assert.Single(ModdedSaves.ModsUsed(again.State));
    }

    [Fact]
    public void ClassicWritesNoCompanionFileAndTheSaveIsAsBefore()
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var path = TempCopies.Create("darklands-save-");
        var state = CardHarness.NewGame();
        var name = new SaveFolder(path).Save(state, "plain", true, true);
        Assert.Equal(["DKSAVE0.BSV", "DKSAVE0.SAV"], Directory.GetFiles(path).Select(Path.GetFileName).Order());
        var (expected, _) = OriginalSaves.Write(CardHarness.NewGame(), "plain", true, true);
        // a save without mods is the original writer's bytes
        var sav = File.ReadAllBytes(Path.Combine(path, name.ToUpperInvariant()));
        Assert.Equal(expected, sav);
        var loaded = new SaveFolder(path).Load(name, CardHarness.World!);
        Assert.Same(ModdedLoad.None, loaded.Mods);
        Assert.Empty(ModdedSaves.ModsUsed(loaded.State));
    }

    [Fact]
    public void ACompanionFileOfAnEarlierSaveOfTheSameNameIsIgnored()
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var set = LoadMods(ExampleMod());
        var path = TempCopies.Create("darklands-modsave-");
        var state = CardHarness.NewGame();
        state.CurrentCard = 366;
        var name = new SaveFolder(path) { Mods = ModsOf(set) }.Save(state, "modded", true, true);
        // the original game deletes the .SAV (its delete knows nothing of the companion file) and saves again under that name
        var other = CardHarness.NewGame(seed: 7);
        var (sav, _) = OriginalSaves.Write(other, "by the original", true, true);
        File.WriteAllBytes(Path.Combine(path, name.ToUpperInvariant()), sav);
        var loaded = new SaveFolder(path) { Mods = ModsOf(set) }.Load(name, CardHarness.World!);
        Assert.Contains("earlier game", loaded.Mods.Problem);
        Assert.Empty(loaded.Mods.MissingMods);
        Assert.Empty(ModdedSaves.ModsUsed(loaded.State));
        Assert.Equal(other.CurrentCard, loaded.State.CurrentCard);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[1, 2]")]
    [InlineData("""{ "format": "one" }""")]
    [InlineData("""{ "format": 99 }""")]
    [InlineData("""{ "format": 1, "sha256": 5 }""")]
    public void ABrokenCompanionFileLoadsThePlainSave(string json)
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var path = TempCopies.Create("darklands-modsave-");
        var name = new SaveFolder(path).Save(CardHarness.NewGame(), "plain", true, true);
        File.WriteAllText(Path.Combine(path, ModdedSaves.CompanionOf(name)), json);
        var loaded = new SaveFolder(path).Load(name, CardHarness.World!);
        Assert.NotNull(loaded.Mods.Problem);
        Assert.Empty(loaded.Mods.MissingMods);
        Assert.Empty(ModdedSaves.ModsUsed(loaded.State));
    }

    [Fact]
    public void DeletingAModdedSaveDeletesItsCompanionFile()
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var set = LoadMods(ExampleMod());
        var path = TempCopies.Create("darklands-modsave-");
        var folder = new SaveFolder(path) { Mods = ModsOf(set) };
        var name = folder.Save(CardHarness.NewGame(), "modded", true, true);
        Assert.True(File.Exists(Path.Combine(path, ModdedSaves.CompanionOf(name))));
        Assert.True(folder.Delete(name.ToUpperInvariant(), playing: null));
        Assert.Empty(Directory.GetFiles(path));
    }

    [Fact]
    public void TheWriterNamesTheGameCardItIsGivenForAModCard()
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var state = CardHarness.NewGame();
        state.CurrentCard = 366;
        state.PreviousCard = 367;
        var (sav, _) = OriginalSaves.Write(state, "x", true, true, savedCardOf: n => n == 366 ? 2 : n == 367 ? CardDispatcher.OverlandMap : null);
        Assert.Equal(2, HeaderCards(sav)[0]);
        Assert.Equal(CardDispatcher.OverlandMap, HeaderCards(sav)[3]);
    }

    [Fact]
    public void AModdedGameNeverWritesACardAbove365EvenOneNoModKnows()
    {
        if (!CardHarness.Available)
        {
            return;
        }
        // a number nothing defines (the "not in the card table" card, saved while its message is up)
        var state = CardHarness.NewGame();
        state.CurrentCard = 999;
        state.SavedCard = 366;
        var (modded, _) = OriginalSaves.Write(state, "x", true, true, savedCardOf: n => n == 366 ? 2 : null);
        Assert.Equal(OriginalSaves.MapCard, HeaderCards(modded)[0]);
        Assert.Equal(2, HeaderCards(modded)[1]);
        Assert.All(HeaderCards(modded), n => Assert.InRange(n, 0, OriginalSaves.LastOriginalCard));
        // Classic writes every word as it is, as the original does
        var (classic, _) = OriginalSaves.Write(state, "x", true, true);
        Assert.Equal(999, HeaderCards(classic)[0]);
        Assert.Equal(366, HeaderCards(classic)[1]);

        // through the save folder of a modded game: the .SAV names the map, the companion file keeps 999
        var set = LoadMods(ExampleMod());
        var folder = new SaveFolder(TempCopies.Create("darklands-modsave-")) { Mods = ModsOf(set) };
        var name = folder.Save(state, "unknown card", true, true);
        Assert.All(HeaderCards(File.ReadAllBytes(Path.Combine(folder.Path, name.ToUpperInvariant()))), n => Assert.InRange(n, 0, OriginalSaves.LastOriginalCard));
        Assert.Contains("\"current\": 999", File.ReadAllText(Path.Combine(folder.Path, ModdedSaves.CompanionOf(name))));
        // read back with the mod: 999 is no card it has, so the .SAV's map stays
        Assert.Equal(OriginalSaves.MapCard, folder.Load(name, CardHarness.World!).State.CurrentCard);
    }
}
