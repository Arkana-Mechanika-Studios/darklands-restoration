using Darklands.Core.Characters;
using Darklands.Core.Economy;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$MeetV00": a rich bishop with a mounted escort demands a "tithe" for the protected road.
/// The party can pay, pray, plead its virtue, refuse, ride off, or throw a potion and escape.
/// Fighting the Church's men costs Virtue and Divine Favour, whatever the outcome. A party with
/// almost no money is let pass.
/// </summary>
// orig: R110+0x0000 (1838:0000) matched; tests: CardSmokeTests
// orig: R110+0x0B0A (1838:0B0A) matched (registration: item pop-ups R110+0x0BB4/0c14 name the member; saint pop-ups do not)
// DORMANT: R110+0x0C76, R110+0x0C78, R110+0x0D8A, R110+0x0D8C are never called.
public sealed class BishopsRoadTithe : Card
{
    private const int BishopPicture = 244;
    private const int ClerkPicture = 243;
    private const int GuardsPicture = 33;

    public override int Number => 264;

    public override int Run(CardContext c)
    {
        Show(c, BishopPicture);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Set(OptionState.Enabled, 0, 1, 2, 3, 4, 5);
        c.State.SavedCard2 = 0;
        c.Variables.Chosen[0] = c.Leader;
        var purse = c.State.Party.Purse.TotalPfennigs;
        if (purse <= 10)
        {
            c.Screen.Text(16);
            c.State.CurrentLocation = Core.GameState.Wilderness;
            // The original returns 0x0C without changing next_card.
            return CardContext.MapCard;
        }
        // C: (unsigned) of the long pfennig counts keeps their low 16 bits, unsigned, before dividing.
        var tithe = Money.From(0, 0, Math.Clamp((ushort)purse / 50 + (ushort)c.State.Party.BankNotes.TotalPfennigs / 100, 1, 9999));
        if (purse / 2 <= tithe.TotalPfennigs)
        {
            tithe = Money.From(0, 0, (ushort)purse / 2);
        }
        c.Variables.Money[0] = tithe;
        c.SetCardItems(0x65, 0x66);
        c.SetCardSaints(0x3D, 0x4E);
        menu[5] = c.ItemsUsable();
        menu[1] = c.SaintsUsable();
        menu.Popup = () => c.SaintPopupChanceHint();

        // C: after every choice the loop sets var_chosen1 = leader before the handler; a pop-up
        // handler then names its own member. Each action does this first for line choices.
        return menu.Run(
            // orig: R110+0x052E (1838:052E) matched; hint R110+0x057E
            new MenuLine(_ => { LeaderSpeaks(c); PayTheTithe(c, tithe); }, () => OptionHint.Say(SuccessAssured)),
            // orig: R110+0x05B6 (line 1); hint R110+0x0608 (1838:0608) matched
            new MenuLine(_ => { LeaderSpeaks(c); PrayForAid(c, menu); }, () => OptionHint.Say("Select a person and a saint.")),
            // orig: R110+0x0640 (line 2); hint R110+0x0718 (1838:0718) matched
            new MenuLine(p => { LeaderSpeaks(c); PleadYourVirtue(c, menu, p); }, () => OptionHint.ChanceOf(Math.Clamp(
                c.PartyAverageSkill((int)Skill.Virtue) + c.Skill(Who.Leader, (int)Skill.SpeakCommon) / 2, 0, 100), c.Leader)),
            // orig: R110+0x075C (1838:075C) matched; hint R110+0x07B6
            new MenuLine(_ => { LeaderSpeaks(c); Refuse(c, menu); }, Unknown),
            // orig: R110+0x07EE (line 4); hint R110+0x08FC (1838:08FC) matched
            new MenuLine(p => { LeaderSpeaks(c); RideOff(c, menu, p); }, () => EscapeHint(c)),
            // orig: R110+0x0948 (line 5); hint R110+0x0A04 (1838:0A04) matched
            new MenuLine(p => { LeaderSpeaks(c); ThrowAPotion(c, menu, p); }, () => OptionHint.Say("Select a person and a potion."), () => PotionHint(c)),
            // orig: R110+0x0A3C (1838:0A3C) matched; hint R110+0x0A8C
            new MenuLine(_ => { LeaderSpeaks(c); Submit(c); }, () => OptionHint.Say(SuccessAssured)),
            // orig: R110+0x0AC4 (1838:0AC4) matched; hint R110+0x0ACA
            new MenuLine(_ => { LeaderSpeaks(c); FightTheGuard(c); }, () => OptionHint.Say("Enter Combat")),
            // DORMANT: lines 8 and 9 are never offered; actions R110+0x0B02/0b06 and hints R110+0x0B04/0b08
            // C: are empty; the hints return AX/DX; neutral hints are used.
            new MenuLine(_ => LeaderSpeaks(c)),
            new MenuLine(_ => LeaderSpeaks(c)));
    }

    private static OptionHint Unknown() => OptionHint.Say("You can always encounter the unknown");

    /// <summary>The loop's var_chosen1 = leader, unless a pop-up named its member (see Run).</summary>
    private static void LeaderSpeaks(CardContext c)
    {
        if (!c.MenuPopup)
        {
            c.Variables.Chosen[0] = c.Leader;
        }
    }

    /// <summary>Throwing the potion: sure for a mounted party, else by the least nimble rider and the potion's quality.</summary>
    // orig: R110+0x0BB4 (1838:0BB4) matched; slot 1 R110+0x0C14
    private static OptionHint PotionHint(CardContext c)
    {
        var m = c.MenuMember;
        c.Variables.Chosen[0] = m;
        int chance;
        if (c.PartyGet(5) != 0)
        {
            chance = c.MenuSlot == 0 ? 99 : 95;
        }
        else if (c.MenuSlot == 0)
        {
            chance = Math.Clamp(c.PartyMinMountedAgility() + c.ItemQuality(m, c.CardItems[c.MenuSlot]) + 1, 1, 99);
        }
        else
        {
            chance = Math.Clamp(c.PartyMinMountedAgility() + c.ItemQuality(m, c.CardItems[c.MenuSlot]) + 10, 10, 95);
        }
        return OptionHint.ChanceOf(chance, m);
    }

    // C: party_min_eff_attr2_bonus() is evaluated before member_skill(best_member, …) (checked against the asm),
    // so the Riding is that of the least nimble rider it found.
    private static OptionHint EscapeHint(CardContext c)
    {
        var chance = c.PartyMinMountedAgility() + c.Skill(c.BestMember, (int)Skill.Riding) / 2;
        if (c.PartyGet(5) == 0)
        {
            chance = 0;
        }
        return OptionHint.ChanceOf(Math.Clamp(chance, 0, 99), OptionHint.WholeParty);
    }

    // orig: R110+0x052E (1838:052E) matched; tests: CardSmokeTests
    private void PayTheTithe(CardContext c, Money tithe)
    {
        c.State.Party.Purse = c.State.Party.Purse.Subtract(tithe);
        Show(c, ClerkPicture);
        c.Screen.Text(1);
        c.LeaveForTheMap();
    }

    // orig: R110+0x05B6 (1838:05B6) matched; tests: CardSmokeTests
    private static void PrayForAid(CardContext c, CardMenu menu)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (answer == -1)
        {
            return;
        }
        if (answer == 1)
        {
            c.Screen.Text(2);
            c.LeaveForTheMap();
            return;
        }
        menu[1] = OptionState.Shown;
        c.Next = 264;
    }

    // orig: R110+0x0640 (1838:0640) matched; tests: CardSmokeTests
    private void PleadYourVirtue(CardContext c, CardMenu menu, int chance)
    {
        Show(c, 358);
        if (c.Random(100) <= chance)
        {
            ShowPicture(c, ClerkPicture);
            c.Screen.Text(13);
            c.PracticeSkill(c.Leader, (int)Skill.SpeakCommon, 7, 10, 0);
            c.LeaveForTheMap();
            return;
        }
        menu[2] = OptionState.Shown;
        ShowPicture(c, ClerkPicture);
        c.Screen.Text(14);
        c.Next = 264;
        Show(c, BishopPicture);
    }

    /// <summary>Submit to the guard or fight (the lines 6 and 7 of <paramref name="block"/>).</summary>
    private static void TheGuardsDrawWeapons(CardContext c, CardMenu menu)
    {
        menu.Set(OptionState.Enabled, 6, 7);
        menu.Set(OptionState.Hidden, 0, 1, 2, 3, 4, 5, 8, 9);
    }

    // orig: R110+0x075C (1838:075C) matched; tests: CardSmokeTests
    private void Refuse(CardContext c, CardMenu menu)
    {
        TheGuardsDrawWeapons(c, menu);
        Show(c, GuardsPicture);
        menu.Block = 4;
        c.Next = 264;
    }

    /// <summary>Without mounts there is no chance at all; otherwise the riders may outrun the guard.</summary>
    // orig: R110+0x07EE (1838:07EE) matched; tests: CardSmokeTests
    private void RideOff(CardContext c, CardMenu menu, int chance)
    {
        TheGuardsDrawWeapons(c, menu);
        if (chance == 0)
        {
            Show(c, GuardsPicture);
            menu.Block = 7;
        }
        else
        {
            if (c.Random(100) <= chance)
            {
                Show(c, 287);
                c.Screen.Text(5);
                c.PracticeSkill(Who.Everybody, (int)Skill.Riding, 1, 10, 0);
                c.LeaveForTheMap();
                return;
            }
            Show(c, 286);
            menu.Block = 6;
            c.PracticeSkill(Who.Everybody, (int)Skill.Riding, 0, 10, 0);
        }
        c.Next = 264;
    }

    // orig: R110+0x0948 (1838:0948) matched; tests: CardSmokeTests
    private void ThrowAPotion(CardContext c, CardMenu menu, int chance)
    {
        c.TakeItem(c.MenuMember, c.CardItems[c.MenuSlot]);
        if (c.Random(100) <= chance)
        {
            Show(c, 189);
            c.Screen.Text(9);
            c.LeaveForTheMap();
            return;
        }
        TheGuardsDrawWeapons(c, menu);
        Show(c, GuardsPicture);
        menu.Block = 10;
        c.Next = 264;
    }

    /// <summary>The guards take money and weapons (the original leaves for the map without clearing cur_loc).</summary>
    // orig: R110+0x0A3C (1838:0A3C) matched; tests: CardSmokeTests
    private void Submit(CardContext c)
    {
        c.LoseItems(Who.Everybody);
        Show(c, 177);
        c.Screen.Text(8);
        c.PassHours(2);
        c.Next = CardContext.MapCard;
    }

    /// <summary>
    /// The bishop's guard. Won, the bishop escapes and the party's name suffers; fled, those
    /// away come back robbed; beaten, robbed. Every member then loses 2 Virtue and 25 Divine Favour.
    /// </summary>
    // orig: R110+0x031A (1838:031A) matched; tests: CardSmokeTests
    // C: arguments right to left: rand_mod(2) + 4 is drawn before rand_mod(3); then run_battle draws its seed (-1).
    private void FightTheGuard(CardContext c)
    {
        var lore = c.PartyLoreLevel();
        var count = c.Random(2) + 4;
        var groups = c.Random(3) + lore / 4 + 2;
        switch (c.RunBattle(0x2E, -1, 3, groups, count, 0x21, lore % 3 + 2, lore > 6 ? 3 : 2, -1, 0, 0))
        {
            case 0:
                c.PassHours(1);
                c.AdjustReputation(c.NearestCity(c.State.MapPosition.X, c.State.MapPosition.Y), -10, -20);
                Show(c, 286);
                c.Screen.Text(11);
                c.LeaveForTheMap();
                break;
            case 1:
            case 2:
                for (var i = 0; i < c.PartySize; i++)
                {
                    if (c.Status(i) == CharacterStatus.Away)
                    {
                        c.SetStatus(i, CharacterStatus.Able);
                        c.LoseItems(i);
                    }
                }
                Show(c, 61);
                c.Screen.Text(15);
                c.LeaveForTheMap();
                break;
            case 3:
                c.PassHours(c.Random(3) + 3);
                c.LoseItems(Who.Everybody);
                Show(c, 145);
                c.Screen.Text(12);
                c.LeaveForTheMap();
                break;
            case 4:
                c.PassHours(1);
                c.LoseItems(Who.Everybody);
                Show(c, 177);
                c.Screen.Text(8);
                c.LeaveForTheMap();
                break;
        }
        for (var i = 0; i < c.PartySize; i++)
        {
            if (c.Status(i) != CharacterStatus.Empty)
            {
                c.AddSkill(i, (int)Skill.Virtue, -2);
                c.AddAttribute(i, (int)Attribute.DivineFavor, -25);
            }
        }
    }
}
