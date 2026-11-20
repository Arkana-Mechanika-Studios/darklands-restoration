namespace Darklands.Presentation;

/// <summary>
/// The laid-out texts of one screen page (a translation pack's interface/layout.json): for each, the place the original draws it and the
/// box it is drawn in now. The screen's hit tests and highlights go through it, so a moved, widened or wrapped line answers the
/// pointer where it is drawn: a point in its drawn box is taken as a point of its original place, a point of its original place
/// that it left answers nothing. With nothing laid out (no pack, no layouts) every point is itself and every highlight its own: the
/// original's.
/// </summary>
// Not in the original: the 1.1 translation packs (screen layouts per language; the extensibility plan's rule of 2026-11-09: one layout
// per text, read by the drawing, the highlight and the hit test).
public sealed class LaidOutAreas
{
    /// <summary>A box in game pixels.</summary>
    public readonly record struct Box(int X, int Y, int Width, int Height)
    {
        public bool Contains(int x, int y) => x >= X && x < X + Width && y >= Y && y < Y + Height;

        public bool Overlaps(Box other) => X < other.X + other.Width && other.X < X + Width && Y < other.Y + other.Height && other.Y < Y + Height;
    }

    private readonly Dictionary<(int X, int Y), (Box Original, Box Drawn)> _laid = [];

    /// <summary>Whether anything is laid out on the page.</summary>
    public bool Any => _laid.Count > 0;

    /// <summary>A text whose original place is <paramref name="original"/> (its first row) is drawn in <paramref name="drawn"/>.</summary>
    public void Laid(Box original, Box drawn) => _laid[(original.X, original.Y)] = (original, drawn);

    /// <summary>The text drawn at (<paramref name="x"/>, <paramref name="y"/>) now is drawn where the original draws it.</summary>
    public void Plain(int x, int y) => _laid.Remove((x, y));

    /// <summary>Nothing laid out (a page drawn again from its picture).</summary>
    public void Clear() => _laid.Clear();

    /// <summary>
    /// The point the original's hit test is given for the pointer at (<paramref name="x"/>, <paramref name="y"/>): in a laid-out text's
    /// drawn box, a point inside its original place; on an original place a laid-out text has left (and no drawn box), (-1, -1); else
    /// the point itself.
    /// </summary>
    public (int X, int Y) Map(int x, int y)
    {
        if (_laid.Count == 0)
        {
            return (x, y);
        }
        foreach (var (original, drawn) in _laid.Values)
        {
            if (drawn.Contains(x, y))
            {
                // the middle of its original place: the original's tests are strict at their edges
                return (original.X + original.Width / 2, original.Y + original.Height / 2);
            }
        }
        foreach (var (original, _) in _laid.Values)
        {
            if (original.Contains(x, y))
            {
                return (-1, -1);
            }
        }
        return (x, y);
    }

    /// <summary>The drawn boxes of the laid-out texts whose original place meets <paramref name="area"/> (a highlight there lights them too).</summary>
    public IEnumerable<Box> DrawnIn(Box area) =>
        _laid.Values.Where(l => l.Original.Overlaps(area)).Select(l => l.Drawn);
}
