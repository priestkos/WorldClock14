# WorldClock14

<img src="images/icon.png" width="96">

Convert a time into every timezone you care about, then paste it into chat.

## Screenshots

Screenshots are mock-ups illustrating the UI, not captures from the live game.

| | |
|---|---|
| ![Live now — current time in all zones](docs/screenshots/01-main-live-now.png) | ![Custom time — typed time with day rollover](docs/screenshots/02-main-custom-time.png) |
| **Live now** — default mode, base zone picker, per-zone **Copy**, **Copy for chat**, and the copied flash line. | **Custom time** — input parsing, **+1d** day marker, DST ambiguity note, echo line. |
| ![Bad zone row in results](docs/screenshots/03-main-warnings-errors.png) | ![WorldClock14 settings](docs/screenshots/04-settings.png) |
| **Resilience** — a bad zone id becomes an error row; other zones still convert. | **Settings** (`/tzconfig`) — zones, add/reorder/delete, base zone, copied-line format, preview. |
| ![Slash commands and log output](docs/screenshots/05-commands-scene.png) | |
| **`/tz` / `/tzconfig`** — commands, help text, and log lines for parse/DST errors (mock `/xllog` panel). | |

### Mock-up video

These are the same HTML mock-ups as the screenshots, recorded in a browser — **not** captures from the live game.

![Mock-up walkthrough preview](docs/videos/mockup-walkthrough.gif)

[Full walkthrough (MP4)](docs/videos/mockup-walkthrough.mp4)

### Feature coverage

Every user-facing surface from the plugin code is shown above (or named in a log line where there is no separate window).

| Feature | Where |
|---|---|
| Main window **WorldClock14** | 01–03, 05 |
| **Settings** button → config window | 01–03 |
| **Live now** / **Custom time** modes | 01, 02, 05 |
| Custom time input + placeholder hint (`20:00 · 8pm · …`) | 02 (filled); empty-field note `Type a time - …` appears in the same footer area when input is blank |
| **Times are read in** base zone combo (incl. **System local**) | 01–03, 05 |
| Zone table, base zone highlight, **+1d** / **-1d** markers | 01, 02 |
| Per-row **Copy** | 01–03 |
| **Copy for chat** + **Copied:** flash | 01, 02 |
| Parsed-time **echo** line | 01, 02 |
| DST ambiguity note (`Careful: the clocks change…`) | 02; also `/tz` log in 05 |
| Parse errors (`I don't understand…`) | 05 log |
| DST gap refusal (`doesn't exist … clocks jump forward`) | 05 log |
| Unknown zone row (`unknown zone "…"`) | 03 |
| **WorldClock14 settings** window | 04 |
| Zone enable, label, id / fixed minutes / **system local**, UTC **Now** offset | 04 |
| Reorder **^** **v**, delete **X** | 04 |
| **Add a zone** preset picker + **Add** | 04 |
| Config **unknown zone** inline warning | 04 |
| **Custom…** add (label + zone id + validation) | 04 |
| **Base zone** picker | 04 |
| **Copied line** format options (24h, weekday, date, multiline, separator, prefix, suffix) | 04 |
| **Preview** + **Copy preview** | 04 |
| **Reset to defaults** + auto-save footer | 04 |
| `/tz` open window | 05 |
| `/tz <time>` convert (+ copy/log per config defaults) | 05 |
| `/tz now` | 05 |
| `/tz config` / `cfg` / `settings` and `/tzconfig` | 05 |
| `/tz help` / `?` / `-h` / `--help` | 05 |
| Dalamud **Open Main UI** / **Open Config UI** (same windows) | 05 caption |

## The problem

"Raid at 20:00" is useless to a party spread across IST, JST, UTC and ET — and the conversion that matters is not the hour, it is the **day**. A time that reads Thursday in Tokyo reads Wednesday in New York, and that is the mistake that loses people.

## What it does

- **Live now** (default): the current instant in every zone you have configured, always ticking.
- **Custom time**: type a time and see it everywhere.
  `20:00` · `8pm` · `8:30pm` · `2000` · `930` · `20` · `Wed 21:00` · `next mon 09:00` ·
  `tomorrow 20:00` · `2026-10-07 20:00` · `2026-10-07T20:00` · `10/07 20:00`
- **Copy for chat**: one line, `16:30 IST | 20:00 JST | 11:00 UTC | 07:00 ET`, or a per-zone
  Copy on any row. Rolled-over zones get a marker: `22:00 ET Wed -1d`.
- `/tz <time>` converts and copies without opening the window; `/tz` opens it; `/tzconfig`
  opens settings; `/tz now` converts this instant.

## Zones

Defaults are IST, JST, UTC and ET. The list is yours: add from 25 presets, type any IANA
(`Asia/Tokyo`) or Windows (`Tokyo Standard Time`) id, or pin a raw offset as minutes
(`330` = `+05:30`) that never shifts for DST. Reorder, disable, rename.

The base zone — what a typed time is *read as* — defaults to system local and can be any
zone in the list.

## Design notes

- **No hooks, no native pointers, no game memory.** The plugin reads the clock and draws.
  All conversion logic lives in `TimeConversion.cs` with zero Dalamud types in it.
- **DST is handled, not guessed.** A time that does not exist (the spring-forward gap) is
  refused with an explanation instead of silently resolving to the wrong instant; the
  repeated hour in autumn is flagged.
- **Bad zone ids cannot take the window down**: an unresolvable id becomes a row that says
  so, and the base zone falls back to system local.
- **Clipboard writes happen inside a draw frame**, queued from `/tz` rather than written
  from the command callback.

## Layout

| File | |
|---|---|
| `TimeConversion.cs` | all logic — parsing, zone resolution, conversion, chat formatting (no Dalamud) |
| `Defaults.cs` | seeded zones and presets (no Dalamud) |
| `Configuration.cs` | `IPluginConfiguration`, normalization of a hand-edited config |
| `WorldClockWindow.cs` | main window |
| `ConfigWindow.cs` | settings |
| `UiHelpers.cs` | shared widgets |
| `preview.html` | window mockup, live via `Intl` |
| `docs/screenshots/` | README mock-up HTML + PNGs |

## Offline tests

`tests/LogicTests` compiles `TimeConversion.cs` and `Defaults.cs` **from the plugin**
(`<Compile Include>`, no copies) and runs with no Dalamud, no dev distrib and no game:

```
cd tests/LogicTests && dotnet run -c Release
# 148,970 checks, 0 failures
```

That includes a round-trip over 148,860 wall clocks (2024–2027, 17 zones): naive time →
instant → back to the same wall clock, with DST-gap and ambiguous hours skipped by design.

### Reference comparison

ICUs on two different sides, compared row by row — 2024-01-01 → 2027-01-01, hourly, 18 zones:

```
cd tests/LogicTests && dotnet run -c Release -- --dump grid.net.tsv
node ../reference.mjs grid.icu.tsv
python ../compare.py grid.net.tsv grid.icu.tsv
```

473,472 rows compared, 0 mismatches, byte-identical output.

## Build

Needs the .NET 10 SDK and the Dalamud dev distrib at
`%AppData%\XIVLauncher\addon\Hooks\dev` (or `DALAMUD_HOME`).

```
dotnet build -c Debug      # load bin/Debug/WorldClock14.dll as a dev plugin
dotnet build -c Release    # zip in bin/Release/WorldClock14/latest.zip
```

Dev plugin path: `/xlsettings` → Experimental → Dev Plugin Locations.
