using Darklands.Core.Characters;
using Darklands.Core.Items;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$WildH00": the Wild Hunt rides at night. Unless the hunt quest has gone far enough the
/// hounds only pass by; otherwise the party can offer the Hunt a deer, outrun it, pray, fight,
/// or invoke the saint the quest named.
/// </summary>
// orig: R060+0x0978 (18CF:0008) matched; tests: CardSmokeTests
// orig: handler setup R060+0x1282 (opt_on[*]=0, screwed-up handler 09b4 for all 20 lines, item handlers
// 09ec/0a28/0a64/0aa0, saint handlers 0adc/0b66/0bf0/0c7a: standard pop-ups)
public sealed class WildHunt : Card
{
    private const int HuntPicture = 61;
    private const int HunterPicture = 130;
    private const int DeerPicture = 129;
    private const int RunningPicture = 286;
    private const int OverlandMap = 0x0C;

    /// <summary>The quest and fact group of the Wild Hunt.</summary>
    private const int HuntQuest = 7;

    /// <summary>The hint of "kill a deer" when the party has bows or crossbows (R136 seg 290E string).</summary>
    private const string DeathForThePrey = "This means death for the Hunt's prey";

    /// <summary>Bit of the item flags the hint tests (thrown weapons count only for the action).</summary>
    private const uint BowFlag = 0x20;

    public override int Number => 0x41;

    public override int Run(CardContext c)
    {
        // orig: R043+0x09AE(0x23) (play song)
        c.PlaySong(0x23);
        Show(c, HuntPicture);
        c.Next = Number;
        var menu = Menu(c, 1);
        menu.Enable(0, 1, 2, 3, 4);

        // The quest names the saint that banishes the Hunt and an item.
        // C: p = events[event_find(8, -1, -1, -1, DS:EE72 = 7, -1)]: the argument also stores 7 in DS:EE72 (text window state, not modelled);
        // p+0x2A is the saint, p+0x2C the item.
        // DIVERGENCE: when no quest event exists the original indexes events[-1]; here saint and item are 0.
        var quest = c.Events[c.Events.Find(Core.Events.EventKinds.Quest, -1, -1, -1, HuntQuest, -1)];
        var saint = quest?.Data1 ?? 0;
        var item = quest?.Data2 ?? 0;
        c.Variables.Texts[0] = c.SaintName(saint);
        c.Variables.Texts[1] = c.World.ItemName(item);
        if (!c.KnowsSaint(Who.Everybody, saint))
        {
            // C: member_bit(-2, saint) false: opt_flag_4 = 0.
            menu[4] = OptionState.Hidden;
        }
        c.SetCardSaints(0x2F, 0x39, 0x45);
        menu[2] = c.SaintsUsable();

        // C: the check comes after the options are prepared; fewer than 4 hunt facts and the hounds only pass by.
        if (HuntProgress(c) < 4)
        {
            c.Screen.Text(0);
            c.State.CurrentLocation = -1;
            return c.Next = OverlandMap;
        }

        return menu.Run(
            // orig: R060+0x0CE8 (line 0); handler R060+0x0E6E
            new MenuLine(chance => OfferADeer(c, chance), () => DeerChance(c)),
            // orig: R060+0x0ECC (line 1); handler R060+0x0FF4
            new MenuLine(chance => Outrun(c, chance), () => OutrunChance(c)),
            // orig: R060+0x1052 (line 2); handler R060+0x1186 (select saint); pop-up handlers 0adc..0c7a
            new MenuLine(_ => Pray(c), OptionHint.Saying(CardTexts.SelectSaint), c.SaintPopupHint),
            // orig: R060+0x11BE (18CF:084E) matched (line 3's action: calls the fight, 0244); hint R060+0x11C4 (enter combat)
            new MenuLine(_ => Fight(c), OptionHint.Saying(CardTexts.EnterCombat)),
            // orig: R060+0x11FC (line 4); handler R060+0x124A (select saint)
            new MenuLine(_ => BanishTheHunt(c), OptionHint.Saying(CardTexts.SelectSaint)),
            // DORMANT: lines 5..9 keep the "screwed up." handler of the setup function (09b4); opt_on[5..9] stay 0
            // (the loop copies only opt_flag_0..4) and the action switch has no case for them.
            // orig: R060+0x1324
            new MenuLine(_ => { }, OptionHint.Saying(CardTexts.ScrewedUp)),
            new MenuLine(_ => { }, OptionHint.Saying(CardTexts.ScrewedUp)),
            new MenuLine(_ => { }, OptionHint.Saying(CardTexts.ScrewedUp)),
            new MenuLine(_ => { }, OptionHint.Saying(CardTexts.ScrewedUp)),
            new MenuLine(_ => { }, OptionHint.Saying(CardTexts.ScrewedUp)));
    }

    private static int HuntProgress(CardContext c) => c.Events.FactValueAnywhere(HuntQuest, 5);

    // orig: R060+0x0E6E (18CF:04FE)
    private static OptionHint DeerChance(CardContext c)
    {
        // C: count_items_flags(-2, 0x20, 0): the flag word is passed as low and high halves; (max Woodwise << 1) / 3.
        if (c.CountItemsWithFlags(Who.Everybody, BowFlag) != 0)
        {
            return OptionHint.Say(DeathForThePrey);
        }
        var chance = (c.PartyMaxSkill((int)Skill.Woodwise) << 1) / 3;
        return new OptionHint(chance, "", c.BestMember);
    }

    /// <summary>
    /// The original handler leaves the chance unset when not everybody rides (a stack leftover);
    /// here it is the least agile member's effective Agility the handler looks up.
    /// </summary>
    // orig: R060+0x0FF4 (18CF:0684)
    // C: with everybody mounted: r.a = party_min_skill(Riding), r.b = (unsigned char)best_member, r.a += mount quality
    // (item_quality_flags(best_member, 0x2000, 0)), r.a = clamp99(r.a). Otherwise party_min_eff_attr2() is called and r.a is never written.
    // DIVERGENCE: the original returns the uninitialised r.a (a stack leftover) as the chance when not everybody rides;
    // here the effective Agility that party_min_eff_attr2() returns is used.
    // UNCERTAIN: what the stack slot of r.a really holds at that point (not checked against the disassembly).
    private static OptionHint OutrunChance(CardContext c)
    {
        if (c.PartyGet(5) != 0)
        {
            var chance = c.PartyMinSkill((int)Skill.Riding);
            var rider = c.BestMember;
            chance += c.ItemQualityByFlags(rider, ItemFlags.Mount);
            return new OptionHint(Math.Clamp(chance, 0, 99), "", rider);
        }
        var agility = c.PartyMinEffectiveAgility();
        return new OptionHint(agility, "", c.BestMember);
    }

    // orig: R060+0x0CE8 (18CF:0378)
    private void OfferADeer(CardContext c, int chance)
    {
        // C: v_si = rand_mod(100) is drawn first, then party_max_skill(Woodwise) is called for best_member only.
        var roll = c.Random(100);
        c.PartyMaxSkill((int)Skill.Woodwise);
        var hunter = c.BestMember;
        c.Variables.Chosen[0] = hunter;
        // C: count_items_flags(-2, 0x30, 0x800) = ItemFlags.MissileWeapon (0x0800_0030).
        if (c.CountItemsWithFlags(Who.Everybody, ItemFlags.MissileWeapon) == 0)
        {
            ShowPicture(c, 16);
            c.Screen.Text(2);
            // C: card_load_near(card_name, pic_buf) restores the main picture.
            ShowPicture(c, HuntPicture);
            c.Next = Number;
            return;
        }
        if (roll <= chance)
        {
            ShowPicture(c, DeerPicture);
            c.Screen.Text(3);
            c.WaitUntilHour(5);
            c.PracticeSkill(hunter, (int)Skill.Woodwise, 7, 10, 0);
            LeaveForTheMap(c);
            return;
        }
        // C: p_6 < v_si && party_max_skill(Woodwise) >= v_si (the skill is only fetched when the roll missed).
        if (chance < roll && c.PartyMaxSkill((int)Skill.Woodwise) >= roll)
        {
            ShowPicture(c, HunterPicture);
            c.Screen.Text(4);
            c.PracticeSkill(hunter, (int)Skill.Woodwise, 7, 10, 0);
        }
        else
        {
            ShowPicture(c, DeerPicture);
            c.Screen.Text(5);
            c.PracticeSkill(hunter, (int)Skill.Woodwise, 0, 10, 0);
        }
        Fight(c);
    }

    // orig: R060+0x0ECC (18CF:055C)
    private static void Outrun(CardContext c, int chance)
    {
        if (c.Succeeds(chance))
        {
            c.WaitUntilHour(5);
            if (c.PartyGet(5) != 0)
            {
                c.PartyMinSkill((int)Skill.Riding);
                c.Variables.Chosen[0] = c.BestMember;
                ShowPicture(c, RunningPicture);
                c.Screen.Text(6);
            }
            else
            {
                c.PartyMinEffectiveAgility();
                c.Variables.Chosen[0] = c.BestMember;
                ShowPicture(c, 287);
                // C: add_attr(-2, 0, rand_mod(10) + 5) comes between the picture and the text.
                c.AddAttribute(Who.Everybody, (int)Attribute.Endurance, c.Random(10) + 5);
                c.Screen.Text(7);
            }
            LeaveForTheMap(c);
            return;
        }
        // C: -(rand_mod(6) - -2) = -(rand_mod(6) + 2).
        c.AddAttribute(Who.Everybody, (int)Attribute.Endurance, -(c.Random(6) + 2));
        ShowPicture(c, 131);
        c.Screen.Text(8);
        Fight(c);
    }

    // orig: R060+0x1052 (18CF:06E2); -1 from the saint pop-up (cancelled) changes nothing
    private void Pray(CardContext c)
    {
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (answer == -1)
        {
            return;
        }
        if (answer != 0)
        {
            if (c.MenuSlot == 1)
            {
                // Saint Gertrude's mists carry the party to the nearest village.
                // C: nearest_loc_type(party_x, 8) takes the party's map position (x, y) and the village icon 8.
                ShowPicture(c, 27);
                c.Screen.Text(9);
                c.State.CurrentLocation = c.NearestLocationOfType(c.State.MapPosition.X, c.State.MapPosition.Y, 8);
                c.Next = 0x85;
                return;
            }
            // C: switch (menu_slot) { case 0: 0x2f; default: 0x45 } (slot 1 left above, so slot 2 and any other take 0x45).
            c.NameToSlot(1, c.MenuSlot == 0 ? 0x2F : 0x45, 2);
            c.WaitUntilHour(5);
            ShowPicture(c, HuntPicture);
            c.Screen.Text(10);
            LeaveForTheMap(c);
            return;
        }
        ShowPicture(c, RunningPicture);
        c.Screen.Text(11);
        ShowPicture(c, HuntPicture);
        c.Next = Number;
    }

    /// <summary>Invoking the quest's saint ends the Hunt for good.</summary>
    // orig: R060+0x11FC (18CF:088C)
    private static void BanishTheHunt(CardContext c)
    {
        ShowPicture(c, HunterPicture);
        c.Screen.Text(14);
        c.Events.ClearFacts(HuntQuest, -1);
        LeaveForTheMap(c);
    }

    // orig: R060+0x0BB4 (18CF:0244); a quest-progress of 4 or more summons the Hunter himself
    private static void Fight(CardContext c)
    {
        var result = HuntProgress(c) < 4
            ? c.RunBattle(0x2E, -1, 0x3D, 1, c.Random(5) + 3, -1, 0, 0, -1, 0, 0)
            : c.RunBattle(0x2E, -1, 0x3C, 5, 1, -1, 0, 0, -1, 0, 0);
        switch (result)
        {
            case 0:
            case 1:
            case 2:
                c.DropAway();
                ShowPicture(c, RunningPicture);
                c.Screen.Text(12);
                break;
            default:
                TornToPieces(c);
                break;
        }
        LeaveForTheMap(c);
    }

    /// <summary>After a defeat the Hunter has one member (able or unconscious) torn apart by his hounds.</summary>
    private static void TornToPieces(CardContext c)
    {
        var victim = c.Random(c.PartySize);
        var status = c.Status(victim);
        // C: the original steps through party[v]+0x00 (1 = able, 2 = unconscious) until it finds one, wrapping at party_size.
        // DIVERGENCE: a party with nobody able or unconscious would hang the original; the loop here ends after 5 * MaxMembers steps.
        for (var tries = 0; status != CharacterStatus.Able && tries < 5 * Party.MaxMembers; tries++)
        {
            if (status == CharacterStatus.Unconscious)
            {
                break;
            }
            if (++victim >= c.PartySize)
            {
                victim = 0;
            }
            status = c.Status(victim);
        }
        // C: party[victim]+0x00 = 0 (the member simply vanishes; nothing else is cleaned up).
        c.SetStatus(victim, CharacterStatus.Empty);
        c.Variables.Chosen[0] = victim;
        ShowPicture(c, HunterPicture);
        c.Screen.Text(13);
        c.Events.ClearFacts(HuntQuest, -1);
    }

    // C: cur_loc = -1; next_card = 0xC at the end of every path that leaves the card.
    private static void LeaveForTheMap(CardContext c)
    {
        c.State.CurrentLocation = -1;
        c.Next = OverlandMap;
    }
}
