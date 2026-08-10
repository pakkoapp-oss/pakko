using System;

namespace Archiver.App.Services;

public interface ILogService
{
    void Info(string message);
    void Warn(string message);

    // CA1716 (member name conflicts with reserved keyword 'Error'): reviewed T-F150 — this
    // Info/Warn/Error triad is the documented public signature in CLAUDE.md's "Key Current
    // Signatures" section; renaming breaks that for zero real benefit (Pakko is a shipped
    // desktop app, not a library consumed by non-C# CLR languages where the keyword clash would
    // actually bite). See docs/CONVENTIONS.md's "Static-Analysis Won't-Fix Conventions" section.
#pragma warning disable CA1716
    void Error(string message, Exception? ex = null);
#pragma warning restore CA1716
}
