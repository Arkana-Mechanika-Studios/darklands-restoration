using Darklands.Core.Characters;
using Darklands.Core.Economy;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Dunge00": the city dungeon. Stripped of its belongings, the party picks the lock, climbs
/// out, digs a tunnel, has a woman charm the jailer, faces the magistrate, rests, prays,
/// waits for the gaoler's round or drinks a potion. Failed attempts make the guards watchful
/// (the dungeon's state 1, then 2: condemned); a saint can turn the judgement (state 3).
/// </summary>
// orig: R047+0x01A6 (1852:0006) matched; tests: CardSmokeTests
// DORMANT: R047+0x0FAC (1852:0E0C), the init, gives option lines 9..19 the
//   "screwed up." handler R047+0x102A and enables them; line 9 is never copied from
//   opt_flag_9 (which the card sets to 0), so it stays enabled, with no action. The texts have nine
//   option lines, so none of them can be picked.
public sealed class CityDungeon : Card
{
    public const int CardNumber = 0x0D;

    private const int Picture = 41, CellPicture = 41, BeatenPicture = 279, CreepPicture = 30, TowerPicture = 32,
        StreetPicture = 55, TunnelPicture = 16, SeducePicture = 29, CourtPicture = 289, ChasePicture = 46;

    private const int SideStreet = 0x09, SideStreetAtNight = 0x0A, CitySquare = 0x12, CitySquareAtNight = 0x19,
        Chase = 0x7A, Magistrate = 0x83, Execution = 0x8C;

    private const int Lockpicks = 0x40, Rope = 0x0F, PotionUsed = 0x63;

    /// <summary>A fellow prisoner who may join (kind 0x4D at the city; its type is the stored character).</summary>
    private const int FellowPrisoner = 0x4D;

    /// <summary>The menu ends to be offered again with the dungeon's new state.</summary>
    private const int Redraw = int.MinValue;

    /// <summary>The dungeon's state: 0 newly in, 1 the guards watchful, 2 condemned, 3 saved by a saint's sign (the original's module word).</summary>
    private int _state;

    /// <summary>How far the tunnel has come (the original's second module word).</summary>
    private int _tunnel;

    /// <summary>The picture the card returns to (a saint's sign changes it for good).</summary>
    private int _picture;

    private readonly OptionState[] _flags = new OptionState[9];

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        c.PlayMusic(0x16);
        _picture = Picture;
        Show(c, _picture);
        for (var k = 0; k < _flags.Length; k++)
        {
            _flags[k] = OptionState.Enabled;
        }
        c.StandingSeen = c.HereQuery(0);
        c.LoseItems(Who.Everybody);
        c.Variables.Money[0] = Money.From(0, 0, 1);
        if (c.State.PreviousCard == Magistrate)
        {
            // C: back from the magistrate the dungeon's state comes back from the party's map position, where it was kept.
            _state = c.State.MapPosition.X;
            _tunnel = c.State.MapPosition.Y;
        }
        else
        {
            _state = 0;
            _tunnel = 0;
        }
        if (c.CountItems(Who.Everybody, Lockpicks) == 0)
        {
            _flags[0] = OptionState.Shown;
        }
        if (c.AbleWomen() == 0)
        {
            _flags[3] = OptionState.Hidden;
        }
        if (c.PresentCount() < 4 && c.EventAtHere(FellowPrisoner))
        {
            var character = c.EventType(c.Events.Find(FellowPrisoner, -1, c.Here, -1, -1, -1));
            if (character != -1)
            {
                c.Variables.Chosen[0] = c.CharacterJoins(character);
                Show(c, _picture);
                c.Screen.Text(0x12, TextFrame.BlankPage);
            }
        }

        while (true)
        {
            c.SetCardItems(PotionUsed);
            switch (_state)
            {
                case 0:
                    c.SetCardSaints(0x0E, 0x23, 0x6C, 0x72);
                    _flags[6] = c.SaintsUsable();
                    _flags[8] = c.ItemsUsable();
                    break;
                case 1 or 3:
                    c.SetCardSaints(0x0E, 0x23, 0x6C, 0x57);
                    _flags[6] = c.SaintsUsable();
                    _flags[1] = OptionState.Hidden;
                    _flags[3] = OptionState.Hidden;
                    _flags[8] = c.ItemsUsable();
                    break;
                case 2:
                    c.SetCardSaints(0x0E, 0x23, 0x6C, 0x50);
                    for (var k = 0; k <= 4; k++)
                    {
                        _flags[k] = OptionState.Hidden;
                    }
                    _flags[6] = c.SaintsUsable();
                    _flags[8] = OptionState.Hidden;
                    break;
            }
            var menu = Menu(c, _state);
            menu.Enable(0, 1, 2, 3, 4, 5, 6, 7, 8);
            for (var k = 0; k < _flags.Length; k++)
            {
                menu[k] = _flags[k];
            }
            c.Next = Number;
            var next = menu.Run(
                // orig: R047+0x0616 (1852:0476) + R047+0x04FE (1852:035E) R047 seg 1852 matched
                Stay(c, chance => PickTheLock(c, chance), () => LockChance(c)),
                // orig: R047+0x0766 (1852:05C6) + R047+0x068A (1852:04EA) R047 seg 1852 matched
                Stay(c, chance => ClimbOut(c, chance), () => ClimbChance(c)),
                // orig: R047+0x0950 (1852:07B0) + R047+0x079E (1852:05FE) R047 seg 1852 matched
                Stay(c, () => DigATunnel(c), () => OptionHint.Say("Success may take some time")),
                // orig: R047+0x0A8C (1852:08EC) + R047+0x097A (1852:07DA) R047 seg 1852 matched
                Stay(c, chance => CharmTheJailer(c, chance), () => CharmChance(c)),
                // orig: R047+0x0AC8 (1852:0928) + R047+0x0AAA (1852:090A) R047 seg 1852 matched
                Stay(c, () => FaceTheMagistrate(c), OptionHint.Saying(NoRisk)),
                // orig: R047+0x0BD2 (1852:0A32) + R047+0x0B00 (1852:0960) R047 seg 1852 matched
                Stay(c, () => Rest(c), OptionHint.Saying(NoRisk)),
                // orig: R047+0x0E4A (1852:0CAA) + R047+0x0C0A (1852:0A6A) R047 seg 1852 matched
                Stay(c, _ => Pray(c), OptionHint.Saying(CardTexts.SelectSaint), () => SaintChance(c)),
                // orig: R047+0x0EDE (1852:0D3E) + R047+0x0E82 (1852:0CE2) R047 seg 1852 matched
                Stay(c, () => WaitForTheRound(c), () => OptionHint.Say(NoRisk, c.Leader)),
                // orig: R047+0x0F74 (1852:0DD4) + R047+0x0F18 (1852:0D78) R047 seg 1852 matched
                // orig: R047+0x1062 (1852:0EC2): the potion pop-up's chance, 100 for the member
                Stay(c, _ => DrinkThePotion(c), OptionHint.Saying(CardTexts.SelectPotion), () => OptionHint.ChanceOf(100, c.MenuMember)));
            if (next != Redraw)
            {
                return next;
            }
        }
    }

    /// <summary>An option whose state is the flag it has now (set with the menu); an action that stays here offers the menu again (its block and lines follow the state).</summary>
    private static MenuLine Stay(CardContext c, Action act, Func<OptionHint> hint) =>
        new(() =>
            {
                act();
                if (c.Next == CardNumber)
                {
                    c.Next = Redraw;
                }
            },
            hint);

    /// <summary>Like the other <c>Stay</c>, for an action that takes the chance of its hint (the pop-up's, for a pop-up choice).</summary>
    private static MenuLine Stay(CardContext c, Action<int> act, Func<OptionHint> hint, Func<OptionHint>? popup = null) =>
        new(chance =>
            {
                act(chance);
                if (c.Next == CardNumber)
                {
                    c.Next = Redraw;
                }
            },
            hint, popup);

    private void ReturnToTheCell(CardContext c) => ShowPicture(c, _picture);

    /// <summary>The guards grow watchful: newly in becomes 1, anything else 2.</summary>
    private void TheGuardsGrowWatchful() => _state = _state != 0 ? 2 : 1;

    /// <summary>The party's best Artifice: +50 when the guards are watchful, −50 after a saint's sign, nothing once condemned.</summary>
    private OptionHint LockChance(CardContext c)
    {
        c.PartyMaxSkill(0x0E);
        var member = c.BestMember;
        c.Variables.Chosen[0] = member;
        var skill = _state switch
        {
            0 => c.Skill(member, 0x0E),
            1 => c.Skill(member, 0x0E) + 50,
            3 => c.Skill(member, 0x0E) - 50,
            _ => 0,
        };
        return OptionHint.ChanceOf(Math.Clamp(skill, 0, 99), member);
    }

    private void PickTheLock(CardContext c, int chance)
    {
        c.PartyMaxSkill(0x0E);
        var member = c.BestMember;
        c.Variables.Chosen[0] = member;
        if (c.Random(100) > chance)
        {
            ShowPicture(c, BeatenPicture);
            c.Screen.Text(5);
            ReturnToTheCell(c);
            c.Injure(Who.Everybody, 0, 1, 20);
            c.PassHours(3);
            c.RemoveItemStack(Who.Everybody, Lockpicks);
            _flags[0] = OptionState.Shown;
            c.PracticeSkill(member, 0x0E, 0, 10, 0);
            TheGuardsGrowWatchful();
            _tunnel = 0;
            c.Next = CardNumber;
            return;
        }
        c.PracticeSkill(member, 0x0E, 1, 10, 0);
        c.PassHours(1);
        c.AddItem(Who.Everybody, 7);
        ShowPicture(c, CreepPicture);
        c.Screen.Text(6);
        FightTheGuards(c);
    }

    /// <summary>A third of the best Agility plus Strength (recorded, original bug, as twice the Agility).</summary>
    private static OptionHint ClimbChance(CardContext c)
    {
        c.PartyMaxAttributeSum(2, 1);
        var member = c.BestMember;
        c.Variables.Chosen[0] = member;
        // C: (unsigned char)(best_value / 3).
        return OptionHint.ChanceOf(Math.Clamp((int)(byte)(c.BestValue / 3), 0, 100), member);
    }

    private void ClimbOut(CardContext c, int chance)
    {
        c.PassHours(1);
        var member = c.Variables.Chosen[0];
        if (c.Random(100) <= chance)
        {
            ShowPicture(c, TowerPicture);
            c.Screen.Text(0x0A);
            c.PracticeSkill(member, 0x0F, 1, 10, 0);
            c.AddItem(Who.Everybody, Rope);
            c.Next = Chase;
            return;
        }
        ShowPicture(c, CellPicture);
        c.Screen.Text(0x0B);
        ReturnToTheCell(c);
        c.PracticeSkill(member, 0x0F, 0, 10, 0);
        _tunnel = 0;
        TheGuardsGrowWatchful();
        c.Next = CardNumber;
    }

    /// <summary>
    /// Half a day of digging: an afternoon may bring the hangman (1 in 9); the tunnel grows
    /// (by 50 after a saint's sign), and past 95 the party is out; else the guards may find it
    /// (a quarter of its length in percent).
    /// </summary>
    private void DigATunnel(CardContext c)
    {
        c.PassHours(12);
        // C: the && skips the draw before one o'clock.
        if (c.State.Date.Hour > 12 && c.Random(9) == 1)
        {
            ShowPicture(c, BeatenPicture);
            c.Screen.Text(0x10);
            c.Next = Execution;
            return;
        }
        if (_state == 3)
        {
            _tunnel += 50;
        }
        else
        {
            _tunnel += c.Random(6) + 12;
        }
        if (_tunnel > 95)
        {
            ShowPicture(c, StreetPicture);
            c.Screen.Text(0x0C);
            c.AdjustReputation(c.Here, -1, -6);
            c.Next = c.IsDaylight ? SideStreet : SideStreetAtNight;
            return;
        }
        if (c.Random(100) < _tunnel / 4)
        {
            ShowPicture(c, BeatenPicture);
            c.Screen.Text(0x0D);
            ReturnToTheCell(c);
            c.Injure(Who.Everybody, 0, 1, 20);
            _tunnel = 0;
            _state = _state == 3 ? 2 : _state + 1;
            c.Next = CardNumber;
            return;
        }
        c.Variables.Numbers[0] = _tunnel;
        ShowPicture(c, TunnelPicture);
        c.Screen.Text(0x16);
        ReturnToTheCell(c);
    }

    /// <summary>The best Charisma among the able women; the member is the one the attribute query found.</summary>
    // C: the member is the attribute queries' own word (DS:9A44), which R029 174a sets together with best_member.
    // UNCERTAIN: with no able woman neither word is set and both keep older values; the C# then uses best_member.
    private static OptionHint CharmChance(CardContext c)
    {
        var chance = c.PartyMaxAttributeOfWomen(5);
        return OptionHint.ChanceOf(chance, c.BestMember);
    }

    private void CharmTheJailer(CardContext c, int chance)
    {
        c.PartyMaxAttributeOfWomen(5);
        var member = c.BestMember;
        c.Variables.Chosen[0] = member;
        if (c.Random(100) <= chance)
        {
            c.PassHours(48);
            ShowPicture(c, SeducePicture);
            c.Screen.Text(0x0E);
            c.Next = c.IsDaylight ? SideStreet : SideStreetAtNight;
            c.PracticeSkill(member, 0x0A, 1, 10, 0);
            c.AddSkill(member, 9, -c.ImproveRoll(c.Skill(member, 9), 2, 6));
            return;
        }
        c.PassHours(2);
        ShowPicture(c, BeatenPicture);
        c.Screen.Text(0x0F);
        ReturnToTheCell(c);
        c.PracticeSkill(member, 0x0A, 0, 10, 0);
        c.Next = CardNumber;
    }

    /// <summary>Before the magistrate (card 0x83), who sees the dungeon's state: it is kept in the party's map position meanwhile.</summary>
    private void FaceTheMagistrate(CardContext c)
    {
        c.PassHours(3);
        // C: party_x/party_y are overwritten with the dungeon's two words (the map position is restored when the party leaves the city).
        c.State.MapPosition = new Core.World.MapPosition(_state, _tunnel);
        c.Next = Magistrate;
    }

    /// <summary>Rests: Divine Favor (attribute 6) recovers, more after a saint's sign; an afternoon may bring the hangman (1 in 10).</summary>
    private void Rest(CardContext c)
    {
        c.AddAttribute(Who.Everybody, 6, _state == 3 ? c.Random(10) + 12 : c.Random(10) + 2);
        ShowPicture(c, CourtPicture);
        c.Screen.Text(0x11);
        c.PassHours(12);
        if (c.State.Date.Hour > 12 && c.Random(10) == 1)
        {
            ShowPicture(c, BeatenPicture);
            c.Screen.Text(0x10);
            c.Next = Execution;
            return;
        }
        ReturnToTheCell(c);
        c.Next = CardNumber;
    }

    // orig: R047+0x107A (1852:0EDA), R047+0x1104 (1852:0F64),
    //   R047+0x118E (1852:0FEE), R047+0x1218 (1852:1078) R047 seg 1852 matched:
    //   the four saint pop-up handlers (identical).
    private static OptionHint SaintChance(CardContext c)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        var saint = c.CardSaints[c.MenuSlot];
        var low = c.SaintChanceLow(c.MenuMember, saint);
        return OptionHint.Say(c.ChanceText(low, c.SaintChanceHigh(c.MenuMember, saint)));
    }

    /// <summary>
    /// The four saints: the first frees the party for half its money, the second and third
    /// free it at a cost in standing, the fourth (by the dungeon's state) opens the way, gives a
    /// sign that halves the sentence, or wins the city's favour once condemned.
    /// </summary>
    private void Pray(CardContext c)
    {
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (answer == -1)
        {
            return;
        }
        c.PassHours(3);
        if (answer == 0)
        {
            ShowPicture(c, CellPicture);
            c.Screen.Text(0x17);
            ReturnToTheCell(c);
            c.PassHours(3);
            c.Next = CardNumber;
            return;
        }
        c.NameToSlot(1, c.CardSaints[c.MenuSlot], 0);
        switch (c.MenuSlot)
        {
            case 0:
                ShowPicture(c, StreetPicture);
                c.Screen.Text(7);
                // C: (unsigned)money_pfennigs(purse) >> 1: the 32-bit total is cut to 16 bits before halving.
                c.State.Party.Purse = Money.FromPfennigs((ushort)c.State.Party.Purse.TotalPfennigs >> 1);
                c.Next = c.IsDaylight ? CitySquare : CitySquareAtNight;
                break;
            case 1 or 2:
                ShowPicture(c, StreetPicture);
                c.Screen.Text(0x13);
                c.AdjustReputation(c.Here, -1, -8);
                c.Next = c.IsDaylight ? CitySquare : CitySquareAtNight;
                break;
            case 3:
                switch (_state)
                {
                    case 0:
                        ShowPicture(c, TowerPicture);
                        c.Screen.Text(0x14);
                        c.Next = c.IsDaylight ? SideStreet : SideStreetAtNight;
                        break;
                    case 1:
                        // C: the card's own picture buffer is overwritten: the cell shows the court picture from now on.
                        _picture = CourtPicture;
                        ShowPicture(c, _picture);
                        c.Screen.Text(3);
                        _state = 3;
                        break;
                    case 2:
                        ShowPicture(c, StreetPicture);
                        c.Screen.Text(0x15);
                        c.AdjustReputation(c.Here, 2, 7);
                        c.Next = c.IsDaylight ? CitySquare : CitySquareAtNight;
                        break;
                    // DORMANT: state 3 (after the sign) has no case: the prayer is answered and nothing happens.
                }
                break;
        }
    }

    /// <summary>Waits until one o'clock; the hangman may come (12 in 100).</summary>
    private static void WaitForTheRound(CardContext c)
    {
        c.WaitUntilHour(13);
        if (c.Random(100) <= 11)
        {
            ShowPicture(c, BeatenPicture);
            c.Screen.Text(0x10);
            c.Next = Execution;
            return;
        }
        c.Next = CardNumber;
    }

    private void DrinkThePotion(CardContext c)
    {
        c.PassHours(1);
        c.TakeItem(Who.Everybody, PotionUsed);
        c.AddItem(Who.Everybody, Rope);
        ShowPicture(c, CreepPicture);
        c.Screen.Text(0x18);
        FightTheGuards(c);
    }

    /// <summary>Out of the cell the guards stand in the way: won, broken through or fled → the chase; downed or surrendered → back inside, stripped and hurt.</summary>
    // orig: R047+0x12A2 (1852:1102) R047 seg 1852 matched
    private void FightTheGuards(CardContext c)
    {
        // C: arguments are evaluated right to left: the sergeants are drawn first, then the archers, then the guards.
        var sergeants = c.Random(3) + 1;
        var archers = c.Random(5) + 4;
        var guards = c.Random(3) + c.PartyLoreLevel() / 4 + 1;
        switch (c.RunBattle(8, c.Here + 0x6F, 3, guards, archers, 0, sergeants, 1, -1, 3, 0))
        {
            case 0 or 1 or 2:
                c.PassHours(2);
                ShowPicture(c, ChasePicture);
                c.Screen.Text(8);
                c.Next = Chase;
                break;
            case 3 or 4:
                c.ReportAway();
                c.PassHours(2);
                ShowPicture(c, CellPicture);
                c.Screen.Text(9);
                ReturnToTheCell(c);
                c.LoseItems(Who.Everybody);
                c.Injure(Who.Everybody, 0, 1, 20);
                c.Next = CardNumber;
                break;
            // DORMANT: other battle results (5: all down) leave the card as it is.
        }
    }
}
