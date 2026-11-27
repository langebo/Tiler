using System.Runtime.CompilerServices;
using Tiler.X11;
using Monitor = Tiler.X11.Monitor;

// Spike program for issue #5, not feature code. Runs the five X11 operations from
// docs/architecture/backends.md against the display in $DISPLAY (Xvfb) and prints one
// machine-checkable "OPERATION ok ..." line each; any failure exits non-zero immediately.
// Run the published native binary: the AOT guard below rejects a JIT run.
internal static class Program
{
    private static int Main()
    {
        if (RuntimeFeature.IsDynamicCodeSupported || RuntimeFeature.IsDynamicCodeCompiled)
        {
            Console.Error.WriteLine(
                $"FAIL AOT-GUARD: IsDynamicCodeSupported={RuntimeFeature.IsDynamicCodeSupported} " +
                $"IsDynamicCodeCompiled={RuntimeFeature.IsDynamicCodeCompiled}; this is not a native AOT binary");
            return 1;
        }

        Console.WriteLine("AOT-GUARD ok IsDynamicCodeSupported=False IsDynamicCodeCompiled=False");

        try
        {
            Run();
            return 0;
        }
        catch (BackendException error)
        {
            Console.Error.WriteLine($"FAIL backend: {error.Error} (exit {error.ExitCode}): {error.Message}");
            return error.ExitCode;
        }
        catch (SpikeFailure failure)
        {
            Console.Error.WriteLine(failure.Message);
            return 1;
        }
    }

    private static void Run()
    {
        using var wire = X11Wire.Open();
        var backend = new X11SpikeBackend(wire);
        using var windows = new X11Windows(wire);

        // Focus: _NET_ACTIVE_WINDOW is set by the WM; under Xvfb the spike plays that role.
        var csd = windows.CreatePlain(80, 60, 640, 480);
        windows.SetTitle(csd, "TL-105 CSD window");
        windows.SetActiveWindow(0);
        var noFocus = backend.GetFocusedWindow();
        windows.SetActiveWindow(csd);
        var focus = backend.GetFocusedWindow();
        Require("FOCUS",
            noFocus is null && focus is not null && focus.WindowId == (ulong)csd &&
            focus.Title == "TL-105 CSD window" && focus.X == 80 && focus.Y == 60 &&
            focus.Width == 640 && focus.Height == 480,
            $"unset={(noFocus is null ? "null" : "window")} window=0x{focus?.WindowId:x} " +
            $"title=\"{focus?.Title}\" monitor={focus?.MonitorId} " +
            $"rect={focus?.X},{focus?.Y} {focus?.Width}x{focus?.Height}");

        // CSD-ish geometry: reparented onto root, border_width 0, no _NET_FRAME_EXTENTS.
        var csdRect = backend.GetClientRect((ulong)csd);
        var csdFrameOrigin = backend.GetFrameOrigin((ulong)csd);
        Require("GEOMETRY-CSD",
            csdRect == new TileRect(80, 60, 640, 480) && csdFrameOrigin == (80, 60),
            $"client={csdRect} frame-origin={csdFrameOrigin} " +
            $"border={wire.Attributes(csd).BorderWidth} extents=none");

        // Framed geometry: client inside a WM-style frame, _NET_FRAME_EXTENTS 4,20,6,8.
        var (frame, framed) = windows.CreateFramed(400, 300, 500, 400, left: 4, top: 20, right: 6, bottom: 8);
        var framedRect = backend.GetClientRect((ulong)framed);
        var framedOrigin = backend.GetFrameOrigin((ulong)framed);
        Require("GEOMETRY-FRAMED",
            framedRect == new TileRect(404, 320, 500, 400) && framedOrigin == (400, 300),
            $"client={framedRect} frame-origin={framedOrigin} frame=0x{frame:x} " +
            $"extents={string.Join(',', backend.GetFrameExtents((ulong)framed))}");

        // Apply: same absolute request through both window shapes, read back the client area.
        var requested = new TileRect(100, 120, 900, 700);
        backend.ApplyRectangle((ulong)csd, requested);
        var csdApplied = backend.GetClientRect((ulong)csd);
        backend.ApplyRectangle((ulong)framed, requested);
        var framedApplied = backend.GetClientRect((ulong)framed);
        Require("APPLY",
            csdApplied == requested && framedApplied == requested,
            $"requested={requested} csd={csdApplied} framed={framedApplied} " +
            $"framed-origin={backend.GetFrameOrigin((ulong)framed)}");

        // Monitors: RANDR when the server has it, otherwise the EWMH properties (fallback).
        var monitors = backend.GetMonitors();
        if (backend.MonitorSource == "randr")
        {
            var oracle = X11Oracles.XrandrMonitors(wire.DisplayName);
            var matches = oracle.Count == monitors.Count && monitors.All(monitor =>
                oracle.Any(reported =>
                    reported.X == monitor.X && reported.Y == monitor.Y &&
                    reported.Width == monitor.Width && reported.Height == monitor.Height));
            Require("MONITORS", matches, $"source=randr randr={backend.RandRVersion} xrandr={oracle.Count} {Describe(monitors)}");
        }
        else
        {
            // No RANDR: play the WM's part and write the EWMH properties from backends.md.
            var root = wire.Geometry(wire.Root);
            wire.SetCardinals(wire.Root, "_NET_WORKAREA", "CARDINAL", 0, 32, root.Width, root.Height - 32);
            wire.SetCardinals(wire.Root, "_NET_LIST_OF_MONITORS", "CARDINAL",
                0, 0, root.Width, root.Height,
                root.Width, 0, root.Width, root.Height);
            var fallback = backend.GetMonitors();
            var primary = fallback[0];
            Require("MONITORS",
                fallback.Count == 2 && primary.WorkY == 32 && primary.WorkHeight == root.Height - 32 &&
                fallback[1].WorkY == 0 && fallback[1].WorkWidth == root.Width,
                $"source=fallback root={root} {Describe(fallback)}");
        }

        // Override-redirect windows (popups, docklets) are not tileable: exit code 4.
        var popup = windows.CreateOverrideRedirect(10, 10, 200, 100);
        BackendException? notTileable = null;
        try
        {
            backend.EnsureTileable((ulong)popup);
        }
        catch (BackendException error)
        {
            notTileable = error;
        }

        Require("NOT-TILEABLE",
            notTileable is { Error: BackendError.WindowNotTileable, ExitCode: 4 },
            $"exit={notTileable?.ExitCode} message=\"{notTileable?.Message}\"");
    }

    private static string Describe(IReadOnlyList<Monitor> monitors) => string.Join(" ", monitors.Select(monitor =>
        $"[{monitor.Name} {monitor.X},{monitor.Y} {monitor.Width}x{monitor.Height} " +
        $"work={monitor.WorkX},{monitor.WorkY} {monitor.WorkWidth}x{monitor.WorkHeight}]"));

    private static void Require(string operation, bool condition, string detail)
    {
        if (!condition)
        {
            throw new SpikeFailure($"FAIL {operation}: {detail}");
        }

        Console.WriteLine($"{operation} ok {detail}");
    }

    private sealed class SpikeFailure(string message) : Exception(message);
}
