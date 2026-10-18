using Darklands.Core.Characters;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$McGuf05": the party went for the dwarf (card 252); he vanishes and his chamber fills with a
/// burning green mist. The party can climb down, edge sideways along the cliff, search for
/// another way out, or pray. A saint who makes the mist harmless also lets the search find the
/// quest's item.
/// </summary>
// orig: R107+0x0E76 (191F:0006) matched; tests: CardSmokeTests
// orig: R107+0x16A8 (191F:0838) matched (registration: saint pop-ups only, they do not name the member)
public sealed class DwarfsPoisonMist : Card
{
    private const int MistPicture = 333;
    private const int GassedPicture = 16;
    private const int CliffPicture = 331;
    private const int TrapDoorPicture = 203;

    /// <summary>The prayer made the mist harmless (kind 0x54, eight hours).</summary>
    private const int MistHarmless = 0x54;

    public override int Number => 253;

    public override int Run(CardContext c)
    {
        Show(c, MistPicture);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Set(OptionState.Enabled, 0, 1, 2, 4);
        c.SetCardSaints(0x72, 0x50, 0x12, 0x3F);
        menu[4] = c.SaintsUsable();
        menu.Popup = () => c.SaintPopupChanceHint();

        return menu.Run(
            // orig: R107+0x11B6 (191F:0346) matched (hint)
            new MenuLine(_ => ClimbDown(c), Unknown),
            // orig: R107+0x1302 (191F:0492) matched (hint)
            new MenuLine(_ => EdgeAlongTheCliff(c), Unknown),
            // orig: R107+0x1514 (191F:06A4) matched (hint)
            new MenuLine(_ => SearchForAnotherExit(c, menu), Unknown),
            // orig: R107+0x154C (191F:06DC) matched (empty); hint R107+0x154E (empty)
            // DORMANT: line 3 is never offered. UNCERTAIN: its empty hint returns whatever is in AX/DX; a neutral hint is used.
            new MenuLine(_ => { }),
            // orig: R107+0x165C (191F:07EC) matched (hint)
            new MenuLine(_ => PrayForSalvation(c, menu), () => OptionHint.Say("Select a person and a saint.")),
            // DORMANT: lines 5..9 are never offered; their actions R107+0x1694.. and hints
            // R107+0x1696.. are empty functions (hints returning AX/DX; neutral hints used).
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            new MenuLine(_ => { }));
    }

    private static OptionHint Unknown() => OptionHint.Say("You can always encounter the unknown");

    /// <summary>Everybody loses 30 Endurance (and a point of its maximum), a point of Perception (and its maximum) and 5–8 Strength.</summary>
    private static void PoisonEverybody(CardContext c)
    {
        c.AddAttribute(Who.Everybody, (int)Attribute.Endurance, -30);
        c.AddAttributeMaximum(Who.Everybody, (int)Attribute.Endurance, -1);
        c.AddAttribute(Who.Everybody, (int)Attribute.Perception, -1);
        c.AddAttributeMaximum(Who.Everybody, (int)Attribute.Perception, -1);
        c.AddAttribute(Who.Everybody, (int)Attribute.Strength, -(c.Random(4) + 5));
    }

    /// <summary>Down the cliff: unharmed if the mist was made harmless, else everybody is poisoned and wakes at evening.</summary>
    // orig: R107+0x10B0 (191F:0240) matched; tests: CardSmokeTests
    private static void ClimbDown(CardContext c)
    {
        if (c.Events.Started(MistHarmless))
        {
            ShowPicture(c, 286);
            c.Screen.Text(9);
            c.PassHours(1);
        }
        else
        {
            ShowPicture(c, GassedPicture);
            c.Screen.Text(1);
            PoisonEverybody(c);
            c.PassHours(c.HoursUntil(18));
        }
        c.LeaveForTheMap();
    }

    /// <summary>Sideways along the cliff: each member who fails an Agility roll costs everybody 5–8 Strength.</summary>
    // orig: R107+0x11EE (191F:037E) matched; tests: CardSmokeTests
    private static void EdgeAlongTheCliff(CardContext c)
    {
        ShowPicture(c, CliffPicture);
        c.Screen.Text(2);
        c.PassHours(1);
        for (var i = 0; i < Party.MaxMembers; i++)
        {
            // C: rand_mod(100) > eff_attr2(i) + Strength(i) / 2; the right side draws nothing.
            if (c.Status(i) != CharacterStatus.Empty
                && c.Random(100) > c.EffectiveAgility(i) + c.Attribute(i, (int)Attribute.Strength) / 2)
            {
                // C: the loss is applied to everybody (who = -2), not to the member who slipped.
                c.AddAttribute(Who.Everybody, (int)Attribute.Strength, -(c.Random(4) + 5));
            }
        }
        ShowPicture(c, CliffPicture);
        c.Screen.Text(3);
        c.PassHours(c.HoursUntil(18));
        c.LeaveForTheMap();
    }

    /// <summary>
    /// The search: with the mist harmless the trap door and the item are found. Otherwise the
    /// first search only burns the party (and heals a little Endurance: the original adds it);
    /// a second one finds the door if the most perceptive member's roll succeeds, or the party
    /// is poisoned.
    /// </summary>
    // orig: R107+0x133A (191F:04CA) matched; tests: CardSmokeTests
    private static void SearchForAnotherExit(CardContext c, CardMenu menu)
    {
        if (c.Events.Started(MistHarmless))
        {
            c.FinishQuest10(c.Here);
            ShowPicture(c, TrapDoorPicture);
            c.Screen.Text(6);
            c.PassHours(2);
            c.LeaveForTheMap();
        }
        else if (menu.Block == 12)
        {
            // C: rand_mod(100) < party_max_attr(3): the query draws nothing.
            if (c.Random(100) < c.PartyMaxAttribute((int)Attribute.Perception))
            {
                c.NameToSlot(0, c.EventDetail(c.Events.Find(8, -1, c.Here, -1, 10, -1)), 0);
                c.FinishQuest10(c.Here);
                ShowPicture(c, TrapDoorPicture);
                c.Screen.Text(6);
                c.PassHours(2);
                c.LeaveForTheMap();
            }
            else
            {
                ShowPicture(c, GassedPicture);
                c.Screen.Text(5);
                PoisonEverybody(c);
                c.PassHours(c.HoursUntil(18));
                c.LeaveForTheMap();
            }
        }
        else
        {
            ShowPicture(c, GassedPicture);
            c.Screen.Text(5);
            // UNCERTAIN: the text says the mist burns, but the original adds 10..14 Endurance here.
            c.AddAttribute(Who.Everybody, (int)Attribute.Endurance, c.Random(5) + 10);
            menu.Block = 12;
            c.Next = 253;
        }
    }

    /// <summary>St. 0x72 or 0x50 carry the party out over the cliff; the others make the mist harmless for eight hours.</summary>
    // orig: R107+0x1550 (191F:06E0) matched; tests: CardSmokeTests
    private static void PrayForSalvation(CardContext c, CardMenu menu)
    {
        // C: invoke_saint(var_chosen1 = menu_member, card_saints[menu_slot]): the member is named before the prayer.
        c.Variables.Chosen[0] = c.MenuMember;
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (answer == -1)
        {
            return;
        }
        if (answer == 1)
        {
            switch (c.MenuSlot)
            {
                case 0:
                case 1:
                    c.PassHours(1);
                    ShowPicture(c, MistPicture);
                    c.Screen.Text(10);
                    c.LeaveForTheMap();
                    break;
                case 2:
                case 3:
                    c.EventHere(MistHarmless, 8);
                    ShowPicture(c, 289);
                    c.Screen.Text(8);
                    menu.Block = 11;
                    c.Next = 253;
                    break;
            }
        }
        else
        {
            ShowPicture(c, GassedPicture);
            c.Screen.Text(7);
            menu.Block = 11;
            c.Next = 253;
        }
    }
}
