using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace WorldClock14;

/// <summary>Zone list, base zone and the shape of the copied line. Saves on every discrete edit and on close.</summary>
public sealed class ConfigWindow : Window, IDisposable
{
    private static readonly Vector4 Accent = new(0.24f, 0.76f, 0.94f, 1f);
    private static readonly Vector4 Idle = new(0.13f, 0.15f, 0.22f, 1f);
    private static readonly Vector4 IdleHover = new(0.20f, 0.23f, 0.33f, 1f);
    private static readonly Vector4 TextDim = new(0.60f, 0.65f, 0.78f, 1f);
    private static readonly Vector4 Danger = new(0.95f, 0.44f, 0.44f, 1f);

    private readonly Configuration config;
    private readonly Action<string>? requestCopy;

    private int presetIndex;
    private bool customMode;
    private string customLabel = "Zone";
    private string customId = "Asia/Tokyo";

    public ConfigWindow(Configuration config, Action<string>? requestCopy = null)
        : base("TimeShift settings###WorldClock14Config")
    {
        this.config = config;
        this.requestCopy = requestCopy;

        Flags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize;
        Size = new Vector2(560, 0) * ImGuiHelpers.GlobalScale;
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        using var padding = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(12, 12) * ImGuiHelpers.GlobalScale);
        using var rounding = ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, 6 * ImGuiHelpers.GlobalScale);

        DrawZones();
        ImGui.Spacing();
        DrawAddZone();
        ImGui.Spacing();
        DrawBaseZone();
        ImGui.Spacing();
        DrawFormat();
        ImGui.Spacing();
        DrawPreview();
        ImGui.Spacing();
        DrawFooter();
    }

    // ------------------------------------------------------------------ zones

    private void DrawZones()
    {
        using (ImRaii.PushColor(ImGuiCol.Text, Accent))
            ImGui.TextUnformatted("Zones");
        using (ImRaii.PushColor(ImGuiCol.Text, TextDim))
            ImGui.TextUnformatted("Order is the order they appear in the copied line.");

        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX;
        if (!ImGui.BeginTable("##tzconfigzones", 5, flags)) return;

        ImGui.TableSetupColumn("On", ImGuiTableColumnFlags.WidthFixed, 26 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Label", ImGuiTableColumnFlags.WidthFixed, 96 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Zone", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("Now", ImGuiTableColumnFlags.WidthFixed, 78 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("##row", ImGuiTableColumnFlags.WidthFixed, 96 * ImGuiHelpers.GlobalScale);

        for (var i = 0; i < config.Zones.Count; i++) DrawZoneRow(i);

        ImGui.EndTable();
    }

    private void DrawZoneRow(int index)
    {
        if (index < 0 || index >= config.Zones.Count) return;
        var zone = config.Zones[index];
        if (zone is null) return;

        ImGui.TableNextRow();

        ImGui.TableSetColumnIndex(0);
        ImGui.PushID(index);
        var enabled = zone.Enabled;
        if (ImGui.Checkbox("##enabled", ref enabled))
        {
            zone.Enabled = enabled;
            Save();
        }
        ImGui.PopID();

        ImGui.TableSetColumnIndex(1);
        ImGui.PushID(index);
        ImGui.SetNextItemWidth(-1);
        var label = zone.Label;
        if (ImGui.InputText("##label", ref label, 24)) zone.Label = label;
        if (ImGui.IsItemDeactivatedAfterEdit()) Save();

        ImGui.TableSetColumnIndex(2);
        if (zone.IsFixed)
        {
            ImGui.SetNextItemWidth(-1);
            var minutes = zone.FixedOffsetMinutes;
            if (ImGui.InputInt("##minutes", ref minutes, 15, 60))
                zone.FixedOffsetMinutes = ZoneResolver.ClampFixedOffset(minutes);
            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                zone.FixedOffsetMinutes = ZoneResolver.ClampFixedOffset(zone.FixedOffsetMinutes);
                Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Fixed offset from UTC in minutes (330 = +05:30). Clamped to ±14 hours. Fixed zones never shift for DST.");
        }
        else if (zone.IsLocal)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, TextDim))
                ImGui.TextUnformatted("system local");
        }
        else
        {
            ImGui.SetNextItemWidth(-1);
            var id = zone.Id;
            if (ImGui.InputText("##zoneid", ref id, 64)) zone.Id = id;
            if (ImGui.IsItemDeactivatedAfterEdit()) Save();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("IANA id (Asia/Tokyo) or Windows id (Tokyo Standard Time).");

            var resolved = ZoneResolver.ResolveId(zone.Id);
            if (resolved is null && zone.Id.Trim().Length > 0)
            {
                ImGui.SameLine();
                using (ImRaii.PushColor(ImGuiCol.Text, Danger))
                    ImGui.TextUnformatted("unknown zone");
            }
        }

        ImGui.TableSetColumnIndex(3);
        var live = ZoneResolver.Resolve(zone);
        if (live is null)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Danger))
                ImGui.TextUnformatted("--");
        }
        else
        {
            var offset = live.GetUtcOffset(DateTimeOffset.UtcNow);
            using (ImRaii.PushColor(ImGuiCol.Text, TextDim))
                ImGui.TextUnformatted((offset < TimeSpan.Zero ? "-" : "+") + offset.Duration().ToString(@"hh\:mm"));
        }

        ImGui.TableSetColumnIndex(4);
        DrawRowButtons(index);
        ImGui.PopID();
    }

    private void DrawRowButtons(int index)
    {
        const float width = 28f;

        using (ImRaii.PushColor(ImGuiCol.Button, Idle))
        using (ImRaii.PushColor(ImGuiCol.ButtonHovered, IdleHover))
        {
            using (ImRaii.Disabled(index == 0))
            {
                if (ImGui.Button("^##up", new Vector2(width, 0) * ImGuiHelpers.GlobalScale)) Move(index, index - 1);
            }

            ImGui.SameLine();

            using (ImRaii.Disabled(index >= config.Zones.Count - 1))
            {
                if (ImGui.Button("v##down", new Vector2(width, 0) * ImGuiHelpers.GlobalScale)) Move(index, index + 1);
            }

            ImGui.SameLine();

            using (ImRaii.PushColor(ImGuiCol.Text, Danger))
            {
                if (ImGui.Button("X##del", new Vector2(width, 0) * ImGuiHelpers.GlobalScale)) RemoveAt(index);
            }
        }
    }

    private void Move(int from, int to)
    {
        if (to < 0 || to >= config.Zones.Count) return;
        (config.Zones[from], config.Zones[to]) = (config.Zones[to], config.Zones[from]);
        Save();
    }

    private void RemoveAt(int index)
    {
        config.Zones.RemoveAt(index);
        Save();
    }

    // ----------------------------------------------------------------- add zone

    private void DrawAddZone()
    {
        var presets = Defaults.Presets();

        using (ImRaii.PushColor(ImGuiCol.Text, Accent))
            ImGui.TextUnformatted("Add a zone");

        ImGui.SetNextItemWidth(260 * ImGuiHelpers.GlobalScale);
        var preview = customMode
            ? "Custom..."
            : $"{presets[presetIndex].Label}  ({presets[presetIndex].Describe()})";
        if (ImGui.BeginCombo("##tzpreset", preview))
        {
            for (var i = 0; i < presets.Count; i++)
            {
                var name = $"{presets[i].Label}  ({presets[i].Describe()})";
                if (ImGui.Selectable($"{name}##preset{i}", !customMode && i == presetIndex))
                {
                    presetIndex = i;
                    customMode = false;
                }
            }

            if (ImGui.Selectable("Custom...", customMode)) customMode = true;
            ImGui.EndCombo();
        }

        ImGui.SameLine();
        if (ImGui.Button("Add"))
        {
            if (customMode)
            {
                if (customId.Trim().Length > 0)
                {
                    config.Zones.Add(TimeZoneEntry.Iana(customLabel.Trim(), customId.Trim()));
                    Save();
                }
            }
            else
            {
                config.Zones.Add(Defaults.FromPreset(presets[presetIndex]));
                Save();
            }
        }

        if (customMode)
        {
            ImGui.SetNextItemWidth(140 * ImGuiHelpers.GlobalScale);
            ImGui.InputText("Label##customlabel", ref customLabel, 24);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(-1);
            ImGui.InputText("Zone id##customid", ref customId, 64);
            if (customId.Trim().Length > 0 && ZoneResolver.ResolveId(customId) is null)
            {
                using (ImRaii.PushColor(ImGuiCol.Text, Danger))
                    ImGui.TextUnformatted($"\"{customId}\" is not a zone id this machine knows.");
            }
        }
    }

    // ---------------------------------------------------------------- base zone

    private void DrawBaseZone()
    {
        using (ImRaii.PushColor(ImGuiCol.Text, Accent))
            ImGui.TextUnformatted("Base zone");
        using (ImRaii.PushColor(ImGuiCol.Text, TextDim))
            ImGui.TextUnformatted("A time you type is read as being in this zone.");

        UiHelpers.BaseZoneCombo(config, "##tzconfigbase");
    }

    // ------------------------------------------------------------------- format

    private void DrawFormat()
    {
        using (ImRaii.PushColor(ImGuiCol.Text, Accent))
            ImGui.TextUnformatted("Copied line");

        var use24 = config.Format.Use24Hour;
        if (ImGui.Checkbox("24-hour times##f24", ref use24))
        {
            config.Format.Use24Hour = use24;
            Save();
        }

        var day = config.Format.ShowDayWhenDifferent;
        if (ImGui.Checkbox("Add the weekday when a zone lands on another day##fday", ref day))
        {
            config.Format.ShowDayWhenDifferent = day;
            Save();
        }

        var dateInstead = config.Format.ShowDateWhenDifferent;
        if (ImGui.Checkbox("Add the date instead of the weekday##fdate", ref dateInstead))
        {
            config.Format.ShowDateWhenDifferent = dateInstead;
            Save();
        }

        var multi = config.Format.MultiLine;
        if (ImGui.Checkbox("One zone per line##fmulti", ref multi))
        {
            config.Format.MultiLine = multi;
            Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("FFXIV's chat box takes the single-line version best.");

        ImGui.SetNextItemWidth(120 * ImGuiHelpers.GlobalScale);
        var separator = config.Format.Separator;
        if (ImGui.InputText("Separator##fsep", ref separator, 16)) config.Format.Separator = separator;
        if (ImGui.IsItemDeactivatedAfterEdit()) Save();

        ImGui.SameLine();
        ImGui.SetNextItemWidth(90 * ImGuiHelpers.GlobalScale);
        var prefix = config.Format.Prefix;
        if (ImGui.InputText("Prefix##fpre", ref prefix, 32)) config.Format.Prefix = prefix;
        if (ImGui.IsItemDeactivatedAfterEdit()) Save();

        ImGui.SameLine();
        ImGui.SetNextItemWidth(90 * ImGuiHelpers.GlobalScale);
        var suffix = config.Format.Suffix;
        if (ImGui.InputText("Suffix##fsuf", ref suffix, 32)) config.Format.Suffix = suffix;
        if (ImGui.IsItemDeactivatedAfterEdit()) Save();
    }

    // ------------------------------------------------------------------ preview

    private void DrawPreview()
    {
        using (ImRaii.PushColor(ImGuiCol.Text, Accent))
            ImGui.TextUnformatted("Preview");

        var baseZone = ZoneResolver.ResolveBaseOrLocal(config.BaseZone);
        var today = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, baseZone).Date;
        var sample = today.AddHours(20);

        string header;
        string preview;

        if (Converter.TryResolveInstant(sample, baseZone, out var instant, out _, out var error))
        {
            var rows = Converter.Convert(instant, baseZone, config.Zones);
            header = TimeFmt.Full(TimeZoneInfo.ConvertTime(instant, baseZone), config.Format.Use24Hour)
                     + "  in " + config.BaseZone.DisplayLabel;
            preview = ChatFormatter.Build(rows, config.Format);
        }
        else
        {
            header = "Can't preview right now.";
            preview = error;
        }

        using (ImRaii.PushColor(ImGuiCol.Text, TextDim))
            ImGui.TextUnformatted(header);

        using (ImRaii.PushColor(ImGuiCol.ChildBg, new Vector4(0.06f, 0.07f, 0.11f, 0.9f)))
        using (var child = ImRaii.Child("##preview", new Vector2(0, 54 * ImGuiHelpers.GlobalScale), true))
        {
            if (child.Success)
            {
                if (preview.Length == 0)
                {
                    using (ImRaii.PushColor(ImGuiCol.Text, TextDim))
                        ImGui.TextUnformatted("No enabled zones - add one above.");
                }
                else
                {
                    ImGui.TextWrapped(preview);
                }
            }
        }

        if (requestCopy is not null && preview.Length > 0)
        {
            if (ImGui.Button("Copy preview")) requestCopy(preview);
        }
    }

    private void DrawFooter()
    {
        ImGui.Separator();

        if (ImGui.Button("Reset to defaults"))
        {
            config.Zones = Defaults.CreateZones();
            config.BaseZone = TimeZoneEntry.Local("Local");
            config.Format = new FormatOptions();
            Save();
        }

        ImGui.SameLine();
        using (ImRaii.PushColor(ImGuiCol.Text, TextDim))
            ImGui.TextUnformatted(config.BaseZone.IsLocal
                ? "Saved automatically."
                : $"Saved automatically. Base: {config.BaseZone.Describe()}");
    }

    private void Save() => config.Save();

    public override void OnClose() => config.Save();

    public void Dispose() { }
}
