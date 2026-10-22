namespace Darklands.Cards;

/// <summary>More values a card can put into its texts: the support words and the city place.</summary>
public sealed partial class CardVariables
{
    /// <summary>
    /// The words for $Support … $Support6: −1 "oppose", 0 "are undecided about", 1 "support"
    /// (<c>var_support0..6</c>, the side of a guild or a lord; an index into the support-word table, which starts at −1).
    /// </summary>
    // orig: var_support0..var_support6, DGROUP ints read by case 59..65 of R043+0x1316 (150B:0886) draft; data; tests: TextAndOpeningTests
    public int[] Support { get; } = new int[7];

    /// <summary>Which of the city's 16 names $CityLocation stands for (index into the working city record's name table).</summary>
    // orig: DS:ED52 (DGROUP int), case 52 of R043+0x1316: city_rec->names[DS:ED52]; data
    // C: the name "city location" for DS:ED52 is the variable's text name ($CityLocation); which scripts set it is not established.
    public int CityLocation { get; set; }
}
