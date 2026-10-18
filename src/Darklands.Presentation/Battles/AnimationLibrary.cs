using Darklands.Content.Formats.Battles;
using Darklands.Core.Battles.Tactics;

namespace Darklands.Presentation.Battles;

/// <summary>
/// The combatants' animation files for the battle rules (header values, step tables, HITSEQ
/// rows) and for drawing (the frames, recoloured for party members).
/// </summary>
// orig: sprite_file_load R014+0x3D2C (0E76:3D2C) draft (spec E §1.3); the party recolouring
// R014+0x1060A (1EC9:00DA) draft (0xEB..0xF2 → 80 + 8·slot)
public sealed class AnimationLibrary(BattleAssets assets) : IAnimationLibrary
{
    private readonly Dictionary<string, (int, int, int, int)> _sizes = ReadSizes(assets.Files.Read("TACANIM.DB"));
    private readonly Dictionary<(string, int), CombatAnimation?> _drawn = [];

    public AnimationInfo? Find(string code, string posture, string weapon)
    {
        if (assets.Animations.Load(code, posture, weapon) is not { } a)
        {
            return null;
        }
        var dying = posture.Equals("DY", StringComparison.OrdinalIgnoreCase);
        var hits = dying || weapon.Length == 0 ? null : assets.HitSequences.For(weapon);
        var moves = a.Header[12] != -1;
        return new AnimationInfo(code + posture + (dying ? "" : weapon), a.Header, moves ? a.StepX : null, moves ? a.StepY : null, a.FrameCount, hits);
    }

    // DIVERGENCE: the original ignores fread's result and, for a family missing from the file, keeps the last record read
    // (known original bug DL 0035); here a missing family keeps the sizes from the animation header.
    public (int HalfWidth, int HalfHeight, int CombatHalfWidth, int CombatHalfHeight)? Sizes(string family) =>
        _sizes.TryGetValue(family.ToUpperInvariant(), out var s) ? s : null;

    /// <summary>The file to draw for an animation slot (party slots recoloured to their colours 80 + 8·slot).</summary>
    public CombatAnimation? Drawn(int slot, string code, string posture, string weapon)
    {
        var name = code + posture + (posture.Equals("DY", StringComparison.OrdinalIgnoreCase) ? "" : weapon);
        var key = (name.ToUpperInvariant(), slot < 5 ? slot : -1);
        if (_drawn.TryGetValue(key, out var cached))
        {
            return cached;
        }
        var a = assets.Animations.Load(code, posture, weapon);
        if (a is not null && slot < 5)
        {
            var table = new byte[256];
            for (var i = 0; i < 256; i++)
            {
                table[i] = (byte)i;
            }
            for (var p = 0xEB; p <= 0xF2; p++)
            {
                table[p] = (byte)(0x50 + 8 * slot + (p - 0xEB));
            }
            a = a.Recolored(table);
        }
        _drawn[key] = a;
        return a;
    }

    /// <summary>TACANIM.DB: 14-byte records name[4], three buffer sizes, the four half sizes.</summary>
    // orig: R025+0x0D3C (1EF8:0D3C) draft (spec E §1.6)
    private static Dictionary<string, (int, int, int, int)> ReadSizes(byte[] data)
    {
        var sizes = new Dictionary<string, (int, int, int, int)>();
        for (var k = 0; k + 14 <= data.Length; k += 14)
        {
            var name = System.Text.Encoding.ASCII.GetString(data, k, 4).TrimEnd('\0').ToUpperInvariant();
            sizes.TryAdd(name, (data[k + 10], data[k + 11], data[k + 12], data[k + 13]));
        }
        return sizes;
    }
}
