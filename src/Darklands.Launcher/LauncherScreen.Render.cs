using Darklands.Launcher.Drawing;
using Darklands.Platform;

namespace Darklands.Launcher;

// The launcher screen: drawing the screen: the card, the settings panel, the plates, the notice and the tooltips. The state and the input are in LauncherScreen.cs.
internal sealed partial class LauncherScreen
{
    /// <summary>The left card and the settings panel, in design pixels (1280 x 720, the painting's shape).</summary>
    private static Rect CardFrame(View v) => v.Rect(136, 196, 414, 508);

    /// <summary>The settings panel, below the frameless window's buttons at the top-right corner.</summary>
    private static Rect SettingsFrame(View v) => v.Rect(768, 46, 494, 660);

    /// <summary>
    /// Where the painting's focus (the castle on its hill: 30.5% across, 34% down the picture) sits in the design: in the gap
    /// between the card and the settings, as in the art direction's mock-up; the painting is enlarged around it to cover the window.
    /// </summary>
    private const double FocusX = 0.305;
    private const double FocusY = 0.34;
    private const double FocusAtX = 620;
    private const double FocusAtY = 318;
    private const double PaintingScale = 1.12;

    /// <summary>Draws the screen on <paramref name="canvas"/> and notes where the controls are.</summary>
    public void Render(Canvas canvas)
    {
        var s = Math.Min((double)canvas.Width / DesignWidth, (double)canvas.Height / DesignHeight);
        var v = new View(s, (int)((canvas.Width - DesignWidth * s) / 2), (int)((canvas.Height - DesignHeight * s) / 2));
        // what never changes (the painting, the title, the parchments, the card's text) is drawn once for each size
        if (_background is null || _background.Width != canvas.Width || _background.Height != canvas.Height)
        {
            _background = new Canvas(canvas.Width, canvas.Height);
            DrawBackground(_background, v);
        }
        _background.Pixels.CopyTo(canvas.Pixels, 0);
        _hotspots = [(_discord, Item.Discord, Part.Row)];
        _blocks = [];
        if (_notesLink is { } notes)
        {
            _hotspots.Add((notes, Item.Notes, Part.Row));
        }
        if (_guideLink is { } guide)
        {
            _hotspots.Add((guide, Item.PackGuide, Part.Row));
        }
        if (_reportLink is { } reportLink)
        {
            _hotspots.Add((reportLink, Item.Report, Part.Row));
        }
        foreach (var (link, item) in new[] { ((Rect?)_discord, Item.Discord), (_notesLink, Item.Notes), (_guideLink, Item.PackGuide), (_reportLink, Item.Report) })
        {
            if (link is { } area)
            {
                // the link under the pointer, or chosen with the keyboard: lit as a row
                RowLight(canvas, new Rect(area.X - v.Len(3), area.Y - v.Len(1), area.Width + v.Len(6), area.Height + v.Len(2)), item);
            }
        }
        // a row of the other tab took the focus (Tab, the arrows, a dropped folder): its tab opens
        if (OnAdvanced(_focus))
        {
            _advanced = true;
        }
        else if (OnGeneral(_focus))
        {
            _advanced = false;
        }
        DrawSettings(canvas, v);
        if (_surface.Frameless)
        {
            DrawWindowButtons(canvas, v);
        }
        DrawPreview(canvas, v);
        // over everything: the tooltip
        DrawTooltip(canvas, v);
    }

    /// <summary>The picture filter's preview inset, in window pixels (empty while it is not shown).</summary>
    internal Rect PreviewArea { get; private set; }

    /// <summary>The preview's picture in the inset (design pixels): a 120 x 75 piece of a game picture at 3 times, as a window shows it.</summary>
    private static Rect PreviewImage(View v) => v.Rect(163, 246, 360, 225);

    /// <summary>
    /// The picture filter's preview (Item.Picture chosen or under the pointer, General tab): a framed inset over the left card,
    /// the chosen filter's preview (made away from the frames by the host's IFilterPreviews, asked for from the first frame on,
    /// so it is ready before the row is ever chosen), and what it shows. A preview not made yet: a soft placeholder; no frame
    /// waits for one.
    /// </summary>
    private void DrawPreview(Canvas canvas, View v)
    {
        PreviewArea = default;
        var image = PreviewImage(v);
        var previews = _context.Previews;
        previews?.Prepare(_model.GameFolderChecked, image.Width, image.Height, _context.Settings.CrtCurvature ?? false);
        if (_advanced || (_focus != Item.Picture && _hover != Item.Picture))
        {
            return;
        }
        var filter = PictureFilters.Parse(_model.Filter) ?? PictureFilter.Sharp;
        var caption = Face(Assets.Italic, 14, v);
        var pad = v.Len(10);
        var frame = new Rect(image.X - pad, image.Y - pad - caption.LineHeight - v.Len(4), image.Width + 2 * pad,
            image.Height + 2 * pad + 2 * caption.LineHeight + v.Len(8));
        Shade(canvas, new Rect(frame.X + v.Len(4), frame.Y + v.Len(5), frame.Width, frame.Height), Black, 80);
        canvas.Fill(frame, HintFill);   // solid: nothing of the card shows through
        canvas.Frame(frame, Math.Max(1, v.Len(1)), 0x8A7454);
        caption.Draw(canvas, Fit(caption, $"Picture: {PictureFilters.Label(filter)}", image.Width, fromEnd: false), image.X, frame.Y + pad / 2, Cream);
        var preview = previews?.Get(filter);
        if (preview is { } shown)
        {
            if (shown.Width == image.Width && shown.Height == image.Height && image.X >= 0 && image.Y >= 0
                && image.Right <= canvas.Width && image.Bottom <= canvas.Height)
            {
                // made at the inset's size: copied row by row
                for (var y = 0; y < image.Height; y++)
                {
                    Array.Copy(shown.Argb, y * shown.Width, canvas.Pixels, (image.Y + y) * canvas.Width + image.X, image.Width);
                }
            }
            else
            {
                // a size just changed: the last one stretched until the new one is made
                canvas.Paint(image, (x, y) => shown.Argb[Math.Min(shown.Height - 1, y * shown.Height / image.Height) * shown.Width
                    + Math.Min(shown.Width - 1, x * shown.Width / image.Width)]);
            }
        }
        else
        {
            canvas.Fill(image, 0x3A2E22);
            var wait = previews is null ? "No preview here." : "Preparing the preview…";
            caption.Draw(canvas, wait, image.X + (image.Width - caption.Measure(wait)) / 2, image.Y + (image.Height - caption.LineHeight) / 2, PaleCream);
        }
        canvas.Frame(image, Math.Max(1, v.Len(1)), 0x8A7454);
        var note = preview is { FromGame: false } ? "A test pattern (no game folder); the game window shows the game itself."
            : "A picture of your game, about as the game window will show it.";
        caption.Draw(canvas, Fit(caption, note, image.Width, fromEnd: false), image.X, image.Bottom + v.Len(6), PaleCream);
        PreviewArea = frame;
    }

    private void DrawBackground(Canvas canvas, View v)
    {
        canvas.Clear(0x1C1712);
        DrawPainting(canvas, v);
        DrawTitle(canvas, v);
        var card = CardFrame(v);
        PanelShadow(canvas, card, v, "card-left");
        Parchment(canvas, card, v, "card-left", 11);
        DrawCard(canvas, v, card);
        var settings = SettingsFrame(v);
        PanelShadow(canvas, settings, v, "card-settings");
        Parchment(canvas, settings, v, "card-settings", 23);
        if (Assets.Art("card-settings") is null)
        {
            // the panel's art has its own corners
            Corners(canvas, settings, v);
        }
    }

    /// <summary>"Darklands" and "Restoration Project" over the painting, with a fleuron rule under them.</summary>
    private void DrawTitle(Canvas canvas, View v)
    {
        // as in the art direction: "Restoration Project" as wide as "Darklands" under it, both in cream on a soft shadow
        var big = Face(Assets.Display, 80, v);
        var sub = Face(Assets.Strong, 40, v);
        var centre = v.OffsetX + v.Len(343);
        var y = v.OffsetY + v.Len(18);
        var shadow = Math.Max(1, v.Len(2.5));
        big.Draw(canvas, "Darklands", centre - big.Measure("Darklands") / 2, y, Cream, shadow, Black);
        y += (int)(big.LineHeight * 0.80);
        sub.Draw(canvas, "Restoration Project", centre - sub.Measure("Restoration Project") / 2, y, Cream, shadow, Black);
        y += sub.LineHeight + v.Len(10);
        Fleuron(canvas, v, centre, y, v.Len(190), Cream, onDark: true);
    }

    /// <summary>The left card: what is new, about the project with its links, and the copyright note at the bottom.</summary>
    private void DrawCard(Canvas canvas, View v, Rect card)
    {
        var x = card.X + v.Len(28);
        var right = card.Right - v.Len(26);
        var width = right - x;
        var y = card.Y + v.Len(26);
        var text = Face(Assets.Text, 15, v);
        var small = Face(Assets.Text, 11.5, v);
        var line = (int)Math.Round(text.LineHeight * 1.05);
        var bottomNote = small.Wrap("Darklands © 1992 MicroProse Software, Inc. Darklands and MicroProse are the property of their "
            + "respective owners. This is an unofficial fan project, not affiliated with or endorsed by them.", width);
        var noteTop = card.Bottom - v.Len(22) - bottomNote.Count * small.LineHeight;

        y = Capitalised(canvas, v, x, y, "What’s new in " + _context.Version, _notes.Count > 0 ? "From the release notes." : "The release notes are not included with this copy.");
        y += v.Len(8);
        if (_notes.Count == 0)
        {
            foreach (var l in text.Wrap("The release notes of this version are on the project's page.", width))
            {
                text.Draw(canvas, l, x, y, Ink);
                y += line;
            }
        }
        // the notes' first items, as many as leave room for the rest of the card; each with its line of detail when all of them fit so
        var about = "MicroProse’s 1992 Darklands, rebuilt from the original program for today’s computers. "
            + "It plays from your own copy of the game: none of it is included.";
        var links = 1 + (_context.PackGuide is null ? 0 : 1) + (_context.ReportProblem is null ? 0 : 1);
        var headingHeight = Math.Max(v.Len(46), Face(Assets.Strong, 25, v).LineHeight - v.Len(3) + Face(Assets.Italic, 14, v).LineHeight);
        var rest = (_context.ReleaseNotesLink is null ? 0 : line) + v.Len(4) + v.Len(20) + headingHeight + v.Len(8)
            // the last link's line, then room for the foot's fleuron (drawn at noteTop - 12) to stand clear of it
            + text.Wrap(about, width).Count * line + v.Len(6) + links * line + v.Len(28);
        var limit = noteTop - rest;
        var detail = Face(Assets.Italic, 12.5, v);
        var shownNotes = _notes.Take(4).Select((n, i) => (Title: n, Detail: i < _noteDetails.Count ? _noteDetails[i].Detail : "")).ToList();
        int Height((string Title, string Detail) n, bool withDetail) =>
            Math.Min(2, text.Wrap(n.Title, width - v.Len(16)).Count) * line + (withDetail && n.Detail.Length > 0 ? detail.LineHeight : 0) + v.Len(2);
        var withDetails = y + shownNotes.Sum(n => Height(n, true)) <= limit;
        NoteDetailsShown.Clear();
        foreach (var item in shownNotes)
        {
            var lines = text.Wrap(item.Title, width - v.Len(16)).Take(2).ToList();
            if (y + Height(item, withDetails) > limit && !withDetails)
            {
                break;
            }
            text.Draw(canvas, "•", x + v.Len(2), y, InkSoft);
            foreach (var l in lines)
            {
                text.Draw(canvas, l, x + v.Len(16), y, Ink);
                y += line;
            }
            if (withDetails && item.Detail.Length > 0)
            {
                var shown = ClauseThatFits(detail, item.Detail, width - v.Len(16));
                detail.Draw(canvas, shown, x + v.Len(16), y - v.Len(2), InkSoft);
                NoteDetailsShown.Add((new Rect(x + v.Len(16), y - v.Len(2), detail.Measure(shown), detail.LineHeight), shown));
                y += detail.LineHeight;
            }
            y += v.Len(2);
        }
        if (_context.ReleaseNotesLink is not null)
        {
            _notesLink = LinkLine(canvas, v, text, x, y, width, "", "", _notes.Count == 0 ? "Read the release notes" : "All the release notes");
            y += line;
        }
        y += v.Len(4);
        Fleuron(canvas, v, (x + right) / 2, y + v.Len(8), width / 2 - v.Len(10), InkSoft, onDark: false);
        y += v.Len(20);
        y = Capitalised(canvas, v, x, y, "About this project", "A free, open-source fan restoration.");
        y += v.Len(8);
        foreach (var l in text.Wrap(about, width))
        {
            text.Draw(canvas, l, x, y, Ink);
            y += line;
        }
        y += v.Len(6);
        _discord = LinkLine(canvas, v, text, x, y, width, "Questions, bugs, ideas: ", "Questions: ", "join us on Discord");
        if (_context.PackGuide is not null)
        {
            y += line;
            _guideLink = LinkLine(canvas, v, text, x, y, width, "Your own pictures, music and sounds: ", "Your own art: ", "how to make a pack");
        }
        if (_context.ReportProblem is not null)
        {
            y += line;
            _reportLink = LinkLine(canvas, v, text, x, y, width, "Something broken? ", "", "make a problem report");
        }

        Fleuron(canvas, v, (x + right) / 2, noteTop - v.Len(12), width / 2 - v.Len(10), InkSoft, onDark: false);
        foreach (var l in bottomNote)
        {
            small.Draw(canvas, l, x, noteTop, InkSoft);
            noteTop += small.LineHeight;
        }
    }

    /// <summary>A line with a link: the link icon, a lead-in (the shorter one when the line would not fit) and the underlined link; returns the link's area.</summary>
    private Rect LinkLine(Canvas canvas, View v, TextFace text, int x, int y, int width, string lead, string shortLead, string link)
    {
        var icon = Assets.Art("icon-link") is null ? v.Len(11) : v.Len(16);
        if (icon + v.Len(9) + text.Measure(lead) + text.Measure(link) > width)
        {
            lead = shortLead;
        }
        var iy = y + (text.LineHeight - icon) / 2;
        Icon(canvas, v, "icon-link", new Rect(x, iy, icon, icon), LinkRed);
        var after = text.Draw(canvas, lead, x + icon + v.Len(9), y, Ink);
        text.Draw(canvas, link, after, y, LinkRed);
        var under = y + text.Ascent + Math.Max(1, v.Len(2));
        canvas.Fill(new Rect(after, under, text.Measure(link), Math.Max(1, v.Len(1))), LinkRed);
        return new Rect(after, y, text.Measure(link), under + Math.Max(1, v.Len(1)) - y);
    }

    /// <summary>A heading with its first letter in a red illuminated square, and an italic caption under it; returns the y below.</summary>
    private int Capitalised(Canvas canvas, View v, int x, int y, string heading, string caption)
    {
        var box = new Rect(x, y, v.Len(46), v.Len(46));
        if (Assets.Art("dropcap") is { } frame)
        {
            ImageOps.Draw(canvas, frame, box);
        }
        else
        {
            canvas.Fill(box, DropCapRed);
            canvas.Frame(box, Math.Max(1, v.Len(2)), DropCapDark);
            canvas.Frame(box.Inset(Math.Max(2, v.Len(4))), Math.Max(1, v.Len(1)), Gold);
        }
        var capital = Face(Assets.Strong, 38, v);
        var letter = heading.AsSpan(0, 1);
        StrongText(canvas, v, capital, letter, box.X + (box.Width - capital.Measure(letter)) / 2, box.Y + (box.Height - capital.LineHeight) / 2 + v.Len(1), Cream);
        var face = Face(Assets.Strong, 25, v);
        var under = Face(Assets.Italic, 14, v);
        StrongText(canvas, v, face, heading.AsSpan(1), box.Right + v.Len(10), y - v.Len(1), Ink);
        under.Draw(canvas, caption, box.Right + v.Len(10), y + face.LineHeight - v.Len(3), InkSoft);
        return Math.Max(box.Bottom, y + face.LineHeight - v.Len(3) + under.LineHeight);
    }

    /// <summary>
    /// The settings panel: the title, the General and Advanced tabs, and the open tab's sections with their icons (General: Game
    /// installation, Display, Sound &amp; Music; Advanced: the Roland ROMs, the SoundFont, Start-up). It is drawn on one grid: rows of
    /// one height, a status line of its own height under a folder or pack, the same gap before each section, the labels, the values
    /// between ‹ › and the small plates (Browse…, Change…, Test sound) in three columns. Under the rows come the hint bar, then
    /// Save and quit at the left and Play, the larger, at the right. Every text is cut or wrapped to its place.
    /// </summary>
    private void DrawSettings(Canvas canvas, View v)
    {
        var frame = SettingsFrame(v);
        _panel = frame.Inset(v.Len(12));
        _statusCut.Clear();
        _statusShown.Clear();
        var x0 = frame.X + v.Len(34);
        var x1 = frame.Right - v.Len(34);
        var plateWidth = v.Len(PlateWidth);
        var layout = new RowLayout(
            x0 + v.Len(30),
            x1 - plateWidth,
            x0 + v.Len(132),
            x1 - plateWidth - v.Len(16),
            x1 - plateWidth - v.Len(16) - v.Len(84),
            x1);
        var title = Face(Assets.Display, 44, v);
        var heading = Face(Assets.Strong, 20.5, v);
        var text = Face(Assets.Text, 16, v);
        var line = Face(Assets.Italic, 13, v);
        var small = Face(Assets.Text, 13, v);
        var rowHeight = v.Len(RowHeight);
        var plateHeight = v.Len(PlateHeight);

        var y = frame.Y + v.Len(16);
        title.Draw(canvas, "Settings", x0, y, Ink);
        // the letters' own height: the display face's line box has much room above and below them
        Leaf(new Rect(x0, y + title.LineHeight / 5, title.Measure("Settings"), title.LineHeight * 3 / 5), "title");
        var ruleLeft = x0 + title.Measure("Settings") + v.Len(16);
        Fleuron(canvas, v, (ruleLeft + x1) / 2, y + title.LineHeight / 2, (x1 - ruleLeft) / 2, InkSoft, onDark: false);
        y += title.LineHeight - v.Len(4);
        y = Tabs(canvas, v, heading, x0, x1, y);

        int Section(string name, string icon, int top)
        {
            top += v.Len(SectionGap);
            var height = v.Len(HeadingHeight);
            var size = v.Len(22);
            Icon(canvas, v, icon, new Rect(x0, top + (height - size) / 2, size, size), InkSoft);
            var ty = top + (height - heading.LineHeight) / 2;
            StrongText(canvas, v, heading, name, x0 + v.Len(30), ty, Ink);
            Leaf(new Rect(x0, ty, v.Len(30) + heading.Measure(name) + (Assets.HasStrong ? 0 : v.Len(1)), heading.LineHeight - v.Len(3)), "heading " + name);
            return top + height;
        }

        // a status line: under its row, cut to fit (its whole text in the row's tooltip; the pointer on it shows that too)
        int Status(int top, Item item, (string Text, uint Colour) status)
        {
            var height = v.Len(StatusHeight);
            var size = v.Len(12);
            var iconArea = new Rect(layout.Label, top + (height - size) / 2, size, size);
            if (status.Colour == PaleRed)
            {
                Icon(canvas, v, "icon-problem", iconArea, InkRed);
            }
            else if (status.Colour == Green)
            {
                Icon(canvas, v, "icon-found", iconArea, InkGreen);
            }
            var tx = layout.Label + size + v.Len(6);
            var fitted = Fit(line, status.Text, layout.Right - tx, fromEnd: false);
            var ty = top + (height - line.LineHeight) / 2;
            line.Draw(canvas, fitted, tx, ty, OnParchment(status.Colour));
            var area = new Rect(layout.Label, Math.Max(top, ty), tx + line.Measure(fitted) - layout.Label, Math.Min(height, line.LineHeight));
            Leaf(area, "status " + status.Text);
            Hot(area, item, Part.Status);
            _statusShown[item] = status;
            if (fitted != status.Text)
            {
                _statusCut.Add(item);
            }
            return top + height;
        }

        // a row with its value between ‹ ›, and a plate at its end (its own Browse…, or another item's: Test sound)
        // at: the row's own columns (two selects side by side on one row)
        int Select(int top, string label, string value, Item item, string? plate = null, Item? plateItem = null, bool narrow = false, RowLayout? at = null)
        {
            var l = at ?? (narrow ? layout with { Select = layout.SelectNarrow, SelectEnd = layout.Right } : layout);
            if (plate is not null)
            {
                var owner = plateItem ?? item;
                Plate(canvas, v, new Rect(layout.Plate, top + (rowHeight - plateHeight) / 2, plateWidth, plateHeight), plate, small, owner, owner == item ? Part.Browse : Part.Row);
            }
            var row = new Rect(l.Label - v.Len(8), top, (plate is null ? l.Right : l.SelectEnd + v.Len(6)) - l.Label + v.Len(8), rowHeight);
            RowLight(canvas, row, item);
            Hot(row, item, Part.Row);
            var ty = top + (rowHeight - text.LineHeight) / 2;
            var labelShown = Fit(text, label, l.Select - l.Label - v.Len(10), fromEnd: false);
            text.Draw(canvas, labelShown, l.Label, ty, Ink);
            Leaf(new Rect(l.Label, ty, text.Measure(labelShown), text.LineHeight), "label " + label);

            var fixedBy = IsFixed(item, out _);
            var arrowLeft = fixedBy ? "" : "‹";
            var arrowRight = fixedBy ? "" : "›";
            var gap = v.Len(8);
            var room = l.SelectEnd - l.Select - text.Measure(arrowLeft) - text.Measure(arrowRight) - 2 * gap;
            var shown = Fit(text, value, Math.Max(0, room), fromEnd: false);
            var inner = l.Select + text.Measure(arrowLeft) + gap;
            text.Draw(canvas, arrowLeft, l.Select, ty, InkSoft);
            text.Draw(canvas, shown, inner + (room - text.Measure(shown)) / 2, ty, fixedBy ? InkSoft : Ink);
            text.Draw(canvas, arrowRight, l.SelectEnd - text.Measure(arrowRight), ty, InkSoft);
            Leaf(new Rect(l.Select, ty, l.SelectEnd - l.Select, text.LineHeight), "value " + label);
            if (!fixedBy)
            {
                // the arrows: a click on ‹ or › steps back or on, as ←/→
                Hot(new Rect(l.Select - v.Len(6), row.Y, inner - l.Select + v.Len(6), row.Height), item, Part.Previous);
                Hot(new Rect(l.SelectEnd - text.Measure(arrowRight) - gap, row.Y, text.Measure(arrowRight) + gap + v.Len(6), row.Height), item, Part.Next);
            }
            return top + rowHeight;
        }

        // a folder: its field (under the heading, or after a label) and its Change… plate
        int Folder(int top, string? label, string shown, Item item, Item plateItem, Part platePart)
        {
            var fieldLeft = label is null ? layout.Label : layout.Select;
            RowLight(canvas, new Rect(layout.Label - v.Len(8), top, layout.SelectEnd - layout.Label + v.Len(14), rowHeight), item);
            if (label is not null)
            {
                var labelY = top + (rowHeight - text.LineHeight) / 2;
                var labelShown = Fit(text, label, layout.Select - layout.Label - v.Len(10), fromEnd: false);
                text.Draw(canvas, labelShown, layout.Label, labelY, Ink);
                Leaf(new Rect(layout.Label, labelY, text.Measure(labelShown), text.LineHeight), "label " + label);
            }
            var fieldHeight = Math.Min(rowHeight - v.Len(4), v.Len(27));
            var field = new Rect(fieldLeft, top + (rowHeight - fieldHeight) / 2, layout.SelectEnd - fieldLeft, fieldHeight);
            Field(canvas, v, field, shown, _focus == item, IsFixed(item, out _), label is null ? text : small);
            Hot(field, item, Part.Row);
            Leaf(field, "field " + (label ?? "game folder"));
            Plate(canvas, v, new Rect(layout.Plate, top + (rowHeight - plateHeight) / 2, plateWidth, plateHeight), "Change…", small, plateItem, platePart);
            return top + rowHeight;
        }

        if (!_advanced)
        {
            y = Section("Game installation", "icon-game", y);
            y = Folder(y, null, _editing ? _editText + "_" : _model.GameFolder ?? "", Item.GameFolder, Item.Change, Part.Row);
            y = Status(y, Item.GameFolder, _model.GameFolderChecked is not null
                ? ("Found" + (_model.GameFolderSource is { } source ? $" ({source})." : "."), Green)
                : _model.GameFolder is null ? ("Type its folder, drop it on this window or press Change…", PaleCream) : ("No Darklands there (DARKLAND.EXE).", PaleRed));

            y = Section("Display", "icon-display", y);
            y = Select(y, "Window size", LauncherModel.SizeLabel(_model.Windows[_model.WindowIndex]), Item.Window);
            // Fullscreen and the picture filter side by side: one row (the tab has no height to spare for another)
            var fullscreenEnd = layout.Select + v.Len(92);   // Yes or No
            Select(y, "Fullscreen", _model.Fullscreen ? "Yes" : "No", Item.Fullscreen,
                at: layout with { SelectEnd = fullscreenEnd, Right = fullscreenEnd + v.Len(8) });
            var pictureLabel = fullscreenEnd + v.Len(30);
            y = Select(y, "Picture", _model.FilterLabel, Item.Picture,
                at: layout with { Label = pictureLabel, Select = pictureLabel + text.Measure("Picture") + v.Len(14), SelectEnd = layout.Right });
            y = Select(y, "HD pack", PackShown(PackKind.Pictures), Item.HdPack, "Browse…");
            y = Status(y, Item.HdPack, PackLine(PackKind.Pictures));

            // sound and music: the card with its sound test, whether its ROMs are there (they are chosen on Advanced), the audio pack
            y = Section("Sound & Music", "icon-sound", y);
            var test = _context.SoundTest is { } soundTest ? (soundTest.Playing ? "Stop" : "Test sound") : null;
            y = Select(y, "Sound card", _model.SoundCardLabel, Item.Sound, test, Item.Test);
            y = Status(y, Item.Sound, RomsStatus(_model.Roms));
            y = Select(y, "Audio pack", PackShown(PackKind.Sounds), Item.AudioPack, "Browse…");
            y = Status(y, Item.AudioPack, PackLine(PackKind.Sounds));
        }
        else
        {
            y = Section("Roland MT-32 / CM-32L", "icon-sound", y);
            y = Folder(y, "ROM folder", _model.Roms.Folder, Item.Roms, Item.Roms, Part.Browse);
            y = Status(y, Item.Roms, RomsStatus(_model.Roms));
            y = Section("General MIDI (for audio packs)", "icon-sound", y);
            y = Select(y, "SoundFont", SoundFontShown(), Item.SoundFont, "Browse…");
            y = Status(y, Item.SoundFont, SoundFontLine());
            y = Section("Start-up", "icon-startup", y);
            // the translation pack: one row (the tab has no height for a status line; a broken pack shows (!) and its problem in the details)
            y = Select(y, "Language pack", PackShown(PackKind.Language), Item.LanguagePack, "Browse…");
            y = Select(y, "Fonts", PackShown(PackKind.Fonts), Item.FontPack, "Browse…");
            // the start-up screen and the opening side by side: one row (the font pack's row takes the other)
            var bannerEnd = layout.Select + v.Len(92);   // Yes or No
            Select(y, "Start-up screen", _model.Banner ? "Yes" : "No", Item.Banner,
                at: layout with { SelectEnd = bannerEnd, Right = bannerEnd + v.Len(8) });
            var introLabel = bannerEnd + v.Len(30);
            y = Select(y, "Opening", _model.Intro ? "Yes" : "No", Item.Intro,
                at: layout with { Label = introLabel, Select = introLabel + text.Measure("Opening") + v.Len(14), SelectEnd = layout.Right });
            // the Interface switch and the mouse wheel side by side: one row (as the start-up screen and the opening)
            var interfaceEnd = layout.Select + v.Len(92);   // Comfort or Classic
            Select(y, "Interface", _model.ComfortInterface ? "Comfort" : "Classic", Item.Interface,
                at: layout with { SelectEnd = interfaceEnd, Right = interfaceEnd + v.Len(8) });
            var wheelLabel = interfaceEnd + v.Len(30);
            y = Select(y, "Mouse wheel", _model.MouseWheel ? "Yes" : "No", Item.MouseWheel,
                at: layout with { Label = wheelLabel, Select = wheelLabel + text.Measure("Mouse wheel") + v.Len(14), SelectEnd = layout.Right });
            // the rules preset of new games and the card menu keys side by side: one row (the tab has no height for another)
            Select(y, "Rules", _model.FixedRules ? "Fixed" : "Original", Item.Rules,
                at: layout with { SelectEnd = interfaceEnd, Right = interfaceEnd + v.Len(8) });
            var keysLabel = interfaceEnd + v.Len(30);
            y = Select(y, "Card menu keys", _model.CardMenuKeys ? "Yes" : "No", Item.CardMenuKeys,
                at: layout with { Label = keysLabel, Select = keysLabel + text.Measure("Card menu keys") + v.Len(14), SelectEnd = layout.Right });
        }

        // the panel's foot: Play at the right, the larger; Save and quit at the left, apart from it
        var play = new Rect(x1 - v.Len(212), frame.Bottom - v.Len(26) - v.Len(56), v.Len(212), v.Len(56));
        BigPlate(canvas, v, play, "Play", red: true, Item.Play);
        BigPlate(canvas, v, new Rect(x0, play.Y + v.Len(8), v.Len(160), v.Len(40)), "Save and quit", red: false, Item.Save);

        // over the buttons, only while there is one: the notice (two lines at most; its whole text in its tooltip)
        var noticeBottom = play.Y - v.Len(14);
        var noticeTop = Math.Max(y + v.Len(10), noticeBottom - v.Len(50));
        DrawNotice(canvas, v, Face(Assets.Italic, 14, v), new Rect(x0, noticeTop, x1 - x0, Math.Max(0, noticeBottom - noticeTop)));
    }

    /// <summary>The settings grid, in design pixels.</summary>
    private const double RowHeight = 32, StatusHeight = 18, SectionGap = 15, HeadingHeight = 28, PlateWidth = 88, PlateHeight = 24;

    /// <summary>The panel's two pages, General and Advanced: their names, the open one in ink and underlined in red, over a rule.</summary>
    private int Tabs(Canvas canvas, View v, TextFace face, int x0, int x1, int y)
    {
        var height = v.Len(34);
        var x = x0;
        foreach (var (name, item, open) in new[] { ("General", Item.TabGeneral, !_advanced), ("Advanced", Item.TabAdvanced, _advanced) })
        {
            var width = face.Measure(name);
            var ty = y + (height - face.LineHeight) / 2;
            if (!open)
            {
                RowLight(canvas, new Rect(x - v.Len(6), y + v.Len(2), width + v.Len(12), height - v.Len(6)), item);
            }
            StrongText(canvas, v, face, name, x, ty, open ? Ink : InkSoft);
            if (open)
            {
                canvas.Fill(new Rect(x - v.Len(2), y + height - v.Len(4), width + v.Len(4), Math.Max(2, v.Len(3))), DropCapRed);
            }
            Hot(new Rect(x - v.Len(8), y, width + v.Len(16), height), item, Part.Row);
            Leaf(new Rect(x - v.Len(2), y + v.Len(2), width + v.Len(4), height - v.Len(2)), "tab " + name);
            x += width + v.Len(30);
        }
        Shade(canvas, new Rect(x0, y + height - v.Len(1), x1 - x0, Math.Max(1, v.Len(1))), InkSoft, 120);
        return y + height;
    }

    /// <summary>The columns of the settings' rows: the labels, the plates, the value selectors (wide, or narrow for Yes / No).</summary>
    private readonly record struct RowLayout(int Label, int Plate, int Select, int SelectEnd, int SelectNarrow, int Right);

    /// <summary>
    /// A frameless window's own buttons at its top-right corner: minimise (left out when the window fills the screen, where it would do
    /// nothing: fullscreen, or gamescope's Game Mode) and close. Small dark plates, lit under the pointer, sunk while held.
    /// </summary>
    private void DrawWindowButtons(Canvas canvas, View v)
    {
        var width = Math.Max(20, v.Len(30));
        var height = Math.Max(17, v.Len(26));
        var margin = Math.Max(5, v.Len(8));
        var close = new Rect(canvas.Width - margin - width, margin, width, height);
        var buttons = _surface.FillsScreen
            ? new[] { (close, Item.Close) }
            : [(new Rect(close.X - Math.Max(4, v.Len(6)) - width, margin, width, height), Item.Minimise), (close, Item.Close)];
        foreach (var (area, item) in buttons)
        {
            var lit = _hover == item;
            var down = _pressed == item;
            var name = down && Assets.Art("plate-small-pressed") is not null ? "plate-small-pressed"
                : lit && Assets.Art("plate-small-hover") is not null ? "plate-small-hover" : "plate-small";
            if (Assets.Art(name) is { } art)
            {
                Sliced(canvas, v, art, name, area);
            }
            else
            {
                canvas.Fill(area, lit ? 0x4A4036U : 0x2C251EU);
                canvas.Frame(area, Math.Max(1, v.Len(1)), lit ? 0x7A6B58U : 0x16110CU);
            }
            var sink = down ? Math.Max(1, v.Len(1)) : 0;
            var cx = area.X + area.Width / 2 + sink;
            var cy = area.Y + area.Height / 2 + sink;
            var stroke = Math.Max(1, v.Len(2));
            var half = Math.Max(3, v.Len(6));
            var ink = item == Item.Close && lit ? PaleRed : Cream;
            if (item == Item.Minimise)
            {
                canvas.Fill(new Rect(cx - half, cy + half / 2, 2 * half, stroke), ink);
            }
            else
            {
                for (var i = -half; i <= half; i++)
                {
                    canvas.Fill(new Rect(cx + i - stroke / 2, cy + i - stroke / 2, stroke, stroke), ink);
                    canvas.Fill(new Rect(cx + i - stroke / 2, cy - i - stroke / 2, stroke, stroke), ink);
                }
            }
            Hot(area, item, Part.Row);
        }
    }

    /// <summary>A folder's field: a lighter inset on the parchment, the path cut at its start to keep its end; while it is typed in, framed in red.</summary>
    private void Field(Canvas canvas, View v, Rect field, string shown, bool focused, bool fixedBy, TextFace text)
    {
        canvas.Fill(field, FieldFill);
        var typing = focused && _editing;
        canvas.Frame(field, Math.Max(1, v.Len(typing ? 2 : 1)), typing ? DropCapRed : InkSoft);
        var fitted = Fit(text, shown, field.Width - v.Len(16), fromEnd: true);
        text.Draw(canvas, fitted, field.X + v.Len(8), field.Y + (field.Height - text.LineHeight) / 2, fixedBy ? InkSoft : Ink);
    }

    /// <summary>A small dark plate (Browse…, Change…, Test sound): plate-small's art, or drawn; lit (its hover art) under the pointer, or when it is the keyboard's choice itself.</summary>
    private void Plate(Canvas canvas, View v, Rect area, string label, TextFace text, Item item, Part part)
    {
        var lit = _hover == item && _hoverPart == part || _focus == item && part == Part.Row;
        var down = _pressed == item && _pressedPart == part;
        var plateName = down && Assets.Art("plate-small-pressed") is not null ? "plate-small-pressed"
            : lit && Assets.Art("plate-small-hover") is not null ? "plate-small-hover" : "plate-small";
        if (Assets.Art(plateName) is { } art)
        {
            Sliced(canvas, v, art, plateName, area);
        }
        else
        {
            canvas.Paint(area, (_, y) => Colours.Mix(0x4A4036, 0x2C251E, area.Height <= 1 ? 0 : 255 * y / (area.Height - 1)));
            canvas.Fill(new Rect(area.X, area.Y, area.Width, Math.Max(1, v.Len(1))), 0x7A6B58);
            canvas.Frame(area, Math.Max(1, v.Len(1)), 0x16110C);
        }
        if (lit && Assets.Art("plate-small-hover") is null)
        {
            Shade(canvas, area, LampLight, 40);
        }
        if (down && Assets.Art("plate-small-pressed") is null)
        {
            Shade(canvas, area, Black, 70);
        }
        var shown = Fit(text, label, area.Width - v.Len(6), fromEnd: false);
        var sink = down ? Math.Max(1, v.Len(1)) : 0;
        text.Draw(canvas, shown, area.X + (area.Width - text.Measure(shown)) / 2 + sink, area.Y + (area.Height - text.LineHeight) / 2 + sink, Cream);
        Hot(area, item, part);
        Leaf(area, "button " + label);
    }

    /// <summary>Save and quit (a stone plate) and Play (a red plate with gold), from their art or drawn.</summary>
    private void BigPlate(Canvas canvas, View v, Rect area, string label, bool red, Item item)
    {
        if (Assets.Art(red ? "plate-red" : "plate-stone") is { } art)
        {
            Sliced(canvas, v, art, red ? "plate-red" : "plate-stone", area);
        }
        else
        {
            var (top, bottom, edge) = red ? (0xA2281AU, 0x5E0E08U, 0x2E0603U) : (0xDAD3C5U, 0x9E9687U, 0x3E372EU);
            canvas.Paint(area, (_, y) => Colours.Mix(top, bottom, area.Height <= 1 ? 0 : 255 * y / (area.Height - 1)));
            canvas.Frame(area, Math.Max(1, v.Len(2)), edge);
            canvas.Frame(area.Inset(Math.Max(2, v.Len(4))), Math.Max(1, v.Len(1)), red ? Gold : 0xF4EFE4);
        }
        // lit: a warm glow, full under the pointer, soft for the keyboard's choice (no frame)
        if (_hover == item || _focus == item)
        {
            Glow(canvas, v, area, _hover == item ? 1 : 0.5);
        }
        var down = _pressed == item;
        if (down)
        {
            // held down: darker, and its word sunk by a pixel
            Shade(canvas, area.Inset(Math.Max(1, v.Len(2))), Black, 60);
        }
        // both in the text face ("Play" a little larger, the main button)
        var face = red ? Face(Assets.Strong, 23, v) : Face(Assets.Strong, 19, v);
        var colour = red ? (_model.CanPlay ? Cream : 0xC9B9A0) : Ink;
        var textX = area.X + (area.Width - face.Measure(label)) / 2 + (down ? Math.Max(1, v.Len(1)) : 0);
        var textY = area.Y + (area.Height - face.LineHeight) / 2 + (down ? Math.Max(1, v.Len(1)) : 0);
        if (red)
        {
            face.Draw(canvas, label, textX, textY, colour, Math.Max(1, v.Len(1.5)), Black);
            var d = v.Len(5);
            Diamond(canvas, textX - v.Len(22), area.Y + area.Height / 2, d, Gold);
            Diamond(canvas, textX + face.Measure(label) + v.Len(22), area.Y + area.Height / 2, d, Gold);
        }
        else
        {
            StrongText(canvas, v, face, label, textX, textY, colour);
        }
        Hot(area, item, Part.Row);
        Leaf(area, "button " + label);
    }

    /// <summary>
    /// The notice over the buttons, only while there is something to tell: the last message (what an action did, why it could
    /// not), else why the launcher is shown (the game could not start, it was not found, the first run). A dark plate with its
    /// text wrapped to two lines at most (long paths broken anywhere), the last cut with "…" when there is more: the pointer
    /// resting on it shows the whole text.
    /// </summary>
    private void DrawNotice(Canvas canvas, View v, TextFace face, Rect room)
    {
        var parts = NoticeParts().ToList();
        var pad = v.Len(6);
        if (parts.Count == 0 || room.Height < face.LineHeight + 2 * pad)
        {
            return;
        }
        var icon = v.Len(16);
        var textX = room.X + pad + icon + v.Len(10);
        var lineWidth = room.Right - pad - textX;
        var lines = parts.SelectMany(p => WrapAll(face, p.Text, lineWidth).Select(l => (Text: l, p.Colour))).ToList();
        var fits = Math.Max(1, Math.Min(2, (room.Height - 2 * pad) / face.LineHeight));
        if (lines.Count > fits)
        {
            var last = lines[fits - 1];
            lines = [.. lines.Take(fits - 1), (Fit(face, last.Text + " …", lineWidth, fromEnd: false), last.Colour)];
        }
        // as tall as its lines, at the bottom of its room (next to the buttons)
        var height = lines.Count * face.LineHeight + 2 * pad;
        var bar = new Rect(room.X, room.Bottom - height, room.Width, height);
        Shade(canvas, bar, HintFill, 215);
        canvas.Frame(bar, Math.Max(1, v.Len(1)), 0x8A7454);
        Leaf(bar, "notice");
        Hot(bar, Item.Notice, Part.Row);
        Icon(canvas, v, "icon-problem", new Rect(bar.X + pad, bar.Y + pad + (face.LineHeight - icon) / 2, icon, icon), Cream);
        var ly = bar.Y + pad;
        foreach (var (text, colour) in lines)
        {
            face.Draw(canvas, text, textX, ly, colour);
            ly += face.LineHeight;
        }
    }

    /// <summary>The notice's text: the sound test's state, else the last message, else why the launcher is shown; nothing else.</summary>
    private IEnumerable<(string Text, uint Colour)> NoticeParts()
    {
        if (_showSound && _context.SoundTest is { Status: { } soundStatus } soundTester)
        {
            yield return (soundStatus, soundTester.Playing ? Green : PaleRed);
        }
        else if (_message is not null)
        {
            yield return (_message, _messageColour);
        }
        else if (_model.FontsNote is { } fonts)
        {
            // the language pack's letters and the fonts: why the HD fonts are on, or that its letters show as '?' without them
            yield return (fonts.Text, fonts.Warning ? PaleRed : Green);
        }
        else if (_context.Reason switch
                 {
                     LauncherReason.StartFailed => _context.Problem ?? "The game could not start.",
                     LauncherReason.GameNotFound when !_model.CanPlay => "Darklands was not found: choose its folder to play.",
                     LauncherReason.FirstRun => "Welcome! Check the settings, then press Play.",
                     _ => null,
                 } is { } note)
        {
            yield return (note, _context.Reason == LauncherReason.FirstRun ? Green : PaleRed);
        }
    }

    /// <summary>
    /// A control's tooltip: its problem in full (a pack's, or its status line's when that is a problem or was cut to fit), then
    /// what it does; the notice's: its whole text.
    /// </summary>
    internal IEnumerable<(string Text, uint Colour)> TooltipParts(Item item)
    {
        if (item == Item.Notice)
        {
            foreach (var part in NoticeParts())
            {
                yield return part;
            }
            yield break;
        }
        var row = item == Item.Change ? Item.GameFolder : item;
        // a folder's field keeps only the end of a long path: the whole path heads its tooltip
        if (!_editing && (row == Item.GameFolder ? _model.GameFolder : row == Item.Roms ? _model.Roms.Folder : null) is { Length: > 0 } path)
        {
            yield return (path, Cream);
        }
        if (row is Item.HdPack or Item.AudioPack or Item.LanguagePack or Item.FontPack
            && PackProblem(row switch { Item.HdPack => PackKind.Pictures, Item.AudioPack => PackKind.Sounds, Item.FontPack => PackKind.Fonts, _ => PackKind.Language }) is { } problem)
        {
            yield return ("Problem: " + problem, PaleRed);
        }
        else if (_statusShown.TryGetValue(row, out var status) && (status.Colour == PaleRed || _statusCut.Contains(row)))
        {
            yield return (status.Text, status.Colour == PaleCream ? Cream : status.Colour);
        }
        if (Help(item) is { Length: > 0 } help)
        {
            yield return (help, PaleCream);
        }
    }

    /// <summary>
    /// The tooltip of <see cref="_tip"/>: a dark plate under its control (over it when there is no room below), as wide as the
    /// settings panel's inside at most, with the parts wrapped; kept in the window.
    /// </summary>
    private void DrawTooltip(Canvas canvas, View v)
    {
        TooltipArea = default;
        if (_tip == Item.None || TooltipParts(_tip).ToList() is not { Count: > 0 } parts)
        {
            return;
        }
        var spot = _hotspots.FirstOrDefault(h => h.Item == _tip && h.Part == _tipPart);
        if (spot.Item == Item.None)
        {
            spot = _hotspots.FirstOrDefault(h => h.Item == _tip);
        }
        if (spot.Item == Item.None)
        {
            return;
        }
        var anchor = spot.Area;
        var face = Face(Assets.Italic, 14, v);
        var pad = v.Len(7);
        var width = Math.Min(canvas.Width - 2 * v.Len(4), _panel.Width - v.Len(20));
        var lines = parts.SelectMany(p => WrapAll(face, p.Text, width - 2 * pad).Select(l => (Text: l, p.Colour))).Take(8).ToList();
        var height = lines.Count * face.LineHeight + 2 * pad;
        var gap = v.Len(4);
        // in the settings panel when its control is (else in the window)
        var inPanel = _panel.Contains(anchor.X, anchor.Y);
        var (left, right) = inPanel ? (_panel.X, _panel.Right) : (v.Len(4), canvas.Width - v.Len(4));
        var x = Math.Clamp(anchor.X, left, Math.Max(left, right - width));
        var box = new Rect(x, PlaceTooltip(canvas, v, anchor, inPanel, x, width, height, gap), width, height);
        Shade(canvas, new Rect(box.X + v.Len(3), box.Y + v.Len(4), box.Width, box.Height), Black, 70);
        Shade(canvas, box, HintFill, 238);
        canvas.Frame(box, Math.Max(1, v.Len(1)), 0x8A7454);
        var ly = box.Y + pad;
        foreach (var (text, colour) in lines)
        {
            face.Draw(canvas, text, box.X + pad, ly, colour);
            ly += face.LineHeight;
        }
        TooltipArea = box;
    }

    /// <summary>
    /// The tooltip's top: under its control, else over it, else (a control of the settings panel) in the free band over Play, the
    /// first of them that covers no other control, heading, label or status line; when none is free, the one in the window that
    /// covers the fewest (under the control first).
    /// </summary>
    private int PlaceTooltip(Canvas canvas, View v, Rect anchor, bool inPanel, int x, int width, int height, int gap)
    {
        var margin = v.Len(4);
        // what the plate must leave visible: every other control and every drawn text but the control's own
        var others = _hotspots.Select(h => h.Area).Concat(_blocks.Select(b => b.Area)).Where(a => !a.Intersects(anchor)).ToList();
        bool InWindow(int top) => top >= margin && top + height <= canvas.Height - margin;
        int Covered(int top)
        {
            var r = new Rect(x, top, width, height);
            return others.Count(o => o.Intersects(r));
        }
        var candidates = new List<int> { anchor.Bottom + gap, anchor.Y - gap - height };
        if (inPanel && _hotspots.FirstOrDefault(h => h.Item == Item.Play).Area is { Height: > 0 } play)
        {
            candidates.Add(play.Y - gap - height);
        }
        var fitting = candidates.Where(InWindow).ToList();
        if (fitting.Count == 0)
        {
            return Math.Clamp(anchor.Bottom + gap, margin, Math.Max(margin, canvas.Height - margin - height));
        }
        // the first free one, else the least covering (MinBy keeps the earlier of equals)
        return fitting.FirstOrDefault(t => Covered(t) == 0, fitting.MinBy(Covered));
    }

    /// <summary>A row lit: brightly under the pointer, softly when the keyboard chose it (no frame).</summary>
    private void RowLight(Canvas canvas, Rect row, Item item)
    {
        var alpha = _hover == item ? 110 : _focus == item ? 60 : 0;
        if (alpha > 0)
        {
            Shade(canvas, row, LampLight, alpha);
        }
    }

    /// <summary>A warm light on parchment and plates (hover and the keyboard's choice).</summary>
    private const uint LampLight = 0xFFF4DA;

    /// <summary>The glow around a lit plate: candlelight.</summary>
    private const uint GlowLight = 0xFFD98A;
}
