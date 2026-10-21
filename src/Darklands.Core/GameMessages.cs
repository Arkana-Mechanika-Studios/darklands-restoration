namespace Darklands.Core;

/// <summary>
/// Messages the rules show while they run ("Alchemy Success", "Religion changed from 30 to
/// 45"). The presentation implements it; rules never wait for anything else.
/// </summary>
public interface IGameMessages
{
    /// <summary>A message box shown for <paramref name="ticks"/> timer ticks, with no key waited for (<c>show_message</c>, root 0000:24BC).</summary>
    void Show(string text, int ticks = 100);

    /// <summary>A note beside a member's portrait; <paramref name="good"/> news is shown bright, bad news red.</summary>
    void Report(int member, string text, bool good);
}

/// <summary>Messages that go nowhere (tests, headless runs), optionally kept for inspection.</summary>
public sealed class GameMessageLog : IGameMessages
{
    public List<string> Lines { get; } = [];

    public void Show(string text, int ticks = 100) => Lines.Add(text);

    public void Report(int member, string text, bool good) => Lines.Add($"{member}: {text}");
}
