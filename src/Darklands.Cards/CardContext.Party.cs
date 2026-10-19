using Darklands.Core.Characters;
using Darklands.Core.Rules;

namespace Darklands.Cards;

/// <summary>
/// Party members for card scripts. <c>who</c>: member slot 0..4, <see cref="Who.Leader"/> (−1)
/// or <see cref="Who.Everybody"/> (−2) where the original allows it. Skills and attributes
/// by the game's numbers (cast <see cref="Core.Characters.Skill"/> / <see cref="Core.Characters.Attribute"/>).
/// </summary>
public sealed partial class CardContext
{
    /// <summary>The leader's slot (<c>leader</c>).</summary>
    // orig: leader DS:907B
    public int Leader => State.Party.LeaderIndex;

    /// <summary>The number of slots in use (<c>party_size</c>).</summary>
    // orig: party_size DS:A67E
    // C: add_member sets party_size to the highest occupied slot + 1; here the length of the slot list.
    public int PartySize => State.Party.Members.Count;

    /// <summary>A member's status (<c>party[who][0]</c>).</summary>
    public CharacterStatus Status(int who) => State.Party.Slot(who).Status;

    public void SetStatus(int who, CharacterStatus status) => State.Party.Slot(who).Status = status;

    /// <summary><c>member_name</c>.</summary>
    // orig: member_name R029+0x0000 (0E76:0000) matched
    public string MemberName(int who) => State.Party.Slot(who).Name;

    /// <summary><c>member_short_name</c>.</summary>
    // orig: member_short_name R029+0x003E (0E76:003E) matched
    public string MemberShortName(int who) => State.Party.Slot(who).ShortName;

    /// <summary><c>member_age</c> (years).</summary>
    // orig: member_age R029+0x00A2 (0E76:00A2) matched
    // UNCERTAIN: member_age returns the word before the record / 365 (unsigned, checked against the asm), while the monthly update and add_member treat that word as years; here the years.
    // DORMANT: no card calls member_age.
    public int MemberAge(int who) => State.Party.Slot(who).Age;

    /// <summary>The member is a woman (record +3).</summary>
    public bool IsFemale(int who) => State.Party.Slot(who).IsFemale;

    // ---- skills and attributes ------------------------------------------------------

    /// <summary><c>member_skill</c>.</summary>
    // orig: member_skill R029+0x01A0 (0E76:01A0) matched
    public int Skill(int who, int skill) => MemberRules.Skill(State, who, skill);

    /// <summary><c>set_skill</c> (0..99).</summary>
    // orig: set_skill R029+0x02B8 (0E76:02B8) matched
    public void SetSkill(int who, int skill, int value) => MemberRules.SetSkill(State, who, skill, value);

    /// <summary><c>add_skill</c> (0..99, with the change report).</summary>
    // orig: add_skill R029+0x02EA (0E76:02EA) matched
    public void AddSkill(int who, int skill, int change) => MemberRules.AddSkill(State, who, skill, change);

    /// <summary><c>member_attr</c>.</summary>
    // orig: member_attr R029+0x05EE (0E76:05EE) matched
    public int Attribute(int who, int attribute) => MemberRules.Attribute(State, who, attribute);

    /// <summary><c>set_attr</c>.</summary>
    // orig: set_attr R029+0x0988 (0E76:0988) matched
    public void SetAttribute(int who, int attribute, int value) => MemberRules.SetAttribute(State, who, attribute, value);

    /// <summary><c>add_attr</c> (1..99, at most maximum + boost).</summary>
    // orig: add_attr R029+0x0A72 (0E76:0A72) matched
    public void AddAttribute(int who, int attribute, int change) => MemberRules.AddAttribute(State, who, attribute, change);

    /// <summary><c>member_attr_max</c>.</summary>
    // orig: member_attr_max R029+0x0B64 (0E76:0B64) matched
    public int AttributeMaximum(int who, int attribute) => MemberRules.AttributeMaximum(State, who, attribute);

    /// <summary><c>set_attr_max</c> (unclamped: original bug).</summary>
    // orig: set_attr_max R029+0x0B84 (0E76:0B84) matched
    public void SetAttributeMaximum(int who, int attribute, int value) => MemberRules.SetAttributeMaximum(State, who, attribute, value);

    /// <summary><c>add_attr_max</c> (1..99).</summary>
    // orig: add_attr_max R029+0x0BB6 (0E76:0BB6) matched
    public void AddAttributeMaximum(int who, int attribute, int change) => MemberRules.AddAttributeMaximum(State, who, attribute, change);

    /// <summary><c>heal_attr_pct</c>.</summary>
    // orig: heal_attr_pct R029+0x08F2 (0E76:08F2) matched
    public void HealAttribute(int who, int attribute, int percent) => MemberRules.HealAttribute(State, who, attribute, percent);

    /// <summary><c>member_best_skill</c>: the best weapon skill (0..6).</summary>
    // orig: member_best_skill R029+0x01C0 (0E76:01C0) matched
    public int BestWeaponSkill(int who) => MemberRules.BestWeaponSkill(State, who);

    /// <summary><c>member_fatigue</c>: the equipment weight carried.</summary>
    // orig: member_fatigue R029+0x00C6 (0E76:00C6) matched
    public int Encumbrance(int who) => MemberRules.Encumbrance(State, who);

    /// <summary><c>member_eff_attr2</c>: Agility as burden allows.</summary>
    // orig: member_eff_attr2 R029+0x06DE (0E76:06DE) matched
    // DIVERGENCE: -1 is resolved before the call; the original reads the Agility byte of record -1 (see MemberRules.EffectiveAgility).
    public int EffectiveAgility(int who) => MemberRules.EffectiveAgility(State, State.Party.Resolve(who));

    // ---- temporary boosts, injuries, practice ---------------------------------------

    /// <summary><c>raise_attr_temp</c>.</summary>
    // orig: raise_attr_temp R029+0x4C04 (0E76:4C04) matched
    public void RaiseAttributeTemporarily(int who, int attribute, int points, int hours) =>
        MemberRules.RaiseAttributeTemporarily(State, who, attribute, points, hours);

    /// <summary>the reference's misnamed <c>lower_skill_temp</c>: raises a skill for a while.</summary>
    // orig: lower_skill_temp R029+0x4B08 (0E76:4B08) draft
    public void RaiseSkillTemporarily(int who, int skill, int points, int hours) =>
        MemberRules.RaiseSkillTemporarily(State, who, skill, points, hours);

    /// <summary>A saint's boost of the equipped weapons' skills for a day.</summary>
    // orig: equip_bonuses R029+0x0792 (0E76:0792) matched
    public void BoostEquippedWeaponSkills(int who, int points) => MemberRules.BoostEquippedWeaponSkills(State, who, points);

    /// <summary><c>injure</c>(who, min Strength loss, min Endurance loss, multiplier).</summary>
    // orig: injure_impl R039+0x026A (1462:026A) matched
    public void Injure(int who, int minStrength, int minEndurance, int multiplier) =>
        MemberRules.Injure(State, who, minStrength, minEndurance, multiplier);

    /// <summary><c>practice_skill</c>(who, skill, mode 1 success / 0 failure, percent, level).</summary>
    // orig: practice_skill_impl R039+0x0132 (1462:0132) matched
    public void PracticeSkill(int who, int skill, int mode, int percent, int level) =>
        Practice.PracticeSkill(State, who, skill, mode, percent, level);

    /// <summary><c>improve_roll</c>.</summary>
    // orig: improve_roll R029+0x18A8 (0E76:18A8) matched
    public int ImproveRoll(int value, int low, int high) => Practice.ImproveRoll(State, value, low, high);

    /// <summary><c>give_reward</c>: fame of size 0..5; returns the new fame.</summary>
    // orig: give_reward_impl R039+0x1E06 (1462:1E06) matched
    public int GiveReward(int size) => Practice.GiveReward(State, size);

    /// <summary>The fame cost (R039 1e90, with its original bug).</summary>
    // orig: R039+0x1E90 (1462:1E90) matched
    public int ChargeFame(int size) => Practice.ChargeFame(State, size);

    // ---- party queries --------------------------------------------------------------

    /// <summary>The member found by the last party query (<c>best_member</c>).</summary>
    // orig: best_member DS:991D
    public int BestMember => State.Party.Last.Member;

    /// <summary>The value found by the last skill query (<c>best_value</c>).</summary>
    // orig: best_value DS:99E6
    public int BestValue => State.Party.Last.Value;

    /// <summary>The skill found by the last best-skill query (<c>best_skill</c>).</summary>
    // orig: best_skill DS:99EB
    public int BestSkillFound => State.Party.Last.Skill;

    /// <summary>The value found by the last attribute query.</summary>
    // orig: attribute_value DS:9C1D
    public int BestAttributeValue => State.Party.Last.AttributeValue;

    /// <summary>The member found by the last min/max attribute query (DS:9A44).</summary>
    // orig: DS:9A44; tests: Oracle:rules.json (attribute queries); FidelityFixTests
    public int AttributeMember => State.Party.Last.AttributeMember;

    /// <summary><c>party_min_skill</c>.</summary>
    // orig: party_min_skill R029+0x1396 (0E76:1396) matched
    public int PartyMinSkill(int skill) => PartyQueries.MinSkill(State, skill);

    /// <summary><c>party_min_skill2</c>.</summary>
    // orig: party_min_skill2 R029+0x13DE (0E76:13DE) matched
    public int PartyMinSkill(int a, int b) => PartyQueries.MinSkill(State, a, b);

    /// <summary><c>party_min_skill_sum</c>.</summary>
    // orig: party_min_skill_sum R029+0x1446 (0E76:1446) draft
    public int PartyMinSkillSum(int a, int b) => PartyQueries.MinSkillSum(State, a, b);

    /// <summary><c>party_max_skill</c>.</summary>
    // orig: party_max_skill R029+0x14A4 (0E76:14A4) matched
    public int PartyMaxSkill(int skill) => PartyQueries.MaxSkill(State, skill);

    /// <summary><c>party_max_skill2</c>.</summary>
    // orig: party_max_skill2 R029+0x153A (0E76:153A) matched
    public int PartyMaxSkill(int a, int b) => PartyQueries.MaxSkill(State, a, b);

    /// <summary><c>party_max_skill_sum</c>.</summary>
    // orig: party_max_skill_sum R029+0x15A2 (0E76:15A2) matched
    public int PartyMaxSkillSum(int a, int b) => PartyQueries.MaxSkillSum(State, a, b);

    /// <summary><c>party_avg_skill</c>.</summary>
    // orig: party_avg_skill R029+0x1600 (0E76:1600) matched
    public int PartyAverageSkill(int skill) => PartyQueries.AverageSkill(State, skill);

    /// <summary>The best skill among able women (R029 14ec).</summary>
    // orig: R029+0x14EC (0E76:14EC) matched
    public int PartyMaxSkillOfWomen(int skill) => PartyQueries.MaxSkillOfWomen(State, skill);

    /// <summary><c>party_best_skill_lo</c>.</summary>
    // orig: party_best_skill_lo R029+0x021E (0E76:021E) matched
    public int PartyBestHandWeaponSkill() => PartyQueries.BestHandWeaponSkill(State);

    /// <summary><c>party_best_skill_hi</c>.</summary>
    // orig: party_best_skill_hi R029+0x026A (0E76:026A) matched
    public int PartyBestMissileSkill() => PartyQueries.BestMissileSkill(State);

    /// <summary><c>party_min_attr</c>.</summary>
    // orig: party_min_attr R029+0x164A (0E76:164A) matched
    public int PartyMinAttribute(int attribute) => PartyQueries.MinAttribute(State, attribute);

    /// <summary>R029 1696 (records 2 × a: original bug).</summary>
    // orig: R029+0x1696 (0E76:1696) matched
    public int PartyMinAttributeSum(int a, int b) => PartyQueries.MinAttributeSum(State, a, b);

    /// <summary><c>party_max_attr</c>.</summary>
    // orig: party_max_attr R029+0x16FE (0E76:16FE) matched
    public int PartyMaxAttribute(int attribute) => PartyQueries.MaxAttribute(State, attribute);

    /// <summary><c>party_max_attr(0x0F)</c>, which reads skill 1.</summary>
    // orig: party_max_attr R029+0x16FE (0E76:16FE) matched (with 0x0F)
    public int PartyMaxAttributeAtSkillOne() => PartyQueries.MaxAttributeAtSkillOne(State);

    /// <summary><c>party_max_attr_sum</c> (records 2 × a: original bug).</summary>
    // orig: party_max_attr_sum R029+0x179C (0E76:179C) matched
    public int PartyMaxAttributeSum(int a, int b) => PartyQueries.MaxAttributeSum(State, a, b);

    /// <summary><c>party_avg_attr</c>.</summary>
    // orig: party_avg_attr R029+0x1800 (0E76:1800) matched
    public int PartyAverageAttribute(int attribute) => PartyQueries.AverageAttribute(State, attribute);

    /// <summary>The best attribute among present women (R029 174a).</summary>
    // orig: R029+0x174A (0E76:174A) matched
    public int PartyMaxAttributeOfWomen(int attribute) => PartyQueries.MaxAttributeOfWomen(State, attribute);

    /// <summary>The best attribute + skill (R029 184a).</summary>
    // orig: R029+0x184A (0E76:184A) matched
    public int PartyMaxAttributePlusSkill(int attribute, int skill) => PartyQueries.MaxAttributePlusSkill(State, attribute, skill);

    /// <summary><c>party_avg_eff_attr2</c>.</summary>
    // orig: party_avg_eff_attr2 R029+0x060E (0E76:060E) matched
    public int PartyAverageEffectiveAgility() => PartyQueries.AverageEffectiveAgility(State);

    /// <summary><c>party_min_eff_attr2</c>.</summary>
    // orig: party_min_eff_attr2 R029+0x0656 (0E76:0656) matched
    public int PartyMinEffectiveAgility() => PartyQueries.MinEffectiveAgility(State);

    /// <summary><c>party_max_eff_attr2</c>.</summary>
    // orig: party_max_eff_attr2 R029+0x0690 (0E76:0690) matched
    public int PartyMaxEffectiveAgility() => PartyQueries.MaxEffectiveAgility(State);

    /// <summary><c>party_max_eff_attr2_bonus</c>.</summary>
    // orig: party_max_eff_attr2_bonus R029+0x072E (0E76:072E) matched
    public int PartyMaxMountedAgility() => PartyQueries.MaxMountedAgility(State);

    /// <summary><c>party_min_eff_attr2_bonus</c>.</summary>
    // orig: party_min_eff_attr2_bonus R029+0x088E (0E76:088E) matched
    public int PartyMinMountedAgility() => PartyQueries.MinMountedAgility(State);

    /// <summary>the reference's <c>party_count_able</c>: able <em>women</em>.</summary>
    // orig: party_count_able R029+0x007C (0E76:007C) matched
    public int AbleWomen() => PartyQueries.AbleWomen(State);

    /// <summary><c>party_present_count</c>.</summary>
    // orig: party_present_count_impl R039+0x1DE6 (1462:1DE6) matched
    public int PresentCount() => PartyQueries.PresentCount(State);

    /// <summary><c>party_all_ok</c>.</summary>
    // orig: party_all_ok R029+0x1972 (0E76:1972) matched
    public bool PartyAllWell() => PartyQueries.AllWell(State);

    /// <summary><c>party_get</c>: 4 fame, 5 all mounted, 6 philosopher's stone.</summary>
    // orig: party_get R029+0x1326 (0E76:1326) matched
    public int PartyGet(int what) => PartyQueries.Get(State, what);

    /// <summary><c>party_set</c>: 4 fame.</summary>
    // orig: party_set R029+0x1382 (0E76:1382) matched
    public void PartySet(int what, int value) => PartyQueries.Set(State, what, value);

    /// <summary><c>party_lore_level</c> (1..10).</summary>
    // orig: party_lore_level_impl R039+0x0470 (1462:0470) matched
    public int PartyLoreLevel() => PartyQueries.LoreLevel(State);

    // ---- saints ---------------------------------------------------------------------

    /// <summary><c>member_bit</c>: the member (−2: anybody present) knows the saint.</summary>
    // orig: member_bit R029+0x1260 (0E76:1260) draft
    public bool KnowsSaint(int who, int saint) => Saints.Knows(State, who, saint);

    /// <summary><c>set_member_bit</c>.</summary>
    // orig: set_member_bit R029+0x1302 (0E76:1302) matched
    public void LearnSaint(int who, int saint) => Saints.Learn(State, who, saint);

    /// <summary><c>saint_chance_lo</c>.</summary>
    // orig: saint_chance_lo R029+0x20C6 (0E76:20C6) matched
    // DIVERGENCE: -1 is resolved first; the original reads the Divine Favor byte and the Virtue byte of record -1 (cards pass a slot).
    public int SaintChanceLow(int who, int saint) => Saints.ChanceLow(State, State.Party.Resolve(who), saint);

    /// <summary><c>saint_chance_hi</c>.</summary>
    // orig: saint_chance_hi R029+0x2116 (0E76:2116) matched
    // DIVERGENCE: -1 is resolved first, as for SaintChanceLow.
    public int SaintChanceHigh(int who, int saint) => Saints.ChanceHigh(State, State.Party.Resolve(who), saint);

    // ---- joining and leaving --------------------------------------------------------

    /// <summary><c>add_member</c>: a hireling joins for <paramref name="hours"/>; the slot or −1.</summary>
    // orig: add_member R029+0x4650 (0E76:4650) matched
    public int AddMember(string name, string shortName, int hours, int term) => PartyRules.AddMember(State, name, shortName, hours, term);

    /// <summary>Ends the hirelings' terms with value <paramref name="term"/> (R029 4aa4).</summary>
    // orig: R029+0x4AA4 (0E76:4AA4) matched
    public void EndTerms(int term) => PartyRules.EndTerms(State, term);

    /// <summary><c>party_report_away</c>.</summary>
    // orig: party_report_away R029+0x23E2 (0E76:23E2) matched
    public int ReportAway() => PartyRules.ReportAway(State);

    /// <summary>
    /// <c>party_drop_away</c>: the members who went away leave the party for good, each deleted
    /// (<see cref="DeleteMember"/>) in slot order. Returns how many.
    /// </summary>
    // orig: party_drop_away R029+0x2434 (0E76:2434) draft, checked against the asm
    // C: each away member gets status 0 and party_delete (R037+0x3F30); then party_panel_rows(0x100) (the screen's).
    public int DropAway()
    {
        var n = 0;
        for (var i = 0; i < Party.MaxMembers; i++)
        {
            if (Status(i) != CharacterStatus.Away)
            {
                continue;
            }
            SetStatus(i, CharacterStatus.Empty);
            DeleteMember(i);
            n++;
        }
        return n;
    }

    /// <summary>
    /// R029 2474: of the members who went away, one leaves the party for good and the others come back. A coin
    /// decides whether the first or the last of them leaves; that one is deleted (<see cref="DeleteMember"/>) before
    /// the next ones are looked at and becomes <c>best_member</c>. Returns how many were away (not who left).
    /// </summary>
    // orig: R029+0x2474 (0E76:2474) draft, checked against the asm
    // C: rand_mod(2) != 0 scans slots 0..4, else 4..0; the first away member gets status 0 and party_delete (R037+0x3F30), the
    // C: others status 1; best_member = the one who left when any was away; party_panel_rows(0x100) (the screen's); the count is returned.
    public int OneOfTheAwayLeaves()
    {
        var left = -1;
        var away = 0;
        var forward = Random(2) != 0;
        for (var k = 0; k < Party.MaxMembers; k++)
        {
            var i = forward ? k : Party.MaxMembers - 1 - k;
            if (Status(i) != CharacterStatus.Away)
            {
                continue;
            }
            if (away == 0)
            {
                SetStatus(i, CharacterStatus.Empty);
                DeleteMember(i);
                left = i;
            }
            else
            {
                SetStatus(i, CharacterStatus.Able);
            }
            away++;
        }
        if (away != 0)
        {
            State.Party.Last.Member = left;
        }
        return away;
    }

    /// <summary><c>party_rejoin_away</c>.</summary>
    // orig: party_rejoin_away R029+0x251A (0E76:251A) matched
    public int RejoinAway() => PartyRules.RejoinAway(State);

    /// <summary>
    /// <c>run_battle</c>(side, seed, ...): side −1 = random, seed −2 = this location, −1 =
    /// random; the battle itself is the <see cref="Battle"/> hook (until it exists the result
    /// is 0). Afterwards the dead leave and time is brought up to date.
    /// </summary>
    // orig: run_battle R029+0x2278 (0E76:2278) matched
    // C: the side is drawn (rand_mod(2)) before the seed (rand_mod(1000)); the battle gets seed + party_seed.
    // DIVERGENCE: cards_shutdown's files and frees and cards_startup's reloads are not modelled (only the shutdown's sound_play(1) is played),
    //   and the "after battle" log is not written; the death film and the end of the game are the host's (AllDead).
    //   They are not neutral for the events: cards_shutdown writes the non-empty events to events.tmp in order, frees them and empties
    //   every slot (1462:0665-06F5), so the battle runs on an empty table; world_load reads the file back into slots 0..n-1 with new
    //   records (1462:04E3-053F), over what the battle put there (from slot 0 on), and the table is compacted. Here the table is kept as
    //   it was: battle-time queries see the events, a saint's boost or price called in a battle is undone when it runs out (the original
    //   loses its event, so the boost stays), and later events fill the old gaps.
    // orig: cards_startup R043+0x078E (1462:078E) matched, called when somebody is left: srand((unsigned)time(0)),
    //   then the shields, card frames and timer pictures are loaded again, order_menu_reset_vec and world_load("locs.tmp") (the locations the
    //   battle saw), party_panel_frames/rows; the C# keeps all of that in memory, the reseed is TimePassage.Reseed (before advance_hours(0)).
    // orig: run_battle begins with cards_shutdown_vec (R043+0x054E), which ends with sound_play(1) (root 047C:078A): the music stops
    //   before the battle driver is loaded; with nobody left after the battle: music_play(0x12), the death film and exit_(0) (AllDead).
    public int RunBattle(int side, int seed, params int[] parameters)
    {
        PlaySound(1);
        if (side == -1)
        {
            side = Random(2);
        }
        var battleSeed = PartyRules.BattleSeed(State, seed);
        var result = Battle?.Invoke(side, battleSeed, parameters) ?? 0;
        PartyRules.AfterBattle(State);
        if (!State.Party.Members.Any(m => m.IsPresent))
        {
            AllDead?.Invoke();
        }
        TimePassage.Reseed(State);
        TimePassage.Advance(State, 0);
        return result;
    }

    /// <summary>Nobody is left after a card's battle: the death tune and film, and the end of the game (supplied by the game; it does not return).</summary>
    public Action? AllDead { get; set; }

    /// <summary>The battle (side, seed, the card's battle parameters → <c>battle_result</c>), supplied by the game.</summary>
    public Func<int, int, int[], int>? Battle { get; set; }
}
