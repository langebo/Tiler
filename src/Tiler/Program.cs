using System.Diagnostics.CodeAnalysis;
using CommandLine;
using Tiler;
using Tiler.Backends;
using Tiler.Engine;

internal class Program
{
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MoveOptions))]
    private static async Task Main(string[] args)
    {
        await Parser.Default.ParseArguments<MoveOptions>(args).WithParsedAsync(async options =>
        {
            // Inside the parse-success branch: `--version`/`--help` and usage errors (exit 1)
            // keep their behavior even when no session type is set, as under `xvfb-run` in CI.
            var sessionType = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
            if (SessionDetector.Resolve(sessionType) is null)
            {
                var error = new NoBackend(sessionType);
                await Console.Error.WriteLineAsync($"tiler: {error.Headline}: {error.Detail}").ConfigureAwait(false);
                Environment.ExitCode = error.ExitCode;
                return;
            }

            await Repositioner.RepositionFocusedWindowAsync(options.Position).ConfigureAwait(false);
        });
    }
}
