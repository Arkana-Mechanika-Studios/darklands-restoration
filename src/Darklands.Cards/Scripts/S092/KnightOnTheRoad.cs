using Darklands.Core.Characters;
using Darklands.Core.Economy;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$MeetJ00": a richly caparisoned knight and his entourage ride up. With a horse the party
/// may challenge him to a race (the best rider) or a joust (the strongest member); otherwise it
/// lets him pass. Renown decides whether he accepts. A win (or the knight's charity) fills an
/// empty purse to 240 pfennigs.
/// </summary>
// orig: R092+0x1436 (197B:0006) matched; tests: CardSmokeTests
public sealed class KnightOnTheRoad : Card
{
    private const int Picture = 102;
    private const uint Mount = 0x2000;

    public override int Number => 0xC3;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = Number;
        var menu = Menu(c);
        menu.Enable(1, 2, 4);
        c.PartyMaxMountedAgility();
        c.Variables.Chosen[1] = (byte)c.BestMember;
        c.PartyMaxAttribute((int)Attribute.Strength);
        c.Variables.Chosen[0] = (byte)c.BestMember;
        if (c.CountItemsWithFlags(Who.Everybody, Mount) == 0)
        {
            menu[1] = OptionState.Shown;
            menu[2] = OptionState.Shown;
        }
        // orig: R092+0x1C40 (197B:0810) matched
        c.SetCardSaints();
        c.SetCardItems();
        return menu.Run(
            // orig: R092+0x16E4 (197B:02B4) matched
            // DORMANT: line 0 is hidden (flag 0): the party would simply ride on.
            new MenuLine(() => RideOn(c), () => OptionHint.ChanceOf(Renown(c), Who.Everybody)),
            new MenuLine(() => Race(c, Renown(c)), () => OptionHint.ChanceOf(Renown(c), c.Variables.Chosen[1])),
            new MenuLine(() => Joust(c, Renown(c)), () => OptionHint.ChanceOf(Renown(c), c.Variables.Chosen[0])),
            // orig: R092+0x19D4 (197B:05A4); R092+0x19FC (197B:05CC) matched
            // DORMANT: line 3 is hidden: on a failed roll the party would ride on after text 10,
            // otherwise stay; its hint names $ChosenThreeName.
            new MenuLine(() =>
                {
                    if (c.Random(100) > Renown(c))
                    {
                        RideOn(c);
                        c.Screen.Text(10);
                    }
                }, () => OptionHint.ChanceOf(Renown(c), c.Variables.Chosen[2])),
            // orig: R092+0x1A2C (197B:05FC) matched
            new MenuLine(() => { c.Screen.Text(7); RideOn(c); }),
            // orig: R092+0x1A7E (197B:064E) and the four like it, matched
            // DORMANT: lines 5..9 are hidden; they would end the game (0x62).
            new MenuLine(() => TheEnd(c)),
            new MenuLine(() => TheEnd(c)),
            new MenuLine(() => TheEnd(c)),
            new MenuLine(() => TheEnd(c)),
            new MenuLine(() => TheEnd(c)));
    }

    /// <summary>The knight accepts a challenge from a party of renown: three points per point of fame.</summary>
    // orig: R092+0x16F2 (197B:02C2) matched (and the same hint at 0408, 0574, 05CC)
    private static int Renown(CardContext c) => Math.Clamp(c.PartyGet(4) * 3, 1, 99);

    /// <summary>Back to the map, out of any place.</summary>
    private static void RideOn(CardContext c)
    {
        c.State.CurrentLocation = Core.GameState.Wilderness;
        c.Next = CardContext.OverlandMapCard;
    }

    /// <summary>The knight's charity: a party with at most 240 pfennigs gets 240 more.</summary>
    private static bool PurseIsNearlyEmpty(CardContext c) => c.State.Party.Purse.TotalPfennigs <= 0xF0;

    // orig: R092+0x171A (197B:02EA) matched
    private static void Race(CardContext c, int chance)
    {
        var rider = c.Variables.Chosen[1];
        if (c.Random(100) <= chance)
        {
            c.Screen.Text(1);
            var knight = c.Random(0x32) + 0x23;
            var horse = c.Random(0x28) + 0x28;
            var mount = c.BestQualityWithFlags(rider, Mount);
            var riding = c.Skill(rider, (int)Skill.Riding);
            // C: −(rnd(34) − mount) − horse − knight + riding ≥ 0; the roll is drawn after the two above.
            if (-(c.Random(0x22) - mount) - horse - knight + riding >= 0)
            {
                c.Screen.Text(5);
                c.PracticeSkill(rider, (int)Skill.Riding, 1, 10, 0);
                if (PurseIsNearlyEmpty(c))
                {
                    c.State.Party.Purse = c.State.Party.Purse.Add(0, 0, 0xF0);
                    c.Screen.Text(13);
                }
                c.PassHours(2);
            }
            else
            {
                c.Screen.Text(4);
                c.PracticeSkill(rider, (int)Skill.Riding, 0, 10, 0);
                c.PassHours(2);
            }
        }
        else
        {
            c.Screen.Text(2);
        }
        RideOn(c);
    }

    // orig: R092+0x1868 (197B:0438) matched
    private static void Joust(CardContext c, int chance)
    {
        var jouster = c.Variables.Chosen[0];
        c.Screen.Text(3);
        c.Screen.Text(8);
        if (c.Random(100) <= chance)
        {
            var knight = c.Random(0x32) + 0x23;
            var horse = c.Random(0x28) + 0x28;
            var lance = c.Random(0x1E) + 0x37;
            var mount = c.BestQualityWithFlags(jouster, Mount);
            var polearms = c.Skill(jouster, (int)Skill.Polearms);
            var riding = c.Skill(jouster, (int)Skill.Riding);
            var tilt = mount / 2 - c.Random(0x32) - lance - horse - knight + riding + polearms;
            if (tilt >= 100)
            {
                JoustEnds(c, won: true);
            }
            else if (tilt <= -100)
            {
                JoustEnds(c, won: false);
            }
            else
            {
                // Both lances glance off; a second pass decides.
                c.Screen.Text(tilt >= 1 ? 11 : 10);
                // C: in rnd(50) − rnd(100) the compiled code draws rnd(100) first (checked against asm).
                var second = c.Random(100);
                var first = c.Random(0x32);
                JoustEnds(c, won: first - second - horse - knight + mount / 2 - lance + polearms + riding >= 1);
            }
        }
        else
        {
            c.Screen.Text(2);
        }
        RideOn(c);
    }

    // orig: R092+0x1686 (197B:0256) matched
    private static void JoustEnds(CardContext c, bool won)
    {
        if (!won)
        {
            c.Screen.Text(9);
            return;
        }
        c.Screen.Text(12);
        c.Screen.Text(13);
        if (PurseIsNearlyEmpty(c))
        {
            c.Screen.Text(14);
            c.State.Party.Purse = c.State.Party.Purse.Add(0, 0, 0xF0);
        }
    }

    private static void TheEnd(CardContext c)
    {
        c.Screen.Text(0);
        c.PassHours(0);
        c.Next = 0x62;
    }
}
