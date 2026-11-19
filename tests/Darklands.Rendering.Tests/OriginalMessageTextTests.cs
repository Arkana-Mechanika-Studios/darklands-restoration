using System.Security.Cryptography;
using System.Text;
using Darklands.Content.Formats.Exe;
using Darklands.Content.Formats.Text.Strings;
using Darklands.Core;
using Darklands.Core.Characters;

namespace Darklands.Rendering.Tests;

/// <summary>
/// The rules' messages in the original's words, read from the player's EXE (vacuous without the game files). No game text is
/// stored here: the words are pinned by a hash of the texts the 1.0 rules built themselves, and the pieces agree with the
/// original-text extractor's keys.
/// </summary>
public sealed class OriginalMessageTextTests
{
    private static readonly Lazy<OriginalMessageText?> Text = new(
        () => GameFolder.Files is { } files ? new OriginalMessageText(DarklandsExecutable.Load(files)) : null);

    private static readonly Lazy<StringTable?> Table = new(
        () => GameFolder.Files is { } files ? OriginalTextExtractor.Extract(files) : null);

    [Fact]
    public void TheWordsAreTheOnesTheRulesWroteBefore()
    {
        if (Text.Value is not { } text)
        {
            return;
        }
        string[] all =
        [
            text.Text(GameMessage.Notice(GameMessageIds.AlchemyFailure)),
            text.Text(GameMessage.Notice(GameMessageIds.AlchemySuccess)),
            text.Text(GameMessage.Notice(GameMessageIds.EventTableFull)),
            text.Text(GameMessage.Notice(GameMessageIds.NotEnoughMoney, 0x50)),
            text.Text(GameMessage.SkillChanged(0, (int)Skill.Religion, 13, 99)),
            text.Text(GameMessage.SkillChanged(20, (int)Skill.Stealth, 0, 3)),
        ];
        // SHA-256 of the six texts joined by line feeds, as 1.0's literals in Core gave them (EventExpiry, EventLog, CampStay, MemberRules)
        Assert.Equal("C1B206CA13EAD7786B7E7623D886488FDBF8FBA68BFD6FAD5FD71A0B84C309A4",
            Convert.ToHexString(SHA256.HashData(Encoding.Latin1.GetBytes(string.Join('\n', all)))));
    }

    [Fact]
    public void ExeIdsAreTheExtractorsKeys()
    {
        if (Text.Value is not { } text || Table.Value is not { } table)
        {
            return;
        }
        foreach (var id in new[] { GameMessageIds.AlchemyFailure, GameMessageIds.AlchemySuccess, GameMessageIds.EventTableFull, GameMessageIds.NotEnoughMoney })
        {
            Assert.Equal(table.Find(id)?.Text, text.Text(GameMessage.Notice(id)));
            Assert.Equal(StringOrigin.Known, table.Find(id)!.Origin);
        }
        Assert.Equal(StringOrigin.Known, table.Find("exe:DS:26BE")?.Origin);
        Assert.Equal(StringOrigin.Known, table.Find("exe:DS:26CD")?.Origin);
        Assert.Equal(
            table.Find("exe:290E:0C42")!.Text + table.Find("exe:DS:26BE")!.Text + "4" + table.Find("exe:DS:26CD")!.Text + "5",
            text.Text(GameMessage.SkillChanged(1, 7, 4, 5)));
    }

    [Fact]
    public void AnUnknownIdIsShownAsItself()
    {
        if (Text.Value is not { } text)
        {
            return;
        }
        Assert.Equal("id:no.such.message", text.Text(GameMessage.Notice("id:no.such.message")));
    }
}
