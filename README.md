# WorldClock14

Convert a time into every timezone you coordinate across, then paste one line into chat.

`/tz` · Dalamud API 15 · .NET 10

## What it does

- **Live now** (default) — the current instant in every configured zone, ticking each frame.
- **Custom time** — type a time and see it everywhere: `20:00`, `8pm`, `8:30pm`, `2000`, `930`,
  `20`, `Wed 21:00`, `next mon 09:00`, `tomorrow 20:00`, `2026-10-07 20:00`, `2026-10-07T20:00`,
  `10/07 20:00`. Bare dates (`10-07`) roll to next year once past; `today`, `tonight`, `tomorrow`
  (`tmrw`, `tmw`), `yesterday` and a filler `at` also parse.
- **Copy for chat** — one line, `16:30 IST | 20:00 JST | 11:00 UTC | 07:00 ET`, or a per-zone Copy
  on any row. A zone whose date moved is marked: `22:00 ET Wed -1d` (`07 Oct -1d` when the date
  option is on).

## How it works

- **No hooks, no native pointers, no game memory.** It reads the clock and draws. Parsing, zone
  resolution, conversion and chat formatting all live in `TimeConversion.cs`, with zero Dalamud
  types in it.
- **DST is handled, not guessed.** A time that does not exist (the spring-forward gap) is refused
  with an explanation; the repeated autumn hour is flagged.
- **Bad zone ids cannot take the window down**: an unresolvable id becomes a row that says so, and
  the base zone falls back to system local.
- **Clipboard writes happen inside a draw frame**, queued from `/tz` and written from the Draw
  handler — so `/tz` also works with the window shut.

## Commands

| Command | Effect |
| --- | --- |
| `/tz` | open the window |
| `/tz <time>` | convert, copy (and log) the line |
| `/tz now` | convert this instant |
| `/tz config`, `cfg`, `settings`, `/tzconfig` | settings window |
| `/tz help`, `?`, `-h`, `--help` | print the accepted forms to `/xllog` |

## Settings

- **Zones** — the list the window and the copied line use, in order: add from 25 presets, type any
  IANA (`Asia/Tokyo`) or Windows (`Tokyo Standard Time`) id, or pin an offset in minutes
  (`330` = `+05:30`) that never shifts for DST. Reorder, disable, rename.
- **Base zone** — what a typed time is *read as*; system local by default, any zone in the list.
- **Format** — 24-hour, weekday (or date) on a day change, one zone per line, separator (default
  ` | `), prefix, suffix.
- **Command behaviour** — copy the line and print it to `/xllog` on `/tz <time>` (both on).

## Build

Requires the .NET 10 SDK and the Dalamud dev distribution
(`%APPDATA%\XIVLauncher\addon\Hooks\dev`, or `DALAMUD_HOME`).

```
dotnet build -c Debug      # load bin/Debug/WorldClock14.dll as a dev plugin
dotnet build -c Release    # zip in bin/Release/WorldClock14/latest.zip
```

## Install (dev plugin)

1. `/xlsettings` → Experimental → Dev Plugin Locations → add `bin\Debug\WorldClock14.dll`.
2. `/xlplugins` → Dev Tools → enable **WorldClock14**.
3. `/tz` opens the window; `/tzconfig` opens settings.

## Tests

`tests/LogicTests` compiles `TimeConversion.cs` and `Defaults.cs` **from the plugin**
(`<Compile Include>`, no copies) and runs with no Dalamud, no dev distrib and no game:

```
cd tests/LogicTests && dotnet run -c Release
# 148977 checks, 0 failures
```

That includes a round trip over 148,860 wall clocks (2024–2027, 17 zones): naive time → instant →
back to the same wall clock, with 30 DST-gap and 30 ambiguous hours skipped by design. A reference
harness then compares .NET against ICU, 2024-01-01 → 2027-01-01, hourly, 18 zones:

```
cd tests/LogicTests && dotnet run -c Release -- --dump grid.net.tsv
node ../reference.mjs grid.icu.tsv
python ../compare.py grid.net.tsv grid.icu.tsv
```

473,472 rows compared, 0 mismatches, byte-identical output.

## Limits

- DST rules come from the host tz database; a fixed-offset zone never moves, by design.
- A time typed without a date is read in the base zone's today.
- The presets are a curated 25, not the full tz database; anything else is typed as an id.
- Not verified in game: behaviour is taken from the code and the offline tests, not from a live client.

## Layout

```
TimeConversion.cs   all logic - parsing, zone resolution, conversion, chat formatting (no Dalamud)
Defaults.cs         seeded zones and the 25 presets (no Dalamud)
Configuration.cs    IPluginConfiguration, repair of a hand-edited config
TimeShiftWindow.cs  main window
ConfigWindow.cs     settings
UiHelpers.cs        shared widgets
preview.html        window mockup, live via Intl
```
