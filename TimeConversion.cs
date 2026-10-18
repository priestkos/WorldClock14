using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace WorldClock14;

/// <summary>
/// One entry in the user's zone list.
/// Deliberately free of every Dalamud type so this whole file compiles into the offline test harness
/// (tests/WorldClock14.LogicTests) and runs with no game, no Dalamud and no dev distrib.
/// </summary>
public sealed class TimeZoneEntry
{
    /// <summary>Shown in the window and copied into chat. E.g. "JST".</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>IANA ("Asia/Tokyo") or Windows ("Tokyo Standard Time") id. Ignored when IsFixed / IsLocal.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Fixed offset from UTC in minutes (IST = 330). Used when IsFixed.</summary>
    public int FixedOffsetMinutes { get; set; }

    /// <summary>Ignore the tz database and use a constant offset that never shifts for DST.</summary>
    public bool IsFixed { get; set; }

    /// <summary>Follow whatever timezone Windows is set to. The base zone defaults to this.</summary>
    public bool IsLocal { get; set; }

    public bool Enabled { get; set; } = true;

    public static TimeZoneEntry Iana(string label, string id) => new() { Label = label, Id = id };
    public static TimeZoneEntry Fixed(string label, int minutes) => new() { Label = label, IsFixed = true, FixedOffsetMinutes = minutes };
    public static TimeZoneEntry Local(string label) => new() { Label = label, IsLocal = true };

    public TimeZoneEntry Clone() => new()
    {
        Label = Label,
        Id = Id,
        FixedOffsetMinutes = FixedOffsetMinutes,
        IsFixed = IsFixed,
        IsLocal = IsLocal,
        Enabled = Enabled,
    };

    /// <summary>How this entry is resolved, for the config UI. E.g. "Asia/Kolkata (system zone)" or "+05:30 fixed".</summary>
    public string Describe()
    {
        if (IsLocal) return "system local";
        if (IsFixed)
        {
            var minutes = ZoneResolver.ClampFixedOffset(FixedOffsetMinutes);
            var sign = minutes < 0 ? "-" : "+";
            return sign + TimeSpan.FromMinutes(Math.Abs(minutes)).ToString(@"hh\:mm") + " fixed";
        }
        return Id;
    }

    public string DisplayLabel => string.IsNullOrWhiteSpace(Label) ? Describe() : Label;
}

/// <summary>Turns a TimeZoneEntry into a real TimeZoneInfo, caching lookups. Never throws for a bad id.</summary>
public static class ZoneResolver
{
    private static readonly Dictionary<string, TimeZoneInfo?> IdCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<int, TimeZoneInfo> FixedCache = new();

    /// <summary>Null when the id is unknown - callers must handle that instead of crashing the window.</summary>
    public static TimeZoneInfo? Resolve(TimeZoneEntry? entry)
    {
        if (entry is null) return null;
        if (entry.IsLocal) return TimeZoneInfo.Local;
        if (entry.IsFixed) return FixedOffset(entry.FixedOffsetMinutes);
        return ResolveId(entry.Id);
    }

    /// <summary>Never-null resolution for the base zone: a broken base id falls back to system local.</summary>
    public static TimeZoneInfo ResolveBaseOrLocal(TimeZoneEntry? entry) => Resolve(entry) ?? TimeZoneInfo.Local;

    public const int MinFixedOffsetMinutes = -14 * 60;
    public const int MaxFixedOffsetMinutes = 14 * 60;

    public static int ClampFixedOffset(int minutes) =>
        Math.Clamp(minutes, MinFixedOffsetMinutes, MaxFixedOffsetMinutes);

    public static TimeZoneInfo FixedOffset(int minutes)
    {
        minutes = ClampFixedOffset(minutes);
        if (FixedCache.TryGetValue(minutes, out var cached)) return cached;

        var sign = minutes < 0 ? "-" : "+";
        var id = "WorldClock14/Fixed" + sign + TimeSpan.FromMinutes(Math.Abs(minutes)).ToString(@"hh\:mm");
        var zone = TimeZoneInfo.CreateCustomTimeZone(id, TimeSpan.FromMinutes(minutes), id, id);
        FixedCache[minutes] = zone;
        return zone;
    }

    /// <summary>
    /// Accepts an IANA id or a Windows id on either platform. .NET resolves IANA ids on Windows through ICU,
    /// but the explicit conversions are kept as a fallback so a host with an odd ICU build still works.
    /// </summary>
    public static TimeZoneInfo? ResolveId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        id = id.Trim();
        if (IdCache.TryGetValue(id, out var cached)) return cached;

        TimeZoneInfo? zone = null;
        if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var direct)) zone = direct;

        if (zone is null)
        {
            try
            {
                if (TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var windowsId) &&
                    TimeZoneInfo.TryFindSystemTimeZoneById(windowsId, out var viaIana))
                {
                    zone = viaIana;
                }
            }
            catch (Exception) { /* ICU-less host: fall through to the Windows-id attempt */ }
        }

        if (zone is null)
        {
            try
            {
                if (TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out var ianaId) &&
                    TimeZoneInfo.TryFindSystemTimeZoneById(ianaId, out var viaWindows))
                {
                    zone = viaWindows;
                }
            }
            catch (Exception) { }
        }

        // A miss is cached too: the tz database does not change while the game is running.
        IdCache[id] = zone;
        return zone;
    }

    /// <summary>Current instant as seen in a zone - the correct way to do "now", with no naive-time round trip.</summary>
    public static DateTimeOffset NowInZone(TimeZoneInfo zone) => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone);
}

/// <summary>A time the user typed: a naive wall-clock date+time, to be read as being in the base zone.</summary>
public sealed class ParsedTime
{
    public DateTime WallClock { get; init; }

    /// <summary>True when the user supplied a date, weekday or "tomorrow" - the UI can then echo it back.</summary>
    public bool HadDate { get; init; }

    public string Echo { get; init; } = string.Empty;
}

/// <summary>
/// Lenient "what you'd type in chat" parser: 20:00, 8pm, 8:30pm, 2000, 20, Wed 21:00, tomorrow 20:00,
/// 2026-10-07 20:00, 10-07 20:00, 2026-10-07T20:00.
/// </summary>
public static class TimeInput
{
    private static readonly Regex RxIsoDate = new(@"^(\d{4})-(\d{1,2})-(\d{1,2})$", RegexOptions.Compiled);
    private static readonly Regex RxShortDate = new(@"^(\d{1,2})-(\d{1,2})$", RegexOptions.Compiled);
    private static readonly Regex RxTimeColon = new(@"^(\d{1,2}):(\d{2})(?::(\d{2}))?(am|pm)?$", RegexOptions.Compiled);
    private static readonly Regex RxHourMeridiem = new(@"^(\d{1,2})(am|pm)$", RegexOptions.Compiled);
    private static readonly Regex RxDigits = new(@"^\d{1,4}$", RegexOptions.Compiled);

    private static readonly Dictionary<string, DayOfWeek> Weekdays = new(StringComparer.Ordinal)
    {
        ["sun"] = DayOfWeek.Sunday, ["sunday"] = DayOfWeek.Sunday,
        ["mon"] = DayOfWeek.Monday, ["monday"] = DayOfWeek.Monday,
        ["tue"] = DayOfWeek.Tuesday, ["tues"] = DayOfWeek.Tuesday, ["tuesday"] = DayOfWeek.Tuesday,
        ["wed"] = DayOfWeek.Wednesday, ["weds"] = DayOfWeek.Wednesday, ["wednesday"] = DayOfWeek.Wednesday,
        ["thu"] = DayOfWeek.Thursday, ["thur"] = DayOfWeek.Thursday, ["thurs"] = DayOfWeek.Thursday, ["thursday"] = DayOfWeek.Thursday,
        ["fri"] = DayOfWeek.Friday, ["friday"] = DayOfWeek.Friday,
        ["sat"] = DayOfWeek.Saturday, ["saturday"] = DayOfWeek.Saturday,
    };

    /// <summary><paramref name="today"/> must be the current date *in the base zone*, so relative words land right.</summary>
    public static bool TryParse(string? raw, DateTime today, out ParsedTime parsed, out string error)
    {
        parsed = new ParsedTime { WallClock = today.Date };
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(raw))
        {
            error = "Type a time - 20:00, 8pm, Wed 21:00 or 2026-10-07 20:00.";
            return false;
        }

        var work = raw.Trim().Replace('T', ' ').Replace('/', '-');
        var tokens = MergeMeridiem(work.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries));

        DateTime? date = null;
        TimeSpan? time = null;
        var hadDate = false;
        var nextWeek = false;

        foreach (var rawToken in tokens)
        {
            var token = rawToken.ToLowerInvariant();
            if (token == "at") continue;
            if (token == "next")
            {
                nextWeek = true;
                continue;
            }

            if (time is null && TryTime(token, out var parsedTime))
            {
                time = parsedTime;
                continue;
            }

            if (date is null && TryWeekday(token, today, nextWeek, out var weekdayDate))
            {
                date = weekdayDate;
                hadDate = true;
                nextWeek = false;
                continue;
            }

            if (date is null && TryDate(token, today, out var calendarDate))
            {
                date = calendarDate;
                hadDate = true;
                continue;
            }

            error = $"I don't understand \"{rawToken}\".";
            return false;
        }

        if (time is null)
        {
            error = hadDate
                ? "That's a date with no time - add one, e.g. 20:00."
                : "No time found in that.";
            return false;
        }

        var day = (date ?? today).Date;
        var wallClock = day.Add(time.Value);
        parsed = new ParsedTime
        {
            WallClock = wallClock,
            HadDate = hadDate,
            Echo = TimeFmt.Echo(wallClock, use24Hour: true),
        };
        return true;
    }

    /// <summary>"8 pm" arrives as two tokens; fold a standalone am/pm back onto the hour so both spellings work.</summary>
    private static string[] MergeMeridiem(string[] tokens)
    {
        var merged = new List<string>(tokens.Length);
        foreach (var token in tokens)
        {
            var lower = token.ToLowerInvariant();
            if (lower is "am" or "pm" or "a.m." or "p.m." && merged.Count > 0)
            {
                merged[^1] += lower[0] == 'a' ? "am" : "pm";
                continue;
            }

            merged.Add(token);
        }

        return merged.ToArray();
    }

    private static bool TryTime(string token, out TimeSpan time)
    {
        time = default;

        var m = RxTimeColon.Match(token);
        if (m.Success)
        {
            var hour = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            var minute = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            var second = m.Groups[3].Success ? int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) : 0;
            if (!ApplyMeridiem(ref hour, m.Groups[4].Value)) return false;
            if (hour > 23 || minute > 59 || second > 59) return false;
            time = new TimeSpan(hour, minute, second);
            return true;
        }

        m = RxHourMeridiem.Match(token);
        if (m.Success)
        {
            var hour = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            if (!ApplyMeridiem(ref hour, m.Groups[2].Value)) return false;
            time = new TimeSpan(hour, 0, 0);
            return true;
        }

        if (!RxDigits.IsMatch(token)) return false;

        if (token.Length <= 2)
        {
            var hour = int.Parse(token, CultureInfo.InvariantCulture);
            if (hour > 23) return false;
            time = new TimeSpan(hour, 0, 0);
            return true;
        }

        // military / compact: 2000 -> 20:00, 930 -> 09:30
        var packed = int.Parse(token, CultureInfo.InvariantCulture);
        var packedHour = packed / 100;
        var packedMinute = packed % 100;
        if (packedHour > 23 || packedMinute > 59) return false;
        time = new TimeSpan(packedHour, packedMinute, 0);
        return true;
    }

    /// <summary>Empty meridiem means 24-hour input; otherwise the hour must be 1-12.</summary>
    private static bool ApplyMeridiem(ref int hour, string meridiem)
    {
        if (meridiem.Length == 0) return hour <= 23;
        if (hour is < 1 or > 12) return false;
        if (meridiem == "am") hour = hour == 12 ? 0 : hour;
        else hour = hour == 12 ? 12 : hour + 12;
        return true;
    }

    private static bool TryWeekday(string token, DateTime today, bool nextWeek, out DateTime date)
    {
        date = default;
        if (!Weekdays.TryGetValue(token, out var target)) return false;

        var ahead = ((int)target - (int)today.DayOfWeek + 7) % 7;
        // "next" always means the occurrence after this week's, including when that weekday is still ahead.
        if (nextWeek) ahead += 7;
        date = today.Date.AddDays(ahead);
        return true;
    }

    private static bool TryDate(string token, DateTime today, out DateTime date)
    {
        date = default;

        switch (token)
        {
            case "today":
            case "tonight":
                date = today.Date;
                return true;
            case "tomorrow":
            case "tmrw":
            case "tmw":
                date = today.Date.AddDays(1);
                return true;
            case "yesterday":
                date = today.Date.AddDays(-1);
                return true;
        }

        var m = RxIsoDate.Match(token);
        if (m.Success &&
            DateTime.TryParseExact(token, new[] { "yyyy-M-d", "yyyy-MM-dd" }, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out date))
        {
            return true;
        }

        m = RxShortDate.Match(token);
        if (m.Success)
        {
            var month = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            var day = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            if (month is < 1 or > 12 || day is < 1 or > 31) return false;
            try
            {
                date = new DateTime(today.Year, month, day);
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }

            // Bare month-day with no year: if that date already passed this year, take next year.
            if (date < today.Date)
            {
                try
                {
                    date = date.AddYears(1);
                }
                catch (ArgumentOutOfRangeException)
                {
                    return false;
                }
            }

            return true;
        }

        return false;
    }
}

public static class TimeFmt
{
    public static string Time(DateTimeOffset local, bool use24Hour) =>
        use24Hour
            ? local.ToString("HH:mm", CultureInfo.InvariantCulture)
            : local.ToString("h:mm tt", CultureInfo.InvariantCulture);

    /// <summary>Echo of a parsed naive wall clock, respecting the user's 12/24-hour preference.</summary>
    public static string Echo(DateTime wallClock, bool use24Hour) =>
        wallClock.ToString("ddd dd MMM yyyy", CultureInfo.InvariantCulture) + " " +
        (use24Hour
            ? wallClock.ToString("HH:mm", CultureInfo.InvariantCulture)
            : wallClock.ToString("h:mm tt", CultureInfo.InvariantCulture));

    /// <summary>Midnight-exact times read better with the date attached, so the UI uses this for the header.</summary>
    public static string Full(DateTimeOffset local, bool use24Hour) =>
        local.ToString("ddd dd MMM yyyy", CultureInfo.InvariantCulture) + " " + Time(local, use24Hour);

    public static string DayAbbr(DateTimeOffset local) => local.ToString("ddd", CultureInfo.InvariantCulture);

    public static string Date(DateTimeOffset local) => local.ToString("dd MMM", CultureInfo.InvariantCulture);

    /// <summary>Unambiguous wall clock for the offline harness's reference comparison.</summary>
    public static string Sortable(DateTimeOffset local) => local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>"+1d" / "-1d" / "" - the cross-timezone detail that actually causes missed raids.</summary>
    public static string Delta(int dayDelta) =>
        dayDelta > 0 ? $"+{dayDelta}d" : dayDelta < 0 ? $"{dayDelta}d" : string.Empty;
}

public sealed class FormatOptions
{
    public bool Use24Hour { get; set; } = true;

    /// <summary>Append the weekday to a zone whose date has rolled over.</summary>
    public bool ShowDayWhenDifferent { get; set; } = true;

    /// <summary>Append "07 Oct" instead of the weekday when a date has rolled over.</summary>
    public bool ShowDateWhenDifferent { get; set; }

    /// <summary>One zone per line instead of a single joined line. FFXIV's chat box takes the single line best.</summary>
    public bool MultiLine { get; set; }

    public string Separator { get; set; } = " | ";

    public string Prefix { get; set; } = string.Empty;

    public string Suffix { get; set; } = string.Empty;
}

/// <summary>One zone's line in the result. Error is set instead of throwing when a zone id is bad.</summary>
public sealed class ConversionRow
{
    public string Label { get; init; } = string.Empty;

    /// <summary>
    /// The instant as seen in this zone. The time text is rendered from this on demand rather than stored,
    /// so whoever builds the line decides the clock format - a baked-in string silently ignores 12/24-hour later.
    /// </summary>
    public DateTimeOffset Local { get; init; }

    public string DayAbbr { get; init; } = string.Empty;
    public string DateText { get; init; } = string.Empty;
    public string DeltaText { get; init; } = string.Empty;
    public int DayDelta { get; init; }
    public string Error { get; init; } = string.Empty;
    public bool IsBase { get; init; }
    public bool Ok => Error.Length == 0;

    /// <summary>This zone's time, formatted with the caller's options.</summary>
    public string Time(FormatOptions options) => TimeFmt.Time(Local, options.Use24Hour);

    /// <summary>This zone's time, e.g. "20:00" or "8:00 PM".</summary>
    public string Time(bool use24Hour) => TimeFmt.Time(Local, use24Hour);
}

public static class Converter
{
    /// <summary>
    /// Reads a naive wall clock as being in <paramref name="baseZone"/> and returns the real instant.
    /// Refuses (with an explanation) rather than guessing when the time does not exist in that zone -
    /// i.e. the hour a DST change skips.
    /// </summary>
    public static bool TryResolveInstant(DateTime wallClock, TimeZoneInfo baseZone, out DateTimeOffset instant, out bool ambiguous, out string error)
    {
        instant = default;
        ambiguous = false;
        error = string.Empty;

        var naive = DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified);

        if (baseZone.IsInvalidTime(naive))
        {
            error = $"{naive:HH:mm} on {naive:dd MMM} doesn't exist in {baseZone.Id} - the clocks jump forward that day.";
            return false;
        }

        ambiguous = baseZone.IsAmbiguousTime(naive);

        try
        {
            instant = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(naive, baseZone));
            return true;
        }
        catch (ArgumentException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Renders one instant across the enabled zones. Zones are taken in the user's order.</summary>
    public static List<ConversionRow> Convert(DateTimeOffset instant, TimeZoneInfo baseZone, IEnumerable<TimeZoneEntry> zones)
    {
        var baseLocal = TimeZoneInfo.ConvertTime(instant, baseZone);
        var rows = new List<ConversionRow>();

        foreach (var entry in zones)
        {
            if (entry is null || !entry.Enabled) continue;

            var zone = ZoneResolver.Resolve(entry);
            if (zone is null)
            {
                rows.Add(new ConversionRow { Label = entry.DisplayLabel, Error = $"unknown zone \"{entry.Id}\"" });
                continue;
            }

            var local = TimeZoneInfo.ConvertTime(instant, zone);
            var dayDelta = (local.Date - baseLocal.Date).Days;

            rows.Add(new ConversionRow
            {
                Label = entry.DisplayLabel,
                Local = local,
                DayAbbr = TimeFmt.DayAbbr(local),
                DateText = TimeFmt.Date(local),
                DeltaText = TimeFmt.Delta(dayDelta),
                DayDelta = dayDelta,
                IsBase = SameZone(zone, baseZone),
            });
        }

        return rows;
    }

    /// <summary>Builds the rows for the given time text, or returns the parse error.</summary>
    public static bool TryBuildRows(string input, DateTimeOffset now, TimeZoneEntry baseEntry, IEnumerable<TimeZoneEntry> zones,
        out List<ConversionRow> rows, out ParsedTime parsed, out bool ambiguous, out string error)
    {
        rows = new List<ConversionRow>();
        parsed = new ParsedTime();
        ambiguous = false;
        var baseZone = ZoneResolver.ResolveBaseOrLocal(baseEntry);
        var today = TimeZoneInfo.ConvertTime(now, baseZone).Date;

        if (!TimeInput.TryParse(input, today, out parsed, out error)) return false;
        if (!TryResolveInstant(parsed.WallClock, baseZone, out var instant, out ambiguous, out error)) return false;

        rows = Convert(instant, baseZone, zones);
        return true;
    }

    /// <summary>
    /// True when two zones are the same database entry or carry identical rules — so system local
    /// matches its IANA equivalent even when the Ids differ (Windows id vs IANA id).
    /// </summary>
    public static bool SameZone(TimeZoneInfo a, TimeZoneInfo b) =>
        ReferenceEquals(a, b) ||
        string.Equals(a.Id, b.Id, StringComparison.OrdinalIgnoreCase) ||
        a.HasSameRules(b);
}

public static class ChatFormatter
{
    /// <summary>One zone's copyable text, e.g. "20:00 JST" or "06:30 ET +1d".</summary>
    public static string RowText(ConversionRow row, FormatOptions options)
    {
        if (!row.Ok) return $"{row.Label} ?";

        var sb = new StringBuilder();
        sb.Append(row.Time(options)).Append(' ').Append(row.Label);

        if (row.DayDelta != 0)
        {
            if (options.ShowDateWhenDifferent) sb.Append(' ').Append(row.DateText);
            else if (options.ShowDayWhenDifferent) sb.Append(' ').Append(row.DayAbbr);
            sb.Append(' ').Append(row.DeltaText);
        }

        return sb.ToString();
    }

    /// <summary>The thing that actually gets pasted into chat.</summary>
    public static string Build(IReadOnlyList<ConversionRow> rows, FormatOptions options)
    {
        var parts = rows.Where(r => r.Ok).Select(r => RowText(r, options)).ToList();
        if (parts.Count == 0) return string.Empty;

        var separator = string.IsNullOrEmpty(options.Separator) ? " | " : options.Separator;
        var body = options.MultiLine ? string.Join("\n", parts) : string.Join(separator, parts);
        return (options.Prefix ?? string.Empty) + body + (options.Suffix ?? string.Empty);
    }

    /// <summary>Row text for a zone, used by the per-zone copy buttons.</summary>
    public static string SingleRow(ConversionRow row, FormatOptions options) => RowText(row, options);
}
