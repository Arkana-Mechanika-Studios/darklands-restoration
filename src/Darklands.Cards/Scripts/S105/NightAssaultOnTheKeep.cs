using Darklands.Core.Characters;
using Darklands.Core.Economy;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Count02" / "$CountX2": by night outside a castle keep, the party decides how to get in:
/// bribe a guard, have the best climber take up a rope, all climb, pray, or blast the gate with
/// potions. Inside, a cruel lord (by the party's seed and the place) must be fought; a just one
/// stops the attack with a saint's word and has the party escorted away.
/// </summary>
// orig: R105+0x0000 (1838:0000) matched; tests: CardSmokeTests
// orig: R105+0x0E1C (1838:0E1C) matched (registration: custom item pop-ups R105+0x0EF6/0f1a/0f3e/0f70 give the potions' chances; saint pop-ups do not name the member)
public sealed class NightAssaultOnTheKeep : Card
{
    private const int KeepPicture = 67;

    // The best climber is remembered from one visit to the next, as in the original.
    // C: DS:64B0 is a DGROUP global: when nobody beats a score of 0 it keeps the climber of an earlier visit.
    private int _climber;

    public override int Number => 244;

    public override int Run(CardContext c)
    {
        // The MSG file is chosen by the place: odd places have the "$Count02" texts.
        c.Screen.Show(c.Here % 2 != 0 ? "$Count02" : "$CountX2", PictureNames.Of(KeepPicture));
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Set(OptionState.Enabled, 0, 1, 2, 3, 4, 7);
        if (c.IsDaylight)
        {
            c.PassHours(c.HoursUntil(18) + 2);
        }
        var bribe = Bribe(c);
        c.Variables.Money[0] = bribe;
        if (c.State.Party.Purse < bribe)
        {
            menu[0] = OptionState.Shown;
        }
        if (c.CountItems(Who.Everybody, 0x3B) == 0)
        {
            menu[1] = OptionState.Shown;
        }
        if (!c.PartyAllWell())
        {
            menu.Set(OptionState.Shown, 1, 2);
        }
        var climb = FindTheBestClimber(c);
        c.Variables.Chosen[0] = _climber;
        c.SetCardItems(0x63, 0x66, 0x67);
        if (LordIsCruel(c))
        {
            c.SetCardSaints(0x72, 0x59, 0x61, 0x87);
        }
        else
        {
            c.SetCardSaints(0x72);
        }
        menu[3] = c.SaintsUsable();
        menu[4] = c.ItemsUsable();
        menu.Popup = () => c.SaintPopupChanceHint();

        return menu.Run(
            // orig: R105+0x060A (line 0); hint R105+0x06F0 (1838:06F0) matched
            new MenuLine(p => BribeAGuard(c, p), () => BribeHint(c)),
            // orig: R105+0x074C (line 1); hint R105+0x0824 (1838:0824) matched (DS:64B2 / DS:64B0)
            new MenuLine(p => ClimbWithARope(c, menu, p), () => OptionHint.ChanceOf(climb, _climber)),
            // orig: R105+0x0836 (line 2); hint R105+0x0910 (1838:0910) matched
            new MenuLine(p => AllClimb(c, menu, p), () => WorstClimberHint(c)),
            // orig: R105+0x096C (line 3); hint R105+0x0AB0 (1838:0AB0) matched
            new MenuLine(_ => PrayForAWayIn(c), () => OptionHint.Say("Select a person and a saint.")),
            // orig: R105+0x0AE8 (line 4); hint R105+0x0C94 (1838:0C94) matched
            new MenuLine(p => UseAPotion(c, p), () => OptionHint.Say("Select a person and a potion."), () => PotionHint(c)),
            // DORMANT: lines 5, 6, 8 and 9 are never offered (opt_flag = 0); each lets no time pass (R105+0x0CCC/0d10/0d94/0dd8).
            new MenuLine(_ => c.PassHours(0), Assured),
            new MenuLine(_ => c.PassHours(0), Assured),
            // orig: R105+0x0D54 (1838:0D54) matched (leaves without clearing cur_loc)
            new MenuLine(_ => c.Next = CardContext.MapCard, Assured),
            new MenuLine(_ => c.PassHours(0), Assured),
            new MenuLine(_ => c.PassHours(0), Assured));
    }

    private static OptionHint Assured() => OptionHint.Say(SuccessAssured);

    private static bool LordIsCruel(CardContext c) => ((c.State.Party.Seed + (sbyte)c.Here) & 1) != 0;

    /// <summary>
    /// A hundredth of the purse in groschen, plus 12. The original passes the purse's pfennigs
    /// (a long) where a word is read, so only its low 16 bits count, clamped to 1..20000.
    /// </summary>
    private static Money Bribe(CardContext c)
    {
        // C: money_pfennigs returns a long; clamp_range (no prototype) reads its low word as the value.
        var pfennigs = Math.Clamp((int)(short)c.State.Party.Purse.TotalPfennigs, 1, 20000);
        return Money.From(0, pfennigs / 100 + 12, 0);
    }

    /// <summary>The best of (Stealth + Agility as burdened) / 2 among the members; returns that score.</summary>
    // C: the loop runs over all five slots whose status byte is non-zero (also the unconscious and those away).
    private int FindTheBestClimber(CardContext c)
    {
        var best = 0;
        for (var i = 0; i < Party.MaxMembers; i++)
        {
            if (c.Status(i) != CharacterStatus.Empty)
            {
                var score = (c.Skill(i, (int)Skill.Stealth) + c.EffectiveAgility(i)) / 2;
                if (best < score)
                {
                    _climber = i;
                    best = score;
                }
            }
        }
        return best;
    }

    private static OptionHint BribeHint(CardContext c)
    {
        var chance = c.Skill(Who.Leader, (int)Skill.Streetwise) + c.Attribute(Who.Leader, (int)Attribute.Charisma) + c.Skill(Who.Leader, (int)Skill.SpeakCommon) / 2;
        if (!LordIsCruel(c))
        {
            chance /= 3;
        }
        return OptionHint.ChanceOf(chance, c.Leader);
    }

    /// <summary>The weakest climber of all (twice Agility plus Stealth, halved) is named and sets the chance.</summary>
    // C: a side effect of the hint: the weakest climber becomes $ChosenOneName (the failure names that member).
    private static OptionHint WorstClimberHint(CardContext c)
    {
        var worst = 999;
        for (var i = 0; i < Party.MaxMembers; i++)
        {
            if (c.Status(i) != CharacterStatus.Empty)
            {
                var score = (c.EffectiveAgility(i) * 2 + c.Skill(i, (int)Skill.Stealth)) / 2;
                if (score < worst)
                {
                    c.Variables.Chosen[0] = i;
                    worst = score;
                }
            }
        }
        return OptionHint.ChanceOf(worst, OptionHint.WholeParty);
    }

    /// <summary>The potions' chances: acid and the bomb by Agility, the fire potion by throwing.</summary>
    // orig: R105+0x0EF6 (1838:0EF6) matched; R105+0x0F1A; R105+0x0F3E; R105+0x0F70 (item pop-ups by slot)
    // C: slot 1 is `eff_attr2 + 0xa << 1`: the shift binds looser than +, so (Agility + 10) * 2.
    private static OptionHint PotionHint(CardContext c)
    {
        var m = c.MenuMember;
        var chance = c.MenuSlot switch
        {
            0 => c.EffectiveAgility(m) * 2 + 15,
            1 => (c.EffectiveAgility(m) + 10) * 2,
            2 => c.Skill(m, (int)Skill.ThrownWeapons) + c.EffectiveAgility(m),
            _ => -1,
        };
        return chance == -1 ? OptionHint.Say(SuccessAssured, m) : OptionHint.ChanceOf(chance, m);
    }

    // orig: R105+0x060A (1838:060A) matched; tests: CardSmokeTests
    private static void BribeAGuard(CardContext c, int chance)
    {
        c.Variables.Chosen[0] = c.Leader;
        if (c.Random(100) < chance)
        {
            if (!LordIsCruel(c))
            {
                c.PassHours(1);
                ShowPicture(c, 236);
                c.Screen.Text(14);
                c.Next = CardContext.MapCard;
                return;
            }
            ShowPicture(c, 260);
            c.Screen.Text(1);
            StormTheKeep(c, 2);
            return;
        }
        ShowPicture(c, 145);
        c.Screen.Text(2);
        c.PassHours(c.HoursUntil(5));
        c.Next = CardContext.MapCard;
    }

    // orig: R105+0x074C (1838:074C) matched; tests: CardSmokeTests
    private void ClimbWithARope(CardContext c, CardMenu menu, int chance)
    {
        c.Variables.Chosen[0] = _climber;
        if (c.Random(100) <= chance)
        {
            ShowPicture(c, 32);
            c.Screen.Text(3);
            c.PracticeSkill(_climber, (int)Skill.Stealth, 1, 10, 0);
            c.PassHours(1);
            StormTheKeep(c, 0);
            return;
        }
        ShowPicture(c, 66);
        c.Screen.Text(5);
        c.Injure(_climber, 0, 15, 2);
        c.PracticeSkill(_climber, (int)Skill.Stealth, 0, 10, 0);
        c.PassHours(1);
        menu[1] = OptionState.Shown;
        c.Next = Number;
    }

    /// <summary>Everybody climbs; if it fails, the weakest climber (named by the hint) falls.</summary>
    // orig: R105+0x0836 (1838:0836) matched; tests: CardSmokeTests
    private void AllClimb(CardContext c, CardMenu menu, int chance)
    {
        if (c.Random(100) <= chance)
        {
            ShowPicture(c, 32);
            c.Screen.Text(4);
            c.PracticeSkill(Who.Everybody, (int)Skill.Stealth, 1, 10, 0);
            c.PassHours(1);
            StormTheKeep(c, 0);
            return;
        }
        ShowPicture(c, 66);
        c.Screen.Text(5);
        var fallen = c.Variables.Chosen[0];
        c.Injure(fallen, 0, 16, 4);
        c.PracticeSkill(fallen, (int)Skill.Stealth, 0, 5, 0);
        c.PassHours(1);
        menu[2] = OptionState.Shown;
        c.Next = Number;
    }

    /// <summary>St. Reinold or another saint lifts the party in; St. Zita sends a servant to open a postern.</summary>
    // orig: R105+0x096C (1838:096C) matched; tests: CardSmokeTests
    // A saint slot past 3 cannot occur (four saints); its switch has no default, so nothing would happen.
    private static void PrayForAWayIn(CardContext c)
    {
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (answer == -1)
        {
            return;
        }
        var named = c.Variables.Chosen[0];
        c.Variables.Chosen[0] = c.MenuMember;
        if (answer == 1)
        {
            switch (c.MenuSlot)
            {
                case 0:
                    ShowPicture(c, 32);
                    c.Screen.Text(6);
                    StormTheKeep(c, 0);
                    break;
                case 1:
                case 2:
                    ShowPicture(c, 29);
                    c.Screen.Text(8);
                    StormTheKeep(c, 0);
                    break;
                case 3:
                    c.PassHours(1);
                    ShowPicture(c, 275);
                    c.Screen.Text(7);
                    StormTheKeep(c, 1);
                    break;
            }
        }
        else
        {
            c.PassHours(1);
            ShowPicture(c, KeepPicture);
            c.Screen.Text(9);
        }
        c.Variables.Chosen[0] = named;
    }

    /// <summary>The potion is used up; it opens the gate, blasts it, or sets the stables afire — or the member stumbles.</summary>
    // orig: R105+0x0AE8 (1838:0AE8) matched; tests: CardSmokeTests
    // C: -(rand_mod(3) - -3) is drawn before -rand_mod(3) (two statements); a slot past 2 succeeds silently and stays.
    private static void UseAPotion(CardContext c, int chance)
    {
        var m = c.MenuMember;
        c.Variables.Chosen[0] = m;
        c.TakeItem(m, c.CardItems[c.MenuSlot]);
        if (c.Random(100) <= chance)
        {
            switch (c.MenuSlot)
            {
                case 0:
                    c.PassHours(1);
                    ShowPicture(c, 333);
                    c.Screen.Text(10);
                    StormTheKeep(c, 2);
                    break;
                case 1:
                    c.PassHours(1);
                    ShowPicture(c, 189);
                    c.Screen.Text(12);
                    StormTheKeep(c, 3);
                    break;
                case 2:
                    c.PassHours(1);
                    ShowPicture(c, 236);
                    c.Screen.Text(13);
                    StormTheKeep(c, 3);
                    break;
            }
            return;
        }
        c.AddAttribute(m, (int)Attribute.Endurance, -(c.Random(3) + 3));
        c.AddAttribute(m, (int)Attribute.Strength, -c.Random(3));
        if (c.Attribute(m, (int)Attribute.Strength) < 1)
        {
            c.SetAttribute(m, (int)Attribute.Strength, 1);
        }
        ShowPicture(c, 145);
        c.Screen.Text(11);
        c.PassHours(c.HoursUntil(5));
        c.Next = CardContext.MapCard;
    }

    /// <summary>
    /// Inside the keep. A just lord stops the attack (the party's name suffers here and in the
    /// nearest city). A cruel one is fought, the battle depending on how the party got in: won,
    /// the castle burns and the place changes for good; fled, those away rejoin; lost, the party
    /// is stripped and hanged from the walls till a stablehand helps. Then on to the map.
    /// </summary>
    // orig: R105+0x035E (1838:035E) matched; tests: CardSmokeTests
    private static void StormTheKeep(CardContext c, int wayIn)
    {
        // C: party_x is declared `extern long` in the original, so the call pushes party_y and party_x: nearest_city(party_x) receives the party's (x, y); nothing is undefined (checked against the asm).
        int NearestCity() => c.NearestCity(c.State.MapPosition.X, c.State.MapPosition.Y);

        if (!LordIsCruel(c))
        {
            ShowPicture(c, 236);
            c.Screen.Text(14);
            c.AdjustReputation(NearestCity(), -10, -20);
            c.AdjustReputation(c.Here, -10, -20);
        }
        else
        {
            // C: the switch is on a char argument; any value but 0..2 takes the default (0x7E), as way 3 does.
            // C: run_battle's arguments are evaluated right to left; the guard count is the only draw, made before the battle.
            var side = wayIn switch { 0 => 0x7B, 1 => 0x7C, 2 => 0x7D, _ => 0x7E };
            var guards = c.Random(5) + 3;
            switch (c.RunBattle(side, -2, 3, 2, guards, 0x24, 4, 1, -1, 0, 0))
            {
                case 0:
                    c.AdjustReputation(NearestCity(), 10, 20);
                    c.GiveReward(1);
                    ShowPicture(c, 31);
                    c.Screen.Text(15);
                    c.PracticeSkill(Who.Everybody, (int)Skill.Virtue, 1, 10, 1);
                    var castle = c.State.Locations[c.Here];
                    castle.Icon = 8;
                    castle.ArrivalCard = 0x85;
                    castle.EncounterCard = CardContext.ClosedCard;
                    break;
                case 1:
                case 2:
                    c.RejoinAway();
                    ShowPicture(c, 287);
                    c.Screen.Text(17);
                    c.PassHours(c.HoursUntil(5));
                    break;
                case 3:
                case 4:
                    c.LoseItems(Who.Everybody);
                    c.Injure(Who.Everybody, 1, 0x27, 5);
                    c.PartyMaxAttributeSum(1, 0);
                    c.Variables.Chosen[0] = c.BestMember;
                    c.PassHours(c.HoursUntil(5));
                    ShowPicture(c, 59);
                    c.Screen.Text(16);
                    c.PracticeSkill(Who.Everybody, (int)Skill.Virtue, 0, 10, 0);
                    break;
                default:
                    return;
            }
        }
        c.Next = CardContext.MapCard;
    }
}
