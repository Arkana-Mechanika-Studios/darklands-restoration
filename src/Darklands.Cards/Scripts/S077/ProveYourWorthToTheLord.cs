using Darklands.Core.Characters;
using Darklands.Core.Economy;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Prove00": a high lord asks the party to prove its famed abilities: turn lead into gold,
/// call on a saint, show its scholarship, its skill with weapons, tell of its adventures, or
/// admit it is no better than ordinary folk. Impressing him earns the task of dealing with a
/// robber knight nearby.
/// </summary>
// orig: R077+0x0000 (1838:0000) matched; tests: CardSmokeTests
// orig: R077+0x0B2E (1838:0B2E) matched; R077+0x0BAC (1838:0BAC) matched; tests: CardSmokeTests
// DORMANT: the "screwed up." default handler of the unlisted lines 6..19 (never offered).
public sealed class ProveYourWorthToTheLord : Card
{
    public const int CardNumber = 0x84;

    private const int Picture = 356;
    private const int CourtPicture = 16;
    private const int UnimpressedPicture = 236;

    /// <summary>The robber knight's castle, where the task leads.</summary>
    private const int RobberKnight = 0x90;

    /// <summary>A failed demonstration is not repeated for eight hours.</summary>
    private const int FailedDemonstration = 0x27;

    private static readonly int[] Saints = [5, 0x2E, 0x40, 0x4E];

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = CardNumber;
        var menu = Menu(c);
        menu.Enable(5);
        menu[2] = c.EventTypeAtHere(FailedDemonstration, 1) ? OptionState.Shown : OptionState.Enabled;
        menu[3] = c.EventTypeAtHere(FailedDemonstration, 2) ? OptionState.Shown : OptionState.Enabled;
        menu[4] = c.EventTypeAtHere(FailedDemonstration, 3) ? OptionState.Shown : OptionState.Enabled;
        c.SetCardItems(0x6E);
        c.SetCardSaints(Saints);
        menu[0] = c.ItemsUsable();
        menu[1] = c.SaintsUsable();

        return menu.Run(
            // orig: R077+0x020C (line 0); R077+0x0BE4 (1838:0BE4) matched; tests: CardSmokeTests
            new MenuLine(() => TurnLeadIntoGold(c), OptionHint.Saying(CardTexts.SelectPotion),
                () => c.MemberPopupHint(CardTexts.PotionWorks)),
            // orig: R077+0x0310 (line 1); R077+0x0C20 (1838:0C20) matched; R077+0x0CAA (1838:0CAA) matched; R077+0x0D34 (1838:0D34) matched;
            // R077+0x0DBE (1838:0DBE) matched; tests: CardSmokeTests
            new MenuLine(() => CallOnASaint(c), OptionHint.Saying(CardTexts.SelectSaint), c.SaintPopupHint),
            new MenuLine(() =>
            {
                if (!ShowScholarship(c, ScholarshipChance(c).Chance))
                {
                    menu[2] = OptionState.Shown;
                }
            }, () => ScholarshipChance(c)),
            new MenuLine(() =>
            {
                if (!ShowWeaponSkill(c, WeaponChance(c).Chance))
                {
                    menu[3] = OptionState.Shown;
                }
            }, () => WeaponChance(c)),
            new MenuLine(() =>
            {
                if (!TellOfAdventures(c, TalesChance(c).Chance))
                {
                    menu[4] = OptionState.Shown;
                }
            }, () => TalesChance(c)),
            new MenuLine(() => AdmitOrdinariness(c), OptionHint.Saying(SuccessAssured)));
    }

    private static int AfterTheAudience(CardContext c) => c.IsDaylight ? 0x12 : 0x19;

    // C: the task's code is repeated inline in five handlers of the original; one helper here.
    // C: closest-place-of-kind takes the city record's position word pair (+4, +6) as one long: x, y.
    /// <summary>
    /// The lord, impressed, sets the party on a robber knight near the city for a reward
    /// ($Money1) and sends it there, to return to <paramref name="returnCard"/> afterwards.
    /// </summary>
    // C: the card to return to is asked after the hour has passed (daylight-test after pass-hours(1)).
    private static void GiveTheTask(CardContext c, int questType, Func<int> returnCard)
    {
        c.Variables.Money[0] = Money.From(c.LocationQuery(c.Here, 2) * 7, 0, 0);
        c.RandomName(c.City.Ordinal + 0x0C, 0, 0);
        var city = c.State.Locations[c.Here];
        c.Variables.Location = c.NearestLocationOfType(city.X, city.Y, 2);
        c.StartQuest3(0x0A, c.Variables.Location, questType, 0x0F, 2, 0);
        c.PassHours(1);
        c.State.SavedCard = returnCard();
        c.State.SavedCard2 = c.Variables.Location;
        c.Next = RobberKnight;
    }

    // orig: R077+0x020C (1838:020C) matched; R077+0x02D8 (1838:02D8) matched; tests: CardSmokeTests
    private static void TurnLeadIntoGold(CardContext c)
    {
        c.TakeItem(c.MenuMember, c.CardItems[c.MenuSlot]);
        c.Screen.Text(1);
        c.PassHours(0);
        GiveTheTask(c, c.City.Ordinal + 1, () => 0x0C);
    }

    // orig: R077+0x0310 (1838:0310) matched; R077+0x04AC (1838:04AC) matched; tests: CardSmokeTests
    private static void CallOnASaint(CardContext c)
    {
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuEntrySlot]);
        if (answer == -1)
        {
            return;
        }
        if (answer != 0)
        {
            if (c.MenuSlot is >= 0 and < 4)
            {
                c.NameToSlot(1, Saints[c.MenuSlot], 0);
            }
            ShowPicture(c, 289);
            c.Screen.Text(3);
            c.PassHours(1);
            GiveTheTask(c, c.City.Ordinal + 1, () => AfterTheAudience(c));
            return;
        }
        ShowPicture(c, CourtPicture);
        c.Screen.Text(2);
        ShowPicture(c, Picture);
        c.PassHours(1);
        c.Next = CardNumber;
    }

    // orig: R077+0x0626 (1838:0626) matched; tests: CardSmokeTests
    // C: the member shown is text-slot-chosen-one as it stands (an earlier value if nobody scores above 0).
    /// <summary>The most learned able member: (each part above its threshold, 1..99): Latin, intelligence, reading and writing, religion with alchemy.</summary>
    private static OptionHint ScholarshipChance(CardContext c)
    {
        var best = 0;
        for (var i = 0; i < c.PartySize; i++)
        {
            if (c.Status(i) != CharacterStatus.Able)
            {
                continue;
            }
            var latin = Math.Clamp(c.Skill(i, 0x0B) - 40, 1, 99);
            var wits = Math.Clamp(c.Attribute(i, 4) - 20, 1, 99);
            var lore = Math.Clamp(c.Skill(i, 0x0C) - 70, 1, 99);
            var total = Math.Clamp(c.Skill(i, 8) + c.Skill(i, 7) - 120, 1, 99) + latin + lore + wits;
            if (total > best)
            {
                best = total;
                c.Variables.Chosen[0] = i;
            }
        }
        return OptionHint.ChanceOf(Math.Clamp(best, 1, 99), c.Variables.Chosen[0]);
    }

    // orig: R077+0x04E4 (1838:04E4) matched; tests: CardSmokeTests
    private static bool ShowScholarship(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            c.Screen.Text(4);
            c.PassHours(2);
            GiveTheTask(c, c.City.Ordinal + 1, () => AfterTheAudience(c));
            return true;
        }
        ShowPicture(c, CourtPicture);
        c.Screen.Text(5);
        ShowPicture(c, Picture);
        c.PassHours(2);
        c.Events.Create(-2, c.Here, 0, FailedDemonstration, 1, 0x63, 0, 8, 0, 0);
        c.Next = CardNumber;
        return false;
    }

    // orig: R077+0x08C8 (1838:08C8) matched; tests: CardSmokeTests
    // C: top-scoring-member is read as an unsigned char.
    /// <summary>The better of the best melee and the best missile fighter shows off.</summary>
    private static OptionHint WeaponChance(CardContext c)
    {
        var melee = c.PartyBestHandWeaponSkill();
        var fighter = (byte)c.BestMember;
        var missile = c.PartyBestMissileSkill();
        var archer = (byte)c.BestMember;
        if (melee < missile)
        {
            c.Variables.Chosen[1] = archer;
            return OptionHint.ChanceOf(missile, archer);
        }
        c.Variables.Chosen[0] = fighter;
        return OptionHint.ChanceOf(melee, fighter);
    }

    // orig: R077+0x0714 (1838:0714) matched; tests: CardSmokeTests
    // C: both members are shown with their equipped weapon (slot 0), the archer too.
    private static bool ShowWeaponSkill(CardContext c, int chance)
    {
        c.PartyBestHandWeaponSkill();
        int fighter = (byte)c.BestMember;
        c.PartyBestMissileSkill();
        var archer = (byte)c.BestMember;
        c.Variables.Chosen[1] = archer;
        var archersWeapon = c.EquippedItem(archer, 0);
        c.NameToSlot(0, archersWeapon != -1 ? archersWeapon : 0x1C, 1);
        c.Variables.Chosen[0] = fighter;
        var fightersWeapon = c.EquippedItem(fighter, 0);
        c.NameToSlot(0, fightersWeapon != -1 ? fightersWeapon : 7, 0);
        if (c.Random(100) <= chance)
        {
            ShowPicture(c, 96);
            c.Screen.Text(6);
            c.PassHours(1);
            GiveTheTask(c, c.City.Ordinal + 0x62, () => AfterTheAudience(c));
            return true;
        }
        c.Screen.Text(7);
        c.PassHours(1);
        c.Events.Create(-2, c.Here, 0, FailedDemonstration, 2, 0x63, 0, 8, 0, 0);
        c.Next = CardNumber;
        return false;
    }

    // orig: R077+0x0A74 (1838:0A74) matched; tests: CardSmokeTests
    // C: fame / 100 is int division.
    private static OptionHint TalesChance(CardContext c)
    {
        var speaking = c.Skill(c.Leader, 0x0A);
        c.Variables.Chosen[0] = c.Leader;
        return OptionHint.ChanceOf((c.State.Party.Fame / 100 + speaking) / 2, c.Leader);
    }

    // orig: R077+0x0918 (1838:0918) matched; tests: CardSmokeTests
    private static bool TellOfAdventures(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            ShowPicture(c, CourtPicture);
            c.Screen.Text(8);
            c.PassHours(1);
            GiveTheTask(c, c.City.Ordinal + 1, () => AfterTheAudience(c));
            return true;
        }
        ShowPicture(c, UnimpressedPicture);
        c.Screen.Text(9);
        c.PassHours(1);
        c.Events.Create(-2, c.Here, 0, FailedDemonstration, 3, 0x63, 0, 8, 0, 0);
        c.Next = CardNumber;
        return false;
    }

    // orig: R077+0x0AB0 (1838:0AB0) matched; R077+0x0AF6 (1838:0AF6) matched; tests: CardSmokeTests
    private static void AdmitOrdinariness(CardContext c)
    {
        ShowPicture(c, UnimpressedPicture);
        c.Screen.Text(0x0A);
        c.PassHours(0);
        c.Next = c.State.PreviousCard;
    }
}
