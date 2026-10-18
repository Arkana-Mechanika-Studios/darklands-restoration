namespace Darklands.Core.Battles.Tactics;

/// <summary>The entries of the battle's Orders menu (DS:9607, 0x14-byte items; the enable byte is +2).</summary>
public enum OrdersItem
{
    /// <summary>DS:9609 "Resume" (Space).</summary>
    Resume = 0,

    /// <summary>DS:961D "&lt;name&gt; finished" (Enter): leave order mode.</summary>
    Finished = 1,

    /// <summary>DS:9631 "Enemy Info" (E).</summary>
    EnemyInfo = 2,

    /// <summary>DS:9645 "Walk towards" (W).</summary>
    Walk = 3,

    /// <summary>DS:9659 "Flee towards" (F).</summary>
    Flee = 4,

    /// <summary>DS:966D "Halt" (H).</summary>
    Halt = 5,

    /// <summary>DS:9681 "Travel As Group" (G).</summary>
    Group = 6,

    /// <summary>DS:9695 "Travel Single File" (Q).</summary>
    SingleFile = 7,

    /// <summary>DS:96A9 "Use Door" (U).</summary>
    UseDoor = 8,

    /// <summary>DS:96BD "Use Stairs" (U).</summary>
    UseStairs = 9,

    /// <summary>DS:96D1 "Open Chest" (O).</summary>
    OpenChest = 10,

    /// <summary>DS:96E5 "Pick Lock" (P).</summary>
    PickLock = 11,

    /// <summary>DS:96F9 "Dissolve Lock" (D): with Eater-Water.</summary>
    DissolveLock = 12,

    /// <summary>DS:970D "Disarm … trap" (D).</summary>
    DisarmTrap = 13,

    /// <summary>DS:9721 "Surrender (All)" (S).</summary>
    Surrender = 14,

    /// <summary>DS:9735 "Loot Bodies" (L).</summary>
    LootBodies = 15,

    /// <summary>DS:9749 "Exit Battlefield" (X).</summary>
    Exit = 16,

    /// <summary>DS:975D "Cancel Giving Order" (Esc).</summary>
    Cancel = 17,
}

/// <summary>The entries of the Attack menu (DS:97DB).</summary>
public enum AttackItem
{
    Throw = 0,
    Standard = 1,
    Vulnerable = 2,
    Berserk = 3,
    Parry = 4,
    Missile = 5,
}

/// <summary>
/// The enable state of the battle's menu bar (Game, Orders, Attack, Party) as the battle code
/// sets it — 1 enabled, 2 greyed — and the variable parts of the labels: the "(Ldr)" / "(All)"
/// suffixes, the "&lt;name&gt; finished" title and which "Disarm … trap" text item 13 shows. The
/// keys of the battle test the same bytes.
/// </summary>
// orig: DS:9419 (the bar) with DS:9607 (Orders), DS:97DB (Attack), DS:987D (Party), DS:991E (Game) set up by R000+0x083E
// R000+0x083E (0E76:083E) and battle_menus_enable R000+0x0F04 draft
public sealed class BattleMenus
{
    /// <summary>The "(Ldr)" and "(All)" suffixes (DS:073E, DS:0744).</summary>
    public const string Leader = "(Ldr)", All = "(All)";

    /// <summary>The Orders items' enable bytes (+2 of each record).</summary>
    public byte[] Orders { get; } = new byte[18];

    /// <summary>The Attack items' enable bytes.</summary>
    public byte[] Attack { get; } = new byte[6];

    /// <summary>DS:9429 / DS:9433 / DS:943D: the Orders, Attack and Party titles (1 the menu opens, 2 greyed).</summary>
    public byte OrdersMenu { get; set; } = 1;

    public byte AttackMenu { get; set; } = 2;

    public byte PartyMenu { get; set; } = 1;

    /// <summary>DS:987F / DS:9893: Party Info, Change Marching Order; DS:9920 / DS:9934: Save Game, Load Saved Game.</summary>
    public byte PartyInfo { get; set; } = 1;

    public byte MarchingOrder { get; set; } = 1;

    public byte SaveGame { get; set; } = 1;

    public byte LoadGame { get; set; } = 1;

    /// <summary>The suffixes of Use Door (DS:06AB + 8), Use Stairs (DS:06BB + 10), Open Chest (DS:06CD + 10), Pick Lock (DS:06DF + 9), Dissolve Lock (DS:06F1 + 13) and Exit Battlefield (DS:0727 + 16).</summary>
    public string DoorSuffix { get; set; } = Leader;

    public string StairsSuffix { get; set; } = Leader;

    public string ChestSuffix { get; set; } = Leader;

    public string PickSuffix { get; set; } = Leader;

    public string DissolveSuffix { get; set; } = Leader;

    public string ExitSuffix { get; set; } = Leader;

    /// <summary>DS:C107: the member whose orders are given ("Gretch" of "Gretch finished").</summary>
    public string Giver { get; set; } = "?";

    /// <summary>DS:970B: the DS offset of item 13's text (0x5B2 / 0x5C6 / 0x5DC disarm simple/moderate/complex, +0x40 the "(Ldr)" forms).</summary>
    public int DisarmText { get; set; } = 0x60C;

    /// <summary>DS:97DD state of the Throw entry as R019's list leaves it, and the list itself (DS:9A47).</summary>
    public List<(int Potion, int Quality, int Count)> ThrowLines { get; } = [];

    public byte Item(OrdersItem item) => Orders[(int)item];

    public void Set(OrdersItem item, byte on) => Orders[(int)item] = on;

    public bool On(OrdersItem item) => Orders[(int)item] == 1;

    public bool On(AttackItem item) => Attack[(int)item] == 1;
}
