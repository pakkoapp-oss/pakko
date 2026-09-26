namespace Archiver.OperationUi.Protocol;

/// <summary>
/// A frame that is not a valid message. The message text never contains frame content: a garbled
/// frame may hold a password.
/// </summary>
public sealed class ProtocolException(string message) : Exception(message);
