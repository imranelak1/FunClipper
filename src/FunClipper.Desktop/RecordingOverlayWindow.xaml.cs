using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FunClipper.Desktop;

public partial class RecordingOverlayWindow : Window
{
    private readonly string _settingsPath;
    private bool _closedForExit, _collapsed, _smoke, _positionLoaded;
    private double _alpha = 0.85;
    private string _corner = "BottomRight";
    private string? _feedback;
    private DateTimeOffset _feedbackExpiry;
    private bool _feedbackError;
    public bool WantsVitals => IsVisible && !_collapsed;
    public bool IsExcludedFromCapture { get; private set; }
    public event Func<Task>? ToggleRecording;
    public event Func<Task>? SaveClip;
    public event Action? OpenLibrary;

    public RecordingOverlayWindow(string dataDirectory, bool smoke = false)
    {
        _smoke = smoke;
        _settingsPath = Path.Combine(dataDirectory, "overlay.json");
        InitializeComponent();
        try
        {
            if (File.Exists(_settingsPath))
            {
                var settings = JsonSerializer.Deserialize<OverlaySettings>(File.ReadAllText(_settingsPath));
                if (settings != null) { _alpha = Math.Clamp(settings.Opacity, 0.85, 0.97); _corner = settings.Corner; _collapsed = settings.Collapsed; }
            }
        }
        catch { }
        ApplyOpacity();
        BuildMenu();
        SourceInitialized += (_, _) =>
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            long style = GetWindowLongPtr(hwnd, -20).ToInt64();
            SetWindowLongPtr(hwnd, -20, new IntPtr(style | 0x08000000L | 0x00000080L));
            IsExcludedFromCapture = SetWindowDisplayAffinity(hwnd, 0x11);
        };
        Loaded += (_, _) => { ApplyCollapse(false); if (!_smoke) DockToCorner(); _positionLoaded = true; };
    }

    public void UpdateVitals(VitalsSnapshot value)
    {
        CpuValue.Text = value.CpuPercent is double cpu ? $"{cpu:F0}%" : "n/a";
        GpuValue.Text = value.GpuPercent is double gpu ? $"{gpu:F0}%" : "n/a";
        RamValue.Text = $"{value.UsedRamGb:F1}/{value.TotalRamGb:F0} GB";
        MemoryLabel.Text = value.DedicatedGpuGb is double vram ? $"GPU memory {vram:F1} GB" : "";
        ToolTip = $"FunClipper: {value.AppRamMb:F0} MB, {value.AppCpuPercent:F1}% CPU. GPU reads the busiest engine. Drag the header; right-click to choose a corner or opacity.";
    }

    public void UpdateRecording(bool recording, bool preparing, bool ready, bool saving, double buffered, int capacity, TimeSpan elapsed, string? message = null)
    {
        bool feedback = _feedback != null && DateTimeOffset.UtcNow < _feedbackExpiry;
        StateLabel.Text = feedback && _feedbackError ? "Capture needs attention" : saving ? "Saving clip…" : preparing ? "Preparing capture…" : recording ? "Recording" : "Not recording";
        StateDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(recording || (feedback && _feedbackError) ? "#FB7185" : preparing ? "#FBBF24" : "#8992A4"));
        ElapsedLabel.Text = elapsed.TotalHours >= 1 ? elapsed.ToString(@"h\:mm\:ss") : elapsed.ToString(@"mm\:ss");
        RecordButton.Content = recording ? "Stop capture" : "Start capture";
        RecordButton.IsEnabled = !preparing;
        ClipButton.IsEnabled = ready && !saving;
        BufferLabel.Text = saving ? "Writing your clip…" : recording ? $"Buffered {Math.Min(capacity, buffered):F0}s / {capacity}s" : "Replay buffer off";
        if (!string.IsNullOrWhiteSpace(message)) BufferLabel.Text = message;
        if (feedback) BufferLabel.Text = _feedback;
    }

    public void ShowSaved(double seconds) { _feedback = $"Saved {seconds:F1}s · open library to edit"; _feedbackError = false; _feedbackExpiry = DateTimeOffset.UtcNow.AddSeconds(5); BufferLabel.Text = _feedback; }
    public void ShowError(string text) { _feedback = "Open library for details"; _feedbackError = true; _feedbackExpiry = DateTimeOffset.UtcNow.AddSeconds(8); StateLabel.Text = "Capture needs attention"; StateDot.Fill = new SolidColorBrush(Color.FromRgb(251,113,133)); BufferLabel.Text = _feedback; ToolTip = text; }
    public void UpdateHotkey(bool available)
    {
        ClipButton.Content = available ? "Save clip · F8" : "Save clip";
        ClipButton.ToolTip = available ? "Save recent footage without leaving your game" : "F8 is used by another app. Click Save clip or use the tray menu.";
    }

    public void UpdateGame(string? name, bool foreground = false)
    {
        GameLabel.Text = string.IsNullOrWhiteSpace(name) ? "No game detected" : $"{name}{(foreground ? "" : " · background")}";
        GameLabel.ToolTip = string.IsNullOrWhiteSpace(name) ? "Open a game, or add its executable in Games." : name + " · desktop capture on screen 1";
    }

    private async void Record_Click(object sender, RoutedEventArgs e) { if (ToggleRecording != null) await ToggleRecording(); }
    private async void Save_Click(object sender, RoutedEventArgs e) { if (SaveClip != null) await SaveClip(); }
    private void Open_Click(object sender, RoutedEventArgs e) => OpenLibrary?.Invoke();
    private void Collapse_Click(object sender, RoutedEventArgs e) { _collapsed = !_collapsed; ApplyCollapse(true); SaveSettings(); }

    private void ApplyCollapse(bool animate)
    {
        double targetHeight = _collapsed ? 54 : 200;
        double currentHeight = ActualHeight;
        BeginAnimation(HeightProperty, null);
        Height = targetHeight;
        ExpandedContent.Visibility = _collapsed ? Visibility.Collapsed : Visibility.Visible;
        CollapseGlyph.Data = Geometry.Parse(_collapsed ? "M 0 0 L 4 4 L 8 0" : "M 0 4 L 4 0 L 8 4");
        CollapseButton.ToolTip = _collapsed ? "Expand widget" : "Collapse widget";
        if (animate && SystemParameters.ClientAreaAnimation)
        {
            var animation = new DoubleAnimation(currentHeight, targetHeight, TimeSpan.FromMilliseconds(150)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop };
            animation.Completed += (_, _) => { if (_positionLoaded && !_smoke) DockToCorner(); };
            BeginAnimation(HeightProperty, animation);
        }
        else { BeginAnimation(HeightProperty, null); Height = targetHeight; }
    }

    private void Drag_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        try { DragMove(); if (!_smoke) SnapNearestCorner(); } catch (InvalidOperationException) { }
        e.Handled = true;
    }

    private void SnapNearestCorner()
    {
        var work = WorkingArea();
        _corner = (Top + ActualHeight / 2 < work.Top + work.Height / 2 ? "Top" : "Bottom") + (Left + ActualWidth / 2 < work.Left + work.Width / 2 ? "Left" : "Right");
        DockToCorner(); SaveSettings();
    }

    private Rect WorkingArea()
    {
        var screen = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle);
        var source = PresentationSource.FromVisual(this);
        var transform = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var point = transform.Transform(new Point(screen.WorkingArea.Left, screen.WorkingArea.Top));
        var size = transform.Transform(new Vector(screen.WorkingArea.Width, screen.WorkingArea.Height));
        return new Rect(point.X, point.Y, size.X, size.Y);
    }

    public void DockToCorner()
    {
        var work = WorkingArea();
        Left = _corner.EndsWith("Left", StringComparison.Ordinal) ? work.Left + 20 : work.Right - Width - 20;
        Top = _corner.StartsWith("Top", StringComparison.Ordinal) ? work.Top + 20 : work.Bottom - Height - 20;
    }

    private void BuildMenu()
    {
        var menu = new System.Windows.Controls.ContextMenu();
        foreach (string corner in new[] { "TopRight", "BottomRight", "TopLeft", "BottomLeft" })
        {
            var item = new System.Windows.Controls.MenuItem { Header = corner.Replace("Right", " right").Replace("Left", " left") };
            item.Click += (_, _) => { _corner = corner; DockToCorner(); SaveSettings(); }; menu.Items.Add(item);
        }
        menu.Items.Add(new System.Windows.Controls.Separator());
        foreach (double alpha in new[] { 0.85, 0.92, 0.97 })
        {
            var item = new System.Windows.Controls.MenuItem { Header = $"Opacity {alpha:P0}" };
            item.Click += (_, _) => { _alpha = alpha; ApplyOpacity(); SaveSettings(); }; menu.Items.Add(item);
        }
        var hide = new System.Windows.Controls.MenuItem { Header = "Hide widget" }; hide.Click += (_, _) => Hide(); menu.Items.Add(hide);
        ContextMenu = menu;
    }

    private void ApplyOpacity() => Shell.Background = new SolidColorBrush(Color.FromArgb((byte)(_alpha * 255),25,28,35));
    private void SaveSettings() { try { Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!); File.WriteAllText(_settingsPath, JsonSerializer.Serialize(new OverlaySettings(_corner,_alpha,_collapsed))); } catch (IOException) { } }
    private void Overlay_Closing(object? sender, CancelEventArgs e) { if (!_closedForExit) { e.Cancel = true; Hide(); } }
    public void CloseForExit() { _closedForExit = true; Close(); }
    private sealed record OverlaySettings(string Corner, double Opacity, bool Collapsed);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
}
