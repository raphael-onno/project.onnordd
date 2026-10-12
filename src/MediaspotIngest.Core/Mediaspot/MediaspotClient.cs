using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MediaspotIngest.Core.Mediaspot;

/// <summary>
/// The few Mediaspot API calls the tool needs: log in, then find the assets the legacy import created.
/// Same calls as the old tool, without its hard-coded credentials and its disabled TLS validation.
/// </summary>
public sealed class MediaspotClient : IDisposable
{
    /// <summary>CloudFront in front of api.mediaspot.io answers 403 to requests without a User-Agent.</summary>
    public const string UserAgent = "MediaspotIngest/1.0";

    private readonly HttpClient http;
    private readonly string apiBaseUrl;
    private string? _sessionKey;
    private DateTime _sessionExpiry;
    private List<MetadataField>? _fields;
    private Dictionary<string, MetadataField>? _assetFields;
    private JsonArray? _languages;
    private (string Login, string Password, long PlatformId)? _credentials;

    public MediaspotClient(HttpClient http, string apiBaseUrl)
    {
        this.http = http;
        this.apiBaseUrl = apiBaseUrl;
        if (http.DefaultRequestHeaders.UserAgent.Count == 0)
            http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
    }

    public bool IsConnected => _sessionKey is not null && DateTime.UtcNow < _sessionExpiry;

    public string? ConnectedUser { get; private set; }

    public async Task LoginAsync(string login, string password, long platformId, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync(
            ServiceUrl("user", "login"),
            new { login, password, platformId, authmode = 0 },
            ct);
        var json = await ReadJsonAsync(response, ct);

        var session = json?["sessionCreated"];
        _sessionKey = session?["key"]?.GetValue<string>()
            ?? throw new MediaspotException(ErrorMessage(json) ?? "Connexion refusée par Mediaspot.");
        _sessionExpiry = session?["expireDate"] is { } expiry && DateTime.TryParse(expiry.ToString(), out var date)
            ? date.ToUniversalTime()
            : DateTime.UtcNow.AddHours(1);
        ConnectedUser = json?["user"]?["login"]?.ToString() ?? login;
        _credentials = (login, password, platformId);
        _fields = null;
        _assetFields = null;
        _languages = null;
    }

    /// <summary>Asset metadata fields of the platform, by tagName (their ids are needed to search on them).</summary>
    public async Task<IReadOnlyDictionary<string, MetadataField>> GetAssetMetadataFieldsAsync(CancellationToken ct = default) =>
        _assetFields ??= await GetFieldsByLayerAsync("Asset", ct);

    /// <summary>Title metadata fields of the platform, by tagName.</summary>
    public Task<Dictionary<string, MetadataField>> GetTitleMetadataFieldsAsync(CancellationToken ct = default) =>
        GetFieldsByLayerAsync("Title", ct);

    private async Task<Dictionary<string, MetadataField>> GetFieldsByLayerAsync(string layer, CancellationToken ct)
    {
        if (_fields is null)
        {
            var json = await SendAsync(HttpMethod.Get, ServiceUrl("setup", "metadata/fields"), null, ct);
            _fields = (json?["metadataFields"]?.AsArray() ?? [])
                .Where(f => f?["tagName"] is not null && f["id"] is not null)
                .Select(f => new MetadataField(
                    f!["id"]!.GetValue<long>(),
                    f["tagName"]!.ToString(),
                    f["baseType"]?.ToString() ?? "String",
                    f["label"]?.ToString() ?? "",
                    f["layer"]?.ToString() ?? ""))
                .ToList();
        }

        return _fields
            .Where(f => string.Equals(f.Layer, layer, StringComparison.OrdinalIgnoreCase))
            .GroupBy(f => f.TagName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The platform's language codes ({id, externalId, value, label, rfc5646}), as the API gives them.</summary>
    public async Task<JsonArray> GetLanguageCodesAsync(CancellationToken ct = default)
    {
        if (_languages is not null)
            return _languages;
        var json = await SendAsync(HttpMethod.Get, ServiceUrl("asset", "metadata/languages"), null, ct);
        return _languages = json?["languages"]?.AsArray() ?? [];
    }

    /// <summary>Non-deleted titles carrying this customer id.</summary>
    public async Task<IReadOnlyList<AssetSummary>> FindTitlesByCustomerIdAsync(string titleCustomerId, CancellationToken ct = default)
    {
        var results = await SearchTitlesAsync("*", new JsonArray(titleCustomerId), ct);
        return results
            .Where(r => string.Equals(r.CustomerId, titleCustomerId, StringComparison.OrdinalIgnoreCase))
            .Select(r => r.Summary)
            .ToList();
    }

    /// <summary>Non-deleted titles whose name is exactly <paramref name="name"/> (Mediaspot stores title names in upper case).</summary>
    public async Task<IReadOnlyList<AssetSummary>> FindTitlesByNameAsync(string name, CancellationToken ct = default)
    {
        var results = await SearchTitlesAsync(name, null, ct);
        return results
            .Where(r => string.Equals(r.Summary.Name?.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase))
            .Select(r => r.Summary)
            .ToList();
    }

    private async Task<IReadOnlyList<(AssetSummary Summary, string? CustomerId)>> SearchTitlesAsync(string pattern, JsonArray? customerIds, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["outputLayer"] = "Title",
            ["pattern"] = pattern,
            ["hideTitleWithoutAsset"] = false,
        };
        if (customerIds is not null)
            body["titleCustomerIds"] = customerIds;

        var json = await SendAsync(HttpMethod.Post, ServiceUrl("search", "search-basic"), body, ct);
        return (json?["results"]?.AsArray() ?? [])
            .Where(r => r is not null && r["isDeleted"]?.GetValue<bool>() != true)
            .Select(r => (new AssetSummary(r!["id"]!.GetValue<long>(), r["name"]?.ToString(), r["status"]?.ToString(), r["type"]?.ToString()),
                r["titleCustomerId"]?.ToString()))
            .Where(r => !string.Equals(r.Item1.Status, "Deleted", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>Creates a title (title-service/create) and returns its Mediaspot id.</summary>
    public async Task<long> CreateTitleAsync(string name, string? titleCustomerId, string? nameAlt, IReadOnlyList<(string TagName, JsonNode? Value)> metadata, CancellationToken ct = default)
    {
        var fields = await GetTitleMetadataFieldsAsync(ct);
        var items = new JsonArray();
        foreach (var (tagName, value) in metadata)
        {
            if (value is null)
                continue;
            if (!fields.TryGetValue(tagName, out var field))
                throw new MediaspotException($"La métadonnée de titre « {tagName} » n'existe pas sur la plateforme.");

            // "$type" first: the API binds metadata polymorphically.
            items.Add(new JsonObject
            {
                ["$type"] = $"VDM.AirLab.Sdk.Core.Database.Types.{field.BaseType}, VDM.AirLab.Sdk.Core",
                ["id"] = field.Id,
                ["fieldType"] = field.BaseType,
                ["fieldName"] = field.TagName,
                ["fieldLabel"] = field.Label,
                ["value"] = value.DeepClone(),
            });
        }

        var body = new JsonObject
        {
            ["name"] = name,
            ["nameAlt"] = nameAlt,
            ["titleCustomerId"] = titleCustomerId,
            ["objectType"] = "GraphSkeleton",
            ["layer"] = "Title",
            ["status"] = "New",
            ["metadata"] = items,
        };
        var json = await SendAsync(HttpMethod.Post, ServiceUrl("title", "create"), body, ct);
        return json?["objectId"]?.GetValue<long>() is { } id and > 0
            ? id
            : throw new MediaspotException(ErrorMessage(json) ?? "Mediaspot n'a pas renvoyé l'id du titre créé.");
    }

    /// <summary>
    /// The provider's Aspera account. On cloud platforms Mediaspot blanks the login and password
    /// (uploads are meant to go through its File API), but host and ports are still returned.
    /// </summary>
    public async Task<ProviderAspera?> GetProviderAsperaAsync(long providerId, CancellationToken ct = default)
    {
        var json = await SendAsync(HttpMethod.Get, ServiceUrl("setup", "provider"), null, ct);
        var providers = (json?["providersAndStorages"] ?? json?["providers"])?.AsArray() ?? [];
        foreach (var entry in providers)
        {
            var provider = entry?["provider"] ?? entry;
            if (provider?["id"]?.GetValue<long>() != providerId)
                continue;

            var account = provider["asperaAccount"];
            var storage = entry?["storageSource"]?["storage"];
            var bucket = storage?["bucketName"]?.ToString();
            var importFolder = bucket is null
                ? null
                : $"s3://{bucket}/{(storage?["docRoot"]?.ToString() ?? "").Trim('/')}/temp/".Replace("//temp", "/temp");
            return new ProviderAspera(
                provider["labName"]?.ToString(),
                account?["asperaHost"]?.ToString(),
                account?["asperaSshPort"]?.GetValue<int>(),
                account?["asperaLogin"]?.ToString(),
                importFolder);
        }

        return null;
    }

    /// <summary>Non-deleted assets whose metadata <paramref name="tagName"/> equals <paramref name="value"/>.</summary>
    public async Task<IReadOnlyList<AssetSummary>> FindAssetsByMetadataAsync(string tagName, string value, CancellationToken ct = default)
    {
        var fields = await GetAssetMetadataFieldsAsync(ct);
        if (!fields.TryGetValue(tagName, out var field))
            throw new MediaspotException($"La métadonnée « {tagName} » n'existe pas sur la plateforme.");

        // "$type" first: the API binds the filter polymorphically.
        var body = new JsonObject
        {
            ["outputLayer"] = "Asset",
            ["pattern"] = "*",
            ["hideTitleWithoutAsset"] = false,
            ["metadata"] = new JsonArray(new JsonObject
            {
                ["$type"] = $"VDM.AirLab.Sdk.Core.Database.Types.{field.BaseType},VDM.AirLab.Sdk.Core",
                ["id"] = field.Id,
                ["fieldType"] = field.BaseType,
                ["targetLayer"] = "Asset",
                ["value"] = value,
            }),
        };

        var json = await SendAsync(HttpMethod.Post, ServiceUrl("search", "search-basic"), body, ct);
        return (json?["results"]?.AsArray() ?? [])
            .Where(r => r is not null && r["isDeleted"]?.GetValue<bool>() != true)
            .Select(r => new AssetSummary(
                r!["id"]!.GetValue<long>(),
                r["name"]?.ToString(),
                r["status"]?.ToString(),
                r["type"]?.ToString()))
            .Where(a => !string.Equals(a.Status, "Deleted", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    // --- Legacy Importer (what Mediaspot's Settings > Inventory > Legacy importer page calls) -----------------

    /// <summary>Ingest config packs of the platform (e.g. "RDD Pack"): they give the asset templates of an import.</summary>
    public async Task<IReadOnlyList<NamedId>> GetIngestConfigPacksAsync(CancellationToken ct = default)
    {
        var json = await SendAsync(HttpMethod.Get, ServiceUrl("ingest", "config/packs"), null, ct);
        return (json?["packs"]?.AsArray() ?? [])
            .Where(p => p?["id"] is not null)
            .Select(p => new NamedId(p!["id"]!.GetValue<long>(), p["name"]?.ToString() ?? $"#{p["id"]}"))
            .ToList();
    }

    /// <summary>Storages of kind LegacyImporter, where the import XLSX files are uploaded.</summary>
    public async Task<IReadOnlyList<NamedId>> GetLegacyImporterStoragesAsync(CancellationToken ct = default)
    {
        var json = await SendAsync(HttpMethod.Get, ServiceUrl("setup", "storages"), null, ct);
        return (json?["storages"]?.AsArray() ?? [])
            .Where(s => s?["kind"] is { } kind && (kind.ToString() == "LegacyImporter" || kind.ToString() == LegacyImporterStorageKind))
            .Select(s => new NamedId(s!["id"]!.GetValue<long>(), s["name"]?.ToString() ?? $"#{s["id"]}"))
            .ToList();
    }

    /// <summary>StorageKind.LegacyImporter as the API serialises it when enums are numbers.</summary>
    private const string LegacyImporterStorageKind = "13";

    /// <summary>Uploads a file to a storage (multipart: objectId=0, storageId, file — in that exact order).</summary>
    public async Task UploadToStorageAsync(long storageId, string filePath, CancellationToken ct = default)
    {
        await EnsureSessionAsync(ct);
        using var form = new MultipartFormDataContent
        {
            { new StringContent("0"), "objectId" },
            { new StringContent(storageId.ToString(System.Globalization.CultureInfo.InvariantCulture)), "storageId" },
        };
        var file = new StreamContent(File.OpenRead(filePath));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        form.Add(file, "file", Path.GetFileName(filePath));

        using var request = new HttpRequestMessage(HttpMethod.Post, ServiceUrl("upload", "upload")) { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", _sessionKey);
        using var response = await http.SendAsync(request, ct);
        await ReadJsonAsync(response, ct);
    }

    /// <summary>One Check or Import call of the Legacy Importer, on rows <paramref name="firstRow"/>..<paramref name="lastRow"/> (500 max).</summary>
    public async Task<LegacyImportResult> RunLegacyImportAsync(LegacyImportRequest request, CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["props"] = new JsonObject { ["fileName"] = request.FileName, ["storageId"] = request.StorageId, ["uploadDate"] = DateTime.UtcNow.ToString("O") },
            ["mode"] = request.Import ? "Import" : "Check",
            ["kind"] = request.Titles ? "Titles" : "Assets",
            ["type"] = "LegacyImport",
            ["firstRow"] = request.FirstRow,
            ["lastRow"] = request.LastRow,
            ["isTest"] = false,
            ["providerId"] = request.ProviderId,
            ["configIngestPackId"] = request.ConfigIngestPackId,
        };
        var json = await SendAsync(HttpMethod.Post, ServiceUrl("legacy-importer", "ops"), body, ct);
        var result = json?["result"];
        var reports = (result?["reports"]?.AsArray() ?? [])
            .Where(r => r is not null)
            .Select(r => new LegacyImportReport(
                // The importer reports Excel row + 1.
                (r!["line"]?.GetValue<int>() ?? 0) - 1,
                r["tag"]?.ToString() ?? "",
                r["message"]?.ToString() ?? "",
                r["possibleValues"] is JsonArray values ? string.Join(", ", values.Select(v => v?.ToString())) : r["possibleValues"]?.ToString()))
            .ToList();
        return new LegacyImportResult(result?["errorCount"]?.GetValue<int>() ?? reports.Count(r => r.IsError), reports);
    }

    /// <summary>Downloads the XLSX as the importer rewrote it (titleId, contentId, assetId filled).</summary>
    public async Task DownloadFromLegacyImporterAsync(long storageId, string fileName, string localPath, CancellationToken ct = default)
    {
        await EnsureSessionAsync(ct);
        var url = ServiceUrl("legacy-importer", $"download/{storageId}/{Uri.EscapeDataString(fileName)}");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", _sessionKey);
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new MediaspotException($"Téléchargement du XLSX traité impossible ({(int)response.StatusCode}) : {await response.Content.ReadAsStringAsync(ct)}");

        await using var output = File.Create(localPath);
        await response.Content.CopyToAsync(output, ct);
    }

    private async Task<JsonNode?> SendAsync(HttpMethod method, string url, JsonNode? body, CancellationToken ct)
    {
        await EnsureSessionAsync(ct);

        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", _sessionKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null)
            request.Content = JsonContent.Create(body);

        using var response = await http.SendAsync(request, ct);
        return await ReadJsonAsync(response, ct);
    }

    private async Task EnsureSessionAsync(CancellationToken ct)
    {
        if (IsConnected)
            return;
        if (_credentials is not { } c)
            throw new MediaspotException("Non connecté à Mediaspot.");
        await LoginAsync(c.Login, c.Password, c.PlatformId, ct);
    }

    private static async Task<JsonNode?> ReadJsonAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var text = await response.Content.ReadAsStringAsync(ct);
        JsonNode? json = null;
        try
        {
            json = string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            // Not JSON: reported below with the HTTP status.
        }

        if (!response.IsSuccessStatusCode)
            throw new MediaspotException($"Mediaspot a répondu {(int)response.StatusCode} : {ErrorMessage(json) ?? response.ReasonPhrase}");
        if (json?["success"] is { } success && success.GetValueKind() == JsonValueKind.False)
            throw new MediaspotException(ErrorMessage(json) ?? "Erreur Mediaspot.");
        return json;
    }

    private static string? ErrorMessage(JsonNode? json) =>
        json?["error"]?["message"]?.ToString() ?? json?["message"]?.ToString() ?? json?["error"]?.ToString();

    private string ServiceUrl(string service, string path) => $"{apiBaseUrl.TrimEnd('/')}/{service}-service/api/{path}";

    public void Dispose() => http.Dispose();
}

public sealed record MetadataField(long Id, string TagName, string BaseType, string Label, string Layer = "Asset");

/// <param name="ImportFolder">Where the Legacy Importer looks for the files, when the provider storage is S3.</param>
public sealed record ProviderAspera(string? ProviderName, string? Host, int? SshPort, string? Login, string? ImportFolder = null);

public sealed record AssetSummary(long Id, string? Name, string? Status, string? Type);

public sealed record NamedId(long Id, string Name)
{
    public override string ToString() => $"{Name} (#{Id})";
}

public sealed record LegacyImportRequest(string FileName, long StorageId, int FirstRow, int LastRow, long ProviderId, long ConfigIngestPackId, bool Import, bool Titles = false);

/// <param name="Row">Excel row number the report is about (already corrected from the importer's +1).</param>
public sealed record LegacyImportReport(int Row, string Tag, string Message, string? PossibleValues)
{
    public bool IsError => string.Equals(Tag, "Error", StringComparison.OrdinalIgnoreCase);
}

public sealed record LegacyImportResult(int ErrorCount, IReadOnlyList<LegacyImportReport> Reports);

public sealed class MediaspotException(string message) : Exception(message);
