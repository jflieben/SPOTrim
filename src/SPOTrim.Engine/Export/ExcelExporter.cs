using ClosedXML.Excel;
using SPOTrim.Engine.Models;

namespace SPOTrim.Engine.Export;

public sealed class ExcelExporter
{
    public byte[] ExportSites(List<SiteInfo> sites, string sheetName = "Sites")
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add(sheetName);

        // Headers
        var headers = new[] { "Site URL", "Title", "Type", "Owner", "Storage Used (MB)", "Storage Quota (MB)", "Last Activity" };
        for (int i = 0; i < headers.Length; i++)
            worksheet.Cell(1, i + 1).Value = headers[i];

        // Data
        for (int row = 0; row < sites.Count; row++)
        {
            var site = sites[row];
            worksheet.Cell(row + 2, 1).Value = site.SiteUrl;
            worksheet.Cell(row + 2, 2).Value = site.SiteTitle;
            worksheet.Cell(row + 2, 3).Value = site.SiteType;
            worksheet.Cell(row + 2, 4).Value = site.Owner;
            worksheet.Cell(row + 2, 5).Value = Math.Round(site.StorageUsedBytes / 1048576.0, 2);
            worksheet.Cell(row + 2, 6).Value = Math.Round(site.StorageQuotaBytes / 1048576.0, 2);
            worksheet.Cell(row + 2, 7).Value = site.LastActivityDate;
        }

        // Auto-fit columns
        worksheet.Columns().AdjustToContents();

        // Style header row
        var headerRange = worksheet.Range(1, 1, 1, headers.Length);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.LightSteelBlue;

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public byte[] ExportLibraries(List<LibraryInfo> libraries, string sheetName = "Libraries")
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add(sheetName);

        var headers = new[] { "Library URL", "Title", "Items", "Versioning", "Major Limit", "Minor Limit", "Storage (MB)", "Version Storage (MB)" };
        for (int i = 0; i < headers.Length; i++)
            worksheet.Cell(1, i + 1).Value = headers[i];

        for (int row = 0; row < libraries.Count; row++)
        {
            var lib = libraries[row];
            worksheet.Cell(row + 2, 1).Value = lib.LibraryUrl;
            worksheet.Cell(row + 2, 2).Value = lib.LibraryTitle;
            worksheet.Cell(row + 2, 3).Value = lib.ItemCount;
            worksheet.Cell(row + 2, 4).Value = lib.VersioningEnabled ? "Enabled" : "Disabled";
            worksheet.Cell(row + 2, 5).Value = lib.MajorVersionLimit;
            worksheet.Cell(row + 2, 6).Value = lib.MinorVersionLimit;
            worksheet.Cell(row + 2, 7).Value = Math.Round(lib.StorageUsedBytes / 1048576.0, 2);
            worksheet.Cell(row + 2, 8).Value = Math.Round(lib.VersionStorageBytes / 1048576.0, 2);
        }

        worksheet.Columns().AdjustToContents();
        var headerRange = worksheet.Range(1, 1, 1, headers.Length);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.LightSteelBlue;

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
