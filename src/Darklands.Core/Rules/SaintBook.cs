using Darklands.Core.Characters;
using Darklands.Core.Events;
using static Darklands.Core.Characters.Attribute;
using static Darklands.Core.Characters.Skill;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Core.Rules;

/// <summary>The saints' help (<see cref="SaintBook"/>), for prayers and card scripts.</summary>
public sealed class SaintEffects : ISaintEffects
{
    public static readonly SaintEffects Instance = new();

    // orig: saint_fns[s](who, x, 8) R136+0xAFC7 via R039 seg 165C (see SaintBook); x (the favor) is unused by every help
    public void Help(GameState state, int who, int saint, int favor) => SaintBook.Help(state, who, saint);
}

/// <summary>
/// What each of the 136 saints does when a prayer reaches them: the "help" (what = 8) of the
/// game's saint functions. Mostly boosts of attributes and skills for about a day, healing,
/// standing in a city, and a few gifts of their own. Each saint's help is a list of boons
/// granted in order, so the random draws come in the original order.
/// </summary>
/// <remarks>
/// <para>A boost "a..b" adds a + rnd(b − a + 1) points (<see cref="MemberRules.RaiseAttributeTemporarily"/>,
/// <see cref="MemberRules.RaiseSkillTemporarily"/>: skill boosts are reported only with the
/// "saintly changes" option). Standing changes go to the current location, or in the open
/// country to the city nearest to the party (<see cref="LocationRules.AdjustReputation"/>); with a
/// list of places, only there.</para>
/// <para>Faithful details: some saints take as well as give (St.Columba, St.John Chrysostom and
/// St.Reinold lower Perception or Charisma for a day, St.Giles lowers Agility and the weapon
/// skills, St.Paul and St.Peter halve Intelligence or Perception for a day, St.Giles of Portugal
/// costs a point of Strength and of Virtue for good); St.Januarius does nothing; St.Edward the
/// Confessor raises everybody's Virtue to 20 and takes a quarter of the purse; St.Alexis gives a
/// penniless party 3..9 groschen; St.Dorothy of Montau gives a long sword to a member without
/// an edged weapon.</para>
/// </remarks>
// orig: the 138 saint functions R039+0x1FD4 .. R039+0x986A (case 8 of each; one per Table row,
//   coordinate R039+0x1FA0+offset, linked 165C:offset) matched;
//   called through saint_fns R136+0xAFC7 (the table order = saint order, checked by Oracle:saints.json);
//   tests: SaintBookTests, Oracle:saints.json
// C: every boost is `raise_attr_temp/lower_skill_temp(who, k, rand_mod(n) + m, h)`: the one random draw in
//   the arguments is made before the callee runs (which draws again for the duration), so the order of
//   draws is the statement order; the other arguments are constants (MSC's right-to-left evaluation
//   changes nothing here).
// C: the accessors the saints call (raise_attr_temp, lower_skill_temp, heal_attr_pct, add_item, count_items_flags ...)
//   resolve who = -1 (a prayer "for the party", DS:89EA = -1) to the leader; the saint bodies that index the
//   records themselves do not: party[-1].f03 is DS:9BEC (in the menu-item records, 0) so the "man" branch is
//   taken, party[-1].f41 is DS:9C2A (the high byte of the UI word DS:9C29: no member's charms change) and
//   saints_known[-1] is member 4's formula bytes 2..21 (IfWoman, SetCharm, AddCharm, Teach).
// DORMANT: R039+0x9578 and R039+0x97B2 are saint functions that saint_fns does not list
//   (Unlisted below); `what` outside 0..8 returns t[what] past the 7-entry table (stack garbage) — no
//   caller passes it.
public static class SaintBook
{
    /// <summary>
    /// One part of a saint's help, granted to member <c>who</c> (a slot 0..4, the leader for −1);
    /// <c>asGiven</c> is the <c>who</c> the prayer passed (−1 for a prayer for the party), which the
    /// saint functions use as it is where they index the member records themselves.
    /// </summary>
    public delegate void Boon(GameState s, int who, int asGiven);

    /// <summary>A saint's help and the original function it comes from (its coordinate <c>R039+0xPPPP</c>).</summary>
    public sealed record SaintHelp(string Original, Boon[] Boons)
    {
        /// <summary>The function's offset in its segment (linked address 165C:offset): payload − (165C − 1462) × 16.</summary>
        public int Offset => Convert.ToInt32(Original[(Original.IndexOf("0x", StringComparison.Ordinal) + 2)..], 16) - (0x165C - 0x1462) * 16;

        /// <summary>The atlas coordinate: R039, payload (165C − 1462) × 16 + offset.</summary>
        public string Coordinate => Original;
    }

    /// <summary>The saints St.Cosmas and St.Damian teach each other.</summary>
    private const int Cosmas = 27;
    private const int Damian = 30;

    /// <summary>The long sword (item code 3) and the item kind bit of edged weapons.</summary>
    private const int LongSword = 3;
    private const uint EdgedWeapon = 0x1;

    /// <summary>Saint <paramref name="saint"/> helps member <paramref name="who"/> (−1 the leader).</summary>
    public static void Help(GameState s, int who, int saint)
    {
        Grant(s, s.Party.Resolve(who), who, Table[saint].Boons);
    }

    /// <summary>The saint's help and its original function.</summary>
    public static SaintHelp Of(int saint) => Table[saint];

    // ---- the boons ------------------------------------------------------------------

    /// <summary>An attribute raised by <paramref name="least"/>..<paramref name="most"/> points for a day.</summary>
    // orig: raise_attr_temp(who, k, rand_mod(n) + m, 0x18) R029+0x4C04
    private static Boon Boost(Attribute a, int least, int most) => (s, who, _) =>
        MemberRules.RaiseAttributeTemporarily(s, who, (int)a, s.Random.Below(most - least + 1) + least, 24);

    /// <summary>A skill raised by <paramref name="least"/>..<paramref name="most"/> points for <paramref name="hours"/> (24; St.Barbara 5 and 10).</summary>
    // orig: lower_skill_temp(who, k, rand_mod(n) + m, h) R029+0x4B08 (the reference's name; it raises)
    private static Boon Boost(Skill k, int least, int most, int hours = 24) => (s, who, _) =>
        MemberRules.RaiseSkillTemporarily(s, who, (int)k, s.Random.Below(most - least + 1) + least, hours);

    /// <summary>The seven weapon skills in order 0..6, each raised by <paramref name="least"/>..<paramref name="most"/>.</summary>
    private static Boon[] AllWeapons(int least, int most) =>
        [.. Enumerable.Range((int)EdgedWeapons, 7).Select(k => Boost((Skill)k, least, most))];

    /// <summary>A skill raised by rnd 4 + Virtue / 5 + 1 (St.John Climacus).</summary>
    // C: rand_mod(4) + member_skill(who, 9) / 5 + 1 — member_skill draws nothing, so the evaluation order does
    //   not matter; Virtue is read afresh for each skill (none of the boosted skills is Virtue).
    private static Boon VirtueBoost(Skill k) => (s, who, _) =>
        MemberRules.RaiseSkillTemporarily(s, who, (int)k, s.Random.Below(4) + MemberRules.Skill(s, who, (int)Virtue) / 5 + 1, 24);

    /// <summary>An attribute lowered by <paramref name="least"/>..<paramref name="most"/> points for a day.</summary>
    // C: written -(rand_mod(n) - 0xfffb): 0xfffb is an unsigned int, so the sum is computed modulo 2^16 and
    //   passed as int: -(rnd + 5), a penalty (the header comments of R039 seg 165C omit these).
    private static Boon Lower(Attribute a, int least, int most) => (s, who, _) =>
        MemberRules.RaiseAttributeTemporarily(s, who, (int)a, -(s.Random.Below(most - least + 1) + least), 24);

    /// <summary>An attribute lowered by half its maximum for a day.</summary>
    // C: -(member_attr_max(who, k) / 2), signed division of a non-negative maximum.
    private static Boon LowerByHalf(Attribute a) => (s, who, _) =>
        MemberRules.RaiseAttributeTemporarily(s, who, (int)a, -(MemberRules.AttributeMaximum(s, who, (int)a) / 2), 24);

    /// <summary>Heals <paramref name="percent"/> % of what the attribute lacks of its maximum.</summary>
    // orig: heal_attr_pct R029+0x08F2
    private static Boon Heal(Attribute a, int percent) => (s, who, _) => MemberRules.HealAttribute(s, who, (int)a, percent);

    /// <summary>The skills of the equipped weapons raised by <paramref name="least"/>..<paramref name="most"/> for a day.</summary>
    // orig: equip_bonuses R029+0x0792
    private static Boon WeaponSkills(int least, int most) => (s, who, _) =>
        MemberRules.BoostEquippedWeaponSkills(s, who, s.Random.Below(most - least + 1) + least);

    /// <summary>A fixed weapon-skill boost (St.Michael: 20, no random draw).</summary>
    private static Boon WeaponSkillsBy(int points) => (s, who, _) => MemberRules.BoostEquippedWeaponSkills(s, who, points);

    /// <summary>The equipped weapons' skills lowered (St.Giles: −(rnd 10 + 15), the unsigned-constant form as in <see cref="Lower"/>).</summary>
    private static Boon LowerWeaponSkills(int least, int most) => (s, who, _) =>
        MemberRules.BoostEquippedWeaponSkills(s, who, -(s.Random.Below(most - least + 1) + least));

    /// <summary>
    /// The standing changes by <paramref name="a"/>/<paramref name="b"/> here, or at the city
    /// nearest to the party, if that place is one of <paramref name="places"/> (none: anywhere).
    /// </summary>
    // orig: what = cur_loc == -1 ? nearest_city(party_x, party_y) : cur_loc; [switch (what) case ...:]
    //   loc_reputation_adj(what, a, b) (19d0, LocationRules.AdjustReputation), repeated inline in the saint functions
    private static Boon Standing(int a, int b, params int[] places) => (s, _, _) =>
    {
        var where = s.CurrentLocation == GameState.Wilderness
            ? LocationRules.NearestCity(s, s.MapPosition.X, s.MapPosition.Y)
            : s.CurrentLocation;
        if (places.Length == 0 || places.Contains(where))
        {
            LocationRules.AdjustReputation(s, where, a, b);
        }
    };

    /// <summary>The standing changes only while the party is at <paramref name="place"/> itself (St.Stephen).</summary>
    // orig: if (cur_loc == 0x55) loc_reputation_adj(cur_loc, 15, 0x19)
    private static Boon StandingOnlyAt(int place, int a, int b) => (s, _, _) =>
    {
        if (s.CurrentLocation == place)
        {
            LocationRules.AdjustReputation(s, place, a, b);
        }
    };

    /// <summary>The standing here changes when the member carries an item of the kinds <paramref name="mask"/> (St.Cecilia).</summary>
    // orig: if (count_items_flags(who, 0x20000000L)) loc_reputation_adj(cur_loc, 0x14, 0x3c)
    // C: in the open country cur_loc is -1: loc_reputation_adj reads the null record locations[-1], draws its chance and writes nothing.
    private static Boon StandingHereIfCarrying(uint mask, int a, int b) => (s, who, _) =>
    {
        if (Inventory.CountWithFlags(s, who, mask) != 0)
        {
            LocationRules.AdjustReputation(s, s.CurrentLocation, a, b);
        }
    };

    /// <summary>
    /// St.John Nepomuk: in Prague (0x46) the standing changes by 15/25; elsewhere by 10/20 when
    /// the place (or the nearest city) is one of the Bohemian ones (Prague included, which only
    /// the nearest-city case can reach).
    /// </summary>
    // orig: if (cur_loc == 0x46) loc_reputation_adj(u, 15, 0x19); else { what = ...; switch (what) { case 0x44..0x47,
    //   0x49, 0x4a: loc_reputation_adj(what, 10, 0x14); } } (R039+0x619A)
    // DIVERGENCE: u is an uninitialised local in the original (whatever the stack slot holds, normally not
    //   a valid location: a wild far-pointer write); here Prague's own standing changes.
    private static readonly Boon PragueStanding = (s, who, _) =>
    {
        const int prague = 0x46;
        if (s.CurrentLocation == prague)
        {
            LocationRules.AdjustReputation(s, prague, 15, 25);
        }
        else
        {
            Standing(10, 20, 0x44, 0x45, prague, 0x47, 0x49, 0x4A)(s, who, who);
        }
    };

    /// <summary>A day's travel blessing for the party (renewed while it is on water).</summary>
    // orig: event_new(-2, 0, 0x63, 0x4b, 0, 0, 9, 0x18, 1, 0) R029+0x2C4E
    // UNCERTAIN: the meaning of kind 0x4B (EventKinds.TravelBlessing) is a guess from its expiry handler.
    private static readonly Boon TravelBlessing = (s, _, _) =>
        s.Events.Create(Who.Everybody, 0, 0x63, EventKinds.TravelBlessing, 0, 0, (int)ExpiryHandler.RenewTravelBlessing, 24, 1, 0);

    /// <summary>The party's event of <paramref name="kind"/> here for <paramref name="hours"/>.</summary>
    // orig: event_here(kind, hours)
    private static Boon EventHere(int kind, int hours) => (s, _, _) => s.Events.CreateHere(kind, hours);

    /// <summary>The help depends on whether the member is a woman (record byte +0x03).</summary>
    // orig: if (party[who].f03 == 0) ... else ...
    // UNCERTAIN: +0x03 is taken as "is a woman" as elsewhere in the port (party_count_able counts it).
    // C: who as given: party[-1].f03 is DS:9BEC, inside the menu-item records, 0 unless the menu code stored there: the f03 == 0 branch.
    private static Boon IfWoman(Boon[] woman, Boon[] man) => (s, who, given) =>
        Grant(s, who, given, given != Who.Leader && s.Party.Slot(who).IsFemale ? woman : man);

    /// <summary>The help comes only while an able woman is in the party (St.Agnes; her chance needs one too).</summary>
    // orig: if (party_count_able()) { ... } R029+0x007C
    private static Boon IfAbleWomen(Boon[] boons) => (s, who, given) =>
    {
        if (PartyQueries.AbleWomen(s) != 0)
        {
            Grant(s, who, given, boons);
        }
    };

    private static void Grant(GameState s, int who, int given, Boon[] boons)
    {
        foreach (var boon in boons)
        {
            boon(s, who, given);
        }
    }

    /// <summary>The member learns another saint.</summary>
    // orig: saints_known[who][3] |= 2 (bit 30, St.Damian) / |= 0x10 (bit 27, St.Cosmas): bits are MSB-first
    // C: who as given: for -1 the bit goes to member 4's formula byte 5 (Saints.Learn).
    private static Boon Teach(int saint) => (s, _, given) => Saints.Learn(s, given, saint);

    /// <summary>The member's battle charms keep their low nibble and get <paramref name="high"/> as the high one.</summary>
    // orig: party[who].f41 = (party[who].f41 & 0xf) - 0x60
    // C: computed as int and stored into a char: (low nibble − 0x60) mod 256 = low nibble + 0xA0.
    // C: who as given: party[-1].f41 is DS:9C2A, the high byte of the UI word DS:9C29 (not modelled): no member changes.
    private static Boon SetCharm(int high) => (s, _, given) =>
    {
        if (given == Who.Leader)
        {
            return;
        }
        var m = s.Party.Slot(given);
        m.BattleCharms = (byte)(m.BattleCharms & 0x0F | high);
    };

    /// <summary>Adds to the member's battle charms byte (St.Dunstan 1, St.Patrick 2).</summary>
    // orig: party[who].f41++ / party[who].f41 += 2
    // C: a char: wraps at 256; the carry runs into the high nibble.
    // C: who as given: for -1 the byte is DS:9C2A (see SetCharm): no member changes.
    private static Boon AddCharm(int points) => (s, _, given) =>
    {
        if (given == Who.Leader)
        {
            return;
        }
        var m = s.Party.Slot(given);
        m.BattleCharms = (byte)(m.BattleCharms + points);
    };

    /// <summary>St.Polycarp: every present member gets the charm 0xB (high nibble) at a strength of 90.</summary>
    // orig: for (i = 0; i < 5; i++) if (party[i].status) { f41 = (f41 & 0xf) - 0x50; f10 = 0x5a; }
    private static readonly Boon FirewallForAll = (s, _, _) =>
    {
        foreach (var m in s.Party.Present)
        {
            m.BattleCharms = (byte)(m.BattleCharms & 0x0F | 0xB0);
            m.CharmStrength = 90;
        }
    };

    /// <summary>St.Crispin: the body and limb armour of every present member gain 10 quality.</summary>
    // orig: for (i = 0; i < 5; i++) if (party[i].status) { party[i].f3b += 10; party[i].f3c += 10; }
    // C: +0x3b/+0x3c are the signed char qualities of the vital and limb armour (also when none is worn):
    //   125 + 10 wraps to −121.
    private static readonly Boon MendArmor = (s, _, _) =>
    {
        foreach (var m in s.Party.Present)
        {
            m.VitalArmor = m.VitalArmor with { Quality = (sbyte)(m.VitalArmor.Quality + 10) };
            m.LimbArmor = m.LimbArmor with { Quality = (sbyte)(m.LimbArmor.Quality + 10) };
        }
    };

    /// <summary>St.Alexis: a party with no florins and no groschen gets 3..9 groschen (its pfennigs stay).</summary>
    // orig: if (party_money.fl == 0 && party_money.gr == 0) party_money.gr = rand_mod(7) + 3
    private static readonly Boon AlmsForThePenniless = (s, _, _) =>
    {
        if (s.Party.Purse is { Florins: 0, Groschen: 0 })
        {
            s.Party.Purse = s.Party.Purse with { Groschen = (short)(s.Random.Below(7) + 3) };
        }
    };

    /// <summary>St.Dorothy of Montau: a member who carries no edged weapon gets a long sword.</summary>
    // orig: if (!count_items_flags(who, 1L)) add_item(who, 3)
    private static readonly Boon SwordForTheUnarmed = (s, who, _) =>
    {
        if (Inventory.CountWithFlags(s, who, EdgedWeapon) == 0)
        {
            Inventory.Add(s, who, LongSword);
        }
    };

    /// <summary>
    /// St.Edward the Confessor: every present member's Virtue is raised to at least 20; then
    /// (when anyone is present) the purse loses a quarter of its pfennig total.
    /// </summary>
    // orig: for (flag = i = 0, p = party; p < party + 5; i++, p++) if (p->status) { if (member_skill(i, 9) < 0x14)
    //   set_skill(i, 9, 0x14); flag = 1; } if (flag) money_sub_fgp(&party_money, 0, 0, (unsigned)money_pfennigs(&party_money) >> 2)
    // C: money_pfennigs returns a long; the (unsigned) cast keeps its low 16 bits before the shift, so a purse
    //   of 65536 pfennigs or more loses a quarter of the remainder only.
    private static readonly Boon VirtueForAllAtAPrice = (s, _, _) =>
    {
        var anyone = false;
        for (var i = 0; i < s.Party.Members.Count; i++)
        {
            if (s.Party.Members[i].IsPresent)
            {
                if (MemberRules.Skill(s, i, (int)Virtue) < 20)
                {
                    MemberRules.SetSkill(s, i, (int)Virtue, 20);
                }
                anyone = true;
            }
        }
        if (anyone)
        {
            s.Party.Purse = s.Party.Purse.Subtract(0, 0, (ushort)s.Party.Purse.TotalPfennigs >> 2);
        }
    };

    /// <summary>St.Giles of Portugal: a point of Strength and of Virtue, lost for good.</summary>
    // orig: add_attr(who, 1, -1); add_skill(who, 9, -1) (the Virtue loss is reported like any skill change)
    private static readonly Boon Penance = (s, who, _) =>
    {
        MemberRules.AddAttribute(s, who, (int)Strength, -1);
        MemberRules.AddSkill(s, who, (int)Virtue, -1);
    };

    /// <summary>St.Raymond Lull: an attribute below 9 is set to 9.</summary>
    // orig: if (member_attr(who, k) < 9) set_attr(who, k, 9)
    private static Boon RaiseToNine(Attribute a) => (s, who, _) =>
    {
        if (MemberRules.Attribute(s, who, (int)a) < 9)
        {
            MemberRules.SetAttribute(s, who, (int)a, 9);
        }
    };

    /// <summary>St.Matthew: the standing with the Fugger and with the Medici bankers each rise by 20..39.</summary>
    // orig: DS:4BB6 += rand_mod(0x14) + 0x14; DS:4BB8 += rand_mod(0x14) + 0x14 (DS:4BB6 read by $fugge00, DS:4BB8 by $Medic00)
    // C: 16-bit ints: they wrap at 32767.
    private static readonly Boon BankersFavor = (s, _, _) =>
    {
        s.FuggerStanding = (short)(s.FuggerStanding + s.Random.Below(20) + 20);
        s.MediciStanding = (short)(s.MediciStanding + s.Random.Below(20) + 20);
    };

    // ---- the table ------------------------------------------------------------------

    /// <summary>
    /// Saint functions the game's table does not list (their table values: feast day 206, class 7,
    /// favor 27..52, Virtue 19, base 35; and feast day 244, class 5, favor 25..60, Virtue 31, base 23).
    /// </summary>
    // DORMANT: no saint number reaches them (saint_fns lists R039+0x7D0C for St.Paul the Simple, whose
    //   table values R039+0x9578 duplicates, and nothing at all for R039+0x97B2). Kept for completeness.
    public static readonly SaintHelp[] Unlisted =
    [
        new("R039+0x9578", [Heal(Strength, 10), Boost(SpeakCommon, 20, 39), Boost(SpeakLatin, 10, 19), Boost(ReadWrite, 40, 79),
            Boost(Charisma, 8, 15), Boost(Endurance, 5, 9)]),
        new("R039+0x97B2", [Heal(Endurance, 50), Heal(Strength, 100), Boost(Religion, 8, 15), Boost(Healing, 12, 23)]),
    ];

    /// <summary>Each saint's help with its original function, in saint order (DARKLAND.LST, saint_fns).</summary>
    private static readonly SaintHelp[] Table =
    [
        // 0 St.Adrian
        new("R039+0x1FD4", [Boost(Endurance, 8, 15), Boost(Charisma, 6, 11), .. AllWeapons(15, 29)]),
        // 1 St.Agatha
        new("R039+0x2130", [IfWoman([Boost(Endurance, 12, 23)], [Boost(Endurance, 6, 11)]), Boost(Charisma, 6, 11)]),
        // 2 St.Agnes
        new("R039+0x21EC", [IfAbleWomen([Standing(10, 20), Boost(Charisma, 10, 29)])]),
        // 3 St.Aidan
        new("R039+0x22AC", [Boost(Woodwise, 25, 29)]),
        // 4 St.Albert the Great
        new("R039+0x232E", [Boost(Intelligence, 8, 15), Boost(Perception, 6, 11), Boost(Alchemy, 30, 44), Boost(SpeakLatin, 15, 29),
            Boost(ReadWrite, 15, 29)]),
        // 5 St.Alcuin
        new("R039+0x241E", [Boost(Intelligence, 6, 11), Boost(SpeakLatin, 20, 39), Boost(ReadWrite, 20, 39)]),
        // 6 St.Alexis
        new("R039+0x24D6", [Standing(10, 20), Boost(Charisma, 6, 11), AlmsForThePenniless]),
        // 7 St.Andrew
        new("R039+0x25A4", [Boost(Endurance, 6, 11), Boost(Charisma, 8, 15)]),
        // 8 St.Andrew the Tribune
        new("R039+0x2642", [Boost(EdgedWeapons, 15, 29), Boost(ImpactWeapons, 15, 29), Boost(Polearms, 15, 29), Boost(ThrownWeapons, 15, 29),
            Boost(Bows, 15, 29)]),
        // 9 St.Anthony
        new("R039+0x2732", [Boost(Endurance, 4, 9), Boost(Strength, 5, 10), Boost(Charisma, 1, 10), Boost(SpeakCommon, 10, 19),
            Boost(SpeakLatin, 10, 19), Boost(ReadWrite, 20, 39)]),
        // 10 St.Anthony of Padua
        new("R039+0x2838", [Boost(Perception, 6, 11), Boost(Strength, 4, 7), Boost(Charisma, 15, 24), Boost(SpeakCommon, 20, 29),
            Boost(SpeakLatin, 10, 19), Boost(ImpactWeapons, 10, 19)]),
        // 11 St.Arnulf
        new("R039+0x2942", [Boost(Perception, 6, 11), Boost(Strength, 4, 7), Boost(Intelligence, 4, 7), .. AllWeapons(10, 19),
            Boost(Streetwise, 6, 11), Boost(SpeakLatin, 6, 11), Boost(ReadWrite, 8, 15), Boost(Riding, 8, 15)]),
        // 12 St.Apollinarius
        new("R039+0x2B22", [Boost(Strength, 2, 4), Boost(Endurance, 8, 23)]),
        // 13 St.Barbara
        new("R039+0x2BC0", [Boost(Artifice, 15, 29, hours: 5), Boost(MissileDevices, 20, 39, hours: 10)]),
        // 14 St.Bathildis
        new("R039+0x2C5E", [Boost(Intelligence, 4, 8), Boost(Perception, 3, 6)]),
        // 15 St.Boniface
        new("R039+0x2CFC", [Boost(Charisma, 8, 15), .. AllWeapons(10, 19), Boost(SpeakCommon, 8, 15)]),
        // 16 St.Catherine
        new("R039+0x2E56", [Boost(Charisma, 8, 15), Boost(Intelligence, 8, 15), Boost(SpeakCommon, 40, 99)]),
        // 17 St.Catherine of Siena
        new("R039+0x2F0E", [Boost(Charisma, 1, 15), Boost(Perception, 1, 15), Boost(SpeakCommon, 10, 29)]),
        // 18 St.Cecilia
        new("R039+0x2FC4", [Boost(Charisma, 3, 10), StandingHereIfCarrying(0x2000_0000, 20, 60)]),
        // 19 St.Ceolwulf
        new("R039+0x306A", [Boost(Perception, 3, 5), Boost(Strength, 6, 11), Boost(Intelligence, 3, 5), .. AllWeapons(6, 11),
            Boost(SpeakLatin, 6, 11)]),
        // 20 St.Charity
        new("R039+0x31FA", [Standing(10, 20), Boost(FlailWeapons, 15, 29), Boost(Charisma, 6, 11), SetCharm(0xA0)]),
        // 21 St.Christina, Astonish.
        new("R039+0x32DA", [Boost(Agility, 8, 15), Boost(Charisma, 1, 10)]),
        // 22 St.Christopher
        new("R039+0x3374", [Boost(Streetwise, 10, 19), Boost(Woodwise, 20, 39), Boost(Riding, 25, 74), Boost(Bows, 6, 11)]),
        // 23 St.Clare
        new("R039+0x3448", [Heal(Endurance, 100), Heal(Strength, 100)]),
        // 24 St.Clotilda
        new("R039+0x34CA", [Heal(Endurance, 25), Heal(Strength, 50), Boost(Healing, 10, 19), Boost(Charisma, 6, 11)]),
        // 25 St.Colman of Cloyne
        new("R039+0x3582", [Boost(SpeakCommon, 30, 89), Boost(SpeakLatin, 6, 11), Boost(ReadWrite, 10, 19), Boost(Charisma, 8, 15)]),
        // 26 St.Columba
        new("R039+0x3656", [Boost(Endurance, 8, 15), Boost(Strength, 6, 13), Boost(Charisma, 4, 7), Lower(Perception, 5, 9), WeaponSkills(10, 19)]),
        // 27 St.Cosmas
        new("R039+0x3744", [Heal(Endurance, 10), Heal(Strength, 10), Boost(Healing, 40, 79), Boost(Perception, 4, 7), Teach(Damian)]),
        // 28 St.Crispin
        new("R039+0x3808", [MendArmor]),
        // 29 St.Cyprian
        new("R039+0x388A", [Boost(Alchemy, 5, 9), Boost(Intelligence, 5, 9)]),
        // 30 St.Damian
        new("R039+0x3928", [Teach(Cosmas), Heal(Endurance, 100), Heal(Strength, 30)]),
        // 31 St.David
        new("R039+0x39B8", [Boost(Intelligence, 4, 7), Boost(Charisma, 1, 6), Boost(Perception, 6, 11), WeaponSkills(8, 15), Boost(Riding, 6, 11)]),
        // 32 St.Denis
        new("R039+0x3AA2", [Heal(Strength, 100), Boost(SpeakCommon, 25, 49), Boost(Charisma, 15, 44)]),
        // 33 St.Derfel Gadarn
        new("R039+0x3B4C", [Boost(Strength, 5, 14), WeaponSkills(20, 59), Heal(Endurance, 30)]),
        // 34 St.Devota
        new("R039+0x3BF4", [Heal(Endurance, 100), Heal(Strength, 20), Boost(Charisma, 15, 29)]),
        // 35 St.Dismas
        new("R039+0x3C92", [Boost(Woodwise, 10, 19), Boost(Streetwise, 20, 39), Boost(Artifice, 20, 39), Boost(Agility, 10, 19),
            Boost(Stealth, 10, 19)]),
        // 36 St.Dominic
        new("R039+0x3D82", [Boost(SpeakCommon, 10, 19), Boost(SpeakLatin, 10, 19), Boost(ReadWrite, 20, 39), Boost(Intelligence, 10, 19)]),
        // 37 St.Dorothy of Montau
        new("R039+0x3E56", [Boost(Charisma, 10, 19), Boost(Perception, 6, 11), Boost(Healing, 10, 19), SwordForTheUnarmed]),
        // 38 St.Drogo
        new("R039+0x3F2C", [Boost(Perception, 6, 11), Boost(Healing, 10, 19), Boost(Woodwise, 20, 39), EventHere(0x4F, 168)]),
        // 39 St.Dunstan
        new("R039+0x3FF2", [WeaponSkills(6, 11), Boost(Perception, 6, 11), Boost(Artifice, 25, 74), Boost(Streetwise, 6, 11), Boost(Alchemy, 5, 14),
            AddCharm(1)]),
        // 40 St.Dymphna
        new("R039+0x40E2", [Heal(Endurance, 30), Boost(Agility, 4, 7), Boost(Stealth, 15, 29), Boost(Healing, 10, 19)]),
        // 41 St.Edward, Confessor
        new("R039+0x41A8", [Boost(Endurance, 4, 7), Boost(Intelligence, 6, 11), Boost(Perception, 8, 15), Boost(Riding, 8, 15), .. AllWeapons(6, 11),
            VirtueForAllAtAPrice]),
        // 42 St.Eligius
        new("R039+0x439E", [Boost(Artifice, 25, 74), Boost(Alchemy, 8, 15)]),
        // 43 St.Emydius
        new("R039+0x443A", [Boost(SpeakCommon, 20, 39), Boost(Charisma, 15, 29)]),
        // 44 St.Engelbert
        new("R039+0x44D8", [Boost(Charisma, 5, 9), Boost(Riding, 10, 19), Boost(SpeakCommon, 6, 11), Boost(Woodwise, 10, 19), TravelBlessing]),
        // 45 St.Erasmus
        new("R039+0x45C8", [Boost(Agility, 6, 11), Boost(Perception, 6, 11), SetCharm(0xA0)]),
        // 46 St.Eric
        new("R039+0x4676", [Boost(Strength, 8, 23), Boost(Charisma, 4, 7), WeaponSkills(15, 24), Boost(Riding, 6, 11), Boost(SpeakCommon, 6, 11)]),
        // 47 St.Eustace
        new("R039+0x4762", [Boost(Riding, 10, 19), Boost(Woodwise, 40, 69), Boost(EdgedWeapons, 6, 11), Boost(ThrownWeapons, 6, 11),
            Boost(Bows, 6, 11)]),
        // 48 St.Felix of Nola
        new("R039+0x4850", [Boost(Stealth, 40, 119), Boost(Woodwise, 10, 19), Boost(Agility, 8, 15)]),
        // 49 St.Finbar
        new("R039+0x490A", [Boost(SpeakLatin, 10, 19), Boost(Charisma, 8, 15), Boost(Perception, 6, 11)]),
        // 50 St.Finnian
        new("R039+0x49C4", [Boost(Streetwise, 4, 7), Boost(SpeakCommon, 6, 11), Boost(ReadWrite, 6, 11), Boost(Agility, 8, 15),
            Boost(Charisma, 6, 11), TravelBlessing]),
        // 51 St.Florian
        new("R039+0x4AD0", [Standing(15, 25, 0x43, 0x51, 0x52, 0x53, 0x54, 0x56), WeaponSkills(6, 11), Boost(Riding, 6, 11), TravelBlessing]),
        // 52 St.Francis
        new("R039+0x9674", [Heal(Endurance, 30), Heal(Strength, 10), Boost(SpeakCommon, 4, 7), Boost(Healing, 10, 19), Boost(Artifice, 12, 23),
            Boost(Woodwise, 4, 7), Boost(Perception, 6, 11), Boost(Charisma, 15, 29), Boost(Endurance, 6, 11)]),
        // 53 St.Gabriel
        new("R039+0x4BCA", [Boost(Intelligence, 10, 29), Boost(Perception, 10, 29)]),
        // 54 St.Genevieve
        new("R039+0x4C68", [Heal(Endurance, 30), Heal(Strength, 20), EventHere(0x4F, 168)]),
        // 55 St.George
        new("R039+0x4CF8", [Boost(Strength, 10, 14), Boost(Agility, 5, 9), .. AllWeapons(25, 49), Boost(Riding, 25, 49)]),
        // 56 St.Gerlac
        new("R039+0x4E6E", [Heal(Endurance, 30), Heal(Strength, 20), Boost(Healing, 20, 39), WeaponSkills(15, 24)]),
        // 57 St.Gertrude o'Nivelles
        new("R039+0x4F22", [Boost(Riding, 5, 9), Boost(Woodwise, 15, 29), Boost(SpeakLatin, 15, 29), Boost(ReadWrite, 15, 29),
            Boost(Charisma, 6, 11), EventHere(0x5D, 1)]),
        // 58 St.Giles
        new("R039+0x501E", [Heal(Endurance, 40), Heal(Strength, 10), LowerWeaponSkills(15, 24), Lower(Agility, 4, 7), Boost(Stealth, 30, 59),
            Boost(Woodwise, 5, 9), Boost(Streetwise, 50, 89)]),
        // 59 St.Giles of Portugal
        new("R039+0x5126", [Penance, Boost(Alchemy, 40, 59), Boost(Artifice, 10, 19), Boost(Intelligence, 5, 9), Boost(Perception, 15, 24)]),
        // 60 St.Godehard
        new("R039+0x5214", [Heal(Endurance, 30), Boost(SpeakLatin, 20, 39), Boost(ReadWrite, 25, 49)]),
        // 61 St.Godfrey
        new("R039+0x52C0", [Heal(Endurance, 20), Boost(SpeakCommon, 15, 29), Boost(Artifice, 10, 19), EventHere(0x4F, 168)]),
        // 62 St.Gottschalk
        new("R039+0x5376", [Standing(20, 40, 4, 5, 6, 7, 10, 11, 12), .. AllWeapons(15, 29)]),
        // 63 St.Gregory Thaumaturg.
        new("R039+0x54DE", [Boost(Charisma, 8, 15), Boost(Alchemy, 10, 19), Boost(Artifice, 20, 39), Boost(SpeakCommon, 10, 19)]),
        // 64 St.Hedwig
        new("R039+0x55B2", [Standing(30, 50, 0x25, 0x48, 0x49, 0x4B), Boost(Healing, 20, 29)]),
        // 65 St.Henry
        new("R039+0x5676", [.. AllWeapons(10, 19), Boost(Intelligence, 8, 23)]),
        // 66 St.Heribert
        new("R039+0x57B6", [Boost(Charisma, 10, 19), Boost(Strength, 4, 7)]),
        // 67 St.Herve
        new("R039+0x5854", [Boost(Perception, 15, 29)]),
        // 68 St.Hildegard
        new("R039+0x58D4", [Boost(Perception, 20, 59)]),
        // 69 St.Hubert
        new("R039+0x5954", [Boost(Polearms, 15, 29), Boost(ThrownWeapons, 15, 29), Boost(Bows, 15, 29), Boost(MissileDevices, 10, 19),
            Boost(Stealth, 25, 49), Boost(Woodwise, 40, 79)]),
        // 70 St.Illtyd
        new("R039+0x5A5C", [Boost(Charisma, 15, 29), .. AllWeapons(10, 19), Boost(SpeakCommon, 10, 19), Boost(Riding, 5, 9)]),
        // 71 St.Isidore
        new("R039+0x5BD2", [Boost(Streetwise, 10, 19), Boost(SpeakCommon, 5, 9), Boost(Charisma, 8, 15)]),
        // 72 St.Ita
        new("R039+0x5C8C", [Heal(Endurance, 50), Heal(Strength, 100), Boost(Healing, 20, 39), Boost(Charisma, 8, 15)]),
        // 73 St.James
        new("R039+0x948A", [Heal(Endurance, 20), Heal(Strength, 10), Boost(Virtue, 20, 34), Boost(Strength, 4, 7), Boost(Charisma, 3, 5),
            Boost(Endurance, 4, 7)]),
        // 74 St.Januarius
        new("R039+0x5D44", []),
        // 75 St.John of Bridlington
        new("R039+0x5DAA", [
            IfWoman(
                [Heal(Endurance, 100), Heal(Strength, 20), Boost(Endurance, 5, 9), Boost(Strength, 4, 7)],
                [Heal(Endurance, 30), Heal(Strength, 10)]),
        ]),
        // 76 St.John Chrysostom
        new("R039+0x5E8C", [Boost(SpeakCommon, 30, 59), Boost(SpeakLatin, 30, 59), Boost(Charisma, 8, 15), Lower(Perception, 6, 11)]),
        // 77 St.John Climacus
        new("R039+0x5F62", [VirtueBoost(Alchemy), VirtueBoost(Religion), VirtueBoost(SpeakCommon), VirtueBoost(SpeakLatin), VirtueBoost(ReadWrite),
            VirtueBoost(Healing), VirtueBoost(Stealth), VirtueBoost(Artifice), VirtueBoost(Woodwise), VirtueBoost(Streetwise)]),
        // 78 St.John Nepolmuchen
        new("R039+0x619A", [Boost(SpeakCommon, 20, 39), Boost(Streetwise, 4, 7), Boost(Charisma, 8, 15), Boost(Intelligence, 6, 11), PragueStanding]),
        // 79 St.Joseph
        new("R039+0x62C0", [Boost(Artifice, 30, 59), Boost(Streetwise, 30, 59), Boost(Charisma, 6, 11)]),
        // 80 St.Jude
        new("R039+0x6378", [Boost(Endurance, 4, 8), Boost(Charisma, 4, 8), Boost(Strength, 4, 8), Boost(Perception, 4, 8), Boost(Agility, 4, 8),
            Boost(Intelligence, 4, 8), WeaponSkills(15, 24), Boost(Alchemy, 6, 11), Boost(Religion, 6, 11),
            Boost(SpeakCommon, 6, 11), Boost(SpeakLatin, 6, 11), Boost(ReadWrite, 6, 11), Boost(Healing, 6, 11),
            Boost(Stealth, 6, 11), Boost(Artifice, 6, 11), Boost(Woodwise, 6, 11), Boost(Streetwise, 6, 11)]),
        // 81 St.Julian, Hospitaler
        new("R039+0x65A8", [Boost(Woodwise, 20, 39), Boost(Riding, 6, 11), TravelBlessing]),
        // 82 St.Kessog
        new("R039+0x6662", [Heal(Endurance, 40), Heal(Strength, 20), Boost(Charisma, 6, 11), Boost(SpeakCommon, 15, 29), Boost(Riding, 4, 7),
            Boost(Healing, 10, 19)]),
        // 83 St.Lasdislaus
        new("R039+0x6750", [Boost(SpeakCommon, 6, 11), Boost(Riding, 10, 19), Boost(Strength, 4, 7), Boost(Agility, 3, 5), WeaponSkills(15, 24)]),
        // 84 St.Lawrence
        new("R039+0x683C", [Heal(Endurance, 100), Boost(SpeakCommon, 10, 19), Boost(Streetwise, 10, 19), Boost(Charisma, 12, 19)]),
        // 85 St.Lazarus
        new("R039+0x6902", [Heal(Endurance, 30), Heal(Strength, 100)]),
        // 86 St.Longinius
        new("R039+0x6984", [.. AllWeapons(10, 19)]),
        // 87 St.Lucy
        new("R039+0x6AAA", [Heal(Endurance, 100), Heal(Strength, 100), Boost(Perception, 6, 11)]),
        // 88 St.Luke
        new("R039+0x6B48", [Heal(Endurance, 50), Heal(Strength, 30), Boost(Healing, 20, 39)]),
        // 89 St.Lutgardis
        new("R039+0x6BE6", [Boost(Charisma, 6, 11), Boost(Perception, 8, 15), Boost(Virtue, 6, 17), Boost(Endurance, 5, 9)]),
        // 90 St.Margaret
        new("R039+0x6CBA", [
            IfWoman(
                [
                    Heal(Endurance, 100), Heal(Strength, 50), Boost(Endurance, 3, 6), Boost(Charisma, 3, 6), Boost(Strength, 3, 6),
                    Boost(Perception, 3, 6), Boost(Agility, 3, 6), Boost(Intelligence, 3, 6), Boost(Alchemy, 5, 10),
                    Boost(Religion, 5, 10), Boost(SpeakCommon, 5, 10), Boost(SpeakLatin, 5, 10), Boost(ReadWrite, 5, 10),
                    Boost(Healing, 5, 10), Boost(Stealth, 5, 10), Boost(Artifice, 5, 10), Boost(Woodwise, 5, 10), Boost(Streetwise, 5, 10),
                ],
                [Heal(Endurance, 10), Heal(Strength, 10)]),
        ]),
        // 91 St.Margaret of Cortona
        new("R039+0x6F16", [Heal(Endurance, 100), Heal(Strength, 40), Boost(Healing, 20, 39), Boost(SpeakCommon, 10, 19)]),
        // 92 St.Mark
        new("R039+0x6FCE", [Boost(ReadWrite, 15, 29), Boost(Agility, 12, 23)]),
        // 93 St.Martin of Tours
        new("R039+0x706C", [Heal(Endurance, 30), Heal(Strength, 30), Boost(Charisma, 8, 15), Boost(Perception, 12, 19), Boost(SpeakCommon, 25, 49),
            Boost(Riding, 20, 39)]),
        // 94 St.Matthew
        new("R039+0x715A", [Boost(SpeakLatin, 15, 29), Boost(ReadWrite, 25, 49), Boost(Intelligence, 8, 15), BankersFavor]),
        // 95 St.Maurice
        new("R039+0x7236", [Boost(EdgedWeapons, 20, 39), Boost(Alchemy, 10, 19)]),
        // 96 St.Michael
        new("R039+0x72D4", [WeaponSkillsBy(20), Boost(Healing, 20, 39), Boost(Charisma, 12, 19), Standing(20, 40)]),
        // 97 St.Milburga
        new("R039+0x73AA", [Boost(Healing, 20, 39), Boost(Charisma, 10, 19), TravelBlessing]),
        // 98 St.Moses the Black
        new("R039+0x7464", [.. AllWeapons(15, 29), Boost(Stealth, 20, 39), Boost(Woodwise, 10, 19), Boost(Streetwise, 10, 19)]),
        // 99 St.Nicholas
        new("R039+0x75DA", [Standing(10, 20), Boost(FlailWeapons, 15, 29), Boost(Charisma, 6, 11), Boost(Endurance, 4, 8)]),
        // 100 St.Odilia
        new("R039+0x76C4", [Boost(Perception, 6, 11), Standing(15, 25, 0x36, 0x4C)]),
        // 101 St.Odo
        new("R039+0x777E", [.. AllWeapons(8, 22), Boost(SpeakCommon, 8, 15), Boost(Perception, 10, 19), Boost(Charisma, 4, 8)]),
        // 102 St.Olaf
        new("R039+0x78F2", [Heal(Endurance, 30), .. AllWeapons(8, 22)]),
        // 103 St.Pantaleon
        new("R039+0x7A24", [Heal(Endurance, 30), Heal(Strength, 30), Boost(Alchemy, 10, 19), Boost(Healing, 15, 29), Boost(Charisma, 6, 11),
            TravelBlessing, SetCharm(0xA0)]),
        // 104 St.Patrick
        new("R039+0x7B22", [Boost(EdgedWeapons, 15, 29), Boost(ImpactWeapons, 15, 29), Boost(Polearms, 15, 29), Boost(SpeakCommon, 15, 29),
            Boost(SpeakLatin, 15, 29), Boost(ReadWrite, 15, 29), Boost(Charisma, 15, 24), Boost(Strength, 12, 19), AddCharm(2)]),
        // 105 St.Paul
        // orig: R039+0x7C6A; R039+0x7D0C
        // BUG: the helps of the two Pauls are mixed up: St.Paul gets R039+0x7C6A, whose feast day (66) and help are
        // those DARKLAND.SNT gives St.Paul the Simple; St.Paul the Simple (106) gets R039+0x7D0C, a copy of St.Thomas
        // Aquinas's help (R039+0x8B58); the help described for St.Paul is R039+0x9578, which no saint number reaches
        // (Z-9318d7).
        new("R039+0x7C6A", [Heal(Endurance, 100), Heal(Strength, 100), LowerByHalf(Intelligence)]),
        // 106 St.Paul the Simple
        new("R039+0x7D0C", [Boost(SpeakCommon, 12, 26), Boost(SpeakLatin, 12, 26), Boost(ReadWrite, 15, 29), Boost(Healing, 10, 19),
            Boost(Intelligence, 12, 23)]),
        // 107 St.Perpetua
        new("R039+0x7DFC", [Boost(Charisma, 15, 29)]),
        // 108 St.Peter
        new("R039+0x7E7E", [Boost(SpeakCommon, 10, 19), Boost(SpeakLatin, 10, 19), Boost(Healing, 10, 19), Boost(EdgedWeapons, 15, 29),
            Boost(Charisma, 8, 15), Boost(Strength, 12, 19), LowerByHalf(Perception)]),
        // 109 St.Peter of Atroa
        new("R039+0x7FA8", [Boost(Alchemy, 10, 19), Boost(Stealth, 40, 79), Boost(Riding, 10, 19), Boost(Intelligence, 6, 11),
            Boost(Perception, 6, 11), Standing(-1, -5)]),
        // 110 St.Polycarp
        // orig: R039+0x80C2
        // BUG: the saints' charms never wear off: FirewallForAll, like SetCharm and AddCharm, sets the charm byte
        // (+0x41) and its strength (+0x10) with no event, and nothing takes them back.
        new("R039+0x80C2", [FirewallForAll]),
        // 111 St.Raphael
        new("R039+0x814A", [Heal(Endurance, 100), Heal(Strength, 100), Boost(Healing, 50, 139), Boost(Agility, 8, 15), Boost(Perception, 6, 11)]),
        // 112 St.Raymond Penafort
        new("R039+0x821E", [Boost(ReadWrite, 30, 89), Boost(Intelligence, 15, 29), TravelBlessing]),
        // 113 St.Raymond Lull
        new("R039+0x82D8", [Boost(Alchemy, 20, 39), Boost(ReadWrite, 30, 59), Boost(Intelligence, 12, 19), RaiseToNine(Endurance),
            RaiseToNine(Strength)]),
        // 114 St.Reinold
        new("R039+0x83CC", [Heal(Endurance, 30), Lower(Charisma, 6, 11), Boost(Alchemy, 6, 17), Boost(Artifice, 35, 69)]),
        // 115 St.Roch
        new("R039+0x8494", [Heal(Endurance, 20), Heal(Strength, 20), Boost(Healing, 10, 19), Boost(Artifice, 8, 15)]),
        // 116 St.Sabas the Goth
        new("R039+0x854C", [Heal(Endurance, 100), Heal(Strength, 100), Boost(Charisma, 10, 19)]),
        // 117 St.Sebastian
        new("R039+0x85EA", [Heal(Endurance, 10), Heal(Strength, 10), Boost(Bows, 20, 39), Boost(Riding, 10, 19), Boost(Agility, 12, 19)]),
        // 118 St.Stanislaus
        new("R039+0x86BE", [Heal(Endurance, 20), Heal(Strength, 10), Boost(SpeakCommon, 30, 59), Boost(SpeakLatin, 20, 39), Boost(Charisma, 6, 11)]),
        // 119 St.Stephen
        new("R039+0x8792", [WeaponSkills(15, 24), StandingOnlyAt(0x55, 15, 25), Boost(SpeakCommon, 6, 11), Boost(Riding, 10, 19),
            Boost(Charisma, 6, 11), Boost(Intelligence, 8, 15)]),
        // 120 St.Swithbert
        new("R039+0x8894", [Heal(Endurance, 20), Heal(Strength, 10), Boost(SpeakCommon, 25, 49), Boost(Charisma, 10, 19)]),
        // 121 St.Tarachus
        new("R039+0x894C", [Heal(Endurance, 40), Heal(Strength, 20), Boost(Charisma, 6, 11)]),
        // 122 St.Thalelaeus,Merciful
        new("R039+0x89EA", [Heal(Endurance, 40), Heal(Strength, 20), Boost(Healing, 30, 59)]),
        // 123 St.Theodore Tiro
        // orig: R039+0x8A88
        // BUG: St.Theodore Tiro raises Flail Weapons twice for a flail wielder: WeaponSkills already raises it for a
        // member holding a flail, and the saint then raises it again; his help also does not match DARKLAND.SNT (all
        // weapon skills, Charisma, Riding): no Riding, Endurance instead.
        new("R039+0x8A88", [WeaponSkills(15, 24), Boost(FlailWeapons, 15, 29), Boost(Charisma, 6, 11), Boost(Endurance, 4, 8)]),
        // 124 St.Thomas the Apostle
        new("R039+0x986A", [Boost(SpeakCommon, 4, 7), Boost(Healing, 10, 19), Boost(Artifice, 12, 23), Boost(Woodwise, 4, 7), Boost(Strength, 4, 8),
            Boost(Endurance, 4, 8)]),
        // 125 St.Thomas Aquinas
        new("R039+0x8B58", [Boost(SpeakCommon, 12, 26), Boost(SpeakLatin, 12, 26), Boost(ReadWrite, 15, 29), Boost(Healing, 10, 19),
            Boost(Intelligence, 12, 23)]),
        // 126 St.Valentine
        new("R039+0x8C46", [Heal(Endurance, 10), Heal(Strength, 10), Boost(Stealth, 6, 11), Boost(Healing, 6, 11), Boost(SpeakCommon, 6, 11),
            Boost(Charisma, 10, 19)]),
        // 127 St.Victor o'Marseilles
        new("R039+0x8D32", [Heal(Endurance, 100), Heal(Strength, 20), WeaponSkills(15, 24), Boost(SpeakCommon, 10, 19), Boost(Charisma, 8, 15)]),
        // 128 St.Vitus
        new("R039+0x8E02", [Boost(Charisma, 10, 19), Boost(Agility, 12, 23), EventHere(0x5D, 1)]),
        // 129 St.Wenceslaus
        new("R039+0x8EAC", [Boost(Riding, 8, 15), WeaponSkills(8, 15), Boost(Perception, 6, 11), Boost(Intelligence, 8, 13), Boost(Strength, 6, 11),
            Standing(15, 25, 0x44, 0x45, 0x46, 0x47, 0x49, 0x4A)]),
        // 130 St.Wilfrid
        new("R039+0x8FD8", [Heal(Endurance, 20), Boost(Artifice, 6, 11), Boost(SpeakCommon, 20, 39), Boost(Charisma, 6, 11), EventHere(0x4F, 168)]),
        // 131 St.Willehad
        new("R039+0x90AA", [Boost(Woodwise, 8, 15), Boost(Riding, 10, 19), Boost(Agility, 8, 15), EventHere(0x4F, 168)]),
        // 132 St.Willebald
        new("R039+0x916E", [Boost(Woodwise, 6, 11), Boost(SpeakCommon, 25, 49), Boost(Charisma, 10, 19)]),
        // 133 St.Williborord
        new("R039+0x9228", [Boost(Woodwise, 8, 15), Boost(SpeakCommon, 20, 39), Boost(Charisma, 10, 19)]),
        // 134 St.Wolfgang
        new("R039+0x92E2", [Boost(Woodwise, 6, 11), Boost(Riding, 6, 11), Boost(SpeakCommon, 20, 39), Boost(Charisma, 8, 15),
            Boost(Intelligence, 6, 11)]),
        // 135 St.Zita
        new("R039+0x93D2", [Heal(Endurance, 20), Heal(Strength, 5), Boost(Streetwise, 6, 11), Boost(Perception, 6, 11)]),
    ];
}
