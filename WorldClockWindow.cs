using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

namespace WorldClock14;

/// <summary>
/// The main window. Nothing in this class touches the game: no hooks, no native pointers, no game memory.
/// The worst it can do is draw badly, which is the point of keeping the conversion in TimeConversion.cs.
/// </summary>
public sealed class WorldClockWindow : Window, IDisposable
{
    private const float FlashSeconds = 2.5f;

    private readonly Configuration config;
    private readonly Action<string> requestCopy;
    private readonly Action openSettings;

    private string input = string.Empty;
    private bool liveNow = true;
    private string note = string.Empty;
    private string echo = string.Empty;
    private string flash = string.Empty;
    private double flashUntil;

    public WorldClockWindow(Configuration config, Action<string> requestCopy, Action openSettings)
        : base("WorldClock14###WorldClock14Main")
    {
        this.config = config;
        this.requestCopy = requestCopy;
        this.openSettings = openSettings;

        Size = new Vector2(420, 380);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(380, 240),
            MaximumSize = new Vector2(900, 1000),
        };
    }

    /// <summary>Used by /tz when the argument could not be read, so the user sees why instead of a silent no-op.</summary>
    public void ShowError(string message, string source)
    {
        input = source;
        liveNow = false;
        note = message;
        echo = string.Empty;
        IsOpen = true;
    }

    public override void Draw()
    {
        var baseZone = ZoneResolver.ResolveBaseOrLocal(config.BaseZone);

        DrawModeRow();
        if (!liveNow) DrawInputRow();
        DrawBaseZoneRow();

        ImGui.Spacing();
        var rows = BuildRows(baseZone);
        DrawResults(rows);

        ImGui.Spacing();
        DrawFooter(rows);
    }

    private void DrawModeRow()
    {
        if (ImGui.RadioButton("Live now##modeNow", liveNow)) liveNow = true;
        ImGui.SameLine();
        if (ImGui.RadioButton("Custom time##modeCustom", !liveNow)) liveNow = false;

        var label = "Settings";
        var width = ImGui.CalcTextSize(label).X + 18 * ImGuiHelpers.GlobalScale;
        ImGui.SameLine();
        ImGui.SetCursorPosX(ImGui.GetWindowContentRegionMax().X - width);
        if (ImGui.Button($"{label}##openSettings", new Vector2(width, 0))) openSettings();
    }

    private void DrawInputRow()
    {
        ImGui.Spacing();
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##tzinput", "20:00  ·  8pm  ·  Wed 21:00  ·  2026-10-07 20:00", ref input, 64);
    }

    private void DrawBaseZoneRow()
    {
        ImGui.TextUnformatted("Times are read in");
        UiHelpers.BaseZoneCombo(config, "##tzbase");
    }

    private List<ConversionRow> BuildRows(TimeZoneInfo baseZone)
    {
        var now = DateTimeOffset.UtcNow;

        if (liveNow)
        {
            note = string.Empty;
            echo = TimeFmt.Full(TimeZoneInfo.ConvertTime(now, baseZone), config.Format.Use24Hour);
            return Converter.Convert(now, baseZone, config.Zones);
        }

        if (input.Trim().Length == 0)
        {
            note = "Type a time - 20:00, 8pm, Wed 21:00 or 2026-10-07 20:00.";
            echo = string.Empty;
            return new List<ConversionRow>();
        }

        if (!Converter.TryBuildRows(input, now, config.BaseZone, config.Zones,
                out var rows, out var parsed, out var ambiguous, out var error))
        {
            note = error;
            echo = string.Empty;
            return new List<ConversionRow>();
        }

        note = ambiguous ? "Careful: the clocks change around that time in the base zone." : string.Empty;
        echo = TimeFmt.Echo(parsed.WallClock, config.Format.Use24Hour);
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
        ImGui.TextUnformatted(row.Label);

        ImGui.TableSetColumnIndex(1);
        if (!row.Ok)
        {
            ImGui.TextWrapped(row.Error);
        }
        else
        {
            ImGui.TextUnformatted(row.Time(config.Format));

            if (row.DeltaText.Length > 0)
            {
                ImGui.SameLine();
                ImGui.TextDisabled(row.DeltaText);
            }
        }

        ImGui.TableSetColumnIndex(2);
        if (!row.Ok) return;

        if (ImGui.Button($"Copy##tzcopy{index}", new Vector2(-1, 0)))
        {
            var text = ChatFormatter.RowText(row, config.Format);
            RequestCopy(text, text);
        }
    }

    private void DrawFooter(List<ConversionRow> rows)
    {
        var copyable = rows.Exists(r => r.Ok);
        var text = copyable ? ChatFormatter.Build(rows, config.Format) : string.Empty;

        ImGui.BeginDisabled(!copyable);
        if (ImGui.Button("Copy for chat", new Vector2(-1, 0)) && copyable) RequestCopy(text, text);
        ImGui.EndDisabled();

        if (flash.Length > 0 && ImGui.GetTime() < flashUntil)
        {
            ImGui.TextWrapped(flash);
        }
        else if (note.Length > 0)
        {
            ImGui.TextWrapped(note);
        }

        if (echo.Length > 0) ImGui.TextDisabled(echo);
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
