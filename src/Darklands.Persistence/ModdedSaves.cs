using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Darklands.Core;

namespace Darklands.Persistence;

/// <summary>What the running game tells the save folder about its mods (null on a <see cref="SaveFolder"/>: none, Classic).</summary>
public sealed class SaveMods
{
    /// <summary>The mods loaded now, with a card registered.</summary>
    public IReadOnlyList<ModMark> Active { get; init; } = [];

    /// <summary>
    /// The game's card (0..365) the original save format names for a mod's new card, null when no mod has that number.
    /// Called only for numbers above <see cref="OriginalSaves.LastOriginalCard"/>.
    /// </summary>
    public Func<int, int?> SavedCardOf { get; init; } = _ => null;

    /// <summary>True when a card of that number can run now (a mod's new card read back from a companion file).</summary>
    public Func<int, bool> HasCard { get; init; } = number => number <= OriginalSaves.LastOriginalCard;
}

/// <summary>What a saved game's companion file changed while it loaded, and what it could not do.</summary>
/// <param name="MissingMods">Mods the game was saved with that are not loaded now: it goes on without them, at the game's cards the save names.</param>
/// <param name="Problem">Why the companion file was not used (unreadable, or it belongs to an earlier save of that name), else null.</param>
public sealed record ModdedLoad(IReadOnlyList<ModMark> MissingMods, string? Problem)
{
    public static readonly ModdedLoad None = new([], null);
}

/// <summary>
/// The companion file of a modded saved game (SAVES\DKSAVEn.MODS.JSON beside DKSAVEn.SAV): which mods the game was saved
/// with and played with, and the card numbers above 365 the .SAV could not hold. The .SAV itself stays in the original
/// format and names only the game's cards, so the original game (and the restoration without the mod) loads it.
/// </summary>
/// <remarks>
/// New code (no original). The file carries the SHA-256 of its .SAV: when the .SAV was replaced since (the original game
/// deleted it and saved again under that name), the companion file is ignored.
/// </remarks>
public static class ModdedSaves
{
    /// <summary>The companion file's ending, after the save's name less ".SAV".</summary>
    public const string Ending = ".MODS.JSON";

    public const int Format = 1;

    /// <summary>The mods each game being played has been played with (kept beside the game, not in its state: the state is the original's).</summary>
    private static readonly ConditionalWeakTable<GameState, List<ModMark>> Used = new();

    /// <summary>
    /// The mods this game has been played with: empty for a game of the game's own cards only (Classic). Not in the original
    /// save format: the companion file keeps it, so a game stays marked modded after the mod is gone.
    /// </summary>
    public static IReadOnlyList<ModMark> ModsUsed(GameState state) => Used.TryGetValue(state, out var marks) ? marks : [];

    /// <summary>Marks a game as played with these mods (each mod and version once).</summary>
    public static void MarkUsed(GameState state, IEnumerable<ModMark> marks)
    {
        foreach (var mark in marks)
        {
            var used = Used.GetValue(state, _ => []);
            if (!used.Any(m => m.Id == mark.Id && m.Version == mark.Version))
            {
                used.Add(mark);
            }
        }
    }

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>The companion file's name for a saved game's name ("DKSAVE3.SAV" gives "DKSAVE3.MODS.JSON").</summary>
    public static string CompanionOf(string savName)
    {
        var stem = savName.EndsWith(".sav", StringComparison.OrdinalIgnoreCase) ? savName[..^4] : savName;
        return (stem + Ending).ToUpperInvariant();
    }

    /// <summary>The four card numbers of a game as its header holds them: the card, the two remembered ones and the previous one.</summary>
    public static int[] CardsOf(GameState state) => [state.CurrentCard, state.SavedCard, state.SavedCard2, state.PreviousCard];

    private static readonly string[] CardKeys = ["current", "saved", "saved2", "previous"];

    /// <summary>The companion file's text for a game just saved as <paramref name="sav"/> under <paramref name="savName"/>.</summary>
    /// <param name="cards">The game's real card numbers (<see cref="CardsOf"/>), those above 365 included.</param>
    /// <param name="active">The mods loaded now.</param>
    public static string Write(GameState state, byte[] sav, string savName, int[] cards, IReadOnlyList<ModMark> active)
    {
        var header = SaveFile.Parse(sav).Header;
        var saved = new JsonObject();
        var real = new JsonObject();
        for (var i = 0; i < CardKeys.Length; i++)
        {
            real[CardKeys[i]] = cards[i];
            saved[CardKeys[i]] = (int)RecordBytes.Word(header, OriginalSaves.CardFieldOffsets[i]);
        }
        static JsonArray Marks(IEnumerable<ModMark> marks) =>
            new(marks.Select(m => (JsonNode)new JsonObject { ["id"] = m.Id, ["name"] = m.Name, ["version"] = m.Version }).ToArray());
        var file = new JsonObject
        {
            ["format"] = Format,
            ["modded"] = true,
            ["note"] = "Written by the Darklands Restoration beside a game saved with mods. The .SAV is in the original format and names only the game's own cards (savedAs); this file keeps the mods' card numbers (cards).",
            ["save"] = savName.ToUpperInvariant(),
            ["sha256"] = Convert.ToHexString(SHA256.HashData(sav)),
            ["mods"] = Marks(active),
            ["playedWith"] = Marks(ModsUsed(state)),
            ["cards"] = real,
            ["savedAs"] = saved,
        };
        return file.ToJsonString(Indented);
    }

    /// <summary>
    /// Applies a companion file to a game just read from <paramref name="sav"/>: the game is marked with the mods it was played
    /// with, and the mods' card numbers come back where a loaded mod has the card. Never throws for a bad file.
    /// </summary>
    public static ModdedLoad Apply(string json, byte[] sav, GameState state, SaveMods? mods)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException e)
        {
            return new ModdedLoad([], $"the companion file cannot be read ({e.Message}); the game loads as a plain saved game");
        }
        if (root is not JsonObject file || file["format"]?.GetValueKind() != JsonValueKind.Number)
        {
            return new ModdedLoad([], "the companion file is not one this program writes; the game loads as a plain saved game");
        }
        try
        {
            if ((int)file["format"]! > Format)
            {
                return new ModdedLoad([], "the companion file was written by a newer version; the game loads as a plain saved game");
            }
            if (!string.Equals((string?)file["sha256"], Convert.ToHexString(SHA256.HashData(sav)), StringComparison.OrdinalIgnoreCase))
            {
                return new ModdedLoad([], "the companion file belongs to an earlier game saved under this name; it is ignored");
            }
            var savedWith = Marks(file["mods"]);
            MarkUsed(state, Marks(file["playedWith"]).Concat(savedWith));
            var active = mods?.Active ?? [];
            var missing = savedWith.Where(m => active.All(a => a.Id != m.Id)).ToList();
            if (file["cards"] is JsonObject cards)
            {
                var hasCard = mods?.HasCard ?? (n => n <= OriginalSaves.LastOriginalCard);
                int? Real(string key) => cards[key]?.GetValueKind() == JsonValueKind.Number ? (int)cards[key]! : null;
                // only a mod's new card (above 365) comes back from the file, and only when it can run now
                if (Real("current") is { } current && current > OriginalSaves.LastOriginalCard && hasCard(current))
                {
                    state.CurrentCard = current;
                }
                if (Real("saved") is { } saved && saved > OriginalSaves.LastOriginalCard && hasCard(saved))
                {
                    state.SavedCard = saved;
                }
                if (Real("saved2") is { } saved2 && saved2 > OriginalSaves.LastOriginalCard && hasCard(saved2))
                {
                    state.SavedCard2 = saved2;
                }
                if (Real("previous") is { } previous && previous > OriginalSaves.LastOriginalCard && hasCard(previous))
                {
                    state.PreviousCard = previous;
                }
            }
            return new ModdedLoad(missing, null);
        }
        catch (Exception e) when (e is InvalidOperationException or FormatException or JsonException or OverflowException)
        {
            return new ModdedLoad([], $"the companion file cannot be read ({e.Message}); the game loads as a plain saved game");
        }
    }

    private static List<ModMark> Marks(JsonNode? list) =>
        list is JsonArray array
            ? array.OfType<JsonObject>()
                .Select(o => new ModMark((string?)o["id"] ?? "", (string?)o["name"] ?? (string?)o["id"] ?? "", (string?)o["version"] ?? ""))
                .Where(m => m.Id.Length > 0)
                .ToList()
            : [];
}
