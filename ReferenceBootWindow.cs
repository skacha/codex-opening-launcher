using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace CodexOpening
{
    // 使用原版网页绘制器承载画面；本机窗口隐藏、恢复和接入仍由 DisplayHandoff 管理。
    public sealed class ReferenceBootWindow : Window
    {
        private readonly Preferences preferences;
        private readonly bool captureOnly;
        private readonly bool recording;
        private readonly WebView2 view = new WebView2();
        private readonly DisplayHandoff display;
        private readonly TaskCompletionSource<bool> ready = new TaskCompletionSource<bool>();
        private readonly JavaScriptSerializer json = new JavaScriptSerializer();
        private readonly Stopwatch elapsed = new Stopwatch();
        private readonly DispatcherTimer audioTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        private MediaPlayer music;
        private bool closing, disposed, speaking;
        private CoreWebView2DevToolsProtocolEventReceiver receiver;
        public readonly TaskCompletionSource<bool> RecordingCompleted = new TaskCompletionSource<bool>();
        public readonly List<double> RecordedVoiceStarts = new List<double>();
        public event Action<byte[], double> FrameCaptured;
        private double frameOrigin;

        public ReferenceBootWindow(Preferences p, bool capture, bool record = false)
        {
            preferences = p; captureOnly = capture; recording = record; display = new DisplayHandoff(Program.RequestedScreen);
            Title = "Codex Boot Sequence"; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            Background = Brushes.Black; Content = view; view.DefaultBackgroundColor = System.Drawing.Color.Black;
            view.HorizontalAlignment = HorizontalAlignment.Center; view.VerticalAlignment = VerticalAlignment.Center;
            SizeChanged += delegate { FitReferenceViewport(); };
            if (capture) { Width = 1264; Height = 800; Left = -32000; Top = -32000; ShowInTaskbar = false; }
            else if (Program.Windowed) { Width = 1106; Height = 700; WindowStartupLocation = WindowStartupLocation.CenterScreen; }
            else Topmost = true;
            SourceInitialized += delegate
            {
                if (captureOnly) return;
                Program.DisplayDevice = display.Device;
                if (!Program.Preview) display.BeginIntro();
                if (!Program.Windowed) display.PlaceSplash(this);
            };
            Loaded += async delegate { await Initialize(); };
            Closing += delegate
            {
                disposed = true; audioTimer.Stop(); if (music != null) { music.Close(); music = null; }
                if (!closing && !captureOnly) { Program.UserConfirmed = false; Program.Outcome = "用户取消启动"; }
                display.Dispose(); view.Dispose(); Program.WriteReport();
            };
        }
        private async Task Initialize()
        {
            try
            {
                // 网页缓存固定保存在 F 盘项目目录，避免新增 C 盘 WebView 配置目录。
                string profile = Path.Combine(Program.Root, "artifacts", captureOnly ? recording ? "reference-webview-recording" : "reference-webview-capture" : "reference-webview-profile");
                var options = new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required --disable-background-timer-throttling --disable-renderer-backgrounding --disable-backgrounding-occluded-windows");
                var environment = await CoreWebView2Environment.CreateAsync(null, profile, options);
                if (disposed) return;
                await view.EnsureCoreWebView2Async(environment);
                if (disposed) return;
                FitReferenceViewport();
                var core = view.CoreWebView2;
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.AreHostObjectsAllowed = false;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.IsZoomControlEnabled = false;
                core.SetVirtualHostNameToFolderMapping("codex-boot.local", Path.Combine(Program.Root, "assets"), CoreWebView2HostResourceAccessKind.DenyCors);
                core.NewWindowRequested += delegate(object sender, CoreWebView2NewWindowRequestedEventArgs e) { e.Handled = true; };
                core.NavigationStarting += delegate(object sender, CoreWebView2NavigationStartingEventArgs e) { if (!e.Uri.StartsWith("https://codex-boot.local/", StringComparison.Ordinal)) e.Cancel = true; };
                core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
                core.WebResourceRequested += delegate(object sender, CoreWebView2WebResourceRequestedEventArgs e)
                {
                    if (!e.Request.Uri.StartsWith("https://codex-boot.local/", StringComparison.Ordinal) && !e.Request.Uri.StartsWith("data:", StringComparison.Ordinal))
                        e.Response = environment.CreateWebResourceResponse(new MemoryStream(), 403, "Forbidden", "Content-Type: text/plain");
                };
                core.WebMessageReceived += Message;
                core.NavigationCompleted += delegate(object sender, CoreWebView2NavigationCompletedEventArgs e) { if (!e.IsSuccess) Fail("网页加载失败"); };
                IList metrics = (IList)json.Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(Program.Root, "assets", "reference-voice", "台词.json")))["Metrics"];
                var lengths = new double[6]; for (int i = 0; i < 6; i++) lengths[i] = Convert.ToDouble(((Dictionary<string, object>)metrics[i])["Seconds"]);
                bool spoken = preferences.VoiceEnabled && preferences.VoiceVolume > 0 && preferences.EvaSeconds >= 15;
                var palette = json.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(Program.Root, "assets", "reference-boot", "palette.json")));
                var config = new { capture = captureOnly && !recording, record = recording, requireInteraction = !recording && preferences.ConfirmRequired && !Program.AutoConfirm,
                    voice = spoken, voiceVolume = preferences.VoiceVolume, voiceLengths = lengths,
                    speed = spoken ? 1 : preferences.EvaSeconds < 15 ? 3 : 1, sound = !captureOnly,
                    palette = new { black = palette["black"], purple = palette["purple"], white = palette["white"] } };
                await core.AddScriptToExecuteOnDocumentCreatedAsync("window.__CODEX_NATIVE_HOST__=true;window.__CODEX_HOST_CONFIG__=" + json.Serialize(config) + ";");
                if (recording)
                {
                    receiver = core.GetDevToolsProtocolEventReceiver("Page.screencastFrame");
                    receiver.DevToolsProtocolEventReceived += async delegate(object sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
                    {
                        try
                        {
                            var frame = json.Deserialize<Dictionary<string, object>>(e.ParameterObjectAsJson);
                            var metadata = (Dictionary<string, object>)frame["metadata"];
                            double stamp = Convert.ToDouble(metadata["timestamp"]);
                            if (frameOrigin == 0) frameOrigin = stamp;
                            if (FrameCaptured != null) FrameCaptured(Convert.FromBase64String((string)frame["data"]), stamp - frameOrigin);
                            await core.CallDevToolsProtocolMethodAsync("Page.screencastFrameAck", "{\"sessionId\":" + Convert.ToInt32(frame["sessionId"]) + "}");
                        }
                        catch (Exception error) { RecordingCompleted.TrySetException(error); }
                    };
                    await core.CallDevToolsProtocolMethodAsync("Page.startScreencast", "{\"format\":\"jpeg\",\"quality\":92,\"maxWidth\":1100,\"maxHeight\":700,\"everyNthFrame\":1}");
                }
                core.Navigate("https://codex-boot.local/reference-boot/index.html");
                if (await Task.WhenAny(ready.Task, Task.Delay(20000)) != ready.Task && !disposed) Fail("参考动画初始化超时");
            }
            catch (Exception e) { Program.Log("参考动画初始化失败", e.GetType().Name); Fail(e.GetType().Name); }
        }
        private void FitReferenceViewport()
        {
            // 对齐截图的 1.58:1 档案底板；字体和容器一起等比缩放，保留原片边缘黑场。
            double availableHeight = ActualHeight * (!captureOnly && !Program.Windowed ? 1400.0 / 1430 : 1);
            double scale = Math.Min(ActualWidth / 1264, availableHeight / 800);
            if (scale <= 0) return;
            view.Width = 1264 * scale; view.Height = 800 * scale;
            if (view.CoreWebView2 != null) view.ZoomFactor = scale;
        }
        private void Message(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            if (disposed || e.Source != "https://codex-boot.local/reference-boot/index.html") return;
            try
            {
                var message = json.Deserialize<Dictionary<string, object>>(e.WebMessageAsJson);
                string name = Convert.ToString(message["event"]);
                if (name == "ready")
                {
                    ready.TrySetResult(true);
                    if (!captureOnly)
                    {
                        Program.Outcome = "播放中"; Program.SplashDevice = display.WindowDevice(this);
                        elapsed.Start(); StartMusic(); view.Focus();
                    }
                }
                else if (name == "voice-start")
                {
                    speaking = true; Program.VoiceCues++;
                    if (recording) RecordedVoiceStarts.Add((DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds - frameOrigin);
                }
                else if (name == "voice-end") { speaking = false; if (Convert.ToBoolean(message["completed"])) Program.VoiceCompleted++; }
                else if (name == "phase") Program.ReferenceStep = Convert.ToInt32(message["step"]);
                else if (name == "waiting") { Program.AwaitedConfirmation = true; Program.Outcome = "等待用户确认"; Program.WriteReport(); }
                else if (name == "confirmed") { Program.ReferenceConfirmations++; if (Program.ReferenceConfirmations >= 2) Program.UserConfirmed = true; }
                else if (name == "complete")
                {
                    Program.VisualStatistics = message["statistics"];
                    if (recording) { RecordingCompleted.TrySetResult(true); return; }
                    bool skipped = Convert.ToBoolean(message["skipped"]);
                    if (skipped) Program.UserConfirmed = true;
                    if (preferences.ConfirmRequired && !Program.AutoConfirm && !Program.UserConfirmed) { Fail("授权记录不完整"); return; }
                    Complete(skipped ? "手动跳过" : Program.AutoConfirm ? "EVA动画播完" : "原版授权序列完成");
                }
                else if (name == "failure") Fail("参考动画脚本失败");
            }
            catch (Exception error) { Program.Log("参考动画消息失败", error.GetType().Name); Fail("接入消息解析失败"); }
        }
        private void StartMusic()
        {
            if (!preferences.BgmEnabled || preferences.BgmVolume <= 0) return;
            try
            {
                music = new MediaPlayer { Volume = 0 };
                music.MediaOpened += delegate { Program.BgmOpened = true; Program.BgmStatus = "播放中"; };
                music.MediaEnded += delegate { if (music != null && !closing) { music.Position = TimeSpan.Zero; music.Play(); } };
                music.MediaFailed += delegate { Program.BgmStatus = "解码失败"; if (music != null) { music.Close(); music = null; } };
                music.Open(new Uri(Path.Combine(Program.Root, "assets", "eva-terminal.wav"))); music.Play();
                audioTimer.Tick += delegate
                {
                    if (music == null) return;
                    if (music.Position.TotalSeconds > 0) Program.BgmProgressed = true;
                    music.Volume = preferences.BgmVolume * (speaking ? 0.10 : 1) * Math.Min(1, elapsed.Elapsed.TotalSeconds / 0.65);
                };
                audioTimer.Start();
            }
            catch (Exception e) { Program.Log("背景音乐跳过", e.GetType().Name); }
        }
        private void Fail(string detail)
        {
            if (disposed || closing) return;
            ready.TrySetException(new InvalidOperationException(detail));
            Program.UserConfirmed = false; Program.Outcome = "参考动画加载失败"; Program.Log("参考动画停止", detail);
            RecordingCompleted.TrySetException(new InvalidOperationException(detail));
            closing = true; Close();
        }
        private void Complete(string outcome)
        {
            if (closing || disposed || captureOnly) return;
            closing = true; Program.Outcome = outcome; Program.IntroFinishedAtMs = Program.Elapsed.ElapsedMilliseconds;
            Program.SequenceSeconds = elapsed.Elapsed.TotalSeconds;
            audioTimer.Stop(); if (music != null) { music.Close(); music = null; }
            if (Program.Preview) { Close(); return; }
            Program.Launch(preferences);
            var handoff = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            var waiting = Stopwatch.StartNew();
            handoff.Tick += delegate
            {
                if (display.TryEnter()) { Program.SameMonitor = display.SameMonitor && Program.SplashDevice == display.Device; handoff.Stop(); Close(); }
                else if (waiting.Elapsed.TotalSeconds > 20) { handoff.Stop(); Program.Log("窗口接入超时", display.Device); Close(); }
            };
            Closed += delegate { handoff.Stop(); }; handoff.Start();
        }
        public async Task Capture(string output, int step, double seconds)
        {
            await ready.Task;
            await view.CoreWebView2.ExecuteScriptAsync("window.__CODEX_CAPTURE__.seek(" + step + "," + seconds.ToString(CultureInfo.InvariantCulture) + ");");
            await Task.Delay(45);
            using (var stream = File.Create(output)) await view.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
        }
        public async Task StopRecording()
        { if (view.CoreWebView2 != null) await view.CoreWebView2.CallDevToolsProtocolMethodAsync("Page.stopScreencast", "{}"); }
        public static int Render(Preferences p, string output, int step, double seconds)
        {
            int result = 0; var app = new Application(); var window = new ReferenceBootWindow(p, true);
            window.Loaded += async delegate
            {
                try { await window.Capture(output, step, seconds); }
                catch (Exception e) { Program.Log("参考画面导出失败", e.GetType().Name); result = 1; }
                finally { window.closing = true; window.Close(); }
            };
            app.Run(window); return result;
        }
    }
}
