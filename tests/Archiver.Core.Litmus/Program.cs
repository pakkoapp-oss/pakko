using System.Globalization;
using System.Runtime.Versioning;
using Archiver.Core.Litmus;

[assembly: SupportedOSPlatform("windows")]

// T-F498: each operation runs in a child process of its own, inside a Job object, so the counters
// see that operation and every process it starts. The child warms the same operation up on a small
// input first and measures only the real run: docs/TESTING.md, "T-F498 litmus run".
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
return args is ["--child", string op, string shape, string scale]
    ? await Child.RunAsync(op, shape, int.Parse(scale, CultureInfo.InvariantCulture))
    : await Parent.RunAsync(args);
