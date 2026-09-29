namespace Archiver.Core.Tests.Fuzz;

/// <summary>A fuzz target's invariant did not hold.</summary>
internal sealed class FuzzViolation(string message, Exception? inner = null) : Exception(message, inner);
