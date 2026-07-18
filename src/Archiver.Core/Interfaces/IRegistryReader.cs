namespace Archiver.Core.Interfaces;

/// <summary>
/// Minimal seam over the Windows registry for GroupPolicyService (T-F51) — kept deliberately
/// small so tests use a hand-rolled fake instead of a mocking library (none is used in this
/// repo). Both members return null for an absent key/value or a value of the wrong registry
/// type; implementations never throw.
/// </summary>
public interface IRegistryReader
{
    int? GetDword(string keyPath, string valueName);
    string[]? GetMultiString(string keyPath, string valueName);
}
