using System.Text.Json.Serialization;

namespace Tiler.DBus;

public class WindowInfo
{
    [JsonPropertyName("wm_class")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("wm_class_instance")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("pid")]
    public uint ProcessId { get; set; }

    [JsonPropertyName("id")]
    public uint WindowId { get; set; }

    [JsonPropertyName("frame_type")]
    public FrameType FrameType { get; set; }

    [JsonPropertyName("window_type")]
    public WindowType WindowType { get; set; }

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }

    [JsonPropertyName("x")]
    public int X { get; set; }

    [JsonPropertyName("y")]
    public int Y { get; set; }

    [JsonPropertyName("focus")]
    public bool IsFocused { get; set; }

    [JsonPropertyName("in_current_workspace")]
    public bool IsInWorkspace { get; set; }
}