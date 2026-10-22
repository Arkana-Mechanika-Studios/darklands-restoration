using Darklands.Core.Characters;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$MeetT00": something lurks ahead — a pack of tatzelwurms. The best scout may sense it;
/// then the party can press on, bluff, use a potion, pray, surrender, sneak around or scout
/// it out. Otherwise the wurms strike, and the party can throw a potion, pray, fight or ride
/// for it.
/// </summary>
// orig: R112+0x049E (1881:000E) matched; tests: CardSmokeTests
// orig: R112+0x12BE (1881:0E2E) matched (registration: item pop-ups R112+0x1398/0f58 name the member, slots 2..3 empty; saint pop-ups do not name the member)
public sealed class TatzelwurmPack : Card
{
    private const int AttackPicture = 358;
    private const int RoadPicture = 286;
    private const int WurmPicture = 331;

    // C: the card's picture buffer (pic_buf), changed by each full reload of the card.
    private int _cardPicture;

    public override int Number => 273;

    public override int Run(CardContext c)
    {
        c.Next = Number;
        var menu = Menu(c, 0);
        c.PartyMaxAttributePlusSkill((int)Attribute.Perception, (int)Skill.Woodwise);
        var scout = c.BestMember;
        c.Variables.Chosen[0] = scout;
        var watchfulness = c.Attribute(scout, (int)Attribute.Perception) + c.Random(10) + c.Skill(scout, (int)Skill.Woodwise) / 2;
        // C: the roll is made before the event test, every time.
        if (c.Random(100) > watchfulness && !c.Events.Started(0x4F))
        {
            menu.Set(OptionState.Enabled, 2, 3, 6, 8);
            c.PartyMinMountedAgility();
            c.Variables.Chosen[0] = c.BestMember;
            Reload(c, AttackPicture);
            menu.Block = 1;
        }
        else
        {
            Reload(c, RoadPicture);
            menu.Set(OptionState.Enabled, 0, 1, 2, 3, 4, 5, 7);
            menu.Block = 0;
        }
        c.SetCardItems(0x65, 0x66);
        menu[2] = c.ItemsUsable();
        c.SetCardSaints(0x36, 0x3D, 0x83, 3);
        menu[3] = c.SaintsUsable();
        menu.Popup = () => c.SaintPopupChanceHint();

        // DORMANT: in text block 2 the option lines 0, 1, 4, 7 are never offered (the switch to this block hides them); they can be offered in other blocks only.
        return menu.Run(
            // orig: R112+0x093C (1881:04AC) matched; hint R112+0x09C2
            new MenuLine(_ => { TheWurmsStrike(c, menu, OptionState.Enabled); c.Next = 273; }, Unknown),
            // orig: R112+0x09FA (1881:056A) matched; hint R112+0x0A80
            new MenuLine(_ => { TheWurmsStrike(c, menu, OptionState.Enabled); c.Next = 273; }, Unknown),
            // orig: R112+0x0AB8 (line 2); hint R112+0x0BC4 (1881:0734) matched
            new MenuLine(p => ThrowAPotion(c, menu, p), () => OptionHint.Say("Select a person and a potion."), () => PotionHint(c)),
            // orig: R112+0x0BFC (line 3); hint R112+0x0D12 (1881:0882) matched
            new MenuLine(_ => PrayForDeliverance(c, menu), () => OptionHint.Say("Select a person and a saint.")),
            // orig: R112+0x0D4A (1881:08BA) matched; hint R112+0x0DD4
            new MenuLine(_ => { TheWurmsStrike(c, menu, OptionState.Shown); c.Next = 273; }, () => OptionHint.Say(SuccessAssured)),
            // orig: R112+0x0E0C (line 5); hint R112+0x0F20 (1881:0A90) matched
            new MenuLine(p => SneakAround(c, menu, p), Unknown),
            // orig: R112+0x0F58 (1881:0AC8) matched; hint R112+0x0F64
            // C: the battle kind is chosen by options_block (non-zero: the ambush kind).
            new MenuLine(_ => FightTheWurms(c, menu.Block != 0), () => OptionHint.Say("Enter Combat")),
            // orig: R112+0x0F9C (line 7); hint R112+0x10B4 (1881:0C24) matched
            new MenuLine(p => ScoutItOut(c, menu, p), () => OptionHint.ChanceOf(Math.Clamp(
                c.PartyAverageSkill((int)Skill.Woodwise) + c.PartyAverageEffectiveAgility() * 2 / 3, 1, 89), OptionHint.WholeParty)),
            // orig: R112+0x10EC (line 8); hint R112+0x1278 (1881:0DE8) matched
            new MenuLine(p => RideForIt(c, p), () => RideHint(c)),
            // orig: R112+0x12BA (1881:0E2A) matched (empty); hint R112+0x12BC (empty)
            // DORMANT: line 9 is never offered. UNCERTAIN: its hint returns AX/DX; a neutral hint is used.
            new MenuLine(_ => { }));
    }

    private static OptionHint Unknown() => OptionHint.Say("You can always encounter the unknown");

    private void Reload(CardContext c, int picture)
    {
        _cardPicture = picture;
        Show(c, picture);
    }

    /// <summary>Hurling a potion: Perception plus half of Woodwise, plus 13 (slot 0) or 21 (slot 1).</summary>
    // orig: R112+0x1398 (1881:0F08) matched; slot 1 R112+0x13E8
    // C: slots 2 and 3 have empty pop-up handlers (R112+0x1438/0faa); the card has two potions, so they never show.
    private static OptionHint PotionHint(CardContext c)
    {
        var m = c.MenuMember;
        c.Variables.Chosen[0] = m;
        var aim = c.Attribute(m, (int)Attribute.Perception) + c.Skill(m, (int)Skill.Woodwise) / 2;
        return OptionHint.ChanceOf(Math.Clamp(aim + (c.MenuSlot == 0 ? 13 : 21), 1, 99), m);
    }

    // C: party_min_skill(0x11) is only called for a mounted party.
    private static OptionHint RideHint(CardContext c)
    {
        var chance = c.PartyGet(5) != 0 ? Math.Clamp(c.PartyMinSkill((int)Skill.Riding) * 2, 25, 99) : 0;
        return OptionHint.ChanceOf(chance, OptionHint.WholeParty);
    }

    /// <summary>The wurms are upon the party: potion (or not, <paramref name="potionLine"/>), prayer, fight or flight.</summary>
    private void TheWurmsStrike(CardContext c, CardMenu menu, OptionState potionLine)
    {
        menu[2] = potionLine;
        menu.Set(OptionState.Enabled, 3, 6, 8);
        menu.Set(OptionState.Hidden, 0, 1, 4, 5, 7, 9);
        c.SetCardSaints(0x4A, 0x67, 0x6B, 0x79);
        menu[3] = c.SaintsUsable();
        c.PartyMinMountedAgility();
        c.Variables.Chosen[0] = c.BestMember;
        Reload(c, AttackPicture);
        menu.Block = 1;
    }

    // orig: R112+0x0AB8 (1881:0628) matched; tests: CardSmokeTests
    private void ThrowAPotion(CardContext c, CardMenu menu, int chance)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        c.TakeItem(c.MenuMember, c.CardItems[c.MenuSlot]);
        if (c.Random(100) <= chance)
        {
            Reload(c, 189);
            c.Screen.Text(3);
            c.LeaveForTheMap();
            return;
        }
        if (menu.Block == 1)
        {
            c.Screen.Text(15);
            FightTheWurms(c, true);
            return;
        }
        TheWurmsStrike(c, menu, OptionState.Shown);
        c.Next = 273;
    }

    /// <summary>The prayer. The original also takes an item from the member: card_items[menu_slot], the potion in the saint's slot.</summary>
    // orig: R112+0x0BFC (1881:076C) matched; tests: CardSmokeTests
    // C: take_item(menu_member, card_items[menu_slot]) runs before the prayer, even if the prayer is cancelled.
    private void PrayForDeliverance(CardContext c, CardMenu menu)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        c.TakeItem(c.MenuMember, c.CardItems[c.MenuSlot]);
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (answer == -1)
        {
            return;
        }
        if (answer == 1)
        {
            Reload(c, 289);
            c.Screen.Text(4);
            c.LeaveForTheMap();
            return;
        }
        if (menu.Block == 1)
        {
            Reload(c, 16);
            c.Screen.Text(5);
            FightTheWurms(c, true);
            return;
        }
        // C: this card_load uses the second picture buffer, so pic_buf (the card's picture) is not changed.
        Show(c, 16);
        c.Screen.Text(5);
        Show(c, _cardPicture);
        c.Next = 273;
    }

    // orig: R112+0x0E0C (1881:097C) matched; tests: CardSmokeTests
    private void SneakAround(CardContext c, CardMenu menu, int chance)
    {
        if (c.Random(100) <= chance)
        {
            c.PassHours(c.HoursUntil(18));
            Reload(c, 61);
            c.Screen.Text(6);
            c.PracticeSkill(Who.Everybody, (int)Skill.Woodwise, 7, 10, 0);
            c.LeaveForTheMap();
            return;
        }
        c.PassHours(1);
        TheWurmsStrike(c, menu, OptionState.Shown);
        c.PracticeSkill(Who.Everybody, (int)Skill.Woodwise, 0, 10, 0);
        c.Next = 273;
    }

    /// <summary>Scouting shows the wurms (potion, prayer, sneaking, fighting or flight); failing, they strike.</summary>
    // orig: R112+0x0F9C (1881:0B0C) matched; tests: CardSmokeTests
    private void ScoutItOut(CardContext c, CardMenu menu, int chance)
    {
        if (c.Random(100) <= chance)
        {
            c.PracticeSkill(Who.Everybody, (int)Skill.Woodwise, 7, 10, 0);
            c.PartyMaxSkill((int)Skill.Woodwise);
            c.Variables.Chosen[0] = c.BestMember;
            Reload(c, WurmPicture);
            menu.Block = 2;
            menu.Set(OptionState.Enabled, 2, 3, 5, 6, 8);
            menu.Set(OptionState.Hidden, 0, 1, 4, 7, 9);
            return;
        }
        TheWurmsStrike(c, menu, OptionState.Shown);
        c.PracticeSkill(Who.Everybody, (int)Skill.Woodwise, 0, 10, 0);
    }

    /// <summary>
    /// Riding away: without mounts the wurms catch the party at once; a good ride escapes; a bad
    /// one throws the worst rider (and, on a low roll, the mounts bolt and are lost, but the wurms follow them).
    /// </summary>
    // orig: R112+0x10EC (1881:0C5C) matched; tests: CardSmokeTests
    // C: the roll is drawn first, even when the chance is 0; card_text(7, p_6) passes the chance (0) as the frame.
    private void RideForIt(CardContext c, int chance)
    {
        var roll = c.Random(100);
        if (chance == 0)
        {
            Reload(c, AttackPicture);
            c.Screen.Text(7);
        }
        else if (chance >= roll)
        {
            Reload(c, 356);
            c.Screen.Text(9);
            c.PracticeSkill(Who.Everybody, (int)Skill.Riding, 1, 10, 0);
            c.LeaveForTheMap();
            return;
        }
        else
        {
            c.PartyMinSkill((int)Skill.Riding);
            c.Variables.Chosen[0] = c.BestMember;
            c.AddAttribute(c.Variables.Chosen[0], (int)Attribute.Endurance, -4);
            c.AddAttribute(c.Variables.Chosen[0], (int)Attribute.Strength, -2);
            if (roll <= 50)
            {
                c.RemoveItemsWithFlags(Who.Everybody, 0x2000);
                c.PassHours(2);
                Reload(c, AttackPicture);
                c.Screen.Text(8);
                c.PracticeSkill(Who.Everybody, (int)Skill.Riding, 0, 10, 0);
                c.LeaveForTheMap();
                return;
            }
            Reload(c, AttackPicture);
            c.Screen.Text(13);
            c.PracticeSkill(Who.Everybody, (int)Skill.Riding, 0, 10, 0);
        }
        FightTheWurms(c, true);
    }

    /// <summary>The wurms: won, fame and goodwill; fled, those away are lost; beaten, the weakest member is torn apart.</summary>
    // orig: R112+0x07B2 (1881:0322) matched; tests: CardSmokeTests
    // C: party_x is declared `extern long` in the original, so the call pushes party_y and party_x: nearest_city(party_x) receives the party's (x, y); nothing is undefined (checked against the asm).
    private void FightTheWurms(CardContext c, bool ambushed)
    {
        switch (c.RunBattle(ambushed ? 0x2E : 0x2F, -1, 0x3B, 1, c.PartyLoreLevel() / 3 + 1, -1, 0, 0, -1, 0, 0))
        {
            case 0:
                Reload(c, WurmPicture);
                c.Screen.Text(11);
                c.PassHours(1);
                c.AdjustReputation(c.NearestCity(c.State.MapPosition.X, c.State.MapPosition.Y), 1, 1);
                c.GiveReward(1);
                c.LeaveForTheMap();
                break;
            case 1:
            case 2:
                c.DropAway();
                Reload(c, 61);
                c.Screen.Text(6);
                c.PassHours(c.HoursUntil(5));
                c.LeaveForTheMap();
                break;
            case 3:
            case 4:
                c.PartyMinAttribute((int)Attribute.Strength);
                var victim = c.BestMember;
                c.Variables.Chosen[0] = victim;
                Reload(c, AttackPicture);
                c.Screen.Text(10);
                c.DeleteMember(victim);
                c.SetStatus(victim, CharacterStatus.Empty);
                c.PassHours(c.Random(3) + 3);
                c.LeaveForTheMap();
                break;
        }
    }
}
