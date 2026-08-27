# Angesn Hardware Monitor

A lightweight Windows hardware-monitoring widget: a borderless, always-on-top readout backed by
[`LibreHardwareMonitorLib`](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor), with all
readings persisted to SQLite so history charts can be added later without touching the monitoring
pipeline. No HWiNFO dependency.

```
CPU TEMP    48°
CPU USE     12%
RAM         36%
GPU TEMP    43°
COMPUTE      4%
GPU MEM     27%
VRAM TEMP   56°
FAN         1250
```

## Metrics

Eight logical metrics, each independently schedulable:

| Row | Meaning |
| --- | --- |
| `CPU TEMP` | CPU package temperature |
| `CPU USE` | Overall CPU utilisation |
| `RAM` | System memory usage (used/total GB optional) |
| `GPU TEMP` | GPU core temperature |
| `COMPUTE` | GPU graphics/compute utilisation |
| `GPU MEM` | How full VRAM is, by capacity |
| `VRAM TEMP` | GPU memory (VRAM junction) temperature |
| `FAN` | GPU fan speed in RPM — `0` is a valid zero-fan reading, not a missing sensor |

An unavailable sensor shows `--`.

## Architecture

```
LibreHardwareMonitorLib
          v
IHardwareMonitorService  <- LibreHardwareMonitorService
          v
   HardwareSnapshot
       /        \
MainViewModel   HardwareHistoryRepository
     v                    v
 WPF widget            SQLite
```

`HardwareMonitorScheduler` is the single background polling loop. The ViewModel never touches
`LibreHardwareMonitorLib`, and the service never returns formatted strings.

### Sensor discovery

Selection is never "find the sensor named exactly X". It is hardware type, then sensor type, then a
prioritised candidate-name list, then a conservative fallback — so the same code works across
NVIDIA, AMD and Intel. Chosen sensors are cached; a refresh only calls `Update()` on the hardware
objects actually needed, so several GPU metrics due together cost one GPU update.

### Polling

Unified (one interval for everything, default 30s) or per-metric intervals, 1–300s. Changes apply
immediately with no restart, and the `Computer` instance is reused for the application's lifetime.

An idle cadence backs polling off once the machine has had no keyboard or mouse input for a
configurable period (default 5 minutes), following the same unified/individual mode — so choosing
per-metric intervals gives a per-metric idle interval too. The loop still re-checks for input every
5 seconds while idle, so returning to the machine refreshes everything within seconds rather than
waiting out a long idle interval.

### History

`%LOCALAPPDATA%\HardwareWidget\Data\hardware-history.db`, one row per (metric, timestamp):

```sql
CREATE TABLE MetricReadings (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    TimestampUtc    TEXT NOT NULL,          -- ISO-8601, human readable
    TimestampUnixMs INTEGER NOT NULL,       -- sorting, range queries, indexes
    MetricType      VARCHAR(255) NOT NULL,  -- stable string key, e.g. 'gpu.compute_usage'
    DeviceId        VARCHAR(255) NULL,
    Value           REAL NULL
);
```

Both timestamps derive from one captured `DateTimeOffset`. A metric that was not due has no row; a
metric that was due but unavailable gets a `NULL` value — a different and equally real observation.
A database failure logs a warning and never stops live monitoring.

## UI

Borderless and resizable from any edge, draggable, Aero Snap disabled. Widening it reflows the rows
into additional columns. Right-click for settings, text size, opacity, always-on-top and lock; the
tray icon holds show/hide and exit.

Each metric can be hidden, and rows can be dragged into any order — the widget follows the order set
in Settings. Appearance and colour stages apply as you change them; the monitoring settings rebuild
the polling schedule, so they wait for their own Save button.

Two appearances, matching the sibling AI Usage Monitor app: **Retro** (embedded pixel font, aliased
text, square corners, 1px border) and **Default** (system font, ClearType, rounded card), plus the
same 13-font list.

Each metric is graded into five colour stages over its own scale rather than a blanket 0–100 —
a CPU is never near 0 °C and fan RPM is not a percentage. All thresholds are editable.

## Requirements

- Windows 10 / 11
- .NET 10 (`net10.0-windows`)
- Administrator rights, requested via `app.manifest`

### CPU temperature and the WinRing0 driver

LibreHardwareMonitor reads CPU temperature through the `WinRing0` kernel driver, which it extracts
next to the executable as `<AppName>.sys`. On current Windows builds Windows Defender deletes that
file on sight, identifying it as `VulnerableDriver:WinNT/Winring0`; if the service does get
registered, starting it then fails with *"the file contains a virus or potentially unwanted
software"*. The driver is also on Microsoft's vulnerable-driver blocklist
(`VulnerableDriverBlocklistEnable`), so excluding it from Defender is not necessarily sufficient.

The result is that CPU temperature reads as unavailable (`--`). **Elevation does not change this** —
the app requests administrator rights and still cannot load the driver. Everything else, including
all GPU sensors and RAM, is unaffected because none of it needs the driver: GPU readings come from
the vendor's user-mode API and RAM from ordinary Windows calls.

A temperature sensor reporting exactly `0.00` is treated as unavailable rather than as a
measurement, since a powered-on part is never at freezing point. Fan RPM deliberately does not get
that rule, because `0` RPM is genuine in zero-fan idle mode.

## Build

```bash
dotnet build src/HardwareWidget -c Release
```
