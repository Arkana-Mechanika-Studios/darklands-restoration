using System.Globalization;
using Darklands.Audio;
using Darklands.Content;

// Renders the game's music and sounds to a .wav file, through the player's own drivers.
//   tune <game folder> <out.wav> [--card config|adlib|sb|sbpro2|mt32|cm32l] [--mt32-roms folder] <step> [<step> ...]
// config (the default without --card: a Sound Blaster) is CONFIG.DRK's choice as the game makes it; the Roland driver's MIDI is
// played by mt32emu with the ROMs of --mt32-roms (default DARKLANDS_MT32_ROMS, else MT32_ROMS next to the program).
// Steps: <n> plays music number n (towns-and-map driver), fx:<n> plays battle sound n,
//        wait:<seconds> renders that long (default 10 s after the last step).

if (args.Length < 3)
{
    Console.Error.WriteLine("usage: tune <game folder> <out.wav> [--card config|adlib|sb|sbpro2|mt32|cm32l] [--mt32-roms folder] <n | fx:n | wait:s> ...");
    return 1;
}
var steps = args.Skip(2).ToList();
string? Option(string name)
{
    var at = steps.IndexOf(name);
    if (at < 0 || at + 1 >= steps.Count)
    {
        return null;
    }
    var value = steps[at + 1];
    steps.RemoveRange(at, 2);
    return value;
}
var choice = Option("--card") ?? "sb";
var files = new GameFiles(args[0]);
var roms = Option("--mt32-roms");
const int Rate = 44100;
using var sound = Darklands.Audio.Mt32.SoundSelection.Select(files, choice == "config" ? null : choice, roms, Rate,
    line => (Darklands.Audio.Mt32.SoundSelection.IsWarning(line) ? Console.Error : Console.Out).WriteLine(line))
    ?? throw new InvalidOperationException("no sound for this choice");
var audio = new SynthAudio(files, Rate, sound.Setup, sound.Midi);
var output = new List<short>();
void Render(double seconds)
{
    var buffer = new short[(int)(seconds * Rate) * 2];
    audio.Render(buffer);
    output.AddRange(buffer);
}
if (!steps[^1].StartsWith("wait:", StringComparison.Ordinal))
{
    steps.Add("wait:10");
}
foreach (var step in steps)
{
    if (step.StartsWith("wait:", StringComparison.Ordinal))
    {
        Render(double.Parse(step[5..], CultureInfo.InvariantCulture));
    }
    else if (step.StartsWith("fx:", StringComparison.Ordinal))
    {
        audio.PlaySound(int.Parse(step[3..], CultureInfo.InvariantCulture));
    }
    else
    {
        audio.PlayMusic(int.Parse(step, CultureInfo.InvariantCulture));
    }
}
WavFile.Write(args[1], output.ToArray(), Rate);
var peak = output.Count == 0 ? 0 : output.Max(s => Math.Abs((int)s));
Console.WriteLine($"{output.Count / 2 / (double)Rate:0.0} s, peak {peak}{(audio.Fault is null ? "" : ", driver fault: " + audio.Fault)}");
return audio.Fault is null ? 0 : 2;
