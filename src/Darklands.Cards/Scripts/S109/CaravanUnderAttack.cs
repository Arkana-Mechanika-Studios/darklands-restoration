using Darklands.Core.Characters;
using Darklands.Core.Economy;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Rescu00": in the next valley robbers are overwhelming a merchant caravan. The party can
/// charge in, pray for the merchants, or pass by. Saved merchants reward the party with money
/// and an item.
/// </summary>
// orig: R109+0x0F6A (192E:000A) matched; tests: CardSmokeTests
// orig: R109+0x186E (192E:090E) matched (registration: standard item pop-ups; saint pop-ups do not name the member)
// DORMANT: texts 1, 2 (the charge) and 4 (the raubritter's tower) are never shown by the script.
public sealed class CaravanUnderAttack : Card
{
    private const int ValleyPicture = 87;

    public override int Number => 261;

    public override int Run(CardContext c)
    {
        // The name is copied from a data block (DS:6594, "$Rescu00").
        Show(c, ValleyPicture);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Set(OptionState.Enabled, 0, 1, 2);
        c.SetCardSaints(0x0C, 0x3F, 0x50);
        menu[1] = c.SaintsUsable();
        menu.Popup = () => c.SaintPopupChanceHint();

        return menu.Run(
            // orig: R109+0x12D8 (192E:0378) matched; hint R109+0x14B4
            new MenuLine(_ => ChargeIntoTheFray(c), () => OptionHint.Say("Enter Combat")),
            // orig: R109+0x14EC (192E:058C) matched; hint R109+0x15CE (the placeholder str_saint_xxx)
            new MenuLine(_ => PrayForTheMerchants(c), () => OptionHint.Say("XXX Saint % success msg XXX.")),
            // orig: R109+0x1606 (192E:06A6) matched; hint R109+0x1658
            new MenuLine(_ => { c.Variables.Chosen[0] = c.Leader; ShowPicture(c, 145); c.Screen.Text(11); c.PassHours(2); c.LeaveForTheMap(); },
                () => OptionHint.Say(SuccessAssured, c.Leader)),
            // DORMANT: lines 3..9 are never offered; each lets no time pass (R109+0x1692..08ca).
            new MenuLine(_ => c.PassHours(0)),
            new MenuLine(_ => c.PassHours(0)),
            new MenuLine(_ => c.PassHours(0)),
            new MenuLine(_ => c.PassHours(0)),
            new MenuLine(_ => c.PassHours(0)),
            new MenuLine(_ => c.PassHours(0)),
            new MenuLine(_ => c.PassHours(0)));
    }

    /// <summary>
    /// The merchants' thanks: 12, 24 or 36 groschen and an item for the leader — a potion, or a
    /// piece of gear of good quality (42..46).
    /// </summary>
    // orig: R109+0x119C (192E:023C) matched; tests: CardSmokeTests
    // C: the item record passed by value is {code, type byte (def +0x1E), quality byte, count 1, weight byte (def +0x25)}.
    private static void TheMerchantsReward(CardContext c)
    {
        c.PracticeSkill(Who.Everybody, (int)Skill.Virtue, 7, 15, 0);
        c.Variables.Money[0] = Money.From(0, (c.Random(3) + 1) * 12, 0);
        c.State.Party.Purse = c.State.Party.Purse.Add(c.Variables.Money[0]);
        int item;
        int quality;
        switch (c.Random(7))
        {
            case 0:
                item = c.Random(22) + 0x5F;
                quality = 0x2D;
                break;
            case 2:
                item = 3;
                quality = c.Random(5) + 0x2A;
                break;
            case 3:
                item = 0x0A;
                quality = c.Random(5) + 0x2A;
                break;
            case 4:
                item = 0x12;
                quality = c.Random(5) + 0x2B;
                break;
            case 5:
                item = 0x1A;
                quality = c.Random(5) + 0x2A;
                break;
            default:
                item = 0x20;
                quality = c.Random(5) + 0x2A;
                break;
        }
        var d = c.World.Items.Items[item];
        c.AddItemStack(Who.Leader, new ItemStack(item, (sbyte)d.Type, (sbyte)quality, 1, (sbyte)d.Weight));
        c.NameToSlot(0, item, 0);
        ShowPicture(c, 194);
        c.Screen.Text(3);
    }

    /// <summary>
    /// The robbers: won, the reward; fled or beaten, the caravan is lost (the party buries the
    /// dead); left for dead, everybody loses half their Strength.
    /// </summary>
    // orig: R109+0x12D8 (192E:0378) matched; tests: CardSmokeTests
    // C: right-to-left arguments: the robbers' number is drawn before run_battle draws its seed (-1).
    private static void ChargeIntoTheFray(CardContext c)
    {
        var robbers = c.Random(3) + 5;
        switch (c.RunBattle(0x38, -1, 7, 2, robbers, 0, 3, 1, -1, 0, 0))
        {
            case 0:
                c.PassHours(1);
                TheMerchantsReward(c);
                break;
            case 1:
            case 2:
                c.PassHours(4);
                c.RejoinAway();
                ShowPicture(c, 145);
                c.Screen.Text(5);
                break;
            case 3:
                c.PassHours(c.HoursUntil(18));
                c.RejoinAway();
                c.PracticeSkill(Who.Everybody, (int)Skill.Virtue, 0, 10, 0);
                ShowPicture(c, 288);
                c.Screen.Text(7);
                break;
            case 4:
                var dead = 0;
                for (var i = 0; i < Party.MaxMembers; i++)
                {
                    if (c.Status(i) == CharacterStatus.Empty)
                    {
                        continue;
                    }
                    c.AddAttribute(i, (int)Attribute.Strength, -(c.Attribute(i, (int)Attribute.Strength) / 2 + 5));
                    // DORMANT: add_attr keeps an attribute at 1 or more, so nobody dies here and texts 12/13 are never shown.
                    if (c.Attribute(i, (int)Attribute.Strength) <= 0)
                    {
                        c.SetStatus(i, CharacterStatus.Empty);
                        c.DeleteMember(i);
                        if (dead++ != 0)
                        {
                            c.Variables.Chosen[1] = i;
                        }
                        else
                        {
                            c.Variables.Chosen[0] = i;
                        }
                    }
                }
                c.PassHours(c.HoursUntil(18));
                c.PracticeSkill(Who.Everybody, (int)Skill.Virtue, 0, 10, 0);
                ShowPicture(c, 177);
                c.Screen.Text(dead == 0 ? 6 : dead == 1 ? 12 : 13);
                break;
        }
        c.LeaveForTheMap();
    }

    /// <summary>St. Apollinarius (slot 0) sends the robbers fleeing and the merchants reward the party; the others save them unseen.</summary>
    // orig: R109+0x14EC (192E:058C) matched; tests: CardSmokeTests
    private static void PrayForTheMerchants(CardContext c)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (answer == -1)
        {
            return;
        }
        if (answer == 1)
        {
            switch (c.MenuSlot)
            {
                case 0:
                    ShowPicture(c, 196);
                    c.Screen.Text(9);
                    TheMerchantsReward(c);
                    break;
                case 1:
                case 2:
                    ShowPicture(c, 196);
                    c.Screen.Text(8);
                    c.PracticeSkill(Who.Everybody, (int)Skill.Virtue, 7, 10, 0);
                    break;
            }
            c.LeaveForTheMap();
            return;
        }
        c.Screen.Text(10);
        c.Next = 261;
    }
}
