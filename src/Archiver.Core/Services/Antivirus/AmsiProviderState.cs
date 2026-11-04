namespace Archiver.Core.Services.Antivirus;

/// <summary>T-F448. What the registered AMSI providers let a scan say, checked once per operation.</summary>
internal enum AmsiProviderState
{
    /// <summary>No provider is registered: nothing answers, a scan has no verdict.</summary>
    None,

    /// <summary>
    /// Every registered provider is one measured to answer Clean for known test threats, so its
    /// Clean is no verdict either.
    /// </summary>
    OnlyNonReporting,

    /// <summary>At least one other provider is registered: its answers are taken as they come.</summary>
    Usable,
}
