using Darklands.Core.Characters;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Insid00": by night upstairs in a merchants' house, before four locked offices (the
/// leading merchant's, the Fuggers', the Medici's, the Hanseatic League's). The party picks a
/// door and opens it by Artifice, an Eater-water potion or a Thunderbolt potion. An item quest
/// (tag 10) hides its item in one of the offices; one office of each house is trapped; guards
/// may come.
/// </summary>
// orig: R108+0x0ED0 (1925:0000) matched; tests: CardSmokeTests
// orig: R108+0x203C (1925:116C) matched (registration: an empty function, so lines 10..19 keep an earlier card's handlers; all hidden)
// DORMANT: R108+0x1152 and R108+0x1154 (empty) and R108+0x203E (a "screwed up." handler) are never called.
public sealed class MerchantHouseOffices : Card
{
    private const int HallPicture = 29;
    private const int DoorPicture = 353;
    private const int StreetPicture = 34;
    private const int TrappedPicture = 333;

    private const int EaterWater = 0x63;
    private const int Thunderbolt = 0x66;

    /// <summary>A failed lock-picking at an office (kind 0x55, by office) and a failed Eater-water (kind 0x56).</summary>
    private const int LockFailed = 0x55;
    private const int AcidFailed = 0x56;

    // C: the chosen office lives in a DGROUP word (DS:EEB4, right after $Money4) that only this
    // card uses; it keeps its value between visits.
    // UNCERTAIN: its value before the first choice is taken as 0.
    private int _office;

    // C: pic_buf, the card's picture: the hall, or an office's door once the party stands before one
    // (the leading merchant's office keeps the hall). After a failed attempt the card shows it again.
    private int _picture = HallPicture;

    public override int Number => 257;

    public override int Run(CardContext c)
    {
        _picture = HallPicture;
        Show(c, _picture);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Set(OptionState.Enabled, 3, 4, 5, 6, 9);
        if (c.Events.QuestAt(10, c.Here))
        {
            // C: event_find does not set last_event; event_f2e_of(last_event) reads the stale global.
            _ = c.Events.Find(8, -1, c.Here, -1, 10, -1);
            c.Variables.Item = c.EventDetail(c.LastEvent);
            c.NameToSlot(0, c.EventDetail(c.LastEvent), 0);
        }
        c.PartyMaxSkill((int)Skill.Artifice);
        c.Variables.Chosen[0] = c.BestMember;

        menu.Popup = () => c.MemberPopupHint();

        return menu.Run(
            // orig: R108+0x14C2 (line 0); hint R108+0x16DC (1925:080C) matched
            new MenuLine(p => PickTheLock(c, menu, p), () => PickHint(c)),
            // orig: R108+0x173E (line 1); hint R108+0x1968 (1925:0A98) matched
            new MenuLine(p => UseEaterWater(c, menu, p), () => EaterWaterHint(c)),
            // orig: R108+0x19BE (line 2); hint R108+0x1AEC (1925:0C1C) matched
            new MenuLine(p => UseAThunderbolt(c, menu, p),
                () => OptionHint.ChanceOf(Math.Clamp(c.ItemQuality(Who.Everybody, Thunderbolt) + c.PartyMaxSkill((int)Skill.Artifice) * 2, 1, 99), c.Variables.Chosen[0])),
            // orig: R108+0x1B2C (1925:0C5C) matched; hint R108+0x1BC0
            new MenuLine(_ => GoToTheOffice(c, menu, 0, changePicture: false), Unknown),
            // orig: R108+0x1BF8 (1925:0D28) matched; hint R108+0x1CBA
            new MenuLine(_ => GoToTheOffice(c, menu, 2, changePicture: true), Unknown),
            // orig: R108+0x1CF2 (1925:0E22) matched; hint R108+0x1DB4
            new MenuLine(_ => GoToTheOffice(c, menu, 1, changePicture: true), Unknown),
            // orig: R108+0x1DEC (1925:0F1C) matched; hint R108+0x1EAE
            new MenuLine(_ => GoToTheOffice(c, menu, 3, changePicture: true), Unknown),
            // orig: R108+0x1EE6 (1925:1016) matched; hint R108+0x1EF2
            // DORMANT: line 7 is never offered; it lets no time pass.
            new MenuLine(_ => c.PassHours(0), Unknown),
            // orig: R108+0x1F2A (1925:105A) matched; hint R108+0x1F80
            new MenuLine(_ => Reconsider(c, menu), Unknown),
            // orig: R108+0x1FB8 (1925:10E8) matched; hint R108+0x2004
            new MenuLine(_ => { ShowPicture(c, StreetPicture); c.Screen.Text(12); c.Next = c.IsDaylight ? 9 : 10; }, Unknown));
    }

    private static OptionHint Unknown() => OptionHint.Say("You can always encounter the unknown");

    /// <summary>The trapped office of this house is the one whose number is the place's number modulo 4.</summary>
    private bool AtTheTrappedOffice(CardContext c) => c.Here % 4 == _office;

    private OptionHint PickHint(CardContext c)
    {
        var chance = Math.Clamp(c.PartyMaxSkill((int)Skill.Artifice) * 2, 20, 99);
        if (AtTheTrappedOffice(c))
        {
            chance -= c.HereQuery(2) * 2;
        }
        if (!c.Events.QuestAt(10, c.Here))
        {
            chance = 0;
        }
        return OptionHint.ChanceOf(chance, c.Variables.Chosen[0]);
    }

    private static OptionHint EaterWaterHint(CardContext c)
    {
        var chance = Math.Clamp(c.ItemQuality(Who.Everybody, EaterWater) + c.PartyMaxSkill((int)Skill.Artifice) * 2, 1, 99);
        if (!c.Events.QuestAt(10, c.Here))
        {
            chance = 0;
        }
        return OptionHint.ChanceOf(chance, c.Variables.Chosen[0]);
    }

    /// <summary>Before an office door: pick the lock, use a potion, or reconsider.</summary>
    // orig: R108+0x1B2C (1925:0C5C) matched (office 0, no door picture) and its twins R108+0x1BF8 (1925:0D28),
    // R108+0x1CF2 (1925:0E22), R108+0x1DEC (1925:0F1C) (offices 2, 1, 3)
    private void GoToTheOffice(CardContext c, CardMenu menu, int office, bool changePicture)
    {
        _office = office;
        // The first office (the leading merchant's) keeps the hall picture: the original shows no door there.
        if (changePicture)
        {
            _picture = DoorPicture;
            ShowPicture(c, DoorPicture);
        }
        menu.Block = 1;
        menu.Set(OptionState.Enabled, 0, 1, 2, 8);
        menu.Set(OptionState.Hidden, 3, 4, 5, 6, 7, 9);
        if (c.EventTypeAtHere(LockFailed, _office))
        {
            menu[0] = OptionState.Shown;
        }
        if (c.EventTypeAtHere(AcidFailed, _office))
        {
            menu.Set(OptionState.Shown, 0, 1);
        }
        if (c.CountItems(Who.Everybody, EaterWater) == 0)
        {
            menu[1] = OptionState.Shown;
        }
        if (c.CountItems(Who.Everybody, Thunderbolt) == 0)
        {
            menu[2] = OptionState.Shown;
        }
        c.Next = 257;
    }

    // orig: R108+0x1F2A (1925:105A) matched; tests: CardSmokeTests
    private void Reconsider(CardContext c, CardMenu menu)
    {
        _picture = HallPicture;
        ShowPicture(c, _picture);
        menu.Block = 0;
        menu.Set(OptionState.Hidden, 0, 1, 2, 7, 8);
        menu.Set(OptionState.Enabled, 3, 4, 5, 6, 9);
        c.Next = 257;
    }

    /// <summary>The office's trap burns the member's face: Charisma and Perception (and their maxima) fall, and injuries.</summary>
    // C: member_attr(...) / -3 and / -6 truncate toward zero, as C# division does.
    private static void SprungTheTrap(CardContext c)
    {
        var m = c.Variables.Chosen[0];
        c.AddAttribute(m, (int)Attribute.Charisma, c.Attribute(m, (int)Attribute.Charisma) / -3);
        c.AddAttribute(m, (int)Attribute.Perception, -(c.Attribute(m, (int)Attribute.Perception) / 8));
        c.AddAttributeMaximum(m, (int)Attribute.Charisma, c.AttributeMaximum(m, (int)Attribute.Charisma) / -6);
        c.AddAttributeMaximum(m, (int)Attribute.Perception, -(c.AttributeMaximum(m, (int)Attribute.Perception) / 16));
        c.Injure(m, 2, 3, 9);
        ShowPicture(c, TrappedPicture);
        c.Screen.Text(10);
        c.Next = c.IsDaylight ? 9 : 10;
    }

    /// <summary>Picking the lock. At the trapped office a bad roll (90+) springs the trap unless it was found before.</summary>
    // orig: R108+0x14C2 (1925:05F2) matched; tests: CardSmokeTests
    private void PickTheLock(CardContext c, CardMenu menu, int chance)
    {
        var roll = c.Random(100);
        menu[0] = OptionState.Shown;
        if (chance > roll)
        {
            SearchTheOffice(c);
            c.PracticeSkill(c.Variables.Chosen[0], (int)Skill.Artifice, 1, 10, 0);
            return;
        }
        if (AtTheTrappedOffice(c))
        {
            if (roll >= 90 && !c.EventTypeAtHere(LockFailed, _office))
            {
                SprungTheTrap(c);
                c.PracticeSkill(c.Variables.Chosen[0], (int)Skill.Artifice, 0, 10, 0);
                return;
            }
            c.EventHere(LockFailed, _office, 72);
            ShowPicture(c, DoorPicture);
            c.Screen.Text(9);
            ShowPicture(c, _picture);
            menu[1] = OptionState.Shown;
        }
        else
        {
            c.EventHere(LockFailed, _office, 72);
            ShowPicture(c, DoorPicture);
            c.Screen.Text(2);
            ShowPicture(c, _picture);
        }
        c.Next = 257;
        c.PracticeSkill(c.Variables.Chosen[0], (int)Skill.Artifice, 0, 10, 0);
    }

    /// <summary>An Eater-water potion is used up on the lock; the same risks as picking it.</summary>
    // orig: R108+0x173E (1925:086E) matched; tests: CardSmokeTests
    private void UseEaterWater(CardContext c, CardMenu menu, int chance)
    {
        var roll = c.Random(100);
        c.TakeItem(Who.Everybody, EaterWater);
        if (c.CountItems(Who.Everybody, EaterWater) == 0)
        {
            menu[1] = OptionState.Shown;
        }
        if (chance > roll)
        {
            SearchTheOffice(c);
            c.PracticeSkill(c.Variables.Chosen[0], (int)Skill.Artifice, 1, 10, 0);
            return;
        }
        if (AtTheTrappedOffice(c))
        {
            menu[1] = OptionState.Shown;
            if (roll >= 90 && !c.EventTypeAtHere(LockFailed, _office))
            {
                SprungTheTrap(c);
                c.PracticeSkill(c.Variables.Chosen[0], (int)Skill.Artifice, 0, 10, 0);
                return;
            }
            menu.Set(OptionState.Shown, 0, 1);
            c.EventHere(AcidFailed, _office, 72);
            ShowPicture(c, DoorPicture);
            c.Screen.Text(9);
        }
        else
        {
            c.EventHere(AcidFailed, _office, 72);
            ShowPicture(c, DoorPicture);
            c.Screen.Text(2);
        }
        ShowPicture(c, _picture);
        c.Next = 257;
        c.PracticeSkill(c.Variables.Chosen[0], (int)Skill.Artifice, 0, 10, 0);
    }

    /// <summary>
    /// A Thunderbolt potion blasts the door (it is used up). With an item quest here the item is
    /// found at once, whichever office this is; otherwise nothing, and the noise may bring the guards.
    /// </summary>
    // orig: R108+0x19BE (1925:0AEE) matched; tests: CardSmokeTests
    private void UseAThunderbolt(CardContext c, CardMenu menu, int chance)
    {
        var roll = c.Random(100);
        c.TakeItem(Who.Everybody, Thunderbolt);
        if (c.CountItems(Who.Everybody, Thunderbolt) == 0)
        {
            menu[2] = OptionState.Shown;
        }
        if (roll < chance)
        {
            if (c.Events.QuestAt(10, c.Here))
            {
                c.FinishQuest10(c.Here);
                ShowPicture(c, 189);
                c.Screen.Text(6);
                ShowPicture(c, _picture);
                // The original does not change next_card: the party stays upstairs.
                return;
            }
            // C: rand_mod(100) < party_avg_eff_attr2(): the average draws nothing.
            if (c.Random(100) < c.PartyAverageEffectiveAgility())
            {
                c.Screen.Text(18, TextFrame.BlankPage);
                c.Next = 10;
                return;
            }
            ShowPicture(c, 189);
            c.Screen.Text(5);
            FightTheGuards(c);
            return;
        }
        menu[2] = OptionState.Shown;
        c.EventHere(LockFailed, _office, 72);
        c.Screen.Text(17, TextFrame.BlankPage);
        c.Screen.Text(16, TextFrame.BlankPage);
        c.Next = 10;
    }

    /// <summary>
    /// Inside the office. If guards are about (events 0x17 of types 1..3 here) a clumsy party is
    /// heard and must fight; otherwise it slips out. The quest's item is found if it is this
    /// office (the quest's first data word modulo 4).
    /// </summary>
    // orig: R108+0x1308 (1925:0438) matched; tests: CardSmokeTests
    // C: (A || B || C) && rand_mod(100) >= ...: the roll is drawn only when one of the events exists.
    private void SearchTheOffice(CardContext c)
    {
        if ((c.EventTypeAtHere(0x17, 1) || c.EventTypeAtHere(0x17, 2) || c.EventTypeAtHere(0x17, 3))
            && c.Random(100) >= c.PartyAverageEffectiveAgility() + c.PartyAverageSkill((int)Skill.Stealth))
        {
            ShowPicture(c, 30);
            if (ItemIsInThisOffice(c))
            {
                c.FinishQuest10(c.Here);
                c.Screen.Text(8);
            }
            else
            {
                c.Screen.Text(7);
            }
            c.PracticeSkill(Who.Everybody, (int)Skill.Stealth, 0, 10, -1);
            FightTheGuards(c);
            return;
        }
        if (ItemIsInThisOffice(c))
        {
            c.FinishQuest10(c.Here);
            ShowPicture(c, 195);
            c.Screen.Text(4);
        }
        else
        {
            ShowPicture(c, 190);
            c.Screen.Text(3);
        }
        c.PracticeSkill(Who.Everybody, (int)Skill.Stealth, 1, 10, -1);
        c.Next = 0x16;
    }

    // C: events[event_find(...)] + 0x2A is the quest's first data word; % 4 is C's signed remainder.
    // C: in a house without the quest event_find gives -1 and events[-1] is the tail of var_named[4] (2E38:1B28), 0 in play: the word is
    // C: read at 0000:002A, the INT 0Ah vector's segment (NullPage), a multiple of 4: office 0 "holds the item" (text 4 and thunk 09C0:214D on no quest).
    private bool ItemIsInThisOffice(CardContext c)
    {
        var quest = c.Events.Find(8, -1, c.Here, -1, 10, -1);
        var data1 = c.Events[quest] is { } e ? e.Data1 : Core.World.NullPage.Int0ASegment;
        return data1 % 4 == _office;
    }

    /// <summary>
    /// The guards: beaten, the city's watch is alert for a while (events 0x12 and 0x11) and the
    /// party is out in the street; fled, those away are reported and the chase goes on (card
    /// 0x7A); beaten down, stripped and dumped, or taken to the dungeon (card 0x0D).
    /// </summary>
    // orig: R108+0x1156 (1925:0286) matched; tests: CardSmokeTests
    // C: right-to-left arguments: the guard count is drawn before run_battle (seed -2 draws nothing).
    // DORMANT: the switch's explicit cases -0x40..-1 (and results above 4) return without a change; run_battle never gives them.
    private static void FightTheGuards(CardContext c)
    {
        var guards = c.Random(5) + 3;
        switch (c.RunBattle(0x1C, -2, 3, 2, guards, 0, 3, 1, -1, 0, 0))
        {
            case 0:
                ShowPicture(c, StreetPicture);
                c.Screen.Text(12);
                c.Next = c.IsDaylight ? 9 : 10;
                c.Events.Create(-2, c.Here, 0x1E, 0x12, 1, 0x63, 0, 0x24, 0, 0);
                c.EventHere(0x11, 240);
                return;
            case 1:
            case 2:
                c.ReportAway();
                c.PassHours(1);
                ShowPicture(c, 46);
                c.Screen.Text(11);
                c.Next = 0x7A;
                return;
            case 3:
                c.PassHours(2);
                c.LoseItems(Who.Everybody);
                ShowPicture(c, 55);
                c.Screen.Text(14);
                c.Next = c.IsDaylight ? 9 : 10;
                return;
            case 4:
                c.PassHours(1);
                c.LoseItems(Who.Everybody);
                ShowPicture(c, 279);
                c.Screen.Text(13);
                c.Next = 0x0D;
                return;
            default:
                return;
        }
    }
}
