using Darklands.Core;
using Darklands.Core.Characters;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$CampB00": bandits find the party's camp. With nobody on guard they attack at once; when the
/// guard sees them coming the party may ignore them (they attack), leave, ask a saint to lead
/// them away, or lay an ambush. Every outcome ends on the map (card 0x0C).
/// </summary>
// orig: R134+0x152C (198A:000C) matched; tests: CardSmokeTests
//   option 0: action R134+0x188E; hint R134+0x18A4
//   option 1: action R134+0x18DC; hint R134+0x18EA
//   option 2: action R134+0x1922; hint R134+0x1980
//   option 3: action R134+0x19B8; hint R134+0x1A1E
//   option 4: action R134+0x1A8C; hint R134+0x1AAA
//   option 5: action R134+0x1AE6; hint R134+0x1B04
//   option 6: action R134+0x1B40; hint R134+0x1B5E
//   option 7: action R134+0x1B9A; hint R134+0x1BB8
//   option 8: action R134+0x1BF4; hint R134+0x1C12
//   option 9: action R134+0x1C4E; hint R134+0x1C6C
//   saint pop-up: R134+0x1DC8, R134+0x1E50
//   other: R134+0x177E, R134+0x1CA8, R134+0x1D00,
//     R134+0x1D38, R134+0x1ED8
// DORMANT (generated): option slots 10..19 keep the default handler R134+0x1D00 ("screwed up.") and are never shown.
// DORMANT (generated): option line(s) 4, 5, 6, 7, 8, 9 are never enabled (every assignment sets their flag to 0).
// DORMANT (generated): card-item-lines stays all -1, so no line opens an item pop-up.
// C: the card-engine globals DS:EE70 / DS:EE72 the C sets are the pop-up boxes' drawing flags (R043+0x2ACA, see DIVERGENCE_REVIEW); presentation only, not modelled.
// C: limit-to-range(lo, hi, v) is Math.Clamp(v, lo, hi) here; every call has lo <= hi.
public sealed class BanditsNearTheCamp : Card
{
    public override int Number => 362;

    private const int Picture = 0;

    /// <summary>The camp is left in peace for a week (event 0x60, 168 hours).</summary>
    private const int LeftInPeace = 0x60;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2, 3);
        c.Variables.Chosen[0] = c.State.SavedCard; // the member who kept watch, −1 none
        if (c.State.SavedCard == -1)
        {
            c.Screen.Text(1);
            Fight(c, surprised: true);
        }
        // The original clears only the saints here; the items stay as the last card set them.
        c.SetCardSaints(0x36, 0x3D, 0x23);
        menu[2] = c.SaintsUsable();

        var unknown = OptionHint.Saying(CardTexts.Unknown);
        return menu.Run([
            new MenuLine(_ =>
            {
                c.Screen.Text(1);
                Fight(c, surprised: true);
            }, unknown),
            new MenuLine(_ => Leave(c), unknown),
            new MenuLine(_ => CallOnASaint(c, menu), OptionHint.Saying(CardTexts.SelectSaint), c.SaintPopupChanceHint),
            new MenuLine(chance => LayAnAmbush(c, chance), () => AmbushChance(c)),
            // orig: R134+0x1A8C .. R134+0x1C4E (hints R134+0x1AAA .. R134+0x1C6C) matched
            // DORMANT: lines 4..9 are never enabled; their handlers show text 0 and go to 0x62 ("Success is assured.").
            // C: this card never sets item-handler-table, so an item pop-up would run whatever the previous card left there;
            //   the table's generic item hint stands in for it (unreachable: no item line).
            .. CardMenu.FirstTextAndEndTable(c, SuccessAssured)[4..]]);
    }

    // DORMANT: R134+0x1D38, _081a, _081c, _081e and _09b8 are empty functions nothing calls.
    // C: the table set-up R134+0x1CA8 fills only saint-handler-table and clears only card-saint-lines (not card-item-lines).

    /// <summary>The praying member is $ChosenTwoName; only a full success (1) sends the bandits away.</summary>
    // orig: R134+0x1922 matched
    private void CallOnASaint(CardContext c, CardMenu menu)
    {
        c.Variables.Chosen[1] = c.MenuMember;
        var result = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuEntrySlot]);
        if (result == -1)
        {
            return;
        }
        if (result == 1)
        {
            c.EventHere(LeftInPeace, 0xA8);
            c.Screen.Text(2);
            Leave(c);
            return;
        }
        menu.Block = 6;
        c.Next = Number;
    }

    /// <summary>Average agility, an eighth of the leader's Intelligence, a third of average Stealth and of the best Woodwise.</summary>
    // orig: R134+0x1A1E matched
    private static OptionHint AmbushChance(CardContext c)
    {
        var chance = c.PartyAverageEffectiveAgility()
            + c.Attribute(Who.Leader, (int)Attribute.Intelligence) / 8
            + c.PartyAverageSkill((int)Skill.Stealth) / 3
            + c.PartyMaxSkill((int)Skill.Woodwise) / 3;
        return OptionHint.ChanceOf(chance, c.Leader);
    }

    /// <summary>The ambush is laid (1..5 hours); if it fails the clumsiest member gives it away. Then the fight.</summary>
    // orig: R134+0x19B8 matched
    private static void LayAnAmbush(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            c.PassHours(c.Random(5) + 1);
            c.Variables.Chosen[0] = c.Leader;
            c.Screen.Text(3);
        }
        else
        {
            c.PassHours(c.Random(5) + 1);
            c.PartyMinEffectiveAgility();
            c.Variables.Chosen[0] = c.BestMember;
            c.Screen.Text(4);
        }
        Fight(c, surprised: false);
    }

    /// <summary>The bandits, more and better the more the party is known (party lore level).</summary>
    // orig: R134+0x177E matched
    // C: battle-driver's arguments are evaluated right to left: the count's roll-below(4) (5th argument) is drawn before
    //   the level's roll-below(2) (4th). party-lore-rating and party-here-total draw nothing.
    private static void Fight(CardContext c, bool surprised)
    {
        var lore = c.PartyLoreLevel();
        var groups = lore % 3 + 1;
        var kind = lore > 5 ? 0x16 : 0x12;
        var count = Math.Clamp(c.Random(4) + lore / 3 + 2, c.PresentCount(), 7);
        var level = c.Random(2) + lore / 4 + 1;
        switch (c.RunBattle(surprised ? 0x30 : 0x2F, -1, 7, level, count, kind, groups, 1, -1, 0, 0))
        {
            case 0:
                c.EventHere(LeftInPeace, 0xA8);
                c.PassHours(1);
                c.Variables.Chosen[0] = c.Leader;
                c.Screen.Text(7);
                Leave(c);
                break;
            case 1:
            case 2:
                c.PassHours(1);
                c.RejoinAway();
                c.Screen.Text(8);
                Leave(c);
                break;
            case 3:
            case 4:
                c.EventHere(LeftInPeace, 0xA8);
                c.PassHours(1);
                c.Screen.Text(9);
                Leave(c);
                break;
        }
    }

    private static void Leave(CardContext c)
    {
        c.State.CurrentLocation = GameState.Wilderness;
        c.Next = 0x0C;
    }
}
