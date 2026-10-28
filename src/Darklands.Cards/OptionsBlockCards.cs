namespace Darklands.Cards;

/// <summary>
/// The cards whose loop shows its menu on the block <c>options_block</c> holds (<c>card_options(options_block)</c>):
/// their <see cref="CardMenu.Block"/> is the game's one <see cref="CardContext.OptionsBlock"/>, which later cards may read.
/// </summary>
// orig: options_block DS:EE53; the card functions below (reference C: `card_options(options_block)` in the loop; R081: push [DS:EE53] at R081+0x21D9)
internal static class OptionsBlockCards
{
    private static readonly HashSet<string> Cards = new(StringComparer.Ordinal)
    {
        "AlchemyMishap", // R089+0x04F8
        "AmbushOnThePilgrimPath", // R123+0x2036
        "ApocalypseDragon", // R131+0x0DC0
        "AssassinsAmbush", // R070+0x090E
        "AstrologersManor", // R117+0x0000
        "BanditAmbushOnTheRoad", // R108+0x2076
        "BanditsNearTheCamp", // R134+0x152C
        "BearCave", // R104+0x0000
        "BishopsRoadTithe", // R110+0x0000
        "CryptUnderTheRuinedChurch", // R108+0x0000
        "DwarfInTheCliff", // R107+0x0000
        "DwarfKingsRedCap_MineB03", // R101+0x0850
        "DwarfWantsPotions_MineB00", // R100+0x0BE0
        "DwarfsPitTraps", // R066+0x2FDC
        "DwarfsPoisonMist", // R107+0x0E76
        "FamineHorseman", // R133+0x0000
        "FortressDungeonCell", // R120+0x0970
        "FortressGuardpost", // R119+0x150C
        "FriarSellingPardons", // R075+0x1864
        "GiantSpiderNest", // R112+0x165C
        "HermitByTheFire", // R114+0x0000
        "HighSabbatField", // R124+0x165C
        "HuntsmanAtTheCamp", // R134+0x0C1C
        "HussiteBand", // R114+0x09B4
        "HutOfTheBlondeWoman", // R116+0x0E3C
        "ImprisonedMonk", // R119+0x0000
        "InnocentsAtTheWitchesFeast", // R127+0x07C6
        "LancersOfWar", // R133+0x074A
        "MediciBank", // R061+0x0978
        "MercenaryBanditAmbush", // R108+0x3028
        "MerchantCaravanOnTheRoad", // R109+0x0000
        "MerchantHouseOffices", // R108+0x0ED0
        "NoblemansRoadToll", // R132+0x034E
        "PaganAltarOnTheHill", // R107+0x1962
        "PeatBog", // R113+0x0B8C
        "PileOfBurnedBooks", // R122+0x159E
        "RefugeesFromThePlague", // R109+0x21A2
        "RivalDragonSlayer", // R082+0x0EA0
        "RiverPiratesAtNight", // R114+0x15D4
        "RobberKnightsMen", // R080+0x06DC
        "SabbatAltarPreparation", // R125+0x18A8
        "SabbatFairFromTheHill", // R123+0x14AA
        "SabbatFeastKitchen", // R124+0x22AC
        "SabbatField", // R126+0x0000
        "SabbatMockBaptism", // R125+0x0000
        "SabbatSummoning", // R126+0x1650
        "SabbatWitchFeast", // R126+0x2C0E
        "SchratsInTheForest", // R112+0x2760
        "SentryWolfAtTheWatchfire", // R124+0x0000
        "SpiderWarehousePoster", // R134+0x0000
        "TatzelwurmPack", // R112+0x049E
        "TemplarDoorGuard", // R120+0x2018
        "TemplarGraveyard", // R118+0x08AA
        "TemplarJailers", // R120+0x14EA
        "TemplarsBurningAFriar", // R117+0x0A80
        "TidalFloodInTheMarshes", // R114+0x207A
        "University_Unive00", // R056+0x088C
        "WitchPriestOrdination", // R122+0x0B2E
        "WitchesOfTheBlight", // R083+0x0000
        "WolvesAtTheWatchfireChain", // R124+0x0BDE
        "DragonBlightedLand", // R081+0x1848 (asm)
    };

    /// <summary>The card class shows its menu on <c>options_block</c>.</summary>
    public static bool Binds(string cardClass) => Cards.Contains(cardClass);
}
