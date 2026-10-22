using Darklands.Core.Characters;
using Darklands.Core.Economy;
using Darklands.Core.Items;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Core.Rules;

/// <summary>The five equipment slots, in the game's order.</summary>
// orig: equipped_type R031+0x5CB0 (18E7:1460) matched
public enum EquipmentSlot
{
    Weapon = 0,
    VitalArmor = 1,
    LimbArmor = 2,
    Shield = 3,
    MissileWeapon = 4,
}

/// <summary>
/// The members' packs: counting, adding, taking and losing items, and what is equipped.
/// Stacks are {code, type, quality, count, weight}; a pack holds at most 64 stacks and a
/// stack at most 255 items. <c>who</c>: 0..4, −1 the leader, −2 every present member.
/// </summary>
public static class Inventory
{
    public const int MaxStacks = 64;
    public const int MaxCount = 255;

    /// <summary>The item code of a stack without its marker bits.</summary>
    private static int CodeOf(ItemStack stack) => stack.Code & 0xFFF;

    private static Pack Pack(GameState s, int member) => s.Party.Slot(member).Pack;

    /// <summary>The members −2 means: present members with something in their pack.</summary>
    private static IEnumerable<int> Carriers(GameState s) =>
        Enumerable.Range(0, s.Party.Members.Count).Where(i => s.Party.PresentMember(i) is not null && s.Party.Members[i].Pack.Stacks > 0);

    private static IEnumerable<int> PresentMembers(GameState s) =>
        Enumerable.Range(0, s.Party.Members.Count).Where(i => s.Party.PresentMember(i) is not null);

    // ---- counting -------------------------------------------------------------------

    /// <summary>
    /// How many items of <paramref name="code"/> are carried (<c>count_items</c>). The shop and
    /// stash lists the original also counts for who ≥ 5 are not modelled (0).
    /// </summary>
    // orig: count_items R029+0x0C76 (0E76:0C76) matched; tests: PracticeAndItemsTests
    // DIVERGENCE: who >= 5 counts the 16-entry lists at R136+0xE68E (with the quantities read from inventories[who - 5], an original bug); not modelled, 0. The lists are enemy_items (the last battle's enemies' items, battle state); no caller passes who >= 5 (the cards pass -2, -1 or a member).
    public static int Count(GameState s, int who, int code) => Count(s, who, st => CodeOf(st) == code);

    /// <summary>How many carried items have any of the kind bits in <paramref name="mask"/> (<c>count_items_flags</c>).</summary>
    // orig: count_items_flags R029+0x0DD8 (0E76:0DD8) matched
    public static int CountWithFlags(GameState s, int who, uint mask) => Count(s, who, st => (s.Item(CodeOf(st)).Flags & mask) != 0);

    private static int Count(GameState s, int who, Func<ItemStack, bool> match)
    {
        if (who >= Party.MaxMembers)
        {
            return 0;
        }
        var members = who == Who.Everybody ? Carriers(s) : [s.Party.Resolve(who)];
        return members.SelectMany(i => Pack(s, i)).Where(match).Sum(st => st.Count);
    }

    /// <summary>
    /// The best quality of the carried items with any of the bits in <paramref name="mask"/>
    /// (<c>best_item_quality</c>); who ≥ 5 looks at the first 16 stacks of member who − 5.
    /// </summary>
    // orig: best_item_quality R029+0x0F54 (0E76:0F54) matched
    // C: who >= 5 reads the first 16 entries of member who - 5 whatever its stack count (stale entries included).
    public static int BestQualityWithFlags(GameState s, int who, uint mask)
    {
        IEnumerable<ItemStack> stacks = who switch
        {
            Who.Everybody => Carriers(s).SelectMany(i => Pack(s, i)),
            >= Party.MaxMembers => Pack(s, who - Party.MaxMembers).Entries.Take(16),
            _ => Pack(s, s.Party.Resolve(who)),
        };
        return stacks.Where(st => (s.Item(CodeOf(st)).Flags & mask) != 0).Select(st => st.Quality).DefaultIfEmpty(0).Max();
    }

    /// <summary>All items of exactly <paramref name="code"/> the present members carry.</summary>
    // orig: party_item_total R029+0x1128 (0E76:1128) matched
    public static int PartyTotal(GameState s, int code) =>
        Carriers(s).SelectMany(i => Pack(s, i)).Where(st => st.Code == code).Sum(st => st.Count);

    /// <summary>
    /// 1 if the member carries <paramref name="code"/>, 2 if the member knows a formula for it
    /// (potions), else 0 (<c>has_item</c>). Faithful quirk: the formula test reads the byte at
    /// 2E38:075F + member × 22 + code, the formula table for potion codes; for other codes it lands
    /// in another member's formulas, below the table in member 4's pack entries 48..63, or above it
    /// in the saint tables (<see cref="Party.MemoryByte"/>). Any bit counts, not only the three formula bits.
    /// </summary>
    // orig: has_item R029+0x118E (0E76:118E) matched; tests: PracticeAndItemsTests
    // DIVERGENCE: has_item does not resolve -1 (DS:9CD3[-1] is the record before slot 0); here -1 is the leader.
    // C: `if (R136+0xE08F[who][code])`: one byte of segment 2E38 at 0x75F + who * 0x16 + code (packs, formulas, saints; Party.MemoryByte).
    public static int Has(GameState s, int who, int code)
    {
        who = s.Party.Resolve(who);
        if (Pack(s, who).Any(st => CodeOf(st) == code))
        {
            return 1;
        }
        return FormulaByte(s, who * Potions.Count + code - Potions.FirstPotion) ? 2 : 0;
    }

    /// <summary>The member (−2: any present member) knows a formula of potion group <paramref name="potion"/> (0..21 from code 0x5F).</summary>
    // orig: knows_item_type R029+0x11FA (0E76:11FA) matched
    public static bool KnowsPotion(GameState s, int who, int potionCode)
    {
        who = s.Party.Resolve(who);
        return who == Who.Everybody
            ? PresentMembers(s).Any(i => FormulaByte(s, i * Potions.Count + potionCode - Potions.FirstPotion))
            : FormulaByte(s, who * Potions.Count + potionCode - Potions.FirstPotion);
    }

    /// <summary>Byte <paramref name="index"/> from the start of the five members' formula tables (22 bytes each) is not zero; outside them it is the bytes around (<see cref="Party.MemoryByte"/>).</summary>
    private static bool FormulaByte(GameState s, int index) => s.Party.MemoryByte(Party.FormulaByteOffset(0, index)) != 0;

    /// <summary>The best quality of <paramref name="code"/> carried (<c>item_quality</c>).</summary>
    // orig: item_quality_impl R031+0x5E48 (18E7:15F8) matched
    public static int BestQuality(GameState s, int who, int code)
    {
        var members = who == Who.Everybody ? Carriers(s) : [s.Party.Resolve(who)];
        return members.SelectMany(i => Pack(s, i)).Where(st => st.Code == code).Select(st => st.Quality).DefaultIfEmpty(0).Max();
    }

    /// <summary>
    /// <c>item_quality_flags</c>. Original bug: it compares the kind bits of item 0 instead of
    /// each stack's, so it is the best quality of everything carried when item 0's bits equal
    /// <paramref name="flags"/>, else 0.
    /// </summary>
    // orig: item_quality_flags_impl R031+0x5F6C (18E7:171C) matched
    // BUG: item_defs[0]->flags is tested for every stack (FixItemQualityByFlags).
    public static int BestQualityByFlags(GameState s, int who, uint flags)
    {
        var fixedTest = s.Behaviour.FixItemQualityByFlags;
        if (!fixedTest && s.Item(0).Flags != flags)
        {
            return 0;
        }
        var members = who == Who.Everybody ? Carriers(s) : [s.Party.Resolve(who)];
        return members.SelectMany(i => Pack(s, i)).Where(st => !fixedTest || s.Item(CodeOf(st)).Flags == flags)
            .Select(st => st.Quality).DefaultIfEmpty(0).Max();
    }

    /// <summary>The code of the first carried item with any bit of <paramref name="mask"/>, 0 if none.</summary>
    // orig: R031+0x57FC (18E7:0FAC) matched
    public static int FirstWithFlags(GameState s, int who, uint mask)
    {
        var members = who == Who.Everybody ? PresentMembers(s) : [s.Party.Resolve(who)];
        return members.SelectMany(i => Pack(s, i)).FirstOrDefault(st => (s.Item(st.Code).Flags & mask) != 0)?.Code ?? 0;
    }

    /// <summary>A random item code (of the first 172) whose kind bits are exactly <paramref name="flags"/>; −1 if none.</summary>
    // orig: random_item_flags R031+0x60E4 (18E7:1894) matched
    // DIVERGENCE: rand_mod(n) counts n < 2 as 2, so with 0 or 1 matching items k can pass the last one and the original falls off the loop (AX undefined); here -1.
    public static int RandomItemWithFlags(GameState s, uint flags)
    {
        var codes = Enumerable.Range(0, 172).Where(i => s.Item(i).Flags == flags).ToList();
        var k = s.Random.Below(codes.Count);
        return k < codes.Count ? codes[k] : -1;
    }

    // ---- adding ---------------------------------------------------------------------

    /// <summary>One item of default quality (<c>add_item</c>).</summary>
    // orig: add_item_impl R031+0x4978 (18E7:0128) matched
    public static int Add(GameState s, int who, int code) => AddMany(s, who, code, 1);

    /// <summary><paramref name="count"/> items of default quality (<c>add_items</c>).</summary>
    // orig: add_items_impl R031+0x4930 (18E7:00E0) matched
    // C: the stack's type, quality and weight are the item record's signed chars stored in unsigned bytes.
    public static int AddMany(GameState s, int who, int code, int count)
    {
        var d = s.Item(code);
        return AddStack(s, who, new ItemStack(code, (byte)d.Type, (byte)d.Quality, (byte)count, (byte)d.Weight));
    }

    /// <summary>One item of the given quality.</summary>
    // orig: add_item_q R031+0x49BE (18E7:016E) matched
    public static int AddWithQuality(GameState s, int who, int code, int quality)
    {
        var d = s.Item(code);
        return AddStack(s, who, new ItemStack(code, (byte)d.Type, (byte)quality, 1, (byte)d.Weight));
    }

    /// <summary>
    /// Adds a stack, joining an equal stack (code, type and quality) when the total stays
    /// within 255 (<c>add_item_rec</c>); returns the member's stack count. Original bug (−2):
    /// each present member gets the items twice, joined to an equal stack <em>and</em> appended
    /// as a new one.
    /// </summary>
    // orig: add_item_rec_impl R031+0x4A04 (18E7:01B4) draft; tests: PracticeAndItemsTests
    // C: checked against the asm (01b4 differs in a byte temp only): code, type and quality compare exactly, cnt + it.cnt <= 0xFF; -1 refuses a 65th stack with the count.
    // BUG: (-2) the stack is joined and appended too (FixPartyItemsAddedTwice).
    // DIVERGENCE: (-2) the original returns party[5].nitems (a byte after the records); here the last member's count.
    public static int AddStack(GameState s, int who, ItemStack stack)
    {
        who = s.Party.Resolve(who);
        if (who == Who.Everybody)
        {
            var last = 0;
            foreach (var i in PresentMembers(s))
            {
                var pack = Pack(s, i);
                var equal = pack.FindIndex(st => Joins(st, stack));
                if (equal >= 0)
                {
                    pack[equal] = pack[equal] with { Count = pack[equal].Count + stack.Count };
                }
                // C: n = nitems before the join; `if (n < 0x40) { items[n] = it; nitems++; }` (Pack.Add: n is the count, unchanged by the join).
                if (equal < 0 || !s.Behaviour.FixPartyItemsAddedTwice)
                {
                    pack.Add(stack);
                }
                last = pack.Stacks;
            }
            return last;
        }
        var own = Pack(s, who);
        var join = own.FindIndex(st => Joins(st, stack));
        if (join >= 0)
        {
            own[join] = own[join] with { Count = own[join].Count + stack.Count };
            return own.Stacks;
        }
        // C: appended while nitems < 0x40 (signed): a count of -1 (keep_items_pct on an empty pack) writes entry -1, which is the
        // C: previous member's entry 63 (2E38:0038 for member 0), and the count becomes 0: the item is lost.
        // BUG: a 65th stack is dropped without a word (quest items too): nothing tries another member, and the callers do not test the pack.
        own.Add(stack);
        return own.Stacks;
    }

    private static bool Joins(ItemStack st, ItemStack add) =>
        st.Code == add.Code && st.Type == add.Type && st.Quality == add.Quality && st.Count + add.Count <= MaxCount;

    // ---- taking ---------------------------------------------------------------------

    /// <summary>
    /// Takes one item of <paramref name="code"/> (−2: from the first present member who has
    /// one); returns the member's stack count, or −1 if there was none (<c>take_item</c>).
    /// </summary>
    // orig: take_item_impl R031+0x4D24 (18E7:04D4) matched
    public static int Take(GameState s, int who, int code)
    {
        who = s.Party.Resolve(who);
        var members = who == Who.Everybody ? PresentMembers(s) : [who];
        foreach (var i in members)
        {
            var at = Pack(s, i).FindIndex(st => st.Code == code);
            if (at >= 0)
            {
                return TakeAt(s, i, at);
            }
        }
        return -1;
    }

    /// <summary>Takes one item off stack <paramref name="index"/>; an emptied stack is unequipped and removed.</summary>
    // orig: item_take_one R031+0x4CC4 (18E7:0474) matched; tests: PracticeAndItemsTests
    // C: --cnt on an unsigned char: a count of 0 becomes 255 and the stack stays.
    public static int TakeAt(GameState s, int who, int index)
    {
        who = s.Party.Resolve(who);
        var pack = Pack(s, who);
        var stack = pack[index];
        if ((byte)(stack.Count - 1) != 0)
        {
            pack[index] = stack with { Count = (byte)(stack.Count - 1) };
            return pack.Stacks;
        }
        pack[index] = stack with { Count = 0 };
        Unequip(s, who, index);
        pack.RemoveAt(index);
        return pack.Stacks;
    }

    /// <summary>Takes one item with any kind bit of <paramref name="mask"/>; the stack count, or 0 if none.</summary>
    // orig: R031+0x5664 (18E7:0E14) matched
    public static int TakeOneWithFlags(GameState s, int who, uint mask)
    {
        who = s.Party.Resolve(who);
        var members = who == Who.Everybody ? PresentMembers(s) : [who];
        foreach (var i in members)
        {
            var at = Pack(s, i).FindIndex(st => (s.Item(st.Code).Flags & mask) != 0);
            if (at >= 0)
            {
                return TakeAt(s, i, at);
            }
        }
        return 0;
    }

    /// <summary>
    /// Removes the first stack of <paramref name="code"/> (−2: from every present member);
    /// the stack count, or −1 if there was none (<c>remove_item_stack</c>).
    /// </summary>
    // orig: remove_item_stack_impl R031+0x4EB8 (18E7:0668) matched
    // DIVERGENCE: (-2) with nobody carrying the code the original returns the uninitialised r; here -1.
    public static int RemoveStack(GameState s, int who, int code)
    {
        who = s.Party.Resolve(who);
        var members = who == Who.Everybody ? PresentMembers(s).ToList() : [who];
        var result = -1;
        foreach (var i in members)
        {
            var at = Pack(s, i).FindIndex(st => st.Code == code);
            if (at >= 0)
            {
                result = RemoveStackAt(s, i, at);
                if (who != Who.Everybody)
                {
                    break;
                }
            }
        }
        return result;
    }

    /// <summary>
    /// Removes stack <paramref name="index"/>, unequipping it. Faithful quirk: an index at or
    /// past the stack count unequips by the stale entry there and only shortens the count: the
    /// last stack drops out but stays in its entry, and the next deletion before it shifts it
    /// back in.
    /// </summary>
    // orig: item_remove_stack R031+0x5024 (18E7:07D4) matched; item_delete R031+0x4C56 (18E7:0406) matched
    // C: item_unequip(who, idx, 9) reads items[idx] whatever the count (a stale entry: its code's flags, type and quality against
    // C: the equipped ones), then item_delete shifts items[idx + 1 .. nitems] down and decrements the signed count (Pack.RemoveAt).
    // DORMANT: item_remove_stack(-2) (every present member, returning an uninitialised sum) has no caller among the ported rules.
    public static int RemoveStackAt(GameState s, int who, int index)
    {
        who = s.Party.Resolve(who);
        var pack = Pack(s, who);
        Unequip(s, who, index);
        pack.RemoveAt(index);
        return pack.Stacks;
    }

    /// <summary>Removes every stack with any kind bit of <paramref name="mask"/> (<c>remove_items_flags</c>); the stack count (0 for −2).</summary>
    // orig: remove_items_flags_impl R031+0x549A (18E7:0C4A) matched
    public static int RemoveWithFlags(GameState s, int who, uint mask)
    {
        who = s.Party.Resolve(who);
        var members = who == Who.Everybody ? PresentMembers(s).ToList() : [who];
        foreach (var i in members)
        {
            var pack = Pack(s, i);
            for (var k = 0; k < pack.Count; k++)
            {
                if ((s.Item(pack[k].Code).Flags & mask) != 0)
                {
                    Unequip(s, i, k);
                    pack.RemoveAt(k);
                    k--;
                }
            }
        }
        return who == Who.Everybody ? 0 : Pack(s, who).Stacks;
    }

    /// <summary>
    /// Loses things (<c>lose_items</c>): stacks heavier than 2 always, weight 2 with chance
    /// (Agility + Stealth) / 4 %, lighter ones with (Agility + Stealth) / 2 %. −2: every present
    /// member, and the purse is emptied.
    /// </summary>
    // orig: lose_items_impl R031+0x50A4 (18E7:0854) matched
    // C: weight * cnt == 2 and < 2 draw rand_mod(100) only when the weight test holds (&&); -2 empties the purse first.
    public static void Lose(GameState s, int who)
    {
        who = s.Party.Resolve(who);
        if (who == Who.Everybody)
        {
            s.Party.Purse = Money.Zero;
            foreach (var i in PresentMembers(s).ToList())
            {
                LoseFrom(s, i);
            }
            return;
        }
        LoseFrom(s, who);
    }

    private static void LoseFrom(GameState s, int member)
    {
        var m = s.Party.Slot(member);
        var care = (m[Attribute.Agility] + m[Skill.Stealth]) / 2;
        var pack = m.Pack;
        for (var i = 0; i < pack.Count;)
        {
            var load = pack[i].Weight * pack[i].Count;
            if (load > 2 || (load == 2 && s.Random.Below(100) < care / 2) || (load < 2 && s.Random.Below(100) < care))
            {
                RemoveStackAt(s, member, i);
            }
            else
            {
                i++;
            }
        }
    }

    /// <summary>
    /// Each stack survives with <paramref name="percent"/> % (<c>keep_items_pct</c>; −2: every
    /// present member). Original bug: the loop starts one past the last stack, so the first
    /// failed roll drops the last stack (unequipping by the stale entry after it), and on an
    /// empty pack it makes the count −1, so the next item added is lost.
    /// </summary>
    // orig: keep_items_pct_impl R031+0x5308 (18E7:0AB8) matched; tests: PracticeAndItemsTests
    // BUG: i starts at nitems (FixKeepItemsOffByOne).
    // C: i starts at the signed char nitems; an empty pack runs once with i = 0 and a failed roll leaves nitems = -1.
    public static void KeepEach(GameState s, int who, int percent)
    {
        who = s.Party.Resolve(who);
        var members = who == Who.Everybody ? PresentMembers(s).ToList() : [who];
        foreach (var i in members)
        {
            for (var k = Pack(s, i).Stacks - (s.Behaviour.FixKeepItemsOffByOne ? 1 : 0); k >= 0; k--)
            {
                if (s.Random.Below(100) > percent)
                {
                    RemoveStackAt(s, i, k);
                }
            }
        }
    }

    // ---- equipment ------------------------------------------------------------------

    private static EquippedItem SlotItem(Character m, EquipmentSlot slot) => slot switch
    {
        EquipmentSlot.Weapon => m.Weapon,
        EquipmentSlot.VitalArmor => m.VitalArmor,
        EquipmentSlot.LimbArmor => m.LimbArmor,
        EquipmentSlot.Shield => m.Shield,
        EquipmentSlot.MissileWeapon => m.MissileWeapon,
        _ => EquippedItem.None,
    };

    /// <summary>The item type in an equipment slot (−1: none).</summary>
    // orig: equipped_type R031+0x5CB0 (18E7:1460) matched
    // DORMANT: equipped_type has no default case (slot outside 0..4 returns AX).
    public static int EquippedType(GameState s, int who, EquipmentSlot slot) => SlotItem(s.Party.Slot(who), slot).Type;

    /// <summary>
    /// The item code in an equipment slot, −1 if none (<c>equipped_item</c>): the stack of that
    /// type and quality, else the last stack of that type. Leaves the stack index in
    /// <see cref="PartyQueryResult.Skill"/> as the original does.
    /// </summary>
    // orig: equipped_item_impl R031+0x5D06 (18E7:14B6) draft
    // C: checked against the asm (14b6 differs in store order only; an earlier port's check reproduced that order with the copy
    //   sequence type = i; i = t; q = type; type = i, quality I and type T unchanged): the equipped type and quality are signed chars compared with the stack's unsigned bytes (no game value reaches 0x80).
    // DIVERGENCE: equipped_item does not resolve -1 (party[-1] is the bytes before the records); here -1 is the leader.
    public static int Equipped(GameState s, int who, EquipmentSlot slot)
    {
        var m = s.Party.Slot(who);
        var item = SlotItem(m, slot);
        if (item.IsEmpty)
        {
            return -1;
        }
        var found = -1;
        for (var i = 0; i < m.Pack.Count; i++)
        {
            if (m.Pack[i].Type == item.Type)
            {
                found = i;
                s.Party.Last.Skill = i;
                if (m.Pack[i].Quality == item.Quality)
                {
                    return m.Pack[i].Code;
                }
            }
        }
        return found != -1 ? m.Pack[found].Code : -1;
    }

    /// <summary>Takes stack <paramref name="index"/> out of its equipment slot if it is the equipped one.</summary>
    // orig: item_unequip R031+0x5B38 (18E7:12E8) matched; tests: PracticeAndItemsTests
    // C: slot 6 (give_one/give_stack: idx is an equipment slot) is not used by these rules; flags of the masked code 0xF hand weapon, 0x8000030 missile weapon.
    public static void Unequip(GameState s, int who, int index)
    {
        var m = s.Party.Slot(who);
        var stack = m.Pack[index];
        var flags = s.Item(CodeOf(stack)).Flags;
        bool Is(EquippedItem e) => e.Type == stack.Type && e.Quality == stack.Quality;
        if ((flags & ItemFlags.HandWeapon) != 0 && Is(m.Weapon))
        {
            m.Weapon = m.Weapon with { Type = -1 };
        }
        else if ((flags & ItemFlags.MissileWeapon) != 0 && Is(m.MissileWeapon))
        {
            m.MissileWeapon = m.MissileWeapon with { Type = -1 };
        }
        else if (Is(m.VitalArmor))
        {
            m.VitalArmor = m.VitalArmor with { Type = -1 };
        }
        else if (Is(m.LimbArmor))
        {
            m.LimbArmor = m.LimbArmor with { Type = -1 };
        }
        else if (Is(m.Shield))
        {
            m.Shield = m.Shield with { Type = -1 };
        }
        EquipmentWeight(s, who);
    }

    /// <summary>Recomputes the weight of the equipped items (0..500, kept in a byte) into the encumbrance.</summary>
    // orig: equip_weight R031+0x5C5A (18E7:140A) matched
    // C: clamp_range(0, 500, w) stored in the signed byte +0x35 and returned from the char (500 -> -12).
    public static int EquipmentWeight(GameState s, int who)
    {
        var weight = 0;
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
        {
            if (Equipped(s, who, slot) is var code and not -1)
            {
                weight += s.Item(code).Weight;
            }
        }
        return s.Party.Slot(who).Encumbrance = (sbyte)Math.Clamp(weight, 0, 500);
    }
}
