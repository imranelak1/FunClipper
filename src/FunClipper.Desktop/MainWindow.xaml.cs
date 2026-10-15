using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using FunClipper.Core;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace FunClipper.Desktop;

public partial class MainWindow : Window
{
    public RecorderService Recorder { get; }
    public ShareClient ShareClient { get; }
    public RecordingOverlayWindow Overlay { get; }
    private readonly ObservableCollection<ClipRow> _clips = new();
    private readonly VitalsSampler _vitals = new();
    private readonly DispatcherTimer _timer;
    private readonly Forms.NotifyIcon? _tray;
    private readonly bool _smoke;
    private bool _exitRequested, _saving, _sharing, _hotkeyRegistered, _captureTransition;
    private readonly Stopwatch _recordingClock = new();
    private IntPtr _handle;
    private ClipInfo? _latest;
    private HwndSource? _source;
    public VitalsSnapshot? LastVitals { get; private set; }

    public MainWindow(string dataDirectory, bool smoke = false)
    {
        InitializeComponent();
        _smoke = smoke;
        Recorder = new RecorderService(dataDirectory);
        ShareClient = new ShareClient(dataDirectory);
        Overlay = new RecordingOverlayWindow(dataDirectory, smoke);
        Overlay.ToggleRecording += ToggleCaptureAsync;
        Overlay.SaveClip += SaveClipAsync;
        Overlay.OpenLibrary += ShowMain;
        Recorder.StatusChanged += OnRecorderStatus;
        ClipList.ItemsSource = _clips;
        foreach (var clip in Recorder.GetClips().Take(50).Reverse()) AddClip(clip, false);
        _latest = _clips.FirstOrDefault()?.Clip;
        if (!smoke)
        {
            _tray = new Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Application, Text = "FunClipper · stopped", Visible = true };
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Open FunClipper", null, (_, _) => ShowMain());
            menu.Items.Add("Show recording widget", null, (_, _) => { Overlay.Show(); Overlay.DockToCorner(); });
            menu.Items.Add("Save recent clip", null, async (_, _) => await SaveClipAsync());
            menu.Items.Add("Edit latest clip", null, (_, _) => { if (_latest != null) OpenEditor(_latest); else ShowMain(); });
            menu.Items.Add("Share latest clip", null, async (_, _) => { if (_latest != null) await ShareClipAsync(_latest); else ShowMain(); });
            menu.Items.Add("Stop capture", null, async (_, _) => await StopCaptureAsync());
            menu.Items.Add("Quit", null, async (_, _) => await ExitAsync());
            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += (_, _) => ShowMain();
            _tray.BalloonTipClicked += (_, _) => { if (_latest != null) OpenEditor(_latest); };
        }
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => UpdateVitals();
        _timer.Start();
        SourceInitialized += (_, _) =>
        {
            _handle = new WindowInteropHelper(this).Handle;
            _source = HwndSource.FromHwnd(_handle);
            _source?.AddHook(WindowHook);
        };
        Loaded += (_, _) => { if (!smoke) { Overlay.Show(); Overlay.DockToCorner(); } };
    }

    public void AddClip(ClipInfo clip, bool latest = true)
    {
        if (_clips.Any(row => row.Clip.Id == clip.Id)) return;
        _clips.Insert(0, new ClipRow(clip));
        while (_clips.Count > 50) _clips.RemoveAt(_clips.Count - 1);
        _latest = latest || _latest == null ? clip : _latest;
        EmptyState.Visibility = _clips.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnRecorderStatus(string status) => Dispatcher.BeginInvoke(() =>
    {
        CaptureStatus.Text = status;
        bool active = Recorder.IsRecording;
        CaptureButton.Content = active ? "Stop capture" : "Start capture";
        ProfileChoice.IsEnabled = !active;
        SaveButton.IsEnabled = Recorder.IsReplayReady && !_saving && !_captureTransition;
        if (!active) UnregisterCaptureHotkey();
        if (_tray != null) _tray.Text = active ? "FunClipper · recording" : "FunClipper · stopped";
        SyncOverlay();
    });

    private async void Capture_Click(object sender, RoutedEventArgs e)
        => await ToggleCaptureAsync();

    public async Task ToggleCaptureAsync()
    {
        if (_captureTransition) return;
        _captureTransition = true;
        CaptureButton.IsEnabled = false;
        SyncOverlay();
        try
        {
            if (Recorder.IsRecording) await StopCaptureAsync();
            else
            {
                _recordingClock.Reset();
                CaptureStatus.Text = "Checking encoder and starting capture…";
                await Recorder.StartAsync((CaptureProfile)ProfileChoice.SelectedIndex);
                _recordingClock.Restart();
                if (!_smoke)
                {
                    _hotkeyRegistered = RegisterHotKey(_handle, 1, 0x4000, 0x77);
                    ActivityStatus.Text = _hotkeyRegistered
                        ? "F8 saves recent footage. Use the tray to save, edit or share without browsing the library."
                        : "F8 is used by another app. Save with the button or tray menu.";
                }
            }
        }
        catch (Exception ex) { ActivityStatus.Text = "Capture could not start. " + ex.Message; Overlay.ShowError(ex.Message); }
        finally
        {
            _captureTransition = false;
            CaptureButton.IsEnabled = true;
            CaptureButton.Content = Recorder.IsRecording ? "Stop capture" : "Start capture";
            SaveButton.IsEnabled = Recorder.IsReplayReady;
            ProfileChoice.IsEnabled = !Recorder.IsRecording;
            SyncOverlay();
        }
    }

    private async Task StopCaptureAsync()
    {
        UnregisterCaptureHotkey();
        await Recorder.StopAsync();
        _recordingClock.Stop();
        CaptureButton.Content = "Start capture"; SaveButton.IsEnabled = false; ProfileChoice.IsEnabled = true;
        SyncOverlay();
    }

    private void UnregisterCaptureHotkey()
    {
        if (!_hotkeyRegistered) return;
        UnregisterHotKey(_handle, 1); _hotkeyRegistered = false;
    }

    private IntPtr WindowHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0312 && wParam.ToInt32() == 1)
        {
            handled = true;
            _ = SaveClipAsync();
        }
        return IntPtr.Zero;
    }

    private async void Save_Click(object sender, RoutedEventArgs e) => await SaveClipAsync();

    private async Task SaveClipAsync()
    {
        if (_saving || !Recorder.IsReplayReady) return;
        _saving = true; SaveButton.IsEnabled = false;
        SyncOverlay();
        try
        {
            ActivityStatus.Text = "Saving recent footage…";
            var clip = await Recorder.SaveReplayAsync();
            var history = _vitals.History().Where(sample => sample.At >= clip.CreatedAt.AddSeconds(-clip.DurationSeconds)).ToArray();
            await File.WriteAllTextAsync(clip.FilePath + ".vitals.json", JsonSerializer.Serialize(history));
            AddClip(clip);
            ActivityStatus.Text = $"Saved {clip.DurationSeconds:F1} seconds. Edit or share directly from this row or the tray.";
            Overlay.ShowSaved(clip.DurationSeconds);
            if (_tray != null)
            {
                _tray.BalloonTipTitle = "Clip saved";
                _tray.BalloonTipText = "Click to trim. Right-click the tray icon to share the latest clip.";
                _tray.ShowBalloonTip(3000);
            }
        }
        catch (Exception ex) { ActivityStatus.Text = "Clip was not saved. " + ex.Message; Overlay.ShowError(ex.Message); }
        finally { _saving = false; SaveButton.IsEnabled = Recorder.IsReplayReady; SyncOverlay(); }
    }

    private void UpdateVitals()
    {
        if (!IsVisible && !Overlay.WantsVitals && !Recorder.IsRecording) return;
        try
        {
            var sample = _vitals.Sample(Recorder.CaptureProcessId);
            LastVitals = sample;
            if (Overlay.WantsVitals) Overlay.UpdateVitals(sample);
            SyncOverlay();
            if (!IsVisible) return;
            string gpu = sample.GpuPercent is double g ? $"{g:F0}%" : "unavailable";
            string cpu = sample.CpuPercent is double c ? $"{c:F0}%" : "unavailable";
            VitalsLine.Text = $"CPU {cpu}   GPU {gpu}   RAM {sample.UsedRamGb:F1} / {sample.TotalRamGb:F0} GB";
            string recorder = sample.RecorderRamMb is double r ? $" + encoder {r:F0} MB / {sample.RecorderCpuPercent:F1}% CPU" : "";
            CostLine.Text = $"FunClipper {sample.AppRamMb:F0} MB / {sample.AppCpuPercent:F1}% CPU{recorder}";
        }
        catch { CostLine.Text = "Some vitals are unavailable on this PC."; }
    }

    private void SyncOverlay()
    {
        bool recording = Recorder.IsRecording;
        if (!recording && !_captureTransition) _recordingClock.Stop();
        SaveButton.Content = !recording || _hotkeyRegistered || _smoke ? "Save clip · F8" : "Save clip";
        int capacity = ProfileChoice.SelectedIndex == 0 ? 15 : 30;
        Overlay.UpdateRecording(recording, _captureTransition, Recorder.IsReplayReady, _saving, Recorder.BufferedSeconds, capacity, _recordingClock.Elapsed);
        Overlay.UpdateHotkey(!recording || _hotkeyRegistered || _smoke);
    }

    private void Widget_Click(object sender, RoutedEventArgs e) { Overlay.Show(); Overlay.DockToCorner(); }

    private void EditClip_Click(object sender, RoutedEventArgs e) { if ((sender as FrameworkElement)?.Tag is ClipRow row) OpenEditor(row.Clip); }
    private async void ShareClip_Click(object sender, RoutedEventArgs e) { if ((sender as FrameworkElement)?.Tag is ClipRow row) await ShareClipAsync(row.Clip); }
    private void Clip_DoubleClick(object sender, MouseButtonEventArgs e) { if (ClipList.SelectedItem is ClipRow row) OpenEditor(row.Clip); }

    private void OpenEditor(ClipInfo clip)
    {
        var editor = new EditorWindow(clip, Recorder.ClipsDirectory, ShareClient);
        editor.ClipExported += clip => AddClip(clip);
        editor.Show(); editor.Activate();
    }

    private async Task ShareClipAsync(ClipInfo clip)
    {
        if (_sharing) return;
        _sharing = true;
        ActivityStatus.Text = "Uploading clip…";
        try
        {
            string url = await ShareClient.UploadAsync(clip.FilePath);
            try { Clipboard.SetText(url); }
            catch (ExternalException)
            {
                ShowMain();
                ActivityStatus.Text = (ShareClient.IsLocal ? "Local preview uploaded. " : "Uploaded. ") + "Clipboard is busy; copy this link: " + url;
                ActivityStatus.Focus();
                ActivityStatus.Select(ActivityStatus.Text.Length - url.Length, url.Length);
                return;
            }
            string scope = ShareClient.IsLocal ? "Local preview link copied · works on this PC. Configure hosting for friends." : "Sharing link copied.";
            ActivityStatus.Text = scope;
            if (_tray != null) { _tray.BalloonTipTitle = "Link copied"; _tray.BalloonTipText = scope; _tray.ShowBalloonTip(3000); }
        }
        catch (Exception ex) { ShowMain(); ActivityStatus.Text = ex is HttpRequestException ? "Could not reach the web companion. Start scripts/Start-Web.ps1 or check Connection." : ex.Message; }
        finally { _sharing = false; }
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Filter = "MP4 video (*.mp4)|*.mp4", Title = "Import a clip" };
        if (picker.ShowDialog(this) != true) return;
        try
        {
            double duration = await MediaTools.ProbeDurationAsync(picker.FileName);
            if (!double.IsFinite(duration) || duration <= 0) throw new InvalidOperationException("This file has no playable video duration.");
            string id = Guid.NewGuid().ToString("N");
            string path = Path.Combine(Recorder.ClipsDirectory, "import-" + id + ".mp4");
            Directory.CreateDirectory(Recorder.ClipsDirectory);
            File.Copy(picker.FileName, path);
            var clip = new ClipInfo(id, path, DateTimeOffset.UtcNow, duration, CaptureProfile.LowEnd);
            File.WriteAllText(Path.ChangeExtension(path, ".json"), JsonSerializer.Serialize(clip));
            AddClip(clip); OpenEditor(clip);
        }
        catch (Exception ex) { ActivityStatus.Text = ex.Message; }
    }

    private void Library_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(ShareClient.LibraryUrl()) { UseShellExecute = true }); }
        catch (Exception ex) { ActivityStatus.Text = ex.Message; }
    }

    private void Connection_Click(object sender, RoutedEventArgs e)
    {
        var window = new ConnectionWindow(ShareClient) { Owner = this };
        window.ShowDialog();
    }

    private void ShowMain() { Show(); WindowState = WindowState.Normal; Activate(); }
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_exitRequested) return;
        e.Cancel = true; Hide();
        if (_tray != null) { _tray.BalloonTipTitle = "FunClipper is in the tray"; _tray.BalloonTipText = Recorder.IsRecording ? "Recording continues. Right-click the tray icon to stop or quit." : "Right-click the tray icon to open or quit."; _tray.ShowBalloonTip(2000); }
    }

    public async Task ExitAsync()
    {
        if (_exitRequested) return;
        _exitRequested = true;
        _timer.Stop(); UnregisterCaptureHotkey();
        _source?.RemoveHook(WindowHook);
        Recorder.StatusChanged -= OnRecorderStatus;
        await Recorder.DisposeAsync();
        Overlay.CloseForExit();
        _vitals.Dispose(); ShareClient.Dispose();
        if (_tray != null) { _tray.Visible = false; _tray.ContextMenuStrip?.Dispose(); _tray.Dispose(); }
        Application.Current.Shutdown();
    }

    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    public sealed record ClipRow(ClipInfo Clip)
    {
        public string DisplayName => $"Clip · {Clip.CreatedAt.LocalDateTime:HH:mm:ss}";
        public string Detail => $"{Clip.CreatedAt.LocalDateTime:g} · {Clip.DurationSeconds:F1} seconds";
    }
}
