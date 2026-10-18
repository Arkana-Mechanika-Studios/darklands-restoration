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
}
