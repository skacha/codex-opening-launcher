using System;
using System.IO;
using System.Text;

// 为通用合成声线增加轻微机械调制、短延迟叠声与动态压缩，保留英文清晰度。
public static class VoiceProcessor
{
    public sealed class Metrics
    {
        public double InputRms { get; set; }
        public double OutputRms { get; set; }
        public double OutputPeak { get; set; }
        public double Seconds { get; set; }
        public bool Finite { get; set; }
    }
    public static Metrics Process(string input, string output)
    {
        int rate = 0, channels = 0, bits = 0; byte[] data = null;
        using (var r = new BinaryReader(File.OpenRead(input)))
        {
            if (Encoding.ASCII.GetString(r.ReadBytes(4)) != "RIFF") throw new InvalidDataException("不是 WAV 文件。");
            r.ReadInt32(); r.ReadBytes(4);
            while (r.BaseStream.Position + 8 <= r.BaseStream.Length)
            {
                string kind = Encoding.ASCII.GetString(r.ReadBytes(4)); int size = r.ReadInt32(); long next = r.BaseStream.Position + size + (size % 2);
                if (kind == "fmt ") { if (r.ReadInt16() != 1) throw new InvalidDataException("必须为 PCM。"); channels = r.ReadInt16(); rate = r.ReadInt32(); r.ReadInt32(); r.ReadInt16(); bits = r.ReadInt16(); }
                if (kind == "data") data = r.ReadBytes(size);
                r.BaseStream.Position = next;
            }
        }
        if (data == null || bits != 16 || rate < 8000 || channels != 1) throw new InvalidDataException("必须为单声道 16 位 PCM。");
        double[] source = new double[data.Length / 2]; double inputPower = 0;
        for (int i = 0; i < source.Length; i++) { source[i] = BitConverter.ToInt16(data, i * 2) / 32768.0; inputPower += source[i] * source[i]; }
        const double pitch = 0.94;
        int count = (int)(source.Length / pitch);
        double[] processed = new double[count]; double lastInput = 0, high = 0, low = 0, power = 0;
        double highAlpha = Math.Exp(-2 * Math.PI * 135 / rate);
        double lowAlpha = 1 - Math.Exp(-2 * Math.PI * 4600 / rate);
        for (int i = 0; i < count; i++)
        {
            double position = i * pitch; int index = (int)position; double part = position - index;
            double x = source[Math.Min(index, source.Length - 1)] * (1 - part) + source[Math.Min(index + 1, source.Length - 1)] * part;
            high = highAlpha * (high + x - lastInput); lastInput = x; low += lowAlpha * (high - low);
            double modulation = 0.86 + 0.14 * Math.Sin(2 * Math.PI * 38 * i / rate);
            double compressed = Math.Tanh(low * 3.2) * modulation;
            processed[i] = compressed; power += compressed * compressed;
        }
        double gain = Math.Min(4, 0.235 / Math.Max(0.0001, Math.Sqrt(power / count)));
        double outputPower = 0, peak = 0;
        using (var w = new BinaryWriter(File.Create(output)))
        {
            w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + count * 4); w.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); w.Write(16);
            w.Write((short)1); w.Write((short)2); w.Write(rate); w.Write(rate * 4); w.Write((short)4); w.Write((short)16);
            w.Write(Encoding.ASCII.GetBytes("data")); w.Write(count * 4);
            for (int i = 0; i < count; i++)
            {
                for (int ch = 0; ch < 2; ch++)
                {
                    int delayed = i - (int)(rate * (ch == 0 ? 0.011 : 0.017));
                    double value = (processed[i] * 0.88 + (delayed >= 0 ? processed[delayed] * 0.12 : 0)) * gain;
                    double fade = Math.Min(1, i / (rate * 0.012)) * Math.Min(1, (count - 1 - i) / (rate * 0.025));
                    value = Math.Max(-0.97, Math.Min(0.97, 1.10 * 0.92 * Math.Tanh(value / 0.92) * fade));
                    peak = Math.Max(peak, Math.Abs(value)); outputPower += value * value;
                    w.Write((short)Math.Round(value * 32767));
                }
            }
        }
        return new Metrics { InputRms = Math.Sqrt(inputPower / source.Length), OutputRms = Math.Sqrt(outputPower / (count * 2)), OutputPeak = peak, Seconds = count / (double)rate, Finite = !Double.IsNaN(outputPower) && peak < 1 };
    }
}
