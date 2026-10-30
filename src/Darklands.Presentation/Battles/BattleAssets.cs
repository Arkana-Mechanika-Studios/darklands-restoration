using Darklands.Content;
using Darklands.Content.Formats.Battles;
using Darklands.Content.Formats.Exe;
using Darklands.Content.Formats.Graphics;
using Darklands.Core.Battles;

namespace Darklands.Presentation.Battles;

/// <summary>Everything the battle screen draws with, loaded once from the player's game files.</summary>
public sealed class BattleAssets
{
    public BattleAssets(GameFiles files)
    {
        Files = files;
        Executable = DarklandsExecutable.Load(files);
        Tables = CombatTablesReader.Read(Executable);
        Enemies = EnemyCatalogueDecoder.Decode(files.Read("DARKLAND.ENM"));
        Maps = PreparedMaps.Load(files);
        Citadel = CitadelFiles.Load(files);
        TileSets = CatArchive.Decode(files.Read("BC"));
        Images = ImageBank.Decode(files.Read("BATTLEGR.IMG"), BattleImages.Count, "BATTLEGR.IMG");
        Common = CommonSprites.Decode(files.Read("COMMONSP.IMG"));
        Animations = new CombatAnimations(files);
        HitSequences = HitSequences.Load(files, Executable);
        EnemyPalettes = EnemyPalettes.Load(files);
        CommonColors = files.Read("COMNCLRS.DAT");
        BackgroundPalettes = files.Read("BKGNDPAL.DAT");
        Items = WorldData.Load(files).ItemDefinitions;
    }

    /// <summary>The item definitions (DARKLAND.LST), for the enemies' equipment.</summary>
    public IReadOnlyList<Core.Items.ItemDefinition> Items { get; }

    public GameFiles Files { get; }

    public DarklandsExecutable Executable { get; }

    public CombatTables Tables { get; }

    public EnemyCatalogue Enemies { get; }

    public PreparedMaps Maps { get; }

    /// <summary>LCASTLE: the rooms of Baphomet's citadel.</summary>
    public CitadelFiles Citadel { get; }

    public CatArchive TileSets { get; }

    /// <summary>BATTLEGR.IMG (see <see cref="BattleImages"/>).</summary>
    public ImageBank Images { get; }

    /// <summary>COMMONSP.IMG: the pointer, the panel frames, box corners and backgrounds, the dead shield.</summary>
    public CommonSprites Common { get; }

    public CombatAnimations Animations { get; }

    public HitSequences HitSequences { get; }

    public EnemyPalettes EnemyPalettes { get; }

    public byte[] CommonColors { get; }

    public byte[] BackgroundPalettes { get; }

    private CatArchive? _einfo;
    private readonly Dictionary<string, Pic?> _portraits = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>An enemy's picture for the Enemy Info ("E02SWST.PIC" in EINFO.CAT), null when the catalogue lacks it.</summary>
    // orig: R021+0x03FE (1EF8:03FE) matched: data-file-choose("einfo.cat"), image-read
    public Pic? EnemyPortrait(string name)
    {
        if (_portraits.TryGetValue(name, out var pic))
        {
            return pic;
        }
        _einfo ??= CatArchive.Decode(Files.Read("EINFO.CAT"));
        pic = null;
        var entry = _einfo.Entries.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
        if (entry is not null)
        {
            var stored = _einfo.ReadStored(entry.Name);
            var asset = "EINFO.CAT/" + entry.Name.ToUpperInvariant();
            try
            {
                pic = new PicDecoder().Decode(asset, stored);
            }
            catch (InvalidDataException)
            {
                pic = new PicDecoder().Decode(asset, CatMemberDecoder.Decode(stored, 0x20000));
            }
        }
        _portraits[name] = pic;
        return pic;
    }
}
