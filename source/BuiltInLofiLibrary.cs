using System.Text;
using System.IO;

namespace Yuhui;

public sealed record BuiltInLofiPreset(string Id, string Name, int Tempo, int RootMidi, bool Minor, double Space, double Brightness);

public static class BuiltInLofiLibrary
{
    public static readonly IReadOnlyList<BuiltInLofiPreset> Presets = new[]
    {
        new BuiltInLofiPreset("chillhop", "Chillhop", 82, 50, false, .20, .65),
        new("late-night", "Late night vibes", 68, 45, true, .42, .34),
        new("hip-hop", "Hip hop radio", 88, 43, true, .17, .72),
        new("relaxing", "Relaxing beats", 64, 48, false, .48, .28),
        new("coffee-shop", "Coffee shop radio", 76, 52, false, .26, .52),
        new("videogame", "Videogame lofi & chill", 90, 57, false, .15, .88),
        new("space", "Space ambient music", 54, 41, true, .76, .22),
        new("sleepy", "Sad & sleepy", 58, 46, true, .56, .19),
        new("synthwave", "Lofi synthwave", 92, 45, true, .36, .94)
    };

    public static string EnsureTrack(BuiltInLofiPreset preset)
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "generated-audio");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, $"{preset.Id}.wav");
        if (File.Exists(file)) return file;

        const int sampleRate = 22050;
        const int durationSeconds = 28;
        var samples = sampleRate * durationSeconds;
        using var stream = File.Create(file);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: false);
        WriteWaveHeader(writer, sampleRate, samples * 2);

        var random = new Random(preset.Id.GetHashCode(StringComparison.Ordinal));
        var progression = preset.Minor ? new[] { 0, 8, 3, 10 } : new[] { 0, 7, 9, 5 };
        for (var i = 0; i < samples; i++)
        {
            var seconds = i / (double)sampleRate;
            var beat = seconds * preset.Tempo / 60d;
            var barBeat = beat % 4;
            var bar = (int)(beat / 4) % progression.Length;
            var root = MidiToHz(preset.RootMidi + progression[bar]);
            var third = root * (preset.Minor ? 1.189207 : 1.259921);
            var fifth = root * 1.498307;
            var pad = .105 * (Math.Sin(Tau * root * seconds) + .72 * Math.Sin(Tau * third * seconds) + .56 * Math.Sin(Tau * fifth * seconds));
            pad *= .72 + .28 * Math.Sin(Tau * .09 * seconds);

            var bass = .09 * Math.Sin(Tau * root / 2 * seconds) * (barBeat < .7 ? 1 : .25);
            var kickPhase = barBeat < .16 ? barBeat / .16 : 1;
            var kick = barBeat < .16 ? .19 * Math.Exp(-kickPhase * 7) * Math.Sin(Tau * (90 - kickPhase * 46) * seconds) : 0;
            var beatInBar = (int)Math.Floor(barBeat);
            var snarePhase = barBeat - beatInBar;
            var snare = (beatInBar == 1 || beatInBar == 3) && snarePhase < .12
                ? .045 * Math.Exp(-snarePhase * 28) * (random.NextDouble() * 2 - 1) : 0;
            var hat = (barBeat % .5) < .045 ? .018 * (random.NextDouble() * 2 - 1) : 0;

            var noteSlot = (int)(beat * 2) % 8;
            var melodyRatio = new[] { 2d, 2.25, 2.5, 3d, 2.5, 2.25, 1.875, 2d }[noteSlot];
            var melodyGate = (beat * 2) % 1;
            var melody = melodyGate < .43 ? .035 * preset.Brightness * Math.Sin(Tau * root * melodyRatio * seconds) * (1 - melodyGate / .48) : 0;
            var room = preset.Space * .018 * (random.NextDouble() * 2 - 1) + preset.Space * .022 * Math.Sin(Tau * root / 8 * seconds);
            var sample = Math.Clamp(pad + bass + kick + snare + hat + melody + room, -.82, .82);
            writer.Write((short)(sample * short.MaxValue));
        }
        return file;
    }

    private static void WriteWaveHeader(BinaryWriter writer, int sampleRate, int dataLength)
    {
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataLength);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataLength);
    }

    private static double MidiToHz(int midi) => 440 * Math.Pow(2, (midi - 69) / 12d);
    private const double Tau = Math.PI * 2;
}
