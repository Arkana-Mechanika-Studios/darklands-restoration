using System.Text.Json;
using System.Text.Json.Nodes;
using Darklands.Core.Randomness;

namespace Darklands.Oracle.Tests.Fuzz;

/// <summary>The generator fuzzer's pieces (GenFuzz): the draw count of a scenario, the mutations, the edge buckets.</summary>
public sealed class GenFuzzTests
{
    [Fact]
    public void ACountedScenarioKnowsItsDrawsAndForcesAsBefore()
    {
        var plain = new MscRandom(8);
        var expected = new[] { plain.NextRaw(), plain.NextRaw(), plain.NextRaw() };
        ScenarioDraws.Counting = true;
        try
        {
            using var none = JsonDocument.Parse("""{"seed": 8}""");
            var random = new MscRandom(8);
            using (ScenarioDraws.Begin(random, none.RootElement))
            {
                Assert.Equal(expected, new[] { random.NextRaw(), random.NextRaw(), random.NextRaw() });
            }
            Assert.Equal(3, ScenarioDraws.LastCount);
            using var forced = JsonDocument.Parse("""{"seed": 8, "force": {"1": 4321}}""");
            var again = new MscRandom(8);
            using (ScenarioDraws.Begin(again, forced.RootElement))
            {
                Assert.Equal(expected[0], again.NextRaw());
                Assert.Equal(4321, again.NextRaw());
            }
            Assert.Equal(2, ScenarioDraws.LastCount);
        }
        finally
        {
            ScenarioDraws.Counting = false;
        }
        using var empty = JsonDocument.Parse("""{"seed": 8}""");
        Assert.Null(ScenarioDraws.Begin(new MscRandom(8), empty.RootElement));
    }

    [Theory]
    [InlineData("castle")]
    [InlineData("mine")]
    [InlineData("wild")]
    public void MutantsKeepTheScenarioShapeAndAreReproducible(string kind)
    {
        var parent = JsonNode.Parse("""
            {"bf": 5, "seed": 100, "specs": [[34, 0, 9], [18, 4, 1], [-1, 0, 0]], "cleared": 0, "lastLocation": -1, "location": 7, "lore": 2,
             "forceMap": -1, "forceSide": -1, "terrain": 3, "month": 4, "party": [1, 1, 1, 0, 0], "ableMembers": 3}
            """)!.AsObject();
        var a = new Random(5);
        var b = new Random(5);
        for (var i = 0; i < 300; i++)
        {
            var x = GenFuzz.Mutate(kind, parent, 700, a);
            Assert.Equal(x.ToJsonString(), GenFuzz.Mutate(kind, parent, 700, b).ToJsonString());
            Assert.Equal(3, x["specs"]!.AsArray().Count);
            Assert.InRange(x["lore"]!.GetValue<int>(), 1, 10);
            Assert.InRange(x["seed"]!.GetValue<int>(), 0, 0xFFFF);
            if (x["force"] is JsonObject force)
            {
                foreach (var (k, v) in force)
                {
                    Assert.InRange(int.Parse(k, System.Globalization.CultureInfo.InvariantCulture), 0, 700);
                    Assert.InRange(v!.GetValue<int>(), 0, 32767);
                }
            }
            if (kind == "wild")
            {
                Assert.Equal(x["party"]!.AsArray().Count(p => p!.GetValue<int>() == 1), x["ableMembers"]!.GetValue<int>());
            }
        }
        Assert.Equal(5, parent["bf"]!.GetValue<int>());          // the parent is left as it was
        Assert.Null(parent["force"]);
    }

    [Fact]
    public void EdgeCountsFallIntoTheAflBuckets()
    {
        Assert.Equal([0, 1, 2, 3, 3, 4, 5, 6, 7], new[] { 1, 2, 3, 4, 7, 8, 16, 32, 128 }.Select(EdgeMap.Bucket));
        Assert.Null(EdgeMap.Attach());             // the committed build is not instrumented
    }
}
