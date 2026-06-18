# NAND Threshold Voltage Analysis & Read Optimization Tool

## Project Overview

WPF (.NET 8) desktop application for NAND Flash threshold voltage analysis. Reads raw voltage-scan binary data, plots incremental Vt distributions, computes optimal read levels per state pair, and evaluates codeword error rates. Target users: NAND characterization engineers.

## Technology Stack

| Concern | Choice |
|---|---|
| UI framework | WPF (.NET 8) |
| UI library | WPF-UI (Fluent Design, light/dark themes) |
| Architecture | MVVM via CommunityToolkit.Mvvm (source generators) |
| Charting | ScottPlot.WPF |
| Embedded HTTP | ASP.NET Core Kestrel (stub only) |
| Excel parsing | EPPlus |
| Serialization | System.Text.Json |

## Input Data

### Voltage Scan Binary Files
- Naming: `{offset/10}.bin` (e.g. `250.bin` = +2.5V offset)
- Layout: Per WL, Upper page bytes -> Middle page bytes -> Lower page bytes, contiguous
- No distinction between data and redundancy regions during analysis
- Byte-level endianness does not matter (all pages use same bit order)

### Factory Excel Database
Core columns (selected die auto-fills from these):

| Column | Example (G8T22) | Purpose |
|---|---|---|
| `die简称` | G8T22 | UI dropdown identifier |
| `xLC` | TLC | Infer state count, valid page count per WL |
| `页 数据 Byte` | 16384 | Compute page_total_bytes |
| `页 冗余 Byte` | 1952 | Compute page_total_bytes |
| `1KB Frame 个数 KB` | 16 | Codeword count per page |
| `块大小 (页数量)` | 576 | Validation |
| `WL/Block` | 192 | WL count upper bound |
| `WL编码` | 7,6,4,0,2,3,1,5 | Gray code -> physical state mapping (comma-separated) |

### GroupModel Text File
- One line per WL, comma-separated page indices
- `-1` is an optional placeholder
- Row count must equal `WL/Block`
- User manually selects the csv/txt file

## Core Analysis Pipeline

1. **File scan**: Parse filenames, sort by voltage offset
2. **Page extraction**: MemoryMappedFile reads target WL/page bytes
3. **Gray code decode**: Assemble U/M/L bits -> gray code -> physical state per cell
4. **Majority vote**: Ground truth per cell across all voltages. Ties -> higher state
5. **Increment curves**: Cells changing state between consecutive voltages, grouped by destination state
6. **0.1% boundaries**: Integrate outward from peak until `(totalCells/8) * 0.1%`. Overlap -> err
7. **Best read voltages**: Per adjacent state pair, valley on scanned grid points, no interpolation
8. **Codeword errors**: Source-vs-reference comparison, page-internal codeword slicing

## Key Design Decisions (from requirements review)

- **Codeword size**: Dynamically computed as `(PageDataBytes + PageRedundantBytes) / FrameCount`
- **Gray code assembly**: Default U-M-L (MSB=U, LSB=L)
- **0.1% boundary overlap**: Mark as `err` if adjacent-state rising edge encountered before threshold
- **Low-pass filter**: Moving average, window=3, applied before boundary search
- **HTTP API / file monitor / CLI**: All deferred — stub interfaces only
- **Ties in majority vote**: Resolved to numerically higher state
- **Never-flipping cells**: Considered 100% stable

## Output Products

- **PNG**: Vt increment distribution chart with state curves, 0.1% boundaries, best read voltage markers
- **CSV reports**: Peak parameters, best voltages per WL, codeword error statistics
- **JSON config**: All UI parameters for save/restore

## Development Stages

| Stage | Content |
|---|---|
| I | WPF project scaffold, WPF-UI + ScottPlot, navigation shell |
| II | Excel parsing, GroupModel parsing, file scan & page extraction |
| III | Core analysis (gray decode, increment dist, peaks, best Vread, codeword) |
| IV | Chart config page with ScottPlot preview |
| V | HTTP API + file monitor stubs |
| VI | CSV/PNG export, config persistence, theme switching |
| VII | Performance optimization, large-file testing, documentation |
