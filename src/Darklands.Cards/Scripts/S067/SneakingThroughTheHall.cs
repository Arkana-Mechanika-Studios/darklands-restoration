using Darklands.Core.Characters;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Sneak00": the party creeps through a guild hall or a faction's house it has slipped
/// into (the guild is handed on in party_x). Search on and perhaps overhear the leader's
/// secret, or slip out; a blunder brings the guards (a fight, or out of a window with a rope).
/// </summary>
// orig: R067+0x1ECE (1A24:000E) matched; tests: CardSmokeTests
// orig: handler setup R067+0x278A (no pop-ups; unused-line handler 0902 "screwed up.")
public sealed class SneakingThroughTheHall : Card
{
    private const int PoliticsQuest = 2;
    private const int Rope = 0x3B;

    public override int Number => 0x5F;

    public override int Run(CardContext c)
    {
        // BUG: the card (R067+0x1ECE) never calls card_load: it only fills card_name and pic_buf ("common"), so the
        // screen keeps the previous card's picture and its blocks 6 and 0x0D, and those of every later
        // text, are read from the previous card's message file (the guild's: $Black00, $Sword00 ...),
        // until a member's sheet, the party information or a battle has the card loaded again
        // (R043+0x1E82 and run_battle's card_load load card_name's texts, "$Sneak00"); see NameTheCard.
        // DIVERGENCE: after the rebels' cards ($Revol00 has 13 blocks, $Revol01 10) the blocks 0x0D, 0x0E and up are not in the loaded
        //   file: card_text and card_options print from msg_file + msg_index.ofs[n], a stale entry msg_load did not set (R043+0x020E: what an
        //   earlier, longer message file left; 0d 0a 0a under the oracle), undefined memory; the C# prints an empty text, with the same lines.
        c.Screen.NameTheCard(Name, PictureNames.Of(0));
        c.Next = Number;
        var menu = Menu(c, 0x0D);
        menu.Enable(0, 1, 3, 9);
        menu.EachRound = () => c.Screen.Text(6, TextFrame.BlankPage);

        return menu.Run(
            new MenuLine(chance => SearchOn(c, chance), () => SearchChance(c)),
            // C: line 1 is offered but its action is empty: the menu simply comes back.
            // orig: R067+0x2470 (hint 05b2)
            new MenuLine(_ => { }, OptionHint.Saying(SuccessAssured)),
            // DORMANT: line 2 (opt_flag_2 = 0): a fight with the guards. orig: R067+0x24AA (hint 05f0)
            new MenuLine(_ => Fight(c), OptionHint.Saying(CardTexts.Unknown)),
            // C: line 3 leads to card 0x62, the main loop's end-of-game code.
            // orig: R067+0x24E8 (hint 0646)
            new MenuLine(_ => { c.Screen.Text(0); c.PassHours(0); c.Next = 0x62; }, OptionHint.Saying(SuccessAssured)),
            // DORMANT: lines 4..8 (opt_flag = 0) have empty actions (067e, 06b8, 06f2, 072a, 0764).
            // C: line 6's hint (R067+0x25B4) leaves its chance unset (r.b = leader is overwritten by -2; r.a is a stack leftover); here −1.
            new MenuLine(_ => { }, OptionHint.Saying(SuccessAssured)),
            new MenuLine(_ => { }, OptionHint.Saying(SuccessAssured)),
            new MenuLine(_ => { }, OptionHint.Saying(SuccessAssured)),
            new MenuLine(_ => { }, OptionHint.Saying(SuccessAssured)),
            new MenuLine(_ => { }, OptionHint.Saying(SuccessAssured)),
            new MenuLine(chance => SlipOut(c, chance), () => SlipOutChance(c)));
    }

    private static int Sneaking(CardContext c)
    {
        // C: is_daylight() != 1: the dark adds 45.
        var dark = c.IsDaylight ? 0 : 0x2D;
        // C: the queries in the original's order (the last one leaves best_member and best_value).
        var agility = c.PartyMaxEffectiveAgility();
        var average = c.PartyAverageSkill((int)Skill.Stealth);
        return (c.PartyMaxSkill((int)Skill.Stealth) + (agility + average) + dark) / 3;
    }

    // orig: R067+0x2418
    private static OptionHint SearchChance(CardContext c) => new(Math.Clamp(Sneaking(c), 1, 99), "", OptionHint.WholeParty);

    // orig: R067+0x270C
    private static OptionHint SlipOutChance(CardContext c)
    {
        var chance = Sneaking(c);
        if (chance < 100)
        {
            chance += c.PartyAverageEffectiveAgility() / 2 + (100 - chance) / 2;
        }
        return new OptionHint(Math.Clamp(chance, 1, 99), "", OptionHint.WholeParty);
    }

    /// <summary>Search on: an hour, and the guild's secret if the party looks into the right guild.</summary>
    // orig: R067+0x233A
    private void SearchOn(CardContext c, int chance)
    {
        var roll = c.Random(100);
        if (roll <= chance)
        {
            c.PassHours(1);
            // C: party_x (the guild bit) and the fact value are and-ed as words.
            if ((c.GuildCode & c.Events.FactValue(PoliticsQuest, 8, c.Here)) != 0)
            {
                OverhearTheSecret(c);
            }
            else
            {
                c.Screen.Text(0x0E);
            }
            c.PracticeSkill(Who.Everybody, (int)Skill.Stealth, 7, 10, 0);
            c.Next = Number;
            return;
        }
        Blunder(c, chance, roll, 0);
    }

    /// <summary>Slip out unseen and back to where the party came from.</summary>
    // orig: R067+0x265E
    private static void SlipOut(CardContext c, int chance)
    {
        var roll = c.Random(100);
        if (roll <= chance)
        {
            c.PassHours(1);
            c.Screen.Text(0x11);
            c.PracticeSkill(Who.Everybody, (int)Skill.Stealth, 7, 10, -1);
            c.Next = c.SavedCard;
            return;
        }
        Blunder(c, chance, roll, -1);
    }

    /// <summary>
    /// A clock knocked over: within the better half-chance and with a rope the party escapes by
    /// a window, otherwise the guards come. <paramref name="level"/> is the practice level the
    /// option uses (0 searching, −1 slipping out).
    /// </summary>
    private static void Blunder(CardContext c, int chance, int roll, int level)
    {
        chance += (100 - chance) / 2;
        // C: && stops early: the rope is looked for only within the better chance.
        if (chance >= roll && c.CountItems(Who.Everybody, Rope) != 0)
        {
            c.PassHours(1);
            c.Screen.Text(0x10);
            c.PracticeSkill(Who.Everybody, (int)Skill.Stealth, 0, 10, level);
            c.Next = c.SavedCard;
            return;
        }
        c.Screen.Text(0x0F);
        c.PracticeSkill(Who.Everybody, (int)Skill.Stealth, 0, 10, level);
        Fight(c);
    }

    /// <summary>What the party overhears about the guild's leader (which secret is the city's).</summary>
    // orig: R067+0x21A6
    private static void OverhearTheSecret(CardContext c)
    {
        // C: a negative remainder matches no secret and shows nothing.
        var secret = (c.LocationQuery(c.Here, 0x21) + c.GuildCode) % 7;
        switch (secret)
        {
            case 0:
                c.Screen.Text(5, TextFrame.BlankPage);
                break;
            case 1:
                // C: card_text(7, v_si) with v_si = 1: the frame is the secret number.
                c.Screen.Text(7, TextFrame.BlankPage);
                break;
            case >= 2 and <= 6:
                c.Screen.Text(8 + secret - 2, TextFrame.BlankPage);
                break;
        }
    }

    /// <summary>
    /// DORMANT: nothing calls this handler (and it is not in the option table): the faction's plans
    /// — $Number1 an hour, $Text2 a day — read from the leader's papers or overheard.
    /// </summary>
    // orig: R067+0x20E2
    internal static void ReadTheirPlans(CardContext c)
    {
        c.Variables.Numbers[0] = c.Random(24);
        c.Variables.Texts[1] = c.Random(4) switch
        {
            0 => "today",
            1 => "tomorrow",
            2 => "two days hence",
            _ => "three days hence",
        };
        var side = c.Events.FactValue(PoliticsQuest, 0, c.Here);
        if (side == -1)
        {
            c.Screen.Text(c.State.Date.Day % 2 != 0 ? 0 : 2, TextFrame.BlankPage);
        }
        else if (side == 1)
        {
            c.Screen.Text(c.State.Date.Day % 2 != 0 ? 1 : 3, TextFrame.BlankPage);
        }
    }

    /// <summary>The guards (and their sergeant) fall on the party.</summary>
    // orig: R067+0x221A
    private static void Fight(CardContext c)
    {
        // C: arguments right to left: the sergeant's quality (lore level % 3) is read first, then
        // the guards' number (rand_mod(4)), then their quality (rand_mod(2)).
        var sergeantQuality = c.PartyLoreLevel() % 3 + 1;
        var guards = c.Random(4) + c.PartyLoreLevel() / 3 + 1;
        var guardQuality = c.Random(2) + c.PartyLoreLevel() / 4 + 1;
        switch (c.RunBattle(0x2D, c.GuildCode + c.Here + 1000, 0x0C, guardQuality, guards, 0x16, sergeantQuality, 1, -1, 0, 0))
        {
            case 1:
            case 2:
                c.ReportAway();
                c.AdjustReputation(c.Here, 0, -(c.Random(6) + 4));
                c.Screen.Text(0x12);
                c.PassHours(1);
                c.Next = c.IsDaylight ? 9 : 10;
                break;
            case 0:
                c.Screen.Text(0x12);
                c.PassHours(1);
                c.Next = c.IsDaylight ? 9 : 10;
                break;
            case 3:
            case 4:
                c.Screen.Text(0x13);
                c.AdjustReputation(c.Here, 0, -(c.Random(11) + 9));
                // C: cur_rep (the script's global copy of the reputation) is lowered by a second draw
                // and not written back; the draw is kept, the copy is not modelled.
                _ = c.Random(11) + 9;
                c.PassHours(3);
                c.Next = 0x0D;
                break;
            default:
                // COVERAGE GAP (reachable: result 5 with a member left Away by an earlier flight; no oracle run ended so): other battle results change nothing.
                break;
        }
    }
}
