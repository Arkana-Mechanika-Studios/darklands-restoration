namespace Darklands.Platform.Tests;

/// <summary>display.filter and --filter: how the host draws the picture (presentation only), sharp unless chosen.</summary>
public sealed class PictureFilterSettingsTests
{
    private static ResolvedSettings Resolve(GameSettings file, string? cli = null) =>
        ResolvedSettings.Resolve(new CommandLineOptions { Filter = cli }, _ => null, file);

    [Fact]
    public void TheDefaultIsSharpTheLookOfBefore()
    {
        Assert.Equal(PictureFilter.Sharp, Resolve(new GameSettings()).Filter);
        Assert.Equal(PictureFilter.Sharp, default(PictureFilter));
        var template = SettingsFile.Parse(SettingsFile.Template);
        Assert.Equal(PictureFilter.Sharp, Resolve(template.Settings).Filter);
    }

    [Theory]
    [InlineData("sharp", PictureFilter.Sharp)]
    [InlineData("smooth", PictureFilter.Smooth)]
    [InlineData("crt", PictureFilter.Crt)]
    [InlineData("CRT", PictureFilter.Crt)]
    [InlineData(" Smooth ", PictureFilter.Smooth)]
    public void TheFileNamesTheFilterInAnyCase(string name, PictureFilter filter)
    {
        var load = SettingsFile.Parse($$"""{ "display": { "filter": "{{name}}" } }""");
        Assert.Empty(load.Problems);
        Assert.Equal(PictureFilters.Name(filter), load.Settings.Filter);
        Assert.Equal(filter, Resolve(load.Settings).Filter);
    }

    [Fact]
    public void AnUnknownFilterInTheFileIsSaidAndLeftOut()
    {
        var load = SettingsFile.Parse("""{ "display": { "filter": "hq4x" } }""");
        Assert.Contains(load.Problems, p => p.Contains("display.filter", StringComparison.Ordinal) && p.Contains("hq4x", StringComparison.Ordinal));
        Assert.Null(load.Settings.Filter);
        Assert.Equal(PictureFilter.Sharp, Resolve(load.Settings).Filter);
        // "" is the default, without a complaint
        var empty = SettingsFile.Parse("""{ "display": { "filter": "" } }""");
        Assert.Empty(empty.Problems);
        Assert.Equal(PictureFilter.Sharp, Resolve(empty.Settings).Filter);
    }

    [Fact]
    public void TheCommandLineWinsAndAnUnknownOneStopsTheStart()
    {
        var file = new GameSettings { Filter = "crt" };
        Assert.Equal(PictureFilter.Crt, Resolve(file).Filter);
        Assert.Equal(PictureFilter.Smooth, Resolve(file, "smooth").Filter);
        var error = Assert.Throws<ArgumentException>(() => Resolve(file, "blur"));
        Assert.Contains("blur", error.Message, StringComparison.Ordinal);
        Assert.Equal("--filter", new CommandLineOptions { Filter = "crt" }.Overrides(_ => null)["display.filter"]);
        Assert.False(new CommandLineOptions().Overrides(_ => null).ContainsKey("display.filter"));
    }

    [Fact]
    public void TheFiltersCycleAndEveryOneHasANameAndALabel()
    {
        Assert.Equal(PictureFilter.Smooth, PictureFilters.Next(PictureFilter.Sharp));
        Assert.Equal(PictureFilter.Crt, PictureFilters.Next(PictureFilter.Smooth));
        Assert.Equal(PictureFilter.Sharp, PictureFilters.Next(PictureFilter.Crt));
        foreach (var filter in Enum.GetValues<PictureFilter>())
        {
            Assert.Equal(filter, PictureFilters.Parse(PictureFilters.Name(filter)));
            Assert.False(string.IsNullOrWhiteSpace(PictureFilters.Label(filter)));
            Assert.Contains($"\"{PictureFilters.Name(filter)}\"", SettingsFile.Template, StringComparison.Ordinal);
        }
        Assert.Equal(Enum.GetValues<PictureFilter>().Length, PictureFilters.Names.Count);
    }
}
