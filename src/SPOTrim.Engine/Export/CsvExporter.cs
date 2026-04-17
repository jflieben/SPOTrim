using System.Text;
using SPOTrim.Engine.Models;

namespace SPOTrim.Engine.Export;

public sealed class CsvExporter
{
    public byte[] ExportSites(List<SiteInfo> sites)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Site URL,Title,Type,Owner,Storage Used (MB),Storage Quota (MB),Last Activity");

        foreach (var site in sites)
        {
            sb.AppendLine($"{Escape(site.SiteUrl)},{Escape(site.SiteTitle)},{Escape(site.SiteType)},{Escape(site.Owner)},{Math.Round(site.StorageUsedBytes / 1048576.0, 2)},{Math.Round(site.StorageQuotaBytes / 1048576.0, 2)},{Escape(site.LastActivityDate)}");
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static string Escape(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}
