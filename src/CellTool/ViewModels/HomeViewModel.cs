using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CellTool.Models;
using CellTool.Services;

namespace CellTool.ViewModels;

public partial class HomeViewModel : ObservableObject
{
    private readonly AnalysisEngine _engine = new();
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private string _inputDirectory = string.Empty;

    [ObservableProperty]
    private string _outputDirectory = string.Empty;

    [ObservableProperty]
    private int _voltageMin = -3000;

    [ObservableProperty]
    private int _voltageMax = 3000;

    [ObservableProperty]
    private int _voltageStep = 10;

    [ObservableProperty]
    private int _wlCount = 192;

    [ObservableProperty]
    private int _startPage = 0;

    [ObservableProperty]
    private bool _isAnalyzing;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    public ObservableCollection<string> LogEntries { get; } = new();

    // Shared state from DataConfigViewModel — set externally before running
    public ChipInfo? SelectedChip { get; set; }
    public GroupModel? GroupModel { get; set; }
    public string ReferenceFilePath { get; set; } = string.Empty;

    [RelayCommand]
    private async Task StartAnalysisAsync()
    {
        if (string.IsNullOrEmpty(InputDirectory) || string.IsNullOrEmpty(OutputDirectory))
        {
            MessageBox.Show("Please select input and output directories.",
                "CellTool", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (SelectedChip is null || GroupModel is null)
        {
            MessageBox.Show("Please load Chip Database and GroupModel in Data Config first.",
                "CellTool", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsAnalyzing = true;
        Progress = 0;
        LogEntries.Clear();
        Log("Starting analysis...");

        _cts = new CancellationTokenSource();

        try
        {
            var config = new AnalysisConfig
            {
                InputDirectory = InputDirectory,
                OutputDirectory = OutputDirectory,
                ReferenceFilePath = ReferenceFilePath,
                VoltageMinMv = VoltageMin,
                VoltageMaxMv = VoltageMax,
                VoltageStepMv = VoltageStep,
                WlCount = WlCount,
                StartPage = StartPage
            };

            var progress = new Progress<(double p, string msg)>(update =>
            {
                Progress = update.p * 100;
                StatusMessage = update.msg;
                Log(update.msg);
            });

            var result = await _engine.RunAsync(config, SelectedChip, GroupModel,
                progress, _cts.Token);

            Log($"Analysis complete. {result.TotalCells} cells, {result.VoltageCount} voltages.");
            Log($"Found {result.StatePeaks.Length} state peaks.");

            var csv = new CsvExporter();
            csv.ExportPeakReport(
                Path.Combine(OutputDirectory, "peaks.csv"),
                result.StatePeaks, Array.Empty<double>());
            csv.ExportBestVoltages(
                Path.Combine(OutputDirectory, "best_voltages.csv"),
                result.BestReadVoltages);

            if (result.BestVoltageErrors is not null)
                csv.ExportCodewordErrors(
                    Path.Combine(OutputDirectory, "codeword_errors.csv"),
                    result.BestVoltageErrors);

            Log("Results exported to output directory.");
        }
        catch (OperationCanceledException)
        {
            Log("Analysis cancelled.");
        }
        catch (Exception ex)
        {
            Log($"Error: {ex.Message}");
            MessageBox.Show($"Analysis failed: {ex.Message}",
                "CellTool", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsAnalyzing = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private void CancelAnalysis() => _cts?.Cancel();

    public void Log(string message)
    {
        var ts = DateTime.Now.ToString("HH:mm:ss");
        Application.Current?.Dispatcher.Invoke(() =>
            LogEntries.Add($"[{ts}] {message}"));
    }
}
