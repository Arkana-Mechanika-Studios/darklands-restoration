using Darklands.Core.Characters;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$McGuf01" (card 249, ported from the original code, which has no decompiled source): a deep
/// lake under a craggy ridge. When an item quest (tag 10) leads here, the item is guarded by
/// Vulcans, a giant spider or a pair of bears, by the place's number; otherwise the lake is
/// empty. The party can watch, search the shore or pray.
/// </summary>
// orig: R106+0x07D4 (18B5:0004) asm (read from the disassembly; no C exists); tests: CardSmokeTests
// orig: R106+0x1588 (18B5:0DB8) asm (registration: default R106+0x15EA; saint pop-ups R106+0x162A.. do not name the member)
public sealed class LonelyMountainLake : Card
{
    private const int LakePicture = 163;
    private const int CampPicture = 61;
    private const int BeastPicture = 199;

    private const int NoGuardian = 0;
    private const int Vulcans = 1;
    private const int Spider = 2;
    private const int Bears = 3;

    public override int Number => 249;

    public override int Run(CardContext c)
    {
        // The CardTable has no name for card 249; the code copies "$McGuf01" (DS:64EA) into card-title.
        c.Screen.Show("$McGuf01", PictureNames.Of(LakePicture));
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Set(OptionState.Enabled, 0, 1, 2, 9);
        // The guardian is kept in the game's saved-card word, as the original does.
        if (c.Events.QuestAt(10, c.Here))
        {
            // C: event-search does not set latest-event; event-detail-word-of(latest-event) reads the stale global.
            // C: (location-now + 100) % 100 with idiv: a signed remainder, non-negative for any location.
            _ = c.Events.Find(8, -1, c.Here, -1, 10, -1);
            c.NameToSlot(0, c.EventDetail(c.LastEvent), 0);
            c.State.SavedCard = ((c.Here + 100) % 100) switch { <= 34 => NoGuardian, <= 56 => Vulcans, <= 78 => Spider, _ => Bears };
        }
        else
        {
            c.State.SavedCard = NoGuardian;
        }
        c.SetCardSaints(0x11, 0x35, 0x44, 0x45);
        menu[2] = c.SaintsUsable();
        menu.Popup = () => c.SaintPopupChanceHint();

        return menu.Run(
            // orig: R106+0x0FF8 (line 0); hint R106+0x1192 (18B5:09C2) asm
            new MenuLine(_ => WatchTheLake(c), Unknown),
            // orig: R106+0x11CA (line 1); hint R106+0x135E (18B5:0B8E) asm
            new MenuLine(_ => SearchTheShore(c), Unknown),
            // orig: R106+0x1396 (line 2); hint R106+0x14B2 (18B5:0CE2) asm
            new MenuLine(_ => PrayForGuidance(c), () => OptionHint.Say("Select a person and a saint.")),
            // orig: R106+0x14EA (18B5:0D1A) asm; hint R106+0x14FC
            // DORMANT: line 3 is never offered (card-option-flag-3 = 0).
            new MenuLine(_ => { c.PassHours(0); c.Next = CardContext.MapCard; }, () => OptionHint.Say(SuccessAssured)),
            // DORMANT: lines 4..8 are never offered; actions R106+0x1534.. and hints R106+0x1536.. are bare retf.
            // C: those hints return whatever is in AX/DX; a neutral hint is used.
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            // orig: R106+0x1548 (18B5:0D78) asm; hint R106+0x1550 (leaves without clearing location-now)
            new MenuLine(_ => c.Next = CardContext.MapCard, () => OptionHint.Say(NoRisk)));
    }

    private static OptionHint Unknown() => OptionHint.Say("You can always encounter the unknown");

    /// <summary>Three hours of watching show signs of the guardian; an alchemist recognises the Vulcans' brimstone.</summary>
    // orig: R106+0x0FF8 (18B5:0828) asm; tests: CardSmokeTests
    private static void WatchTheLake(CardContext c)
    {
        c.PassHours(3);
        switch (c.State.SavedCard)
        {
            case Vulcans:
                var alchemy = c.PartyMaxSkill((int)Skill.Alchemy);
                c.Variables.Chosen[0] = c.BestMember;
                ShowPicture(c, LakePicture);
                c.Screen.Text(alchemy >= 10 ? 20 : 6);
                break;
            case Spider:
                ShowPicture(c, 0);
                c.Screen.Text(7);
                break;
            case Bears:
                ShowPicture(c, 198);
                c.Screen.Text(8);
                break;
            default:
                if (c.Random(2) == 1)
                {
                    ShowPicture(c, 129);
                    c.Screen.Text(1);
                }
                else
                {
                    ShowPicture(c, 100);
                    c.Screen.Text(5);
                }
                break;
        }
        ShowPicture(c, LakePicture);
        c.Next = 249;
    }

    /// <summary>The item is taken and its guardian attacks; with no guardian it is simply found, if a quest leads here.</summary>
    // orig: R106+0x11CA (18B5:09FA) asm; tests: CardSmokeTests
    private static void SearchTheShore(CardContext c)
    {
        c.PassHours(1);
        switch (c.State.SavedCard)
        {
            case Vulcans:
                c.FinishQuest10(c.Here);
                ShowPicture(c, 232);
                c.Screen.Text(11);
                FightTheVulcans(c);
                break;
            case Spider:
                c.FinishQuest10(c.Here);
                ShowPicture(c, 232);
                c.Screen.Text(12);
                FightTheSpider(c);
                break;
            case Bears:
                c.FinishQuest10(c.Here);
                ShowPicture(c, BeastPicture);
                c.Screen.Text(13);
                FightTheBears(c);
                break;
            default:
                if (c.Events.QuestAt(10, c.Here))
                {
                    c.FinishQuest10(c.Here);
                    ShowPicture(c, 331);
                    c.Screen.Text(17);
                }
                else
                {
                    c.PassHours(1);
                    ShowPicture(c, 331);
                    c.Screen.Text(4);
                }
                ShowPicture(c, LakePicture);
                break;
        }
        c.Next = CardContext.MapCard;
    }

    /// <summary>A saint warns of the fire-creatures, or of a strong beast; without a guardian the lake feels peaceful.</summary>
    // orig: R106+0x1396 (18B5:0BC6) asm; tests: CardSmokeTests
    // C: text-slot-chosen-one = menu-owner-member is stored between pushing the saint and pushing the member, before the prayer.
    private static void PrayForGuidance(CardContext c)
    {
        var saint = c.CardSaints[c.MenuEntrySlot];
        c.Variables.Chosen[0] = c.MenuMember;
        var answer = c.InvokeSaint(c.MenuMember, saint);
        if (answer == -1)
        {
            return;
        }
        var guardian = c.State.SavedCard;
        if (answer != 1)
        {
            ShowPicture(c, 100);
            c.Screen.Text(2);
        }
        else if (guardian == Vulcans && c.MenuSlot != 3)
        {
            ShowPicture(c, 232);
            c.Screen.Text(10);
        }
        else if (guardian is Spider or Bears)
        {
            ShowPicture(c, LakePicture);
            c.Screen.Text(9);
        }
        else
        {
            ShowPicture(c, 155);
            c.Screen.Text(3);
        }
        ShowPicture(c, LakePicture);
        c.Next = 249;
    }

    /// <summary>
    /// The Vulcans: won, the item is carried off; fled, those who ran come back having lost
    /// things; lost, the fire consumes everybody in the cave — the whole party.
    /// </summary>
    // orig: R106+0x0A86 (18B5:02B6) asm; tests: CardSmokeTests
    // C: right-to-left arguments: roll-below(5) + 3 is the only draw before battle-driver (seed -2 draws nothing).
    private static void FightTheVulcans(CardContext c)
    {
        var vulcans = c.Random(5) + 3;
        switch (c.RunBattle(0x69, -2, 0x31, 2, vulcans, -1, 0, 0, -1, 0, 0))
        {
            case 0:
                c.PassHours(1);
                ShowPicture(c, CampPicture);
                c.Screen.Text(19);
                break;
            case 1:
            case 2:
                for (var i = 0; i < c.PartySize; i++)
                {
                    if (c.Status(i) == CharacterStatus.Away)
                    {
                        c.SetStatus(i, CharacterStatus.Able);
                        c.LoseItems(i);
                        c.Variables.Chosen[0] = i;
                    }
                }
                ShowPicture(c, CampPicture);
                c.Screen.Text(18);
                c.PassHours(c.HoursUntil(18));
                ShowPicture(c, BeastPicture);
                c.Screen.Text(14);
                break;
            case 3:
            case 4:
                ShowPicture(c, BeastPicture);
                c.Screen.Text(14);
                for (var i = 0; i < Party.MaxMembers; i++)
                {
                    if (c.Status(i) != CharacterStatus.Empty)
                    {
                        // The original removes $ChosenOneName each time, then empties the slot.
                        c.DeleteMember(c.Variables.Chosen[0]);
                        c.SetStatus(i, CharacterStatus.Empty);
                    }
                }
                c.PassHours(0);
                break;
            default:
                return;
        }
        c.Next = CardContext.MapCard;
    }

    /// <summary>
    /// The spider: won, the item is carried off; fled, the first member who ran is carried
    /// off in its web (the others return); lost, it takes one member at random.
    /// </summary>
    // orig: R106+0x0C22 (18B5:0452) asm; tests: CardSmokeTests
    private static void FightTheSpider(CardContext c)
    {
        var spiders = c.Random(3) + 1;
        switch (c.RunBattle(0x3A, -2, 0x39, 2, spiders, -1, 0, 0, -1, 0, 0))
        {
            case 0:
                c.PassHours(1);
                ShowPicture(c, CampPicture);
                c.Screen.Text(19);
                break;
            case 1:
            case 2:
                var away = 0;
                for (var i = 0; i < c.PartySize; i++)
                {
                    if (c.Status(i) == CharacterStatus.Away)
                    {
                        if (away++ == 0)
                        {
                            c.SetStatus(i, CharacterStatus.Empty);
                            c.DeleteMember(i);
                            c.Variables.Chosen[0] = i;
                        }
                        else
                        {
                            c.SetStatus(i, CharacterStatus.Able);
                        }
                    }
                }
                if (away != 0)
                {
                    ShowPicture(c, 0);
                    c.Screen.Text(15);
                    c.PassHours(c.HoursUntil(5));
                    ShowPicture(c, CampPicture);
                    c.Screen.Text(21);
                }
                else
                {
                    c.PassHours(c.HoursUntil(5));
                    ShowPicture(c, CampPicture);
                    c.Screen.Text(18);
                }
                c.PassHours(0);
                break;
            case 3:
            case 4:
                var taken = false;
                var victim = c.Random(c.PartySize);
                if (c.Status(victim) != CharacterStatus.Empty)
                {
                    c.SetStatus(victim, CharacterStatus.Empty);
                    c.DeleteMember(victim);
                    c.Variables.Chosen[0] = victim;
                    taken = true;
                }
                ShowPicture(c, BeastPicture);
                c.Screen.Text(15);
                c.PassHours(c.HoursUntil(5));
                ShowPicture(c, CampPicture);
                c.Screen.Text(taken ? 21 : 18);
                break;
            default:
                return;
        }
        c.Next = CardContext.MapCard;
    }

    /// <summary>The bears: won, the item is carried off; otherwise those who ran return mauled and the party limps away.</summary>
    // orig: R106+0x0E9C (18B5:06CC) asm; tests: CardSmokeTests
    // C: raise-attribute-by(i, 1, -roll-below(smaller-of(member-stat(i, 1) + 1, 50))); every result but 0 and 1..4 also leaves.
    private static void FightTheBears(CardContext c)
    {
        var bears = c.Random(5) + 3;
        var result = c.RunBattle(0x3A, -2, 0x3A, 2, bears, -1, 0, 0, -1, 0, 0);
        if (result == 0)
        {
            c.PassHours(1);
            ShowPicture(c, CampPicture);
            c.Screen.Text(19);
        }
        else if (result is >= 1 and <= 4)
        {
            var mauled = 0;
            for (var i = 0; i < c.PartySize; i++)
            {
                if (c.Status(i) == CharacterStatus.Away)
                {
                    c.SetStatus(i, CharacterStatus.Able);
                    var strength = c.Attribute(i, (int)Attribute.Strength);
                    c.AddAttribute(i, (int)Attribute.Strength, -c.Random(Math.Min(strength + 1, 50)));
                    mauled++;
                }
            }
            ShowPicture(c, BeastPicture);
            c.Screen.Text(mauled != 0 ? 16 : 18);
            c.PassHours(c.HoursUntil(5));
        }
        c.Next = CardContext.MapCard;
    }
}
