using System.Text.Json.Serialization;

namespace Archiver.Core.Services;

// T-F348: generated metadata for LaunchArguments' string list. The reflection serializer built
// it at run time, ~37 ms of Archiver.Shell's start on every Explorer "Open".
[JsonSerializable(typeof(IReadOnlyList<string>))]
[JsonSerializable(typeof(string[]))]
internal sealed partial class LaunchArgumentsJsonContext : JsonSerializerContext;
