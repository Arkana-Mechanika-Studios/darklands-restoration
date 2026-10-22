using Darklands.Core.Characters;
using Darklands.Core.Events;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$jewel00": the jewelers' quarter by day. Unlike the other guild cards it has no shop and no
/// journeyman: while the city's politics quest (tag 2) runs here, listen for gossip (five
/// secrets), request an audience (it always leads to the guild's hall on success), accuse the
/// guild leader (no evidence is needed or spent), or sneak in (by stealth or under cover of a
/// potion's commotion).
/// </summary>
// orig: R067+0x031E (1869:000E) matched; tests: CardSmokeTests
// orig: handler setup R067+0x0E02 (1869:0AF2); unused-line handler R067+0x0EA4 ("screwed up.");
// saint pop-ups R067+0x0F9C/0d16/0da0/0e2a (standard; the card lists saints 0, 0x59, 0x61 and 0x80 but no line offers them)
public sealed class JewelersGuild : Card
{
    /// <summary>The guild's number in the city politics facts and for the next cards (party_x); also the bit its facts use.</summary>
    private const int Guild = 0x80;

    private const int PoliticsQuest = 2;
    private const int FactKind = 0x1C;
    private const int MainPicture = 1;
    private const int CityStreets = 0x17;

    public override int Number => 0x5D;

    public override int Run(CardContext c)
    {
        Show(c, MainPicture);
        c.Next = Number;
        // C: opt_flag_9 is not set by this card: it keeps whatever the previous card left. The text has
        // no tenth line and the switch no case 9, so it is offered hidden here (see the dormant line 9).
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2, 4, 5, 6);
        if (!c.Events.QuestAt(PoliticsQuest, c.Here))
        {
            // C: unlike the other guild cards the audience line is hidden as well.
            menu[1] = menu[2] = menu[3] = menu[4] = menu[5] = OptionState.Hidden;
        }
        else
        {
            // C: the fact values are tested as (char) & -0x80 (0xFF80 once widened): the sign-extended byte has its
            // upper bits set exactly when bit 7 is, so it is the test of bit 7.
            if ((Fact(c, 3) & -0x80) != 0)
            {
                menu[1] = OptionState.Shown;
            }
            if ((Fact(c, 4) & -0x80) != 0)
            {
                menu[2] = OptionState.Shown;
            }
            if ((Fact(c, 5) & -0x80) != 0)
            {
                menu[4] = menu[5] = OptionState.Shown;
            }
            if ((Fact(c, 9) & -0x80) != 0)
            {
                menu[3] = OptionState.Enabled;
            }
            if (c.Events.FactValue(PoliticsQuest, 0x17, c.Here) == Guild)
            {
                menu[4] = menu[5] = OptionState.Hidden;
            }
        }
        // C: no var_support0 = loc_side_get(..) on this card.
        var standing = c.LocationQuery(c.Here, 0);
        // The card lists saints as well, but offers only the potion pop-up.
        c.SetCardItems(0x5F, 0x65, 0x61, 0x66);
        c.SetCardSaints(0, 0x59, 0x61, 0x80);
        if (menu[5] == OptionState.Enabled)
        {
            menu[5] = c.ItemsUsable();
        }

        return menu.Run(
            // orig: R067+0x0604 (hint R067+0x0622): no shop here, only the time passes.
            new MenuLine(_ => { c.PassHours(1); c.Next = StreetCard(c); }, OptionHint.Saying(SuccessAssured)),
            new MenuLine(chance => ListenForGossip(c, menu, chance), () => GossipChance(c, standing)),
            new MenuLine(chance => RequestAnAudience(c, menu, chance), () => AudienceChance(c, standing)),
            new MenuLine(chance => AccuseTheGuildLeader(c, ref standing, chance), () => AccusationChance(c, standing)),
            new MenuLine(chance => SneakIn(c, chance), () => SneakChance(c)),
            new MenuLine(chance => SneakInUnderCover(c, chance), OptionHint.Saying(CardTexts.SelectPotion), () => PotionCoverHint(c)),
            // orig: R067+0x0D0A, hint R067+0x0D28: shown at the start, so it is offered.
            new MenuLine(_ => { c.Screen.Text(0); c.PassHours(0); c.Next = 4; }, OptionHint.Saying(SuccessAssured)),
            // DORMANT: line 7 (opt_flag_7 = 0): the same as line 6.
            // orig: R067+0x0D60, hint R067+0x0D7E (100%, leader)
            new MenuLine(_ => { c.Screen.Text(0); c.PassHours(0); c.Next = 4; }, () => new OptionHint(100, SuccessAssured, c.Leader)),
            // DORMANT: line 8 (opt_flag_8 = 0): back to the streets. orig: R067+0x0DB8, hint 0aba R067+0x0DCA
            new MenuLine(_ => { c.PassHours(0); c.Next = CityStreets; }, OptionHint.Saying(SuccessAssured)),
            // DORMANT: line 9 has the "screwed up." handler and no action (stale opt_flag_9 only).
            new MenuLine(_ => { }, OptionHint.Saying(CardTexts.ScrewedUp)));
    }

    private static int Fact(CardContext c, int type) => (sbyte)c.Events.FactValue(PoliticsQuest, type, c.Here);

    private static void NoteFact(CardContext c, int type) =>
        c.Events.Update(-2, c.Here, FactKind, type, Guild, PoliticsQuest, EventUpdate.Or);

    private static int StreetCard(CardContext c) => c.IsDaylight ? 0x5D : 0x5E;

    // orig: R067+0x0766
    private static OptionHint GossipChance(CardContext c, int standing)
    {
        var speech = c.Skill(c.Leader, (int)Skill.SpeakCommon) + standing;
        var wits = c.Attribute(c.Leader, (int)Attribute.Intelligence) + c.Attribute(c.Leader, (int)Attribute.Perception);
        return new OptionHint(Math.Clamp(Math.Min(wits, speech), 0, 99), "", c.Leader);
    }

    /// <summary>With luck, one of five secrets about the guild's leader.</summary>
    // orig: R067+0x065A
    private static void ListenForGossip(CardContext c, CardMenu menu, int chance)
    {
        c.Variables.Chosen[0] = c.Leader;
        var roll = c.Random(100);
        if (roll < chance)
        {
            chance -= roll;
            // C: chance > roll, so the divisor is at least 1.
            c.PassHours(Math.Min(4, 100 / chance));
            // C: % of a possibly negative value keeps its sign; a negative remainder shows no secret.
            var secret = (c.LocationQuery(c.Here, 0x21) + Guild) % 5;
            if (secret is >= 0 and <= 4)
            {
                c.Screen.Text(0x0D + secret, TextFrame.BlankPage);
            }
            NoteFact(c, 9);
            c.PracticeSkill(c.Leader, (int)Skill.SpeakCommon, 7, 10, 0);
        }
        else
        {
            c.PracticeSkill(c.Leader, (int)Skill.SpeakCommon, 0, 10, 0);
            c.PassHours(2);
            c.Screen.Text(1, TextFrame.BlankPage);
        }
        menu[1] = OptionState.Shown;
        NoteFact(c, 3);
    }

    // orig: R067+0x0898
    private static OptionHint AudienceChance(CardContext c, int standing)
    {
        var value = c.Skill(c.Leader, (int)Skill.SpeakCommon) + c.Attribute(c.Leader, (int)Attribute.Charisma) + standing;
        return new OptionHint(Math.Clamp(value, 0, 99), "", c.Leader);
    }

    /// <summary>
    /// An audience with the masters: with a successful talk (a roll below the chance) the party
    /// is led to the guild hall; otherwise the guild turns the party away and the card goes on.
    /// </summary>
    // orig: R067+0x07CA
    // C: the function has no return statement on the failure path (the value is not used by the menu loop).
    private static void RequestAnAudience(CardContext c, CardMenu menu, int chance)
    {
        c.Variables.Chosen[0] = c.Leader;
        menu[2] = OptionState.Shown;
        // C: hours_until(5) (R029+0x577A) is called and its result dropped (no effect).
        NoteFact(c, 4);
        // C: a roll strictly below the chance (the other guild cards accept an equal roll).
        if (c.Random(100) < chance)
        {
            c.PracticeSkill(c.Leader, (int)Skill.SpeakCommon, 1, 6, 0);
            c.PassHours(2);
            c.SavedCard = StreetCard(c);
            c.GuildCode = Guild;
            c.Next = 0x6A;
            return;
        }
        c.PracticeSkill(c.Leader, (int)Skill.SpeakCommon, 0, 6, 0);
        c.Screen.Text(3, TextFrame.BlankPage);
        // C: next_card is not changed: the card stays.
        c.PassHours(Math.Min(Core.Time.TimeOfDay.HoursUntil(c.State.Date.Hour, 19) / 2, 3));
    }

    // orig: R067+0x09B4
    private static OptionHint AccusationChance(CardContext c, int standing)
    {
        c.Variables.Chosen[0] = c.Leader;
        var value = c.Attribute(c.Leader, (int)Attribute.Charisma) + c.Skill(c.Leader, (int)Skill.SpeakCommon) * 2 + standing;
        return new OptionHint(Math.Clamp(value, 0, 99), "", OptionHint.WholeParty);
    }

    /// <summary>
    /// Accuse the guild leader (nothing is taken from the party and the guild's side does not
    /// change): a success lowers the party's standing; a failure forgets the leader's secret.
    /// </summary>
    // orig: R067+0x08DE
    private static void AccuseTheGuildLeader(CardContext c, ref int standing, int chance)
    {
        if (c.Succeeds(chance))
        {
            c.PracticeSkill(c.Leader, (int)Skill.SpeakCommon, 7, 10, 0);
            // C: rand_mod(9) is drawn and its result dropped.
            c.Random(9);
            c.Screen.Text(0x0B);
            // C: cur_rep is the card's copy of the reputation, read at the start: it is lowered and written back.
            standing--;
            c.LocationSet(c.Here, 0, standing);
            c.PassHours(3);
            c.Next = CityStreets;
            return;
        }
        c.PracticeSkill(c.Leader, (int)Skill.SpeakCommon, 0, 10, 0);
        c.Screen.Text(0x0C);
        c.PassHours(3);
        c.Next = CityStreets;
        // Only a failed accusation clears the secret facts (8 and 9) and notes the audience (4).
        c.Events.Update(-2, c.Here, FactKind, 8, ~Guild, PoliticsQuest, EventUpdate.And);
        c.Events.Update(-2, c.Here, FactKind, 9, ~Guild, PoliticsQuest, EventUpdate.And);
        NoteFact(c, 4);
    }

    // orig: R067+0x0B1A
    private static OptionHint SneakChance(CardContext c)
    {
        var stealth = c.PartyMinSkill((int)Skill.Stealth);
        var streetwise = c.PartyMinSkill((int)Skill.Streetwise);
        return new OptionHint(streetwise > stealth ? stealth : streetwise, "", OptionHint.WholeParty);
    }

    /// <summary>The better of Stealth and Streetwise is what each member practises (Stealth on a tie).</summary>
    private static int SneakingSkill(CardContext c, int member) =>
        c.Skill(member, (int)Skill.Streetwise) <= c.Skill(member, (int)Skill.Stealth) ? (int)Skill.Stealth : (int)Skill.Streetwise;

    /// <summary>
    /// Sneak in by a side door. The original counts a roll <em>above</em> the chance as
    /// getting in; any miss is the same near miss.
    /// </summary>
    // orig: R067+0x09F6
    private static void SneakIn(CardContext c, int chance)
    {
        var roll = c.Random(100);
        // C: hours_until(19) (R029+0x577A) is called and its result dropped.
        NoteFact(c, 5);
        if (roll > chance)
        {
            // C: every slot of the party practises, whatever its member's state (no check of the status byte).
            for (var i = 0; i < c.PartySize; i++)
            {
                c.PracticeSkill(Who.Everybody, SneakingSkill(c, i), 1, 10, 0);
            }
            c.Screen.Text(5, TextFrame.BlankPage);
            c.PassHours(1);
            c.SavedCard = StreetCard(c);
            c.Next = 0x6C;
            return;
        }
        for (var i = 0; i < c.PartySize; i++)
        {
            c.PracticeSkill(Who.Everybody, SneakingSkill(c, i), 0, 10, 0);
        }
        c.Screen.Text(4, TextFrame.BlankPage);
        c.PassHours(2);
        c.Next = StreetCard(c);
    }

    /// <summary>The potion pop-up's chance: the party's weaker sneaking skill plus 25, 30, 37 or 45 by potion slot.</summary>
    // orig: R067+0x0EDC, 0bfc R067+0x0F0C, 0c2c R067+0x0F3C, 0c5c R067+0x0F6C
    private static OptionHint PotionCoverHint(CardContext c)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        int[] bonus = [0x19, 0x1E, 0x25, 0x2D];
        var slot = Math.Clamp(c.MenuSlot, 0, 3);
        return new OptionHint(Math.Clamp(c.PartyMinSkill((int)Skill.Stealth, (int)Skill.Streetwise) + bonus[slot], 0, 99), "", c.MenuMember);
    }

    /// <summary>
    /// A potion's commotion covers the party (the potion is not used up). Chosen from the
    /// option line instead of the pop-up, the chance is −1 and the party always gets in. A miss
    /// shows the text of the potion used.
    /// </summary>
    // orig: R067+0x0B46 (option hint R067+0x0CD2 "Select a person and a potion.")
    private static void SneakInUnderCover(CardContext c, int chance)
    {
        var roll = c.Random(100);
        // C: hours_until(19) (R029+0x577A) is called and its result dropped.
        NoteFact(c, 5);
        if (roll > chance)
        {
            ImproveSneaking(c, 16, 4);
            c.Screen.Text(6, TextFrame.BlankPage);
            c.PassHours(1);
            c.SavedCard = StreetCard(c);
            c.Next = 0x6C;
            return;
        }
        ImproveSneaking(c, 8, 2);
        switch (c.MenuSlot)
        {
            case 0:
                c.Screen.Text(7, TextFrame.BlankPage);
                break;
            case 1:
                c.Screen.Text(8, TextFrame.BlankPage);
                break;
            case 2:
                c.Screen.Text(9, TextFrame.BlankPage);
                break;
            case 3:
                c.Screen.Text(0x0A, TextFrame.BlankPage);
                break;
        }
        c.PassHours(1);
        c.Next = StreetCard(c);
    }

    /// <summary>
    /// Every party slot may improve the better sneaking skill (<paramref name="percent"/>%, by
    /// 1..<paramref name="most"/>); the original does not look at the member's status here either.
    /// </summary>
    private static void ImproveSneaking(CardContext c, int percent, int most)
    {
        for (var i = 0; i < c.PartySize; i++)
        {
            var skill = SneakingSkill(c, i);
            if (c.Random(100) <= percent)
            {
                c.AddSkill(i, skill, c.ImproveRoll(c.Skill(i, skill), 1, most));
            }
        }
    }
}
