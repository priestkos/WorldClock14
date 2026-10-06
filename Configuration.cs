using System;
using System.Collections.Generic;
using Dalamud.Configuration;

namespace TimeShiftXIV;

/// <summary>
/// Everything is a preference: which zones the window shows, the zone the typed time is read in,
/// and how the copied line is formatted. Nothing here is ever written back into the game.
/// </summary>
[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    /// <summary>Target zones, in the order they appear in the window and in the copied line.</summary>
    public List<TimeZoneEntry> Zones { get; set; } = Defaults.CreateZones();

    /// <summary>The zone a typed time is read as. Defaults to whatever Windows is set to.</summary>
    public TimeZoneEntry BaseZone { get; set; } = TimeZoneEntry.Local("Local");

    public FormatOptions Format { get; set; } = new();

    /// <summary>Put the converted line on the clipboard when /tz is given a time as well as the window button.</summary>
    public bool CopyFromCommand { get; set; } = true;

    /// <summary>Print the converted line to /xllog when /tz is given a time.</summary>
    public bool LogFromCommand { get; set; } = true;

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);

    /// <summary>
    /// Repairs anything a hand-edited or half-written config could be missing, so the window can never
    /// be left with a null collection to draw from.
    /// </summary>
    public void Normalize()
    {
        if (Zones is null || Zones.Count == 0) Zones = Defaults.CreateZones();

        foreach (var zone in Zones)
        {
            if (zone is null) continue;
            zone.Label ??= string.Empty;
            zone.Id ??= string.Empty;
        }

        BaseZone ??= TimeZoneEntry.Local("Local");
        BaseZone.Label ??= string.Empty;
        BaseZone.Id ??= string.Empty;

        Format ??= new FormatOptions();
        Format.Separator ??= " | ";
        Format.Prefix ??= string.Empty;
        Format.Suffix ??= string.Empty;
    }
}
