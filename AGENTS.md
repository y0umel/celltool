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

## 执行计划 (ExecPlans)

io规范、各种功能落地参考 ExecPlan（详见 celltool/PLANS.md）

## Architecture Rules

1. ViewModels must not reference UI controls. Use data-binding only.
2. Services are plain C# classes; inject via constructor DI.
3. All long-running work runs on `Task.Run` with `CancellationToken` and reports progress via `IProgress<T>`.
4. `AnalysisEngine` is the single orchestrator: it calls VoltageFileReader → GrayCodeDecoder → source-level Vt reconstruction → CodewordAnalyzer in sequence.
5. No WPF-specific types in `Services/` or `Models/`.

## Key Design Decisions (from requirements review)

- **Codeword size**: dynamically computed as `(PageDataBytes + PageRedundantBytes) / FrameCount` — NOT hardcoded to 1152.
- **bin file layout**: per WL, Upper page bytes → Middle page bytes → Lower page bytes, contiguous. No distinction between data and redundancy regions during analysis.
- **Gray code assembly**: default `U-M-L` (MSB=U, LSB=L). Byte-level endianness does not matter (all pages use the same bit order).
- **Majority-vote ground truth**: ties resolved to the numerically higher state. Cells that never flip are considered 100% stable.
- **Voltage scan units**: voltage-scan file names are integer voltage codes. One code corresponds to 10mV, but UI ranges, CSV peak/read outputs, and analysis grid points use code units, not mV.
- **Vt distribution reconstruction**: draw one curve per physical source level (`L0..L7` for TLC). Use the configured source/reference file as the baseline, map source and current raw Gray codes through WL encoding to physical levels, count cells whose current level differs from their source level, and plot the positive adjacent-offset delta of each source-level cumulative error count. This matches the reference tool style more closely than the older `Lx-left/right` boundary-side output.
- **Chart display x-axis**: reconstructed PNG curves carry per-level code x-values. Read-boundary positions use the manually editable L spacing from data configuration (`MLC=145`, `TLC=80`, `QLC=40` code defaults); L1..L6 use `level * spacing + voltageCode`, and the last level uses the last read boundary position.
- **WL mode selection**: each GroupModel row can have 1-4 valid page indices. The valid page count selects SLC/MLC/TLC/QLC mode and the matching manually editable WL encoding.
- **0.1% boundary / optimal read voltage**: deferred for this reconstruction pass. Do not emit reliable best-read codes until boundary-search validation is reintroduced.
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
