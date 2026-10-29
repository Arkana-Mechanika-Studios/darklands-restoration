namespace Darklands.Audio.Packs;

/// <summary>Which driver a number goes to: the towns-and-map driver (music) or the battle driver (effects).</summary>
public enum AudioKind
{
    Music,
    Effect,
}

/// <summary>One piece of music or battle effect, by a stable name a pack can use.</summary>
/// <param name="Name">The stable name (lower case, hyphens): a pack's file name or manifest key.</param>
/// <param name="Number">The number the game sends (GameSounds: xSOUND.DC for music, xSOUND.DB for effects).</param>
/// <param name="Loops">Whether a replacement repeats by default (a pack's "loop" overrides it).</param>
/// <param name="Where">Where the game plays it, in plain words (for the guide and modders).</param>
public sealed record AudioCue(string Name, AudioKind Kind, int Number, bool Loops, string Where);

/// <summary>
/// The named catalogue of the game's music pieces and battle effects. Every number the game sends is listed under one stable
/// name; any other number of a driver is reached by its generic name, <c>music-XX</c> or <c>effect-XX</c> (two hex digits).
/// The names are part of the pack format: they never change once released.
/// </summary>
/// <remarks>
/// New code (replacement sound, not part of the game). The places come from the callers in this repository (cards,
/// screens, battle); the numbers are those the original sends to its play slot (root 047C:078A): music through R043+0x09AE,
/// battle effects through R014+0x465E. The control numbers (0 reset, 1 stop, 2 pause, 3 resume, 5 current piece, 6 silence,
/// 7 ready: <see cref="GameSounds"/>) are not replaceable: a pack follows them.
/// </remarks>
public static class AudioCatalogue
{
    /// <summary>Every named cue, music first, in number order.</summary>
    public static IReadOnlyList<AudioCue> All { get; } =
    [
        Music("church-district", 0x08, true, "the church district (cards R048+0x2504)"),
        Music("inn-1", 0x09, true, "an inn at night, by day one of two at random; camp in a town, one of two"),
        Music("inn-2", 0x0A, true, "an inn by day, the other of two; camp in a town, the other of two"),
        Music("marketplace", 0x0B, true, "the marketplace by day (R049+0x05B2)"),
        Music("crafts-quarter", 0x0C, true, "the business district, the crafts quarter and the military crafts quarter"),
        Music("saint-prayer", 0x0D, true, "praying to a saint (the previous piece comes back afterwards)"),
        Music("city-square-day", 0x0E, true, "the city square by day (R048+0x1B18)"),
        Music("university", 0x0F, true, "the university (R056+0x088C)"),
        Music("shell-game", 0x10, true, "the shell-game swindler in the market (R088+0x0B20)"),
        Music("fortress-day", 0x11, true, "the city lord's fortress by day (R050+0x1CEE)"),
        Music("party-death", 0x12, false, "the whole party dies (R029+0x2278)"),
        Music("travel-1", 0x13, true, "travelling on the map, one of two at random (R030+0x00A8)"),
        Music("travel-2", 0x14, true, "travelling on the map, the other of two; also the character creation (R037+0x0000)"),
        Music("camp-wilderness", 0x15, true, "camping in the wilderness (R036+0x0082)"),
        Music("city-dungeon", 0x16, true, "the city dungeon (R047+0x01A6)"),
        Music("grove-day", 0x17, true, "a grove outside the city by day (R052+0x104C)"),
        Music("travel-notice", 0x18, false, "the cue when the map travel notices a place"),
        Music("city-walls-day", 0x1B, true, "before the city walls by day (R045+0x04C0)"),
        Music("city-walls-night", 0x1C, true, "before the city walls at night (R045+0x04C0)"),
        Music("grove-night", 0x1D, true, "a grove outside the city at night (R052+0x164E)"),
        Music("robbers-city", 0x1F, false, "robbers in a city street (R053+0x0000)"),
        Music("ambush", 0x20, false, "robbers or bandits on the road, a spider nest, schrats in the forest"),
        Music("wild-hunt", 0x23, false, "the Wild Hunt rides at night"),
        Music("holzfrau", 0x24, false, "a holzfrau's advice about the saints (R086+0x0C40)"),
        Music("schrat-fruit", 0x25, false, "a schrat's magic fruit; the Wild Hunt's quarry (R087+0x14EE, R086+0x084E)"),
        Music("dream-1", 0x26, false, "called in a dream, first part (R117+0x0896)"),
        Music("dream-2", 0x27, false, "called in a dream, second part (R117+0x0896)"),
        Music("astrologer", 0x28, false, "the astrologer's manor (R117+0x03AA)"),
        Music("hamlet-square", 0x29, true, "a hamlet's square (R077+0x1320)"),
        Music("templars-burning", 0x2A, false, "Templars burning a friar (R117+0x1152)"),
        Music("ending-prelude", 0x2D, false, "the start of the ending (R133+0x186C; plays ENDDARK1.DGT's speech)"),
        Music("opening-1", 0x2F, false, "the opening, first scene (root 03A1:000A; plays OPENDARK.DGT's clips)"),
        Music("opening-2", 0x30, false, "the opening, second scene"),
        Music("opening-3", 0x31, false, "the opening, third scene"),
        Music("opening-4", 0x32, false, "the opening, fourth scene"),
        Music("opening-5", 0x33, false, "the opening, fifth scene"),
        Music("opening-6", 0x34, false, "the opening, sixth scene"),
        Music("opening-7", 0x35, false, "the opening, seventh scene"),
        Music("ending-1", 0x36, false, "the ending, first scene (R133+0x186C)"),
        Music("ending-2", 0x37, false, "the ending, second scene"),
        Music("ending-3", 0x38, false, "the ending, third scene"),
        Music("ending-4", 0x39, false, "the ending, fourth scene (ENDDARK2.DGT's speech from here)"),
        Music("ending-5", 0x3A, false, "the ending, fifth scene"),
        Music("ending-6", 0x3B, false, "the ending, sixth scene"),
        Effect("battle-death", 0x0A, "a combatant dies (R014+0x2324)"),
        Effect("battle-missile-heavy", 0x0B, "a heavy missile is loosed (R018+0x0000)"),
        Effect("battle-cloud", 0x0C, "a cloud or slowing spell or potion lands (R019+0x0000)"),
        Effect("battle-blast", 0x0D, "a flash or blast lands (R019+0x0000)"),
        Effect("battle-ambush-seen", 0x0E, "hidden enemies are spotted (R014+0x4442)"),
        Effect("battle-missile-light", 0x0F, "an arrow or bolt is loosed (R018+0x0000)"),
        Effect("battle-knockback", 0x10, "a hit knocks the target back"),
        Effect("battle-swing", 0x11, "a melee swing that does not hit"),
        Effect("battle-refused", 0x12, "an order that cannot be carried out (R014+0x4694; plays even with effects off)"),
    ];

    private static readonly Dictionary<string, AudioCue> ByName = All.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<(AudioKind, int), AudioCue> ByNumber = All.ToDictionary(c => (c.Kind, c.Number));

    /// <summary>The music numbers a pack cannot replace: the drivers' commands (<see cref="GameSounds"/>).</summary>
    public static bool IsControl(int musicNumber) => musicNumber is >= 0 and <= 7;

    /// <summary>The cue of a name (a catalogue name or a generic <c>music-XX</c> / <c>effect-XX</c>), null when there is none.</summary>
    public static AudioCue? Find(string name)
    {
        if (ByName.TryGetValue(name, out var cue))
        {
            return cue;
        }
        foreach (var (prefix, kind) in new[] { ("music-", AudioKind.Music), ("effect-", AudioKind.Effect) })
        {
            if (name.Length == prefix.Length + 2 && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(name.AsSpan(prefix.Length), System.Globalization.NumberStyles.AllowHexSpecifier, null, out var number)
                && !(kind == AudioKind.Music && IsControl(number)))
            {
                return Find(kind, number);
            }
        }
        return null;
    }

    /// <summary>The cue of a number: its catalogue entry, else a generic one (<c>music-XX</c>, <c>effect-XX</c>, looping music).</summary>
    public static AudioCue Find(AudioKind kind, int number) =>
        ByNumber.TryGetValue((kind, number), out var cue)
            ? cue
            : new AudioCue($"{(kind == AudioKind.Music ? "music" : "effect")}-{number & 0xFF:x2}", kind, number, kind == AudioKind.Music,
                "not played by the game as far as known");

    private static AudioCue Music(string name, int number, bool loops, string where) => new(name, AudioKind.Music, number, loops, where);

    private static AudioCue Effect(string name, int number, string where) => new(name, AudioKind.Effect, number, false, where);
}
