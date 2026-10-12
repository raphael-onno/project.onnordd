using MediaspotIngest.Core.Template;

namespace MediaspotIngest.Core.Filling;

public static class CellValues
{
    /// <summary>The picklist's exact spelling of a value (case and trailing spaces matter to the importer).</summary>
    public static string Canonicalize(TemplateColumn column, string value)
    {
        var trimmed = value.Trim();
        if (column.AllowedValues.Count == 0)
            return trimmed;

        return column.AllowedValues.FirstOrDefault(v => string.Equals(v.Trim(), trimmed, StringComparison.OrdinalIgnoreCase))
            ?? trimmed;
    }

    public static bool IsAllowed(TemplateColumn column, string value) =>
        column.AllowedValues.Count == 0
        || column.AllowedValues.Any(v => string.Equals(v.Trim(), value.Trim(), StringComparison.Ordinal));
}
