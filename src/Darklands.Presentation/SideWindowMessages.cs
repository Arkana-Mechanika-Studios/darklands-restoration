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
    public void Show(string text, int ticks = 100) => new MessageBox(ui).Show(text, ticks * 1000 / 60);

    public void Report(int member, string text, bool good) => CardScreen.Tell(ui, text, member);
}
