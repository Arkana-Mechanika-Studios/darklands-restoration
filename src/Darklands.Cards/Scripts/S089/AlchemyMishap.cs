using Darklands.Core.Characters;
using Darklands.Core.Economy;
using Darklands.Core.Events;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Alche02": an alchemical work goes wrong. Where it happened is told by the card before:
/// an inn room (0x1D, 0x1E), lodgings in the slums (0x23), a village inn (0x89) or the camp
/// (any other). If the mishap event says the work blew up, everybody loses three points of
/// Strength. In a city the innkeeper wants damages, or the party refuses and its virtue suffers.
/// </summary>
// orig: R089+0x04F8 (1887:0008) matched; tests: CardSmokeTests
public sealed class AlchemyMishap : Card
{
    private const int Picture = 189;
    private const int InnRoom = 0x1D;
    private const int InnRoomAtNight = 0x1E;
    private const int SlumLodging = 0x23;
    private const int VillageInn = 0x89;

    public override int Number => 0xB5;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = Number;
        var before = c.State.PreviousCard;
        var blewUp = c.Events.ValueOfTypeAt(EventKinds.AlchemyMishap, MishapPlace(before), c.Here) != 0;
        switch (before)
        {
            case InnRoom or InnRoomAtNight:
                if (blewUp)
                {
                    EverybodyHurt(c);
                    c.AdjustReputation(c.Here, -9 - c.Random(9), -0x14);
                    // C: (rnd(11) + city size) × 9 groschen, kept in 1..3000; the city query draws nothing.
                    var damages = Math.Clamp((c.Random(11) + c.HereQuery(2)) * 9, 1, 3000);
                    c.Variables.Money[0] = Money.From(0, damages, 0);
                    c.OptionsBlock = 0;
                }
                else
                {
                    c.AdjustReputation(c.Here, -6 - c.Random(9), -0x10);
                    c.Variables.Money[0] = Money.From(0, 0, c.HereQuery(2) * 30);
                    c.OptionsBlock = 2;
                }
                return SettleWithTheInnkeeper(c);
            case SlumLodging:
                if (blewUp)
                {
                    EverybodyHurt(c);
                }
                c.AdjustReputation(c.Here, -6 - c.Random(9), -0x10);
                c.Screen.Text(blewUp ? 7 : 3, TextFrame.BlankPage);
                // C: the original sets neither the damages ($Money1) nor options_block here: the menu shows
                // the values the last cards left (stale globals), as here.
                return SettleWithTheInnkeeper(c);
            case VillageInn:
                if (blewUp)
                {
                    // The village burns down.
                    if (c.State.Here is { } village)
                    {
                        // C: the location record's words +0x0C (arrival card) and +0x00 (map icon).
                        village.ArrivalCard = 0x88;
                        village.Icon = 9;
                    }
                    EverybodyHurt(c);
                    c.Screen.Text(4, TextFrame.BlankPage);
                }
                else
                {
                    c.Screen.Text(5, TextFrame.BlankPage);
                }
                return CardContext.OverlandMapCard;
            default:
                if (blewUp)
                {
                    EverybodyHurt(c);
                    c.Screen.Text(1, TextFrame.BlankPage);
                }
                else
                {
                    c.Screen.Text(3, TextFrame.BlankPage);
                }
                // C: cur_loc = -1: the camp is left for the map.
                c.State.CurrentLocation = Core.GameState.Wilderness;
                return CardContext.OverlandMapCard;
        }
    }

    /// <summary>The mishap event's type for the place the work was done.</summary>
    private static int MishapPlace(int cardBefore) => cardBefore switch
    {
        InnRoom or InnRoomAtNight => 0,
        SlumLodging => 3,
        VillageInn => 2,
        _ => 1,
    };

    private static void EverybodyHurt(CardContext c) => c.AddAttribute(Who.Everybody, (int)Attribute.Strength, -3);

    private int SettleWithTheInnkeeper(CardContext c)
    {
        var damages = c.Variables.Money[0];
        var menu = Menu(c, null);
        menu.Enable(0, 1);
        menu[0] = c.State.Party.Purse < damages ? OptionState.Shown : OptionState.Enabled;
        // C: card_options(options_block) each round: the block the start-up (or an earlier card) left.
        menu.EachRound = () => menu.Block = c.OptionsBlock;
        // orig: R089+0x0910 (1887:0420) matched (resets the option handlers only)
        return menu.Run(
            // orig: R089+0x0800 (1887:0310); hint R089+0x0838 matched
            new MenuLine(() => PayTheDamages(c, damages), () => OptionHint.Say(SuccessAssured, c.Leader)),
            // orig: R089+0x0872 (1887:0382); hint R089+0x08D6 matched
            new MenuLine(() => RefuseToPay(c), () => OptionHint.Say(SuccessAssured, c.Leader)));
    }

    private static void PayTheDamages(CardContext c, Money damages)
    {
        c.State.Party.Purse = c.State.Party.Purse.Subtract(damages);
        c.Screen.Text(6);
        c.PassHours(2);
        c.Next = c.IsDaylight ? 9 : 10;
    }

    /// <summary>Refusing: the town remembers, the more so if the inn room blew up.</summary>
    private static void RefuseToPay(CardContext c)
    {
        if (c.Events.ValueOfTypeAt(EventKinds.AlchemyMishap, 0, c.Here) == 0)
        {
            c.AdjustReputation(c.Here, -0x21, -0x21);
            c.AddSkill(Who.Leader, (int)Skill.Virtue, -1);
        }
        else
        {
            c.AdjustReputation(c.Here, -0x59, -0x59);
            c.AddSkill(Who.Leader, (int)Skill.Virtue, -5);
        }
        c.PassHours(2);
        c.Next = c.IsDaylight ? 9 : 10;
    }
}
