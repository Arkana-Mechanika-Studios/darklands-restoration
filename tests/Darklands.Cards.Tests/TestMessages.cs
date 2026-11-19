using Darklands.Core;

namespace Darklands.Cards.Tests;

/// <summary>The test screens' rule messages in the original's words (as the card screen shows them): a notice through ShowMessage, a
/// report through Tell. Without the game files the text is the message's id and arguments.</summary>
internal static class TestMessages
{
    public static string Text(GameMessage message) => CardHarness.Assets?.MessageText.Text(message) ?? message.ToString();

    /// <summary>A card text in the original's words (its id and arguments without the game files).</summary>
    public static string Words(GameText text) => CardHarness.Assets?.MessageText.Text(text) ?? text.ToString();

    public static void Post(ICardScreen screen, GameMessage message)
    {
        if (message.Kind == MessageKind.Notice)
        {
            screen.ShowMessage(new GameText(message.Id, message.Args), message.Ticks);
        }
        else
        {
            screen.Tell(new GameText(message.Id, message.Args), message.Member);
        }
    }
}
