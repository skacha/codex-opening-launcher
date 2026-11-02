using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace CodexOpening
{
    // 阶段以语音实际长度分配，避免同步画面过长或截断播报。
    public sealed class BootTimeline
    {
        public readonly double[] Starts = new double[5];
        public readonly double Duration;
        public BootTimeline(double requested, bool spoken)
        {
            double[] lengths = { 4, 4.3, 4.6, 4.9, 4 };
            if (spoken) try
            {
                var root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(Program.Root, "assets", "voice", "台词.json")));
                var metrics = (object[])root["Metrics"];
                var measured = new double[5];
                for (int i = 0; i < 5; i++)
                {
                    measured[i] = Convert.ToDouble(((Dictionary<string, object>)metrics[i])["Seconds"]);
                    if (Double.IsNaN(measured[i]) || Double.IsInfinity(measured[i]) || measured[i] < 0.1 || measured[i] > 30) throw new InvalidDataException();
                }
                lengths = measured;
            } catch { }
            double sum = 0; foreach (double length in lengths) sum += length;
            Duration = spoken ? Math.Max(requested, sum + 1.25) : requested;
            double gap = (Duration - sum) / 5;
            for (int i = 1; i < 5; i++) Starts[i] = Starts[i-1] + (spoken ? lengths[i-1] + gap : Duration / 5);
        }
        public double VisualProgress(double seconds)
        {
            int phase = 0; while (phase < 4 && seconds >= Starts[phase+1]) phase++;
            double end = phase == 4 ? Duration : Starts[phase+1];
            return Math.Min(1, (phase + Math.Max(0, Math.Min(1, (seconds-Starts[phase]) / (end-Starts[phase])))) / 5);
        }
    }
}
