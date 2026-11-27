using System.Text.RegularExpressions;
using Darklands.Presentation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Darklands.Architecture.Tests;

/// <summary>
/// The L-9 guard of the 1.2 view registry (step 9): a classic screen that declares its slots (ScreenLayouts) hit-tests only with
/// them: none of its source files compares a pointer coordinate (a click's or a release's x or y, the mouse's) with a number or
/// divides it by one; its lists and areas answer (SlotLayout.RowAt / Contains). A screen joins the guard when it declares its slots.
/// </summary>
public sealed partial class ScreenSlotGuardTests
{
    // a pointer coordinate: releaseX, mouse.Y, click.X, ui.Mouse.Y, action.X, pick.Y, pointer.X ... (also after Laid.Map: lx / ly are its results)
    [GeneratedRegex(@"(?i)\b(release|mouse|click|pick|pointer|action)\w*\.?[XY]\b")]
    private static partial Regex PointerCoordinate();

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Darklands.slnx")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new InvalidOperationException("no repository root");
    }

    // the files a screen's hit tests live in besides its own: the shops' common page
    private static readonly Dictionary<string, string[]> SharedFiles = new(StringComparer.Ordinal)
    {
        ["BarterScreen"] = ["BuySellPage"],
        ["ItemStoreScreen"] = ["BuySellPage"],
        ["LootScreen"] = ["BuySellPage"],
    };

    private static IEnumerable<string> FilesOf(string screen) =>
        new[] { screen }.Concat(SharedFiles.GetValueOrDefault(screen) ?? []).SelectMany(OwnFilesOf).Distinct();

    private static IEnumerable<string> OwnFilesOf(string screen)
    {
        var folder = Path.Combine(Root(), "src", "Darklands.Presentation");
        return Directory.EnumerateFiles(folder, screen + ".cs", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(folder, screen + ".*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
    }

    [Fact]
    public void EveryScreenWithSlotsHasAnEntryAndItsSourceFiles()
    {
        Assert.NotEmpty(ScreenLayouts.ByScreen);
        foreach (var screen in ScreenLayouts.ByScreen.Keys)
        {
            Assert.NotNull(ViewCatalogue.Find(screen));
            Assert.NotEmpty(FilesOf(screen));
        }
    }

    [Fact]
    public void AScreenWithSlotsHitTestsOnlyWithThem()
    {
        var found = new List<string>();
        foreach (var screen in ScreenLayouts.ByScreen.Keys)
        {
            foreach (var file in FilesOf(screen))
            {
                var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file), path: file);
                foreach (var binary in tree.GetRoot().DescendantNodes().OfType<BinaryExpressionSyntax>())
                {
                    if (!binary.IsKind(SyntaxKind.LessThanExpression) && !binary.IsKind(SyntaxKind.LessThanOrEqualExpression)
                        && !binary.IsKind(SyntaxKind.GreaterThanExpression) && !binary.IsKind(SyntaxKind.GreaterThanOrEqualExpression)
                        && !binary.IsKind(SyntaxKind.DivideExpression))
                    {
                        continue;
                    }
                    if (PointerCoordinate().IsMatch(binary.Left.ToString()) || PointerCoordinate().IsMatch(binary.Right.ToString()))
                    {
                        var line = binary.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                        found.Add($"{Path.GetFileName(file)}:{line}: {binary}");
                    }
                }
            }
        }
        Assert.True(found.Count == 0, "hit tests outside the screen's slots (L-9):\n" + string.Join("\n", found));
    }

    [Fact]
    public void TheGuardSeesAConstantHitTest()
    {
        // the guard's own check: the original form of the marching order's test is caught
        var tree = CSharpSyntaxTree.ParseText("class A { void B(int releaseX, int releaseY) { if (releaseX < 60) { var r = releaseY / 40; } } }");
        var caught = tree.GetRoot().DescendantNodes().OfType<BinaryExpressionSyntax>()
            .Count(b => PointerCoordinate().IsMatch(b.Left.ToString()) || PointerCoordinate().IsMatch(b.Right.ToString()));
        Assert.Equal(2, caught);
    }
}
