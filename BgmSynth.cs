using System;
using System.IO;
using System.Text;

// 独立合成电子氛围配乐，不包含外部录音或原作旋律。
public static class BgmSynth
{
    private const int Rate = 44100;
    private const double Length = 16;
    private static double Tone(double hz, double t) { return Math.Sin(2 * Math.PI * hz * t); }
    private static double Note(int midi) { return 440 * Math.Pow(2, (midi - 69) / 12.0); }
    public static void Main(string[] args)
    {
        int count = (int)(Rate * Length);
        var dry = new double[count];
        int[] sequence = { 62, 69, 72, 76, 65, 69, 72, 74, 58, 65, 69, 72, 60, 67, 70, 74 };
        for (int i = 0; i < count; i++)
        {
            double t = i / (double)Rate;
            int segment = (int)(t / 4);
            int root = new[] { 38, 34, 41, 36 }[segment];
            double pad = 0;
            foreach (int interval in new[] { 12, 19, 22, 26 })
                pad += 0.024 * Tone(Note(root + interval), t) * (0.8 + 0.2 * Tone(0.09 + interval * 0.001, t));
            double beat = t % (2.0 / 3);
            double bass = 0.10 * (Tone(Note(root), t) + 0.18 * Tone(2 * Note(root), t)) * Math.Exp(-beat * 3.5);
            double step = t % (1.0 / 3);
            double hz = Note(sequence[(int)(t * 3) % sequence.Length]);
            double envelope = (1 - Math.Exp(-step * 130)) * Math.Exp(-step * 8);
            double bell = 0.07 * (Tone(hz, t) + 0.22 * Tone(hz * 2, t)) * envelope;
            double kick = 0.035 * Math.Sin(2 * Math.PI * (45 * beat + 5 * (1 - Math.Exp(-beat * 30)))) * Math.Exp(-beat * 22);
            double pulse = 0.012 * Tone(1200, t) * Math.Exp(-(t % (1.0 / 6)) * 95);
            double edge = Math.Min(1, t / 0.45) * Math.Min(1, (Length - t) / 0.85);
            dry[i] = (pad + bass + bell + kick + pulse) * edge;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0])));
        using (var writer = new BinaryWriter(File.Create(args[0])))
        {
            writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + count * 4);
            writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
            writer.Write((short)1); writer.Write((short)2); writer.Write(Rate); writer.Write(Rate * 4);
            writer.Write((short)4); writer.Write((short)16); writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(count * 4);
            for (int i = 0; i < count; i++)
            {
                double left = dry[i] + Echo(dry, i, 0.22) * 0.26 + Echo(dry, i, 0.49) * 0.12;
                double right = dry[i] + Echo(dry, i, 0.29) * 0.26 + Echo(dry, i, 0.55) * 0.12;
                double endFade = Math.Min(1, (count - 1 - i) / (Rate * 0.3));
                writer.Write((short)(Math.Max(-0.95, Math.Min(0.95, left * endFade)) * 32767));
                writer.Write((short)(Math.Max(-0.95, Math.Min(0.95, right * endFade)) * 32767));
            }
        }
    }
    private static double Echo(double[] samples, int index, double delay)
    { int at = index - (int)(delay * Rate); return at >= 0 ? samples[at] : 0; }
}
