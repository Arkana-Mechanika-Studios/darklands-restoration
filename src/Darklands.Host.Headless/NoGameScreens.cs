using Darklands.Audio;
using Darklands.Audio.Packs;
using Darklands.Content;
using Darklands.Platform;
using Darklands.Presentation;
using Darklands.Presentation.Graphics;
using Darklands.Rendering;

/// <summary>No other screens (the headless battle opens only the character sheet and the party information).</summary>
internal sealed class NoGameScreens : Darklands.Cards.IGameScreens
{
    public void Barter(int shop, string name, long categories) { }
    public void Camp(int mode) { }
    public void Alchemy(int member, int place) { }
    public void ItemStore() { }
    public void CharacterSheet(int member) { }
    public void PartyInfo() { }
    public void Loot(IList<Darklands.Core.Characters.ItemStack> items, Darklands.Core.Economy.Money money) { }
    public int Battle(Darklands.Cards.BattleRequest request) => 0;
}
