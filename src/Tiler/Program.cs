using System.Diagnostics.CodeAnalysis;
using CommandLine;
using Tiler;

internal class Program
{
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MoveOptions))]
    private static async Task Main(string[] args)
    {
        await Parser.Default.ParseArguments<MoveOptions>(args).WithParsedAsync(async options =>
            await Repositioner.RepositionFocusedWindowAsync(options.Position).ConfigureAwait(false));
    }
}