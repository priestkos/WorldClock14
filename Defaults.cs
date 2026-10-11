using System;
using System.Collections.Generic;

namespace WorldClock14;

/// <summary>
/// The seeded zone list and the one-click presets. Pure data with no Dalamud types, so the offline
/// harness compiles this file too and can prove every preset id actually resolves on this machine.
/// </summary>
public static class Defaults
{
    /// <summary>
    /// The four seeded zones. UTC is stored as a fixed offset on purpose: it has no DST, and a fixed
    /// entry cannot be broken by a tz database the host happens to be missing.
    /// </summary>
    public static List<TimeZoneEntry> CreateZones() => new()
    {
        TimeZoneEntry.Iana("IST", "Asia/Kolkata"),
        TimeZoneEntry.Iana("JST", "Asia/Tokyo"),
        TimeZoneEntry.Fixed("UTC", 0),
        TimeZoneEntry.Iana("ET", "America/New_York"),
    };

    /// <summary>Curated spread of the zones an FFXIV player actually coordinates across.</summary>
    public static List<ZonePreset> Presets() => new()
    {
        new ZonePreset("IST", "Asia/Kolkata"),
        new ZonePreset("PKT", "Asia/Karachi"),
        new ZonePreset("GST", "Asia/Dubai"),
        new ZonePreset("MSK", "Europe/Moscow"),
        new ZonePreset("JST", "Asia/Tokyo"),
        new ZonePreset("KST", "Asia/Seoul"),
        new ZonePreset("CST", "Asia/Shanghai"),
        new ZonePreset("HKT", "Asia/Hong_Kong"),
        new ZonePreset("SGT", "Asia/Singapore"),
        new ZonePreset("ICT", "Asia/Bangkok"),
        new ZonePreset("AEST", "Australia/Sydney"),
        new ZonePreset("ACST", "Australia/Adelaide"),
        new ZonePreset("AWST", "Australia/Perth"),
        new ZonePreset("NZST", "Pacific/Auckland"),
        new ZonePreset("UTC", string.Empty, true, 0),
        new ZonePreset("BST", "Europe/London"),
        new ZonePreset("CET", "Europe/Berlin"),
        new ZonePreset("EET", "Europe/Athens"),
        new ZonePreset("ET", "America/New_York"),
        new ZonePreset("CT", "America/Chicago"),
        new ZonePreset("MT", "America/Denver"),
        new ZonePreset("PT", "America/Los_Angeles"),
        new ZonePreset("AKST", "America/Anchorage"),
        new ZonePreset("HST", "Pacific/Honolulu"),
        new ZonePreset("BRT", "America/Sao_Paulo"),
    };

    public static TimeZoneEntry FromPreset(ZonePreset preset) => preset.IsFixed
        ? TimeZoneEntry.Fixed(preset.Label, preset.FixedMinutes)
        : TimeZoneEntry.Iana(preset.Label, preset.Id);
}

/// <summary>A ready-made zone the user can add with one click.</summary>
public sealed record ZonePreset(string Label, string Id, bool IsFixed = false, int FixedMinutes = 0)
{
    /// <summary>How the preset resolves, for the picker: an IANA id, or "+00:00 fixed".</summary>
    public string Describe()
    {
        if (!IsFixed) return Id;
        var sign = FixedMinutes < 0 ? "-" : "+";
        return sign + TimeSpan.FromMinutes(Math.Abs(FixedMinutes)).ToString(@"hh\:mm") + " fixed";
    }
}
