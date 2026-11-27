using Darklands.Core.Events;
using Darklands.Core.Items;

namespace Darklands.Core.Rules;

/// <summary>
/// The Fixed rules' day-long potions and saints' protections (F-1 set 1b, the user's decisions of 2026-11-25): each is a timed event of
/// the potions' bonus kind (0x4A, which the original reads and leaves alone when it ends) that lasts about a day (the original's
/// boost hours: 19..21) and is taken back exactly when it ends; a new dose or prayer of the same protection replaces the running
/// one. Saved games keep the original's format: the original loads them (an effect then simply does not wear off there).
/// <list type="bullet">
/// <item>weapon potions (Deadly Blade, Strongedge, Great Power): their part of the bonus byte (+0x34), taken off again; the battle
/// counts each only with the weapons the manual names (<see cref="WeaponBonusFor"/>);</item>
/// <item>Hardarmor: its armour levels (+0x41's low nibble); Firewall and the saints' fire: the fire strength (+0x10) alone, against
/// flame weapons (Arabian Fire, the dragon's breath), never the charm nibble nor the attacker's bit 7;</item>
/// <item>St. Patrick: armour against missiles; St. Pantaleon: armour against Wolf, Boar, Bear and Spider; St. Dunstan: quality of
/// the worn metal armour: read by the battle from the running events (<see cref="Amount"/>), nothing stored in the member.</item>
/// <item>F-1 set 2 (2026-11-27): Great Power's weapon quality, St. Michael's quality of the party's weapons and armour (read at the
/// blow, as St. Dunstan's), St. Christopher's week of faster travel (read by the overland step, <see cref="RunsOnTheParty"/>).</item>
/// </list>
/// Old saves' permanent bonuses, charms and strengths stay as they are: only events this code made (marked) are taken back.
/// </summary>
// Not in the original: the Fixed rule set, F-1 set 1b (B-11a75a, B-d0b264, B-44772a, B-a19878, B-315695, B-f32e0e, B-ce6144, B-243faa) and set 2
//   (B-555b7a, B-723764, B-623452).
public static class DayProtections
{
    // the event types: the potions' codes, a saint's fire 0x100 + its number, the battle amounts 0x200 / 0x300 + the saint's number
    public const int DeadlyBlade = 0x69, Strongedge = 0x6A, GreatPower = 0x6B, Hardarmor = 0x6D, Firewall = 0x74;

    /// <summary>A saint's fire protection: <c>SaintFire + saint</c>.</summary>
    public const int SaintFire = 0x100;

    /// <summary>St. Patrick's armour against missiles (the saint's number 104).</summary>
    public const int MissileArmour = 0x200 + 104;

    /// <summary>St. Pantaleon's armour against the four animals (103).</summary>
    public const int AnimalArmour = 0x200 + 103;

    /// <summary>St. Dunstan's quality of the worn metal armour (39).</summary>
    public const int MetalQuality = 0x300 + 39;

    /// <summary>St. Michael's quality of the party's weapons and of its armour (96; F-1 set 2).</summary>
    public const int MichaelWeaponQuality = 0x300 + 96, MichaelArmourQuality = 0x500 + 96;

    /// <summary>Great Power as the manual has it: the quality of an impact weapon, flail or handgun (F-1 set 2; the byte is not touched).</summary>
    public const int GreatPowerQuality = 0x400 + GreatPower;

    /// <summary>St. Christopher's faster travel, for a week (22; F-1 set 2): a step takes <see cref="TravelPart"/> of its time.</summary>
    public const int ChristopherTravel = 0x600 + 22;

    /// <summary>The part of a step's minutes a step takes while St. Christopher's travel runs: 2/3 (half again as fast).</summary>
    public const int TravelPart = 2, TravelWhole = 3;

    // the mark of an event this code made (Data1): old saves' events of the same kind and type are the original's, left alone
    private const int Mark = 0x1B1B;

    private const int Hours = 24;

    /// <summary>The natural weapons of the four animals St. Pantaleon guards against (Wolf, Boar, Bear, Spider: the user's list).</summary>
    public static readonly IReadOnlySet<int> AnimalWeapons = new HashSet<int> { 44, 51, 41, 45 };

    /// <summary>The handguns' weapon types (Iron and Brass Handgun): Great Power's missile weapons.</summary>
    public static readonly IReadOnlySet<int> HandgunTypes = new HashSet<int> { 32, 33 };

    /// <summary>Metal armour: scale, chainmail, brigandine and plate, on the body (types 72..75) and the limbs (81..84).</summary>
    public static bool IsMetalArmour(int type) => type is >= 72 and <= 75 or >= 81 and <= 84;

    private static bool IsFire(int type) => type == Firewall || type is >= SaintFire and < SaintFire + 0x100;

    private static bool Ours(TimedEvent e) => e.Kind == EventKinds.PotionBonus && e.Data1 == Mark;

    private static IEnumerable<(int Slot, TimedEvent Event)> Running(GameState s, int member, int type) =>
        s.Events.All().Where(x => Ours(x.Event) && x.Event.Owner == member && x.Event.Type == type);

    /// <summary>
    /// Starts protection <paramref name="type"/> of <paramref name="value"/> on member <paramref name="member"/> for about a day: the same
    /// protection running on the member is taken back first (a new dose replaces the old one), then this one is given and recorded.
    /// </summary>
    public static void Start(GameState s, int member, int type, int value) => Start(s, member, type, value, Hours);

    /// <summary>As <see cref="Start(GameState, int, int, int)"/> for about <paramref name="hours"/> (the original's boost hours: rnd 3 + hours − 5).</summary>
    public static void Start(GameState s, int member, int type, int value, int hours)
    {
        ArgumentNullException.ThrowIfNull(s);
        if (s.Party.OccupiedSlot(member) is null)
        {
            return;
        }
        foreach (var (slot, e) in Running(s, member, type).ToList())
        {
            s.Events.Free(slot);
            TakeBack(s, e);
        }
        var m = s.Party.Slot(member);
        var extra = 0;
        switch (type)
        {
            case DeadlyBlade:
                m.WeaponPotions = (byte)(m.WeaponPotions + (value << 4));
                break;
            case Strongedge or GreatPower:
                m.WeaponPotions = (byte)(m.WeaponPotions + value);
                break;
            case Hardarmor:
                m.ArmourPotions = (byte)(m.ArmourPotions + value);
                break;
            case var fire when IsFire(fire):
                // the strength before any of these fire protections (another running one knows it): what it goes back to
                extra = s.Events.All().Where(x => Ours(x.Event) && x.Event.Owner == member && IsFire(x.Event.Type)).Select(x => x.Event.Extra)
                    .DefaultIfEmpty(m.FirewallStrength).First();
                break;
        }
        var created = s.Events.Create(member, s.CurrentLocation, extra, EventKinds.PotionBonus, type, value, (int)ExpiryHandler.UndoTemporaryBoost,
            s.Random.Below(3) + hours - 5, (int)EventFlags.CallExpiryHandler, 0);
        if (s.Events[created] is { } e2)
        {
            e2.Data1 = Mark;
        }
        if (IsFire(type))
        {
            SetFire(s, member, extra);
        }
    }

    /// <summary>Whether <paramref name="e"/> is one of these protections (its end takes it back: <see cref="TakeBack"/>).</summary>
    public static bool Handles(TimedEvent e) => e is not null && Ours(e);

    /// <summary>A protection ends (its event already freed or about to be): what it gave is taken back.</summary>
    public static void TakeBack(GameState s, TimedEvent e)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(e);
        if (s.Party.OccupiedSlot(e.Owner) is not { } m)
        {
            return;
        }
        switch (e.Type)
        {
            case DeadlyBlade:
                m.WeaponPotions = (byte)(m.WeaponPotions - (e.Value << 4));
                break;
            case Strongedge or GreatPower:
                m.WeaponPotions = (byte)(m.WeaponPotions - e.Value);
                break;
            case Hardarmor:
                m.ArmourPotions = (byte)(m.ArmourPotions - e.Value);
                break;
            case var fire when IsFire(fire):
                SetFire(s, e.Owner, e.Extra, except: e);
                break;
        }
    }

    // the fire strength: the highest running fire protection's, or the strength from before them
    private static void SetFire(GameState s, int member, int before, TimedEvent? except = null)
    {
        var running = s.Events.All().Where(x => Ours(x.Event) && x.Event.Owner == member && IsFire(x.Event.Type) && !ReferenceEquals(x.Event, except))
            .Select(x => x.Event.Value).DefaultIfEmpty(0).Max();
        s.Party.Slot(member).FirewallStrength = (byte)Math.Max(before, running);
    }

    /// <summary>The largest running amount of protection <paramref name="type"/> on member <paramref name="member"/> (0: none).</summary>
    public static int Amount(GameState s, int member, int type)
    {
        ArgumentNullException.ThrowIfNull(s);
        return Running(s, member, type).Select(x => x.Event.Value).DefaultIfEmpty(0).Max();
    }

    /// <summary>Whether protection <paramref name="type"/> runs on any present member (a party-wide effect: St. Christopher's travel).</summary>
    public static bool RunsOnTheParty(GameState s, int type)
    {
        ArgumentNullException.ThrowIfNull(s);
        return s.Events.All().Any(x => Ours(x.Event) && x.Event.Type == type && s.Party.OccupiedSlot(x.Event.Owner) is not null);
    }

    /// <summary>Whether Great Power counts with a weapon of type <paramref name="weapon"/>: impact weapons, flails and handguns (the manual).</summary>
    public static bool GreatPowerCounts(GameState s, int weapon)
    {
        ArgumentNullException.ThrowIfNull(s);
        var flags = WeaponFlags(s, weapon);
        return (flags & 2) != 0 || (flags & 8) != 0 || HandgunTypes.Contains(weapon);
    }

    // the item flags of weapon type <weapon> (0: none or not a weapon)
    private static uint WeaponFlags(GameState s, int weapon) =>
        weapon < 0 ? 0u : s.Data.ItemDefinitions.Where(d => d.Type == weapon && (d.Flags & (ItemFlags.HandWeapon | ItemFlags.MissileWeapon)) != 0)
            .Select(d => d.Flags).FirstOrDefault();

    /// <summary>
    /// The weapon potions' bonus byte as it counts for a blow with a weapon of type <paramref name="weapon"/> (−1: none): the part the
    /// running potions gave counts only with the weapons the manual names (Deadly Blade: every weapon but impact weapons and flails;
    /// Strongedge: edged weapons and polearms; Great Power: impact weapons, flails and handguns); the rest of the byte (an old save's
    /// lasting bonus) counts as before.
    /// </summary>
    public static byte WeaponBonusFor(GameState s, int member, byte bonus, int weapon)
    {
        ArgumentNullException.ThrowIfNull(s);
        var flags = WeaponFlags(s, weapon);
        var (edged, impact, polearm, flail) = ((flags & 1) != 0, (flags & 2) != 0, (flags & 4) != 0, (flags & 8) != 0);
        var isWeapon = (flags & (ItemFlags.HandWeapon | ItemFlags.MissileWeapon)) != 0;
        var result = (int)bonus;
        foreach (var (type, value) in new[] { (DeadlyBlade, Amount(s, member, DeadlyBlade)), (Strongedge, Amount(s, member, Strongedge)), (GreatPower, Amount(s, member, GreatPower)) })
        {
            var part = type == DeadlyBlade ? value << 4 : value;
            var counts = type switch
            {
                DeadlyBlade => isWeapon && !impact && !flail,
                Strongedge => edged || polearm,
                _ => impact || flail || HandgunTypes.Contains(weapon),
            };
            if (!counts)
            {
                result -= part;
            }
        }
        return (byte)result;
    }
}
