using Darklands.Content.Formats.Graphics;
using Darklands.Content.Formats.Text;
using Darklands.Content.Formats.World;

namespace Darklands.Content;

/// <summary>
/// Decoded game assets, loaded on first use and kept: pictures, fonts and MSG card texts.
/// </summary>
public sealed class GameAssets(GameFiles files)
{
    private readonly Dictionary<string, Pic> _pictures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MessageFile> _messages = new(StringComparer.OrdinalIgnoreCase);
    private Fnt? _fonts;
    private MsgFilesArchive? _messageArchive;
    private Saints? _saints;

    public GameFiles Files => files;

    /// <summary>A picture from the PICS folder by base name ("startscr") or file name.</summary>
    public Pic Picture(string name)
    {
        var file = name.Contains('.') ? name : name + ".PIC";
        if (!_pictures.TryGetValue(file, out var pic))
        {
            var path = files.Exists("PICS\\" + file) ? "PICS\\" + file : file;
            pic = new PicDecoder().Decode(path.ToUpperInvariant(), files.Read(path));
            _pictures[file] = pic;
        }
        return pic;
    }

    /// <summary>The shared sprites of COMMONSP.IMG (panel frames, box corners, backgrounds), loaded once.</summary>
    // orig: R000+0x1010 (0E76:1010) draft (loaded once at start-up)
    public CommonSprites CommonSprites => _common ??= CommonSprites.Decode(files.Read("COMMONSP.IMG"));

    private CommonSprites? _common;

    public bool PictureExists(string name)
    {
        var file = name.Contains('.') ? name : name + ".PIC";
        return files.Exists("PICS\\" + file) || files.Exists(file);
    }

    /// <summary>
    /// The font in a graphics-driver font slot (1..3). The driver fills slot n with font n−1
    /// of FONTS.FNT.
    /// </summary>
    // orig: the driver loader root 047C:07CE("mgraphic.exe", "fonts.fnt") (called from R000+0x052A (0E76:052A) game start) and font_load root 0617:02F6 (slot, idx, name) draft (the C copies font number idx of the file into the driver's slot); the driver itself (mgraphic.exe, asm) is not in the sources; status asm
    // C: "slot n holds font n-1" is how the screens look (card text = slot 1, party panel = slot 3), not read from the driver; the C names only the slot argument. Slots outside 1..3 are clamped here (the original has the slot count of the driver).
    public FntFont Font(int slot)
    {
        _fonts ??= new FntDecoder().Decode("FONTS.FNT", files.Read("FONTS.FNT"));
        return _fonts.Fonts[Math.Clamp(slot - 1, 0, _fonts.Fonts.Count - 1)];
    }

    /// <summary>A saint's description for the prayer screen (DARKLAND.SNT record), "" when there is none.</summary>
    // orig: saint_load_bg R034+0x0720 (1462:0720) matched: fopen("darkland.snt", "r"),
    //   fseek(saint * 360 + 1), fread 360 bytes
    // C: text mode: CR LF would read as LF and ^Z would end the read; the stock file has neither.
    // DIVERGENCE: without the file the original prints "saint file" and exits to DOS; here the description is empty.
    public string SaintDescription(int saint)
    {
        if (_saints is null && files.Exists("DARKLAND.SNT"))
        {
            _saints = SaintsDecoder.DecodeBytes(files.Read("DARKLAND.SNT"), "DARKLAND.SNT");
        }
        return _saints is not null && saint >= 0 && saint < _saints.Descriptions.Count ? _saints.Descriptions[saint].Text : "";
    }

    /// <summary>The text blocks of a card's .MSG file in MSGFILES ("$Party02").</summary>
    public MessageFile Messages(string cardName)
    {
        if (!_messages.TryGetValue(cardName, out var file))
        {
            _messageArchive ??= new MsgFilesArchiveDecoder().Decode(files.Read("MSGFILES"));
            // C: msg_load asks the catalogue for name + ".msg", and catalog_find compares names with strnicmp(.., 12)
            // (root 0617:0736): a card named "$MeetI00.msg" finds "$MEETI00.MSG" by its first twelve characters.
            var wanted = cardName + ".MSG";
            var key = wanted.Length > 12 ? wanted[..12] : wanted;
            var document = MsgCardDocumentParser.ParseEntryPayload(_messageArchive.ReadPayload(key));
            file = new MessageFile(cardName, document.Cards
                .Select(c => new MessageBlock(c.Layout.TextX, c.Layout.TextY, c.Layout.TextMaxX, c.RawTextBytes))
                .ToList());
            _messages[cardName] = file;
        }
        return file;
    }
}

/// <summary>A card's texts: the blocks of one .MSG file.</summary>
public sealed record MessageFile(string Name, IReadOnlyList<MessageBlock> Blocks);

/// <summary>
/// One text block of a card: where it is drawn (x, y and wrapping width, in card-window pixels)
/// and its text bytes, including the option markers and control codes.
/// </summary>
public sealed record MessageBlock(int X, int Y, int Width, byte[] Text);
