using Darklands.Core.Characters;
using Darklands.Core.Economy;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Unive00": the university of a city. By day the party asks around for new saints, alchemical
/// formulas, a better philosopher's stone, a seller of rare materials or a teacher; each search is
/// a roll against the leader's learning, and a failed one closes that line for the stay and
/// costs a little standing. By night the gates are locked and only the ways to the streets
/// remain.
/// </summary>
// orig: R056+0x088C (18C0:000C) matched; tests: CardSmokeTests
// orig: R056+0x14D6 (18C0:0C56) matched (init helper: enables all 20 option lines, installs the 4 item and 4 saint pop-up handlers per member row and empties card_items and card_saints; the card fills neither)
// DORMANT: R056+0x1578 (18C0:0CF8) the init helper points opt_fn[0..19] at this "screwed up." default; lines 0..9 get their own handlers and lines 10..19 stay enabled but the MSG has no line for them, so it never runs.
// DORMANT: R056+0x15B0 (18C0:0D30), R056+0x15EC (18C0:0D6C), R056+0x1628 (18C0:0DA8) and R056+0x1664 (18C0:0DE4) the item pop-up handlers (item slots 0..3, each "success is assured" naming the member): the card has no items (card_items stays empty), so no pop-up slot can be chosen.
// DORMANT: R056+0x16A0 (18C0:0E20), R056+0x172A (18C0:0EAA), R056+0x17B4 (18C0:0F34) and R056+0x183E (18C0:0FBE) the saint pop-up handlers (saint slots 0..3, the member's chance with the saint): the card has no saints, so none can be chosen.
public sealed class University_Unive00 : Card
{
    public const int CardNumber = 0x31;

    private const int Picture = 290;
    private const int LibraryPicture = 190, AlchemistsPicture = 197, FrustratedSearchPicture = 353, MerchantPicture = 244;

    /// <summary>The card's kind of event for "this line was tried today at this university" (type = the line).</summary>
    private const int UniversityTried = 0x4E;

    /// <summary>The five lines of a day visit.</summary>
    private const int SaintsLine = 0, FormulasLine = 1, StoneLine = 2, MaterialsLine = 3, TeacherLine = 4;

    private const int Learn = 0x39, AlchemistShop = 0x53, MainStreet = 6, MainStreetAtNight = 8, SideStreet = 9, SideStreetAtNight = 0x0A;

    /// <summary>A teacher the party can hire here (kind 0x28); its type is the skill taught, its value the quality.</summary>
    private const int TeacherOffer = 0x28;

    private CardMenu _menu = null!;

    public override int Number => CardNumber;

    // orig: R056+0x088C (18C0:000C) matched (entry)
    public override int Run(CardContext c)
    {
        c.PlayMusic(0x0F);
        Show(c, Picture);
        c.Variables.Chosen[0] = c.Leader;
        // The stone costs more in a bigger city: size x level x 4 groschen (16-bit arithmetic).
        var level = ((c.HereQuery(0x21) + 7) & 0x0F) + 0x0E;
        c.Variables.Numbers[0] = level;
        c.Variables.Money[0] = Money.From(0, unchecked((short)(c.HereQuery(2) * level * 4)), 0);

        var lines = new OptionState[5];
        Array.Fill(lines, OptionState.Enabled);
        if (c.State.Party.Purse < c.Variables.Money[0])
        {
            lines[StoneLine] = OptionState.Shown;
        }
        for (var line = 0; line < lines.Length; line++)
        {
            if (c.EventTypeAtHere(UniversityTried, line))
            {
                lines[line] = OptionState.Shown;
            }
        }
        var block = 0;
        if (!c.IsDaylight)
        {
            block = 1;
            Array.Fill(lines, OptionState.Hidden);
        }

        _menu = Menu(c, block);
        _menu.Enable(0, 1, 2, 3, 4, 8, 9);
        _menu[SaintsLine] = lines[SaintsLine];
        _menu[FormulasLine] = lines[FormulasLine];
        _menu[StoneLine] = lines[StoneLine];
        _menu[MaterialsLine] = lines[MaterialsLine];
        _menu[TeacherLine] = lines[TeacherLine];
        c.Next = Number;
        // (Round sets $ChosenOneName before its action asks the chance: the actions ask their chance themselves instead of being rated by the menu, as the lines have no pop-up.)
        return _menu.Run(
            // orig: R056+0x0BAA (18C0:032A) matched (option 0 action, see AskAboutSaints); chance handler R056+0x0C8C (see SaintsChance)
            Round(c, () => AskAboutSaints(c, SaintsChance(c).Chance), () => SaintsChance(c)),
            // orig: R056+0x0D14 (18C0:0494) matched (option 1 action, see AskAboutFormulas); chance handler R056+0x0DEE (see ScholarChance)
            Round(c, () => AskAboutFormulas(c, ScholarChance(c).Chance), () => ScholarChance(c)),
            // orig: R056+0x0E7C (18C0:05FC) matched (option 2 action, see ImproveTheStone); chance handler R056+0x0F68 (see ScholarChance)
            Round(c, () => ImproveTheStone(c, ScholarChance(c).Chance), () => ScholarChance(c)),
            // orig: R056+0x0FF6 (18C0:0776) matched (option 3 action, see FindMaterials); chance handler R056+0x10D6 (see ScholarChance)
            Round(c, () => FindMaterials(c, ScholarChance(c).Chance), () => ScholarChance(c)),
            // orig: R056+0x1164 (18C0:08E4) matched (option 4 action, see FindTeachers); chance handler R056+0x12DA (see ScholarChance)
            Round(c, () => FindTeachers(c, ScholarChance(c).Chance), () => ScholarChance(c)),
            // DORMANT: R056+0x1368 (18C0:0AE8) (option 5: no time passes, nothing else) with hint R056+0x1374 (18C0:0AF4) "success is assured": the flag is 0, the line is hidden.
            Round(c, () => c.PassHours(0), OptionHint.Saying(SuccessAssured)),
            // DORMANT: R056+0x13AC (18C0:0B2C) (option 6: to the main street for the hour) with hint R056+0x13C0 (18C0:0B40) "success is assured": the flag is 0, the line is hidden.
            Round(c, () => c.Next = c.IsDaylight ? MainStreet : MainStreetAtNight, OptionHint.Saying(SuccessAssured)),
            // DORMANT: R056+0x13F8 (18C0:0B78) (option 7: no time passes) with hint R056+0x1404 (18C0:0B84) (100% for the leader): the flag is 0, the line is hidden.
            Round(c, () => c.PassHours(0), () => OptionHint.ChanceOf(100, c.Leader)),
            // orig: R056+0x143E (18C0:0BBE) matched (option 8: the main street for the hour); hint R056+0x1452 "success is assured"
            Round(c, () => c.Next = c.IsDaylight ? MainStreet : MainStreetAtNight, OptionHint.Saying(SuccessAssured)),
            // orig: R056+0x148A (18C0:0C0A) matched (option 9: the side street for the hour); hint R056+0x149E "success is assured"
            Round(c, () => c.Next = c.IsDaylight ? SideStreet : SideStreetAtNight, OptionHint.Saying(SuccessAssured)));
    }

    /// <summary>One choice: the leader is the one named in the texts (the loop sets that before the handlers run).</summary>
    private static MenuLine Round(CardContext c, Action action, Func<OptionHint> hint) =>
        new(() =>
            {
                c.Variables.Chosen[0] = c.Leader;
                action();
            },
            hint);

    private void CloseLine(int line) => _menu[line] = OptionState.Shown;

    /// <summary>A failed search takes two hours, costs a little standing and is remembered for a week (the line closes).</summary>
    private void SearchFailed(CardContext c, int line)
    {
        c.PassHours(2);
        c.EventHere(UniversityTried, line, 0xA8);
        c.AdjustReputation(c.Here, -1, -1);
    }

    /// <summary>The party's best Religion, Intelligence, Perception and Latin and a tenth of its fame, with a base of 20, over four.</summary>
    // orig: R056+0x0C8C (18C0:040C) matched
    private static OptionHint SaintsChance(CardContext c)
    {
        // C: the five calls are summed in this order; only the total is used.
        var religion = c.PartyMaxSkill((int)Skill.Religion);
        var intelligence = c.PartyMaxAttribute(4);
        var perception = c.PartyMaxAttribute(3);
        var fame = c.PartyGet(4) / 10;
        var latin = c.PartyMaxSkill((int)Skill.SpeakLatin);
        return OptionHint.ChanceOf(Math.Clamp((religion + intelligence + perception + fame + latin + 0x14) / 4, 0, 99), c.Leader);
    }

    // orig: R056+0x0BAA (18C0:032A) matched
    private void AskAboutSaints(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            c.PassHours(1);
            ShowPicture(c, LibraryPicture);
            c.Screen.Text(3);
            ShowPicture(c, Picture);
            c.PassHours(0);
            c.State.SavedCard = CardNumber;
            c.Next = Learn;
            return;
        }
        SearchFailed(c, SaintsLine);
        ShowPicture(c, LibraryPicture);
        c.Screen.Text(2);
        ShowPicture(c, Picture);
        CloseLine(SaintsLine);
        c.Next = CardNumber;
    }

    /// <summary>The leader and party: Perception, Alchemy, Latin, Intelligence and a tenth of fame (base 20, over four).</summary>
    // orig: R056+0x0DEE (18C0:056E) matched (formulas)
    // orig: R056+0x0F68 (18C0:06E8) matched (the stone; same body)
    // orig: R056+0x10D6 (18C0:0856) matched (materials; same body)
    // orig: R056+0x12DA (18C0:0A5A) matched (teachers; same body)
    private static OptionHint ScholarChance(CardContext c)
    {
        var perception = c.Attribute(Who.Leader, 3);
        var alchemy = c.PartyMaxSkill((int)Skill.Alchemy);
        var latin = c.Skill(Who.Leader, (int)Skill.SpeakLatin);
        var fame = c.PartyGet(4) / 10;
        var intelligence = c.Attribute(Who.Leader, 4);
        return OptionHint.ChanceOf(Math.Clamp((perception + alchemy + latin + fame + intelligence + 0x14) / 4, 0, 99), c.Leader);
    }

    // orig: R056+0x0D14 (18C0:0494) matched
    private void AskAboutFormulas(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            c.PassHours(1);
            ShowPicture(c, AlchemistsPicture);
            c.Screen.Text(4);
            ShowPicture(c, Picture);
            c.State.SavedCard = CardNumber;
            c.Next = AlchemistShop;
            return;
        }
        SearchFailed(c, FormulasLine);
        ShowPicture(c, FrustratedSearchPicture);
        c.Screen.Text(5);
        ShowPicture(c, Picture);
        CloseLine(FormulasLine);
        c.Next = CardNumber;
    }

    /// <summary>The stone only improves when its level is below the one on offer; then the party pays.</summary>
    // orig: R056+0x0E7C (18C0:05FC) matched
    private void ImproveTheStone(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            c.PassHours(1);
            if (c.PartyGet(6) < c.Variables.Numbers[0])
            {
                c.State.Party.PhilosophersStone = c.Variables.Numbers[0];
                c.State.Party.Purse = c.State.Party.Purse.Subtract(c.Variables.Money[0]);
                ShowPicture(c, AlchemistsPicture);
                c.Screen.Text(6);
                ShowPicture(c, Picture);
            }
            else
            {
                ShowPicture(c, FrustratedSearchPicture);
                c.Screen.Text(7);
                ShowPicture(c, Picture);
            }
        }
        else
        {
            SearchFailed(c, StoneLine);
            c.Screen.Text(7);
        }
        CloseLine(StoneLine);
        c.Next = CardNumber;
    }

    // orig: R056+0x0FF6 (18C0:0776) matched
    private void FindMaterials(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            c.PassHours(1);
            ShowPicture(c, AlchemistsPicture);
            c.Screen.Text(8);
            ShowPicture(c, Picture);
            // The university's own shop, in alchemical materials (category mask 0x400).
            c.Barter(-1, 0x400, "University");
        }
        else
        {
            c.PassHours(2);
            c.EventHere(UniversityTried, MaterialsLine, 0xA8);
            c.AdjustReputation(c.Here, -1, -1);
            CloseLine(MaterialsLine);
            ShowPicture(c, FrustratedSearchPicture);
            c.Screen.Text(9);
            ShowPicture(c, Picture);
        }
        c.Next = CardNumber;
    }

    /// <summary>Finds teachers: for a week five teachers (Alchemy, Religion, Latin, Read and Write, Healing) can be hired here.</summary>
    // C: the roll here is strictly below the chance (the others are "not above").
    // orig: R056+0x1164 (18C0:08E4) matched
    private void FindTeachers(CardContext c, int chance)
    {
        if (c.Random(100) < chance)
        {
            c.PassHours(1);
            // C: (unsigned) % 15 + 45 is the extra word of the teacher events.
            var spread = (ushort)c.HereQuery(0x21) % 15 + 45;
            c.Events.Create(-2, c.Here, spread, TeacherOffer, 7, 0x5A, 0, 0xA8, 0, 0);
            c.Events.Create(-2, c.Here, spread, TeacherOffer, 8, 0x46, 0, 0xA8, 0, 0);
            c.Events.Create(-2, c.Here, spread, TeacherOffer, 0x0B, 0x32, 0, 0xA8, 0, 0);
            c.Events.Create(-2, c.Here, spread, TeacherOffer, 0x0C, 0x32, 0, 0xA8, 0, 0);
            c.Events.Create(-2, c.Here, spread, TeacherOffer, 0x0D, 0x6E, 0, 0xA8, 0, 0);
            ShowPicture(c, MerchantPicture);
            c.Screen.Text(0x0A);
        }
        else
        {
            c.PassHours(2);
            c.EventHere(UniversityTried, TeacherLine, 0xA8);
            c.AdjustReputation(c.Here, -1, -1);
            CloseLine(TeacherLine);
            ShowPicture(c, FrustratedSearchPicture);
            c.Screen.Text(0x0B);
        }
        ShowPicture(c, Picture);
        c.Next = CardNumber;
    }
}
