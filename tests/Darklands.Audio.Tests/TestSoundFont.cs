using System.Text;

namespace Darklands.Audio.Tests;

/// <summary>
/// A minimal SoundFont 2 file and a tiny MIDI file written by the tests (no SoundFont is downloaded or shipped): one preset
/// (bank 0, program 0) of one looped sine wave sample tuned to A4 (440 Hz), which every General MIDI program then plays.
/// </summary>
internal static class TestSoundFont
{
    private const int SampleRate = 22050;
    private const int Period = 50;          // 22050 / 50 = 441 Hz, a hair above A4 (middle A is key 69)
    private const int Samples = 40 * Period;

    public static byte[] Sf2()
    {
        var info = Chunks(Chunk("ifil", Words(2, 1)), Chunk("INAM", Text("test sine")));
        var smpl = new byte[(Samples + 46) * 2];
        for (var i = 0; i < Samples; i++)
        {
            var value = (short)Math.Round(20000 * Math.Sin(2 * Math.PI * i / Period));
            smpl[2 * i] = (byte)value;
            smpl[2 * i + 1] = (byte)(value >> 8);
        }
        var sdta = Chunks(Chunk("smpl", smpl));

        var phdr = Concat(Preset("sine", 0, 0, 0), Preset("EOP", 0, 0, 1));
        var pbag = Concat(Words(0, 0), Words(1, 0));
        var pmod = new byte[10];
        var pgen = Concat(Words(41, 0), Words(0, 0));                       // instrument 0
        var inst = Concat(Name("sine", 0), Name("EOI", 1));
        var ibag = Concat(Words(0, 0), Words(2, 0));
        var imod = new byte[10];
        var igen = Concat(Words(54, 1), Words(53, 0), Words(0, 0));         // loop continuously; sample 0
        var shdr = Concat(Sample("sine", 0, Samples, Period, Samples - Period, SampleRate, 69, 1), Sample("EOS", 0, 0, 0, 0, 0, 0, 0));
        var pdta = Chunks(Chunk("phdr", phdr), Chunk("pbag", pbag), Chunk("pmod", pmod), Chunk("pgen", pgen), Chunk("inst", inst),
            Chunk("ibag", ibag), Chunk("imod", imod), Chunk("igen", igen), Chunk("shdr", shdr));

        return Riff("sfbk", List("INFO", info), List("sdta", sdta), List("pdta", pdta));
    }

    /// <summary>A format 0 MIDI file: program 0, one note (key 69 unless given) of <paramref name="beats"/> beats at 120 beats a minute.</summary>
    public static byte[] Midi(int key = 69, int beats = 1)
    {
        var track = new List<byte> { 0x00, 0xC0, 0x00, 0x00, 0x90, (byte)key, 100 };
        track.AddRange(VariableLength(480 * beats));
        track.AddRange([0x80, (byte)key, 0x40, 0x00, 0xFF, 0x2F, 0x00]);
        var file = new List<byte>();
        file.AddRange("MThd"u8.ToArray());
        file.AddRange(BigEndian(6, 4));
        file.AddRange(BigEndian(0, 2));
        file.AddRange(BigEndian(1, 2));
        file.AddRange(BigEndian(480, 2));
        file.AddRange("MTrk"u8.ToArray());
        file.AddRange(BigEndian(track.Count, 4));
        file.AddRange(track);
        return [.. file];
    }

    private static byte[] BigEndian(int value, int bytes) => [.. Enumerable.Range(0, bytes).Select(i => (byte)(value >> 8 * (bytes - 1 - i)))];

    private static byte[] VariableLength(int value)
    {
        var bytes = new List<byte> { (byte)(value & 0x7F) };
        for (value >>= 7; value > 0; value >>= 7)
        {
            bytes.Insert(0, (byte)(value & 0x7F | 0x80));
        }
        return [.. bytes];
    }

    private static byte[] Words(params int[] values) => [.. values.SelectMany(v => new[] { (byte)v, (byte)(v >> 8) })];

    private static byte[] Text(string text)
    {
        var bytes = Encoding.ASCII.GetBytes(text + "\0");
        return bytes.Length % 2 == 0 ? bytes : [.. bytes, 0];
    }

    private static byte[] Name(string name, int bag) => Concat(Padded(name), Words(bag));

    private static byte[] Padded(string name)
    {
        var bytes = new byte[20];
        Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        return bytes;
    }

    private static byte[] Preset(string name, int preset, int bank, int bag) =>
        Concat(Padded(name), Words(preset, bank, bag), new byte[12]);

    private static byte[] Sample(string name, int start, int end, int loopStart, int loopEnd, int rate, int pitch, int type) =>
        Concat(Padded(name), Dwords(start, end, loopStart, loopEnd, rate), [(byte)pitch, 0], Words(0, type));

    private static byte[] Dwords(params int[] values) => [.. values.SelectMany(v => BitConverter.GetBytes(v))];

    private static byte[] Concat(params byte[][] parts) => [.. parts.SelectMany(p => p)];

    private static byte[] Chunk(string id, byte[] body) =>
        Concat(Encoding.ASCII.GetBytes(id), BitConverter.GetBytes(body.Length), body, body.Length % 2 == 0 ? [] : [(byte)0]);

    private static byte[] Chunks(params byte[][] chunks) => Concat(chunks);

    private static byte[] List(string type, byte[] body) => Chunk("LIST", Concat(Encoding.ASCII.GetBytes(type), body));

    private static byte[] Riff(string type, params byte[][] chunks) => Chunk("RIFF", Concat(Encoding.ASCII.GetBytes(type), Concat(chunks)));
}
