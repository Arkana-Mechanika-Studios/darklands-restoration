using Darklands.Core.Characters;
using Darklands.Core.Rules;

namespace Darklands.Cards;

/// <summary>Items for card scripts (item codes of DARKLAND.LST; flag masks see <see cref="Core.Items.ItemFlags"/>).</summary>
public sealed partial class CardContext
{
    /// <summary><c>item-tally</c>.</summary>
    // orig: item-tally R029+0x0C76 (0E76:0C76) matched
    public int CountItems(int who, int code) => Inventory.Count(State, who, code);

    /// <summary><c>item-tally-by-flags</c>.</summary>
    // orig: item-tally-by-flags R029+0x0DD8 (0E76:0DD8) matched
    public int CountItemsWithFlags(int who, uint mask) => Inventory.CountWithFlags(State, who, mask);

    /// <summary><c>top-quality-of-item</c>.</summary>
    // orig: top-quality-of-item R029+0x0F54 (0E76:0F54) matched
    public int BestQualityWithFlags(int who, uint mask) => Inventory.BestQualityWithFlags(State, who, mask);

    /// <summary>All items of a code the present members carry.</summary>
    // orig: party-item-sum R029+0x1128 (0E76:1128) matched
    public int PartyItemTotal(int code) => Inventory.PartyTotal(State, code);

    /// <summary><c>carries-item</c>: 1 carried, 2 formula known, 0 no.</summary>
    // orig: carries-item R029+0x118E (0E76:118E) matched
    public int HasItem(int who, int code) => Inventory.Has(State, who, code);

    /// <summary><c>give-one-item-form</c>.</summary>
    // orig: give-item-inner R031+0x4978 (18E7:0128) matched
    public void AddItem(int who, int code) => Inventory.Add(State, who, code);

    /// <summary><c>give-several-items</c>.</summary>
    // orig: give-several-items-inner R031+0x4930 (18E7:00E0) matched
    public void AddItems(int who, int code, int count) => Inventory.AddMany(State, who, code, count);

    /// <summary>One item of a quality (R031 give-item-with-quality).</summary>
    // orig: give-item-with-quality R031+0x49BE (18E7:016E) matched
    public void AddItemWithQuality(int who, int code, int quality) => Inventory.AddWithQuality(State, who, code, quality);

    /// <summary><c>give-item-record-form</c>.</summary>
    // orig: give-item-record-inner R031+0x4A04 (18E7:01B4) draft
    public int AddItemStack(int who, ItemStack stack) => Inventory.AddStack(State, who, stack);

    /// <summary><c>pick-up-item</c>: −1 if none.</summary>
    // orig: pick-up-item-inner R031+0x4D24 (18E7:04D4) matched
    public int TakeItem(int who, int code) => Inventory.Take(State, who, code);

    /// <summary>Takes one item off a pack slot (R031 item-take-single).</summary>
    // orig: item-take-single R031+0x4CC4 (18E7:0474) matched
    public int TakeItemAt(int who, int index) => Inventory.TakeAt(State, who, index);

    /// <summary><c>item-stack-take-out</c>: −1 if none.</summary>
    // orig: item-stack-take-out-inner R031+0x4EB8 (18E7:0668) matched
    public int RemoveItemStack(int who, int code) => Inventory.RemoveStack(State, who, code);

    /// <summary><c>drop-items</c> (−2 also empties the purse).</summary>
    // orig: drop-items-inner R031+0x50A4 (18E7:0854) matched
    public void LoseItems(int who) => Inventory.Lose(State, who);

    /// <summary><c>item-keep-percent</c>.</summary>
    // orig: item-keep-percent-inner R031+0x5308 (18E7:0AB8) matched
    public void KeepItems(int who, int percent) => Inventory.KeepEach(State, who, percent);

    /// <summary><c>items-take-out-by-flags</c>.</summary>
    // orig: items-take-out-by-flags-inner R031+0x549A (18E7:0C4A) matched
    public int RemoveItemsWithFlags(int who, uint mask) => Inventory.RemoveWithFlags(State, who, mask);

    /// <summary>Takes one item with a flag (R031 0e14).</summary>
    // orig: R031+0x5664 (18E7:0E14) matched
    public int TakeItemWithFlags(int who, uint mask) => Inventory.TakeOneWithFlags(State, who, mask);

    /// <summary>The first carried item code with a flag, 0 if none (R031 0fac).</summary>
    // orig: R031+0x57FC (18E7:0FAC) matched
    public int FirstItemWithFlags(int who, uint mask) => Inventory.FirstWithFlags(State, who, mask);

    /// <summary><c>worn-item</c>(who, slot 0 weapon, 1 vital armour, 2 limb armour, 3 shield, 4 missile weapon): −1 none.</summary>
    // orig: worn-item-inner R031+0x5D06 (18E7:14B6) draft
    public int EquippedItem(int who, int slot) => Inventory.Equipped(State, who, (EquipmentSlot)slot);

    /// <summary><c>item-grade</c>: the best quality of a code carried.</summary>
    // orig: item-grade-inner R031+0x5E48 (18E7:15F8) matched
    public int ItemQuality(int who, int code) => Inventory.BestQuality(State, who, code);

    /// <summary><c>item-grade-flags</c> (original bug: tests item 0's flags).</summary>
    // orig: item-grade-flags-inner R031+0x5F6C (18E7:171C) matched
    public int ItemQualityByFlags(int who, uint flags) => Inventory.BestQualityByFlags(State, who, flags);

    /// <summary>A random item code with exactly these flags (R031 random-item-flag-set).</summary>
    // orig: random-item-flag-set R031+0x60E4 (18E7:1894) matched
    public int RandomItemWithFlags(uint flags) => Inventory.RandomItemWithFlags(State, flags);
}
