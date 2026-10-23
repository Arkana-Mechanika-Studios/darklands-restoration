using Darklands.Core.Characters;
using Darklands.Core.Economy;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$MeetP00": a frail friar leads old pilgrims to St. Mary's shrine near the nearest city.
/// The party can wish them well, give them money, give them its mounts, or escort them for a
/// few days. Charity brings Divine Favour.
/// </summary>
// orig: R109+0x1C58 (19FD:0008) matched; tests: CardSmokeTests
// orig: R109+0x2168 (19FD:0518) matched (registration: an empty function, so lines 10..19 keep an earlier card's handlers; all hidden)
// DORMANT: R109+0x216A is never called.
public sealed class PilgrimsOnTheRoad : Card
{
    public override int Number => 262;

    public override int Run(CardContext c)
    {
        Show(c, 155);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Set(OptionState.Enabled, 0, 1, 2, 3);
        c.State.CurrentLocation = Core.GameState.Wilderness;
        var alms = Money.From(0, c.Random(50) + 3, 0);
        c.Variables.Money[0] = alms;
        if (c.State.Party.Purse < alms)
        {
            menu[1] = OptionState.Shown;
        }
        if (c.PartyGet(5) == 0)
        {
            menu[2] = OptionState.Shown;
        }

        menu.Popup = () => c.MemberPopupHint();

        return menu.Run(
            // orig: R109+0x1EBA (19FD:026A) matched; hint R109+0x1ED4
            new MenuLine(_ => { c.Screen.Text(1); c.LeaveForTheMap(); }, NoRiskForAll),
            // orig: R109+0x1F0C (19FD:02BC) matched; hint R109+0x1F84
            new MenuLine(_ => GiveAlms(c, alms), NoRiskForAll),
            // orig: R109+0x1FBC (19FD:036C) matched; hint R109+0x203E
            new MenuLine(_ => GiveTheMounts(c), NoRiskForAll),
            // orig: R109+0x2076 (19FD:0426) matched; hint R109+0x2118
            new MenuLine(_ => EscortThem(c), () => OptionHint.Say("You can always encounter the unknown")),
            // DORMANT: lines 4..9 are never offered; actions R109+0x2150.. and hints R109+0x2152..
            // C: are empty; the hints return AX/DX; neutral hints are used.
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            new MenuLine(_ => { }));
    }

    private static OptionHint NoRiskForAll() => OptionHint.Say(NoRisk);

    private static void BlessEverybody(CardContext c, int religionShare, int bonus)
    {
        for (var i = 0; i < Party.MaxMembers; i++)
        {
            if (c.Status(i) != CharacterStatus.Empty)
            {
                c.AddAttribute(i, (int)Attribute.DivineFavor, c.Skill(i, (int)Skill.Religion) / religionShare + bonus);
            }
        }
    }

    // orig: R109+0x1F0C (19FD:02BC) matched; tests: CardSmokeTests
    private static void GiveAlms(CardContext c, Money alms)
    {
        c.State.Party.Purse = c.State.Party.Purse.Subtract(alms);
        c.Screen.Text(2);
        c.PracticeSkill(Who.Everybody, (int)Skill.Virtue, 1, 5, -1);
        BlessEverybody(c, 4, 4);
        c.LeaveForTheMap();
    }

    /// <summary>All mounts and pack animals (items with the mount flag) are given away.</summary>
    // orig: R109+0x1FBC (19FD:036C) matched; tests: CardSmokeTests
    // C: remove_items_flags(i, 0x2000, 0) passes the long mask as two words, low first: 0x2000.
    private static void GiveTheMounts(CardContext c)
    {
        c.PassHours(3);
        c.Screen.Text(3);
        c.PracticeSkill(Who.Everybody, (int)Skill.Virtue, 1, 15, 0);
        for (var i = 0; i < Party.MaxMembers; i++)
        {
            if (c.Status(i) != CharacterStatus.Empty)
            {
                c.AddAttribute(i, (int)Attribute.DivineFavor, c.Skill(i, (int)Skill.Religion) / 4 + 4);
                c.RemoveItemsWithFlags(i, 0x2000);
            }
        }
        c.LeaveForTheMap();
    }

    /// <summary>One to three days' escort to the nearest city, where the party arrives (its map position does not move).</summary>
    // orig: R109+0x2076 (19FD:0426) matched; tests: CardSmokeTests
    // C: load_city(cur_loc = nearest_city(party_x, party_y)).
    private static void EscortThem(CardContext c)
    {
        c.Screen.Text(4);
        c.Variables.Numbers[0] = c.Random(3) + 1;
        c.PassHours(c.Variables.Numbers[0] * 24);
        c.Screen.Text(5);
        c.PracticeSkill(Who.Everybody, (int)Skill.Virtue, 1, 10, 0);
        BlessEverybody(c, 5, c.Variables.Numbers[0]);
        var city = c.NearestCity(c.State.MapPosition.X, c.State.MapPosition.Y);
        c.State.CurrentLocation = city;
        c.LoadCity(city);
        c.Next = CardContext.MapCard;
    }
}
