using System.Globalization;
using System.Text.Json.Nodes;
using MediaspotIngest.Core.Domain;

namespace MediaspotIngest.Core.Mediaspot;

/// <summary>
/// Creates the titles in Mediaspot through the API, as the old tool's Custom Titles Manager did, so the
/// Legacy Importer only has assets to import. An existing title with the same Title ID is reused, never duplicated.
/// </summary>
public sealed class TitleCreator(MediaspotClient client)
{
    /// <summary>Same normalisation as Mediaspot's importer: "/" becomes a space, "'" a typographic apostrophe, upper case.</summary>
    public static string MediaspotName(string mainTitle) =>
        mainTitle.Trim().Replace('/', ' ').Replace('\'', '’').ToUpperInvariant();

    /// <summary>What is missing for the title to be created; empty when it can be.</summary>
    public static IReadOnlyList<string> Check(TitleInfo title)
    {
        // The Title ID (customer id) is optional: Mediaspot assigns the title's own id, and only demands a customer id
        // when the platform defines one (customerExternalIdLabel) — its refusal is then reported as is.
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(title.MainTitle))
            problems.Add("titre manquant");
        if (title.ProductionYear is not (>= 1890 and <= 2100))
            problems.Add("année manquante");
        if (title.IsSeason && (!int.TryParse(title.SeasonNumber, out _) || title.EpisodesCount is null))
            problems.Add("numéro de saison et nombre d'épisodes obligatoires pour une saison");
        return problems;
    }

    public async Task<TitleCreation> CreateOrFindAsync(TitleInfo title, string defaultLanguage, string defaultPictureFormat, CancellationToken ct = default)
    {
        var problems = Check(title);
        if (problems.Count > 0)
            return TitleCreation.Failed(string.Join(", ", problems));

        var customerId = string.IsNullOrWhiteSpace(title.TitleCustomId) ? null : title.TitleCustomId.Trim();
        var name = MediaspotName(title.MainTitle);
        var existing = customerId is not null
            ? await client.FindTitlesByCustomerIdAsync(customerId, ct)
            : await client.FindTitlesByNameAsync(name, ct);
        if (existing.Count == 1)
            return TitleCreation.Found(existing[0].Id, existing[0].Name);
        if (existing.Count > 1)
            return TitleCreation.Failed(customerId is not null
                ? $"{existing.Count} titres portent déjà ce Title ID ({string.Join(", ", existing.Select(e => e.Id))})"
                : $"{existing.Count} titres portent déjà ce nom ({string.Join(", ", existing.Select(e => e.Id))}) : préciser un Title ID");

        var languageCode = (title.OriginalLanguage ?? defaultLanguage).Trim().ToUpperInvariant();
        var language = (await client.GetLanguageCodesAsync(ct))
            .FirstOrDefault(l => string.Equals(l?["value"]?.ToString(), languageCode, StringComparison.OrdinalIgnoreCase));
        if (language is null)
            return TitleCreation.Failed($"langue « {languageCode} » inconnue de la plateforme");

        var year = title.ProductionYear!.Value;
        var metadata = new List<(string, JsonNode?)>
        {
            ("titleType", title.IsSeason ? "Season" : "Movie"),
            ("titleOriginalPictureFormat", (title.PictureFormat ?? defaultPictureFormat).Trim().Replace(',', '.')),
            ("titleOriginalLanguage", language),
            ("productionDate", new DateTime(year, 1, 1).ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture)),
        };
        if (title.IsSeason)
        {
            metadata.Add(("serieSeasonNumber", long.Parse(title.SeasonNumber!, CultureInfo.InvariantCulture)));
            metadata.Add(("serieSeasonEpisodeTotalNumber", (long)title.EpisodesCount!.Value));
        }

        // The old tool's convention, shown in Mediaspot's title lists: "#<Title ID> <NAME> (<year>)".
        var nameAlt = customerId is null ? $"{name} ({year})" : $"#{customerId} {name} ({year})";
        var id = await client.CreateTitleAsync(name, customerId, nameAlt, metadata, ct);
        return TitleCreation.Created(id, name);
    }
}

public sealed record TitleCreation(TitleCreationOutcome Outcome, long? TitleId, string? Name, string? Error)
{
    public static TitleCreation Created(long id, string? name) => new(TitleCreationOutcome.Created, id, name, null);

    public static TitleCreation Found(long id, string? name) => new(TitleCreationOutcome.Found, id, name, null);

    public static TitleCreation Failed(string error) => new(TitleCreationOutcome.Failed, null, null, error);
}

public enum TitleCreationOutcome
{
    Created,
    Found,
    Failed,
}
