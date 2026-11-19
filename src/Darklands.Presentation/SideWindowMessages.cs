using Darklands.Core;

namespace Darklands.Presentation;

/// <summary>
/// The rules' messages on a screen that is no card: a notice box for a moment, a member's report in the side window waiting for a key or
/// the left button (the same drawing as the card screen's).
/// </summary>
// orig: notice-display root 0000:24BC and the side-window message R043+0x29D0 (through thunk 09C0:1E91 in raise-skill-by R029+0x02EA): both
//   are the program's, called from any screen; on the creation's member sheet a prayer's skill boosts report through R043+0x29D0 (lockstep
//   session carry-create-pray3: R034+0x0102 < R039+0x49C4 < R043+0x29D0 in the original's stack samples)
public sealed class SideWindowMessages(GameUi ui) : IGameMessages
{
    /// <summary>Shows the message at once, in the original's words: a notice box for its ticks, a report in the side window (its
    /// <see cref="GameMessage.Good"/> is not used: the window draws every report the same).</summary>
    public void Post(GameMessage message) => Show(ui, message);

    /// <summary>A rule's message on <paramref name="ui"/>'s screen, at once.</summary>
    public static void Show(GameUi ui, GameMessage message)
    {
        var text = ui.Assets.MessageText.Text(message);
        if (message.Kind == MessageKind.Notice)
        {
            new MessageBox(ui).Show(text, message.Ticks * 1000 / 60);
        }
        else
        {
            CardScreen.Tell(ui, text, message.Member);
        }
    }
}
