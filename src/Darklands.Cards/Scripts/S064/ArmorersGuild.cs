using Darklands.Core.Characters;
using Darklands.Core.Events;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Armor00": the armorers' quarter by day. Shop at the street front; and while the
/// city's politics quest (tag 2) runs here: listen for gossip, seek an audience with the
/// guild masters, accuse the guild leader with evidence, or sneak in (by stealth or under
/// cover of a potion's commotion). A near copy of the blacksmiths' card with the armorers'
/// guild number, two journeymen (one of them teaches "Weapon Skills") and, apart from the
/// third potion's explosion, no pictures of its own.
/// </summary>
// orig: R064+0x0D48 (190C:0008) matched; tests: CardSmokeTests
// orig: handler setup R064+0x1C32 (190C:0EF2); unused-line handler R064+0x1CD4 ("screwed up.");
// saint pop-ups R064+0x1DCC/1116/11a0/122a (standard, no saints listed: never offered)
// UNCERTAIN: the card sets the window global DS:EE70 = 0xF where the blacksmiths' card sets 0 after the option flags; it is
// not modelled (it is not read by anything this port reconstructs).
public sealed class ArmorersGuild : Card
{
    /// <summary>The guild's number in the city politics facts and for the next cards (party_x); also the bit its facts use.</summary>
    private const int Guild = 0x10;

    private const int PoliticsQuest = 2;
    private const int FactKind = 0x1C;
    private const int MainPicture = 36;
    private const int CityStreets = 0x17;

    public override int Number => 0x51;

    public override int Run(CardContext c)
    {
        Show(c, MainPicture);
        c.Next = Number;
        // C: opt_flag_9 is not set by this card: it keeps whatever the previous card left. The text has
        // no tenth line and the switch no case 9, so it is offered hidden here (see the dormant line 9).
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2, 4, 5, 6);
        // C: this card tests ev8_at(2, loc) != 1 (the blacksmiths' card uses !): the call yields 0 or 1, so it is the same test.
        // UNCERTAIN: ev8_at (R029, not in the matched C) is taken to return exactly 0 or 1.
        if (!c.Events.QuestAt(PoliticsQuest, c.Here))
        {
            menu[1] = menu[3] = menu[4] = menu[5] = OptionState.Hidden;
        }
        else
        {
            // C: the fact values are tested as (char) & 0x10: only the low byte counts.
            if ((Fact(c, 3) & Guild) != 0)
            {
                menu[1] = OptionState.Shown;
            }
            if ((Fact(c, 4) & Guild) != 0)
            {
                menu[2] = OptionState.Shown;
            }
            if ((Fact(c, 5) & Guild) != 0)
            {
                menu[4] = menu[5] = OptionState.Shown;
            }
            if ((Fact(c, 9) & Guild) != 0)
            {
                menu[3] = OptionState.Enabled;
            }
            if (c.Events.FactValue(PoliticsQuest, 0x17, c.Here) == Guild)
            {
                menu[4] = menu[5] = OptionState.Hidden;
            }
        }
        c.Support[0] = c.SideOf(Guild);
        var standing = c.LocationQuery(c.Here, 0);
        c.SetCardItems(0x5F, 0x65, 0x61, 0x66);
        if (menu[5] == OptionState.Enabled)
        {
            menu[5] = c.ItemsUsable();
        }

        return menu.Run(
            new MenuLine(_ => VisitTheShop(c), OptionHint.Saying(SuccessAssured)),
            new MenuLine(chance => ListenForGossip(c, menu, chance), () => GossipChance(c, standing)),
            new MenuLine(chance => SeekAnAudience(c, menu, chance), () => AudienceChance(c)),
            new MenuLine(chance => AccuseTheGuildLeader(c, ref standing, chance), () => AccusationChance(c)),
            new MenuLine(chance => SneakIn(c, chance), () => SneakChance(c)),
            new MenuLine(chance => SneakInUnderCover(c, chance), OptionHint.Saying(CardTexts.SelectPotion), () => PotionCoverHint(c)),
            // orig: R064+0x1B46, hint R064+0x1B58
            new MenuLine(_ => { c.PassHours(0); c.Next = CityStreets; }, OptionHint.Saying(SuccessAssured)),
            // DORMANT: line 7 (opt_flag_7 = 0): the city entry card after the first text.
            // orig: R064+0x1B90, hint R064+0x1BAE (100%, leader)
            new MenuLine(_ => { c.Screen.Text(0); c.PassHours(0); c.Next = 4; }, () => new OptionHint(100, SuccessAssured, c.Leader)),
            // DORMANT: line 8 (opt_flag_8 = 0): the city entry card. orig: R064+0x1BE8, hint 0eba R064+0x1BFA
            new MenuLine(_ => { c.PassHours(0); c.Next = 4; }, OptionHint.Saying(SuccessAssured)),
            // DORMANT: line 9 has the "screwed up." handler and no action (stale opt_flag_9 only).
            new MenuLine(_ => { }, OptionHint.Saying(CardTexts.ScrewedUp)));
    }

    private static int Fact(CardContext c, int type) => (sbyte)c.Events.FactValue(PoliticsQuest, type, c.Here);

    private static void NoteFact(CardContext c, int type) =>
        c.Events.Update(-2, c.Here, FactKind, type, Guild, PoliticsQuest, EventUpdate.Or);

    private static int StreetCard(CardContext c) => c.IsDaylight ? 0x51 : 0x52;

    // orig: R064+0x1026 (hint R064+0x1058)
    private void VisitTheShop(CardContext c)
    {
        c.OpenShop(3, null, 0xFF, 0x400);
        ShowPicture(c, MainPicture);
        c.PassHours(1);
        c.Next = StreetCard(c);
    }

    // orig: R064+0x11FC
    private static OptionHint GossipChance(CardContext c, int standing)
    {
        var speech = c.Skill(c.Leader, (int)Skill.SpeakCommon) + standing;
        var wits = c.Attribute(c.Leader, (int)Attribute.Intelligence) + c.Attribute(c.Leader, (int)Attribute.Perception);
        return new OptionHint(Math.Clamp(Math.Min(wits, speech), 0, 99), "", c.Leader);
    }

    /// <summary>What the guild members think of the politics, and with luck a secret about their leader.</summary>
    // orig: R064+0x1090
    private static void ListenForGossip(CardContext c, CardMenu menu, int chance)
    {
        c.Variables.Chosen[0] = c.Leader;
        var roll = c.Random(100);
        c.Support[0] = c.SideOf(Guild);
        if (roll < chance)
        {
            chance -= roll;
            // C: chance > roll, so the divisor is at least 1.
            c.PassHours(Math.Min(4, 100 / chance));
            if (c.Events.FactValue(PoliticsQuest, 0x17, c.Here) == Guild)
            {
                // The guild is the heart of the rebellion. (This card shows the text on the picture, not on a blank page.)
                c.Screen.Text(0x16);
                c.Screen.Text(2);
            }
            else if ((Fact(c, 8) & Guild) == 0)
            {
                c.Screen.Text(1, TextFrame.BlankPage);
            }
            else
            {
                // C: % of a possibly negative value keeps its sign; a negative remainder shows no secret.
                var secret = (c.LocationQuery(c.Here, 0x21) + Guild) % 7;
                if (secret is >= 0 and <= 6)
                {
                    c.Screen.Text(0x0D + secret);
                }
                NoteFact(c, 9);
                c.PracticeSkill(c.Leader, (int)Skill.SpeakCommon, 7, 10, 0);
            }
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

    // orig: R064+0x14B4
    private static OptionHint AudienceChance(CardContext c)
    {
        if (c.Events.FactValue(PoliticsQuest, 0x17, c.Here) == Guild && c.Events.FactValue(PoliticsQuest, 0, c.Here) == -1)
        {
            return OptionHint.Say(SuccessAssured);
        }
        var value = c.HereQuery(0) + (c.Skill(c.Leader, (int)Skill.SpeakCommon) + c.Attribute(c.Leader, (int)Attribute.Charisma));
        return new OptionHint(Math.Clamp(value, 1, 99), "", c.Leader);
    }

    /// <summary>
    /// An audience with the masters: during an uprising they send the party to the rebels (or
    /// the loyalists); otherwise a good talk leads to the guild hall. Without the politics quest
    /// the guild offers journeymen for hire.
    /// </summary>
    // orig: R064+0x1260
    private static void SeekAnAudience(CardContext c, CardMenu menu, int chance)
    {
        menu[2] = OptionState.Shown;
        // C: hours_until(5) is called and its result dropped (no effect).
        NoteFact(c, 4);
        if (c.Events.QuestAt(PoliticsQuest, c.Here))
        {
            if (c.Events.FactValue(PoliticsQuest, 0x17, c.Here) == Guild)
            {
                var side = c.Events.FactValue(PoliticsQuest, 0, c.Here);
                if (side == 0)
                {
                    c.GuildCode = 0;
                    c.SavedCard = CityStreets;
                    c.Next = 0x74;
                    return;
                }
                if (side == -1)
                {
                    c.GuildCode = 0;
                    c.SavedCard = CityStreets;
                    c.Next = 0x79;
                    return;
                }
                c.Screen.Text(0x16, TextFrame.BlankPage);
                c.Screen.Text(3, TextFrame.BlankPage);
                c.PassHours(1);
                c.Next = StreetCard(c);
                return;
            }
            // C: && evaluates left to right: the roll is made first, the fact read only on success.
            if (c.Succeeds(chance) && c.Events.FactValue(PoliticsQuest, 0, c.Here) != 0)
            {
                c.PracticeSkill(c.Leader, (int)Skill.SpeakCommon, 1, 6, 0);
                c.PassHours(2);
                c.GuildCode = Guild;
                c.SavedCard = StreetCard(c);
                c.Next = 0x6A;
                return;
            }
            c.PracticeSkill(c.Leader, (int)Skill.SpeakCommon, 0, 6, 0);
            c.Screen.Text(3, TextFrame.BlankPage);
            c.PassHours(Math.Min(Core.Time.TimeOfDay.HoursUntil(c.State.Date.Hour, 19) / 2, 3));
            c.Next = StreetCard(c);
            return;
        }
        OfferJourneymen(c);
    }

    /// <summary>The masters offer two journeymen (hiring events of a week, kinds 0 and 14) for up to 119 farthings a day.</summary>
    private static void OfferJourneymen(CardContext c)
    {
        // C: (unsigned) cur_loc_query(0x21) % 15: the remainder of the unsigned 16-bit value.
        var skill = c.HereQuery(7) + (ushort)c.HereQuery(0x21) % 15 - 5;
        if (!c.EventTypeAtHere(0x28, 0))
        {
            c.Events.Create(-2, c.Here, skill, 0x28, 0, 0x32, 0, 0xA8, 0, 0);
        }
        if (!c.EventTypeAtHere(0x28, 0x0E))
        {
            c.Events.Create(-2, c.Here, skill, 0x28, 0x0E, 0x32, 0, 0xA8, 0, 0);
        }
        c.Variables.Texts[0] = "Weapon Skills";
        // C: unlike the blacksmiths' card, no clamp: just rand_mod(0x78).
        c.Variables.Money[0] = Core.Economy.Money.From(0, 0, c.Random(0x78));
        c.PassHours(1);
        c.Screen.Text(0x17);
        c.Next = CityStreets;
    }

    /// <summary>The evidence that fits the leader's secret: having it doubles the chance.</summary>
    private static readonly int[] EvidenceForSecret = [0x78, 0x79, 0x78, 0x7C, 0x7E, 0x7F, 0x80];

    // orig: R064+0x169C
    private static OptionHint AccusationChance(CardContext c)
    {
        c.Variables.Chosen[0] = c.Leader;
        var value = c.Attribute(c.Leader, (int)Attribute.Charisma) + (c.Skill(c.Leader, (int)Skill.SpeakCommon) << 1) + c.HereQuery(0);
        if ((Fact(c, 8) & Guild) != 0)
        {
            var secret = (c.LocationQuery(c.Here, 0x21) + Guild) % 7;
            if (secret is >= 0 and <= 6 && c.CountItems(Who.Everybody, EvidenceForSecret[secret]) != 0)
            {
                value <<= 1;
            }
        }
        return new OptionHint(Math.Clamp(value, 0, 99), "", OptionHint.WholeParty);
    }

    /// <summary>Lay the evidence before the guild: it may turn the guild's politics and costs the party some standing.</summary>
    // orig: R064+0x155E
    private static void AccuseTheGuildLeader(CardContext c, ref int standing, int chance)
    {
        c.Events.Update(-2, c.Here, FactKind, 8, ~Guild, PoliticsQuest, EventUpdate.And);
        c.Events.Update(-2, c.Here, FactKind, 9, ~Guild, PoliticsQuest, EventUpdate.And);
        var secret = (c.LocationQuery(c.Here, 0x21) + Guild) % 7;
        if (secret is >= 0 and <= 6)
        {
            c.TakeItem(Who.Everybody, EvidenceForSecret[secret]);
        }
        if (c.Succeeds(chance))
        {
            c.PracticeSkill(c.Leader, (int)Skill.SpeakCommon, 7, 10, 0);
            c.Support[0] = c.Random(3) - 1;
            c.SetSide(Guild, c.Support[0]);
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
        NoteFact(c, 4);
    }

    // orig: R064+0x18C8
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
    /// getting in, and a roll up to half the chance as the near miss.
    /// </summary>
    // orig: R064+0x175A
    private static void SneakIn(CardContext c, int chance)
    {
        var roll = c.Random(100);
        // C: hours_until(19) is called and its result dropped.
        NoteFact(c, 5);
        if (chance < roll)
        {
            for (var i = 0; i < c.PartySize; i++)
            {
                if (c.Status(i) == CharacterStatus.Able)
                {
                    // C: practises for everybody (−2) once per able member, with that member's better skill.
                    c.PracticeSkill(Who.Everybody, SneakingSkill(c, i), 1, 10, 0);
                }
            }
            c.Screen.Text(5, TextFrame.BlankPage);
            c.PassHours(1);
            IntoTheGuildHall(c);
            return;
        }
        for (var i = 0; i < c.PartySize; i++)
        {
            if (c.Status(i) == CharacterStatus.Able)
            {
                c.PracticeSkill(Who.Everybody, SneakingSkill(c, i), 0, 10, 0);
            }
        }
        c.Screen.Text(roll * 2 <= chance ? 4 : 0x14, TextFrame.BlankPage);
        c.PassHours(2);
        c.Next = StreetCard(c);
    }

    /// <summary>The potion pop-up's chance: the party's weaker sneaking skill plus 25, 30, 37 or 45 by potion slot.</summary>
    // orig: R064+0x1D0C, 0ffc R064+0x1D3C, 102c R064+0x1D6C, 105c R064+0x1D9C
    private static OptionHint PotionCoverHint(CardContext c)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        int[] bonus = [0x19, 0x1E, 0x25, 0x2D];
        var slot = Math.Clamp(c.MenuSlot, 0, 3);
        return new OptionHint(Math.Clamp(c.PartyMinSkill((int)Skill.Stealth, (int)Skill.Streetwise) + bonus[slot], 0, 99), "", c.MenuMember);
    }

    /// <summary>
    /// A potion's commotion covers the party (the potion is not used up). Chosen from the
    /// option line instead of the pop-up, the chance is −1 and the party always gets in.
    /// </summary>
    // orig: R064+0x18F4 (option hint R064+0x1B0E "Select a person and a potion.")
    private void SneakInUnderCover(CardContext c, int chance)
    {
        var roll = c.Random(100);
        NoteFact(c, 5);
        if (chance < roll)
        {
            ImproveSneaking(c, 16, 4);
            c.Screen.Text(6, TextFrame.BlankPage);
            c.PassHours(1);
            IntoTheGuildHall(c);
            return;
        }
        ImproveSneaking(c, 8, 2);
        if (roll * 2 <= chance)
        {
            switch (c.MenuSlot)
            {
                case 0:
                    c.Screen.Text(7, TextFrame.BlankPage);
                    break;
                case 1:
                    c.Screen.Text(8, TextFrame.BlankPage);
                    break;
                case 2:
                    ShowPicture(c, 19);
                    c.Screen.Text(9, TextFrame.BlankPage);
                    Show(c, MainPicture);
                    break;
                case 3:
                    c.Screen.Text(0x0A, TextFrame.BlankPage);
                    break;
            }
        }
        else
        {
            c.Screen.Text(0x15, TextFrame.BlankPage);
        }
        c.PassHours(1);
        c.Next = StreetCard(c);
    }

    /// <summary>Each able member may improve the better sneaking skill (<paramref name="percent"/>%, by 1..<paramref name="most"/>).</summary>
    private static void ImproveSneaking(CardContext c, int percent, int most)
    {
        for (var i = 0; i < c.PartySize; i++)
        {
            if (c.Status(i) == CharacterStatus.Able)
            {
                var skill = SneakingSkill(c, i);
                if (c.Random(100) <= percent)
                {
                    c.AddSkill(i, skill, c.ImproveRoll(c.Skill(i, skill), 1, most));
                }
            }
        }
    }

    /// <summary>Inside: the rebels' meeting during an uprising (0x5F), else the guild hall (0x6C).</summary>
    private static void IntoTheGuildHall(CardContext c)
    {
        // C: unlike the blacksmiths' card, party_x (the guild code) is left as it was on every way in.
        c.SavedCard = CityStreets;
        c.Next = c.Events.FactValue(PoliticsQuest, 0x17, c.Here) == Guild ? 0x5F : 0x6C;
    }
}
