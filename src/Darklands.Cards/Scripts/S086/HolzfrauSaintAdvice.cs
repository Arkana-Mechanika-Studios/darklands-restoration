using Darklands.Core.Characters;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$WildH02": a holzfrau, the woodwife of the schrats, appears at night and whispers the name
/// of the saint who protects against the Wild Hunt (the saint of the Hunt's quest, tag 7).
/// What she says depends on whether the party already knows the saint and whether she was met
/// before (fact tag 7, type 1 the first meeting, type 3 this advice).
/// </summary>
// orig: R086+0x0C40 (18FC:0000) matched; tests: CardSmokeTests
// DORMANT: text blocks 4 and 5 are never shown by this card.
public sealed class HolzfrauSaintAdvice : Card
{
    public const int CardNumber = 0xA9;

    private const int Picture = 130;
    private const int QuarryTag = 7;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        c.PlayMusic(0x24);
        Show(c, Picture);
        // The Hunt's quest event (kind 8, tag 7) names the saint (first data word) and an item (second).
        // DIVERGENCE: without the quest (slot -1) the original reads through a stray pointer; 0 here.
        var quest = c.Events.Find(8, -1, -1, -1, QuarryTag, -1);
        var saint = c.Events[quest]?.Data1 ?? 0;
        var item = c.Events[quest]?.Data2 ?? 0;
        // UNCERTAIN: the saint name table of the texts (R034) is taken to be the saint list's full names.
        c.Variables.Texts[0] = saint >= 0 && saint < c.World.Items.Saints.Count ? c.World.Items.Saints[saint].FullName : "";
        c.Variables.Texts[1] = c.World.ItemName(item);
        if (c.KnowsSaint(Who.Everybody, saint))
        {
            c.Screen.Text(3);
        }
        else if (!c.Events.FactAnywhere(QuarryTag, 3))
        {
            c.Events.Create(-2, c.Here, 0, 0x1C, 3, 0, 0, 9999, 0, QuarryTag);
            c.Screen.Text(c.Events.FactAnywhere(QuarryTag, 1) ? 0 : 1);
        }
        else
        {
            c.Screen.Text(2);
        }
        c.State.CurrentLocation = -1;
        c.Next = 0x0C;
        return c.Next;
    }

    /// <summary>The option table the original sets up and never uses: no option lines, no items, no saints.</summary>
    // orig: R086+0x0D7E (18FC:013E) matched; R086+0x0DB6 (18FC:0176) matched; tests: CardSmokeTests
    // DORMANT: nothing calls the set-up (the card shows no menu); its default handler says "screwed up.".
    internal static void ClearOptionTable(CardContext c)
    {
        c.SetCardSaints();
        c.SetCardItems();
    }

    // DORMANT: the default hint of an unlisted option line of the unused table above.
    internal static OptionHint DefaultHint() => OptionHint.Say(CardTexts.ScrewedUp);
}
