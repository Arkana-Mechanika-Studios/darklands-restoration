using Darklands.Core.Characters;
using Darklands.Core.Economy;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$CityM00": the monastery of a city. For a gift of 60 pfennigs a head the monks pray for the
/// party (restoring Divine Favor), arrange tutors, let the party into their library of saints,
/// treat its wounds or turn it away. A party in disgrace, or without learning and fame, is
/// sent off at the gate (the card then goes to the church).
/// </summary>
// orig: R057+0x0F72 (192F:0002) matched; tests: CardSmokeTests
// orig: R057+0x1C46 (192F:0CD6) matched (init helper: clears the 20 option lines and points opt_fn[0..19] at the default; there is no item or saint pop-up on this card)
// DORMANT: R057+0x1C68 (192F:0CF8) the "screwed up." default for opt_fn[0..19]; lines 0..9 get their own handlers and lines 10..19 stay cleared, so it never runs.
public sealed class Monastery_CityM00 : Card
{
    public const int CardNumber = 0x36;

    private const int Picture = 37;
    private const int GatePicture = 292, LibrarianPicture = 302, PrayerRoomPicture = 259, MonkPicture = 263, TutorPicture = 262,
        MonkOfficePicture = 299, AbbotPicture = 243, DeniedPicture = 310, DeniedFriendlyPicture = 297,
        HealingDeniedPicture = 295;

    private const int Church = 0x13, Learn = 0x39, Healer = 0xB4, Cleric = 0x6F, NoWay = 0x62;
    private const int MonasteryByDay = 0x36, MonasteryAtNight = 0x38;

    /// <summary>The monastery's events, per location: the visit of the day, the library, the prayers, the tutors asked, the healing asked, the tutors hired.</summary>
    private const int PrayersGiven = 0x30, TutorsAsked = 0x28, TutorsRefused = 0x33, LibraryRefused = 0x34, HealingAsked = 0x31, SeenToday = 0x32;

    /// <summary>A teacher the party can hire here (kind 0x28); its type is the skill taught, its value the quality.</summary>
    private const int TeacherOffer = 0x28;

    private const int Redraw = int.MinValue;

    private const int PrayersOption = 0, TutorsOption = 1, LibraryOption = 2, HealingOption = 3;

    private CardMenu _menu = null!;

    public override int Number => CardNumber;

    // orig: R057+0x0F72 (192F:0002) matched (entry)
    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.StandingSeen = c.HereQuery(0);
        var states = new[]
        {
            OptionState.Enabled, OptionState.Enabled, OptionState.Enabled, OptionState.Enabled, OptionState.Enabled,
            OptionState.Hidden, OptionState.Hidden, OptionState.Hidden, OptionState.Enabled, OptionState.Hidden,
        };
        if (c.EventAtHere(PrayersGiven))
        {
            states[PrayersOption] = OptionState.Shown;
        }
        if (c.EventAtHere(TutorsAsked))
        {
            states[TutorsOption] = OptionState.Shown;
        }
        if (c.EventAtHere(TutorsRefused))
        {
            states[TutorsOption] = OptionState.Shown;
        }
        if (c.EventAtHere(LibraryRefused))
        {
            states[LibraryOption] = OptionState.Shown;
        }
        if (c.EventAtHere(HealingAsked))
        {
            states[HealingOption] = OptionState.Shown;
        }
        c.Variables.Money[0] = Gift(c);
        if (c.State.Party.Purse < c.Variables.Money[0])
        {
            states[PrayersOption] = OptionState.Shown;
        }

        _menu = Menu(c, 0);
        _menu.Enable(0, 1, 2, 3, 4, 5, 6, 7, 8, 9);
        for (var k = 0; k < states.Length; k++)
        {
            _menu[k] = states[k];
        }
        var lines = new MenuLine[]
        {
            // orig: R057+0x12FA (192F:038A) matched (option 0 action, see PayForPrayers; read from the disassembly); hint R057+0x140C "no risk" for the leader
            Round(c, () => PayForPrayers(c), () => OptionHint.Say(NoRisk, c.Leader)),
            // orig: R057+0x1446 (192F:04D6) matched (option 1 action, see AskAboutTutors); chance handler R057+0x15E2 (see TutorsChance)
            Round(c, chance => AskAboutTutors(c, chance), () => TutorsChance(c)),
            // orig: R057+0x1666 (192F:06F6) matched (option 2 action, see AskForTheLibrary; read with the asm); chance handler R057+0x17CC (see LibraryChance)
            Round(c, chance => AskForTheLibrary(c, chance), () => LibraryChance(c)),
            // orig: R057+0x1834 (192F:08C4) matched (option 3 action, see AskForHealing; read with the asm); chance handler R057+0x1986 (see HealingChance)
            Round(c, chance => AskForHealing(c, chance), () => HealingChance(c)),
            // orig: R057+0x1A10 (192F:0AA0) matched (option 4: sanctuary is refused, the church is the place for it); hint R057+0x1A4C "no risk" for the leader
            Round(c, () =>
                {
                    ShowPicture(c, MonkOfficePicture);
                    c.Screen.Text(0x0C);
                    c.Next = Church;
                },
                () => OptionHint.Say(NoRisk, c.Leader)),
            // DORMANT: R057+0x1A86 (192F:0B16) (option 5, "ask about the monks' problems": only remembers the church as the card to return to) with hint R057+0x1A8E (192F:0B1E) "success is assured" for the leader: the flag is 0, the line is hidden.
            Round(c, () => c.State.SavedCard = Church, () => OptionHint.Say(SuccessAssured, c.Leader)),
            // DORMANT: R057+0x1AC8 (192F:0B58) (option 6, "speak with the abbot": when the monks' task is still open and unsolved, the abbot's text and the cleric's card) with hint R057+0x1B30 (192F:0BC0) "no risk": the flag is 0, the line is hidden.
            Round(c, () =>
                {
                    if (c.Events.QuestAt(2, c.Here) && c.Events.FactValue(2, 0, c.Here) != 0)
                    {
                        ShowPicture(c, AbbotPicture);
                        c.Screen.Text(9);
                        c.State.SavedCard = CardNumber;
                        c.Next = Cleric;
                    }
                },
                OptionHint.Saying(NoRisk)),
            // DORMANT: R057+0x1B6C (192F:0BFC) (option 7, "a quiet place to pray": back to the previous card) with hint R057+0x1B74 (192F:0C04) "success is assured": the flag is 0, the line is hidden.
            Round(c, () => c.Next = c.State.PreviousCard, OptionHint.Saying(SuccessAssured)),
            // orig: R057+0x1BAC (192F:0C3C) matched (option 8: leave, back to the church); hint R057+0x1BB4 "success is assured"
            Round(c, () => c.Next = Church, OptionHint.Saying(SuccessAssured)),
            // DORMANT: R057+0x1BEC (192F:0C7C) (option 9: the gate-keeper's refusal text, then card 0x62) with hint R057+0x1C0A (192F:0C9A) "success is assured": the flag is 0, the line is hidden.
            Round(c, () =>
                {
                    c.Screen.Text(0);
                    c.PassHours(0);
                    c.Next = NoWay;
                },
                OptionHint.Saying(SuccessAssured)),
        };

        // The C loop: every round the gift is worked out again, a party in disgrace or too unlearned
        // is turned away, the visit is noted for twelve hours, and the menu is drawn (text 5 for a
        // second visit within those hours).
        while (true)
        {
            c.Variables.Money[0] = Gift(c);
            if (c.StandingSeen <= -0x28 || c.PartyMaxSkill((int)Skill.Virtue) + c.PartyGet(4) / 0x14 <= 0x0F)
            {
                ShowPicture(c, GatePicture);
                c.Screen.Text(1);
                return c.Next = Church;
            }
            int block;
            if (c.EventAtHere(SeenToday))
            {
                block = 5;
            }
            else
            {
                block = 0;
                c.EventHere(SeenToday, 12);
            }
            _menu.Block = block;
            c.Next = Number;
            var next = _menu.Run(lines);
            if (next != Redraw)
            {
                return next;
            }
        }
    }

    /// <summary>A gift of 60 pfennigs for every slot of the party.</summary>
    private static Money Gift(CardContext c) => Money.From(0, 0, c.PartySize * 0x3C);

    /// <summary>One choice: when the card stays, the menu is drawn again with the new state.</summary>
    private static MenuLine Round(CardContext c, Action action, Func<OptionHint> hint) =>
        new(() =>
            {
                action();
                if (c.Next == CardNumber)
                {
                    c.Next = Redraw;
                }
            },
            hint);

    /// <summary>Like the other <c>Round</c>, for an action that takes the chance of its hint.</summary>
    private static MenuLine Round(CardContext c, Action<int> action, Func<OptionHint> hint) =>
        new(chance =>
            {
                action(chance);
                if (c.Next == CardNumber)
                {
                    c.Next = Redraw;
                }
            },
            hint);

    private void CloseOption(int option) => _menu[option] = OptionState.Shown;

    private int ThisCardForTheHour(CardContext c) => c.IsDaylight ? MonasteryByDay : MonasteryAtNight;

    /// <summary>The monks pray: a gift, a few hours, and Divine Favor for every member that is there.</summary>
    // orig: R057+0x12FA (192F:038A) matched
    private void PayForPrayers(CardContext c)
    {
        ShowPicture(c, LibrarianPicture);
        c.Screen.Text(2);
        c.PassHours(c.Random(2) + 1);
        c.Variables.Money[0] = Gift(c);
        c.State.Party.Purse = c.State.Party.Purse.Subtract(c.Variables.Money[0]);
        for (var member = 0; member < c.PartySize; member++)
        {
            if (c.Status(member) != CharacterStatus.Empty)
            {
                c.AddAttribute(member, 6, c.Skill(member, (int)Skill.Religion) / 9 + 1);
            }
        }
        ShowPicture(c, PrayerRoomPicture);
        c.Screen.Text(3);
        ShowPicture(c, Picture);
        c.EventHere(PrayersGiven, 0xA8);
        CloseOption(PrayersOption);
        c.Next = ThisCardForTheHour(c);
    }

    /// <summary>The better of the party's average Virtue plus standing plus fame, and its best Virtue plus Charisma.</summary>
    // orig: R057+0x15E2 (192F:0672) matched
    private static OptionHint TutorsChance(CardContext c)
    {
        var byStanding = c.PartyAverageSkill((int)Skill.Virtue) + (c.HereQuery(0) + c.PartyGet(4) / 0x32);
        var byCharm = c.PartyMaxSkill((int)Skill.Virtue) + c.PartyMaxAttribute(5);
        var chance = Math.Clamp(Math.Max(byStanding, byCharm), 1, 99);
        // The member shown is the best one of the last query, unless the standing side is at least as good.
        return OptionHint.ChanceOf(chance, byStanding >= byCharm ? OptionHint.WholeParty : c.BestMember);
    }

    // orig: R057+0x1446 (192F:04D6) matched
    private void AskAboutTutors(CardContext c, int chance)
    {
        ShowPicture(c, MonkPicture);
        c.Screen.Text(4);
        ShowPicture(c, Picture);
        c.PassHours(2);
        if (c.Random(100) <= chance)
        {
            // C: the price in groschen is the city size plus the purse in 150-pfennig lumps, kept in 12..180 and divided by 12 (16-bit arithmetic).
            var lumps = unchecked((short)(c.State.Party.Purse.TotalPfennigs / 0x96));
            int sum = unchecked((short)(c.HereQuery(2) + lumps));
            var price = Math.Clamp(sum, 0x0C, 0xB4) / 0x0C;
            c.Variables.Money[0] = Money.From(0, price, 0);
            c.Events.Create(-2, c.Here, 0x32, TeacherOffer, 8, 0x32, 0, 0xA8, 0, 0);
            c.Events.Create(-2, c.Here, 0x32, TeacherOffer, 0x0B, 0x19, 0, 0xA8, 0, 0);
            c.Events.Create(-2, c.Here, 0x32, TeacherOffer, 0x0C, 0x19, 0, 0xA8, 0, 0);
            ShowPicture(c, TutorPicture);
            c.Screen.Text(8);
            ShowPicture(c, Picture);
            CloseOption(TutorsOption);
            c.Next = CardNumber;
            return;
        }
        c.EventHere(TutorsRefused, 0x37);
        ShowPicture(c, TutorPicture);
        c.Screen.Text(6);
        ShowPicture(c, Picture);
        CloseOption(TutorsOption);
        c.Next = CardNumber;
    }

    /// <summary>
    /// The party's best Latin plus Read-and-Write sum, plus half a bad standing and 25 for prayers
    /// already given; 75 or more is a sure thing.
    /// </summary>
    // orig: R057+0x17CC (192F:085C) matched ("cannot print stale" in the tool's note: hand-built, checked against the asm)
    // C: party_max_attr_sum(0xb, 0xc) is called with SKILL numbers but reads the member record as an attribute table (+0x49 + k): bytes 0xb and 0xc there are the Intelligence and Charisma MAXIMA, not Latin and Read-and-Write; the value kept is twice the first byte (see MaxOfRecordBytes).
    private static OptionHint LibraryChance(CardContext c)
    {
        var chance = MaxOfRecordBytes(c, 0x0B, 0x0C);
        var member = c.BestMember;
        if (c.StandingSeen < 0)
        {
            chance += c.StandingSeen / 2;
        }
        if (c.EventAtHere(PrayersGiven))
        {
            chance += 0x19;
        }
        chance = Math.Clamp(chance, 0, 99);
        if (chance >= 0x4B)
        {
            chance = 100;
        }
        return OptionHint.ChanceOf(chance, member);
    }

    /// <summary>
    /// The original's <c>party_max_attr_sum</c> over raw record bytes: member record byte
    /// <c>0x49 + k</c> is attribute k for k 0..6, the attribute maximum k-7 for k 7..13, then the
    /// skills. It keeps twice the first byte of the best sum (original bug, see
    /// <c>OriginalBehaviour.FixAttributeSums</c>) and the member's slot.
    /// </summary>
    // orig: R029+0x179C (party_max_attr_sum) draft (checked against asm)
    private static int MaxOfRecordBytes(CardContext c, int first, int second)
    {
        static int Byte(Character m, int k) =>
            k < Character.AttributeCount ? m.Attributes[k]
            : k < 2 * Character.AttributeCount ? m.AttributeMaximums[k - Character.AttributeCount]
            : m.Skills[k - 2 * Character.AttributeCount];
        var last = c.State.Party.Last;
        last.Value = 0;
        for (var slot = 0; slot < c.PartySize; slot++)
        {
            var m = c.State.Party.Members[slot];
            if (m.Status == CharacterStatus.Empty)
            {
                continue;
            }
            if (Byte(m, first) + Byte(m, second) > last.Value)
            {
                last.Value = (byte)(c.State.Behaviour.FixAttributeSums ? Byte(m, first) + Byte(m, second) : Byte(m, first) * 2);
                last.Member = slot;
            }
        }
        return last.Value;
    }

    // orig: R057+0x1666 (192F:06F6) matched (read with the asm)
    private void AskForTheLibrary(CardContext c, int chance)
    {
        CloseOption(LibraryOption);
        if (c.Random(100) <= chance)
        {
            ShowPicture(c, MonkPicture);
            c.Screen.Text(4);
            c.PassHours(1);
            c.EventHere(LibraryRefused, 0x5A0);
            c.State.SavedCard = Church;
            c.Next = Learn;
            return;
        }
        c.PassHours(1);
        c.EventHere(LibraryRefused, 0x2D0);
        if (!c.EventAtHere(PrayersGiven))
        {
            ShowPicture(c, MonkPicture);
            c.Screen.Text(4);
            c.PassHours(1);
            ShowPicture(c, DeniedPicture);
            c.Screen.Text(7);
        }
        else
        {
            c.PassHours(1);
            ShowPicture(c, DeniedFriendlyPicture);
            c.Screen.Text(0x10);
        }
        ShowPicture(c, Picture);
        c.Next = ThisCardForTheHour(c);
    }

    /// <summary>
    /// True when nobody is hurt: no present member's Strength is at or below 90% of its maximum.
    /// </summary>
    private static bool NobodyHurt(CardContext c)
    {
        var allWell = true;
        for (var member = 0; member < c.PartySize; member++)
        {
            if (c.Status(member) != CharacterStatus.Empty && c.AttributeMaximum(member, 1) * 9 / 10 >= c.Attribute(member, 1))
            {
                allWell = false;
            }
        }
        return allWell;
    }

    /// <summary>The Virtue of the party (+20), but nothing at all if nobody is hurt.</summary>
    // orig: R057+0x1986 (192F:0A16) matched (read with the asm)
    private static OptionHint HealingChance(CardContext c)
    {
        var chance = c.PartyAverageSkill((int)Skill.Virtue) + 0x14;
        if (NobodyHurt(c))
        {
            chance = 0;
        }
        return OptionHint.ChanceOf(Math.Clamp(chance, 0, 100), OptionHint.WholeParty);
    }

    // orig: R057+0x1834 (192F:08C4) matched (read with the asm)
    private void AskForHealing(CardContext c, int chance)
    {
        ShowPicture(c, MonkPicture);
        c.Screen.Text(4);
        ShowPicture(c, Picture);
        c.PassHours(1);
        if (c.Random(100) <= chance)
        {
            c.State.SavedCard = Church;
            c.Next = Healer;
            return;
        }
        CloseOption(HealingOption);
        if (NobodyHurt(c))
        {
            ShowPicture(c, MonkOfficePicture);
            c.Screen.Text(0x0A);
        }
        else
        {
            ShowPicture(c, HealingDeniedPicture);
            c.Screen.Text(0x0B);
            ShowPicture(c, Picture);
        }
        c.Next = Church;
    }
}
