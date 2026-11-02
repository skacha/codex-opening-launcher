using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace CodexOpening
{
    // 启动画面与应用共用物理显示器坐标，避免多屏及缩放比例混用。
    public sealed class DisplayHandoff : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct MonitorInfo
        { public int Size; public Rect Bounds, Work; public int Flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device; }
        private delegate bool EnumProc(IntPtr window, IntPtr value);
        private delegate bool MonitorProc(IntPtr monitor, IntPtr dc, IntPtr rect, IntPtr value);
        private delegate void EventProc(IntPtr hook, uint kind, IntPtr window, int obj, int child, uint thread, uint time);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, IntPtr value);
        [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorProc callback, IntPtr value);
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(Point point, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
        [DllImport("user32.dll")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int length);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
        [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr window, int command);
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder text, int length);
        [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint first, uint last, IntPtr module, EventProc callback, uint process, uint thread, uint flags);
        [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
        private readonly Dictionary<IntPtr, Rect> hidden = new Dictionary<IntPtr, Rect>();
        private readonly EventProc callback;
        private IntPtr hook;
        private bool handedOff;
        private readonly MonitorInfo monitor;
        private static HashSet<uint> hostProcesses = new HashSet<uint>();
        private static DateTime refreshProcessesAt = DateTime.MinValue;
        public string Device { get { return monitor.Device; } }
        public string WindowDevice(Window window) { return Info(MonitorFromWindow(new WindowInteropHelper(window).Handle, 2)).Device; }
        public bool SameMonitor { get; private set; }
        public int HiddenCount { get { return hidden.Count; } }

        public DisplayHandoff(int requestedScreen)
        {
            Point point; GetCursorPos(out point);
            IntPtr chosen = MonitorFromPoint(point, 2);
            var monitors = new List<IntPtr>();
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate(IntPtr m, IntPtr dc, IntPtr r, IntPtr v) { monitors.Add(m); return true; }, IntPtr.Zero);
            if (requestedScreen > 0) foreach (IntPtr item in monitors) if (Info(item).Device.EndsWith("DISPLAY" + requestedScreen, StringComparison.OrdinalIgnoreCase)) { chosen = item; break; }
            monitor = Info(chosen);
            callback = OnWindowEvent;
        }
        private static MonitorInfo Info(IntPtr handle)
        { var info = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) }; GetMonitorInfo(handle, ref info); return info; }
        private static bool IsCodexHost(IntPtr window)
        {
            // 缓存目标进程，避免为桌面上每个窗口分别读取一次进程名称。
            if (DateTime.UtcNow >= refreshProcessesAt)
            {
                var current = new HashSet<uint>();
                foreach (Process process in Process.GetProcessesByName("ChatGPT"))
                { using (process) { current.Add((uint)process.Id); } }
                hostProcesses = current; refreshProcessesAt = DateTime.UtcNow.AddSeconds(1);
            }
            uint id; GetWindowThreadProcessId(window, out id);
            if (!hostProcesses.Contains(id)) return false;
            try
            {
                var name = new StringBuilder(256); GetClassName(window, name, name.Capacity);
                return name.ToString() == "Chrome_WidgetWin_1";
            }
            catch { return false; }
        }
        private static bool IsCodex(IntPtr window)
        {
            if (!IsCodexHost(window)) return false;
            long style = GetWindowLongPtr(window, -16).ToInt64(), extended = GetWindowLongPtr(window, -20).ToInt64();
            // 排除工具窗、无激活窗口、子窗口和附属弹窗；不得把它们放大成全屏输入遮罩。
            return (extended & (0x80L | 0x08000000L)) == 0 && (style & 0x40000000L) == 0 &&
                GetWindow(window, 4) == IntPtr.Zero && (extended & 0x40000L) != 0;
        }
        public static int RepairLegacyOverlays()
        {
            var mains = new List<Rect>();
            EnumWindows(delegate(IntPtr w, IntPtr v) { if (IsCodex(w) && IsWindowVisible(w)) { Rect r; GetWindowRect(w, out r); mains.Add(r); } return true; }, IntPtr.Zero);
            int repaired = 0;
            EnumWindows(delegate(IntPtr w, IntPtr v)
            {
                if (!IsCodexHost(w) || !IsWindowVisible(w)) return true;
                long extended = GetWindowLongPtr(w, -20).ToInt64();
                if ((extended & 0x88L) != 0x88L) return true;
                Rect r; GetWindowRect(w, out r);
                foreach (var main in mains) if (r.Right-r.Left > 800 && r.Bottom-r.Top > 500 && Math.Abs(r.Left-main.Left) <= 8 && Math.Abs(r.Top-main.Top) <= 8 && Math.Abs(r.Right-main.Right) <= 8 && Math.Abs(r.Bottom-main.Bottom) <= 8)
                { ShowWindowAsync(w, 0); repaired++; break; }
                return true;
            }, IntPtr.Zero);
            return repaired;
        }
        public static object[] Audit()
        {
            var rows = new List<object>();
            EnumWindows(delegate(IntPtr w, IntPtr v)
            {
                if (!IsCodexHost(w)) return true;
                Rect r; GetWindowRect(w, out r); long ex = GetWindowLongPtr(w, -20).ToInt64();
                IntPtr hit = GetAncestor(WindowFromPoint(new Point { X = (r.Left+r.Right)/2, Y = (r.Top+r.Bottom)/2 }), 2);
                rows.Add(new { handle = w.ToInt64(), visible = IsWindowVisible(w), primary = IsCodex(w), toolWindow = (ex & 0x80) != 0, topmost = (ex & 8) != 0, bounds = r, centerHitRoot = hit.ToInt64(), centerRoutesToThisWindow = hit == w }); return true;
            }, IntPtr.Zero);
            return rows.ToArray();
        }
        public void PlaceSplash(Window window)
        {
            var r = monitor.Bounds;
            SetWindowPos(new WindowInteropHelper(window).Handle, new IntPtr(-1), r.Left, r.Top, r.Right-r.Left, r.Bottom-r.Top, 0x40);
        }
        public void BeginIntro()
        {
            Program.RepairedOverlays = RepairLegacyOverlays();
            // 仅在正式启动时暂时隐藏已有窗口，预览永远不操作 Codex。
            EnumWindows(delegate(IntPtr w, IntPtr value) { if (IsWindowVisible(w) && IsCodex(w)) Hide(w); return true; }, IntPtr.Zero);
            hook = SetWinEventHook(0x8000, 0x8002, IntPtr.Zero, callback, 0, 0, 0);
        }
        private void Hide(IntPtr window)
        {
            if (!hidden.ContainsKey(window)) { Rect original; GetWindowRect(window, out original); hidden[window] = original; }
            ShowWindowAsync(window, 0);
        }
        private void OnWindowEvent(IntPtr h, uint kind, IntPtr window, int obj, int child, uint thread, uint time)
        {
            if (obj != 0 || child != 0 || window == IntPtr.Zero || !IsCodex(window)) return;
            if (!handedOff) Hide(window);
        }
        public bool TryEnter()
        {
            var windows = new List<IntPtr>();
            EnumWindows(delegate(IntPtr w, IntPtr v) { if (IsCodex(w)) windows.Add(w); return true; }, IntPtr.Zero);
            if (windows.Count == 0) return false;
            if (!handedOff)
            {
                handedOff = true;
                if (hook != IntPtr.Zero) { UnhookWinEvent(hook); hook = IntPtr.Zero; }
                var r = monitor.Work;
                foreach (IntPtr window in windows)
                {
                    ShowWindowAsync(window, 0);
                    SetWindowPos(window, IntPtr.Zero, r.Left, r.Top, r.Right-r.Left, r.Bottom-r.Top, 0x4014);
                    ShowWindowAsync(window, 3);
                }
            }
            SameMonitor = true;
            foreach (IntPtr window in windows) if (!IsWindowVisible(window) || Info(MonitorFromWindow(window, 2)).Device != monitor.Device) SameMonitor = false;
            if (SameMonitor) SetForegroundWindow(windows[0]);
            return SameMonitor;
        }
        public void Dispose()
        {
            if (hook != IntPtr.Zero) { UnhookWinEvent(hook); hook = IntPtr.Zero; }
            if (!handedOff) foreach (var entry in hidden)
            {
                if (!IsWindow(entry.Key)) continue;
                var r = entry.Value;
                SetWindowPos(entry.Key, IntPtr.Zero, r.Left, r.Top, r.Right-r.Left, r.Bottom-r.Top, 0x4014);
                ShowWindowAsync(entry.Key, 5);
            }
        }
    }
}
