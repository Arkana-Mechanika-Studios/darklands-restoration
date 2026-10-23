using Darklands.Cards;
using Darklands.Content.Formats.Graphics;
using Darklands.Core;
using Darklands.Core.Characters;
using Darklands.Core.Rules;
using Darklands.Platform;
using Darklands.Presentation.Graphics;

namespace Darklands.Presentation;

/// <summary>
/// The prayer screen: a saint's picture, name, feast day and description over the card window,
/// the divine favor offered and the chance, and four lines — more favor, less favor, pray, don't
/// pray. The party panel on the left stays; a click on the praying member's row leaves.
/// </summary>
/// <remarks>
/// Opened from a card's saint pop-up (<see cref="PrayerMode.ForParty"/>, through
/// <see cref="CardContext.SaintPrayer"/>) and from the character sheet's saints list
/// (<see cref="PrayerMode.ForMember"/>).
/// </remarks>
// orig: saint_pray_party R034+0x0000, saint_pray R034+0x0102, saint_screen R034+0x022C,
//   saint_finish R034+0x06E2, saint_load_bg R034+0x0720, saint_draw R034+0x0878,
//   saint_key R034+0x08D2, saint_draw_df R034+0x09B2, saint_draw_menu R034+0x0AA4,
//   saint_draw_info R034+0x0BC4, saint_text_box R034+0x0D08, saint_party_box R034+0x0E04
//   R034+0x0000..0x0E04 (1462:xxxx) matched; invoke_saint
//   R029+0x2180 asm (= saint_pray_party(who, s, 1), then the card is redrawn);
//   tests: SaintPrayerTests (rules), the headless "saint" step (looks)
// orig: the music: DS:9C3D = thunk 09C0:18F1(5), thunk 09C0:18F1(13) before the screen, thunk 09C0:18F1(DS:9C3D) on every way out;
//   saint_pray does neither in a battle (DS:907A == 1) and plays back only a kept piece other than -1; tests: MusicTests
// DIVERGENCE: the original redraws the whole screen when a hot key's box asks for it (DS:9C46); here the
//   hot keys put back what they cover themselves. Alt-Q is the game-wide hot key (the screen's own
//   0x1000 case, saint_finish then quit, is not reached).
public sealed class SaintPrayerScreen(GameUi ui, PartyPanel panel)
{
    // Colours of the saint pictures' palettes (the screen's gfx colour is 8).
    private const byte TextColor = 8;
    private const byte HighlightColor = 9;
    private const byte DisabledColor = 7;
    private const byte BoxBorder = 1;
    private const byte BoxBackground = 0xFF;

    // The menu: four lines of 8 pixels at (0x94, 0x9E); hover area 148 < x < 318, 158 < y < 190.
    private const int MenuX = 0x94;
    private const int MenuY = 0x9E;
    private const int MenuStep = 8;
    private const int MenuWidth = 0xA9;
    private const int PrayLine = 2;

    // The member list for "Pray now for ...": a 60 × 50 box at (0xFA, 0x8E), rows of 9 pixels.
    private const int BoxX = 0xFA;
    private const int BoxY = 0x8E;
    private const int BoxWidth = 60;
    private const int BoxHeight = 0x32;
    private const int BoxRow = 9;

    private readonly Canvas _canvas = ui.Canvas;
    private readonly TextPainter _text = new(ui.Assets.Font(2));
    private readonly MessageBox _message = new(ui);
    private Pic? _background;
    private SavedRegion? _box;

    /// <summary>The prayer screen for card scripts and the saints' help for them, wired to a card context.</summary>
    public static void Attach(CardContext context, GameUi ui, PartyPanel panel)
    {
        var screen = new SaintPrayerScreen(ui, panel);
        context.SaintEffects = SaintEffects.Instance;
        context.SaintPrayer = (who, saint) => (int)screen.Pray(context.State, who, saint, PrayerMode.ForParty, context.SaintEffects);
    }

    /// <summary>
    /// Member <paramref name="member"/> prays to <paramref name="saint"/>: the screen until the
    /// player prays or leaves, then the prayer. Returns what the original's caller receives.
    /// </summary>
    // orig: saint_pray_party R034+0x0000 / saint_pray R034+0x0102
    public PrayerOutcome Pray(GameState state, int member, int saint, PrayerMode mode, ISaintEffects effects)
    {
        // orig: saint_pray_party always, saint_pray `if (DS:907A != 1)`: DS:9C3D = music_play(5); music_play(13)
        var music = mode == PrayerMode.ForParty || InBattle?.Invoke() != true;
        if (music)
        {
            ui.SaveMusic();
            ui.PlayMusic(PrayerMusic);
        }
        var saved = _canvas.SaveScreen();
        var prayer = new SaintPrayer(state, member, saint, mode);
        var target = Run(state, prayer);
        if (target is not { } who)
        {
            Leave(saved, state);
            PlayBack(mode, music);
            return PrayerOutcome.Cancelled;
        }
        // orig: saint_pray: "Saint Call Successful" before the help, "Saint Call Fails" after the extra loss;
        //   saint_pray_party says nothing. The rolls and the favor spent are the same (SaintPrayer.Pray).
        var success = prayer.Pray(who, effects);
        if (mode == PrayerMode.ForMember)
        {
            // DIVERGENCE: the success message comes after the help here (the help's own change reports, when
            //   "saintly changes" is on, therefore come first); the original shows it before.
            _message.Show(success ? "Saint Call Successful" : "Saint Call Fails", MessageMilliseconds);
        }
        Leave(saved, state);
        PlayBack(mode, music);
        return prayer.Outcome(success);
    }

    /// <summary>The prayer's music (the piece playing before is kept and played back after).</summary>
    private const int PrayerMusic = 13;

    /// <summary>True inside a battle (DS:907A == 1): a member's prayer then leaves the music alone.</summary>
    public Func<bool>? InBattle { get; init; }

    // orig: saint_pray_party: music_play(DS:9C3D) on each of its three returns; saint_pray: `if (DS:907A != 1 && DS:9C3D != -1)`
    //   music_play(DS:9C3D) on both of its returns
    private void PlayBack(PrayerMode mode, bool music)
    {
        if (mode == PrayerMode.ForParty || music && ui.SavedMusic.Value != -1)
        {
            ui.RestoreMusic();
        }
    }

    /// <summary>The original's message wait: 100 timer ticks (root 0000:24BC(msg, 100)).</summary>
    // C: root 0000:24BC is taken to be the framed message box MessageBox shows (60 ticks there).
    private const int MessageMilliseconds = MessageBox.MessageMilliseconds * 100 / 60;

    private void Leave(SavedScreen saved, GameState state)
    {
        _canvas.Restore(saved);
        panel.Draw(_canvas, state.Party);   // the Divine Favor bars show the offer spent (root 0000:1716)
        ui.Present();
    }

    /// <summary>The screen's loop: returns the member prayed for (−1: the leader), or null when the player leaves.</summary>
    // orig: saint_screen R034+0x022C
    private int? Run(GameState state, SaintPrayer prayer)
    {
        var party = state.Party;
        _box = null;
        DrawAll(prayer);
        // orig: R034 saint screen, matched C: the button waited out before the loop; then each pass tests the button's state:
        // with it up the box's and the menu's rows light under the pointer; held over the box or the menu, it is waited out and
        // the row under the pointer after the release counts; held over the member's own row of the party column, the screen
        // returns at once (no wait); then the menu bar and a waiting key, which replaces the mouse's; tests: ScreenInputTests
        ui.WaitForButtonRelease();
        var menuRow = -1;
        var boxRow = -1;
        while (true)
        {
            var key = 0;
            var mouse = ui.Mouse;
            var (mx, my) = (mouse.X, mouse.Y);
            var inBox = _box is not null && mx > 250 && mx < 310 && my > 142 && my < 192;
            var inMenu = mx > 148 && mx < 318 && my > 158 && my < 190;
            if (inBox && !mouse.Left)
            {
                // orig: rows (y − 142) / 9, highlighted at (0xFB, (row + 16) × 9), 0x3B × 9
                var row = (my - 142) / BoxRow;
                if (row != boxRow)
                {
                    Recolor(0xFB, (boxRow + 16) * BoxRow, 0x3B, BoxRow, HighlightColor, TextColor);
                    boxRow = row;
                    Recolor(0xFB, (row + 16) * BoxRow, 0x3B, BoxRow, TextColor, HighlightColor);
                }
            }
            else if (inBox)
            {
                // orig: DS:89EA = R029+0x4F76(0, 4, (y − 142) / 9) with the pointer after the release; prays only if row < party_size
                // and the slot is used
                // C: the row is a slot number, not a list position: with an empty slot before a member, the list
                //   and the slots disagree, as in the original.
                ui.WaitForButtonRelease();
                var slot = Math.Clamp((ui.Mouse.Y - 142) / BoxRow, 0, 4);
                if (slot < party.Members.Count && party.Members[slot].IsPresent)
                {
                    CloseMemberList();
                    return slot;
                }
            }
            else if (inMenu && !mouse.Left)
            {
                var row = (my - 158) / MenuStep;
                if (row != menuRow)
                {
                    Recolor(MenuX, menuRow * MenuStep + 158, MenuWidth, MenuStep, HighlightColor, TextColor);
                    menuRow = row;
                    if (row == PrayLine && prayer.ChoosesMember && _box is null && prayer.CanPray)
                    {
                        Recolor(MenuX, row * MenuStep + 158, MenuWidth, MenuStep, TextColor, HighlightColor);
                        OpenMemberList(party);
                        boxRow = -1;
                    }
                    else
                    {
                        if (row != PrayLine)
                        {
                            CloseMemberList();
                        }
                        // C: the original highlights from x 0x95 here (0x94 above): the first pixel column keeps its colour.
                        Recolor(MenuX + 1, row * MenuStep + 158, MenuWidth, MenuStep, TextColor, HighlightColor);
                    }
                }
            }
            else if (inMenu)
            {
                // C: the row after the release, (y − 158) / 8 truncated toward zero: a row outside 0..3 gives no key
                ui.WaitForButtonRelease();
                key = ((ui.Mouse.Y - 158) / MenuStep) switch
                {
                    0 => 0x324D,   // M
                    1 => 0x264C,   // L
                    2 => 0x1950,   // P
                    3 => 0x2044,   // D
                    _ => 0,
                };
            }
            else if (_box is not null)
            {
                CloseMemberList();
            }
            mouse = ui.Mouse;
            if (mouse.Left && mouse.X < 60 && mouse.Y / 40 == prayer.Member)
            {
                CloseMemberList();
                return null;
            }
            // DIVERGENCE: root 0000:0000 is GameUi.Poll's (MenuBar, HotKeys); the party_icons_dirty redraw is not modelled.
            key = ui.KeyAfterTheMouse(key);
            if (key is Keys.Escape or 0x2044 or 0x2064)
            {
                CloseMemberList();
                return null;
            }
            if (key is 0x1950 or 0x1970 && prayer.CanPray)
            {
                CloseMemberList();
                return Who.Leader;
            }
            OnKey(key, prayer);
        }
    }

    /// <summary>More or less favor (the status line is redrawn).</summary>
    // orig: saint_key R034+0x08D2
    // DORMANT: saint_key's D case (0x2044/0x2064: redraw the menu) — saint_screen returns on D before calling it.
    private void OnKey(int key, SaintPrayer prayer)
    {
        var redraw = key switch
        {
            0x0C2D or 0x264C or 0x266C or 0x4A2D => prayer.OfferLess(),
            0x0D2B or 0x324D or 0x326D or 0x4E2B => prayer.OfferMore(),
            _ => false,
        };
        if (redraw)
        {
            DrawStatus(prayer);
        }
    }

    // ---- drawing --------------------------------------------------------------------

    private void DrawAll(SaintPrayer prayer)
    {
        LoadBackground(prayer.Saint);
        CopyBackground(0x3C, 0, 0x104, 0xC8);
        DrawStatus(prayer);
        DrawMenu(prayer);
        DrawInfo(prayer.Saint);
        ui.Present();
    }

    /// <summary>The saint's picture: sntback&lt;class&gt;.pic (classes 10..13 use 0, others 1).</summary>
    // orig: saint_load_bg R034+0x0720
    private void LoadBackground(int saint)
    {
        var picture = SaintTable.Of(saint).Background switch
        {
            >= 0 and <= 9 and var n => $"sntback{n}",
            >= 10 and <= 13 => "sntback0",
            _ => "sntback1",
        };
        _background = ui.Assets.PictureExists(picture) ? ui.Assets.Picture(picture) : null;
        if (_background is not null)
        {
            _canvas.UsePalette(_background);
        }
    }

    /// <summary>Puts back a rectangle of the saint's picture.</summary>
    private void CopyBackground(int x, int y, int width, int height)
    {
        if (_background is null)
        {
            _canvas.Fill(x, y, width, height, Palette.Blank);
            return;
        }
        var image = _background.Image;
        for (var row = y; row < y + height && row < image.Height; row++)
        {
            for (var column = x; column < x + width && column < image.Width; column++)
            {
                _canvas[column, row] = image.Pixels[row * image.Width + column];
            }
        }
    }

    /// <summary>"Success: n" and "DF Remaining: nn".</summary>
    // orig: saint_draw_df R034+0x09B2 (it also closes the member list)
    private void DrawStatus(SaintPrayer prayer)
    {
        CloseMemberList();
        CopyBackground(0x9F, 0x93, 0x97, 8);
        _text.Draw(_canvas, 0xDB, 0x93, $"DF Remaining: {prayer.FavorRemaining:00}", TextColor);
        _text.Draw(_canvas, 0x9F, 0x93, $"Success: {prayer.ShownChance}", TextColor);
        ui.Present();
    }

    /// <summary>The four lines, their key letter in colour 12; the prayer line grey while the chance is 0.</summary>
    // orig: saint_draw_menu R034+0x0AA4: "\214 M\377ore divine favor is spent" ...
    // C: \214 (0x8C) and \377 are the driver's colour codes (drv+0x0FDC: colour = byte & 0x7F, 0x7F back to the call's colour): the key
    //   letter in colour 12, the rest in the window colour, as "\x8e+\x8f" in the battle panel. checked against asm
    private void DrawMenu(SaintPrayer prayer)
    {
        DrawLine(MenuY, " M", "ore divine favor is spent", TextColor);
        DrawLine(MenuY + MenuStep, " L", "ess divine favor is spent", TextColor);
        DrawLine(MenuY + 2 * MenuStep, " P", prayer.ChoosesMember ? "ray now for ..." : "ray now for the entire party",
            prayer.CanPray ? TextColor : DisabledColor);
        DrawLine(MenuY + 3 * MenuStep, " D", "on't pray right now", TextColor);
    }

    private void DrawLine(int y, string key, string rest, byte color)
    {
        var x = _text.Draw(_canvas, MenuX, y, key, 12);
        _text.Draw(_canvas, x, y, rest, color);
    }

    /// <summary>The saint's name with an illuminated capital, the feast day, the description.</summary>
    // orig: saint_draw_info R034+0x0BC4
    // UNCERTAIN: the text routine root 0265:0134 is assembly only: the name is taken as centred in its
    //   154-pixel column with the capital at y 8 (positions from a capture of the original), the description as
    //   wrapped in 156 pixels from (0x94, 0x25).
    private void DrawInfo(int saint)
    {
        DrawTitle(SaintName(saint));
        _text.Draw(_canvas, 0x4C, 0xAD, "Saint Day", TextColor);
        _text.Draw(_canvas, 0x62, 0xB5, "is", TextColor);
        var day = SaintTable.Of(saint).FeastDay;
        var date = $"{TextVariables.MonthNames[Quests.MonthOf(day)]} {Quests.DayOfMonth(day)}";
        var width = Math.Min(_text.Measure(date), 50);
        _text.Draw(_canvas, (50 - width) / 2 + 75, 0xBD, date, TextColor);
        new CardTextLayout(_text).Draw(_canvas, TextPainter.Encode(ui.Assets.SaintDescription(saint)), 0x94, 0x25, 0x9C,
            new CardTextLayout.Settings { Color = TextColor });
    }

    /// <summary>The saint's full name (saint_names R136+0xF16E, from DARKLAND.LST).</summary>
    private string SaintName(int saint)
    {
        _names ??= Content.Formats.World.DarklandListDecoder.DecodeBytes(ui.Assets.Files.Read("DARKLAND.LST"), "DARKLAND.LST").Saints;
        return saint >= 0 && saint < _names.Count ? _names[saint].FullName : "";
    }

    private IReadOnlyList<Content.Formats.World.DarklandSaintName>? _names;

    private void DrawTitle(string name)
    {
        if (name.Length == 0)
        {
            return;
        }
        var cap = DropCaps.Get(ui.Assets, (byte)name[0]);
        var tail = cap is null ? name : name[1..];
        var width = _text.Measure(tail) + (cap is null ? 0 : 0x11);
        var x = 0x94 + Math.Max(0, (0x9A - width) / 2);
        if (cap is not null)
        {
            _canvas.Draw(cap, x, 8, transparent: true);
            x += 0x11;
        }
        _text.Draw(_canvas, x, 0x11, tail, TextColor);
    }

    // ---- the member list --------------------------------------------------------------

    /// <summary>The present members' names, one per row, in a framed box.</summary>
    // orig: saint_party_box R034+0x0E04 and saint_text_box R034+0x0D08
    private void OpenMemberList(Party party)
    {
        CloseMemberList();
        _box = _canvas.Save(BoxX, BoxY, BoxWidth, BoxHeight);
        _canvas.Fill(BoxX, BoxY, BoxWidth, BoxHeight, BoxBackground);
        var right = BoxX + BoxWidth - 1;
        _canvas.Fill(BoxX, BoxY, BoxWidth, 1, BoxBorder);
        _canvas.Fill(BoxX, 0xBF, BoxWidth, 1, BoxBorder);
        _canvas.Fill(BoxX, BoxY, 1, 0xBF - BoxY + 1, BoxBorder);
        _canvas.Fill(right, BoxY, 1, 0xBF - BoxY + 1, BoxBorder);
        // UNCERTAIN: the names are drawn by the text routine (assembly only) in a 52-pixel column; here each full
        //   name is cut to fit that width on its row.
        var y = 0x91;
        foreach (var member in party.Members.Where(m => m.IsPresent))
        {
            var name = member.Name;
            while (name.Length > 0 && _text.Measure(name) > BoxWidth - 8)
            {
                name = name[..^1];
            }
            _text.Draw(_canvas, 0xFF, y, name, TextColor);
            y += BoxRow;
        }
        ui.Present();
    }

    private void CloseMemberList()
    {
        if (_box is not null)
        {
            _canvas.Restore(_box);
            _box = null;
            ui.Present();
        }
    }

    private void Recolor(int x, int y, int width, int height, byte from, byte to)
    {
        if (y >= 0)
        {
            _canvas.Recolor(x, y, width, height, from, to);
        }
    }
}
