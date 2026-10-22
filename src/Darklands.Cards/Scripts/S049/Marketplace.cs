using Darklands.Core.Economy;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Marke00": the marketplace by day. The party trades with the goods merchant, a foreign
/// trader or the herbalist (who may also pay for a finished errand, or offer a new one), visits
/// the Fugger, Medici or Hanse offices, the pawnshop, or goes back to the streets. During the
/// days before the city's festival the market is taken over by the fair. Afterwards, the
/// city's politics may call the party aside.
/// </summary>
// orig: R049+0x05B2 (1893:0002) matched; tests: CardSmokeTests
// DORMANT: R049+0x1BB8 (1893:1608) is an empty init; R049+0x1BBA (1893:160A)
//   is a "screwed up." handler nothing installs. The option table is not reset here: lines 10..19 keep
//   whatever the card before left (no text line for them).
public sealed class Marketplace : Card
{
    public const int CardNumber = 0x15;

    private const int Picture = 78, MerchantPicture = 194, HerbalistPicture = 197;

    private const int Challenged = 0x01, MainStreet = 0x06, MainStreetAtNight = 0x08, SideStreet = 0x09, SideStreetAtNight = 0x0A,
        CitySquare = 0x12, MarketAtNight = 0x16, PoliticsCalls = 0x37, Fugger = 0x43, Medici = 0x45, Hanse = 0x47, Fair = 0xB2,
        ErrandOffered = 0x90, ErrandPaid = 0x91, Errand10Offered = 0x151, Errand10Paid = 0x152;

    /// <summary>Events: the fair seen (0x2F), a merchant's errand declined or done lately (kind 7, by merchant), politics settled (0x3E).</summary>
    private const int FairSeen = 0x2F, MerchantBusy = 7, PoliticsSettled = 0x3E, Politics = 2;

    /// <summary>
    /// How likely the watch is to notice the party in a hostile city (the original's module
    /// word): it is set only in a hostile city and never cleared, so it stays in force at
    /// every later market visit while the game runs.
    /// </summary>
    // C: DS:586C is an initialised global (0).
    private int _watchfulness;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        c.PlayMusic(0x0B);
        Show(c, Picture);
        if (c.HereQuery(0x23) != 0 && !c.EventAtHere(FairSeen))
        {
            c.EventHere(FairSeen, 24);
            return Fair;
        }
        OptionState IfThere(int what) => c.HereQuery(what) != 0 ? OptionState.Enabled : OptionState.Hidden;
        var fugger = IfThere(0x0C);
        var medici = IfThere(0x0D);
        var hanse = IfThere(0x0E);
        var pawnshop = IfThere(0x1F);
        c.StandingSeen = c.HereQuery(0);
        if (c.Hostile())
        {
            // C: thunk 09C0:20E9(0x1E, 3, 4): LocalPrice's middle argument is unused.
            _watchfulness = c.LocalPrice(0x1E, 4);
        }

        var goods = new Merchant(Owner: 0, Patron: 0, OfferBelow: 2, NameSeedPaid: 1, NameSeedErrand: 1, QuestType: 1,
            FairWage: true, PaidSiteIcon: 0x1A,
            // C: R029+0x21AA(1, 0, 0, DS:C100, 2): the "array" DS:C100 is passed as its address 0xC100, the mask's low word.
            Shop: () => c.Barter(1, 0x0002C100));
        var foreign = new Merchant(Owner: 4, Patron: 4, OfferBelow: 3, NameSeedPaid: 0x63, NameSeedErrand: 0x63, QuestType: 0x63,
            FairWage: true, PaidSiteIcon: 2,
            // C: (-0x7bc1, 0x3f) is the long mask 0x003F843F.
            Shop: () => c.Barter(-1, 0x003F843F, "Foreign Trader"));
        var herbalist = new Merchant(Owner: 5, Patron: 5, OfferBelow: 2, NameSeedPaid: 0, NameSeedErrand: 5, QuestType: 0x62,
            FairWage: false, PaidSiteIcon: 2, Shop: () => c.Barter(-1, 0x400, "herbalist"));

        var menu = Menu(c, c.Hostile() ? 1 : 0);
        menu.Enable(0, 1, 2, 3, 4, 5, 6, 8, 9);
        menu[3] = fugger;
        menu[4] = medici;
        menu[5] = hanse;
        menu[6] = pawnshop;
        c.Next = Number;
        var next = menu.Run(
            // orig: R049+0x0E4A (1893:089A) + R049+0x0A0A (1893:045A) R049 seg 1893 matched
            new MenuLine(chance => Trade(c, goods, chance), GoodsSafety),
            // orig: R049+0x12F4 (1893:0D44) + R049+0x0EA2 (1893:08F2) R049 seg 1893 matched
            new MenuLine(chance => Trade(c, foreign, chance), ForeignSafety),
            // orig: R049+0x17C0 (1893:1210) + R049+0x134C (1893:0D9C) R049 seg 1893 matched
            new MenuLine(chance => Trade(c, herbalist, chance), Safety),
            // orig: R049+0x183C (1893:128C) + R049+0x180A (1893:125A) R049 seg 1893 matched
            GoToTheOffice(c, Fugger),
            // orig: R049+0x18B8 (1893:1308) + R049+0x1886 (1893:12D6) R049 seg 1893 matched
            GoToTheOffice(c, Medici),
            // orig: R049+0x1934 (1893:1384) + R049+0x1902 (1893:1352) R049 seg 1893 matched
            GoToTheOffice(c, Hanse),
            // orig: R049+0x19B2 (1893:1402) + R049+0x197E (1893:13CE) R049 seg 1893 matched
            new MenuLine(chance =>
                {
                    if (Noticed(c, chance))
                    {
                        c.Next = Challenged;
                        return;
                    }
                    // C: (-0x3c01, 0x2c3e) is the long mask 0x2C3EC3FF.
                    c.Barter(-2, 0x2C3EC3FF, "Pawnshop");
                },
                Safety),
            // orig: R049+0x1A44 (1893:1494) + R049+0x19FC (1893:144C) R049 seg 1893 matched
            // DORMANT: opt_flag_7 is set to 0, so this line (to the main street) is never offered.
            Leave(c, () => c.IsDaylight ? MainStreet : MainStreetAtNight, Safety),
            // orig: R049+0x1AD6 (1893:1526) + R049+0x1A8E (1893:14DE) R049 seg 1893 matched
            Leave(c, () => c.IsDaylight ? MainStreet : MainStreetAtNight, Safety),
            // orig: R049+0x1B68 (1893:15B8) + R049+0x1B20 (1893:1570) R049 seg 1893 matched
            Leave(c, () => c.IsDaylight ? SideStreet : SideStreetAtNight, SideStreetSafety));
        if (next is ErrandPaid or Errand10Paid or ErrandOffered or Errand10Offered)
        {
            return next;
        }
        return ThePoliticsMayIntervene(c, next);
    }

    /// <summary>
    /// A merchant of the market: <paramref name="Owner"/> the quest owner, <paramref name="Patron"/>
    /// the name in $NamedOneName, the offer chance (percent below which an errand is offered), the
    /// seeds of the names drawn, the errand's quest type offset, whether a finished errand of tag 3
    /// pays money (the herbalist pays in herbs), the kind of site of a paid errand's next lead.
    /// </summary>
    private sealed record Merchant(int Owner, int Patron, int OfferBelow, int NameSeedPaid, int NameSeedErrand, int QuestType,
        bool FairWage, int PaidSiteIcon, Action Shop);

    // C: ((D << 1) + D << 1) / -10 + 100: six tenths off; the division by −10 truncates towards zero.
    private OptionHint GoodsSafety() => _watchfulness == 0 ? OptionHint.Say(SuccessAssured) : OptionHint.ChanceOf((_watchfulness * 6) / -10 + 100, OptionHint.WholeParty);

    // C: ((D << 2) + D) / -10 + 100.
    private OptionHint ForeignSafety() => _watchfulness == 0 ? OptionHint.Say(SuccessAssured) : OptionHint.ChanceOf((_watchfulness * 5) / -10 + 100, OptionHint.WholeParty);

    private OptionHint Safety() => _watchfulness == 0 ? OptionHint.Say(SuccessAssured) : OptionHint.ChanceOf(100 - _watchfulness, OptionHint.WholeParty);

    private OptionHint SideStreetSafety() => _watchfulness == 0 ? OptionHint.Say(SuccessAssured) : OptionHint.ChanceOf(_watchfulness / -10 + 100, OptionHint.WholeParty);

    // C: the roll is drawn first, even when nothing threatens.
    private bool Noticed(CardContext c, int chance) => c.Random(100) >= chance && _watchfulness != 0;

    private static int MarketForTheHour(CardContext c) => c.IsDaylight ? CardNumber : MarketAtNight;

    private static int SiteNear(CardContext c, int icon)
    {
        var here = c.State.Locations[c.Here];
        return c.NearestLocationOfType(here.X, here.Y, icon);
    }

    /// <summary>
    /// Visits a merchant: a finished errand (quest tag 3 or 10, resolved) is paid; else the
    /// shop opens. Then, unless an errand of his is running or he was seen lately, he may offer
    /// a new errand (quest 3 to a nearby site, or quest 10), or else is marked as seen for 3 days.
    /// </summary>
    private void Trade(CardContext c, Merchant m, int chance)
    {
        c.Variables.Named[0] = CardContext.PatronNames[m.Patron];
        c.Variables.Chosen[0] = c.Leader;
        if (Noticed(c, chance))
        {
            c.PassHours(0);
            c.Next = Challenged;
            return;
        }
        var size = c.HereQuery(2);
        if (c.Events.ResolvedQuestAt(3, m.Owner, c.Here))
        {
            if (m.FairWage)
            {
                c.State.Party.Purse = c.State.Party.Purse.Add(size * 5, 0, 0);
                c.Variables.Money[0] = Money.From(size * 5, 0, 0);
                c.RandomName(c.City.Ordinal + m.NameSeedPaid, 0, 0);
                ShowPicture(c, MerchantPicture);
                c.Screen.Text(5);
                c.Variables.Location = SiteNear(c, m.PaidSiteIcon);
                c.State.SavedCard = c.IsDaylight ? CardNumber : MarketAtNight;
                c.Next = ErrandPaid;
                c.State.SavedCard2 = m.Owner;
            }
            else
            {
                // The herbalist pays in herbs, to the party's best alchemist.
                c.PartyMaxSkill(7);
                var alchemist = (byte)c.BestMember;
                foreach (var herb in new[] { 0x49, 0x4A, 0x4F, 0x50, 0x4B })
                {
                    c.AddItems(alchemist, herb, c.Random(5) + 5);
                }
                ShowPicture(c, Picture);
                c.Screen.Text(6);
                c.State.SavedCard = MarketForTheHour(c);
                c.Variables.Location = SiteNear(c, 2);
                c.Next = ErrandPaid;
                c.State.SavedCard2 = m.Owner;
            }
            MarkSeen(c, m);
        }
        else if (c.Events.ResolvedQuestAt(0x0A, m.Owner, c.Here))
        {
            c.Variables.Money[0] = Money.From(size, 0, 0);
            c.State.Party.Purse = c.State.Party.Purse.Add(size, 0, 0);
            ShowPicture(c, MerchantPicture);
            c.Screen.Text(5);
            MarkSeen(c, m);
            c.State.SavedCard = c.Events.Find(0x24, -1, c.Here, m.Owner, 0x0A, -1);
            c.State.SavedCard2 = MarketForTheHour(c);
            c.RandomName(c.City.Ordinal + m.NameSeedErrand, 0, 0);
            c.Next = Errand10Paid;
        }
        else
        {
            m.Shop();
        }

        var roll = c.Random(100);

        if (c.Events.QuestWithValue(0x0A, m.Owner, c.Here) || c.Events.QuestWithValue(3, m.Owner, c.Here) || c.EventTypeAtHere(MerchantBusy, m.Owner))
        {
            return;
        }
        if (roll < m.OfferBelow)
        {
            if (m.FairWage)
            {
                c.Variables.Money[0] = Money.From(size * 5, 0, 0);
                c.RandomName(c.City.Ordinal + m.NameSeedPaid, 0, 0);
                ShowPicture(c, MerchantPicture);
                c.Screen.Text(2);
            }
            else
            {
                ShowPicture(c, HerbalistPicture);
                c.Screen.Text(4);
            }
            c.Variables.Location = SiteNear(c, 2);
            c.StartQuest3(m.Owner, c.Variables.Location, c.City.Ordinal + m.QuestType, 0x0F, 1, 0);
            c.PassHours(1);
            c.State.SavedCard = MarketForTheHour(c);
            // C: the herbalist's branch looks the site up a second time (the same site).
            c.State.SavedCard2 = m.FairWage ? c.Variables.Location : SiteNear(c, 2);
            c.Next = ErrandOffered;
            return;
        }
        if (roll < 0x0E)
        {
            c.PassHours(1);
            c.Variables.Money[0] = Money.From(size, 0, 0);
            ShowPicture(c, MerchantPicture);
            c.Screen.Text(2);
            c.State.SavedCard = c.StartQuest10(m.Owner);
            c.State.SavedCard2 = MarketForTheHour(c);
            c.Next = Errand10Offered;
            return;
        }
        MarkSeen(c, m);
    }

    /// <summary>A kind-7 event of the merchant for 72 hours: he is not asked again meanwhile.</summary>
    private static void MarkSeen(CardContext c, Merchant m) =>
        c.Events.Create(-2, c.Here, 0x5F, MerchantBusy, m.Owner, 0, 0, 72, 0, 0);

    private MenuLine GoToTheOffice(CardContext c, int office) =>
        new(() =>
            {
                c.PassHours(0);
                // (the line's chance is asked here, not rated by the menu: the hour passes first and the line has no pop-up.)
                c.Next = Noticed(c, Safety().Chance) ? Challenged : office;
            },
            Safety);

    private MenuLine Leave(CardContext c, Func<int> street, Func<OptionHint> hint) =>
        new(chance =>
            {
                if (Noticed(c, chance))
                {
                    c.PassHours(0);
                    c.Next = Challenged;
                    return;
                }
                c.PassHours(1);
                c.Next = street();
            },
            hint);

    /// <summary>
    /// Leaving the market: an agent of the city's politics may take the party aside (and
    /// later back to the city square), and an uprising under way or decided takes over.
    /// </summary>
    private static int ThePoliticsMayIntervene(CardContext c, int next)
    {
        var here = c.Here;
        var events = c.Events;
        if (events.QuestAt(Politics, here))
        {
            var chance = c.IsDaylight ? 1 : 4;
            chance += -(c.PartyMaxAttribute(3) - 0x23);
            if (events.Fact(Politics, 6, here))
            {
                chance += 5;
            }
            if (c.Random(100) <= chance)
            {
                c.State.SavedCard = CitySquare;
                next = PoliticsCalls;
            }
        }
        if (events.Fact(Politics, 0x15, here) && events.FactValue(Politics, 0, here) == 1 && !c.EventAtHere(PoliticsSettled))
        {
            next = 0xBD;
        }
        if (events.Fact(Politics, 0x16, here) && events.FactValue(Politics, 0, here) == -1 && !c.EventAtHere(PoliticsSettled))
        {
            next = 0xBE;
        }
        if (events.Fact(Politics, 0x15, here) && events.FactValue(Politics, 0, here) == 1)
        {
            next = 0x80;
        }
        if (events.Fact(Politics, 0x16, here) && events.FactValue(Politics, 0, here) == -1)
        {
            next = 0x7B;
        }
        c.Next = next;
        return next;
    }
}
