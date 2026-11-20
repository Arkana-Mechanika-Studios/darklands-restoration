using Avalonia.Controls;
using Darklands.Presentation;

namespace Darklands.Workshop;

/// <summary>
/// One of the Workshop's editors, a tab of its window (W-0): the translators' text editor now; the art, sound, font and card editors
/// (1.2) take the same place.
/// </summary>
internal interface IWorkshopEditor
{
    /// <summary>The tab's title.</summary>
    string Title { get; }

    /// <summary>The tab's content.</summary>
    Control View { get; }

    /// <summary>Unsaved changes.</summary>
    bool Dirty { get; }

    /// <summary>Writes the changes.</summary>
    void Save();

    /// <summary>The game's check of what is saved (its findings).</summary>
    IReadOnlyList<LanguagePackCheck.Finding> Check();

    /// <summary>Raised when the editor's dirty state or progress changes (the window's title and status follow).</summary>
    event Action? Changed;
}
