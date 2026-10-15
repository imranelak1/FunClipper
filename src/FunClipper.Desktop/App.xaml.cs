using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using System.Diagnostics;
using FunClipper.Core;

namespace FunClipper.Desktop;

public partial class App : Application
{
    private Mutex? _instance;
    private EventWaitHandle? _activation;
    private RegisteredWaitHandle? _activationWait;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, _) =>
        {
            if (sender is Window native && native.WindowStyle != WindowStyle.None)
            {
                int dark = 1;
                try { DwmSetWindowAttribute(new WindowInteropHelper(native).Handle, 20, ref dark, sizeof(int)); } catch (DllNotFoundException) { }
            }
        }));
        try
        {
            bool smoke = e.Args.Contains("--smoke");
            using var gameFixture = smoke && e.Args.Contains("--game-smoke") ? new GameSmokeFixture() : null;
            if (!smoke)
            {
                _activation = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\FunClipper.Activate." + Environment.UserName);
                _instance = new Mutex(true, "Local\\FunClipper.Desktop." + Environment.UserName, out bool first);
                if (!first) { _activation.Set(); Shutdown(); return; }
                _activationWait = ThreadPool.RegisterWaitForSingleObject(_activation, (_, _) => Dispatcher.BeginInvoke(() =>
                {
                    if (MainWindow is Window existing) { existing.Show(); existing.WindowState = WindowState.Normal; existing.Activate(); }
                }), null, Timeout.Infinite, false);
            }
            string directory = smoke
                ? Path.GetFullPath(Path.Combine("artifacts", "run", "desktop"))
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FunClipper");
            var window = new MainWindow(directory, smoke);
            MainWindow = window;
            if (smoke)
            {
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Left = -10000;
                window.Top = -10000;
            }
            window.Show();
            if (smoke)
            {
                if (gameFixture is not null)
                {
                    await window.Games.RegisterGameAsync(gameFixture.ExecutablePath, "FunClipper test game");
                    window.Games.Start();
                    var detectionWait = Stopwatch.StartNew();
                    while (!window.Games.Current.RunningGames.Any(game => game.ProcessId == gameFixture.ProcessId))
                    {
                        if (detectionWait.Elapsed > TimeSpan.FromSeconds(15)) throw new TimeoutException("The owned game fixture was not detected.");
                        await Task.Delay(100);
                    }
                    window.ApplyGames(window.Games.Current);
                }
                var demo = await MediaTools.CreateDemoAsync(window.Recorder.ClipsDirectory);
                window.AddClip(demo);
                await Task.Delay(900);
                string review = Path.GetFullPath(Path.Combine(".impeccable", "review"));
                Directory.CreateDirectory(review);
                RenderWindow(window, Path.Combine(review, "desktop.png"));
                window.Width = window.MinWidth; window.Height = window.MinHeight;
                window.UpdateLayout();
                RenderWindow(window, Path.Combine(review, "desktop-minimum.png"));
                window.Width = 850; window.Height = 660;
                var editor = new EditorWindow(demo, window.Recorder.ClipsDirectory, window.ShareClient);
                editor.Left = -10000;
                editor.Top = -10000;
                editor.Show();
                await Task.WhenAny(editor.PreviewReady, Task.Delay(10000));
                await Task.Delay(150);
                RenderWindow(editor, Path.Combine(review, "editor.png"));
                editor.Close();
                var connection = new ConnectionWindow(window.ShareClient) { Left = -10000, Top = -10000, WindowStartupLocation = WindowStartupLocation.Manual };
                connection.Show();
                await Task.Delay(100);
                RenderWindow(connection, Path.Combine(review, "connection.png"));
                var fields = ((StackPanel)connection.Content).Children.OfType<Control>().Where(control => control is TextBox or PasswordBox).ToArray();
                string[] names = fields.Select(control => FrameworkElementAutomationPeer.CreatePeerForElement(control)?.GetName() ?? "").ToArray();
                if (!names.SequenceEqual(new[] { "Server URL", "Access key" })) throw new InvalidOperationException("Native connection accessible names failed.");
                var statusControl = (TextBox)window.FindName("ActivityStatus");
                await File.WriteAllTextAsync(Path.Combine(review, "native-accessibility.json"), JsonSerializer.Serialize(new { connectionFieldNames = names, activityStatusReadOnly = statusControl.IsReadOnly, activityStatusHasTextPattern = FrameworkElementAutomationPeer.CreatePeerForElement(statusControl)?.GetPattern(PatternInterface.Text) != null }));
                connection.Close();
                var games = new GamesWindow(window.Games) { Left = -10000, Top = -10000, WindowStartupLocation = WindowStartupLocation.Manual };
                games.Show();
                await Task.Delay(120);
                RenderWindow(games, Path.Combine(review, "games.png"));
                games.Width = games.MinWidth; games.Height = games.MinHeight;
                RenderWindow(games, Path.Combine(review, "games-minimum.png"));
                games.Close();
                window.Overlay.Left = -10000; window.Overlay.Top = -10000;
                window.Overlay.Show();
                if (window.LastVitals is { } overlayVitals) window.Overlay.UpdateVitals(overlayVitals);
                window.Overlay.UpdateRecording(false, false, false, false, 0, 30, TimeSpan.Zero);
                await Task.Delay(120);
                RenderWindow(window.Overlay, Path.Combine(review, "overlay.png"));
                ((Button)window.Overlay.FindName("CollapseButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(200);
                RenderWindow(window.Overlay, Path.Combine(review, "overlay-collapsed.png"));
                ((Button)window.Overlay.FindName("CollapseButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(200);
                await File.WriteAllTextAsync(Path.Combine(review, "overlay-behavior.json"), JsonSerializer.Serialize(new { topmost = window.Overlay.Topmost, takesFocusOnShow = window.Overlay.ShowActivated, excludedFromCapture = window.Overlay.IsExcludedFromCapture, transparentWindow = window.Overlay.AllowsTransparency, interactiveControls = new[] { "Start capture", "Save clip", "Open library", "Collapse", "Drag to corner" } }));
                window.Overlay.Hide();
                var samples = new List<VitalsSnapshot>();
                for (int i = 0; i < 6; i++) { await Task.Delay(1000); if (window.LastVitals is { } sample) samples.Add(sample); }
                await File.WriteAllTextAsync("artifacts/prototype-vitals.json", JsonSerializer.Serialize(new { context = "Offscreen UI open, recorder stopped, synthetic clip loaded; not a gameplay benchmark", samples }, new JsonSerializerOptions { WriteIndented = true }));
                if (gameFixture is not null) await window.Games.RemoveRegisteredGameAsync(gameFixture.ExecutablePath);
                await window.ExitAsync();
            }
        }
        catch (Exception ex)
        {
            Directory.CreateDirectory("artifacts");
            File.WriteAllText("artifacts/desktop-error.txt", ex.ToString());
            if (!e.Args.Contains("--smoke")) MessageBox.Show(ex.Message, "FunClipper could not start", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private sealed class GameSmokeFixture : IDisposable
    {
        private readonly Process _process;
        public string ExecutablePath { get; } = Path.GetFullPath(Path.Combine("tests", "FunClipper.GameChecks", "bin", "Debug", "net10.0-windows", "FunClipper.GameChecks.exe"));
        public int ProcessId => _process.Id;
        public GameSmokeFixture()
        {
            if (!File.Exists(ExecutablePath)) throw new FileNotFoundException("Build FunClipper.GameChecks before --game-smoke.", ExecutablePath);
            var start = new ProcessStartInfo(ExecutablePath) { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add("--fixture-child");
            _process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the owned game fixture.");
        }
        public void Dispose()
        {
            try { if (!_process.HasExited) _process.Kill(); }
            finally { _process.Dispose(); }
        }
    }

    private static void RenderWindow(Window window, string path)
    {
        window.UpdateLayout();
        var content = (FrameworkElement)window.Content;
        double width = content.ActualWidth + content.Margin.Left + content.Margin.Right;
        double height = content.ActualHeight + content.Margin.Top + content.Margin.Bottom;
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            context.DrawRectangle(window.Background, null, new Rect(0, 0, width, height));
            context.DrawRectangle(new VisualBrush(content), null, new Rect(content.Margin.Left, content.Margin.Top, content.ActualWidth, content.ActualHeight));
        }
        var bitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _activationWait?.Unregister(null);
        _activation?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
