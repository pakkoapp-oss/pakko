using System.Globalization;
using Archiver.OperationUi.Protocol;

namespace Archiver.Shell;

/// <summary>The labels Shell sends the operation window helper; English until T-F268 step 6 localizes them.</summary>
internal static class OperationWindowText
{
    public static Hello CreateHello()
    {
        CultureInfo culture = CultureInfo.CurrentUICulture;
        return new Hello(FrameCodec.ProtocolVersion, culture.Name, culture.TextInfo.IsRightToLeft, new Dictionary<string, string>
        {
            [WindowStrings.Cancel] = "Cancel",
            [WindowStrings.CancelAll] = "Cancel all",
            [WindowStrings.Close] = "Close",
            [WindowStrings.ItemOfCount] = "Archive {0} of {1}  ·  {2}",
        });
    }
}
