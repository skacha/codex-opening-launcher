using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace CodexOpening
{
    public sealed class Preferences
    {
        public bool Enabled { get; set; }
        public string Mode { get; set; }
        public string MediaPath { get; set; }
        public double ImageSeconds { get; set; }
        public double EvaSeconds { get; set; }
        public bool BgmEnabled { get; set; }
        public double BgmVolume { get; set; }
        public bool VoiceEnabled { get; set; }
        public double VoiceVolume { get; set; }
        public bool ConfirmRequired { get; set; }
        public double FadeSeconds { get; set; }
        public double Volume { get; set; }
        public string Fit { get; set; }
        public string AppId { get; set; }
        public Preferences()
        {
            Enabled = true; Mode = "builtin"; MediaPath = "";
            ImageSeconds = 4; EvaSeconds = 24; FadeSeconds = 0.5; Volume = 0;
            BgmEnabled = true; BgmVolume = 0.385;
            VoiceEnabled = true; VoiceVolume = 1.0;
            ConfirmRequired = true;
            Fit = "cover"; AppId = "OpenAI.Codex_2p2nqsd0c76g0!App";
        }
        public void Normalize()
        {
            if (Mode != "image" && Mode != "video" && Mode != "eva") Mode = "builtin";
            if (Fit != "contain") Fit = "cover";
            ImageSeconds = Clamp(ImageSeconds, 1, 120, 4);
            EvaSeconds = Clamp(EvaSeconds, 1, 120, 24);
            BgmVolume = Clamp(BgmVolume, 0, 1, 0.385);
            VoiceVolume = Clamp(VoiceVolume, 0, 1, 1.0);
            FadeSeconds = Clamp(FadeSeconds, 0, 2, 0.5);
            Volume = Clamp(Volume, 0, 1, 0);
            if (String.IsNullOrWhiteSpace(AppId)) AppId = "OpenAI.Codex_2p2nqsd0c76g0!App";
            // 仅允许商店应用标识，不接受命令行或脚本。
            if (!System.Text.RegularExpressions.Regex.IsMatch(AppId, @"^[A-Za-z0-9._-]+![A-Za-z0-9._-]+$"))
                throw new InvalidDataException("应用标识格式不正确。");
            MediaPath = MediaPath ?? "";
        }
        private static double Clamp(double n, double min, double max, double fallback)
        { return Double.IsNaN(n) || Double.IsInfinity(n) ? fallback : Math.Max(min, Math.Min(max, n)); }
    }

    public static class Program
    {
        public static string Root = AppDomain.CurrentDomain.BaseDirectory;
        public static string ConfigPath = Path.Combine(Root, "settings.json");
        public static string LogPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexOpeningLauncher", "launcher.log");
        public static string ReportPath;
        public static bool Preview;
        public static bool Windowed;
        public static double RenderProgress = 0.6;
        public static double RenderTime = 8;
        public static object VisualStatistics;
        public static int RequestedScreen;
        public static string DisplayDevice;
        public static string SplashDevice;
        public static bool SameMonitor;
        public static int VoiceCues;
        public static int VoiceCompleted;
        public static int RepairedOverlays;
        public static bool UserConfirmed;
        public static bool AutoConfirm;
        public static bool AwaitedConfirmation;
        public static double SequenceSeconds;
        public static long LaunchAtMs;
        public static long IntroFinishedAtMs;
        public static bool LaunchRequested;
        public static bool LaunchSucceeded;
        public static string Outcome = "未开始";
        public static bool BgmOpened;
        public static bool BgmProgressed;
        public static string BgmStatus = "未启用";
        public static Stopwatch Elapsed = Stopwatch.StartNew();
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

        [STAThread]
        public static int Main(string[] args)
        {
            Preview = args.Contains("--preview");
            Windowed = args.Contains("--windowed");
            AutoConfirm = args.Contains("--auto-confirm");
            int.TryParse(Argument(args, "--screen", "0"), out RequestedScreen);
            double.TryParse(Argument(args, "--progress", "0.6"), NumberStyles.Float, CultureInfo.InvariantCulture, out RenderProgress);
            double.TryParse(Argument(args, "--time", "8"), NumberStyles.Float, CultureInfo.InvariantCulture, out RenderTime);
            ConfigPath = Argument(args, "--config", ConfigPath);
            ReportPath = Argument(args, "--report", null);
            Preferences p = Load();
            if (args.Contains("--repair-windows") || args.Contains("--audit-windows"))
            {
                int repaired = args.Contains("--repair-windows") ? DisplayHandoff.RepairLegacyOverlays() : 0;
                File.WriteAllText(ReportPath ?? Path.Combine(Root, "artifacts", "window-audit.json"), Json.Serialize(new { repairCount = repaired, windows = DisplayHandoff.Audit() }), Encoding.UTF8);
                return 0;
            }
            if (args.Contains("--self-test")) return SelfTest(Argument(args, "--output", Root));
            if (args.Contains("--render")) return Render(p, Argument(args, "--output", Path.Combine(Root, "preview.png")));
            var app = new Application();
            app.ShutdownMode = ShutdownMode.OnMainWindowClose;
            app.DispatcherUnhandledException += delegate(object sender, DispatcherUnhandledExceptionEventArgs e)
            {
                Log("界面异常", e.Exception.GetType().Name);
                if (!Preview && !args.Contains("--settings") && (p.Mode != "eva" || !p.ConfirmRequired || UserConfirmed)) Launch(p);
                Outcome = "界面异常"; e.Handled = true; app.Shutdown(1);
            };
            try
            {
                if (args.Contains("--settings"))
                { app.Run(new SettingsWindow(p)); return 0; }
                bool owns;
                using (var mutex = new Mutex(true, @"Local\CodexOpeningLauncher-" + Environment.UserName, out owns))
                {
                    // 连续点击不叠加多个全屏遮罩。
                    if (!owns && !Preview) return 0;
                    try
                    {
                        if ((!p.Enabled || args.Contains("--no-animation")) && !Preview)
                        { Launch(p); Outcome = "动画关闭"; }
                        else app.Run(new SplashWindow(p));
                    }
                    finally { if (owns) mutex.ReleaseMutex(); }
                }
                return LaunchRequested && !LaunchSucceeded ? 1 : 0;
            }
            catch (Exception e)
            {
                Log("启动异常", e.GetType().Name);
                if (!Preview && !args.Contains("--settings") && (p.Mode != "eva" || !p.ConfirmRequired || UserConfirmed)) Launch(p);
                Outcome = "启动异常"; return 1;
            }
            finally { WriteReport(); }
        }

        public static string Argument(string[] args, string name, string fallback)
        { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback; }

        public static Preferences Load()
        {
            try
            {
                var p = File.Exists(ConfigPath) ? Json.Deserialize<Preferences>(File.ReadAllText(ConfigPath, Encoding.UTF8)) : new Preferences();
                if (p == null) throw new InvalidDataException();
                p.Normalize(); return p;
            }
            catch (Exception e) { Log("配置无效，使用默认值", e.GetType().Name); return new Preferences(); }
        }
        public static void Save(Preferences p)
        {
            p.Normalize();
            // 先写临时文件，再原子替换，保留上一版配置用于恢复。
            string temp = ConfigPath + ".tmp";
            File.WriteAllText(temp, Json.Serialize(p), new UTF8Encoding(false));
            if (File.Exists(ConfigPath)) File.Replace(temp, ConfigPath, ConfigPath + ".bak");
            else File.Move(temp, ConfigPath);
        }
        public static void Log(string action, string detail)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 262144) File.Delete(LogPath);
                File.AppendAllText(LogPath, DateTime.Now.ToString("s") + " " + action + " " + detail + Environment.NewLine, Encoding.UTF8);
            }
            catch { /* 日志失败不阻断应用启动。 */ }
        }
        public static void Launch(Preferences p)
        {
            if (Preview || LaunchRequested) return;
            LaunchRequested = true;
            LaunchAtMs = Elapsed.ElapsedMilliseconds;
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "shell:AppsFolder\\" + p.AppId) { UseShellExecute = true });
                LaunchSucceeded = true; Log("启动请求已提交", "商店应用");
            }
            catch (Exception e)
            {
                Log("启动失败", e.GetType().Name);
                MessageBox.Show("Unable to open Codex. Use the original shortcut.\nLog: " + LogPath, "Codex Startup", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        public static void WriteReport()
        {
            if (ReportPath == null) return;
            File.WriteAllText(ReportPath, Json.Serialize(new {
                outcome = Outcome, preview = Preview, launchRequested = LaunchRequested,
                launchSucceeded = LaunchSucceeded, elapsedMs = Elapsed.ElapsedMilliseconds,
                bgmOpened = BgmOpened, bgmProgressed = BgmProgressed, bgmStatus = BgmStatus,
                displayDevice = DisplayDevice, splashDevice = SplashDevice, sameMonitor = SameMonitor, voiceCues = VoiceCues,
                launchAtMs = LaunchAtMs, introFinishedAtMs = IntroFinishedAtMs,
                userConfirmed = UserConfirmed, awaitedConfirmation = AwaitedConfirmation, voiceCompleted = VoiceCompleted,
                repairedOverlays = RepairedOverlays, sequenceSeconds = SequenceSeconds, visualStatistics = VisualStatistics
            }), Encoding.UTF8);
        }
        private static int Render(Preferences p, string output)
        {
            var w = new SplashWindow(p); w.Width = 1280; w.Height = 720;
            var content = (FrameworkElement)w.Content;
            content.Measure(new Size(1280, 720)); content.Arrange(new Rect(0, 0, 1280, 720));
            w.SetEvaProgress(RenderProgress);
            w.SetEvaPreviewTime(RenderTime);
            if (RenderProgress >= 1) w.ShowConfirmationPreview();
            content.UpdateLayout();
            var bitmap = new RenderTargetBitmap(1280, 720, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(content);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(output)) encoder.Save(stream);
            return 0;
        }
        private static int SelfTest(string output)
        {
            Directory.CreateDirectory(output);
            var results = new List<string>();
            var p = new Preferences { ImageSeconds = -1, FadeSeconds = 99, Volume = Double.NaN, Mode = "wrong" };
            p.Normalize();
            if (p.ImageSeconds != 1 || p.FadeSeconds != 2 || p.Volume != 0 || p.Mode != "builtin") return 1;
            results.Add("配置边界与无效数字：通过");
            p.Mode = "eva"; p.EvaSeconds = 8; p.Normalize();
            if (p.Mode != "eva" || p.EvaSeconds != 8) return 6;
            results.Add("EVA 模式与独立时长：通过");
            string original = ConfigPath; ConfigPath = Path.Combine(output, "roundtrip.json");
            p.MediaPath = "中文 空格 # 图片.png"; Save(p);
            if (Load().MediaPath != p.MediaPath) return 2;
            p.ImageSeconds = 7; Save(p);
            if (!File.Exists(ConfigPath + ".bak") || Load().ImageSeconds != 7) return 3;
            results.Add("中文路径、配置读写与备份：通过");
            File.WriteAllText(ConfigPath, "{bad json", Encoding.UTF8);
            if (Load().Mode != "builtin") return 4;
            results.Add("配置损坏回退：通过"); ConfigPath = original;
            p.AppId = "bad app & command";
            try { p.Normalize(); return 5; } catch (InvalidDataException) { results.Add("拒绝无效应用标识：通过"); }
            File.WriteAllLines(Path.Combine(output, "self-test.txt"), results, Encoding.UTF8);
            return 0;
        }
    }

    public sealed class SplashWindow : Window
    {
        private readonly Preferences p;
        private readonly Grid scene = new Grid();
        private readonly Grid stage = new Grid();
        private readonly TextBlock note;
        private readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        private readonly Stopwatch clock = new Stopwatch();
        private readonly Stopwatch progressClock = new Stopwatch();
        private MediaElement player;
        private TimeSpan lastPosition;
        private bool closing;
        private bool ready;
        private bool mediaOpened;
        private bool pausedForSettings;
        private EvaBootScene evaScene;
        private MediaPlayer bgm;
        private DispatcherTimer audioFade;
        private readonly DisplayHandoff display = new DisplayHandoff(Program.RequestedScreen);
        private MediaPlayer voice;
        private int voiceIndex = -1;
        private bool voiceActive;
        private readonly BootTimeline timeline;
        private readonly Canvas confirmLayer = new Canvas();
        private readonly Button confirmButton = Button("PLEASE WAIT...");
        private bool awaitingConfirmation;
        private Button skipButton;

        public SplashWindow(Preferences preferences)
        {
            p = preferences;
            timeline = new BootTimeline(p.EvaSeconds, p.VoiceEnabled && p.VoiceVolume > 0 && p.EvaSeconds >= 15);
            Program.SequenceSeconds = p.Mode == "eva" ? timeline.Duration : p.Mode == "video" ? 0 : p.ImageSeconds;
            Title = "Codex Boot Sequence";
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Normal; Background = Brushes.Black;
            Topmost = true;
            Program.DisplayDevice = display.Device;
            SourceInitialized += delegate { if (!Program.Preview) display.BeginIntro(); if (!Program.Windowed) display.PlaceSplash(this); };
            if (Program.Windowed) { WindowState = WindowState.Normal; Width = 960; Height = 600; WindowStartupLocation = WindowStartupLocation.CenterScreen; Topmost = false; }
            scene.Background = new SolidColorBrush(Color.FromRgb(9, 15, 18));
            scene.Children.Add(stage);
            if (p.Mode == "eva")
            { evaScene = new EvaBootScene(); stage.Children.Add(evaScene); timer.Interval = TimeSpan.FromMilliseconds(33); }
            else BuildBuiltin();
            var footer = new Grid { Margin = new Thickness(44, 0, 44, 34), VerticalAlignment = VerticalAlignment.Bottom };
            footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            note = Text("BOOT SEQUENCE / STAND BY", 13, "#98B5B0"); note.VerticalAlignment = VerticalAlignment.Center;
            footer.Children.Add(note);
            var controls = new StackPanel { Orientation = Orientation.Horizontal };
            var settings = Button("SETTINGS"); settings.Click += OpenSettings; controls.Children.Add(settings);
            var skip = Button(Program.Preview ? "CLOSE / ESC" : "SKIP / ESC");
            skipButton = skip;
            skip.Margin = new Thickness(12, 0, 0, 0); skip.Click += delegate { SkipOrCancel(); };
            if (p.Mode == "eva")
            {
                footer.Margin = new Thickness(44, 0, 44, 12);
                note.Foreground = new SolidColorBrush(Color.FromRgb(129, 174, 190));
                foreach (var control in new[] { settings, skip })
                { control.Background = new SolidColorBrush(Color.FromRgb(12, 27, 38)); control.BorderBrush = new SolidColorBrush(Color.FromRgb(36, 89, 107)); control.Foreground = new SolidColorBrush(Color.FromRgb(85, 221, 245)); }
            }
            controls.Children.Add(skip); Grid.SetColumn(controls, 1); footer.Children.Add(controls); scene.Children.Add(footer);
            confirmButton.Visibility = Visibility.Collapsed; confirmButton.IsEnabled = false;
            confirmButton.Background = new SolidColorBrush(Color.FromRgb(8, 24, 32)); confirmButton.Foreground = new SolidColorBrush(Color.FromRgb(85, 221, 245));
            confirmButton.BorderBrush = confirmButton.Foreground;
            confirmButton.FontFamily = new FontFamily("Consolas"); confirmButton.Padding = new Thickness(0);
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); border.AppendChild(presenter);
            var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(21, 69, 84)))); template.Triggers.Add(hover);
            var pressed = new Trigger { Property = System.Windows.Controls.Button.IsPressedProperty, Value = true };
            pressed.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(33, 97, 110)))); template.Triggers.Add(pressed);
            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.45)); template.Triggers.Add(disabled);
            confirmButton.Template = template;
            System.Windows.Automation.AutomationProperties.SetName(confirmButton, "ENTER CODEX");
            confirmButton.Click += delegate { ConfirmEntry(); };
            confirmLayer.Children.Add(confirmButton); scene.Children.Add(confirmLayer);
            scene.SizeChanged += delegate { LayoutConfirmButton(); };
            Content = scene;
            PreviewKeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { e.Handled = true; SkipOrCancel(); } else if (e.Key == Key.Enter && awaitingConfirmation) { e.Handled = true; ConfirmEntry(); } };
            stage.MouseLeftButtonDown += delegate { if (p.Mode != "eva") Finish("点击跳过"); };
            ContentRendered += Start;
            Closing += delegate { timer.Stop(); if (evaScene != null) { Program.VisualStatistics = evaScene.Statistics(); evaScene.StopAnimation(); } StopVoice(); StopBgm(); display.Dispose(); if (player != null) { player.Stop(); player.Close(); } Program.Log("开场结束", Program.Outcome); };
        }

        public void SetEvaProgress(double value)
        {
            if (evaScene == null) return;
            evaScene.SetProgress(value);
            if (value >= 0.8 && p.ConfirmRequired && !Program.AutoConfirm) { confirmButton.Visibility = Visibility.Visible; LayoutConfirmButton(); }
        }
        public void ShowConfirmationPreview() { WaitForConfirmation(); }
        public void SetEvaPreviewTime(double seconds) { if (evaScene != null) evaScene.SetPreviewTime(seconds); }
        private void LayoutConfirmButton()
        {
            double scale = Math.Min(scene.ActualWidth / 1600, scene.ActualHeight / 900);
            Canvas.SetLeft(confirmButton, (scene.ActualWidth-1600*scale)/2 + 510*scale);
            Canvas.SetTop(confirmButton, (scene.ActualHeight-900*scale)/2 + 700*scale);
            confirmButton.Width = 580*scale; confirmButton.Height = 52*scale; confirmButton.FontSize = Math.Max(12,22*scale);
        }
        private void WaitForConfirmation()
        {
            if (awaitingConfirmation) return;
            awaitingConfirmation = true; Program.AwaitedConfirmation = true;
            if (evaScene != null) evaScene.SetConfirmationReady();
            SetEvaProgress(1); confirmButton.Visibility = Visibility.Visible; confirmButton.IsEnabled = true; confirmButton.Content = "ENTER CODEX";
            LayoutConfirmButton(); confirmButton.Focus(); skipButton.Content = "CANCEL / ESC";
            note.Text = "ALL SYSTEMS NOMINAL / AWAITING YOUR CONFIRMATION";
            if (evaScene != null) Program.VisualStatistics = evaScene.Statistics();
            Program.Outcome = "等待用户确认"; Program.WriteReport();
        }
        private void ConfirmEntry()
        { if (!awaitingConfirmation || closing) return; Program.UserConfirmed = true; confirmButton.IsEnabled = false; Finish("用户确认进入"); }
        private void SkipOrCancel()
        {
            if (p.Mode != "eva" || !p.ConfirmRequired) { Finish("手动跳过"); return; }
            if (awaitingConfirmation) { Program.Outcome = "用户取消启动"; Close(); return; }
            StopVoice(); voiceIndex = 5; WaitForConfirmation();
        }

        public static TextBlock Text(string value, double size, string color)
        { return new TextBlock { Text = value, FontSize = size, Foreground = (Brush)new BrushConverter().ConvertFromString(color), FontFamily = new FontFamily("Microsoft YaHei UI"), TextWrapping = TextWrapping.Wrap }; }
        public static Button Button(string value)
        {
            return new Button { Content = value, Padding = new Thickness(18, 9, 18, 9), FontSize = 13,
                Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromRgb(31, 48, 51)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(55, 77, 79)), Cursor = Cursors.Hand };
        }
        private void BuildBuiltin()
        {
            var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var mark = new Border { Width = 100, Height = 100, CornerRadius = new CornerRadius(28), BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(78, 147, 135)), Background = new SolidColorBrush(Color.FromRgb(18, 40, 39)), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 30) };
            var glyph = Text(">_", 46, "#A8F3D9"); glyph.FontFamily = new FontFamily("Consolas"); glyph.HorizontalAlignment = HorizontalAlignment.Center; glyph.VerticalAlignment = VerticalAlignment.Center;
            mark.Child = glyph; panel.Children.Add(mark);
            var title = Text("Codex", 56, "#F0F7F4"); title.HorizontalAlignment = HorizontalAlignment.Center; panel.Children.Add(title);
            var sub = Text("BRING YOUR IDEAS ONLINE", 17, "#9BB9B2"); sub.HorizontalAlignment = HorizontalAlignment.Center; sub.Margin = new Thickness(0, 16, 0, 38); panel.Children.Add(sub);
            var rail = new Border { Height = 2, Width = 210, Background = new SolidColorBrush(Color.FromRgb(31, 55, 52)), ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Center };
            var bar = new Border { Width = 70, Height = 2, HorizontalAlignment = HorizontalAlignment.Left, Background = new SolidColorBrush(Color.FromRgb(129, 234, 200)) };
            var slide = new TranslateTransform(); bar.RenderTransform = slide; rail.Child = bar; panel.Children.Add(rail);
            Loaded += delegate
            {
                slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-70, 210, TimeSpan.FromSeconds(1.7)) { RepeatBehavior = RepeatBehavior.Forever });
                mark.BeginAnimation(OpacityProperty, new DoubleAnimation(0.5, 1, TimeSpan.FromSeconds(1.2)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
            };
            stage.Children.Add(panel);
        }
        private void Start(object sender, EventArgs e)
        {
            ContentRendered -= Start;
            if (!Program.Windowed) display.PlaceSplash(this);
            Program.SplashDevice = display.WindowDevice(this);
            Program.Outcome = "播放中"; clock.Start(); progressClock.Start();
            timer.Tick += Tick; timer.Start();
            // 动画期间不启动应用；已存在的 Codex 窗口临时隐藏，避免另一块屏幕提前显示。
            if (!Program.Preview) { Activate(); }
            else note.Text = "PREVIEW / NO APPLICATION LAUNCH";
            try
            {
                if (p.Mode == "builtin" || p.Mode == "eva") { if (p.Mode == "eva") StartBgm(); ready = true; return; }
                string path = Path.IsPathRooted(p.MediaPath) ? p.MediaPath : Path.Combine(Program.Root, p.MediaPath);
                if (!File.Exists(path)) { Finish("素材不存在"); return; }
                var uri = new Uri(Path.GetFullPath(path));
                Stretch fit = p.Fit == "contain" ? Stretch.Uniform : Stretch.UniformToFill;
                if (p.Mode == "image")
                {
                    // 先检查文件大小和尺寸，避免完整解码超大图片。
                    using (var stream = File.OpenRead(path))
                    {
                        if (stream.Length > 20 * 1024 * 1024) { Finish("图片文件过大"); return; }
                        var metadata = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
                        if ((long)metadata.PixelWidth * metadata.PixelHeight > 32000000) { Finish("图片尺寸过大"); return; }
                    }
                    // 加载后释放文件句柄，播放期间可以替换原素材。
                    var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.UriSource = uri; bitmap.EndInit(); bitmap.Freeze();
                    if ((long)bitmap.PixelWidth * bitmap.PixelHeight > 32000000) { Finish("图片尺寸过大"); return; }
                    var picture = new Image { Source = bitmap, Stretch = fit, RenderTransformOrigin = new Point(0.5, 0.5) };
                    stage.Children.Clear(); stage.Children.Add(picture); stage.ClipToBounds = true;
                    var zoom = new ScaleTransform(1, 1); picture.RenderTransform = zoom;
                    zoom.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 1.04, TimeSpan.FromSeconds(p.ImageSeconds)));
                    zoom.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 1.04, TimeSpan.FromSeconds(p.ImageSeconds)));
                    ready = true; clock.Restart();
                }
                else
                {
                    player = new MediaElement { LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Close, Stretch = fit, Volume = p.Volume, ScrubbingEnabled = true };
                    player.MediaOpened += delegate { mediaOpened = true; progressClock.Restart(); };
                    player.MediaEnded += delegate { Finish("视频播完"); };
                    player.MediaFailed += delegate { Finish("视频解码失败"); };
                    stage.Children.Clear(); stage.Children.Add(player); player.Source = uri; player.Play();
                }
            }
            catch (Exception error) { Program.Log("素材加载失败", error.GetType().Name); Finish("素材加载失败"); }
        }
        private void StartBgm()
        {
            if (!p.BgmEnabled || p.BgmVolume <= 0) return;
            try
            {
                string path = Path.Combine(Program.Root, "assets", "eva-terminal.wav");
                if (!File.Exists(path)) { Program.BgmStatus = "文件缺失"; Program.Log("背景音乐跳过", "文件缺失"); return; }
                bgm = new MediaPlayer(); bgm.Volume = 0; Program.BgmStatus = "加载中";
                bgm.MediaOpened += delegate { Program.BgmOpened = true; Program.BgmStatus = "播放中"; };
                bgm.MediaEnded += delegate { if (bgm != null && !closing) { bgm.Position = TimeSpan.Zero; bgm.Play(); } };
                bgm.MediaFailed += delegate { Program.BgmStatus = "解码失败"; Program.Log("背景音乐跳过", "解码失败"); StopBgm(); };
                bgm.Open(new Uri(path)); bgm.Play();
            }
            catch (Exception error) { Program.BgmStatus = "播放失败"; Program.Log("背景音乐跳过", error.GetType().Name); StopBgm(); }
        }
        private void StopBgm()
        {
            if (audioFade != null) { audioFade.Stop(); audioFade = null; }
            if (bgm != null) { bgm.Close(); bgm = null; }
        }
        private void FadeBgm()
        {
            if (bgm == null) return;
            if (p.FadeSeconds <= 0) { StopBgm(); return; }
            double initial = bgm.Volume;
            var elapsed = Stopwatch.StartNew();
            audioFade = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            audioFade.Tick += delegate
            {
                if (bgm == null || elapsed.Elapsed.TotalSeconds >= p.FadeSeconds) { StopBgm(); return; }
                bgm.Volume = initial * Math.Max(0, 1 - elapsed.Elapsed.TotalSeconds / p.FadeSeconds);
            };
            audioFade.Start();
        }
        private void Tick(object sender, EventArgs e)
        {
            if (closing || pausedForSettings) return;
            if (p.Mode == "eva")
            {
                if (awaitingConfirmation) { if (bgm != null) bgm.Volume = p.BgmVolume * 0.18; return; }
                PlayVoiceCue(clock.Elapsed.TotalSeconds);
                if (bgm != null)
                {
                    if (bgm.Position.TotalMilliseconds > 0) Program.BgmProgressed = true;
                    bgm.Volume = p.BgmVolume * (voiceActive ? 0.10 : 1) * Math.Min(1, clock.Elapsed.TotalSeconds / 0.65) * Math.Max(0, Math.Min(1, (timeline.Duration - clock.Elapsed.TotalSeconds) / 0.8));
                }
                double visual = timeline.VisualProgress(clock.Elapsed.TotalSeconds); SetEvaProgress(visual);
                if (clock.Elapsed.TotalSeconds >= timeline.Duration && !voiceActive)
                { if (p.ConfirmRequired && !Program.AutoConfirm) WaitForConfirmation(); else Finish("EVA动画播完"); }
                return;
            }
            if (p.Mode != "video") { if (ready && clock.Elapsed.TotalSeconds >= p.ImageSeconds) Finish("图片动画播完"); return; }
            if (player != null && player.Position > lastPosition)
            { lastPosition = player.Position; ready = true; progressClock.Restart(); }
            if (!ready && clock.Elapsed.TotalSeconds > 8) Finish("视频加载超时");
            else if (ready && progressClock.Elapsed.TotalSeconds > 10) Finish("视频播放停滞");
            else if (mediaOpened && player != null && player.NaturalDuration.HasTimeSpan && player.Position >= player.NaturalDuration.TimeSpan && ready)
                Finish("视频播完");
        }
        private void StopVoice()
        { if (voice != null) { voice.Close(); voice = null; } voiceActive = false; }
        private void PlayVoiceCue(double seconds)
        {
            if (!p.VoiceEnabled || p.VoiceVolume <= 0 || p.EvaSeconds < 15) return;
            int next = voiceIndex + 1;
            if (next >= timeline.Starts.Length || seconds < timeline.Starts[next] || voiceActive) return;
            StopVoice(); voiceIndex = next;
            string path = Path.Combine(Program.Root, "assets", "voice", "cue-" + next + ".wav");
            if (!File.Exists(path)) return;
            try
            {
                voice = new MediaPlayer { Volume = p.VoiceVolume };
                voice.MediaOpened += delegate { Program.VoiceCues++; };
                voice.MediaEnded += delegate { voiceActive = false; Program.VoiceCompleted++; };
                voice.MediaFailed += delegate { voiceActive = false; };
                voiceActive = true; voice.Open(new Uri(path)); voice.Play();
            }
            catch { StopVoice(); }
        }
        private void OpenSettings(object sender, RoutedEventArgs e)
        {
            e.Handled = true; pausedForSettings = true; clock.Stop(); progressClock.Stop();
            if (evaScene != null) evaScene.SetPaused(true);
            if (player != null) player.Pause();
            if (bgm != null) bgm.Pause();
            if (voice != null) voice.Pause();
            Topmost = false;
            var settings = new SettingsWindow(Program.Load()); settings.Owner = this; settings.ShowDialog();
            Topmost = true; pausedForSettings = false; clock.Start(); progressClock.Start();
            if (evaScene != null) evaScene.SetPaused(false);
            if (player != null) player.Play();
            if (bgm != null) bgm.Play();
            if (voice != null) voice.Play();
        }
        public void Finish(string reason)
        {
            if (closing) return;
            closing = true; Program.Outcome = reason; timer.Stop();
            Program.IntroFinishedAtMs = Program.Elapsed.ElapsedMilliseconds;
            StopVoice();
            if (player != null) player.Volume = 0;
            FadeBgm();
            if (Program.Preview) { CloseWithFade(); return; }
            // 播放完成后才激活商店应用；窗口移到同一屏幕后再撤下开场。
            Program.Launch(p); note.Text = "STARTUP CONFIRMED / ENTERING CODEX";
            var wait = Stopwatch.StartNew();
            var handoff = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            handoff.Tick += delegate
            {
                if (display.TryEnter()) { Program.SameMonitor = display.SameMonitor && Program.SplashDevice == display.Device; handoff.Stop(); CloseWithFade(); }
                else if (wait.Elapsed.TotalSeconds > 20) { handoff.Stop(); Program.Log("窗口接入超时", display.Device); CloseWithFade(); }
            };
            Closed += delegate { handoff.Stop(); };
            handoff.Start();
        }
        private void CloseWithFade()
        {
            if (p.FadeSeconds <= 0) { Close(); return; }
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(p.FadeSeconds));
            fade.Completed += delegate { Close(); };
            BeginAnimation(OpacityProperty, fade);
        }
    }

    public sealed class SettingsWindow : Window
    {
        private readonly Preferences p;
        private readonly ComboBox mode = new ComboBox();
        private readonly ComboBox fit = new ComboBox();
        private readonly TextBox media = new TextBox();
        private readonly TextBox seconds = new TextBox();
        private readonly TextBox fade = new TextBox();
        private readonly Slider volume = new Slider { Minimum = 0, Maximum = 100, TickFrequency = 10 };
        private readonly CheckBox bgmEnabled = new CheckBox { Content = "Enable EVA background music" };
        private readonly Slider bgmVolume = new Slider { Minimum = 0, Maximum = 100, TickFrequency = 10 };
        private readonly CheckBox voiceEnabled = new CheckBox { Content = "Enable English AI voice (24s recommended; muted below 15s)" };
        private readonly CheckBox confirmRequired = new CheckBox { Content = "Wait for confirmation before entering Codex" };
        private readonly Slider voiceVolume = new Slider { Minimum = 0, Maximum = 100, TickFrequency = 10 };
        private readonly CheckBox enabled = new CheckBox { Content = "Enable startup sequence" };
        private readonly TextBlock status = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DarkSlateGray, Margin = new Thickness(0, 12, 0, 0) };

        public SettingsWindow(Preferences preferences)
        {
            p = preferences; Title = "Codex Startup Settings"; Width = 690; Height = 640;
            MinWidth = 580; MinHeight = 520; WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = new FontFamily("Microsoft YaHei UI"); FontSize = 14;
            Background = new SolidColorBrush(Color.FromRgb(246, 248, 247));
            var panel = new StackPanel { Margin = new Thickness(30) };
            panel.Children.Add(new TextBlock { Text = "CODEX STARTUP CONTROL", FontSize = 23, Margin = new Thickness(0, 0, 0, 10) });
            panel.Children.Add(new TextBlock { Text = "Changes apply to the next launch from the Codex startup shortcut.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 0, 0, 22) });
            enabled.IsChecked = p.Enabled; panel.Children.Add(enabled);
            mode.ItemsSource = new[] { "Built-in", "Image", "Video", "EVA · NERV / MAGI" }; mode.SelectedIndex = p.Mode == "eva" ? 3 : p.Mode == "image" ? 1 : p.Mode == "video" ? 2 : 0;
            Add(panel, "Startup content", mode);
            var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            media.Text = p.MediaPath; media.Padding = new Thickness(7); row.Children.Add(media);
            var browse = new Button { Content = "Browse...", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(10, 0, 0, 0) };
            browse.Click += Browse; Grid.SetColumn(browse, 1); row.Children.Add(browse); Add(panel, "Local media (PNG / JPEG / BMP / MP4 / WMV / AVI)", row);
            seconds.Text = (p.Mode == "eva" ? p.EvaSeconds : p.ImageSeconds).ToString(CultureInfo.CurrentCulture); Add(panel, "Sequence duration (1-120 seconds; videos play to the end)", seconds);
            mode.SelectionChanged += delegate { seconds.Text = (mode.SelectedIndex == 3 ? p.EvaSeconds : p.ImageSeconds).ToString(CultureInfo.CurrentCulture); };
            fade.Text = p.FadeSeconds.ToString(CultureInfo.CurrentCulture); Add(panel, "Exit fade (0-2 seconds)", fade);
            fit.ItemsSource = new[] { "Fill screen (crop)", "Fit screen (letterbox)" }; fit.SelectedIndex = p.Fit == "contain" ? 1 : 0; Add(panel, "Screen fit", fit);
            volume.Value = p.Volume * 100; Add(panel, "Video volume (0-100)", volume);
            bgmEnabled.IsChecked = p.BgmEnabled; bgmEnabled.Margin = new Thickness(0, 15, 0, 0); panel.Children.Add(bgmEnabled);
            bgmVolume.Value = p.BgmVolume * 100; Add(panel, "EVA music volume (default 38.5%)", bgmVolume);
            voiceEnabled.IsChecked = p.VoiceEnabled; voiceEnabled.Margin = new Thickness(0, 15, 0, 0); panel.Children.Add(voiceEnabled);
            voiceVolume.Value = p.VoiceVolume * 100; Add(panel, "AI voice volume (0-100)", voiceVolume);
            confirmRequired.IsChecked = p.ConfirmRequired; confirmRequired.Margin = new Thickness(0, 15, 0, 0); panel.Children.Add(confirmRequired);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 20, 0, 0) };
            var save = new Button { Content = "SAVE", Padding = new Thickness(22, 9, 22, 9) }; save.Click += delegate { Save(); }; buttons.Children.Add(save);
            var preview = new Button { Content = "SAVE & PREVIEW", Padding = new Thickness(22, 9, 22, 9), Margin = new Thickness(12, 0, 0, 0) };
            preview.Click += delegate
            {
                if (!Save()) return;
                Process.Start(new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName, "--preview --config \"" + Program.ConfigPath + "\"") { UseShellExecute = true });
            };
            buttons.Children.Add(preview);
            var reset = new Button { Content = "RESET", Padding = new Thickness(15, 9, 15, 9), Margin = new Thickness(12, 0, 0, 0) };
            reset.Click += delegate { enabled.IsChecked = true; mode.SelectedIndex = 0; media.Text = ""; seconds.Text = "4"; fade.Text = "0.5"; fit.SelectedIndex = 0; volume.Value = 0; bgmEnabled.IsChecked = true; bgmVolume.Value = 38.5; voiceEnabled.IsChecked = true; voiceVolume.Value = 100; confirmRequired.IsChecked = true; }; buttons.Children.Add(reset);
            panel.Children.Add(buttons); panel.Children.Add(status);
            Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }
        private void Add(StackPanel panel, string label, FrameworkElement element)
        {
            panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 13, 0, 5), TextWrapping = TextWrapping.Wrap });
            if (element is TextBox) ((TextBox)element).Padding = new Thickness(7);
            panel.Children.Add(element);
        }
        private void Browse(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Filter = "Images and videos|*.png;*.jpg;*.jpeg;*.bmp;*.mp4;*.wmv;*.avi;*.m4v;*.mov|All files|*.*", CheckFileExists = true };
            if (dialog.ShowDialog(this) != true) return;
            media.Text = dialog.FileName;
            mode.SelectedIndex = new[] { ".png", ".jpg", ".jpeg", ".bmp" }.Contains(Path.GetExtension(dialog.FileName).ToLowerInvariant()) ? 1 : 2;
        }
        private bool Save()
        {
            double duration, transition;
            if (!Double.TryParse(seconds.Text, out duration) || Double.IsNaN(duration) || Double.IsInfinity(duration) || duration < 1 || duration > 120 ||
                !Double.TryParse(fade.Text, out transition) || Double.IsNaN(transition) || Double.IsInfinity(transition) || transition < 0 || transition > 2)
            { status.Text = "Invalid duration. Check the allowed ranges."; return false; }
            string mediaPath = media.Text;
            if (mode.SelectedIndex == 1 || mode.SelectedIndex == 2)
            {
                try { mediaPath = Path.GetFullPath(Path.IsPathRooted(mediaPath) ? mediaPath : Path.Combine(Program.Root, mediaPath)); }
                catch { status.Text = "Invalid media path. Please browse again."; return false; }
                if (!File.Exists(mediaPath)) { status.Text = "Media file not found. Please browse again."; return false; }
            }
            p.Enabled = enabled.IsChecked == true; p.Mode = mode.SelectedIndex == 3 ? "eva" : mode.SelectedIndex == 1 ? "image" : mode.SelectedIndex == 2 ? "video" : "builtin";
            p.MediaPath = mediaPath;
            if (p.Mode == "eva") p.EvaSeconds = duration; else p.ImageSeconds = duration;
            p.FadeSeconds = transition;
            p.Fit = fit.SelectedIndex == 1 ? "contain" : "cover"; p.Volume = volume.Value / 100;
            p.BgmEnabled = bgmEnabled.IsChecked == true; p.BgmVolume = bgmVolume.Value / 100;
            p.VoiceEnabled = voiceEnabled.IsChecked == true; p.VoiceVolume = voiceVolume.Value / 100;
            p.ConfirmRequired = confirmRequired.IsChecked == true;
            try { Program.Save(p); status.Text = "Settings saved."; return true; }
            catch (Exception e) { Program.Log("保存设置失败", e.GetType().Name); status.Text = "Unable to save. Check folder permissions."; return false; }
        }
    }
}
