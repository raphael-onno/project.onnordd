namespace MediaspotIngest.Core.Template;

public sealed class TemplateColumn
{
    /// <summary>1-based column number in the sheet.</summary>
    public int Index { get; init; }

    public string TagName { get; init; } = "";

    public string Layer { get; init; } = "";

    public ColumnRequirement Requirement { get; init; }

    public ColumnValueKind ValueKind { get; init; }

    /// <summary>The picklist, when row 4 holds one.</summary>
    public IReadOnlyList<string> AllowedValues { get; init; } = [];

    public string Label { get; init; } = "";

    /// <summary>Blank separator columns, and columns whose metadata the template does not name (e.g. Mapping Audio CHn).</summary>
    public bool IsSpacer => string.IsNullOrEmpty(TagName);

    /// <summary>Filled by the importer itself (titleId, contentId, assetId): always left empty.</summary>
    public bool IsAutomatic => Requirement == ColumnRequirement.Automatic;

    public string DisplayName => string.IsNullOrEmpty(Label) ? TagName : $"{Label} ({TagName})";

    public override string ToString() => TagName;

    public static TemplateColumn Parse(int index, string tagName, string layer, string requirement, string allowedValues, string label)
    {
        var (kind, values) = ParseAllowedValues(allowedValues);
        return new TemplateColumn
        {
            Index = index,
            TagName = tagName,
            Layer = layer,
            Requirement = ParseRequirement(requirement),
            ValueKind = kind,
            AllowedValues = values,
            Label = label,
        };
    }

    private static ColumnRequirement ParseRequirement(string text) => text.Trim().ToUpperInvariant() switch
    {
        "M" => ColumnRequirement.Mandatory,
        "A" => ColumnRequirement.Automatic,
        var t when t.StartsWith("M IF SEASON", StringComparison.Ordinal) => ColumnRequirement.MandatoryIfSeason,
        _ => ColumnRequirement.Optional,
    };

    private static (ColumnValueKind, IReadOnlyList<string>) ParseAllowedValues(string text)
    {
        switch (text.Trim().ToLowerInvariant())
        {
            case "":
            case "string":
                return (ColumnValueKind.Text, []);
            case "date":
                return (ColumnValueKind.Date, []);
            case "bool":
                return (ColumnValueKind.Boolean, ["False", "True"]);
            case "list":
            case "list;none":
                // A platform value list (e.g. language codes) the template does not spell out.
                return (ColumnValueKind.PlatformList, []);
        }

        // Values are kept as typed: the importer compares them exactly (some even carry a trailing space).
        var values = text.Split(';')
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim() == v ? v : v.TrimStart())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (values.Count == 2 && values.Contains("False") && values.Contains("True"))
            return (ColumnValueKind.Boolean, values);

        return (ColumnValueKind.Picklist, values);
    }
}

public enum ColumnRequirement
{
    Optional,
    Mandatory,
    MandatoryIfSeason,
    Automatic,
}

public enum ColumnValueKind
{
    Text,
    Picklist,
    PlatformList,
    Boolean,
    Date,
}
