using System.Text.Json.Serialization;

namespace Archiver.OperationUi.Protocol;

// Respect* make a frame with a missing field or a null where the record says non-null fail at the
// boundary (ProtocolException) instead of turning into a null deep in the window.
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(ProtocolMessage))]
internal sealed partial class ProtocolJsonContext : JsonSerializerContext;
