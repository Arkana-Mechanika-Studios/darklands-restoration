using Darklands.Core;
using Darklands.Core.Characters;
using Darklands.Core.Economy;

namespace Darklands.Cards;

/// <summary>
/// Card-engine pieces of the witch-castle, mine and last-castle cards: the last castle's progress flags,
/// the current place's record and the films.
/// </summary>
public sealed partial class CardContext
{
    // The battle a card asks for (BattleMode, BattleEnemy, BattleSeed), CombatFlags (DS:9080), the bank notes
    // (Party.BankNotes, DS:9072), DeleteMember (party_delete), RandomName (random_name) and the pop-up hints (CardMenu.cs) are
    // the shared ones (CardContext.Memory, Helpers, Fights and Battles files).

    /// <summary>The last castle's progress (DS:160C, <see cref="GameState.LastCastleFlags"/>).</summary>
    // orig: DS:160C
    // C: the meaning of the bits is read from the LastC cards that set them.
    public int LastCastleFlags { get => State.LastCastleFlags; set => State.LastCastleFlags = value; }

    /// <summary>The location record of the current place (null in the wilderness).</summary>
    public Core.World.Location? HerePlace => State.Here;
}

// ---- films (from helper w6) -------------------------------------------------------------

/// <summary>
/// The full-screen films the cards can play (the game's closing sequence after Baphomet's
/// defeat): a list of steps the presentation plays one after the other.
/// </summary>
public sealed partial class CardContext
{
    /// <summary>
    /// Plays a film (tunes, PAN animations and digitised speech) on the whole screen, supplied by
    /// the presentation. It returns true when the film was played (the card screen was torn down
    /// and must be drawn again), false when films are switched off. Until it is set no film plays.
    /// </summary>
    public Func<IReadOnlyList<FilmStep>, bool>? PlayFilm { get; set; }

    /// <summary>Plays a film through <see cref="PlayFilm"/>; false when there is none.</summary>
    public bool Film(IReadOnlyList<FilmStep> steps) => PlayFilm?.Invoke(steps) ?? false;
}

/// <summary>
/// One step of a film: the digitised speech that starts with it (a .DGT file, played only by
/// sound cards that can; it replaces the one still playing), the tune, and the PAN animation
/// shown while it plays (none: the tune only starts).
/// </summary>
public sealed record FilmStep(int Tune, string? Animation = null, string? Speech = null);

/// <summary>The films of the game.</summary>
// orig: the film sequence in R133+0x186C (1971:04DC) matched — tunes
//   (R043+0x09AE), PAN animations (root 030B:0004 "fin0a".."fin5a"), digitised speech (R043+0x09C8)
// DIVERGENCE: played by the presentation through the PlayFilm hook (connected by DarklandsGame to Cinematics.Film); the C drives the sound
//   and PAN routines itself.
public static class Films
{
    /// <summary>The closing film after Baphomet has been defeated (FIN0A..FIN5A).</summary>
    public static readonly IReadOnlyList<FilmStep> Ending =
    [
        new(0x2D, Speech: "enddark1.dgt"),
        new(0x36, "fin0a"),
        new(0x37, "fin1a"),
        new(0x38, "fin2a"),
        new(0x39, "fin3a", "enddark2.dgt"),
        new(0x3A, "fin4a"),
        new(0x3B, "fin5a"),
    ];
}
