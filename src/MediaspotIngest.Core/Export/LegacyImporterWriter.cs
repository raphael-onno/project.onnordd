using System.Text.RegularExpressions;
using ClosedXML.Excel;
using MediaspotIngest.Core.Domain;
using MediaspotIngest.Core.Template;

namespace MediaspotIngest.Core.Export;

/// <summary>
/// Writes a Legacy Importer file: a copy of the platform template with one row per item from row 6.
/// Follows what Mediaspot's importer expects — every cell of a data row written (dense), everything as text,
/// <c>##</c> for "no value", including the importer-owned columns (titleId, contentId, assetId).
/// </summary>
public static partial class LegacyImporterWriter
{
    /// <summary>Mediaspot's upload strips anything else from file names.</summary>
    public static string SafeFileName(string name) => UnsafeCharacters().Replace(name, "_");

    public static string Write(LegacyTemplate template, IReadOnlyList<IngestItem> items, string outputDirectory, string baseName)
    {
        if (items.Count == 0)
            throw new InvalidOperationException("Aucun item à exporter.");

        Directory.CreateDirectory(outputDirectory);
        var outputPath = Path.Combine(outputDirectory, SafeFileName(baseName) + ".xlsx");
        if (File.Exists(outputPath))
            throw new IOException($"Le fichier « {outputPath} » existe déjà : il n'est pas écrasé.");

        using (var workbook = new XLWorkbook(template.FilePath))
        {
            var sheet = workbook.Worksheet(template.SheetName);

            // A template may ship sample rows; the importer would read them as data.
            var lastUsedRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
            if (lastUsedRow >= LegacyTemplate.FirstDataRow)
                sheet.Rows(LegacyTemplate.FirstDataRow, lastUsedRow).Delete();

            var row = LegacyTemplate.FirstDataRow;
            foreach (var item in items)
            {
                foreach (var column in template.Columns)
                    sheet.Cell(row, column.Index).SetValue(CellText(column, item));
                row++;
            }

            workbook.SaveAs(outputPath);
        }

        return outputPath;
    }

    private static string CellText(TemplateColumn column, IngestItem item)
    {
        // "##" rather than blank keeps the row dense; for assetId it also reads as "to import".
        if (column.IsSpacer)
            return LegacyTemplate.Skip;

        // Importer-owned columns stay "##", except a value we know for sure (titleId of a title created from here).
        var value = item.GetCell(column.TagName);
        return LegacyTemplate.IsEmpty(value) ? LegacyTemplate.Skip : value!;
    }

    [GeneratedRegex("[^A-Za-z0-9_-]")]
    private static partial Regex UnsafeCharacters();
}
