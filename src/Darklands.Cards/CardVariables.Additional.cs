namespace Darklands.Cards;

/// <summary>More values a card can put into its texts: the support words and the city place.</summary>
public sealed partial class CardVariables
{
    /// <summary>
    /// The words for $Support … $Support6: −1 "oppose", 0 "are undecided about", 1 "support"
    /// (<c>text-slot-support-zero..6</c>, the side of a guild or a lord; an index into the support-word table, which starts at −1).
    /// </summary>
    // orig: text-slot-support-zero..text-slot-support-six, DGROUP ints read by case 59..65 of R043+0x1316 (150B:0886) draft; data; tests: TextAndOpeningTests
    public int[] Support { get; } = new int[7];

    /// <summary>Which of the city's 16 names $CityLocation stands for (index into the working city record's name table).</summary>
    // orig: DS:ED52 (DGROUP int), case 52 of R043+0x1316: city-record->names[DS:ED52]; data
    // C: the name "city location" for DS:ED52 is the variable's text name ($CityLocation). Nothing writes it: the one instruction that names DS:ED52 is this read
    //   (R043+0x13CE), no word 0xED52 is in the EXE (no pointer to it), and the tables and buffers below it (the handler table ending at DS:ED45, $Money5 at DS:ED46, $Number1/$Number2 at DS:ED4C/ED50) end before it, so it keeps the 0 of the start-up BSS clear (names[0]).
    public int CityLocation { get; set; }
}
