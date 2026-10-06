using System;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace TimeShiftXIV;

public sealed class Plugin : IDalamudPlugin
{
    public string Name => "TimeShiftXIV";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private const string CommandName = "/tz";
    private const string ConfigCommandName = "/tzconfig";

    private readonly Configuration configuration;
    private readonly WindowSystem windowSystem = new("TimeShiftXIV");
    private readonly TimeShiftWindow mainWindow;
    private readonly ConfigWindow configWindow;

    /// <summary>
    /// Copies land here and are written from the Draw handler instead of a command callback, so the
    /// clipboard write always happens inside a frame. Queueing also means /tz works with the window shut.
    /// </summary>
    private string? pendingCopy;

    public Plugin()
    {
        configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        configuration.Normalize();

        mainWindow = new TimeShiftWindow(configuration, QueueCopy, ToggleConfig);
        configWindow = new ConfigWindow(configuration, QueueCopy);

        windowSystem.AddWindow(mainWindow);
        windowSystem.AddWindow(configWindow);

        PluginInterface.UiBuilder.Draw += OnDraw;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMain;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfig;

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Convert a time into your zones. /tz <time> converts and copies; bare /tz opens the window.",
        });

        CommandManager.AddHandler(ConfigCommandName, new CommandInfo((_, _) => ToggleConfig())
        {
            HelpMessage = "TimeShift settings.",
        });

        Log.Info("[TimeShiftXIV] loaded with " + configuration.Zones.Count + " zones, base " + configuration.BaseZone.DisplayLabel + ".");
    }

    public void Dispose()
    {
        CommandManager.RemoveHandler(ConfigCommandName);
        CommandManager.RemoveHandler(CommandName);

        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfig;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMain;
        PluginInterface.UiBuilder.Draw -= OnDraw;

        windowSystem.RemoveAllWindows();
        mainWindow.Dispose();
        configWindow.Dispose();
    }

    private void OnDraw()
    {
        windowSystem.Draw();

        var text = pendingCopy;
        if (text is null) return;

        pendingCopy = null;
        try
        {
            ImGui.SetClipboardText(text);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[TimeShiftXIV] could not write to the clipboard.");
        }
    }

    private void OnCommand(string command, string args)
    {
        args = args.Trim();

        if (args.Length == 0)
        {
            ToggleMain();
            return;
        }

        var lowered = args.ToLowerInvariant();

        if (lowered is "config" or "cfg" or "settings")
        {
            ToggleConfig();
            return;
        }

        if (lowered is "help" or "?" or "-h" or "--help")
        {
            Log.Info("[TimeShiftXIV] /tz opens the window. /tz <time> converts and copies it. /tz now converts this instant. /tzconfig opens settings. Times: 20:00, 8pm, 8:30pm, 2000, 20, Wed 21:00, tomorrow 20:00, 2026-10-07 20:00.");
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var baseZone = ZoneResolver.ResolveBaseOrLocal(configuration.BaseZone);

        if (lowered == "now")
        {
            var rows = Converter.Convert(now, baseZone, configuration.Zones);
            Deliver(rows, TimeFmt.Full(TimeZoneInfo.ConvertTime(now, baseZone), configuration.Format.Use24Hour));
            return;
        }

        if (!Converter.TryBuildRows(args, now, configuration.BaseZone, configuration.Zones,
                out var parsedRows, out var parsed, out _, out var error))
        {
            Log.Warning("[TimeShiftXIV] " + error + " (input: \"" + args + "\")");
            mainWindow.ShowError(error, args);
            return;
        }

        Deliver(parsedRows, parsed.Echo);
    }

    private void Deliver(System.Collections.Generic.List<ConversionRow> rows, string description)
    {
        var text = ChatFormatter.Build(rows, configuration.Format);
        if (text.Length == 0)
        {
            Log.Warning("[TimeShiftXIV] nothing to convert - every zone is disabled or has an unknown id.");
            return;
        }

        if (configuration.CopyFromCommand) QueueCopy(text);
        if (configuration.LogFromCommand) Log.Info("[TimeShiftXIV] " + description + " -> " + text);
    }

    private void QueueCopy(string text) => pendingCopy = text;

    private void ToggleMain() => mainWindow.Toggle();

    private void ToggleConfig() => configWindow.Toggle();
}
