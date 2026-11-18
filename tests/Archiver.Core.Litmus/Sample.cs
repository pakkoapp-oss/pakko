using System.Globalization;

namespace Archiver.Core.Litmus;

internal sealed record Sample(double Ms, double Own, double Children, double Cpu, double Procs, double Alloc, double Handles, double Peak)
{
    public static Sample Parse(string line)
    {
        var v = line.Split(';').Select(p => p.Split('='))
            .ToDictionary(p => p[0], p => double.Parse(p[1], CultureInfo.InvariantCulture));
        return new Sample(v["ms"], v["own"], v["children"], v["cpu"], v["procs"], v["alloc"], v["handles"], v["peak"]);
    }
}
