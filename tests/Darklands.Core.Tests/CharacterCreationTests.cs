using Darklands.Core.Characters;
using Darklands.Core.Characters.Creation;
using Darklands.Core.Items;
using Darklands.Core.Randomness;

namespace Darklands.Core.Tests;

/// <summary>The character generator's rules (R037 18c8) on synthetic tables.</summary>
public sealed class CharacterCreationTests
{
    internal static CreationTables Tables()
    {
        static Occupation Entry(int index, int experience, int[] attributes, int skill, int limit) =>
            new(index, $"entry {index}", experience, attributes, Enumerable.Repeat(skill, 19).ToArray(), Enumerable.Repeat(limit, 19).ToArray(), 0);
        var backgrounds = Enumerable.Range(0, 6).Select(i => Entry(i, 90 + i, [1, 1, 0, 1, 0, 1], 1, 0)).ToList();
        var occupations = Enumerable.Range(0, 37).Select(i => Entry(i, 10 + i, [1, 1, 1, 1, 1, 1], 1, 5)).ToList();
        var aging = Enumerable.Range(0, 9).Select(k => (IReadOnlyList<int>)Enumerable.Repeat(-k, 6).ToArray()).ToList();
        var names = new NameTables(["Hans", "Maximilianus"], ["Greta", "Anna"], ["Fugger", "of Zollern"], ["Imhof"]);
        return new CreationTables(backgrounds, occupations, aging, names);
    }

    internal static List<ItemDefinition> Items() =>
        [.. Enumerable.Range(0, 200).Select(c => new ItemDefinition(c, $"item {c}", "", c & 0xFF, 0, 2, 20, 1))];

    // the generator screen resets (and so draws the first name) when it opens; the constructor draws nothing
    private static CharacterCreation New(uint seed = 7, uint clock = 1000)
    {
        var c = new CharacterCreation(Tables(), Items(), new MscRandom(seed), partySeed: 33, clock: () => clock);
        c.Reset();
        return c;
    }

    [Fact]
    public void ResetMakesAYoungManWithBaseAttributes()
    {
        var c = New();
        Assert.Equal(CreationStage.NameAndSex, c.Stage);
        Assert.Equal(0, c.Age);
        Assert.False(c.IsFemale);
        Assert.Equal([12, 15, 12, 12, 12, 11, 99], c.Attributes);
        Assert.All(c.Skills, s => Assert.Equal(0, s));
        Assert.True(c.NameIsRandom);
        Assert.Equal(c.Name.Split(' ')[0][..Math.Min(9, c.Name.Split(' ')[0].Length)], c.Nickname);
    }

    [Fact]
    public void RandomNameSeedsWithPartySeedPlusClockAndReseedsFromTheClock()
    {
        var names = Tables().Names;
        var random = new MscRandom(5);
        var name = RandomNames.Make(random, names, 33, 1000, NameKind.Woman, 0x12345);
        var expected = new MscRandom(33 + 1000);
        expected.Scaled(1000);
        var first = names.FemaleNames[expected.Scaled(names.FemaleNames.Count)];
        var last = names.Surnames[expected.Scaled(names.Surnames.Count)];
        Assert.Equal(first + " " + last, name);
        Assert.Equal(0x2345u, random.State);
    }

    [Fact]
    public void NicknameIsTheFirstWordUpToNineLetters()
    {
        Assert.Equal("Maximilia", RandomNames.Nickname("Maximilianus Fugger"));
        Assert.Equal("Hans", RandomNames.Nickname("Hans"));
    }

    [Fact]
    public void ChangingSexSetsTheBaseAttributesAndDrawsANameOnlyWhenRandom()
    {
        var c = New();
        c.ToggleSex();
        Assert.True(c.IsFemale);
        Assert.Equal([14, 12, 12, 12, 12, 12, 99], c.Attributes);
        Assert.Contains(c.Name.Split(' ')[0], Tables().Names.FemaleNames);
        c.SetName("Ursula Kress");
        c.ToggleSex();
        Assert.Equal("Ursula Kress", c.Name);
    }

    [Fact]
    public void ChildhoodGivesTheBackgroundsChangesAndPoints()
    {
        var c = New();
        Assert.Equal(CreationOutcome.NewStage, c.Choose(1));
        Assert.Equal(CreationStage.FamilyBackground, c.Stage);
        c.Choose(3);
        Assert.Equal(CreationStage.ChildhoodAttributes, c.Stage);
        Assert.Equal(2, c.Background);
        Assert.Equal(15, c.Age);
        Assert.Equal(92, c.Experience);
        Assert.Equal(92, c.TermExperience);
        Assert.Equal([13, 16, 12, 13, 12, 12, 99], c.Attributes);
        Assert.All(c.Skills, s => Assert.Equal(1, s));
        Assert.Equal(-1, c.LastOccupation);
    }

    [Fact]
    public void AttributePointsCostMoreFrom29And39AndStopAt40()
    {
        var c = New();
        c.Choose(1);
        c.Choose(1);
        c.SelectRow(0);
        c.Attributes[0] = 28;
        var before = c.Experience;
        Assert.True(c.Raise());
        Assert.Equal(before - 1, c.Experience);
        Assert.True(c.Raise());
        Assert.Equal(before - 3, c.Experience);
        c.Attributes[0] = 39;
        Assert.True(c.Raise());
        Assert.Equal(before - 6, c.Experience);
        Assert.False(c.Raise());
        Assert.True(c.Lower());
        Assert.Equal(before - 3, c.Experience);
        Assert.Equal(39, c.Attributes[0]);
    }

    [Fact]
    public void FirstAdultTermGivesTwoMoreSkillPointsAndTwentyMoreExperience()
    {
        var c = New();
        c.Choose(1);
        c.Choose(1);
        c.Choose(1);
        Assert.Equal(CreationStage.Occupation, c.Stage);
        var first = c.Choices[0];
        c.Choose(1);
        Assert.Equal(CreationStage.OccupationSkills, c.Stage);
        Assert.Equal(20, c.Age);
        Assert.Equal(first, c.LastOccupation);
        Assert.True(c.HasBeen(first));
        Assert.Equal(10 + first + 20, c.Experience);
        Assert.All(c.Skills, s => Assert.Equal(1 + 1 + 2, s));
        c.Choose(1);
        var second = c.Choices[0];
        c.Choose(1);
        Assert.Equal(25, c.Age);
        Assert.Equal(10 + second + 5, c.Experience);
        Assert.All(c.Skills, s => Assert.Equal(5, s));
        Assert.Equal(first, c.PreviousOccupation);
    }

    [Fact]
    public void AgingFromThirtyAndNoEnduranceOrStrengthGainAfterForty()
    {
        var c = New();
        c.Age = 25;
        Array.Fill(c.Attributes, 50, 0, 6);
        c.ServeTerm(20);
        Assert.Equal(30, c.Age);
        Assert.All(c.Attributes.Take(6), a => Assert.Equal(51, a));
        c.Age = 45;
        Array.Fill(c.Attributes, 50, 0, 6);
        c.ServeTerm(20);
        Assert.Equal(50, c.Age);
        Assert.Equal([46, 46, 47, 47, 47, 47], c.Attributes.Take(6));
    }

    [Fact]
    public void SkillPointsRespectTheOccupationsLimitAndCost()
    {
        var c = New();
        c.Choose(1);
        c.Choose(1);
        c.Choose(1);
        c.Choose(1);
        c.SelectRow(8);
        Assert.Equal(7, CharacterCreation.SkillOfRow(8));
        for (var k = 0; k < 5; k++)
        {
            Assert.True(c.Raise());
        }
        Assert.False(c.Raise());
        Assert.Equal(5, c.SkillPointsSpent[7]);
        Assert.Equal(1, CharacterCreation.SkillCost(48));
        Assert.Equal(2, CharacterCreation.SkillCost(49));
        Assert.Equal(3, CharacterCreation.SkillCost(79));
    }

    [Fact]
    public void ArrowsSkipTheGapsBetweenSkillGroups()
    {
        var c = New();
        c.Choose(1);
        c.Choose(1);
        c.Choose(1);
        c.Choose(1);
        c.SelectRow(6);
        c.MoveCursor(1);
        Assert.Equal(8, c.Cursor);
        c.MoveCursor(-1);
        Assert.Equal(6, c.Cursor);
        c.SelectRow(0);
        c.MoveCursor(-1);
        Assert.Equal(20, c.Cursor);
        c.MoveCursor(1);
        Assert.Equal(0, c.Cursor);
    }

    [Fact]
    public void EachTermReplacesTheKitAndSameItemsStack()
    {
        var c = New();
        c.Age = 20;
        c.ServeTerm(0);
        Assert.Equal([0x28, 0x2F], c.Kit.Select(s => s.Code));
        c.ServeTerm(0x21);
        Assert.Equal([0x26, 0x2F], c.Kit.Select(s => s.Code));
        Assert.Equal(new ItemStack(0x26, 0x26, 20, 1, 2), c.Kit[0]);
        c.ServeTerm(0x0D);
        Assert.Empty(c.Kit);
    }

    [Fact]
    public void ClericsLearnTheMirroredSaintOfTheDraw()
    {
        var c = New();
        c.Age = 20;
        var probe = new MscRandom(99);
        var n = probe.Below(0x88);
        ((MscRandom)c.Random).Seed(99);
        c.ServeTerm(8);
        Assert.Equal([(n & ~7) | (7 - (n & 7))], c.Saints);
    }

    [Fact]
    public void FormulasAreAByteIncrement()
    {
        var c = New();
        c.Age = 20;
        var probe = new MscRandom(4);
        var a = probe.Below(22);
        var b = probe.Below(22);
        ((MscRandom)c.Random).Seed(4);
        c.ServeTerm(0x1A);
        Assert.Equal(a == b ? 2 : 1, c.FormulaBytes[a]);
    }

    [Fact]
    public void BeginningAdventuringAddsAWeaponAndPotionsAndMakesTheCharacter()
    {
        var c = New();
        c.Choose(1);
        c.Choose(1);
        c.Choose(1);
        c.Choose(1);
        c.Skills[3] = 40;
        c.FormulaBytes[2] = 3;
        c.FormulaBytes[5] = 8;
        Assert.Equal(CreationOutcome.Finished, c.Choose(2));
        var made = c.Finished!;
        Assert.Contains(made.Pack, s => s.Code == 0x15);
        Assert.Contains(made.Pack, s => s.Code == Potions.FirstPotion + 2);
        Assert.Contains(made.Pack, s => s.Code == Potions.FirstPotion + 5);
        Assert.Equal([6, 7], made.Formulas.Order());
        Assert.Equal(c.Attributes, made.AttributeMaximums);
        Assert.Equal(CharacterStatus.Able, made.Status);
        Assert.Equal('A', made.ShieldLetter);
    }

    [Fact]
    public void EscapeFromChildhoodAttributesLetsABackgroundRowServeAnAdultOccupation()
    {
        var c = New();
        c.Choose(1);
        c.Choose(1);
        Assert.Equal(CreationOutcome.NewStage, c.Back());
        Assert.Equal(CreationStage.FamilyBackground, c.Stage);
        c.Choose(4);
        Assert.Equal(CreationStage.OccupationSkills, c.Stage);
        Assert.Equal(3, c.LastOccupation);
        Assert.Equal(20, c.Age);
        Assert.Equal(CreationOutcome.Left, New().Back());
    }

    [Fact]
    public void KillCharacterStartsAgainAndNextOccupationClearsThePointsSpent()
    {
        var c = New();
        c.Choose(1);
        c.Choose(1);
        c.Choose(1);
        c.Choose(1);
        c.SelectRow(0);
        c.Raise();
        c.Choose(1);
        Assert.All(c.SkillPointsSpent, s => Assert.Equal(0, s));
        Assert.Equal(CreationStage.Occupation, c.Stage);
        c.Choose(1);
        c.Choose(3);
        Assert.Equal(CreationStage.NameAndSex, c.Stage);
        Assert.Equal(0, c.Age);
    }

    [Fact]
    public void AStaleCursorInTheChildhoodAttributesRaisesWhatFollowsTheArrays()
    {
        var c = New();
        c.Choose(1);
        c.Choose(1);
        c.Choose(1);
        c.Choose(1);
        Assert.Equal(CreationStage.OccupationSkills, c.Stage);
        c.SelectRow(12);
        c.Choose(3);                                   // kill the character: the cursor stays 12
        Assert.Equal(CreationStage.NameAndSex, c.Stage);
        c.Choose(1);
        c.Choose(1);
        Assert.Equal(CreationStage.ChildhoodAttributes, c.Stage);
        Assert.Equal(12, c.Cursor);
        var bows = c.Skills[5];
        var points = c.Experience;
        // row 12: DS:8C06[12] is the screen state (2) and DS:E86E[12] is skill 5: the raise moves the screen on to state 3
        Assert.True(c.Raise());
        Assert.Equal(CreationStage.Occupation, c.Stage);
        Assert.Equal(bows + 1, c.Skills[5]);
        Assert.Equal(points - CharacterCreation.AttributeCost(bows), c.Experience);
    }

    [Fact]
    public void WithNoRowUnderThePointerThePanelShowsTheEntryTheRecordBufferNames()
    {
        var c = New();
        c.Choose(1);
        Assert.Equal(CreationStage.FamilyBackground, c.Stage);
        Assert.Equal(0, c.ShownEntry(-1));             // the buffer is 0 until a roster list was drawn
        c.RecordBufferWord = 10;                       // the last listed character carried 10 stacks
        Assert.Equal(10, c.ShownEntry(-1));
        // background 10 lies past the six: the occupations' table follows, so it is occupation 4
        Assert.Equal(c.Tables.Occupations[4].Experience, c.ShownExperience(10));
    }

    [Fact]
    public void EligibilityFollowsTheOriginalRules()
    {
        var c = New();
        c.Age = 15;
        c.Background = 0;
        var list = OccupationEligibility.Available(c);
        Assert.Contains(0, list);
        Assert.Contains(4, list);
        Assert.DoesNotContain(1, list);
        Assert.True(list.Count <= OccupationEligibility.MaxChoices);
        c.Age = 30;
        c.Attributes[(int)Characters.Attribute.Intelligence] = 35;
        c.SetTaken(25, true);
        Assert.Contains(26, OccupationEligibility.Available(c));
        c.Attributes[(int)Characters.Attribute.Intelligence] = 34;
        Assert.DoesNotContain(26, OccupationEligibility.Available(c));
    }

    [Fact]
    public void SpentGaugeIsHalfThePercentSpent()
    {
        var c = New();
        c.Choose(1);
        c.Choose(1);
        Assert.Equal(0, c.SpentGauge);
        c.Experience = c.TermExperience / 2;
        Assert.Equal(25, c.SpentGauge);
    }
}
