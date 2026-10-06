using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace TimeShiftXIV;

/// <summary>
/// The main window. Nothing in this class touches the game: no hooks, no native pointers, no game memory.
/// The worst it can do is draw badly, which is the point of keeping the conversion in TimeConversion.cs.
/// </summary>
public sealed class TimeShiftWindow : Window, IDisposable
{
    // Deep indigo with a cyan accent - cold enough to sit next to the game's warm UI without clashing.
    private static readonly Vector4 Accent = new(0.24f, 0.76f, 0.94f, 1f);
    private static readonly Vector4 AccentHover = new(0.36f, 0.86f, 1f, 1f);
    private static readonly Vector4 OnAccent = new(0.03f, 0.06f, 0.10f, 1f);
    private static readonly Vector4 AheadDay = new(0.96f, 0.70f, 0.28f, 1f);
    private static readonly Vector4 BehindDay = new(0.62f, 0.64f, 0.98f, 1f);
    private static readonly Vector4 Idle = new(0.13f, 0.15f, 0.22f, 1f);
    private static readonly Vector4 IdleHover = new(0.20f, 0.23f, 0.33f, 1f);
    private static readonly Vector4 TextMain = new(0.95f, 0.96f, 1f, 1f);
    private static readonly Vector4 TextDim = new(0.60f, 0.65f, 0.78f, 1f);
    private static readonly Vector4 Danger = new(0.95f, 0.44f, 0.44f, 1f);

    private const float FlashSeconds = 2.5f;

    private readonly Configuration config;
    private readonly Action<string> requestCopy;
    private readonly Action openSettings;

    private string input = string.Empty;
    private bool liveNow = true;
    private string note = string.Empty;
    private bool noteIsError;
    private string echo = string.Empty;
    private string flash = string.Empty;
    private double flashUntil;

    public TimeShiftWindow(Configuration config, Action<string> requestCopy, Action openSettings)
        : base("TimeShift###TimeShiftXIVMain")
    {
        this.config = config;
        this.requestCopy = requestCopy;
        this.openSettings = openSettings;

        Flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.AlwaysAutoResize;
        Size = new Vector2(408, 0) * ImGuiHelpers.GlobalScale;
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    /// <summary>Used by /tz when the argument could not be read, so the user sees why instead of a silent no-op.</summary>
    public void ShowError(string message, string source)
    {
        input = source;
        liveNow = false;
        note = message;
        noteIsError = true;
        echo = string.Empty;
        IsOpen = true;
    }

    public override void Draw()
    {
        using var padding = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(12, 12) * ImGuiHelpers.GlobalScale);
        using var rounding = ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, 6 * ImGuiHelpers.GlobalScale);

        var baseZone = ZoneResolver.ResolveBaseOrLocal(config.BaseZone);

        DrawHeader();
        ImGui.Spacing();
        DrawModeRow();
        if (!liveNow) DrawInputRow();
        DrawBaseZoneRow();

        ImGui.Spacing();
        var rows = BuildRows(baseZone);
        DrawResults(rows);

        ImGui.Spacing();
        DrawFooter(rows);
    }

    private void DrawHeader()
    {
        ImGui.SetWindowFontScale(1.15f);
        using (ImRaii.PushColor(ImGuiCol.Text, TextMain))
            ImGui.TextUnformatted("TimeShift");
        ImGui.SetWindowFontScale(1f);

        var label = "Settings";
        var width = ImGui.CalcTextSize(label).X + 18 * ImGuiHelpers.GlobalScale;
        ImGui.SameLine();
        ImGui.SetCursorPosX(ImGui.GetWindowContentRegionMax().X - width);

        using (ImRaii.PushColor(ImGuiCol.Button, Idle))
        using (ImRaii.PushColor(ImGuiCol.ButtonHovered, IdleHover))
        using (ImRaii.PushColor(ImGuiCol.ButtonActive, IdleHover))
        using (ImRaii.PushColor(ImGuiCol.Text, TextDim))
        {
            if (ImGui.Button($"{label}##openSettings", new Vector2(width, 0))) openSettings();
        }
    }

    private void DrawModeRow()
    {
        var width = (ImGui.GetContentRegionAvail().X - 6 * ImGuiHelpers.GlobalScale) / 2f;
        ModeButton("Live now##modeNow", liveNow, width, () => liveNow = true);
        ImGui.SameLine();
        ModeButton("Custom time##modeCustom", !liveNow, width, () => liveNow = false);
    }

    private static void ModeButton(string label, bool active, float width, Action onClick)
    {
        using (ImRaii.PushColor(ImGuiCol.Button, active ? Accent : Idle))
        using (ImRaii.PushColor(ImGuiCol.ButtonHovered, active ? AccentHover : IdleHover))
        using (ImRaii.PushColor(ImGuiCol.ButtonActive, AccentHover))
        using (ImRaii.PushColor(ImGuiCol.Text, active ? OnAccent : TextDim))
        {
            if (ImGui.Button(label, new Vector2(width, 0)) && !active) onClick();
        }
    }

    private void DrawInputRow()
    {
        ImGui.Spacing();
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##tzinput", "20:00  ·  8pm  ·  Wed 21:00  ·  2026-10-07 20:00", ref input, 64);
    }

    private void DrawBaseZoneRow()
    {
        using (ImRaii.PushColor(ImGuiCol.Text, TextDim))
            ImGui.TextUnformatted("Times are read in");

        UiHelpers.BaseZoneCombo(config, "##tzbase");
    }

    private List<ConversionRow> BuildRows(TimeZoneInfo baseZone)
    {
        var now = DateTimeOffset.UtcNow;

        if (liveNow)
        {
            note = string.Empty;
            noteIsError = false;
            echo = TimeFmt.Full(TimeZoneInfo.ConvertTime(now, baseZone), config.Format.Use24Hour);
            return Converter.Convert(now, baseZone, config.Zones);
        }

        if (input.Trim().Length == 0)
        {
            note = "Type a time - 20:00, 8pm, Wed 21:00 or 2026-10-07 20:00.";
            noteIsError = false;
            echo = string.Empty;
            return new List<ConversionRow>();
        }

        if (!Converter.TryBuildRows(input, now, config.BaseZone, config.Zones,
                out var rows, out var parsed, out var ambiguous, out var error))
        {
            note = error;
            noteIsError = true;
            echo = string.Empty;
            return new List<ConversionRow>();
        }

        note = ambiguous ? "Careful: the clocks change around that time in the base zone." : string.Empty;
        noteIsError = false;
        echo = parsed.Echo;
        return rows;
    }

    private void DrawResults(List<ConversionRow> rows)
    {
        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.PadOuterX;
        if (!ImGui.BeginTable("##tzrows", 3, flags)) return;

        ImGui.TableSetupColumn("Zone", ImGuiTableColumnFlags.WidthFixed, 96 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##copy", ImGuiTableColumnFlags.WidthFixed, 62 * ImGuiHelpers.GlobalScale);

        for (var i = 0; i < rows.Count; i++) DrawResultRow(rows[i], i);

        ImGui.EndTable();
    }

    private void DrawResultRow(ConversionRow row, int index)
    {
        ImGui.TableNextRow();

        ImGui.TableSetColumnIndex(0);
        var labelColour = !row.Ok ? Danger : row.IsBase ? Accent : TextDim;
        using (ImRaii.PushColor(ImGuiCol.Text, labelColour))
            ImGui.TextUnformatted(row.Label);

        ImGui.TableSetColumnIndex(1);
        if (!row.Ok)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Danger))
                ImGui.TextWrapped(row.Error);
        }
        else
        {
            ImGui.SetWindowFontScale(1.25f);
            using (ImRaii.PushColor(ImGuiCol.Text, TextMain))
                ImGui.TextUnformatted(row.Time(config.Format));
            ImGui.SetWindowFontScale(1f);

            if (row.DeltaText.Length > 0)
            {
                ImGui.SameLine();
                using (ImRaii.PushColor(ImGuiCol.Text, row.DayDelta > 0 ? AheadDay : BehindDay))
                    ImGui.TextUnformatted(row.DeltaText);
            }
        }

        ImGui.TableSetColumnIndex(2);
        if (!row.Ok) return;

        using (ImRaii.PushColor(ImGuiCol.Button, Idle))
        using (ImRaii.PushColor(ImGuiCol.ButtonHovered, IdleHover))
        using (ImRaii.PushColor(ImGuiCol.ButtonActive, IdleHover))
        using (ImRaii.PushColor(ImGuiCol.Text, Accent))
        {
            if (ImGui.Button($"Copy##tzcopy{index}", new Vector2(-1, 0)))
            {
                var text = ChatFormatter.RowText(row, config.Format);
                RequestCopy(text, text);
            }
        }
    }

    private void DrawFooter(List<ConversionRow> rows)
    {
        var copyable = rows.Exists(r => r.Ok);
        var text = copyable ? ChatFormatter.Build(rows, config.Format) : string.Empty;

        using (ImRaii.Disabled(!copyable))
        using (ImRaii.PushColor(ImGuiCol.Button, Accent))
        using (ImRaii.PushColor(ImGuiCol.ButtonHovered, AccentHover))
        using (ImRaii.PushColor(ImGuiCol.ButtonActive, AccentHover))
        using (ImRaii.PushColor(ImGuiCol.Text, OnAccent))
        {
            if (ImGui.Button("Copy for chat", new Vector2(-1, 28 * ImGuiHelpers.GlobalScale)) && copyable)
                RequestCopy(text, text);
        }

        if (flash.Length > 0 && ImGui.GetTime() < flashUntil)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Accent))
                ImGui.TextWrapped(flash);
        }
        else if (note.Length > 0)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, noteIsError ? Danger : TextDim))
                ImGui.TextWrapped(note);
        }

        if (echo.Length > 0)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, TextDim))
                ImGui.TextUnformatted(echo);
        }
    }

    private void RequestCopy(string text, string flashText)
    {
        if (text.Length == 0) return;

        requestCopy(text);

        var flat = flashText.Replace('\n', ' ');
        flash = "Copied: " + (flat.Length > 68 ? flat[..65] + "..." : flat);
        flashUntil = ImGui.GetTime() + FlashSeconds;
    }

    public void Dispose() { }
}
