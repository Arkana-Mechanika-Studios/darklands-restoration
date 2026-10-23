using Darklands.Core.Characters;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// Card 279 ("$MeetB03"): a blizzard rises. The party can keep going (two to five days of
/// freezing that cost Strength and spoil alchemical ingredients and some gear), pray, or camp
/// and wait it out (milder).
/// </summary>
// orig: R113+0x17DA (19B5:000A) matched; tests: CardSmokeTests
// orig: R113+0x1DBA (19B5:05EA) matched (registration: saint pop-ups only, they do not name the member)
// DORMANT: R113+0x1E54, R113+0x1E56, R113+0x1E58, R113+0x1E5A are never called.
public sealed class Blizzard_MeetB03 : Card
{
    public override int Number => 279;

    public override int Run(CardContext c)
    {
        Show(c, 286);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Set(OptionState.Enabled, 0, 1, 2);
        c.SetCardSaints(0x16, 0x26, 0x3C, 0x82);
        menu[1] = c.SaintsUsable();
        menu.Popup = () => c.SaintPopupChanceHint();

        return menu.Run(
            // orig: R113+0x1A12 (line 0); hint R113+0x1B8C (19B5:03BC) matched
            new MenuLine(_ => KeepGoing(c), Unknown),
            // orig: R113+0x1BC4 (line 1); hint R113+0x1C6C (19B5:049C) matched
            new MenuLine(_ => Pray(c), () => OptionHint.Say("Select a person and a saint.")),
            // orig: R113+0x1CA4 (line 2); hint R113+0x1D66 (19B5:0596) matched
            new MenuLine(_ => WaitItOut(c), Unknown),
            // DORMANT: lines 3..9 are never offered; actions R113+0x1D9E.. and hints R113+0x1DA0.. are
            // C: empty; the hints return AX/DX; neutral hints are used.
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            new MenuLine(_ => { }));
    }

    private static OptionHint Unknown() => OptionHint.Say("You can always encounter the unknown");

    /// <summary>
    /// Pressing on: each member loses a point of Endurance and twice the days in Strength; items
    /// whose kind is exactly 0x400 are spoiled with 16 in 100, those of kind 0x4000000 lose a point of quality.
    /// </summary>
    // orig: R113+0x1A12 (19B5:0242) matched; tests: CardSmokeTests
    // C: the kind tests compare the whole flag word (== 0x400L), not a bit; the roll is drawn only for those items (&&).
    // C: the stack count (record +0x6A) is read anew each step, so after a spoiled stack is removed the next one is skipped.
    // DORMANT: add_attr keeps Strength at 1 or more, so the "Strength <= 0" repair never applies.
    private static void KeepGoing(CardContext c)
    {
        var days = c.Random(4) + 2;
        c.Variables.Numbers[0] = days;
        for (var i = 0; i < Party.MaxMembers; i++)
        {
            if (c.Status(i) == CharacterStatus.Empty)
            {
                continue;
            }
            c.AddAttribute(i, (int)Attribute.Endurance, -1);
            c.AddAttribute(i, (int)Attribute.Strength, -(days * 2));
            if (c.Attribute(i, (int)Attribute.Strength) <= 0)
            {
                c.SetAttribute(i, (int)Attribute.Strength, 1);
            }
            var pack = c.State.Party.Members[i].Pack;
            for (var j = 0; j < pack.Count; j++)
            {
                var flags = c.State.Item(pack[j].Code).Flags;
                if (flags == 0x400 && c.Random(100) <= 15)
                {
                    c.TakeItemAt(i, j);
                }
                else if (flags == 0x4000000)
                {
                    pack[j] = pack[j] with { Quality = Math.Clamp(pack[j].Quality - 1, 1, 99) };
                }
            }
        }
        ShowPicture(c, 286);
        c.Screen.Text(1);
        c.PassHours(days * 24);
        c.LeaveForTheMap();
    }

    // orig: R113+0x1BC4 (19B5:03F4) matched; tests: CardSmokeTests
    private static void Pray(CardContext c)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (answer == -1)
        {
            return;
        }
        if (answer == 1)
        {
            ShowPicture(c, 289);
            c.Screen.Text(2);
            c.LeaveForTheMap();
            return;
        }
        ShowPicture(c, 16);
        c.Screen.Text(3);
        c.Next = 279;
    }

    /// <summary>Camping it out: 5 Endurance and up to 2 Strength each, and half a day more.</summary>
    // orig: R113+0x1CA4 (19B5:04D4) matched; tests: CardSmokeTests
    // DORMANT: as above, the Strength repair never applies.
    private static void WaitItOut(CardContext c)
    {
        var days = c.Random(4) + 2;
        c.Variables.Numbers[0] = days;
        for (var i = 0; i < Party.MaxMembers; i++)
        {
            if (c.Status(i) == CharacterStatus.Empty)
            {
                continue;
            }
            c.AddAttribute(i, (int)Attribute.Endurance, -5);
            c.AddAttribute(i, (int)Attribute.Strength, -c.Random(3));
            if (c.Attribute(i, (int)Attribute.Strength) <= 0)
            {
                c.SetAttribute(i, (int)Attribute.Strength, 1);
            }
        }
        ShowPicture(c, 287);
        c.Screen.Text(4);
        c.PassHours(days * 24 + 12);
        c.LeaveForTheMap();
    }
}
