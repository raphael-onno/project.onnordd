using ClosedXML.Excel;

namespace MediaspotIngest.Core.Template;

/// <summary>
/// A platform's Legacy Importer template. The template describes its own columns in its first five rows,
/// which is exactly what Mediaspot's importer reads:
/// <list type="number">
/// <item>metadata tagName (the key the importer matches on)</item>
/// <item>layer: Title, Content or Asset</item>
/// <item>requirement: M, O, A (filled by the importer) or "M if Season"</item>
/// <item>allowed values, separated by <c>;</c> — or a type hint such as <c>Date</c>, <c>bool</c>, <c>List</c></item>
/// <item>human-readable label</item>
/// </list>
/// Data starts at row 6. Changing the platform's columns therefore only means changing its template.
/// </summary>
public sealed class LegacyTemplate
{
    public const int TagNameRow = 1;
    public const int LayerRow = 2;
    public const int RequirementRow = 3;
    public const int AllowedValuesRow = 4;
    public const int LabelRow = 5;
    public const int FirstDataRow = 6;

    /// <summary>Written in a cell to tell the importer "leave this metadata alone".</summary>
    public const string Skip = "##";

    public required string FilePath { get; init; }

    public required string SheetName { get; init; }

    public required IReadOnlyList<TemplateColumn> Columns { get; init; }

    public IEnumerable<TemplateColumn> DataColumns => Columns.Where(c => !c.IsSpacer);

    public TemplateColumn? Find(string tagName) =>
        Columns.FirstOrDefault(c => !c.IsSpacer && string.Equals(c.TagName, tagName, StringComparison.OrdinalIgnoreCase));

    public bool Has(string tagName) => Find(tagName) is not null;

    public static bool IsEmpty(string? value) => string.IsNullOrWhiteSpace(value) || value.Trim() == Skip;

    public static LegacyTemplate Load(string filePath)
    {
        using var workbook = new XLWorkbook(filePath);
        var sheet = workbook.Worksheets.First();
        var lastColumn = sheet.Row(TagNameRow).LastCellUsed()?.Address.ColumnNumber
            ?? throw new InvalidDataException($"The template '{filePath}' has no tagName in row {TagNameRow}.");
        lastColumn = Math.Max(lastColumn, sheet.Row(LabelRow).LastCellUsed()?.Address.ColumnNumber ?? 0);

        var columns = new List<TemplateColumn>(lastColumn);
        for (var index = 1; index <= lastColumn; index++)
        {
            string Text(int row) => sheet.Cell(row, index).GetString().Trim();

            columns.Add(TemplateColumn.Parse(
                index,
                tagName: Text(TagNameRow),
                layer: Text(LayerRow),
                requirement: Text(RequirementRow),
                allowedValues: Text(AllowedValuesRow),
                label: Text(LabelRow)));
        }

        return new LegacyTemplate { FilePath = filePath, SheetName = sheet.Name, Columns = columns };
    }
}
