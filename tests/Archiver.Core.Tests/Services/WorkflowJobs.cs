using System.Text.RegularExpressions;

namespace Archiver.Core.Tests.Services;

// Splits a workflow file under .github/workflows into its jobs, by the two-space job headers.
internal static partial class WorkflowJobs
{
    public static string Job(string repoRoot, string file, string name) =>
        Parse(repoRoot, file).Where(j => j.Name == name).Select(j => j.Body).SingleOrDefault() ?? "";

    public static List<(string Name, string Body)> Parse(string repoRoot, string file)
    {
        string workflow = Text(repoRoot, file);
        string jobs = workflow[(workflow.IndexOf("\njobs:\n", StringComparison.Ordinal) + 7)..];
        MatchCollection headers = JobHeader().Matches(jobs);
        return [.. headers.Select((m, i) => (m.Groups[1].Value,
            jobs[m.Index..(i + 1 < headers.Count ? headers[i + 1].Index : jobs.Length)]))];
    }

    // Comment lines go: the one above a job would otherwise count as the previous job's text.
    public static string Text(string repoRoot, string file) =>
        string.Join('\n', File.ReadAllText(Path.Combine(repoRoot, ".github", "workflows", file))
            .Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
            .Where(l => !l.TrimStart().StartsWith('#')));

    [GeneratedRegex(@"^  ([a-z0-9-]+):\n", RegexOptions.Multiline)]
    private static partial Regex JobHeader();
}
