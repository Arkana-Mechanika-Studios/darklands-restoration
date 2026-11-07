using Darklands.Cards;
using Darklands.Content;
using Darklands.Core;
using Darklands.Core.Rules;
using Darklands.Presentation;
using Darklands.Presentation.Graphics;

namespace Darklands.Battles.Tests;

/// <summary>The item and saint pop-up box lists, per member, the card's items or saints that member can use.</summary>
public sealed class OptionPopupTests
{
    private sealed class NoScreen : ICardScreen
    {
        public void Show(string cardName, string picture) { }
        public void ShowPicture(string picture) { }
        public void Text(int block, TextFrame frame = TextFrame.OnPicture) { }
        public int Choose(int block, IReadOnlyList<OptionState> options, Func<int, OptionHint>? hint = null) => 0;
        public void Tell(string message, int member = OptionHint.WholeParty) { }
        public void PassTime(int hours) { }
    }

    private static (CardContext, TextPainter)? Setup()
    {
        if (GameFolder.Files is not { } files)
        {
            return null;
        }
        var world = WorldData.Load(files);
        var state = Darklands.Simulation.NewGame.Quickstart(world, 1);
        return (new CardContext(state, world, new NoScreen()), new TextPainter(new GameAssets(files).Font(1)));
    }

    /// <summary>An item carried by member <paramref name="who"/> and not by the leader.</summary>
    private static int ItemOnlyFor(CardContext c, int who) =>
        c.State.Party.Members[who].Pack.Select(p => p.Code).First(code => Inventory.Has(c.State, 0, code) == 0 && code < 0x5F);

    [Fact]
    public void TheCardEngineKeepsTheBoxsStatesAsTheBoxShowsThem()
    {
        // CardContext.PopupStates (what card-option-enabled becomes after a choice in the box, CardMenu.Run) is the box's own state table
        if (Setup() is not var (c, painter))
        {
            return;
        }
        var items = c.State.Party.Members.SelectMany(m => m.Pack.Select(p => p.Code)).Distinct().Take(3).Append(0x65).ToArray();
        foreach (var difficulty in new[] { Difficulty.Basic, Difficulty.Standard, Difficulty.Expert })
        {
            foreach (var all in new[] { false, true })
            {
                c.State.Difficulty = difficulty;
                c.PopupListsAll = all;
                c.SetCardItems(items);
                c.SetCardSaints(0, 0x17, 0x59, 0x61);
                foreach (var saints in new[] { false, true })
                {
                    Assert.Equal(OptionPopupText.Build(c, saints, painter, darkPicture: false).States, c.PopupStates(saints));
                }
            }
        }
    }

    [Fact]
    public void OnlyTheMembersCarryingTheItemGetALineWithTheirCount()
    {
        if (Setup() is not var (c, painter))
        {
            return;
        }
        c.State.Difficulty = Difficulty.Standard;
        var item = ItemOnlyFor(c, 2);
        c.SetCardItems(item);
        var box = OptionPopupText.Build(c, saints: false, painter, darkPicture: false);
        Assert.Equal(OptionState.Enabled, box.States[2 * 4]);
        Assert.Equal(OptionState.Hidden, box.States[0]);
        Assert.All(Enumerable.Range(0, 20).Where(k => k != 8), k => Assert.Equal(OptionState.Hidden, box.States[k]));
        var text = System.Text.Encoding.Latin1.GetString(box.Text);
        var hans = c.State.Party.Members[2];
        Assert.Contains("\x81" + hans.ShortName + "\xff\n", text);
        Assert.Contains("\x15 \x80" + c.World.ItemName(item) + $" ({c.CountItems(2, item)})\n", text);
        Assert.DoesNotContain(c.State.Party.Members[0].ShortName, text);
        Assert.True(box.Width > painter.Measure(c.World.ItemName(item)));
    }

    /// <summary>
    /// The box is as wide as its widest line in the card window's font (slot 3, where the control codes are 0 wide) plus 6: the original's
    /// R043+0x2CE2 measured "\x15 \x80Stone-tar (1)" in window DS:016A with font 3 and made the box 0x43 wide (vis10 session uncert-polit-popup-hint).
    /// </summary>
    [Fact]
    public void TheBoxWidthIsTheWidestLineInTheCardWindowsFontPlusSix()
    {
        if (GameFolder.Files is not { } files || Setup() is not var (c, _))
        {
            return;
        }
        var cardFont = new TextPainter(new GameAssets(files).Font(3));
        c.State.Difficulty = Difficulty.Standard;
        var item = ItemOnlyFor(c, 2);
        c.SetCardItems(item);
        var box = OptionPopupText.Build(c, saints: false, cardFont, darkPicture: false);
        var line = TextPainter.Encode(" " + c.World.ItemName(item) + $" ({c.CountItems(2, item)})");
        Assert.Equal(Math.Max(cardFont.Measure(line), cardFont.Measure(c.State.Party.Members[2].ShortName)) + 6, box.Width);
        Assert.Equal(0x43 - 6, cardFont.Measure(TextPainter.Encode(" Stone-tar (1)")));
    }

    [Fact]
    public void AtBasicTheLeaderSeesTheItemGreyedAndCannotChooseIt()
    {
        if (Setup() is not var (c, painter))
        {
            return;
        }
        c.State.Difficulty = Difficulty.Basic;
        c.State.Party.LeaderIndex = 0;
        c.SetCardItems(ItemOnlyFor(c, 2));
        var box = OptionPopupText.Build(c, saints: false, painter, darkPicture: true);
        Assert.Equal(OptionState.Shown, box.States[0]);
        Assert.Equal(OptionState.Enabled, box.States[8]);
        var text = System.Text.Encoding.Latin1.GetString(box.Text);
        Assert.Contains("\x15 \x87", text);
        Assert.Contains("\x15 \x8f", text);
    }

    [Fact]
    public void WhenTheCardListsAllEveryAbleMemberMayChooseEverySlot()
    {
        if (Setup() is not var (c, painter))
        {
            return;
        }
        c.State.Difficulty = Difficulty.Expert;
        c.SetCardSaints(1, 2, 3, 4);
        c.PopupListsAll = true;
        var box = OptionPopupText.Build(c, saints: true, painter, darkPicture: false);
        for (var i = 0; i < c.State.Party.Members.Count; i++)
        {
            Assert.All(Enumerable.Range(i * 4, 4), k => Assert.Equal(OptionState.Enabled, box.States[k]));
        }
        // the last member's paragraph mark and the line break before it are cut off
        Assert.EndsWith(c.World.Items.Saints[4].ShortName, System.Text.Encoding.Latin1.GetString(box.Text));
    }

    [Fact]
    public void ASaintIsListedForTheMembersWhoKnowIt()
    {
        if (Setup() is not var (c, painter))
        {
            return;
        }
        c.State.Difficulty = Difficulty.Standard;
        var saint = Enumerable.Range(0, 136).First(s => c.State.Party.Members.All(m => !m.Saints.Contains(s)));
        c.State.Party.Members[1].Saints.Add(saint);
        c.SetCardSaints(-1, saint);
        var box = OptionPopupText.Build(c, saints: true, painter, darkPicture: false);
        Assert.Equal(OptionState.Enabled, box.States[1 * 4 + 1]);
        Assert.Equal(OptionState.Hidden, box.States[1]);
        Assert.DoesNotContain(" (", System.Text.Encoding.Latin1.GetString(box.Text));
    }

    [Fact]
    public void WhenTheLastMembersLastSlotIsEmptyTheBoxCountsOneMoreLine()
    {
        // card 0x129 (vis10 visuals-fortress-saint): only the last member knows the card's one saint. The builder cuts the text's last two
        //   bytes (newline, 0x14) by writing a zero over the newline; the text then ends inside the hidden line of the last slot, and
        //   wrapped-text-size steps past the zero into the 0x14 and counts one more line: the box is a line and 2 px taller than its text.
        if (Setup() is not var (c, painter))
        {
            return;
        }
        c.State.Difficulty = Difficulty.Standard;
        var last = c.State.Party.Members.Count - 1;
        var saint = Enumerable.Range(0, 136).First(s => c.State.Party.Members.All(m => !m.Saints.Contains(s)));
        c.State.Party.Members[last].Saints.Add(saint);
        c.SetCardSaints(saint);
        var box = OptionPopupText.Build(c, saints: true, painter, darkPicture: false);
        Assert.Equal(OptionState.Enabled, box.States[last * 4]);
        Assert.Equal([0x14, 0], box.Tail);
        Assert.Equal(OptionPopupText.Lines, box.States.Length);
        var layout = new CardTextLayout(painter);
        var lineHeight = painter.LineHeight;
        Assert.Equal(2 * lineHeight, layout.MeasureHeight(box.Text, box.Width - 3, 0, box.States));
        Assert.Equal(3 * lineHeight + 2, layout.MeasureHeight(box.Buffer, box.Width - 3, 0, box.States));

        // a saint in the last slot ends the text on its name: nothing is read past the zero
        c.SetCardSaints(-1, -1, -1, saint);
        var full = OptionPopupText.Build(c, saints: true, painter, darkPicture: false);
        Assert.Equal(2 * lineHeight, layout.MeasureHeight(full.Buffer, full.Width - 3, 0, full.States));
    }
}
