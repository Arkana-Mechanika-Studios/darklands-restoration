using Darklands.Content.Formats.Graphics;

namespace Darklands.Content.Formats.Battles;

/// <summary>The combatants' animation files, loaded on first use from the catalogue of their picture code.</summary>
// orig: sprite_file_load R014+0x3D2C (0E76:3D2C) draft (catalogue by the first letter;
// file name = code + "WK"/"CB" + weapon code, or code + "DY")
public sealed class CombatAnimations(GameFiles files)
{
    private readonly Dictionary<string, CatArchive> _catalogues = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CombatAnimation?> _loaded = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The animation "code + posture (+ weapon)" (F60WKSW, E02CBSW, M01DY), or null when the catalogue lacks it.</summary>
    public CombatAnimation? Load(string code, string posture, string weapon)
    {
        var name = code + posture + (posture.Equals("DY", StringComparison.OrdinalIgnoreCase) ? "" : weapon) + ".IMC";
        if (_loaded.TryGetValue(name, out var cached))
        {
            return cached;
        }
        var (file, catalogue) = Catalogue(code);
        CombatAnimation? animation = null;
        if (catalogue is not null && catalogue.Entries.Any(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            animation = CombatAnimation.Decode(CatMemberDecoder.Decode(catalogue.ReadStored(name), 0x20000), $"{file}/{name.ToUpperInvariant()}");
        }
        _loaded[name] = animation;
        return animation;
    }

    private (string? File, CatArchive? Catalogue) Catalogue(string code)
    {
        var file = char.ToUpperInvariant(code[0]) switch
        {
            'A' => "A00C.CAT",
            'C' => "C00C.CAT",
            'E' => "E00C.CAT",
            'M' => "M00C.CAT",
            'F' => code.Length > 1 && code[1] == '6' ? "F60C.CAT" : "F01C.CAT",
            _ => null,
        };
        if (file is null || !files.Exists(file))
        {
            return (file, null);
        }
        if (!_catalogues.TryGetValue(file, out var catalogue))
        {
            catalogue = CatArchive.Decode(files.Read(file));
            _catalogues[file] = catalogue;
        }
        return (file, catalogue);
    }
}
