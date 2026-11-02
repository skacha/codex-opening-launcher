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
                var content = new EvaBootScene();
                content.Measure(new Size(960,540)); content.Arrange(new Rect(0,0,960,540));
                double startSeconds=args.Length>3?Double.Parse(args[3],CultureInfo.InvariantCulture):0;
                string frames=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[2])),Path.GetFileNameWithoutExtension(args[2])+"-frames");
                Directory.CreateDirectory(frames);
                {
                    bool confirmed=false;
                    int count=180;
                    for(int frame=0;frame<count;frame++)
                    {
                        double seconds=startSeconds+frame/30.0;
                        content.SetProgress(timeline.VisualProgress(seconds));
                        content.SetPreviewTime(seconds);
                        if(!confirmed && seconds>=timeline.Duration) { content.SetConfirmationReady(); confirmed=true; }
                        content.UpdateLayout();
                        var bitmap=new RenderTargetBitmap(960,540,96,96,PixelFormats.Pbgra32); bitmap.Render(content);
                        var png=new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                        using(var file=File.Create(Path.Combine(frames,"frame-"+frame.ToString("0000")+".png"))) png.Save(file);
                    }
                    var start = new ProcessStartInfo(args[1],"-hide_banner -loglevel error -y -framerate 30 -i \""+Path.Combine(frames,"frame-%04d.png")+"\" -frames:v "+count+" -an -c:v libx264 -preset veryfast -crf 19 -pix_fmt yuv420p -movflags +faststart \""+Path.GetFullPath(args[2])+"\"");
                    start.UseShellExecute=false; start.CreateNoWindow=true;
                    using(var encoder=Process.Start(start)) { encoder.WaitForExit(); if(encoder.ExitCode!=0)throw new InvalidOperationException("视频编码失败。"); }
                }
                Console.WriteLine("连续动效预览导出完成，无音轨。");return 0;
            }
            catch(Exception e) { Console.Error.WriteLine("导出失败："+e.GetType().Name);return 1; }
        }
    }
}
