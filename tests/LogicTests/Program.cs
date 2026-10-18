using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using WorldClock14;

namespace WorldClock14.LogicTests;

/// <summary>
/// Runs WorldClock14's real logic (TimeConversion.cs, compiled straight out of the plugin) with no game
/// running. Two jobs: a self-checking assertion suite, and --dump, which writes a grid another timezone
/// implementation can be compared against.
/// </summary>
internal static class Program
{
    private static int checks;
    private static int failures;

    private static int Main(string[] args)
    {
        var dumpIndex = Array.IndexOf(args, "--dump");
        if (dumpIndex >= 0 && dumpIndex + 1 < args.Length)
        {
            DumpGrid(args[dumpIndex + 1]);
            return 0;
        }

        var today = new DateTime(2026, 10, 7);
        Check("sanity: 2026-10-07 is a Wednesday", today.DayOfWeek.ToString(), "Wednesday");

        Section("zone resolution");
        ZoneResolution();
        Section("time parsing");
        TimeParsing(today);
        Section("parse rejections");
        ParseRejections(today);
        Section("instant resolution and DST edges");
        InstantResolution();
        Section("conversion across zones");
        Conversion();
        Section("chat line formatting");
        Formatting();
        Section("round trip, 3 years of wall clocks x 17 zones");
        RoundTrip();

        Console.WriteLine();
        Console.WriteLine($"{checks} checks, {failures} failures");
        return failures == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------ sections

    private static void ZoneResolution()
    {
        foreach (var preset in Defaults.Presets())
        {
            if (preset.IsFixed)
            {
                Assert($"preset {preset.Label} is a fixed offset",
                    ZoneResolver.FixedOffset(preset.FixedMinutes).GetUtcOffset(DateTimeOffset.UtcNow) == TimeSpan.FromMinutes(preset.FixedMinutes));
                continue;
            }

            Assert($"preset {preset.Label} resolves ({preset.Id})", ZoneResolver.ResolveId(preset.Id) is not null);
        }

        Check("IANA id resolves", ZoneResolver.ResolveId("Asia/Tokyo")?.Id, "Asia/Tokyo");
        Assert("Windows id resolves too", ZoneResolver.ResolveId("Tokyo Standard Time") is not null);
        var tokyoIana = ZoneResolver.ResolveId("Asia/Tokyo");
        var tokyoWindows = ZoneResolver.ResolveId("Tokyo Standard Time");
        Assert("SameZone matches IANA and Windows ids for Tokyo",
            tokyoIana is not null && tokyoWindows is not null && Converter.SameZone(tokyoIana, tokyoWindows));
        Assert("unknown id is null, not an exception", ZoneResolver.ResolveId("Mars/Olympus_Mons") is null);
        Assert("empty id is null", ZoneResolver.ResolveId("") is null);
        Assert("null id is null", ZoneResolver.ResolveId(null) is null);

        var ist = ZoneResolver.ResolveId("Asia/Kolkata")!;
        Check("IST is +05:30", ist.GetUtcOffset(new DateTimeOffset(2026, 10, 7, 0, 0, 0, TimeSpan.Zero)).ToString(), "05:30:00");

        var fixedIst = ZoneResolver.FixedOffset(330);
        Check("fixed +330", fixedIst.GetUtcOffset(DateTimeOffset.UtcNow).ToString(), "05:30:00");
        Check("fixed -300", ZoneResolver.FixedOffset(-300).GetUtcOffset(DateTimeOffset.UtcNow).ToString(), "-05:00:00");
        Assert("fixed offset is clamped to +-14h", ZoneResolver.FixedOffset(99999).GetUtcOffset(DateTimeOffset.UtcNow) == TimeSpan.FromHours(14));
    }

    private static void TimeParsing(DateTime today)
    {
        Check("20:00", Wall("20:00", today), "2026-10-07 20:00");
        Check("20:00:45 keeps seconds", Wall("20:00:45", today, "yyyy-MM-dd HH:mm:ss"), "2026-10-07 20:00:45");
        Check("8pm", Wall("8pm", today), "2026-10-07 20:00");
        Check("8 pm", Wall("8 pm", today), "2026-10-07 20:00");
        Check("8:30pm", Wall("8:30pm", today), "2026-10-07 20:30");
        Check("12am is midnight", Wall("12am", today), "2026-10-07 00:00");
        Check("12pm is noon", Wall("12pm", today), "2026-10-07 12:00");
        Check("2000 military", Wall("2000", today), "2026-10-07 20:00");
        Check("930 military", Wall("930", today), "2026-10-07 09:30");
        Check("bare hour 20", Wall("20", today), "2026-10-07 20:00");
        Check("tomorrow 20:00", Wall("tomorrow 20:00", today), "2026-10-08 20:00");
        Check("tonight 22:30", Wall("tonight 22:30", today), "2026-10-07 22:30");
        Check("2026-10-07 20:00", Wall("2026-10-07 20:00", today), "2026-10-07 20:00");
        Check("ISO T separator", Wall("2026-10-07T20:00", today), "2026-10-07 20:00");
        Check("single digit ISO", Wall("2026-1-7 20:00", today), "2026-01-07 20:00");
        Check("10-07 20:00 uses the current year", Wall("10-07 20:00", today), "2026-10-07 20:00");
        Check("slashes work", Wall("10/07 20:00", today), "2026-10-07 20:00");
        Check("time first, date second", Wall("20:00 2026-12-24", today), "2026-12-24 20:00");
        Check("at is ignored", Wall("22:00 at night".Replace("night", ""), today), "2026-10-07 22:00");
        Check("wed 21:00 (today is Wed)", Wall("wed 21:00", today), "2026-10-07 21:00");
        Check("thursday 19:00", Wall("thursday 19:00", today), "2026-10-08 19:00");
        Check("mon 09:00", Wall("mon 09:00", today), "2026-10-12 09:00");
        Check("next wed rolls a full week", Wall("next wed 21:00", today), "2026-10-14 21:00");
        Check("next mon skips this week's Monday", Wall("next mon 09:00", today), "2026-10-19 09:00");
        Check("01-05 after October rolls to next year", Wall("01-05 20:00", today), "2027-01-05 20:00");

        Assert("parsed Echo is populated", TimeInput.TryParse("20:00", today, out var p, out _) && p.Echo.Length > 0);
        Check("echo respects 12-hour", TimeFmt.Echo(new DateTime(2026, 10, 7, 20, 0, 0), use24Hour: false), "Wed 07 Oct 2026 8:00 PM");
        Assert("HadDate is false for a bare time", TimeInput.TryParse("20:00", today, out var p2, out _) && !p2.HadDate);
        Assert("HadDate is true for tomorrow", TimeInput.TryParse("tomorrow 20:00", today, out var p3, out _) && p3.HadDate);

        Check("fixed Describe clamps to +14:00", TimeZoneEntry.Fixed("X", 99999).Describe(), "+14:00 fixed");
        Check("fixed Describe clamps to -14:00", TimeZoneEntry.Fixed("Y", -99999).Describe(), "-14:00 fixed");
    }

    private static void ParseRejections(DateTime today)
    {
        CheckReject("empty", "");
        CheckReject("whitespace", "   ");
        CheckReject("nonsense", "banana");
        CheckReject("hour 25", "25:00");
        CheckReject("hour 13 with pm", "13pm");
        CheckReject("minute 70", "20:70");
        CheckReject("date only", "tomorrow");
        CheckReject("month 13", "2026-13-01 20:00");
        CheckReject("day 40", "2026-01-40 20:00");
        CheckReject("military minute 99", "2099");
        CheckReject("am on a 24h hour", "20am");
    }

    private static void InstantResolution()
    {
        var jst = ZoneResolver.ResolveId("Asia/Tokyo")!;
        var ist = ZoneResolver.ResolveId("Asia/Kolkata")!;
        var et = ZoneResolver.ResolveId("America/New_York")!;
        var pt = ZoneResolver.ResolveId("America/Los_Angeles")!;
        var utc = ZoneResolver.FixedOffset(0);

        Assert("JST 20:00 -> 11:00 UTC",
            Converter.TryResolveInstant(new DateTime(2026, 10, 7, 20, 0, 0), jst, out var a, out _, out _) &&
            a.UtcDateTime.TimeOfDay == new TimeSpan(11, 0, 0) && a.UtcDateTime.Day == 7);

        // JST has no DST; ET was still on EDT (-4) on 7 Oct 2026.
        Assert("that instant is 07:00 in ET",
            Converter.TryResolveInstant(new DateTime(2026, 10, 7, 20, 0, 0), jst, out var b, out _, out _) &&
            TimeZoneInfo.ConvertTime(b, et).TimeOfDay == new TimeSpan(7, 0, 0));

        Assert("that instant is 16:30 in IST",
            TimeZoneInfo.ConvertTime(b, ist).TimeOfDay == new TimeSpan(16, 30, 0));

        // Winter: ET is EST (-5), so the same JST hour is an hour earlier.
        Assert("winter JST 20:00 is 06:00 ET",
            Converter.TryResolveInstant(new DateTime(2026, 1, 15, 20, 0, 0), jst, out var c, out _, out _) &&
            TimeZoneInfo.ConvertTime(c, et).TimeOfDay == new TimeSpan(6, 0, 0));

        // 2026 US spring forward: Sunday 8 March. 02:30 does not exist in ET.
        var gap = Converter.TryResolveInstant(new DateTime(2026, 3, 8, 2, 30, 0), et, out _, out _, out var gapError);
        Assert("DST gap is refused, not guessed", !gap);
        Assert($"DST gap says why (\"{gapError}\")", gapError.Contains("doesn't exist", StringComparison.Ordinal));

        // 02:00:00 is also inside the gap; 01:59 and 03:00 are fine.
        Assert("01:59 ET resolves", Converter.TryResolveInstant(new DateTime(2026, 3, 8, 1, 59, 0), et, out _, out _, out _));
        Assert("03:00 ET resolves", Converter.TryResolveInstant(new DateTime(2026, 3, 8, 3, 0, 0), et, out _, out _, out _));
        Assert("02:30 ET on the 9th resolves", Converter.TryResolveInstant(new DateTime(2026, 3, 9, 2, 30, 0), et, out _, out _, out _));

        // 2026 US fall back: Sunday 1 November. 01:30 happens twice.
        var ambiguous = Converter.TryResolveInstant(new DateTime(2026, 11, 1, 1, 30, 0), et, out _, out var isAmbiguous, out _);
        Assert("fall-back hour is flagged ambiguous", ambiguous && isAmbiguous);
        Assert("01:30 on 2 November is not ambiguous",
            Converter.TryResolveInstant(new DateTime(2026, 11, 2, 1, 30, 0), et, out _, out var notAmbiguous, out _) && !notAmbiguous);

        // JST and IST never have DST, so nothing there is ever invalid or ambiguous.
        var oddities = 0;
        foreach (var zone in new[] { jst, ist, utc })
        {
            for (var day = 0; day < 366 * 3; day++)
            {
                var probe = new DateTime(2024, 1, 1).AddDays(day);
                foreach (var hour in new[] { 0, 1, 2, 3, 4, 12, 23 })
                {
                    if (zone.IsInvalidTime(probe.AddHours(hour))) oddities++;
                    if (zone.IsAmbiguousTime(probe.AddHours(hour))) oddities++;
                }
            }
        }
        Check("no DST oddities in JST/IST/UTC over 3 years", oddities, 0);

        Assert("PT 20:00 lands on the next day in JST",
            Converter.TryResolveInstant(new DateTime(2026, 10, 7, 20, 0, 0), pt, out var d, out _, out _) &&
            TimeZoneInfo.ConvertTime(d, jst).Date > TimeZoneInfo.ConvertTime(d, pt).Date);
    }

    private static void Conversion()
    {
        var zones = Defaults.CreateZones();
        var baseZone = ZoneResolver.ResolveId("Asia/Tokyo")!;
        var stamp = new DateTimeOffset(2026, 10, 7, 11, 0, 0, TimeSpan.Zero);

        var opts = new FormatOptions();
        var rows = Converter.Convert(stamp, baseZone, zones);
        Check("four rows", rows.Count, 4);
        Check("IST row", rows[0].Time(opts), "16:30");
        Check("JST row", rows[1].Time(opts), "20:00");
        Check("UTC row", rows[2].Time(opts), "11:00");
        Check("ET row", rows[3].Time(opts), "07:00");
        Check("the same rows render as 12-hour when asked", rows[1].Time(new FormatOptions { Use24Hour = false }), "8:00 PM");
        Assert("JST is flagged as the base zone", rows[1].IsBase);
        Assert("no day deltas for this instant", rows.All(r => r.DayDelta == 0 && r.DeltaText.Length == 0));

        // An instant where the Americas have already rolled over.
        var roll = new DateTimeOffset(2026, 10, 8, 2, 0, 0, TimeSpan.Zero);
        var rollRows = Converter.Convert(roll, baseZone, zones);
        Check("JST 11:00 on the 8th", rollRows[1].Time(opts), "11:00");
        Assert("ET is a day behind", rollRows[3].DayDelta == -1 && rollRows[3].DeltaText == "-1d");
        Assert("UTC is on the same date as the base zone", rollRows[2].DayDelta == 0);
        Assert("IST is not", rollRows[0].DayDelta == 0);

        // Disabled zones drop out, and a broken id becomes a row with an error rather than an exception.
        var mixed = new List<TimeZoneEntry>
        {
            new TimeZoneEntry { Label = "JST", Id = "Asia/Tokyo", Enabled = false },
            TimeZoneEntry.Iana("NOPE", "Mars/Olympus_Mons"),
            TimeZoneEntry.Iana("ET", "America/New_York"),
        };
        var mixedRows = Converter.Convert(stamp, baseZone, mixed);
        Check("disabled zone is skipped", mixedRows.Count, 2);
        Assert("bad id becomes an error row", !mixedRows[0].Ok && mixedRows[0].Error.Contains("unknown zone", StringComparison.Ordinal));
        Assert("good zone still renders", mixedRows[1].Ok && mixedRows[1].Time(opts) == "07:00");

        var withNull = new List<TimeZoneEntry?>
        {
            null,
            TimeZoneEntry.Iana("ET", "America/New_York"),
        };
        var nullSafe = Converter.Convert(stamp, baseZone, withNull!);
        Check("null zone entries are skipped", nullSafe.Count, 1);
        Assert("row after null still renders", nullSafe[0].Ok && nullSafe[0].Time(opts) == "07:00");
    }

    private static void Formatting()
    {
        var zones = Defaults.CreateZones();
        var baseZone = ZoneResolver.ResolveId("Asia/Tokyo")!;

        var rows = Converter.Convert(new DateTimeOffset(2026, 10, 7, 11, 0, 0, TimeSpan.Zero), baseZone, zones);
        Check("default single line", ChatFormatter.Build(rows, new FormatOptions()),
            "16:30 IST | 20:00 JST | 11:00 UTC | 07:00 ET");

        var opts24 = new FormatOptions { Use24Hour = false };
        Check("12-hour line", ChatFormatter.Build(rows, opts24),
            "4:30 PM IST | 8:00 PM JST | 11:00 AM UTC | 7:00 AM ET");

        var rollRows = Converter.Convert(new DateTimeOffset(2026, 10, 8, 2, 0, 0, TimeSpan.Zero), baseZone, zones);
        Check("roll-over line gets weekday and delta", ChatFormatter.Build(rollRows, new FormatOptions()),
            "07:30 IST | 11:00 JST | 02:00 UTC | 22:00 ET Wed -1d");

        var dateOpts = new FormatOptions { ShowDateWhenDifferent = true };
        Check("date instead of weekday", ChatFormatter.Build(rollRows, dateOpts),
            "07:30 IST | 11:00 JST | 02:00 UTC | 22:00 ET 07 Oct -1d");

        var decorated = new FormatOptions { Prefix = "Raid ", Suffix = " (please confirm)", Separator = " / " };
        Check("prefix, suffix and separator", ChatFormatter.Build(rows, decorated),
            "Raid 16:30 IST / 20:00 JST / 11:00 UTC / 07:00 ET (please confirm)");

        var multiline = new FormatOptions { MultiLine = true };
        Check("multi-line block", ChatFormatter.Build(rows, multiline),
            "16:30 IST\n20:00 JST\n11:00 UTC\n07:00 ET");

        Check("single row text", ChatFormatter.RowText(rows[1], new FormatOptions()), "20:00 JST");

        var broken = new List<ConversionRow>
        {
            rows[0],
            new ConversionRow { Label = "NOPE", Error = "unknown zone \"x\"" },
        };
        Check("error rows are left out of the copied line", ChatFormatter.Build(broken, new FormatOptions()), "16:30 IST");
        Check("all-broken line is empty", ChatFormatter.Build(broken.Skip(1).ToList(), new FormatOptions()), "");
    }

    private static void RoundTrip()
    {
        var ids = new[]
        {
            "Asia/Kolkata", "Asia/Tokyo", "Asia/Seoul", "Asia/Shanghai", "Asia/Singapore", "Asia/Kathmandu",
            "America/New_York", "America/Chicago", "America/Denver", "America/Los_Angeles", "America/Sao_Paulo",
            "Europe/London", "Europe/Berlin", "Europe/Athens", "Australia/Sydney", "Australia/Adelaide",
            "Pacific/Auckland",
        };

        var tested = 0;
        var mismatches = 0;
        var invalidSkipped = 0;
        var ambiguousSkipped = 0;

        foreach (var id in ids)
        {
            var zone = ZoneResolver.ResolveId(id);
            if (zone is null)
            {
                Console.WriteLine($"  FAIL zone {id} did not resolve");
                failures++;
                continue;
            }

            for (var day = 0; day < 365 * 3; day++)
            {
                var date = new DateTime(2024, 1, 1).AddDays(day);
                foreach (var hour in new[] { 0, 1, 2, 3, 6, 12, 18, 23 })
                {
                    var naive = DateTime.SpecifyKind(date.AddHours(hour), DateTimeKind.Unspecified);
                    if (zone.IsInvalidTime(naive))
                    {
                        invalidSkipped++;
                        continue;
                    }

                    if (zone.IsAmbiguousTime(naive))
                    {
                        ambiguousSkipped++;
                        continue;
                    }

                    tested++;
                    if (!Converter.TryResolveInstant(naive, zone, out var instant, out _, out _))
                    {
                        mismatches++;
                        continue;
                    }

                    var back = TimeZoneInfo.ConvertTime(instant, zone);
                    if (back.DateTime != naive) mismatches++;
                }
            }
        }

        checks += tested;
        failures += mismatches;
        Console.WriteLine($"  {tested.ToString("N0", CultureInfo.InvariantCulture)} wall clocks round-tripped across {ids.Length} zones, {mismatches} mismatches");
        Console.WriteLine($"  skipped {invalidSkipped} DST-gap times and {ambiguousSkipped} ambiguous times (by design)");
    }

    // ------------------------------------------------------------------- helpers

    private static string Wall(string input, DateTime today) => Wall(input, today, "yyyy-MM-dd HH:mm");

    private static string Wall(string input, DateTime today, string format)
    {
        if (!TimeInput.TryParse(input, today, out var parsed, out var error))
        {
            return "parse error: " + error;
        }

        return parsed.WallClock.ToString(format, CultureInfo.InvariantCulture);
    }

    private static void CheckReject(string what, string input)
    {
        var ok = !TimeInput.TryParse(input, new DateTime(2026, 10, 7), out _, out var error);
        checks++;
        if (ok)
        {
            Console.WriteLine($"  ok   reject {what} (\"{error}\")");
            return;
        }

        failures++;
        Console.WriteLine($"  FAIL reject {what}: it parsed");
    }

    private static void Section(string name) => Console.WriteLine($"\n== {name}");

    private static void Check(string what, object? actual, object? expected)
    {
        checks++;
        var a = Text(actual);
        var e = Text(expected);
        if (a == e)
        {
            Console.WriteLine($"  ok   {what}: {a}");
            return;
        }

        failures++;
        Console.WriteLine($"  FAIL {what}: got {a}, expected {e}");
    }

    private static void Assert(string what, bool condition)
    {
        checks++;
        if (condition)
        {
            Console.WriteLine($"  ok   {what}");
            return;
        }

        failures++;
        Console.WriteLine($"  FAIL {what}");
    }

    private static string Text(object? value) => value switch
    {
        null => "<null>",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "<null>",
    };

    /// <summary>
    /// Writes zone / unix-second / local wall clock / offset minutes as TSV, straight through the shipped
    /// resolver, for comparison against another timezone implementation (tools/reference.mjs).
    /// </summary>
    private static void DumpGrid(string path)
    {
        var ids = new[]
        {
            "Asia/Kolkata", "Asia/Tokyo", "Asia/Seoul", "Asia/Shanghai", "Asia/Singapore", "Asia/Kathmandu",
            "America/New_York", "America/Chicago", "America/Denver", "America/Los_Angeles", "America/Sao_Paulo",
            "Europe/London", "Europe/Berlin", "Europe/Athens", "Australia/Sydney", "Australia/Adelaide",
            "Pacific/Auckland", "Pacific/Honolulu",
        };

        var start = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var lines = 0;

        using var writer = new StreamWriter(path);
        foreach (var id in ids)
        {
            var zone = ZoneResolver.ResolveId(id);
            if (zone is null)
            {
                Console.WriteLine($"UNRESOLVED {id}");
                continue;
            }

            for (var instant = start; instant < end; instant = instant.AddHours(1))
            {
                var local = TimeZoneInfo.ConvertTime(instant, zone);
                writer.Write(id);
                writer.Write('\t');
                writer.Write(instant.ToUnixTimeSeconds());
                writer.Write('\t');
                writer.Write(TimeFmt.Sortable(local));
                writer.Write('\t');
                writer.Write((int)local.Offset.TotalMinutes);
                writer.Write('\n');
                lines++;
            }
        }

        Console.WriteLine($"wrote {lines:N0} rows for {ids.Length} zones to {path}");
    }
}
