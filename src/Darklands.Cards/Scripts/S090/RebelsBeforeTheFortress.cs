using Darklands.Core.Characters;
using Darklands.Core.Economy;
using Darklands.Core.Events;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Revol14": the crowd surrounds the fortress and demands the city lord's surrender. The
/// party may talk or bribe the sergeant of the guard into opening a sally port, pray, scale
/// the wall, blow open or dissolve a door with a potion, wait a day (0xBC), or abandon the
/// rebels. Every way in leads to the fight inside.
/// </summary>
// orig: R090+0x161E (1999:000E) matched; tests: CardSmokeTests
public sealed class RebelsBeforeTheFortress : Card
{
    private const int Picture = 0;
    private const int Rope = 0x3B;
    private const int FirePotion = 0x63;
    private const int AcidPotion = 0x66;
    private const int GateSaint = 0x0C;
    private const int TownSaint = 0x72;

    /// <summary>The rebels' location fact: the sergeant refused to be bought (48 hours).</summary>
    private const int SergeantRefused = 0x26;

    /// <summary>The rebels' location fact: the party deserted them (1,440 hours).</summary>
    private const int Deserted = 0x25;

    private const int RebelsTag = 2;

    public override int Number => 0xBA;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = Number;
        var menu = Menu(c);
        menu.Enable(0, 1, 4, 6, 7);
        c.RandomName(c.City.Ordinal + 0x0C, c.LocationQuery(c.Here, 0x21) % 5 == 0 ? 1 : 0, 0);
        c.PartyMaxSkill((int)Skill.Stealth);
        c.Variables.Chosen[0] = (byte)c.BestMember;
        var price = Money.From(c.HereQuery(2) + c.SidePercent(1) / 17, 0, 0);
        c.Variables.Money[0] = price;
        if (c.State.Party.Purse < price)
        {
            menu[1] = OptionState.Shown;
        }
        if (c.Events.Fact(RebelsTag, SergeantRefused, c.Here))
        {
            menu[0] = OptionState.Shown;
            menu[1] = OptionState.Shown;
        }
        // UNCERTAIN: as the original has it, carrying item 0x3B greys out the climb.
        if (c.CountItems(Who.Everybody, Rope) != 0)
        {
            menu[4] = OptionState.Shown;
        }
        if (c.State.PreviousCard == 0xBC)
        {
            menu[4] = OptionState.Shown;
            menu[6] = OptionState.Shown;
        }
        // orig: R090+0x1FDA (1999:09CA) matched (handlers, pop-up hints, both lists)
        c.SetCardItems(FirePotion, AcidPotion);
        c.SetCardSaints(GateSaint, TownSaint, 0x61, 0x59);
        menu[5] = c.ItemsUsable();
        menu[3] = c.SaintsUsable();

        void SergeantRefuses()
        {
            c.Events.Create(Who.Everybody, c.Here, 0x5A, EventKinds.LocationFact, SergeantRefused, 0, 0, 0x30, 0, RebelsTag);
            menu[0] = OptionState.Shown;
            menu[1] = OptionState.Shown;
            c.Screen.Text(2);
            c.PassHours(1);
        }

        return menu.Run(
            // orig: R090+0x1A20 (1999:0410); hint R090+0x1A90 matched
            new MenuLine(chance => { if (Succeeds(c, chance)) { c.PassHours(1); c.Screen.Text(1); StormTheFortress(c); } else { SergeantRefuses(); } },
                () => OptionHint.ChanceOf(TalkChance(c), c.Leader)),
            // orig: R090+0x1AE6 (1999:04D6); hint R090+0x1B62 matched
            new MenuLine(() =>
                {
                    if (Succeeds(c, BribeChance(c)))
                    {
                        c.State.Party.Purse = c.State.Party.Purse.Subtract(c.Variables.Money[0]);
                        c.PassHours(1);
                        c.Screen.Text(3);
                        StormTheFortress(c);
                    }
                    else
                    {
                        SergeantRefuses();
                    }
                },
                () => OptionHint.ChanceOf(BribeChance(c), c.Leader)),
            // orig: R090+0x1BAE (1999:059E); hint R090+0x1C1A matched
            // DORMANT: line 2 is hidden (flag 0).
            new MenuLine(() => PrayForTheGate(c), OptionHint.Saying(CardTexts.SelectSaint)),
            // orig: R090+0x1C52 (1999:0642); hint R090+0x1D0A; saint hints R090+0x20CC..0c54 matched
            new MenuLine(() => Pray(c), OptionHint.Saying(CardTexts.SelectSaint), c.SaintPopupChanceHint),
            // orig: R090+0x1D42 (1999:0732); hint R090+0x1D9E matched
            new MenuLine(chance => ScaleTheWall(c, chance), () => OptionHint.ChanceOf(ClimbChance(c), c.Variables.Chosen[0])),
            // orig: R090+0x1DCC (1999:07BC); hint R090+0x1E5A; item hints R090+0x209C, R090+0x20B4 (100 %) matched
            new MenuLine(() => UseAPotion(c, c.MenuPopup ? 100 : -1), OptionHint.Saying(CardTexts.SelectPotion)),
            // orig: R090+0x1E92 (1999:0882); hint R090+0x1EA4 matched
            new MenuLine(() => { c.PassHours(24); c.Next = 0xBC; }, OptionHint.Saying(NoRisk)),
            // orig: R090+0x1EDC (1999:08CC); hint R090+0x1F2A ("Coward!", checked against asm) matched
            new MenuLine(() => Desert(c), OptionHint.Saying("Coward!")),
            // orig: R090+0x1F52; R090+0x1F96 matched
            // DORMANT: lines 8 and 9 are hidden (flags 0); they would only let no time pass.
            new MenuLine(() => c.PassHours(0)),
            new MenuLine(() => c.PassHours(0)));
    }

    private static bool Succeeds(CardContext c, int chance) => c.Random(100) <= chance;

    private static int TalkChance(CardContext c) =>
        Math.Clamp((c.Attribute(Who.Leader, (int)Attribute.Charisma) + c.SidePercent(-1) + c.Skill(Who.Leader, (int)Skill.SpeakCommon) - 0x55) / 3, 0, 99);

    private static int BribeChance(CardContext c) =>
        Math.Clamp(c.Skill(Who.Leader, (int)Skill.SpeakCommon) / 2 + ((c.Attribute(Who.Leader, (int)Attribute.Charisma) + 20) << 1), 0, 99);

    private static int ClimbChance(CardContext c) => Math.Clamp(c.EffectiveAgility(c.Variables.Chosen[0]) << 1, 0, 99);

    /// <summary>A prayer the card never offers (its line is hidden): the gate opens.</summary>
    private static void PrayForTheGate(CardContext c)
    {
        c.Variables.Chosen[1] = c.MenuMember;
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (answer == -1)
        {
            return;
        }
        c.PassHours(1);
        if (answer == 1)
        {
            c.Screen.Text(4);
            StormTheFortress(c);
            return;
        }
        // (The original also lowers its own copy of the reputation here, which nothing reads.)
        c.Screen.Text(6);
        c.Next = 0xBA;
    }

    /// <summary>Any answered prayer brings the party in; saint 0x0C opens the gate, saint 0x72 also wins the town's favour.</summary>
    private static void Pray(CardContext c)
    {
        c.Variables.Chosen[1] = c.MenuMember;
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (answer == -1)
        {
            return;
        }
        if (answer == 1)
        {
            c.Screen.Text(5);
            var saint = c.CardSaints[c.MenuSlot];
            if (saint == GateSaint)
            {
                c.PassHours(1);
                c.Screen.Text(4);
                StormTheFortress(c);
                return;
            }
            if (saint == TownSaint)
            {
                c.AdjustReputation(c.Here, 20, 20);
            }
            StormTheFortress(c);
            return;
        }
        c.AdjustReputation(c.Here, -2, -1);
        c.PassHours(1);
        c.Screen.Text(6);
        c.Next = 0xBA;
    }

    private static void ScaleTheWall(CardContext c, int chance)
    {
        if (Succeeds(c, chance))
        {
            c.Screen.Text(8);
            c.PassHours(1);
            StormTheFortress(c);
            return;
        }
        c.Injure(c.Variables.Chosen[0], 2, 3, 10);
        c.Screen.Text(9);
        c.PassHours(1);
        c.Next = 0xBA;
    }

    /// <summary>The potion chosen in the pop-up always works; chosen any other way it is spilled.</summary>
    private static void UseAPotion(CardContext c, int chance)
    {
        var potion = c.CardItems[c.MenuSlot];
        c.TakeItem(c.MenuMember, potion);
        if (Succeeds(c, chance))
        {
            c.Screen.Text(potion == AcidPotion ? 10 : 11);
            c.PassHours(1);
            StormTheFortress(c);
            return;
        }
        c.Screen.Text(12);
        c.PassHours(1);
        c.Next = 0xBA;
    }

    private static void Desert(CardContext c)
    {
        c.Events.Create(Who.Everybody, c.Here, 0x5A, EventKinds.LocationFact, Deserted, 0, 0, 0x5A0, 0, RebelsTag);
        c.ConflictOutcome(-1, -1, 1);
        c.Screen.Text(15);
        c.Next = c.IsDaylight ? 9 : 10;
    }

    /// <summary>The fight inside the fortress; a win leads to the city lord (0x60).</summary>
    // orig: R090+0x1936 (1999:0326) matched
    // C: called with an argument (0 or 1) it does not declare; ignored.
    private static void StormTheFortress(CardContext c)
    {
        // Arguments are evaluated right to left: the second group's number is drawn first.
        var second = c.Random(4) + 3;
        var first = c.Random(5) + 3;
        var result = c.BattleResult = c.RunBattle(0x25, c.Here + 0x70, 3, 2, first, 0, 2, second, -1, 0, 0);
        switch (result)
        {
            case 0:
                c.PassHours(0);
                c.Next = 0x60;
                break;
            case 1 or 2:
                c.RejoinAway();
                c.PassHours(1);
                c.ConflictOutcome(-1, -1, 1);
                c.Next = 0x82;
                break;
            case 3:
                c.ConflictOutcome(-1, -1, 1);
                c.LoseItems(Who.Everybody);
                c.Screen.Text(13);
                c.PassHours(1);
                c.Next = 0x0D;
                break;
            case 4:
                c.ConflictOutcome(-1, -1, 1);
                c.LoseItems(Who.Everybody);
                c.Injure(Who.Everybody, 2, 2, 4);
                c.Screen.Text(14);
                c.PassHours(1);
                c.Next = 0x0D;
                break;
        }
    }
}
