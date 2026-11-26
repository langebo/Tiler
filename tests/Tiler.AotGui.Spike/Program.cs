using System.Diagnostics;
using Gtk;

// Spike program for issue #4, not feature code. Needs an X display (run under Xvfb).
// Window with a label + check button, one main-loop round-trip, exit 0.
// --cycles N opens and closes N windows to prove repeated open/close.
internal static class Program
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static Application _app = null!;
    private static int _cycles = 1;
    private static int _opened;

    private static int Main(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--cycles")
            {
                _cycles = int.Parse(args[i + 1]);
            }
        }

        _app = Application.New("dev.blange.tiler.aotguispike", Gio.ApplicationFlags.FlagsNone);
        _app.OnActivate += (_, _) => OpenWindow();
        // Gio parses the command line itself; --cycles is ours, so give it nothing to parse.
        return _app.Run([]);
    }

    private static void OpenWindow()
    {
        var index = ++_opened;

        var box = Box.New(Orientation.Vertical, 6);
        box.Append(Label.New("GTK4 on .NET 11 Native AOT: alive"));
        box.Append(CheckButton.NewWithLabel("check button"));

        var window = ApplicationWindow.New(_app);
        window.SetTitle("Tiler AOT GUI spike");
        window.SetDefaultSize(400, 200);
        window.SetChild(box);

        // The first frame-clock tick is the "first frame" the < 500 ms target measures.
        window.AddTickCallback((_, _) =>
        {
            var firstFrameMs = Clock.Elapsed.TotalMilliseconds;
            GLib.Functions.IdleAdd(0, () =>
            {
                Console.WriteLine($"window {index}/{_cycles} first-frame-ms {firstFrameMs:F1}");
                // Open the next window before destroying this one: a GtkApplication quits
                // as soon as its last window goes away.
                if (index < _cycles)
                {
                    OpenWindow();
                }

                window.Destroy();
                if (index >= _cycles)
                {
                    _app.Quit();
                }

                return false;
            });
            return false;
        });

        window.Present();
    }
}
