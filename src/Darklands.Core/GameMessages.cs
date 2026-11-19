namespace Darklands.Core;

/// <summary>How a <see cref="GameMessage"/> is shown.</summary>
public enum MessageKind
{
    /// <summary>A framed box for <see cref="GameMessage.Ticks"/> timer ticks, with no key waited for (<c>notice-display</c>, root 0000:24BC).</summary>
    Notice,

    /// <summary>A note beside a member's portrait (or the whole column), waited out with a key or the left button.</summary>
    Report,
}

/// <summary>
/// A message the rules show while they run: a text id and its arguments, never resolved text (the presentation turns it into
/// words: docs/LOCALISATION.md). <paramref name="Member"/> is the member slot a report is about (−2 the whole column, −1 none);
/// <paramref name="Good"/> news is shown bright, bad news red where the screen colours it.
/// </summary>
public sealed record GameMessage(
    string Id,
    IReadOnlyDictionary<string, object> Args,
    MessageKind Kind,
    int Member = -1,
    bool Good = false,
    int Ticks = GameMessage.NoticeTicks)
{
    /// <summary>The ticks of a notice when the rule gives none (root 0000:24BC called with 100).</summary>
    public const int NoticeTicks = 100;

    private static readonly IReadOnlyDictionary<string, object> None = new Dictionary<string, object>();

    /// <summary>A notice box with the text <paramref name="id"/> for <paramref name="ticks"/> timer ticks.</summary>
    public static GameMessage Notice(string id, int ticks = NoticeTicks) => new(id, None, MessageKind.Notice, Ticks: ticks);

    /// <summary>A report about <paramref name="member"/>.</summary>
    public static GameMessage Report(int member, string id, IReadOnlyDictionary<string, object> args, bool good) =>
        new(id, args, MessageKind.Report, member, good);

    /// <summary>
    /// "&lt;skill&gt; changed from <paramref name="from"/> to <paramref name="to"/>" about <paramref name="member"/>
    /// (<see cref="GameMessageIds.SkillChanged"/>); a rise is good news.
    /// </summary>
    public static GameMessage SkillChanged(int member, int skill, int from, int to) =>
        Report(member, GameMessageIds.SkillChanged,
            new Dictionary<string, object> { ["skill"] = skill, ["from"] = from, ["to"] = to }, to > from);

    /// <summary>An integer argument.</summary>
    public int Number(string name) => Convert.ToInt32(Args[name], System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>For logs and test failures: the id and its arguments, no text.</summary>
    public override string ToString() =>
        Args.Count == 0 ? Id : $"{Id} {string.Join(' ', Args.OrderBy(a => a.Key, StringComparer.Ordinal).Select(a => $"{a.Key}={a.Value}"))}";
}

/// <summary>
/// The ids of the rules' messages. A string of DARKLAND.EXE is keyed by where it is (<c>exe:DS:offset</c>, the original-text
/// extractor's keys); a sentence the original builds from pieces gets an <c>id:</c> of its own, with arguments.
/// </summary>
public static class GameMessageIds
{
    /// <summary>"Alchemy Failure" (DS:2715), shown when a brew fails (R029+0x3FB2).</summary>
    public const string AlchemyFailure = "exe:DS:2715";

    /// <summary>"Alchemy Success" (DS:2725), shown when a brew succeeds (R029+0x3FB2).</summary>
    public const string AlchemySuccess = "exe:DS:2725";

    /// <summary>"EVENT Failure" (DS:2707): the event table is full (R029+0x3144).</summary>
    public const string EventTableFull = "exe:DS:2707";

    /// <summary>"Not Enough Money" (DS:3FED): the purse cannot pay a camp day (R036+0x147A).</summary>
    public const string NotEnoughMoney = "exe:DS:3FED";

    /// <summary>
    /// "&lt;skill&gt; changed from &lt;from&gt; to &lt;to&gt;", arguments <c>skill</c> (the skill number), <c>from</c>, <c>to</c>: the
    /// skill's name, then " changed from " (DS:26BE), the old value, " to " (DS:26CD), the new value (raise-skill-by R029+0x02EA,
    /// ability-change-notice R021+0x0080).
    /// </summary>
    public const string SkillChanged = "id:skill.changed";
}

/// <summary>
/// Where the rules post their messages. The presentation implements it. A post returns nothing and the rules never depend on
/// whether, when or how long a message is shown, so a view may queue it (<see cref="GameMessageQueue"/>); the classic screens show
/// it at once, at the moment the original shows it.
/// </summary>
public interface IGameMessages
{
    /// <summary>Hands <paramref name="message"/> to the presentation.</summary>
    void Post(GameMessage message);
}

/// <summary>Messages that go nowhere (tests, headless runs), kept for inspection.</summary>
public sealed class GameMessageLog : IGameMessages
{
    /// <summary>The messages posted, in order.</summary>
    public List<GameMessage> Posted { get; } = [];

    public void Post(GameMessage message) => Posted.Add(message);

    /// <summary>The ids posted, in order.</summary>
    public IEnumerable<string> Ids => Posted.Select(m => m.Id);
}

/// <summary>
/// Messages kept until the view presents them: a presentation that shows them later than the post (an enhanced view, a log
/// window) takes them with <see cref="Drain"/>. The classic screens do not use it (they show at the post, as the original).
/// </summary>
public sealed class GameMessageQueue : IGameMessages
{
    private readonly Queue<GameMessage> _waiting = new();

    /// <summary>The number of messages waiting.</summary>
    public int Count => _waiting.Count;

    public void Post(GameMessage message) => _waiting.Enqueue(message);

    /// <summary>The waiting messages, oldest first; the queue is empty afterwards.</summary>
    public IReadOnlyList<GameMessage> Drain()
    {
        var all = _waiting.ToArray();
        _waiting.Clear();
        return all;
    }
}
