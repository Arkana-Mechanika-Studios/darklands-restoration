using Darklands.Core;

namespace Darklands.Cards.Tests;

/// <summary>The test screens' rule messages in the original's words (as the card screen shows them): a notice through ShowMessage, a
/// report through Tell. Without the game files the text is the message's id and arguments.</summary>
internal static class TestMessages
{
    public static string Text(GameMessage message) => CardHarness.Assets?.MessageText.Text(message) ?? message.ToString();

    public static void Post(ICardScreen screen, GameMessage message)
    {
        if (message.Kind == MessageKind.Notice)
        {
            screen.ShowMessage(Text(message), message.Ticks);
        }
        else
        {
            screen.Tell(Text(message), message.Member);
        }
    }
}
