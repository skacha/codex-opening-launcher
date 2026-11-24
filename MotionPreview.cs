using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;

namespace CodexOpening
{
    // 直接录制同一个原版网页运行时；不用另一个绘制器拼接替代画面。
    public static class MotionPreview
    {
        private static string N(double value) { return value.ToString("0.######", CultureInfo.InvariantCulture); }
        [STAThread]
        public static int Main(string[] args)
        {
            Program.Root = Path.GetFullPath(args[0]); Program.Preview = true;
            Program.ConfigPath = Path.Combine(Program.Root, "settings.json");
            var p = Program.Load(); p.Mode = "eva";
            string output = Path.GetFullPath(args[2]);
            double start = args.Length > 3 ? Double.Parse(args[3], CultureInfo.InvariantCulture) : 0;
            double duration = args.Length > 4 ? Double.Parse(args[4], CultureInfo.InvariantCulture) : 6;
            int rate = args.Length > 5 ? Int32.Parse(args[5], CultureInfo.InvariantCulture) : 30;
            bool audio = args.Length > 6 && args[6] == "audio";
            string folder = Path.Combine(Path.GetDirectoryName(output), Path.GetFileNameWithoutExtension(output) + "-recording");
            Directory.CreateDirectory(folder);
            int result = 0; var frames = new List<KeyValuePair<string, double>>();
            var app = new Application(); var window = new ReferenceBootWindow(p, true, true);
            window.FrameCaptured += delegate(byte[] bytes, double stamp)
            {
                string path = Path.Combine(folder, "frame-" + frames.Count.ToString("00000") + ".jpg");
                File.WriteAllBytes(path, bytes); frames.Add(new KeyValuePair<string, double>(path, stamp));
            };
            window.Loaded += async delegate
            {
                try
                {
                    if (await Task.WhenAny(window.RecordingCompleted.Task, Task.Delay(65000)) != window.RecordingCompleted.Task) throw new TimeoutException("原版运行时录制超时。");
                    await window.RecordingCompleted.Task;
                    await window.StopRecording();
                    if (frames.Count < 30) throw new InvalidOperationException("录制帧不足。");
                    var concat = new StringBuilder("ffconcat version 1.0\n");
                    for (int i = 0; i < frames.Count; i++)
                    {
                        concat.Append("file '").Append(frames[i].Key.Replace("\\", "/").Replace("'", "'\\''")).Append("'\n");
                        double hold = i + 1 < frames.Count ? Math.Max(0.001, frames[i+1].Value - frames[i].Value) : 0.04;
                        concat.Append("duration ").Append(N(hold)).Append('\n');
                    }
                    string listPath = Path.Combine(folder, "frames.ffconcat");File.WriteAllText(listPath, concat.ToString(), new UTF8Encoding(false));
                    string command = "-hide_banner -loglevel error -y -f concat -safe 0 -i \"" + listPath + "\"";
                    command += audio ? AudioArguments(p, window.RecordedVoiceStarts, folder) : " -an";
                    command += " -ss " + N(start) + " -t " + N(duration) + " -vf scale=trunc(iw/2)*2:trunc(ih/2)*2,tpad=stop_mode=clone:stop_duration=3 -r " + rate + " -c:v libx264 -preset veryfast -crf 18 -pix_fmt yuv420p -movflags +faststart \"" + output + "\"";
                    var process = new ProcessStartInfo(args[1], command) { UseShellExecute = false, CreateNoWindow = true };
                    using (var encoder = Process.Start(process)) { await Task.Run(delegate { encoder.WaitForExit(); }); if (encoder.ExitCode != 0) throw new InvalidOperationException("视频编码失败。"); }
                    File.WriteAllText(Path.Combine(folder,"recording.json"),new JavaScriptSerializer().Serialize(new { frames = frames.Count, seconds = frames[frames.Count-1].Value, voiceStarts = window.RecordedVoiceStarts, renderer = "upstream HTML/CSS / WebView2" }));
                    Console.WriteLine("原版运行时录制完成：" + frames.Count + " 帧；" + (audio ? "包含六段英文语音和配乐。" : "无音轨。"));
                }
                catch (Exception e) { Console.Error.WriteLine("录制失败：" + e.GetType().Name + " / " + e.Message); result = 1; }
                finally { window.Close(); }
            };
            app.Run(window); return result;
        }
        private static string AudioArguments(Preferences p, List<double> starts, string folder)
        {
            var json = new JavaScriptSerializer();
            IList metrics = (IList)json.Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(Program.Root,"assets","reference-voice","台词.json")))["Metrics"];
            bool spoken = p.VoiceEnabled && p.VoiceVolume > 0 && p.EvaSeconds >= 15;
            if (spoken && starts.Count != 6) throw new InvalidOperationException("六段语音起点记录不完整。");
            string command = " -stream_loop -1 -i \"" + Path.Combine(Program.Root,"assets","eva-terminal.wav") + "\"";
            string filters = "", inputs = "[bgm]", duck = "0";
            if (spoken) for(int i=0;i<6;i++)
            {
                command += " -i \"" + Path.Combine(Program.Root,"assets","reference-voice","cue-"+i+".wav") + "\"";
                double length = Convert.ToDouble(((Dictionary<string, object>)metrics[i])["Seconds"]);
                duck += "+between(t," + N(starts[i]) + "," + N(starts[i]+length) + ")";
                filters += "["+(i+2)+":a]volume="+N(p.VoiceVolume)+",adelay="+(int)Math.Round(starts[i]*1000)+":all=1[v"+i+"];";inputs += "[v"+i+"]";
            }
            filters += "[1:a]volume='"+N(p.BgmEnabled?p.BgmVolume:0)+"*if(gt("+duck+",0),0.1,1)*min(1,t/0.65)':eval=frame[bgm];";
            filters += inputs+"amix=inputs="+(spoken?7:1)+":duration=longest:normalize=0[audio]";
            string path = Path.Combine(folder,"audio-filter.txt");File.WriteAllText(path,filters);
            return command + " -filter_complex_script \"" + path + "\" -map 0:v -map [audio] -c:a aac -b:a 192k";
        }
    }
}
