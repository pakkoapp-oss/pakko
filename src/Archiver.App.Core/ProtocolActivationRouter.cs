namespace Archiver.App.Core;

/// <summary>
/// Recognizes a pakko://browse?files=&lt;base64 JSON array&gt; protocol-activation URI (T-F03) so
/// Archiver.App can enter the Archive Browser (T-F05) directly instead of the pending-list/
/// extract-options view — mirrors FileActivationRouter's WinUI-free decide-then-branch split, but
/// for protocol (not file) activation. pakko://extract and pakko://archive are unaffected and keep
/// going through MainViewModel.AddPathsFromProtocolUri unchanged. Only a single file is
/// recognized as a browse request — browsing multiple archives at once has no meaning, the same
/// one-archive-only rule FileActivationRouter already enforces for double-click.
/// </summary>
public static class ProtocolActivationRouter
{
    public static bool TryGetBrowsePath(string rawUri, out string? path)
    {
        path = null;
        try
        {
            var uri = new Uri(rawUri);
            if (uri.Host != "browse")
                return false;

            var query = uri.Query.TrimStart('?');
            string? base64 = null;
            foreach (var part in query.Split('&'))
            {
                var idx = part.IndexOf('=');
                if (idx > 0 && part[..idx] == "files")
                {
                    base64 = Uri.UnescapeDataString(part[(idx + 1)..]);
                    break;
                }
            }
            if (string.IsNullOrEmpty(base64))
                return false;

            var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            var files = System.Text.Json.JsonSerializer.Deserialize<string[]>(json);
            if (files is null || files.Length != 1)
                return false;

            path = files[0];
            return true;
        }
        catch
        {
            return false;
        }
    }
}
