using System.Text.Json.Serialization;

namespace Tiler.DBus;

[JsonSerializable(typeof(WindowInfo))]
[JsonSerializable(typeof(IEnumerable<WindowInfo>))]
internal partial class DbusJsonContext : JsonSerializerContext
{
}