using Archiver.Core.Services;

namespace Archiver.App.Core;

/// <summary>Result of <see cref="LaunchActivationRouter.Decide"/>.</summary>
/// <param name="Mode">What the activation should do.</param>
/// <param name="BrowsePath">The archive to open — set only when <paramref name="Mode"/> is <see cref="FileActivationMode.Browse"/>.</param>
/// <param name="Paths">The paths to add — set only when <paramref name="Mode"/> is <see cref="FileActivationMode.AddToList"/>.</param>
public sealed record LaunchActivationDecision(FileActivationMode Mode, string? BrowsePath, IReadOnlyList<string> Paths);

/// <summary>
/// Routes a Launch-kind activation carrying Archiver.Shell's <see cref="LaunchArguments"/> (T-F232,
/// replacing the <c>pakko://</c> protocol route of T-F03/T-F56). <c>--browse</c> with exactly one
/// file enters the Archive Browser; anything else parsed adds its paths to the pending list —
/// the same one-archive-only browse rule <see cref="FileActivationRouter"/> applies.
/// </summary>
public static class LaunchActivationRouter
{
    /// <summary>The decision for <paramref name="arguments"/>, or null for a plain launch or anything unrecognized.</summary>
    public static LaunchActivationDecision? Decide(string? arguments)
    {
        if (!LaunchArguments.TryParse(arguments, out var operation, out var files))
            return null;

        return operation == LaunchOperation.Browse && files.Count == 1
            ? new LaunchActivationDecision(FileActivationMode.Browse, files[0], [])
            : new LaunchActivationDecision(FileActivationMode.AddToList, null, files);
    }
}
