using CommandLine;

namespace Tiler;

public record MoveOptions
{
    [Value(0, Required = true, MetaName = "Position", HelpText = $"Must be on of: 'Full, LeftHalf, MiddleHalf, RightHalf, LeftThird, MiddleThird, RightThird'")]
    public Position Position { get; set; }
}