using Darklands.Content.Formats.Graphics;
using Darklands.Core.Battles;
using Darklands.Core.Battles.Generation;

namespace Darklands.Content.Formats.Battles;

/// <summary>The prepared battlefield maps of IMAPS.CAT, decoded to floor images.</summary>
// orig: data-file-choose("imaps.cat") in battlefield-builder-entry R004+0x0000; file-read-whole root 05D9:019A decodes a member
public sealed class PreparedMaps(CatArchive archive) : IBattlefieldMaps
{
    public static PreparedMaps Load(GameFiles files) => new(CatArchive.Decode(files.Read("IMAPS.CAT")));

    public BattlefieldLevel Load(string member)
    {
        var image = new byte[BattlefieldLevel.ImageSize];
        var decoded = CatMemberDecoder.Decode(archive.ReadStored(member), 0x10000);
        decoded.AsSpan(0, Math.Min(decoded.Length, image.Length)).CopyTo(image);
        return new BattlefieldLevel(image);
    }
}
