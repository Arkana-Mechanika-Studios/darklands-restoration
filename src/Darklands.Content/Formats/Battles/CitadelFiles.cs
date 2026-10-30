using Darklands.Content.Formats.Graphics;
using Darklands.Core.Battles.Generation;

namespace Darklands.Content.Formats.Battles;

/// <summary>The members of LCASTLE (Baphomet's citadel: a level, tile description and group list per room), decoded.</summary>
// orig: data-file-choose("lcastle") in R013+0x0000; file-read-whole root 05D9:019A decodes a member
public sealed class CitadelFiles(CatArchive archive) : ICitadelFiles
{
    public static CitadelFiles Load(GameFiles files) => new(CatArchive.Decode(files.Read("LCASTLE")));

    public byte[] Read(string member) => CatMemberDecoder.Decode(archive.ReadStored(member), 0x10000);
}
