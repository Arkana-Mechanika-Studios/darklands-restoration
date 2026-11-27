using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Darklands.Architecture.Tests;

/// <summary>
/// The guard of GitHub issue 5: a card fills its text slots ($NamedOneName .. $NamedFiveName, $Text1 .. $Text4) only through the
/// routes a translation pack reaches (LocationName, ItemNameShown, SaintName, FormulaNameShown, PatronName, TableName, ExeText,
/// Word ...). A raw read of the game's data (a location's, item's or saint's .Name, a personal-name table, a written-out string)
/// would stay English in a translated game while the words around it are the pack's ("船至Kassel").
/// </summary>
public sealed class CardNameFillGuardTests
{
    // a raw string of the game's data: the record fields and the untranslated readers
    private static readonly HashSet<string> RawMembers = new(StringComparer.Ordinal) { "Name", "FullName", "ShortName" };

    private static readonly string[] RawCalls = ["PersonalNames.Name", "World.ItemName", "World.Words.Text", "World.FileLocationName"];

    // the slots' fills a pack cannot reach yet, each with its reason (none since the burnt ship's name, DS:6967, has its key)
    private static readonly Dictionary<string, string> Known = new(StringComparer.Ordinal);

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Darklands.slnx")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new InvalidOperationException("no repository root");
    }

    private static IEnumerable<string> CardSources()
    {
        var folder = Path.Combine(Root(), "src", "Darklands.Cards");
        var sep = Path.DirectorySeparatorChar;
        return Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{sep}obj{sep}", StringComparison.Ordinal) && !f.Contains($"{sep}bin{sep}", StringComparison.Ordinal));
    }

    private static bool IsSlot(ExpressionSyntax left) =>
        left is ElementAccessExpressionSyntax { Expression: var e } && e.ToString() is var s
        && (s.EndsWith("Variables.Named", StringComparison.Ordinal) || s.EndsWith("Variables.Texts", StringComparison.Ordinal));

    /// <summary>The raw reads of one slot fill's value.</summary>
    private static IEnumerable<string> RawReads(ExpressionSyntax value)
    {
        foreach (var node in value.DescendantNodesAndSelf())
        {
            switch (node)
            {
                case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression)
                    && !(literal.Parent is ArgumentSyntax { Parent.Parent: InvocationExpressionSyntax call } && call.Expression.ToString().EndsWith("ExeText", StringComparison.Ordinal))
                    && literal.Token.ValueText.Length > 0:
                    yield return literal.ToString();
                    break;
                case MemberAccessExpressionSyntax member when RawMembers.Contains(member.Name.Identifier.ValueText) && member.Parent is not InvocationExpressionSyntax:
                    yield return member.ToString();
                    break;
                case InvocationExpressionSyntax call when RawCalls.Any(r => call.Expression.ToString().EndsWith(r, StringComparison.Ordinal)):
                    yield return call.ToString();
                    break;
            }
        }
    }

    [Fact]
    public void TheCardsFillTheirTextSlotsOnlyThroughThePacksRoutes()
    {
        var found = new List<string>();
        var known = new HashSet<string>(StringComparer.Ordinal);
        var fills = 0;
        foreach (var file in CardSources())
        {
            var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file), path: file);
            foreach (var assignment in tree.GetRoot().DescendantNodes().OfType<AssignmentExpressionSyntax>().Where(a => IsSlot(a.Left)))
            {
                fills++;
                foreach (var raw in RawReads(assignment.Right))
                {
                    var key = $"{Path.GetFileName(file)}: {raw}";
                    if (Known.ContainsKey(key))
                    {
                        known.Add(key);
                        continue;
                    }
                    var line = assignment.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    found.Add($"{Path.GetRelativePath(Root(), file)}:{line}: {raw}");
                }
            }
        }
        // the scan sees the fills (25 files touch the slots), and each known exception is still there (or leaves this list)
        Assert.True(fills > 30, $"only {fills} slot fills seen");
        Assert.Equal(Known.Keys.Order(), known.Order());
        Assert.True(found.Count == 0, "a card fills a text slot with a raw string of the game (route it through the pack: LocationName, "
            + "ItemNameShown, SaintName, TableName, ExeText ...):" + Environment.NewLine + string.Join(Environment.NewLine, found));
    }

    [Fact]
    public void TheGuardSeesARawFill()
    {
        var tree = CSharpSyntaxTree.ParseText("""
            class X { void F(C c, int port) {
                c.Variables.Named[0] = c.State.Locations[port].Name;
                c.Variables.Texts[1] = c.PersonalNames.Name(NameTable.Titles, 4);
                c.Variables.Named[2] = "Kassel";
                c.Variables.Named[3] = c.LocationName(port);
                c.Variables.Texts[0] = c.ExeText("exe:DS:5C5D");
            } }
            """);
        var reads = tree.GetRoot().DescendantNodes().OfType<AssignmentExpressionSyntax>().Where(a => IsSlot(a.Left))
            .Select(a => RawReads(a.Right).ToList()).ToList();
        Assert.Equal(5, reads.Count);
        Assert.Equal(new[] { "c.State.Locations[port].Name" }, reads[0]);
        Assert.Equal(new[] { "c.PersonalNames.Name(NameTable.Titles, 4)" }, reads[1]);
        Assert.Equal(new[] { "\"Kassel\"" }, reads[2]);
        Assert.Empty(reads[3]);
        Assert.Empty(reads[4]);
    }
}
