using System.Diagnostics;
using System.Text;

namespace Darklands.Localization.Tests;

public class FluentLocalizerTests
{
    private static OriginalTable Original() => new(new Dictionary<string, string>
    {
        ["rp:greet"] = "Welcome, traveller.",
        ["rp:rest"] = "Just Relax",
        ["rp:only-original"] = "Only here",
        ["exe:DS:0001"] = "Plain original",
    });

    private static FluentLocalizer Make(string locale, string ftl, IReadOnlyDictionary<string, FluentFunctionImpl>? fns = null)
    {
        var l = new FluentLocalizer(locale, Original(), fns);
        var problems = l.AddOverlay("test.ftl", ftl);
        Assert.Empty(problems);
        return l;
    }

    [Fact]
    public void Translated_key_wins_over_original()
    {
        var l = Make("de", "rp-greet = Willkommen, Reisender.\n");
        Assert.Equal("Willkommen, Reisender.", l.Get("rp:greet"));
        Assert.True(l.IsTranslated("rp:greet"));
    }

    [Fact]
    public void Missing_key_falls_back_to_original_and_is_reported()
    {
        var l = Make("de", "rp-greet = Hallo\n");
        Assert.Equal("Only here", l.Get("rp:only-original"));
        Assert.Equal("Only here", l.Get("rp:only-original"));
        Assert.False(l.IsTranslated("rp:only-original"));
        Assert.Equal("nowhere:key", l.Get("nowhere:key"));

        var report = l.Missing.Snapshot();
        Assert.Contains(new MissingKeyEntry("rp:only-original", MissingKind.Untranslated, 2), report);
        Assert.Contains(new MissingKeyEntry("nowhere:key", MissingKind.Unknown, 1), report);
        Assert.Contains("nowhere:key", l.Missing.ToReport());
    }

    [Fact]
    public void Original_text_is_not_interpreted_as_fluent()
    {
        var table = new OriginalTable(new Dictionary<string, string> { ["rp:x"] = "b{se { $nope } |" });
        var l = new FluentLocalizer("de", table);
        Assert.Equal("b{se { $nope } |", l.Get("rp:x"));
    }

    [Fact]
    public void Variables_are_substituted_without_isolation_marks()
    {
        var l = Make("en", "rp-greet = Welcome, { $name }! You carry { $count } coins.\n");
        var text = l.Get("rp:greet", ("name", "Hans"), ("count", 12));
        Assert.Equal("Welcome, Hans! You carry 12 coins.", text);
    }

    [Fact]
    public void Plural_selector_follows_the_locale()
    {
        const string en = "rp-potions = { $n ->\n    [one] { $n } potion\n   *[other] { $n } potions\n}\n";
        var l = Make("en", en);
        Assert.Equal("1 potion", l.Get("rp:potions", ("n", 1)));
        Assert.Equal("3 potions", l.Get("rp:potions", ("n", 3)));

        const string ru = "rp-potions = { $n ->\n    [one] { $n } one\n    [few] { $n } few\n   *[other] { $n } many\n}\n";
        var r = Make("ru", ru);
        Assert.Equal("21 one", r.Get("rp:potions", ("n", 21)));
        Assert.Equal("3 few", r.Get("rp:potions", ("n", 3)));
        Assert.Equal("5 many", r.Get("rp:potions", ("n", 5)));
    }

    [Fact]
    public void Gender_selector_on_a_string_argument()
    {
        const string ftl = "rp-barters = { $who ->\n    [she] She barters.\n   *[other] He barters.\n}\n";
        var l = Make("en", ftl);
        Assert.Equal("She barters.", l.Get("rp:barters", ("who", "she")));
        Assert.Equal("He barters.", l.Get("rp:barters", ("who", "he")));
    }

    [Fact]
    public void Key_and_option_attributes()
    {
        const string ftl = "rp-rest = Nur ausruhen\n    .key = n\n    .option-1 = Erste\n    .option-2 = Zweite { $who }\n";
        var l = Make("de", ftl);
        Assert.Equal("Nur ausruhen", l.Get("rp:rest"));
        Assert.Equal("n", l.GetAttribute("rp:rest", "key"));
        Assert.Equal("Zweite Ida", l.GetAttribute("rp:rest", "option-2", new Dictionary<string, object?> { ["who"] = "Ida" }));
        Assert.Null(l.GetAttribute("rp:rest", "option-9"));
        Assert.Null(l.GetAttribute("rp:greet", "key"));
    }

    [Fact]
    public void Message_with_only_attributes_still_uses_the_original_text()
    {
        var l = Make("de", "rp-rest =\n    .key = n\n");
        Assert.Equal("Just Relax", l.Get("rp:rest"));
        Assert.Equal("n", l.GetAttribute("rp:rest", "key"));
    }

    [Fact]
    public void Custom_functions_can_be_called()
    {
        var fns = new Dictionary<string, FluentFunctionImpl>
        {
            ["OPTION"] = args => "<opt" + Convert.ToInt32(args[0]) + ">",
        };
        var l = Make("en", "rp-card = Pick { OPTION(2) } now\n", fns);
        Assert.Equal("Pick <opt2> now", l.Get("rp:card"));
    }

    [Fact]
    public void Parse_errors_are_reported_with_file_and_line_and_good_entries_survive()
    {
        var l = new FluentLocalizer("de", Original());
        var problems = l.AddOverlay("pack/de/main.ftl", "rp-greet = Hallo\n\nthis is not valid\n\nrp-rest = Ruhe\n");
        Assert.NotEmpty(problems);
        var p = problems[0];
        Assert.Equal("pack/de/main.ftl", p.Source);
        Assert.Equal(3, p.Line);
        Assert.Contains("pack/de/main.ftl:3", p.ToString());
        Assert.Equal("Hallo", l.Get("rp:greet"));
        Assert.Equal("Ruhe", l.Get("rp:rest"));
        Assert.NotEmpty(l.Diagnostics);
    }

    [Fact]
    public void A_parse_error_on_the_first_line_is_line_one()
    {
        var l = new FluentLocalizer("de", Original());
        var problems = l.AddOverlay("x.ftl", "bad first\nrp-greet = Hallo\n");
        Assert.Equal(1, problems[0].Line);
    }

    [Fact]
    public void Duplicate_ids_are_reported_and_the_first_is_kept()
    {
        var l = new FluentLocalizer("de", Original());
        Assert.Empty(l.AddOverlay("a.ftl", "rp-greet = Eins\n"));
        Assert.NotEmpty(l.AddOverlay("b.ftl", "rp-greet = Zwei\n"));
        Assert.Equal("Eins", l.Get("rp:greet"));
    }

    [Fact]
    public void A_missing_variable_does_not_throw_and_is_logged()
    {
        var l = Make("en", "rp-greet = Hi { $name }\n");
        var text = l.Get("rp:greet");
        Assert.False(string.IsNullOrEmpty(text));
        Assert.NotEmpty(l.Diagnostics);
    }

    [Fact]
    public void Pseudo_locale_transforms_original_and_translated_text_but_keeps_variables()
    {
        var l = new FluentLocalizer(PseudoLocale.Tag, Original());
        l.AddOverlay("qps.ftl", "rp-greet = Hello { $name }, welcome\n    .key = h\n");
        var text = l.Get("rp:greet", ("name", "Hans"));
        Assert.StartsWith("[", text);
        Assert.EndsWith("]", text);
        Assert.Contains("Hans", text);
        Assert.DoesNotContain("Hello", text);
        Assert.True(text.Length > "Hello Hans, welcome".Length * 1.3, text);
        Assert.Equal("h", l.GetAttribute("rp:greet", "key"));

        var fallback = l.Get("rp:only-original");
        Assert.StartsWith("[", fallback);
        Assert.DoesNotContain("Only", fallback);
    }

    [Fact]
    public void Original_localizer_answers_from_the_table()
    {
        ILocalizer l = new OriginalLocalizer(Original());
        Assert.Equal("Plain original", l.Get("exe:DS:0001"));
        Assert.Equal("missing:one", l.Get("missing:one"));
        Assert.Single(l.Missing.Snapshot());
    }

    [Fact]
    public void Five_thousand_lookups_are_fast()
    {
        var pairs = Enumerable.Range(0, 5000).Select(i => KeyValuePair.Create("exe:DS:" + i.ToString("X4"), "Text " + i));
        var table = new OriginalTable(pairs);
        var l = new FluentLocalizer("en", table);
        var sb = new StringBuilder();
        for (var i = 0; i < 5000; i += 2)
        {
            sb.Append("exe-ds-").Append(i.ToString("x4")).Append(" = Uebersetzt { $n } ").Append(i).Append('\n');
        }

        Assert.Empty(l.AddOverlay("big.ftl", sb.ToString()));
        var args = new Dictionary<string, object?> { ["n"] = 7 };
        _ = l.Get("exe:DS:0000", args); // warm up

        var sw = Stopwatch.StartNew();
        for (var i = 0; i < 5000; i++)
        {
            var text = l.Get("exe:DS:" + i.ToString("X4"), args);
            Assert.NotEmpty(text);
        }

        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 2000, "5000 lookups took " + sw.ElapsedMilliseconds + " ms");
    }

    [Fact]
    public void Concurrent_lookups_are_safe()
    {
        var l = Make("en", "rp-greet = Hi { $name }\n");
        var args = new Dictionary<string, object?> { ["name"] = "Ida" };
        Parallel.For(0, 2000, _ => Assert.Equal("Hi Ida", l.Get("rp:greet", args)));
    }
}
