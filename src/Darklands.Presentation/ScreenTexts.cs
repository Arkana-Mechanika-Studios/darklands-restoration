namespace Darklands.Presentation;

/// <summary>
/// The keys of the texts the screens draw (L-6): where the original's string is in DARKLAND.EXE (<c>exe:DS:XXXX</c>, each screen's own
/// copy as its code loads it), or an <c>id:rw.*</c> for the rewrite's own. The words come from <see cref="GameUi.Words(string)"/>.
/// </summary>
internal static class ScreenTexts
{
    // root 0000:13A0 exit-to-system (every "Quit to DOS?" pushes DS:0086)
    public const string QuitToDos = "exe:DS:0086";

    // the switches (root 0000:1154 saintly changes, 0000:11F2 music, 0000:129A sound effects): the name, then the switch's own "ON" / "OFF"
    public const string SoundEffects = "exe:DS:00AD";
    public const string SoundOn = "exe:DS:023A";
    public const string SoundOff = "exe:DS:023D";
    public const string Music = "exe:DS:00BC";
    public const string MusicOn = "exe:DS:0233";
    public const string MusicOff = "exe:DS:0236";
    public const string SaintlyChanges = "exe:DS:00C3";
    public const string SaintlyOn = "exe:DS:022C";
    public const string SaintlyOff = "exe:DS:022F";

    // difficulty-display root 0000:1438: "Difficulty is now set to " and the level's name
    public const string DifficultyIsNow = "exe:DS:0093";
    public const string Basic = "exe:DS:0241";
    public const string Standard = "exe:DS:0247";
    public const string Expert = "exe:DS:0250";

    // the pause box root 0000:0F2C
    public const string GamePaused = "exe:DS:020A";
    public const string PressAKey = "exe:DS:0216";

    // the save and load screens (R037)
    public const string SaveGameComment = "exe:DS:4204";
    public const string SavingGame = "exe:DS:4056";
    public const string DeleteThisGame = "exe:DS:47F0";

    /// <summary>"%s %d/%d/%d  %d": a saved game's place, year, month, day and hour in the load list (R037).</summary>
    public const string SavedGameLine = "exe:DS:4815";

    // the prayer screen (R034): the status line, the menu lines with their key letter in colour 12, the feast day's words
    public const string FavorRemaining = "exe:DS:3566";
    public const string SuccessChance = "exe:DS:3579";
    public const string MoreFavor = "exe:DS:3587";
    public const string LessFavor = "exe:DS:35A5";
    public const string PrayForParty = "exe:DS:35C3";
    public const string PrayForMember = "exe:DS:35E4";
    public const string DontPray = "exe:DS:35F8";
    public const string SaintDay = "exe:DS:3614";
    public const string SaintDayIs = "exe:DS:361E";

    // the rewrite's own: the notice of a saved game whose mods are not loaded
    public const string AndMore = "id:rw.notice.and-more";
}
