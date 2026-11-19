namespace Darklands.Localization.Tests;

public class KeyManglingTests
{
    [Theory]
    [InlineData("exe:DS:4F48", "exe-ds-4f48")]
    [InlineData("exe:290E:01A0", "exe-290e-01a0")]
    [InlineData("exe:R012+00A0", "exe-r012-00a0")]
    [InlineData("msg:$CITYG04#3", "msg-cityg04-3")]
    [InlineData("lst:item/42.name", "lst-item-42-name")]
    [InlineData("cty:2.political-center", "cty-2-political-center")]
    [InlineData("rp:camp.spend-day", "rp-camp-spend-day")]
    [InlineData("rp:a_b", "rp-a_b")]
    [InlineData("mod:fehde:card.3.hint", "mod-fehde-card-3-hint")]
    public void Known_forms_mangle_as_documented(string key, string id) =>
        Assert.Equal(id, KeyMangling.ToFluentId(key));

    [Theory]
    [InlineData("")]
    [InlineData("1abc")]
    [InlineData(":x")]
    [InlineData("rp:with space")]
    [InlineData("rp:äö")]
    public void Bad_keys_are_rejected(string key) =>
        Assert.Throws<ArgumentException>(() => KeyMangling.ToFluentId(key));

    [Fact]
    public void Collisions_are_found()
    {
        var groups = KeyMangling.Collisions(["rp:a.b", "rp:a-b", "rp:a_b", "msg:$X#1", "msg:X#1", "lst:item/1.name"]);
        Assert.Equal(2, groups.Count);
        Assert.Contains(groups, g => g.Order().SequenceEqual(["rp:a-b", "rp:a.b"]));
        Assert.Contains(groups, g => g.Order().SequenceEqual(["msg:$X#1", "msg:X#1"]));
    }

    [Fact]
    public void Ids_are_fluent_identifiers()
    {
        foreach (var key in new[] { "exe:DS:4F48", "msg:$CITYG04#3", "snt:saint/32.desc", "enm:template/17.name" })
        {
            Assert.Matches("^[a-z][a-z0-9_-]*$", KeyMangling.ToFluentId(key));
        }
    }
}
