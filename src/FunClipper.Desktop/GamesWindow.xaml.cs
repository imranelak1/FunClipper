using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using FunClipper.Desktop.GameDetection;
using Microsoft.Win32;

namespace FunClipper.Desktop;

public partial class GamesWindow : Window
{
    private readonly GameDetectionService _games;
    private bool _closed, _busy;

    public GamesWindow(GameDetectionService games)
    {
        InitializeComponent();
        _games = games;
        _games.Changed += OnChanged;
        ApplySnapshot(_games.Current);
        Closed += (_, _) => { _closed = true; _games.Changed -= OnChanged; };
    }

    private void OnChanged(GameDetectionSnapshot snapshot) => Dispatcher.BeginInvoke(() => { if (!_closed) ApplySnapshot(snapshot); });

    private void ApplySnapshot(GameDetectionSnapshot snapshot)
    {
        RunningList.ItemsSource = snapshot.RunningGames.Select(game => new RunningGameRow(game.Name,
            $"{(game.IsForeground ? "In focus" : "Running in background")} · {game.Source} · {Path.GetFileName(game.ExecutablePath)}")).ToArray();
        RegisteredList.ItemsSource = snapshot.RegisteredGames.Select(game => new RegisteredGameRow(game.Name, game.ExecutablePath)).ToArray();
        NoRunning.Visibility = snapshot.RunningGames.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        NoRegistered.Visibility = snapshot.RegisteredGames.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (!_busy) GameManagerStatus.Text = snapshot.Status;
    }

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var picker = new OpenFileDialog { Title = "Choose the game's executable", Filter = "Game executable (*.exe)|*.exe", CheckFileExists = true };
        if (picker.ShowDialog(this) != true) return;
        await PerformAsync(async () =>
        {
            await _games.RegisterGameAsync(picker.FileName, string.IsNullOrWhiteSpace(GameName.Text) ? null : GameName.Text.Trim());
            GameName.Clear();
        }, "Game added. It will be detected when its executable runs.");
    }

    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || (sender as FrameworkElement)?.Tag is not RegisteredGameRow game) return;
        await PerformAsync(() => _games.RemoveRegisteredGameAsync(game.ExecutablePath), "Removed from your added games. Game files were left in place.");
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
        => await PerformAsync(() => _games.RefreshCatalogAsync(), "Game libraries refreshed.");

    private async Task PerformAsync(Func<Task> operation, string completed)
    {
        if (_busy) return;
        _busy = true; AddButton.IsEnabled = false; RefreshButton.IsEnabled = false;
        GameManagerStatus.Text = "Updating games…";
        try { await operation(); if (!_closed) { ApplySnapshot(_games.Current); GameManagerStatus.Text = completed; } }
        catch (Exception ex) { if (!_closed) GameManagerStatus.Text = ex.Message; }
        finally { _busy = false; if (!_closed) { AddButton.IsEnabled = true; RefreshButton.IsEnabled = true; } }
    }

    private sealed record RunningGameRow(string Name, string Detail);
    private sealed record RegisteredGameRow(string Name, string ExecutablePath)
    {
        public string ExecutableName => Path.GetFileName(ExecutablePath);
    }
}
