using Darklands.Localization;

namespace Darklands.Presentation;

/// <summary>How a screen runs: its own blocking loop on <see cref="GameUi"/> (every screen of the game today), or a registry view a host runs frame by frame.</summary>
public enum ViewKind
{
    /// <summary>A screen with its own loop (Run / Show / Pick on GameUi.Poll), as the original's screens are.</summary>
    ClassicLoop,

    /// <summary>A view of the registry: a model, a layout, a drawing and the commands its input gives, run by a host (none yet).</summary>
    RegistryView,
}

/// <summary>
/// One screen the game can show: its id (the name it announces with <see cref="GameUi.OnScreen"/>, which a pack's
/// <c>interface/layout.json</c> keys its slots by, so it never changes), how it runs, the model it reads (a 1.1 seam, null: none
/// yet) and the commands it takes. Its slots are the keyed texts the recorded sessions saw drawn on it (<see cref="ViewCatalogue.SlotsOf"/>).
/// </summary>
/// <param name="Id">The screen's name.</param>
/// <param name="Kind">How it runs.</param>
/// <param name="Model">The model it reads, as the type's name (null: the screen reads the game state directly).</param>
/// <param name="Commands">The player commands it takes (docs/COMMANDS.md), as the types' names.</param>
/// <param name="Announced">Whether the screen announces itself (OnScreen): false for the card screen and the text input box (the recorded
/// sessions name them by their type, ScreenNames).</param>
public sealed record ViewEntry(string Id, ViewKind Kind, string? Model, IReadOnlyList<string> Commands, bool Announced = true);

/// <summary>
/// The view registry's catalogue (1.2, the modular UI's grounds): an entry for every screen the game can show. It is what a 1.3
/// interface mod addresses, and what the Workshop's layout panel and the inspector list. Every screen runs its own loop today; new
/// screens are written as registry views, and the classic ones move over one by one. Kept true by its test: a screen that announces
/// a name not here, an entry no screen announces, or a recorded screen not here fails.
/// </summary>
// Not in the original: the 1.2 view registry (the modular UI's grounds).
public static class ViewCatalogue
{
    // the commands every classic screen takes: a key, a click, the pointer moved (the player's own vocabulary)
    private static readonly string[] Input = [nameof(Commands.PressKey), nameof(Commands.ClickAt), nameof(Commands.PointAt)];

    // and a line of the list it is choosing from (GameUi.CommandTargets)
    private static readonly string[] InputAndLines = [.. Input, nameof(Commands.ChooseLine)];

    /// <summary>Every screen, by id.</summary>
    public static IReadOnlyList<ViewEntry> Entries { get; } =
    [
        new("StartScreen", ViewKind.ClassicLoop, null, Input),
        new("LoadGameScreen", ViewKind.ClassicLoop, null, Input),
        new("SaveGameDialog", ViewKind.ClassicLoop, null, Input),
        new("CharacterCreationScreen", ViewKind.ClassicLoop, null, Input),
        new("CharacterGeneratorScreen", ViewKind.ClassicLoop, null, Input),
        new("CardScreen", ViewKind.ClassicLoop, "OptionHint", InputAndLines, Announced: false),
        new("MenuBar", ViewKind.ClassicLoop, null, Input),
        new("MessageBox", ViewKind.ClassicLoop, null, Input),
        new("TextInputBox", ViewKind.ClassicLoop, null, Input, Announced: false),
        new("OverlandScreen", ViewKind.ClassicLoop, null, Input),
        new("CampScreen", ViewKind.ClassicLoop, null, Input),
        new("BattleScreen", ViewKind.ClassicLoop, null, Input),
        new("CharacterSheetScreen", ViewKind.ClassicLoop, "SheetSession", Input),
        new("PartyInfoScreen", ViewKind.ClassicLoop, null, Input),
        new("PartyRosterScreen", ViewKind.ClassicLoop, null, Input),
        new("MarchingOrderScreen", ViewKind.ClassicLoop, null, Input),
        new("SaintPrayerScreen", ViewKind.ClassicLoop, null, Input),
        new("AlchemyScreen", ViewKind.ClassicLoop, null, Input),
        new("BarterScreen", ViewKind.ClassicLoop, "BarterSession", Input),
        new("ItemStoreScreen", ViewKind.ClassicLoop, "StoreSession", Input),
        new("LootScreen", ViewKind.ClassicLoop, "LootSession", Input),
    ];

    private static readonly Dictionary<string, ViewEntry> ById = Entries.ToDictionary(e => e.Id, StringComparer.Ordinal);

    /// <summary>The entry of a screen id, null when the catalogue has none.</summary>
    public static ViewEntry? Find(string id) => id is not null && ById.TryGetValue(id, out var entry) ? entry : null;

    /// <summary>
    /// The slots of a screen: the keyed texts the recorded sessions saw drawn on it (the shipped boxes, or <paramref name="boxes"/>),
    /// in key order. A pack's layout addresses them by these keys.
    /// </summary>
    public static IReadOnlyList<string> SlotsOf(string id, IReadOnlyList<TextBox>? boxes = null) =>
        [.. (boxes ?? ShippedBoxes.Default).Where(b => b.Screens.Contains(id)).Select(b => b.Key).Distinct().Order(StringComparer.Ordinal)];
}
