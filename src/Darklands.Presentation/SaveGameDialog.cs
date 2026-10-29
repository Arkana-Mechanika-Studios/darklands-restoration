using Darklands.Core;
using Darklands.Core.Battles;
using Darklands.Core.Battles.Tactics;
using Darklands.Persistence;

namespace Darklands.Presentation;

/// <summary>
/// Alt-S: the game asks for a comment ("Save Game Comment:", up to 23 letters, "Darklands" to
/// start with) and saves under the next free name, SAVES\DKSAVEn.SAV, behind a "Saving Game" box.
/// An empty comment (Esc) saves nothing.
/// </summary>
// orig: save_game R037+0x2A68 (1462:2A68) matched: `char comment[0x29] = "Darklands"`,
// in a battle textback.pic is loaded first, text_input(comment, "Save Game Comment:", 0x17), `if (comment[0]) R037+0x2ACE(comment)`;
// 2ace R037+0x2ACE (1462:2ACE) draft draws text_box(card_window, -1, 1, "Saving Game", ...) while it writes and puts the screen back after;
// tests: LoadScreenTests
// C: there is no message afterwards; the box simply goes away.
public sealed class SaveGameDialog(GameUi ui, SaveFolder folder)
{
    public const int CommentLength = 0x17;

    /// <summary>Asks for the comment and saves; the file name, or null when nothing was saved.</summary>
    /// <param name="battle">The battle running when the game is saved inside one.</param>
    // orig: R037+0x2ACE (1462:2ACE) draft, its branch for a battle (DS:907A = 1): the members' running modifiers end, the records are
    // the combatants', bit 7 of the difficulty is set for the file, then the battle-state save R003+0x00DC writes the battle file
    // orig: R037+0x2B66..0x2B70: in a battle the card window's text colour (DS:017C -> +0x0C) is set to 9 before the "Saving Game" box
    //   (root 0000:23E6) and put back after it: the box's words are in colour 9 there, colour 1 outside a battle; checked against the
    //   original's screen (vis10 session D-b797f7, local/batch/c5uncertain)
    public string? Run(GameState state, TacticalBattle? battle = null)
    {
        var comment = new TextInputBox(ui).Ask("Save Game Comment:", CommentLength, OriginalSaves.DefaultComment);
        if (comment.Length == 0)
        {
            return null;
        }
        if (battle is not null)
        {
            battle.EndPartyModifiersForSave();
            battle.WriteBattleFile(state.BattleFile ??= new BattleFile(), inBattle: true, state.Battlefield.ClearedLevels, state.LastCastleFlags);
        }
        var box = new MessageBox(ui);
        var shown = box.Open("Saving Game", battle is not null ? (byte)9 : MessageBox.TextColor);
        try
        {
            return folder.Save(state, comment, ui.Options.Music, ui.Options.SoundEffects, battle?.Battle);
        }
        finally
        {
            box.Close(shown);
        }
    }
}
