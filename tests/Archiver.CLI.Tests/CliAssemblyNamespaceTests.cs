using System.Reflection;
using System.Runtime.CompilerServices;
using Archiver.CLI;
using FluentAssertions;

namespace Archiver.CLI.Tests;

/// <summary>
/// T-F440 (SonarCloud S3903): a type declared beside the top-level statements lands in the global
/// namespace, where any other assembly's type of the same name collides with it. Only the
/// compiler's own <c>Program</c> may live there.
/// </summary>
public class CliAssemblyNamespaceTests
{
    [Fact]
    public void EveryDeclaredType_IsInsideTheCliNamespace()
    {
        Assembly pakko = typeof(CliArgumentParser).Assembly;

        IEnumerable<string> outside = pakko.GetTypes()
            .Where(t => t.DeclaringType is null)
            .Where(t => t.Name != "Program" && !t.Name.StartsWith('<'))
            .Where(t => t.GetCustomAttribute<CompilerGeneratedAttribute>() is null)
            .Where(t => t.Namespace is null || !t.Namespace.StartsWith("Archiver.CLI", StringComparison.Ordinal))
            .Select(t => t.FullName ?? t.Name);

        outside.Should().BeEmpty();
    }
}
