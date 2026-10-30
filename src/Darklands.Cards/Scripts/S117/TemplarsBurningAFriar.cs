using Darklands.Core.Characters;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$wqSta03": white-robed Templars with a red cross are about to burn a Franciscan friar at
/// the stake. The party can approach, pray, or ignore them; then greet them, throw a potion,
/// attack, or leave. A rescued friar tells of the Templars' monastery; losing means capture
/// for the Sabbat (card 296).
/// </summary>
// orig: R117+0x0A80 (18E0:0000) matched; tests: CardSmokeTests
// orig: R117+0x1152 (18E0:06D2) matched (registration: item pop-ups R117+0x122C/07da/0808/0836 name the member; saint pop-ups do not)
public sealed class TemplarsBurningAFriar : Card
{
    public override int Number => 295;

    public override int Run(CardContext c)
    {
        c.PlayMusic(0x2A);
        Show(c, 0);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Set(OptionState.Enabled, 0, 1, 2);
        c.PartyMaxSkill((int)Skill.Religion);
        c.Variables.Chosen[0] = c.BestMember;
        // C: party-map-column is declared `extern long`, so the call pushes party-map-row and party-map-column (and 0x24): closest-place-of-kind gets (x, y, 0x24); nothing is undefined (checked against the asm).
        c.Variables.Location = c.NearestLocationOfType(c.State.MapPosition.X, c.State.MapPosition.Y, 0x24);
        c.Variables.Location2 = c.SabbatSite();
        c.SetCardItems(0x5F, 0x60, 0x65, 0x66);
        c.SetCardSaints(0x25, 0x3D, 0x43, 0x5D);
        menu[4] = c.ItemsUsable();
        menu[1] = c.SaintsUsable();
        menu.Popup = () => c.SaintPopupChanceHint();

        return menu.Run(
            // orig: R117+0x0E0A (18E0:038A) matched; hint R117+0x0E38
            new MenuLine(_ => MoveCloser(c, menu), Unknown),
            // orig: R117+0x0E70 (18E0:03F0) matched; hint R117+0x0EB4
            new MenuLine(_ => PrayForGuidance(c), () => OptionHint.Say("Select a person and a saint.")),
            // orig: R117+0x0EEC (18E0:046C) matched; hint R117+0x0EFA
            new MenuLine(_ => c.LeaveForTheMap(), () => OptionHint.Say(NoRisk)),
            // orig: R117+0x0F32 (18E0:04B2) matched; hint R117+0x0F48
            new MenuLine(_ => { c.Screen.Text(4); FightTheTemplars(c); }, Unknown),
            // orig: R117+0x0F80 (18E0:0500) matched; hint R117+0x0FF0
            new MenuLine(p => ThrowAPotion(c, menu, p), () => OptionHint.Say("Select a person and a potion."), () => PotionHint(c)),
            // orig: R117+0x1028 (18E0:05A8) matched; hint R117+0x1032
            new MenuLine(_ => FightTheTemplars(c), () => OptionHint.Say("Enter Combat")),
            // orig: R117+0x106A (18E0:05EA) matched; hint R117+0x10AE
            new MenuLine(_ => LeaveTheFriar(c), () => OptionHint.Say(NoRisk)),
            // orig: R117+0x10E6 (18E0:0666) matched; hint R117+0x1118
            // The flight always succeeds: the hint's chance is shown but not rolled.
            new MenuLine(_ => { c.Screen.Text(7); c.AddAttribute(Who.Everybody, (int)Attribute.DivineFavor, -5); c.PassHours(2); c.LeaveForTheMap(); },
                () => OptionHint.ChanceOf(Math.Clamp(c.PartyMaxSkill((int)Skill.Woodwise) + c.PartyAverageEffectiveAgility() * 2, 1, 99), OptionHint.WholeParty)),
            // DORMANT: lines 8 and 9 are never offered; actions R117+0x114A/06ce and hints R117+0x114C/06d0 are
            // C: empty; the hints return AX/DX; neutral hints are used.
            new MenuLine(_ => { }),
            new MenuLine(_ => { }));
    }

    private static OptionHint Unknown() => OptionHint.Say("You can always encounter the unknown");

    /// <summary>The potions' chances by slot: quality + 2, − 3, + 12, × 2.</summary>
    // orig: R117+0x122C (18E0:07AC) matched; slots 1..3 R117+0x125A, R117+0x1288, R117+0x12B6
    private static OptionHint PotionHint(CardContext c)
    {
        var m = c.MenuMember;
        c.Variables.Chosen[0] = m;
        var quality = c.ItemQuality(m, c.CardItems[c.MenuEntrySlot]);
        var chance = c.MenuSlot switch { 0 => quality + 2, 1 => quality - 3, 2 => quality + 12, _ => quality * 2 };
        return OptionHint.ChanceOf(chance, m);
    }

    /// <summary>Close up: greet them, throw a potion, attack, or leave.</summary>
    // orig: R117+0x0E0A (18E0:038A) matched; tests: CardSmokeTests
    private static void MoveCloser(CardContext c, CardMenu menu)
    {
        menu.Set(OptionState.Hidden, 0, 1, 2, 7, 8, 9);
        menu.Set(OptionState.Enabled, 3, 4, 5, 6);
        menu.Block = 1;
        c.Next = 295;
    }

    // orig: R117+0x0E70 (18E0:03F0) matched; tests: CardSmokeTests
    private static void PrayForGuidance(CardContext c)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuEntrySlot]);
        if (answer == -1)
        {
            return;
        }
        c.Screen.Text(answer == 1 ? 2 : 3);
        c.Next = 295;
    }

    /// <summary>The potion (not taken: the original never takes it) scatters the Templars, or they charge.</summary>
    // orig: R117+0x0F80 (18E0:0500) matched; tests: CardSmokeTests
    private static void ThrowAPotion(CardContext c, CardMenu menu, int chance)
    {
        if (c.Random(100) <= chance)
        {
            c.Screen.Text(10);
            c.PracticeSkill(c.MenuMember, (int)Skill.Virtue, 1, 10, 0);
            c.LeaveForTheMap();
            return;
        }
        menu.Set(OptionState.Enabled, 5, 7);
        menu.Set(OptionState.Hidden, 0, 1, 2, 3, 4, 6, 8, 9);
        menu.Block = 5;
        c.Next = 295;
    }

    // orig: R117+0x106A (18E0:05EA) matched; tests: CardSmokeTests
    private static void LeaveTheFriar(CardContext c)
    {
        c.Screen.Text(9);
        c.AddSkill(Who.Everybody, (int)Skill.Virtue, -1);
        c.AddAttribute(Who.Everybody, (int)Attribute.DivineFavor, -20);
        c.AddAttributeMaximum(Who.Everybody, (int)Attribute.DivineFavor, -2);
        c.LeaveForTheMap();
    }

    /// <summary>
    /// The Templars: won, the friar is saved (Virtue, Divine Favour); fled, the friar is lost
    /// (text 7 is shown even after text 11, as in the original); beaten, captured for the Sabbat (card 296).
    /// </summary>
    // orig: R117+0x0D16 (18E0:0296) matched; tests: CardSmokeTests
    // C: arguments right to left: roll-below(3) + 1 (eighth) is drawn before roll-below(5) + 3 (fifth); the seed is the place's (-2).
    // C: the function takes no parameter; the callers' arguments (1, 2) are ignored.
    private static void FightTheTemplars(CardContext c)
    {
        var leaders = c.Random(3) + 1;
        var monks = c.Random(5) + 3;
        switch (c.RunBattle(0x30, -2, 0x28, 2, monks, 0, 3, leaders, -1, 0, 0))
        {
            case 0:
                c.Screen.Text(6);
                c.PracticeSkill(Who.Everybody, (int)Skill.Virtue, 1, 15, 0);
                c.AddAttribute(Who.Everybody, (int)Attribute.DivineFavor, 10);
                c.AddAttributeMaximum(Who.Everybody, (int)Attribute.DivineFavor, 1);
                c.PassHours(1);
                c.LeaveForTheMap();
                break;
            case 1:
            case 2:
                c.Screen.Text(c.DropAway() != 0 ? 11 : 7);
                c.Screen.Text(7);
                c.AddAttribute(Who.Everybody, (int)Attribute.DivineFavor, -2);
                c.PassHours(1);
                c.LeaveForTheMap();
                break;
            case 3:
            case 4:
                c.PassHours(1);
                c.Screen.Text(8);
                c.Next = 296;
                break;
        }
    }
}
