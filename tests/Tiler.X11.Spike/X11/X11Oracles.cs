using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Tiler.X11;

internal record XrandrMonitor(string Name, int X, int Y, int Width, int Height);

internal record XwininfoGeometry(int X, int Y, int Width, int Height, int BorderWidth);

/// <summary>
/// Independent read-back: the values xrandr and xwininfo report, parsed here so the spike and
/// the tests compare the backend against the X tools rather than against themselves.
/// </summary>
internal static partial class X11Oracles
{
    public static IReadOnlyList<XrandrMonitor> XrandrMonitors(string? displayName = null)
    {
        var output = Run("xrandr", ["--listmonitors"], displayName);
        var monitors = new List<XrandrMonitor>();
        foreach (var line in output.Split('\n'))
        {
            var match = XrandrMonitorLine().Match(line);
            if (match.Success)
            {
                monitors.Add(new XrandrMonitor(
                    match.Groups["output"].Value,
                    int.Parse(match.Groups["x"].Value, CultureInfo.InvariantCulture),
                    int.Parse(match.Groups["y"].Value, CultureInfo.InvariantCulture),
                    int.Parse(match.Groups["width"].Value, CultureInfo.InvariantCulture),
                    int.Parse(match.Groups["height"].Value, CultureInfo.InvariantCulture)));
            }
        }

        if (monitors.Count == 0)
        {
            throw new InvalidOperationException($"xrandr --listmonitors produced no monitors:\n{output}");
        }

        return monitors;
    }

    public static XwininfoGeometry Xwininfo(ulong windowId, string? displayName = null)
    {
        var output = Run("xwininfo", ["-id", $"0x{windowId:x}"], displayName);
        return new XwininfoGeometry(
            Field(output, "Absolute upper-left X"),
            Field(output, "Absolute upper-left Y"),
            Field(output, "Width"),
            Field(output, "Height"),
            Field(output, "Border width"));
    }

    private static int Field(string output, string name)
    {
        var match = Regex.Match(output, $@"^\s*{Regex.Escape(name)}:\s+(-?\d+)\s*$", RegexOptions.Multiline);
        return match.Success
            ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)
            : throw new InvalidOperationException($"xwininfo output has no '{name}':\n{output}");
    }

    private static string Run(string file, string[] arguments, string? displayName)
    {
        var start = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        if (displayName is not null)
        {
            start.ArgumentList.Add("-display");
            start.ArgumentList.Add(displayName);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"{file} did not start");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode == 0 ? output : throw new InvalidOperationException($"{file} exited {process.ExitCode}:\n{error}");
    }

    // " 0: +*screen 1920/508x1080/286+0+0  screen"
    [GeneratedRegex(@"^\s*\d+:\s+[+*]*(?<output>\S+)\s+(?<width>\d+)/\d+x(?<height>\d+)/\d+\+(?<x>-?\d+)\+(?<y>-?\d+)\s")]
    private static partial Regex XrandrMonitorLine();
}
