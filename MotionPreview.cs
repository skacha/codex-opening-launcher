using System;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CodexOpening
{
    // 离屏导出真实绘制器的动效；不会显示窗口或启动 Codex。
    public static class MotionPreview
    {
        [STAThread]
        public static int Main(string[] args)
        {
            try
            {
                // 离屏位图使用软件渲染，避免与桌面 GPU 合成同步等待。
                RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
                Program.Root = Path.GetFullPath(args[0]);
                Program.Preview = true;
                Program.ConfigPath = Path.Combine(Program.Root,"settings.json");
                Preferences p = Program.Load();
                var timeline = new BootTimeline(p.EvaSeconds,p.VoiceEnabled && p.VoiceVolume>0 && p.EvaSeconds>=15);
                p.Mode = "eva";
                var window = new SplashWindow(p);
                var content = (FrameworkElement)window.Content;
                content.Measure(new Size(960,540)); content.Arrange(new Rect(0,0,960,540));
                double startSeconds=args.Length>3?Double.Parse(args[3],CultureInfo.InvariantCulture):0;
                double duration=args.Length>4?Double.Parse(args[4],CultureInfo.InvariantCulture):6;
                int rate=args.Length>5?Int32.Parse(args[5],CultureInfo.InvariantCulture):30;
                bool audio=args.Length>6&&args[6]=="audio";
                string frames=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[2])),Path.GetFileNameWithoutExtension(args[2])+"-frames");
                Directory.CreateDirectory(frames);
                {
                    bool confirmed=false;
                    int count=(int)Math.Ceiling(duration*rate);
                    for(int frame=0;frame<count;frame++)
                    {
                        double seconds=startSeconds+frame/(double)rate;
                        window.SetEvaProgress(timeline.VisualProgress(seconds));
                        window.SetEvaPreviewTime(seconds);
                        if(!confirmed && seconds>=timeline.Duration) { window.ShowConfirmationPreview(); confirmed=true; }
                        content.UpdateLayout();
                        var bitmap=new RenderTargetBitmap(960,540,96,96,PixelFormats.Pbgra32); bitmap.Render(content);
                        var png=new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                        using(var file=File.Create(Path.Combine(frames,"frame-"+frame.ToString("0000")+".png"))) png.Save(file);
                    }
                    string command="-hide_banner -loglevel error -y -framerate "+rate+" -i \""+Path.Combine(frames,"frame-%04d.png")+"\"";
                    command+=audio?AudioArguments(p,timeline,startSeconds,duration):" -an";
                    command+=" -frames:v "+count+" -t "+N(duration)+" -c:v libx264 -preset veryfast -crf 19 -pix_fmt yuv420p -movflags +faststart \""+Path.GetFullPath(args[2])+"\"";
                    var start = new ProcessStartInfo(args[1],command);
                    start.UseShellExecute=false; start.CreateNoWindow=true;
                    using(var encoder=Process.Start(start)) { encoder.WaitForExit(); if(encoder.ExitCode!=0)throw new InvalidOperationException("视频编码失败。"); }
                }
                Console.WriteLine(audio?"连续动效预览导出完成，含英文语音和背景音乐。":"连续动效预览导出完成，无音轨。");return 0;
            }
            catch(Exception e) { Console.Error.WriteLine("导出失败："+e.GetType().Name);return 1; }
        }
        private static string N(double value){return value.ToString("0.######",CultureInfo.InvariantCulture);}
        private static string AudioArguments(Preferences p,BootTimeline timeline,double start,double duration)
        {
            // 离线混音使用同一时间轴；播报期间配乐降至 10%，结束后保留等待确认的底乐。
            string command=" -stream_loop -1 -i \""+Path.Combine(Program.Root,"assets","eva-terminal.wav")+"\"";
            string duck="0",filters="",inputs="[bgm]";
            var root=new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<System.Collections.Generic.Dictionary<string,object>>(File.ReadAllText(Path.Combine(Program.Root,"assets","voice","台词.json")));
            var metrics=(System.Collections.IList)root["Metrics"];
            bool spoken=p.VoiceEnabled&&p.VoiceVolume>0&&p.EvaSeconds>=15;
            if(spoken)for(int i=0;i<5;i++)
            {
                command+=" -i \""+Path.Combine(Program.Root,"assets","voice","cue-"+i+".wav")+"\"";
                double length=Convert.ToDouble(((System.Collections.Generic.Dictionary<string,object>)metrics[i])["Seconds"]);
                duck+="+between(t,"+N(timeline.Starts[i])+","+N(timeline.Starts[i]+length)+")";
                filters+="["+(i+2)+":a]volume="+N(p.VoiceVolume)+",adelay="+(int)Math.Round(timeline.Starts[i]*1000)+":all=1[v"+i+"];";inputs+="[v"+i+"]";
            }
            double music=p.BgmEnabled?p.BgmVolume:0;
            filters+="[1:a]volume='"+N(music)+"*if(gte(t,"+N(timeline.Duration)+"),0.18,if(gt("+duck+",0),0.1,1)*min(1,t/0.65)*max(0,min(1,("+N(timeline.Duration)+"-t)/0.8)))':eval=frame[bgm];";
            filters+=inputs+"amix=inputs="+(spoken?6:1)+":duration=longest:normalize=0,atrim=start="+N(start)+":duration="+N(duration)+",asetpts=PTS-STARTPTS[audio]";
            string path=Path.Combine(Program.Root,"artifacts","preview-audio-filter.txt");File.WriteAllText(path,filters);
            return command+" -filter_complex_script \""+path+"\" -map 0:v -map [audio] -c:a aac -b:a 192k";
        }
    }
}
