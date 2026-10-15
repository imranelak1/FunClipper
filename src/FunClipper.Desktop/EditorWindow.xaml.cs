using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FunClipper.Core;

namespace FunClipper.Desktop;

public partial class EditorWindow : Window
{
    private readonly ClipInfo _clip;
    private readonly string _outputDirectory;
    private readonly ShareClient _share;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherTimer _playback;
    private bool _ready, _playing, _busy;
    private ClipInfo? _exported;
    private string? _posterPath;
    internal Task PreviewReady { get; }
    public event Action<ClipInfo>? ClipExported;

    public EditorWindow(ClipInfo clip, string outputDirectory, ShareClient share)
    {
        _clip = clip; _outputDirectory = outputDirectory; _share = share;
        InitializeComponent();
        InSlider.Maximum = clip.DurationSeconds; OutSlider.Maximum = clip.DurationSeconds; OutSlider.Value = clip.DurationSeconds;
        Player.Source = new Uri(Path.GetFullPath(clip.FilePath));
        DurationLabel.Text = $"{clip.DurationSeconds:F1} seconds";
        _ready = true; UpdateTrimLabels();
        _playback = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _playback.Tick += (_, _) => { if (_playing && Player.Position.TotalSeconds >= OutSlider.Value) StopPlayback(); };
        LoadVitals();
        PreviewReady = LoadPosterAsync();
    }

    private async Task LoadPosterAsync()
    {
        string poster = Path.Combine(Path.GetTempPath(), "funclipper-" + Guid.NewGuid().ToString("N") + ".jpg");
        _posterPath = poster;
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(15));
            var start = new ProcessStartInfo(MediaTools.FindFfmpeg()) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardError = true, RedirectStandardOutput = true };
            foreach (var argument in new[] { "-nostdin", "-hide_banner", "-loglevel", "error", "-ss", Math.Min(1, _clip.DurationSeconds / 2).ToString(CultureInfo.InvariantCulture), "-i", _clip.FilePath, "-frames:v", "1", "-vf", "scale=640:-2", "-threads", "1", "-y", poster }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not prepare preview.");
            process.StandardInput.Close();
            using var registration = deadline.Token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch { } });
            var errors = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync(deadline.Token); await errors; await output;
            if (process.ExitCode != 0 || !File.Exists(poster) || _lifetime.IsCancellationRequested) return;
            var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.UriSource = new Uri(poster); bitmap.EndInit(); bitmap.Freeze();
            Poster.Source = bitmap;
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { if (!_lifetime.IsCancellationRequested) EditorStatus.Text = "Preview image unavailable. You can still play or export this clip."; }
        catch (OperationCanceledException) { }
        finally { try { if (File.Exists(poster)) File.Delete(poster); } catch { } }
    }

    private void LoadVitals()
    {
        VitalsLabel.Text = "No recorded performance history for this clip.";
        try
        {
            string path = _clip.FilePath + ".vitals.json";
            if (!File.Exists(path)) return;
            var history = JsonSerializer.Deserialize<VitalsSnapshot[]>(File.ReadAllText(path));
            if (history == null || history.Length == 0) return;
            var cpu = history.Where(sample => sample.CpuPercent.HasValue).Select(sample => sample.CpuPercent!.Value).ToArray();
            var gpu = history.Where(sample => sample.GpuPercent.HasValue).Select(sample => sample.GpuPercent!.Value).ToArray();
            string cpuText = cpu.Length > 0 ? $"CPU peak {cpu.Max():F0}%" : "CPU unavailable";
            string gpuText = gpu.Length > 0 ? $"GPU peak {gpu.Max():F0}%" : "GPU unavailable";
            VitalsLabel.Text = $"During this clip: {cpuText} · {gpuText} · RAM peak {history.Max(sample => sample.UsedRamGb):F1} GB. Usage alone does not establish the cause of a stutter.";
        }
        catch { VitalsLabel.Text = "Performance history could not be read."; }
    }

    private void Trim_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready) return;
        double gap = Math.Min(0.1, _clip.DurationSeconds);
        if (sender == InSlider && InSlider.Value > OutSlider.Value - gap) InSlider.Value = Math.Max(0, OutSlider.Value - gap);
        if (sender == OutSlider && OutSlider.Value < InSlider.Value + gap) OutSlider.Value = Math.Min(_clip.DurationSeconds, InSlider.Value + gap);
        _exported = null; UpdateTrimLabels();
        if (_playing) StopPlayback();
    }

    private void UpdateTrimLabels()
    {
        InLabel.Text = $"{InSlider.Value:F1}s"; OutLabel.Text = $"{OutSlider.Value:F1}s";
        SelectionLabel.Text = $"Keep {OutSlider.Value - InSlider.Value:F1} seconds";
    }

    private void Mute_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        _exported = null; Player.IsMuted = MuteChoice.IsChecked == true;
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (_playing) { StopPlayback(); return; }
        Poster.Visibility = Visibility.Hidden; Player.Visibility = Visibility.Visible;
        Player.Position = TimeSpan.FromSeconds(InSlider.Value); Player.IsMuted = MuteChoice.IsChecked == true; Player.Play();
        _playing = true; _playback.Start(); PlayButton.Content = "Pause";
    }

    private void StopPlayback() { Player.Pause(); _playing = false; _playback.Stop(); PlayButton.Content = "Play selection"; }
    private void Player_Ended(object sender, RoutedEventArgs e) => StopPlayback();
    private void Player_Failed(object sender, ExceptionRoutedEventArgs e) { StopPlayback(); Poster.Visibility = Visibility.Visible; Player.Visibility = Visibility.Hidden; EditorStatus.Text = "Windows could not play this video. Export or open it with your media player."; }

    private async Task<ClipInfo> PrepareEditAsync()
    {
        if (_exported != null) return _exported;
        _exported = await MediaTools.TrimAsync(_clip, InSlider.Value, OutSlider.Value, MuteChoice.IsChecked == true, _outputDirectory, _lifetime.Token);
        ClipExported?.Invoke(_exported);
        return _exported;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy; SaveEditButton.IsEnabled = !busy; ShareButton.IsEnabled = !busy;
        InSlider.IsEnabled = !busy; OutSlider.IsEnabled = !busy; MuteChoice.IsEnabled = !busy;
    }

    private async void SaveEdit_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        SetBusy(true); EditorStatus.Text = "Saving edit…";
        try { var edit = await PrepareEditAsync(); EditorStatus.Text = $"Saved {edit.DurationSeconds:F1} seconds to your clip library."; }
        catch (OperationCanceledException) { }
        catch (Exception ex) { EditorStatus.Text = ex.Message; }
        finally { SetBusy(false); }
    }

    private async void Share_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        SetBusy(true); EditorStatus.Text = "Preparing clip…";
        try
        {
            bool unchanged = InSlider.Value < 0.01 && OutSlider.Value >= _clip.DurationSeconds - 0.01 && MuteChoice.IsChecked != true;
            var edit = unchanged ? _clip : await PrepareEditAsync();
            EditorStatus.Text = "Uploading clip…";
            string url = await _share.UploadAsync(edit.FilePath, _lifetime.Token);
            try { Clipboard.SetText(url); EditorStatus.Text = _share.IsLocal ? "Local preview link copied. It works on this PC; configure hosting to share with friends." : "Sharing link copied."; }
            catch (ExternalException) { EditorStatus.Text = "Uploaded. Clipboard is busy; copy this link: " + url; }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { EditorStatus.Text = ex is HttpRequestException ? "Could not reach the web companion. Start it or check Connection in the main window." : ex.Message; }
        finally { SetBusy(false); }
    }

    private void Editor_Closed(object? sender, EventArgs e)
    {
        _lifetime.Cancel(); _playback.Stop(); Player.Stop(); Player.Close();
        try { if (_posterPath != null && File.Exists(_posterPath)) File.Delete(_posterPath); } catch { }
    }
}
