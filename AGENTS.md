# CellTool — AI Agent Instructions

## Project Summary

WPF (.NET 8) desktop application for NAND Flash threshold voltage analysis. Reads raw voltage-scan binary data, plots incremental Vt distributions, computes optimal read levels per state pair, and evaluates codeword error rates. Target users: NAND characterization engineers.

## Tech Stack

| Concern | Choice |
|---|---|
| UI framework | WPF (.NET 8) |
| UI library | WPF-UI (Fluent Design, light/dark themes) |
| Architecture | MVVM via CommunityToolkit.Mvvm (source generators) |
| Charting | ScottPlot.WPF |
| Embedded HTTP | ASP.NET Core Kestrel (stub only for now) |
| Excel parsing | EPPlus or ClosedXML |
| Serialization | System.Text.Json |
| Testing | xUnit + Moq |

## Project Layout

```
src/CellTool/
├── Models/          # Plain data classes, no logic
├── Services/        # Core analysis engine + I/O helpers
├── ViewModels/      # CommunityToolkit.Mvvm ObservableObject + [RelayCommand]
├── Views/           # WPF UserControl per tab page
├── App.xaml(.cs)    # Application entry
└── MainWindow.xaml(.cs)  # NavigationView shell
```

## Architecture Rules

1. ViewModels must not reference UI controls. Use data-binding only.
2. Services are plain C# classes; inject via constructor DI.
3. All long-running work runs on `Task.Run` with `CancellationToken` and reports progress via `IProgress<T>`.
4. `AnalysisEngine` is the single orchestrator: it calls VoltageFileReader → GrayCodeDecoder → PeakAnalyzer → CodewordAnalyzer in sequence.
5. No WPF-specific types in `Services/` or `Models/`.

## Key Design Decisions (from requirements review)

- **Codeword size**: dynamically computed as `(PageDataBytes + PageRedundantBytes) / FrameCount` — NOT hardcoded to 1152.
- **bin file layout**: per WL, Upper page bytes → Middle page bytes → Lower page bytes, contiguous. No distinction between data and redundancy regions during analysis.
- **Gray code assembly**: default `U-M-L` (MSB=U, LSB=L). Byte-level endianness does not matter (all pages use the same bit order).
- **Majority-vote ground truth**: ties resolved to the numerically higher state. Cells that never flip are considered 100% stable.
- **Incremental distribution**: count cells that change state between consecutive voltages, grouped by the destination state.
- **0.1% boundary**: integrate outward from peak until `(totalCells/8) × 0.001`. If adjacent-state rising edge is hit before reaching threshold, mark boundary as `err`. Apply low-pass (moving-average, window=3) before boundary search.
- **Optimal read voltage**: per adjacent state pair (TLC → 7 values), search only on scanned voltage grid points (no interpolation), pick the valley with minimum misclassification.
- **Codeword analysis**: source-vs-reference comparison mode. Codewords are sliced within each page independently. Output two error rates: at best read voltage and at zero offset.
- **GroupModel**: user-selected CSV/TXT, one WL per line, comma-separated page indices. `-1` is an optional placeholder. Row count must equal `WL/Block`.
- **HTTP API / file monitor / CLI**: all deferred — stub interfaces only, throw `NotImplementedException`.

## Build & Run

```bash
dotnet restore src/CellTool/CellTool.csproj
dotnet build src/CellTool/CellTool.csproj
dotnet run --project src/CellTool/CellTool.csproj
```

Requires .NET 8 SDK and WPF workload.

## Coding Conventions

- C# files: UTF-8, LF line endings.
- Public API: XML doc-comments on service methods.
- Async suffix on Task-returning methods.
- `var` for obvious types, explicit types otherwise.
- Private fields: no underscore prefix (use `[ObservableProperty]` source-gen instead for VM properties).
