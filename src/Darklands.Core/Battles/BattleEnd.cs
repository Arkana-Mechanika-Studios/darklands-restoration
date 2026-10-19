using Darklands.Core.Characters;

namespace Darklands.Core.Battles;

/// <summary>What the end-of-battle test asks the battle screen to do.</summary>
public interface IBattleEndActions
{
    /// <summary><c>loot_after_fight</c>: the fallen enemies' belongings become loot piles.</summary>
    void LootAfterFight();

    /// <summary><c>loot_pile_open</c>(member): open the loot screen.</summary>
    void OpenLoot(int member);

    /// <summary><c>level_switch</c>: show another level.</summary>
    void SwitchLevel(int floor);

    /// <summary>The screen parts of <c>combat_mode_leave</c>, <c>combat_recover</c>'s placing of the woken and the redraws.</summary>
    void LeftCombatMode();

    /// <summary>Puts a woken member on a free spot near where it fell (<c>combat_recover</c>'s search).</summary>
    void PlaceWokenMember(int member);

    /// <summary>The victory clean-up of the sprites and animations (enemy_anim_free5, cbt_set_anim(i, 5)).</summary>
    void VictoryCleanUp();
}

/// <summary>
/// The end of a battle: the end test run every loop (<c>combat_over</c>), leaving battle mode,
/// recovering afterwards, and the skill improvements for the skills used.
/// </summary>
public static class BattleEnd
{
    /// <summary>Exit floor of a member who left through an exit.</summary>
    public const int LeftThroughExit = -99;

    /// <summary>The end test; true when the battle screen should close.</summary>
    // orig: combat_over R014+0x08FE (0E76:08FE) draft; checked against asm up to R014+0x0A54 (results 4/6/7, enemy floor, fled/left); tests: BattleEndTests
    public static bool IsOver(Battle b, IBattleEndActions actions)
    {
        var party = b.Combatants.Take(Combatant.PartySlots).ToArray();
        if (b.Result is BattleResult.Surrendered or BattleResult.FellIntoPit)
        {
            return true;
        }
        // C: unsigned comparison of the clock words.
        if (b.Result == BattleResult.TimedExit && b.Clock > b.TimedExitTime)
        {
            return true;
        }
        // The floor where the enemies are: that of the first able enemy, else the one shown.
        // C: DS:C651 is a global (the battle file keeps the last value found).
        b.EnemyFloor = b.FloorShown;
        for (var i = Combatant.PartySlots; i < Combatant.Count; i++)
        {
            if (b[i].IsAble)
            {
                b.EnemyFloor = b[i].Floor;
                break;
            }
        }
        var enemyFloor = b.EnemyFloor;
        // Everybody still standing there has left through an exit?
        var nobodyLeft = true;
        var gone = 0;
        foreach (var m in party)
        {
            if (m.Floor == LeftThroughExit)
            {
                gone++;
            }
            else if (m.IsAble && m.Floor == enemyFloor)
            {
                nobodyLeft = false;
            }
        }
        if (nobodyLeft && gone != 0)
        {
            if (b.RealCombat)
            {
                b.Result = BattleResult.Fled;
                b.ResultCard = b.ExitCards.Fled;
            }
            else
            {
                b.Result = BattleResult.LeftBeforeFight;
                b.ResultCard = b.ExitCards.LeftBeforeFight;
            }
            foreach (var m in party)
            {
                if (m.Floor != LeftThroughExit && m.Status is CharacterStatus.Able or CharacterStatus.Unconscious)
                {
                    m.Status = CharacterStatus.Away;
                }
            }
            return true;
        }
        if (b.RealCombat)
        {
            // Nobody standing on the enemies' floor but somebody elsewhere: go there.
            nobodyLeft = true;
            var elsewhere = 0;
            foreach (var m in party)
            {
                if (m.Floor == enemyFloor && m.IsAble)
                {
                    nobodyLeft = false;
                }
                else if (m.Floor != enemyFloor && m.IsAble)
                {
                    elsewhere++;
                }
            }
            if (nobodyLeft && elsewhere != 0)
            {
                foreach (var m in party)
                {
                    if (m.Floor == enemyFloor && m.Status is CharacterStatus.Able or CharacterStatus.Unconscious)
                    {
                        m.Status = CharacterStatus.Away;
                    }
                }
                actions.LootAfterFight();
                var first = Array.FindIndex(party, m => m.IsAble);
                if (first >= 0)
                {
                    b.PartyFloor = party[first].Floor;
                    actions.SwitchLevel(b.PartyFloor);
                    for (var j = Combatant.PartySlots; j < Combatant.Count; j++)
                    {
                        b[j].Status = CharacterStatus.Empty;
                    }
                    ChooseNewLeader(b);
                    LeaveCombatMode(b, actions);
                    Recover(b, actions);
                    return false;
                }
            }
            var noneAble = true;
            var noEnemy = true;
            var noneConscious = true;
            foreach (var m in party)
            {
                if (m.Status != CharacterStatus.Empty && m.Floor != LeftThroughExit && m.Floor == enemyFloor)
                {
                    if (m.IsAble)
                    {
                        noneAble = false;
                        noneConscious = false;
                    }
                    else if (m.Status == CharacterStatus.Unconscious)
                    {
                        noneConscious = false;
                    }
                }
            }
            for (var i = Combatant.PartySlots; i < Combatant.Count; i++)
            {
                if (b[i].IsAble)
                {
                    noEnemy = false;
                    break;
                }
            }
            if (noneAble || noEnemy || noneConscious)
            {
                LeaveCombatMode(b, actions);
                if (noEnemy)
                {
                    b.Result = BattleResult.Won;
                    b.EndTime = (ushort)(b.Clock + 60);
                }
                if (noneConscious)
                {
                    // C: "able and gone through an exit" can never hold here (an able member off the floor
                    // was counted above); the test is kept as written.
                    foreach (var m in party)
                    {
                        if (m.IsAble && m.Floor == LeftThroughExit)
                        {
                            b.Result = BattleResult.Lost;
                        }
                    }
                    if (b.Result != BattleResult.Lost)
                    {
                        b.Result = BattleResult.AllDown;
                        b.EndTime = (ushort)(b.Clock + 240);
                    }
                }
                else if (noneAble)
                {
                    b.Result = BattleResult.Lost;
                    b.EndTime = (ushort)(b.Clock + 240);
                }
            }
        }
        var now = b.Clock;
        if (b.Result == BattleResult.Fled)
        {
            b.ResultCard = b.ExitCards.Fled;
        }
        else if (b.Result == BattleResult.Lost)
        {
            b.ResultCard = b.ExitCards.Lost;
        }
        if (b.Result is BattleResult.Lost or BattleResult.AllDown && b.Clock > b.EndTime)
        {
            return true;
        }
        if (b.Result == BattleResult.Won && b.EndTime < b.Clock)
        {
            for (var i = Combatant.PartySlots; i < Combatant.Count; i++)
            {
                b[i].Status = CharacterStatus.Empty;
            }
            if (b.DemoMode != 0)
            {
                // C: a dragon fight ends with the victory (R014+0x0C7C); for the seven-headed dragon the view is then drawn again
                // (R014+0x0CB6: floor_view_draw(−1), combat_draw), which the screen does every frame here.
                b.LeaveAfterVictory = true;
            }
            foreach (var m in party)
            {
                if (m.Status != CharacterStatus.Empty)
                {
                    m.Action = CombatAction.None;
                }
            }
            actions.VictoryCleanUp();
            ChooseNewLeader(b);
            actions.LootAfterFight();
            Recover(b, actions);
            actions.OpenLoot(b.Leader);
            b.ResultCard = b.ExitCards.Won;
            if (b.LeaveAfterVictory)
            {
                return true;
            }
            if (b.Persistent && !b.LeaveAfterVictory)
            {
                // A persistent battlefield stays open: the party walks out through an exit.
                b.Result = BattleResult.Running;
            }
        }
        // C: unsigned comparison: the battle closes once the end time has passed (and only when not persistent).
        return !(b.Result == BattleResult.Running || b.Persistent || now <= b.EndTime);
    }

    /// <summary>When the leader cannot lead (empty, dead or gone), the first able or unconscious member leads.</summary>
    private static void ChooseNewLeader(Battle b)
    {
        if (b[b.Leader].Status is CharacterStatus.Empty or CharacterStatus.Dead or CharacterStatus.Away)
        {
            for (var i = 0; i < Combatant.PartySlots; i++)
            {
                if (b[i].Status is CharacterStatus.Able or CharacterStatus.Unconscious)
                {
                    b.Leader = b.ActiveMember = i;
                    break;
                }
            }
        }
    }

    /// <summary>Leaves battle mode: no more real combat, the alarm reset.</summary>
    // orig: combat_mode_leave R014+0x0D86 (0E76:0D86) draft
    // DIVERGENCE: the menu states, the cbt_flags bits 13/14 and the collision boxes are the screen's (LeftCombatMode).
    public static void LeaveCombatMode(Battle b, IBattleEndActions actions)
    {
        b.RealCombat = false;
        b.Alarm = 0;
        actions.LeftCombatMode();
    }

    /// <summary>After a fight: damage cleared, endurance back to at least three quarters, the unconscious woken.</summary>
    // orig: combat_recover R014+0x0DEE (0E76:0DEE) draft; tests: BattleEndTests
    // DIVERGENCE: the search for a free spot near where the woken member fell (random_n draws) is the movement layer's (PlaceWokenMember).
    public static void Recover(Battle b, IBattleEndActions actions)
    {
        for (var i = 0; i < Combatant.PartySlots; i++)
        {
            var m = b[i];
            if (m.Status is CharacterStatus.Able or CharacterStatus.Unconscious)
            {
                m.QueuedEndurance = 0;
                m.QueuedStrength = 0;
                // C: maxend * 3 / 4 computed as int, stored in a char.
                var threeQuarters = (sbyte)(m.MaximumEndurance * 3 / 4);
                if (m.Endurance < threeQuarters)
                {
                    m.Endurance = threeQuarters;
                }
            }
            if (m.Status != CharacterStatus.Unconscious)
            {
                continue;
            }
            m.Status = CharacterStatus.Able;
            actions.PlaceWokenMember(i);
        }
    }

    /// <summary>After the battle: each skill used may improve (mode 1 for successful use).</summary>
    // orig: R014+0x0694 (0E76:0694) draft (byte-matched per the draft notes)
    // DIVERGENCE: saving the changed floor and activations and ending the temporary modifiers (DS:A532) belong to the battle screen.
    public static void ImproveSkills(Battle b, Action<int, int, int, int>? changed = null)
    {
        if (b.Result == BattleResult.FellIntoPit)
        {
            return;
        }
        for (var i = 0; i < Combatant.PartySlots; i++)
        {
            var m = b[i];
            if (m.Status is not (CharacterStatus.Able or CharacterStatus.Unconscious))
            {
                continue;
            }
            var used = m.SkillsUsed;
            if (used.HasFlag(SkillUse.HandWeaponHit))
            {
                TryImprove(b, i, b.Tables.SkillCategory(m.DefaultWeapon), 1, changed);
                used &= (SkillUse)0xF6;
            }
            else if (used.HasFlag(SkillUse.HandWeapon))
            {
                TryImprove(b, i, b.Tables.SkillCategory(m.DefaultWeapon), 0, changed);
                used &= (SkillUse)0xFE;
            }
            if (used.HasFlag(SkillUse.MissileHit))
            {
                TryImprove(b, i, b.Tables.SkillCategory(m.ReadiedWeapon), 1, changed);
                used &= (SkillUse)0xED;
            }
            else if (used.HasFlag(SkillUse.Missile))
            {
                TryImprove(b, i, b.Tables.SkillCategory(m.ReadiedWeapon), 0, changed);
                used &= (SkillUse)0xFD;
            }
            if (used.HasFlag(SkillUse.Thrown))
            {
                TryImprove(b, i, (int)Skill.ThrownWeapons, 1, changed);
                used &= (SkillUse)0xFB;
            }
            if (used.HasFlag(SkillUse.ArtificeSucceeded))
            {
                TryImprove(b, i, (int)Skill.Artifice, 1, changed);
                used &= (SkillUse)0x9F;
            }
            else if (used.HasFlag(SkillUse.ArtificeTried))
            {
                TryImprove(b, i, (int)Skill.Artifice, 0, changed);
                used &= (SkillUse)0xBF;
            }
            m.SkillsUsed = used;
        }
    }

    /// <summary>
    /// A chance of (100 − skill)/2 (mode 1) or /4 (mode 0) in 100 to raise a skill below 99 by
    /// 1..3/4/5 (by difficulty); <paramref name="changed"/> gets (member, skill, from, to).
    /// </summary>
    // orig: skill_try_improve R021+0x0000 (1EF8:0000) matched; skill_change_msg R021+0x0080 (1EF8:0080) matched
    // C: the message at the member's row is shown through changed (TacticalBattle.AfterBattle: ITacticalView.MemberMessage).
    public static void TryImprove(Battle b, int member, int skill, int mode, Action<int, int, int, int>? changed)
    {
        var c = b[member];
        int v = c.Skills[skill];
        var chance = mode == 1 ? (100 - v) / 2 : (100 - v) / 4;
        if (v < 99 && b.RollScaled(100) <= chance)
        {
            var amount = b.RollOneTo(b.Tables.SkillGainDie(b.Difficulty)) + b.Tables.SkillGainAddition(b.Difficulty);
            if (amount == 0)
            {
                return;
            }
            var from = c.Skills[skill];
            // C: the char is incremented in place (wrapping) and then clamped to 0..99.
            c.Skills[skill] = (sbyte)Math.Clamp((int)(sbyte)(from + amount), 0, 99);
            changed?.Invoke(member, skill, from, c.Skills[skill]);
        }
    }
}
