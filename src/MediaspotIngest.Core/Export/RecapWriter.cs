using ClosedXML.Excel;
using MediaspotIngest.Core.Domain;
using MediaspotIngest.Core.Template;
using MediaspotIngest.Core.Validation;

namespace MediaspotIngest.Core.Export;

/// <summary>
/// The recap of every batch of a profile (the old tool's "Export Recap XLSX"): one row per element, with where it
/// stands in the tool and in Mediaspot. Statuses are the last ones read: run "Suivi Mediaspot" first for fresh ones.
/// </summary>
public static class RecapWriter
{
    private static readonly string[] Header =
    [
        "Lot", "Titre", "Id registre", "Fichier", "Chemin registre", "Type", "Contenu", "Taille (Go)", "Statut",
        "Fichier sur disque", "Contrôle fichier", "Problèmes de métadonnées", "Transféré le", "Importé le",
        "Request Mediaspot", "Statut Mediaspot",
    ];

    public static string Write(string profileName, IReadOnlyList<IngestItem> items, LegacyTemplate? template, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var path = Path.Combine(outputDirectory, LegacyImporterWriter.SafeFileName($"Recap_{profileName}_{DateTime.Now:yyyyMMdd_HHmmss}") + ".xlsx");
        var validator = template is null ? null : new RowValidator(template);

        using var workbook = new XLWorkbook();
        WriteDetail(workbook.AddWorksheet("Éléments"), items, validator);
        WriteSummary(workbook.AddWorksheet("Synthèse par lot"), items, validator);
        workbook.Worksheet(1).SetTabActive();
        workbook.SaveAs(path);
        return path;
    }

    private static void WriteDetail(IXLWorksheet sheet, IReadOnlyList<IngestItem> items, RowValidator? validator)
    {
        for (var c = 0; c < Header.Length; c++)
            sheet.Cell(1, c + 1).SetValue(Header[c]);

        var row = 2;
        foreach (var item in items.OrderBy(i => i.BatchNumber).ThenBy(i => i.TitleKey).ThenBy(i => i.RelativePath))
        {
            var issues = validator?.Validate(item) ?? [];
            var status = validator is null ? (ItemStatus?)null : ItemStatusRules.Evaluate(item, issues.Count == 0);
            var size = item.ActualSizeBytes ?? item.ExpectedSizeBytes;

            sheet.Cell(row, 1).SetValue($"Batch_{item.BatchNumber:000}");
            sheet.Cell(row, 2).SetValue(item.TitleKey);
            sheet.Cell(row, 3).SetValue(item.Id);
            sheet.Cell(row, 4).SetValue(item.FileName);
            sheet.Cell(row, 5).SetValue(item.RelativePath);
            sheet.Cell(row, 6).SetValue(item.GetCell("assetType") ?? "");
            sheet.Cell(row, 7).SetValue(item.GetCell("contentType") ?? "");
            if (size is { } bytes)
                sheet.Cell(row, 8).SetValue(Math.Round(bytes / (1024.0 * 1024 * 1024), 3));
            sheet.Cell(row, 9).SetValue(status?.ToString() ?? "");
            sheet.Cell(row, 10).SetValue(item.Location.ToString());
            sheet.Cell(row, 11).SetValue(item.FileWarning ?? "");
            sheet.Cell(row, 12).SetValue(string.Join(" | ", issues.Select(i => i.ToString())));
            if (item.TransferredAt is { } transferred)
                sheet.Cell(row, 13).SetValue(transferred);
            if (item.ExportedAt is { } exported)
                sheet.Cell(row, 14).SetValue(exported);
            if (item.MediaspotAssetId is { } assetId)
                sheet.Cell(row, 15).SetValue(assetId);
            sheet.Cell(row, 16).SetValue(item.MediaspotStatus ?? "");
            row++;
        }

        FormatTable(sheet, row - 1);
        sheet.Column(13).Style.DateFormat.Format = "dd/mm/yyyy hh:mm";
        sheet.Column(14).Style.DateFormat.Format = "dd/mm/yyyy hh:mm";
    }

    private static void WriteSummary(IXLWorksheet sheet, IReadOnlyList<IngestItem> items, RowValidator? validator)
    {
        string[] header = ["Lot", "Éléments", "Taille (Go)", "FixNeeded", "Ready", "Exported", "Pending", "Done", "Contrôles fichier en alerte"];
        for (var c = 0; c < header.Length; c++)
            sheet.Cell(1, c + 1).SetValue(header[c]);

        var row = 2;
        foreach (var batch in items.GroupBy(i => i.BatchNumber).OrderBy(g => g.Key))
        {
            var statuses = batch
                .Select(i => validator is null ? (ItemStatus?)null : ItemStatusRules.Evaluate(i, validator.Validate(i).Count == 0))
                .ToList();
            sheet.Cell(row, 1).SetValue($"Batch_{batch.Key:000}");
            sheet.Cell(row, 2).SetValue(batch.Count());
            sheet.Cell(row, 3).SetValue(Math.Round(batch.Sum(i => i.ActualSizeBytes ?? i.ExpectedSizeBytes ?? 0) / (1024.0 * 1024 * 1024), 1));
            var column = 4;
            foreach (var status in new[] { ItemStatus.FixNeeded, ItemStatus.Ready, ItemStatus.Exported, ItemStatus.Pending, ItemStatus.Done })
                sheet.Cell(row, column++).SetValue(statuses.Count(s => s == status));
            sheet.Cell(row, column).SetValue(batch.Count(i => i.FileWarning is not null));
            row++;
        }

        FormatTable(sheet, row - 1);
    }

    private static void FormatTable(IXLWorksheet sheet, int lastRow)
    {
        var lastColumn = sheet.LastColumnUsed()?.ColumnNumber() ?? 1;
        if (lastRow >= 2)
            sheet.Range(1, 1, lastRow, lastColumn).CreateTable();
        else
            sheet.Row(1).Style.Font.Bold = true;
        sheet.SheetView.FreezeRows(1);
        sheet.Columns().AdjustToContents(1, Math.Min(lastRow, 200));
    }
}
