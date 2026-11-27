using System.Diagnostics;

namespace Tiler.X11.Tests;

/// <summary>
/// A private Xvfb display for tests that need server flags the shared display does not have
/// (a RANDR-disabled server for the fallback path). Picks a display nobody answers on, waits
/// until it accepts connections, and kills the server on dispose.
/// </summary>
internal sealed class XvfbServer : IDisposable
{
    private readonly Process _process;
    private readonly int _number;

    private XvfbServer(Process process, int number)
    {
        _process = process;
        _number = number;
        Display = $":{number}";
    }

    public string Display { get; }

    public static bool IsAvailable => (Environment.GetEnvironmentVariable("PATH") ?? "")
        .Split(':', StringSplitOptions.RemoveEmptyEntries)
        .Any(directory => File.Exists(Path.Combine(directory, "Xvfb")));

    public static XvfbServer Start(int width, int height, params string[] extraArguments)
    {
        for (var number = 90; number < 200; number++)
        {
            // A display that answers belongs to someone else (possibly the shared xvfb-run
            // display, which has RANDR): never take it over.
            if (CanConnect($":{number}"))
            {
                continue;
            }

            var start = new ProcessStartInfo("Xvfb") { RedirectStandardError = true };
            // -ac: the caller's XAUTHORITY (xvfb-run's cookie) does not match this server.
            foreach (var argument in new[] { $":{number}", "-screen", "0", $"{width}x{height}x24", "-nolisten", "tcp", "-ac" })
            {
                start.ArgumentList.Add(argument);
            }

            foreach (var argument in extraArguments)
            {
                start.ArgumentList.Add(argument);
            }

            var process = Process.Start(start) ?? throw new InvalidOperationException("Xvfb did not start");
            if (WaitForDisplay(process, number))
            {
                return new XvfbServer(process, number);
            }

            process.Kill();
            process.Dispose();
        }

        throw new InvalidOperationException("could not start Xvfb on any display between :90 and :199");
    }

    public void Dispose()
    {
        if (!_process.HasExited)
        {
            _process.Kill();
            _process.WaitForExit(2000);
        }

        _process.Dispose();
        // SIGKILL leaves the lock file behind, which would make the number unusable next run.
        try
        {
            File.Delete($"/tmp/.X{_number}-lock");
        }
        catch (IOException)
        {
            // not ours to remove any more; the next run just picks another number
        }
    }

    private static bool WaitForDisplay(Process process, int number)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (process.HasExited)
            {
                return false;
            }

            if (CanConnect($":{number}"))
            {
                return true;
            }

            Thread.Sleep(50);
        }

        return false;
    }

    private static bool CanConnect(string display)
    {
        try
        {
            using var wire = X11Wire.Open(display);
            return wire.Root != 0;
        }
        catch (BackendException)
        {
            return false;
        }
    }
}
