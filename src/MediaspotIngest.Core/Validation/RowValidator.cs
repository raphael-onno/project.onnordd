using System.Globalization;
using MediaspotIngest.Core.Domain;
using MediaspotIngest.Core.Filling;
using MediaspotIngest.Core.Template;

namespace MediaspotIngest.Core.Validation;

/// <summary>
/// Checks an item's row against the template, before Mediaspot's importer rejects it.
/// Rules come from the template itself: requirement (row 3) and allowed values (row 4).
/// </summary>
public sealed class RowValidator(LegacyTemplate template)
{
    private static readonly string[] DateFormats = ["yyyy-MM-dd", "dd/MM/yyyy", "MM/dd/yyyy", "yyyy"];

    public IReadOnlyList<CellIssue> Validate(IngestItem item)
    {
        var issues = new List<CellIssue>();
        var isSeason = string.Equals(item.GetCell("titleType")?.Trim(), "Season", StringComparison.OrdinalIgnoreCase);

        foreach (var column in template.DataColumns)
        {
            if (column.IsAutomatic)
                continue;

            var value = item.GetCell(column.TagName);
            var mandatory = column.Requirement == ColumnRequirement.Mandatory
                || column.Requirement == ColumnRequirement.MandatoryIfSeason && isSeason;

            if (LegacyTemplate.IsEmpty(value))
            {
                if (mandatory)
                    issues.Add(new CellIssue(column, "Valeur obligatoire manquante."));
                continue;
            }

            var issue = CheckValue(column, value!.Trim());
            if (issue is not null)
                issues.Add(new CellIssue(column, issue));
        }

        // The asset import attaches each row to its title by titleId (the Mediaspot id), else by titleCustomerId:
        // without either, it has nothing to attach to, whatever row 3 says.
        var titleColumn = template.Find("titleId") ?? template.Find("titleCustomerId");
        if (titleColumn is not null
            && LegacyTemplate.IsEmpty(item.GetCell("titleId"))
            && LegacyTemplate.IsEmpty(item.GetCell("titleCustomerId")))
        {
            issues.Add(new CellIssue(titleColumn, "Titre pas encore créé dans Mediaspot (onglet Titres → Créer dans Mediaspot) et pas de Title ID."));
        }

        return issues;
    }

    private static string? CheckValue(TemplateColumn column, string value)
    {
        switch (column.ValueKind)
        {
            case ColumnValueKind.Picklist:
                return CellValues.IsAllowed(column, value) ? null : $"« {value} » n'est pas dans la liste autorisée.";

            case ColumnValueKind.Boolean:
                return value is "True" or "False" ? null : "Attendu : True ou False.";

            case ColumnValueKind.Date:
                return DateTime.TryParseExact(value, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                    ? null
                    : "Date attendue (aaaa-mm-jj).";
        }

        if (string.Equals(column.TagName, "productionDate", StringComparison.OrdinalIgnoreCase))
        {
            return value.Length == 4 && int.TryParse(value, out var year) && year is >= 1890 and <= 2100
                ? null
                : "Année de production attendue (aaaa).";
        }

        if (column.TagName is "serieSeasonNumber" or "serieSeasonEpisodeTotalNumber" or "serieEpisodeNumberProduction")
            return long.TryParse(value, out _) ? null : "Nombre attendu.";

        if (string.Equals(column.TagName, "originalFileName", StringComparison.OrdinalIgnoreCase)
            && value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return "Nom de fichier invalide.";

        return null;
    }
}

public sealed record CellIssue(TemplateColumn Column, string Message)
{
    public override string ToString() => $"{Column.DisplayName} : {Message}";
}
