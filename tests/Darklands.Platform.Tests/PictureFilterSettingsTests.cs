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
    [InlineData("smooth", PictureFilter.Even)]
    [InlineData("crt", PictureFilter.Crt)]
    [InlineData("CRT", PictureFilter.Crt)]
    [InlineData(" Smooth ", PictureFilter.Even)]
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
        Assert.Equal(PictureFilter.Even, Resolve(file, "smooth").Filter);
        var error = Assert.Throws<ArgumentException>(() => Resolve(file, "blur"));
        Assert.Contains("blur", error.Message, StringComparison.Ordinal);
        Assert.Equal("--filter", new CommandLineOptions { Filter = "crt" }.Overrides(_ => null)["display.filter"]);
        Assert.False(new CommandLineOptions().Overrides(_ => null).ContainsKey("display.filter"));
    }

    [Fact]
    public void TheFiltersCycleAndEveryOneHasANameAndALabel()
    {
        // the order of the launcher and Ctrl+F11: sharp, even, soft, softcrt, hq, crt, crtplus, then sharp again
        var seen = new List<PictureFilter> { PictureFilter.Sharp };
        for (var i = 0; i < 7; i++)
        {
            seen.Add(PictureFilters.Next(seen[^1]));
        }
        Assert.Equal([PictureFilter.Sharp, PictureFilter.Even, PictureFilter.Soft, PictureFilter.SoftCrt, PictureFilter.Hq, PictureFilter.Crt,
            PictureFilter.CrtPlus, PictureFilter.Sharp], seen);
        Assert.Equal(["sharp", "even", "soft", "softcrt", "hq", "crt", "crtplus"], PictureFilters.Names);
        // the shader-only filters and what is drawn without the shader; the CRT kinds (curvature)
        Assert.Equal([PictureFilter.Soft, PictureFilter.SoftCrt, PictureFilter.Hq, PictureFilter.CrtPlus],
            Enum.GetValues<PictureFilter>().Where(PictureFilters.NeedsShader));
        Assert.Equal(PictureFilter.Even, PictureFilters.WithoutShader(PictureFilter.Soft));
        Assert.Equal(PictureFilter.Even, PictureFilters.WithoutShader(PictureFilter.Hq));
        Assert.Equal(PictureFilter.Crt, PictureFilters.WithoutShader(PictureFilter.SoftCrt));
        Assert.Equal(PictureFilter.Crt, PictureFilters.WithoutShader(PictureFilter.CrtPlus));
        Assert.Equal([PictureFilter.SoftCrt, PictureFilter.Crt, PictureFilter.CrtPlus], Enum.GetValues<PictureFilter>().Where(PictureFilters.IsCrt));
        foreach (var filter in Enum.GetValues<PictureFilter>())
        {
            Assert.Equal(filter, PictureFilters.Parse(PictureFilters.Name(filter)));
            Assert.False(string.IsNullOrWhiteSpace(PictureFilters.Label(filter)));
            Assert.StartsWith(PictureFilters.Label(filter) + ":", PictureFilters.Description(filter), StringComparison.Ordinal);
            Assert.Contains($"\"{PictureFilters.Name(filter)}\"", SettingsFile.Template, StringComparison.Ordinal);
        }
        Assert.Equal(Enum.GetValues<PictureFilter>().Length, PictureFilters.Names.Count);
    }
}
