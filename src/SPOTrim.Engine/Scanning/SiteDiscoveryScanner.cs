using System.Text.Json;
using SPOTrim.Engine.Auth;
using SPOTrim.Engine.Graph;
using SPOTrim.Engine.Models;

namespace SPOTrim.Engine.Scanning;

/// <summary>
/// Discovers all SharePoint sites (and optionally OneDrive sites) in the tenant.
/// </summary>
public sealed class SiteDiscoveryScanner : IScanProvider
{
    private readonly GraphClient _graphClient;
    private readonly DelegatedAuth _auth;

    public string Category => "Discovery";

    public SiteDiscoveryScanner(GraphClient graphClient, DelegatedAuth auth)
    {
        _graphClient = graphClient;
        _auth = auth;
    }

    public async IAsyncEnumerable<SiteInfo> ScanAsync(ScanContext context,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        context.ReportProgress("Enumerating SharePoint sites...", 3);

        // Get all sites via Graph
        var siteCount = 0;
        await foreach (var site in _graphClient.GetPaginatedAsync(
            "sites?$select=id,displayName,webUrl,createdDateTime,lastModifiedDateTime,siteCollection&$top=999",
            ct: ct))
        {
            ct.ThrowIfCancellationRequested();

            var siteUrl = site.TryGetProperty("webUrl", out var url) ? url.GetString() ?? "" : "";
            var siteId = site.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "";
            var siteTitle = site.TryGetProperty("displayName", out var name) ? name.GetString() ?? "" : "";

            // Determine site type
            var siteType = "Other";
            if (siteUrl.Contains("-my.sharepoint.com", StringComparison.OrdinalIgnoreCase) ||
                siteUrl.Contains("/personal/", StringComparison.OrdinalIgnoreCase))
            {
                siteType = "OneDrive";
                if (!context.Config.IncludeOneDrive) continue;
            }
            else if (site.TryGetProperty("siteCollection", out var sc) && sc.ValueKind != JsonValueKind.Null)
            {
                siteType = "TeamSite";
            }

            siteCount++;
            context.SetTotalTargets(siteCount);

            yield return new SiteInfo
            {
                ScanId = context.ScanId,
                SiteId = siteId,
                SiteUrl = siteUrl,
                SiteTitle = siteTitle,
                SiteType = siteType,
                LastActivityDate = site.TryGetProperty("lastModifiedDateTime", out var lm) ? lm.GetString() ?? "" : ""
            };

            context.CompleteTarget();
        }

        context.ReportProgress($"Discovered {siteCount} sites", 3);
    }
}
