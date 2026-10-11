using System;
using Dalamud.Bindings.ImGui;

namespace WorldClock14;

/// <summary>Small pieces of UI both windows need. Kept here so the two never drift apart.</summary>
internal static class UiHelpers
{
    /// <summary>
    /// Zone picker for "the time I type is read in". Returns true when the choice changed (and has saved).
    /// Selecting a zone copies it rather than aliasing the list entry, so editing one later cannot move the other.
    /// </summary>
    internal static bool BaseZoneCombo(Configuration config, string id)
    {
        var preview = config.BaseZone.DisplayLabel;
        if (!config.BaseZone.IsLocal) preview += $"  ({config.BaseZone.Describe()})";

        ImGui.SetNextItemWidth(-1);
        if (!ImGui.BeginCombo(id, preview)) return false;

        var changed = false;

        if (ImGui.Selectable($"System local##{id}_local", config.BaseZone.IsLocal))
        {
            config.BaseZone = TimeZoneEntry.Local("Local");
            changed = true;
        }

        for (var i = 0; i < config.Zones.Count; i++)
        {
            var zone = config.Zones[i];
            var selected = !config.BaseZone.IsLocal && !config.BaseZone.IsFixed && !zone.IsFixed &&
                           string.Equals(config.BaseZone.Id, zone.Id, StringComparison.OrdinalIgnoreCase);

            if (ImGui.Selectable($"{zone.DisplayLabel}##{id}_zone{i}", selected))
            {
                var copy = zone.Clone();
                copy.IsLocal = false;
                config.BaseZone = copy;
                changed = true;
            }
        }

        ImGui.EndCombo();

        if (changed) config.Save();
        return changed;
    }
}
