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

- **Codeword size**: dynamically computed as `(PageDataBytes + PageRedundantBytes) / FrameCount` — DO NOT hardcode.
- **bin file layout**: per WL, configured page slots are read contiguously from GroupModel. The observed target-tool TLC sample uses slot roles `U-M-L`.
- **Gray code assembly**: `GrayCodeOrder` means page-slot role order, not raw Gray weight order. For observed TLC `U-M-L` slots, raw Gray is assembled as `(L << 2) | (M << 1) | U`; byte bit order is configurable and defaults to `MSB` per reverse-engineered target-tool behavior.
- **Voltage scan units**: voltage-scan file names are integer voltage codes. One code corresponds to 10mV, but UI ranges, CSV peak/read outputs, and analysis grid points use code units, not mV.
- **Chart display x-axis**: reconstructed PNG curves carry per-level code x-values. Read-boundary positions use manually editable L spacing from data configuration; each mode accepts either one spacing value or a comma-separated per-gap list (`MLC=2 gaps`, `TLC=6 gaps`, `QLC=14 gaps`).
- **WL mode selection**: each GroupModel row can have 1-4 valid page indices. The valid page count selects SLC/MLC/TLC/QLC mode and the matching manually editable WL encoding.
- **0.1% boundary / optimal read voltage**: deferred for this reconstruction pass. Do not emit reliable best-read codes until boundary-search validation is reintroduced.
- **Codeword analysis**: source-vs-reference comparison mode. Codewords are sliced within each page independently. Output two error rates: at best read voltage and at zero offset.
- **GroupModel**: user-selected CSV/TXT, one WL per line, comma-separated page indices. `-1` is an optional placeholder. Row count must equal `WL/Block`.
- **HTTP API / file monitor / CLI**: all deferred — stub interfaces only, throw `NotImplementedException`.

## Vread / Vt Reconstruction Ground Rules

These rules are hard constraints for future analysis changes. If generated curves deviate from a reference image, debug within these rules first. Do not change correct low-level reconstruction logic merely to make a wrong curve look plausible.

### Reverse-Engineered Target-Tool Facts

- The confirmed target-tool TLC decoder scans bits MSB-first and assembles observed `U-M-L` page slots into raw Gray as `(L << 2) | (M << 1) | U`; physical level is `index_of(rawGray in WL encoding)`.
- `calProc` maintains per-cell `RdLogic`, `Last`, `CStats`, `JCnt`, `SJPos`, `EJPos`, stable, error-count, and update-count tables. The confirmed stable path writes `CStats = 0x40 + RdLogic` and `Last = RdLogic`; confirmed transition-window paths update `SJPos/EJPos` as recorded in `CalProcState`.
- The final target-tool histogram is a per-WL matrix shaped like `uint32[levels][737]` for full TLC `-128..127` / spacing-80 scans: left unable, ordinary bins, right unable, abnormal. The lower scan bound (`-128` in the observed run) is a sentinel, not an ordinary distribution bin.
- The exact target-tool branch that converts `SJPos/EJPos/CStats` into distribution columns has not been fully recovered. Until it is confirmed, keep the state replay separate from the physically grounded adjacent-Gray transition reconstruction. Do not use a partially inferred `SJ/EJ` window as the sole plotted Vt position.

- Each `Ri` / Vread boundary may only search for direct Gray-code transitions between its two adjacent physical levels, `L(i-1)` and `Li`.
- A single `Ri` scan can read the relative distributions of both adjacent levels within the configured maximum offset range. The offset is a relative distance from that `Ri`, not an absolute Vt coordinate.
- `R1` is the absolute origin (`0` code). `R2..Rn` absolute positions may be fixed by manual L spacing, or estimated from adjacent Vread direct-transition offsets when that diagnostic is explicitly enabled.
- Each source level between two Vreads can estimate that Vread gap from the same cell's two adjacent direct-transition offsets: `leftBoundaryOffset - rightBoundaryOffset`. This remains valid even when the cell lies outside the two zero-offset boundaries, because both offsets move in the same direction. Do not discard these samples merely because they are not visually between the two boundary origins.
- For plotting, only cells that show a legal direct transition within the scanned offset range can be drawn. Cells that do not show the required direct transition before the maximum offset must be counted as that level's LOR or ROR, not forced into the visible peak.
- A cell from level `Li` may be plotted to the left of `Ri` or to the right of `R(i+1)` if its direct transition relative to its adjacent Vread says so. That point still belongs to `Li`; it must not be reused to estimate any unrelated Vread spacing.
- Non-adjacent level transitions, cross-level jumps, and `LeftSideOffset` / `RightSideOffset` are not valid evidence for internal `L1..L6` Vread spacing and must not replace direct adjacent-neighbor evidence.
- L0 is determined by scanning left of `R1`; L7 (or the highest physical level for the current mode) is determined by scanning right of the last read boundary. Cells not observed within range go to LOR/ROR as appropriate.
- Do not use chart-layer scaling, cross-level fallback, side-offset substitution, or forced integral normalization to explain away violations of the adjacent-boundary model. Report the issue as insufficient direct-transition evidence, insufficient scan range, bad spacing estimation, or abnormal data.

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
