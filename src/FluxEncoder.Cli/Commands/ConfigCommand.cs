using System.CommandLine;
using FluxEncoder.Cli.Hosting;
using FluxEncoder.Cli.Rendering;
using FluxEncoder.Core.Results;
using FluxEncoder.Infrastructure.Configuration;

namespace FluxEncoder.Cli.Commands;

/// <summary>
/// <c>config</c>: where FluxEncoder keeps its files, the effective settings, and a starter settings file.
/// </summary>
internal static class ConfigCommand
{
    public static Command Build(CliHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        var path = new Command("path", "Show where settings, history, and cache files are kept.");
        path.SetAction(_ =>
        {
            var paths = host.Paths;
            host.Out.WriteLine(TerminalText.Safe($"settings  {paths.SettingsFile}"));
            host.Out.WriteLine(TerminalText.Safe($"history   {paths.HistoryDatabase}"));
            host.Out.WriteLine(TerminalText.Safe($"cache     {paths.Cache}"));
            return (int)ExitCode.Success;
        });

        var show = new Command("show", "Print the effective settings as JSON (built-in defaults when there is no settings file).");
        show.SetAction(_ =>
        {
            var (settings, warnings) = SettingsStore.Load(host.Paths.SettingsFile);
            foreach (var warning in warnings)
            {
                host.Error.WriteLine(TerminalText.Safe(warning));
            }

            host.Out.Write(SettingsStore.ToJson(settings));
            return (int)ExitCode.Success;
        });

        var force = new Option<bool>("--force") { Description = "Replace an existing settings file (the old one is kept as settings.json.bak)." };
        var init = new Command("init", "Write a settings file with the defaults spelled out and example presets.") { force };
        init.SetAction(result =>
        {
            var file = host.Paths.SettingsFile;
            if (File.Exists(file) && !result.GetValue(force))
            {
                host.Error.WriteLine(TerminalText.Safe($"{file} already exists. Use --force to replace it (the old file is kept as .bak)."));
                return (int)ExitCode.OutputError;
            }

            SettingsStore.Save(file, SettingsStore.Template);
            host.Out.WriteLine(TerminalText.Safe($"Wrote {file}"));
            return (int)ExitCode.Success;
        });

        var tools = new Command("tools", "Show the resolved FFmpeg and FFprobe binaries and how they were found.");
        tools.SetAction(_ =>
        {
            var (settings, warnings) = SettingsStore.Load(host.Paths.SettingsFile);
            foreach (var warning in warnings)
            {
                host.Error.WriteLine(TerminalText.Safe(warning));
            }

            var discovery = ToolSession.Locate(host, settings.Tools, null, null);
            var ffmpegLine = discovery.FFmpeg is { } ffmpeg
                ? $"{ffmpeg.Path} ({ffmpeg.Source})"
                : "not found";
            var ffprobeLine = discovery.FFprobe is { } ffprobe
                ? $"{ffprobe.Path} ({ffprobe.Source})"
                : "not found";
            host.Out.WriteLine(TerminalText.Safe($"ffmpeg   {ffmpegLine}"));
            host.Out.WriteLine(TerminalText.Safe($"ffprobe  {ffprobeLine}"));
            return discovery.IsComplete ? (int)ExitCode.Success : (int)ExitCode.DependencyMissing;
        });

        return new Command("config", "Show and initialise FluxEncoder's settings.") { path, show, init, tools };
    }
}
