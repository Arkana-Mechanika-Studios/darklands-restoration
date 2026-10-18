using Darklands.Cards;
using Darklands.Core;
using Darklands.Core.Rules;
using Darklands.Core.Time;
using Darklands.Core.World;
using Darklands.Platform;
using Darklands.Presentation.Graphics;

namespace Darklands.Presentation;

// orig: R034+0x0EE6 (party_info) R034+0x0EE6 (1550:0006) matched the reference; tests: none (visual, headless)
// DIVERGENCE: party_info_close (R034+0x119C) marks the party icons for redrawing; here GameScreens2 puts the earlier screen back.
/// <summary>
/// The party information screen (F6): fame, the time and date, where the party is, its
/// wealth and a small map. Pointing at the map names the nearest town, its size and the
/// party's standing there; Tab and Shift-Tab step through the towns. Any other key or a click
/// leaves.
/// </summary>
public sealed class PartyInfoScreen(GameUi ui, PartyPanel panel)
{
    private const byte ShadowColor = 0xBF;
    private const byte TitleColor = 5;
    private const byte TextColor = 14;
    private const int ShiftTab = 0x0F00;

    /// <summary>The small map: its window on the screen and where the world map's corner lands on it.</summary>
    private const int MapLeft = 0xC6;
    private const int MapTop = 0x4A;
    private const int MapRight = 0x140;
    private const int MapBottom = 0xC3;

    /// <summary>The town box above the map.</summary>
    private const int TownBoxX = 0xC8;
    private const int TownBoxY = 0x12;
    private const int TownCenterX = 0x102;

    private SavedScreen _back = null!;
    private int _town;

    // orig: R034+0x0EE6 and R034+0x11BC (background, fonts 1)
    public void Show(GameState state)
    {
        var canvas = ui.Canvas;
        _town = state.InCity ? state.CurrentLocation : LocationRules.NearestCity(state, state.MapPosition.X, state.MapPosition.Y);
        _back = ScreenDrawing.DrawBackground(ui, panel, state.Party, "ptystats");
        DrawPartyMarker(state);
        DrawWealthAndPlace(state);
        DrawTitles(state.Party.Fame);
        _back = canvas.SaveScreen();
        ScreenDrawing.WaitForRelease(ui);
        var shown = -2;
        while (true)
        {
            var mouse = ui.Mouse;
            if (mouse.X > MapLeft && mouse.X < MapRight && mouse.Y > MapTop && mouse.Y < MapBottom)
            {
                // C: the nearest town is sought at (x - 3, y + 3) of the map, as the original.
                var town = TownUnder(state, mouse.X - MapLeft - 3, mouse.Y - MapTop + 3);
                if (town != shown && town != -1)
                {
                    shown = town;
                    DescribeTown(state, town, 0x78, 0x34);
                }
                else if (town == -1 && shown != -1)
                {
                    canvas.CopyFrom(_back, TownBoxX, TownBoxY, 0x78, 0x34);
                    shown = -1;
                }
            }
            // orig: R034+0x0EE6 matched C: `if (root 047C:073D()) key = root 047C:0744(); if ((key == 0 || key == 0x0F09 || key == 0x0F00) &&
            // !(mouse_buttons & 1)) { if (key) R034+0x144C(key); continue; } R034+0x119C(); return;` (no menu bar, no hot keys;
            // the button is a level test: held, the screen closes, Tab or not); tests: ScreenInputTests
            // UNCERTAIN: hosts may not deliver Shift-Tab as 0x0F00 (the BIOS code); then only Tab steps.
            var key = ui.PollKeyOnly();
            if ((key == 0 || key == Keys.Tab || key == ShiftTab) && !ui.Mouse.Left)
            {
                if (key != 0)
                {
                    StepToTown(state, key == ShiftTab);
                }
                continue;
            }
            // orig: R034+0x119C: the button waited out (R034+0x11A2), then the release count cleared (R034+0x11B1)
            ui.WaitForButtonRelease();
            ui.TakeLeftReleases();
            return;
        }
    }

    // orig: R034+0x144C (1550:056C) matched
    // C: case 0x0F00 subtracts 2 and falls through into the Tab case, which shows the town and then advances the counter.
    // DIVERGENCE: the original also moves the mouse pointer onto the town (mouse_move); the host owns the pointer here.
    /// <summary>Tab marks the next town, Shift-Tab the one before (the counter runs one ahead, as in the game).</summary>
    private void StepToTown(GameState state, bool back)
    {
        if (back && (_town -= 2) < 0)
        {
            _town = Location.CityCount - 1;
        }
        DescribeTown(state, _town, 0x7A, 0x32);
        if (++_town >= Location.CityCount)
        {
            _town = 0;
        }
    }

    private void DescribeTown(GameState state, int town, int clearWidth, int clearHeight)
    {
        ui.Canvas.CopyFrom(_back, TownBoxX, TownBoxY, clearWidth, clearHeight);
        var place = state.Locations[town];
        ScreenDrawing.Centered(ui, TownCenterX, 0x22, place.Name, TextColor);
        ScreenDrawing.Centered(ui, TownCenterX, 0x2C, PartyStanding.SizeWord(place.Size), TextColor);
        ScreenDrawing.Centered(ui, TownCenterX, 0x36, "Rep: " + PartyStanding.StandingWord(state, town), TextColor);
        ScreenDrawing.Centered(ui, TownCenterX, 0x40, $"({place.Reputation})", TextColor);
    }

    // orig: the scaling of R034+0x1A86: x × 327 / 118, y × 931 / 106 in 32-bit
    /// <summary>The town near a point of the small map (pixels from its corner).</summary>
    private static int TownUnder(GameState state, int x, int y) =>
        PartyStanding.NearestTown(state, (int)((long)x * 327 / 118), (int)((long)y * 931 / 106));

    // orig: R034+0x19D0 (1550:0AF0) matched: maploctr at (116x/327 + 0xC8, 106y/931 + 0x46), the location's place or the party's map position
    private void DrawPartyMarker(GameState state)
    {
        var (x, y) = state.Here is { } place ? (place.X, place.Y) : (state.MapPosition.X, state.MapPosition.Y);
        ScreenDrawing.Overlay(ui, "maploctr", (int)(116L * x / 327) + 0xC8, (int)(106L * y / 931) + 0x46);
    }

    // orig: R034+0x166E (1550:078E) matched
    // C: the town test is cur_loc != -1 && cur_loc < 0x5C here, but cur_loc <= 0x5C when party_info picks the first town for Tab (location 92 counts there).
    private void DrawWealthAndPlace(GameState state)
    {
        var text = ui.Text;
        var canvas = ui.Canvas;
        var date = state.Date;
        ScreenDrawing.Centered(ui, 0x60, 0x40, TextVariables.BellNames[TimeOfDay.Band(date.Hour)], TextColor);
        text.Draw(canvas, 0x58, 0x48, date.Hour.ToString(Invariant), TextColor);
        ScreenDrawing.Centered(ui, 0xA0, 0x40, TextVariables.MonthNames[date.Month], TextColor);
        text.Draw(canvas, 0x98, 0x48, date.Day.ToString(Invariant), TextColor);
        if (state.InCity)
        {
            text.Draw(canvas, 0x48, 0x6F, state.Here!.Name, TextColor);
        }
        else
        {
            var near = LocationRules.NearestCity(state, state.MapPosition.X, state.MapPosition.Y);
            var town = state.Locations[near];
            var direction = PartyStanding.Direction(town.X, town.Y, state.MapPosition.X, state.MapPosition.Y);
            text.Draw(canvas, 0x46, 0x6F, PartyStanding.Directions[direction] + " of ", TextColor);
            text.Draw(canvas, 0x48, 0x78, town.Name, TextColor);
        }
        var purse = state.Party.Purse;
        text.Draw(canvas, 0x43, 0x9D, $"{purse.Florins} Florins", TextColor);
        text.Draw(canvas, 0x43, 0xA5, $"{purse.Groschen} Groschen", TextColor);
        text.Draw(canvas, 0x43, 0xAD, $"{purse.Pfennigs} Pfenniges", TextColor);
        text.Draw(canvas, 0x43, 0xB5, $"({purse.TotalPfennigs}) Total", TextColor);
        text.Draw(canvas, 0x84, 0x9D, $"{state.Party.BankNotes.Florins} Florins", TextColor);
        text.Draw(canvas, 0x84, 0xAD, $"{state.Party.PhilosophersStone} PhStone", TextColor);
        if (state.InCity)
        {
            text.Draw(canvas, 0x84, 0x6F, PartyStanding.StandingWord(state, state.CurrentLocation), TextColor);
            text.Draw(canvas, 0x8E, 0x77, $"({state.Here!.Reputation})", TextColor);
        }
    }

    // orig: R034+0x11DC (1550:02FC) matched
    /// <summary>The headings, drawn twice: a shadow one pixel lower right, then the title colour.</summary>
    private void DrawTitles(int fame)
    {
        var title = PartyStanding.FameTitle(fame);
        // orig: text_width R034+0x1B3C (1550:0C5C) matched: the sum of char_width(c, screen_win_font)
        //   over the string, as Measure in the current font; also centres the town lines at TownCenterX (0x102 - width / 2).
        var width = ui.Text.Measure(title);
        (int X, int Y, string Text)[] headings =
        [
            (0x85, 5, "PARTY INFORMATION"), (0xD6, 0x12, "MAP INFORMATION"), (0x6A, 0x12, "PARTY FAME"),
            (0x82 - width / 2, 0x1A, title), (0x7F, 0x22, fame.ToString(Invariant)), (0x53, 0x30, "TIME"),
            (0x94, 0x30, "DATE"), (0x4A, 0x5F, "LOCATION"), (0x4B, 0x8D, "WEALTH"), (0x88, 0x8D, "NOTES"),
            (0x88, 0x5F, "LOCAL REP"),
        ];
        foreach (var (x, y, text) in headings)
        {
            ui.Text.Draw(ui.Canvas, x + 1, y + 1, text, ShadowColor);
        }
        foreach (var (x, y, text) in headings)
        {
            ui.Text.Draw(ui.Canvas, x, y, text, TitleColor);
        }
    }

    private static readonly System.Globalization.CultureInfo Invariant = System.Globalization.CultureInfo.InvariantCulture;
}
