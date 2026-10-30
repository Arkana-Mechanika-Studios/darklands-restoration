using Darklands.Core.Characters;
using Darklands.Core.Rules;

namespace Darklands.Core.Events;

/// <summary>
/// The expiry pass that runs after every advance of the clock: each event whose end has come
/// runs its handler (when it has <see cref="EventFlags.CallExpiryHandler"/>) and is removed;
/// a quest instead clears its facts, which removes the quest too.
/// </summary>
public static class EventExpiry
{
    // orig: R029+0x3F30 (0E76:3F30) matched; tests: EventLogTests
    // C: after the handler the original reads events[i]->kind again even if the handler freed the slot (a null pointer, not kind 8, then event-release does nothing); here a freed slot is skipped.
    public static void Run(GameState s)
    {
        var log = s.Events;
        for (var i = 0; i < EventLog.Capacity; i++)
        {
            if (!log.HasEnded(i))
            {
                continue;
            }
            if (log[i]!.Flags.HasFlag(EventFlags.CallExpiryHandler))
            {
                Handle(s, log[i]!);
            }
            if (log[i] is not { } e)
            {
                continue;
            }
            if (e.Kind == EventKinds.Quest)
            {
                log.ClearFacts(e.Tag, e.Location);
            }
            else
            {
                log.Free(i);
            }
        }
    }

    /// <summary>Runs an event's expiry handler.</summary>
    // orig: event-expiry-handlers R136+0xAF9F; R029+0x3FB0 (0E76:3FB0) matched; R029+0x4436 (0E76:4436) matched; event-expiry-notice R029+0x4442 (0E76:4442) matched; R029+0x4626 (0E76:4626) matched; tests: PracticeAndItemsTests
    // DORMANT: handler 0 (R029+0x3FB0) is empty; a handler number above 9 would call past the table in the original (nothing creates one).
    public static void Handle(GameState s, TimedEvent e)
    {
        switch (e.ExpiryHandler)
        {
            case ExpiryHandler.AlchemyResult:
                AlchemyResult(s, e);
                break;
            case ExpiryHandler.DailyCityPolitics:
                CityPolitics.DailyPolitics(s, e);
                break;
            case ExpiryHandler.ResolveQuestsHere:
                Quests.ResolveAt(s, e);
                break;
            // orig: R029+0x4436: event-here-test(0x3B, 24).
            case ExpiryHandler.FollowUp:
                s.Events.CreateHere(EventKinds.FollowUp, 24);
                break;
            // orig: event-expiry-notice R029+0x4442: party[type0][0] != 0 (any status but empty).
            case ExpiryHandler.MemberTermEnds:
                if (s.Party.PresentMember(e.Type) is not null)
                {
                    s.Events.Create(-2, s.CurrentLocation, 0, EventKinds.MemberLeft, e.Type, e.Type, 0, EventLog.Forever, 0, 0);
                }
                break;
            case ExpiryHandler.UndoTemporaryBoost:
                UndoBoost(s, e);
                break;
            case ExpiryHandler.SabbatEnds:
                Quests.SabbatEnds(s, e);
                break;
            case ExpiryHandler.SabbatBegins:
                Quests.SabbatBegins(s, e);
                break;
            // orig: R029+0x4626: DS:D843 (the party's terrain) 1 or 2.
            case ExpiryHandler.RenewTravelBlessing:
                if (s.PartyTerrain is 1 or 2)
                {
                    s.Events.Create(-2, 0, 0x63, EventKinds.TravelBlessing, 0, 0, (int)ExpiryHandler.RenewTravelBlessing, 6,
                        (int)EventFlags.CallExpiryHandler, 0);
                }
                break;
        }
    }

    /// <summary>
    /// A brew is done (Owner = the alchemist, Type = the formula, Value = the chance in %,
    /// Extra = potions, Data1/Data2 the mishap's sort and the formula's difficulty). Failure
    /// ("Alchemy Failure"): outside the wilderness, half the time a mishap event follows for
    /// 30 days or 120 days; the Alchemy skill is practised as a failure. Success: the potions,
    /// of quality 10 × grade + the potion's own quality ("Alchemy Success").
    /// </summary>
    // orig: R029+0x3FB2 (0E76:3FB2) matched; tests: PracticeAndItemsTests
    // C: roll-below(4 - f2c) is drawn before roll-below(2), and both before the location-now test.
    // C: the messages are root 0000:24BC(text, 100): a framed box for 100 ticks (R029+0x3FB2, no key waited for).
    // C: the stack takes the item record's type (+1E, a signed char), quality (+26) + 10 x grade and weight (+25).
    public static void AlchemyResult(GameState s, TimedEvent e)
    {
        if (e.Value <= s.Random.Below(100))
        {
            // BUG: the formula's risk works backwards: the explosion (longer = 1, the inn barred 120 days) comes when
            // roll-below(4 - risk) is not 0, so a low-risk formula (0) explodes three times in four and a high-risk one (2) half the time.
            var longer = s.Random.Below(4 - e.Data2) != 0 ? 1 : 0;
            if (s.Random.Below(2) != 0 && s.CurrentLocation != GameState.Wilderness)
            {
                s.Events.Create(-2, s.CurrentLocation, 0x32, EventKinds.AlchemyMishap, e.Data1, longer, 0, longer * 0x870 + 0x2D0, 0, 0);
            }
            s.Messages.Show("Alchemy Failure");
            Practice.PracticeSkill(s, e.Owner, (int)Skill.Alchemy, 0, 3, 0);
            return;
        }
        var code = Items.Potions.FirstPotion + e.Type / 3;
        var potion = s.Item(code);
        var stack = new ItemStack(code, (byte)potion.Type, (byte)(e.Type % 3 * 10 + potion.Quality), (byte)e.Extra, (byte)potion.Weight);
        Inventory.AddStack(s, e.Owner, stack);
        s.Messages.Show("Alchemy Success");
        Practice.PracticeSkill(s, e.Owner, (int)Skill.Alchemy, 1, 3, 0);
    }

    /// <summary>
    /// A temporary boost wears off: attribute boosts and skill boosts are taken back (reported
    /// only with the "saintly changes" option). Potion bonuses (kind 0x4A) are not: an original
    /// bug that makes them permanent. Neither is an event owned by −2 (see
    /// <see cref="MemberRules.RaiseSkillTemporarily"/>): the original's presence test reads the
    /// byte before the member records, which is 0, so those boosts stay for good.
    /// </summary>
    // orig: R029+0x448A (0E76:448A) matched; tests: MemberRulesTests, OriginalMemoryTests
    // C: checked against the asm (12C10-12C18): `shl bx,7; cmp byte [bx-6397h],0` with owner -2 tests DS:9B69, the 2nd name byte of the
    // C: 14th combat potion sub-menu entry (DS:9A47[13], written only by R019+0x0C80's sprintf "q%d" for a list of 14 or more potions
    // C: and never cleared): 0 unless such a menu was built in this run (not modelled), so the undo is skipped.
    // BUG: boosts given to the whole party are never taken back (FixEverybodyBoostsUndone).
    // BUG: the weapon potions' kind 0x4A events (Deadly Blade, Strongedge, Great Power: PotionEffects.Drink) run out here
    // and nothing is undone: only kinds 0x48 and 0x49 are, so the bonus byte (+0x34) keeps them for good.
    // BUG: the recorded amount is taken off whatever the attribute is by then (raise-attribute-by(owner, type, -value)): a boost that a
    // night's rest (Endurance back to its maximum) or wounds (Strength) have already used up is taken off again and leaves the
    // attribute below where it was; a saint's lowering (a negative value) is given back the same way.
    public static void UndoBoost(GameState s, TimedEvent e)
    {
        s.ReportSkillChanges = true;
        if (e.Owner == Who.Everybody ? !s.Behaviour.FixEverybodyBoostsUndone : s.Party.PresentMember(e.Owner) is null)
        {
            return;
        }
        s.ReportSkillChanges = s.ShowSaintlyChanges;
        switch (e.Kind)
        {
            case EventKinds.AttributeBoost:
                MemberRules.AddAttribute(s, e.Owner, e.Type, -e.Value);
                break;
            case EventKinds.SkillBoost:
                MemberRules.AddSkill(s, e.Owner, e.Type, -e.Value);
                break;
        }
        s.ReportSkillChanges = true;
    }
}
