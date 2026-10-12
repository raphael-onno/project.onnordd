using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using MediaspotIngest.Core.Domain;
using MediaspotIngest.Core.Mediaspot;

namespace MediaspotIngest.Core.UnitTests;

public sealed class TitleCreatorTests
{
    private const string TITLE_CUSTOMER_ID = "EC0001";
    private const long EXISTING_TITLE_ID = 5001;
    private const long CREATED_TITLE_ID = 6001;

    private const string LOGIN = """{"success":true,"sessionCreated":{"key":"k","expireDate":"2099-01-01T00:00:00Z"}}""";
    private const string FIELDS = """
        {"success":true,"metadataFields":[
          {"id":1,"tagName":"titleType","baseType":"DbMetadataString","label":"Title Type","layer":"Title"},
          {"id":2,"tagName":"titleOriginalPictureFormat","baseType":"DbMetadataString","label":"Picture Format","layer":"Title"},
          {"id":3,"tagName":"titleOriginalLanguage","baseType":"DbMetadataLanguageCode","label":"Original Language","layer":"Title"},
          {"id":4,"tagName":"productionDate","baseType":"DbMetadataDateTime","label":"Production Date","layer":"Title"}]}
        """;
    private const string LANGUAGES = """{"success":true,"languages":[{"id":10,"externalId":0,"value":"FRA","label":"French","rfc5646":"fr"}]}""";
    private const string NO_TITLE = """{"success":true,"results":[]}""";

    [Fact]
    public async Task A_title_already_in_mediaspot_with_the_same_title_id_is_reused_not_duplicated()
    {
        var handler = new ScriptedHandler(LOGIN,
            $$"""{"success":true,"results":[{"id":{{EXISTING_TITLE_ID}},"name":"20 ANS D’ECART","titleCustomerId":"{{TITLE_CUSTOMER_ID}}","status":"Accepted"}]}""");
        var creator = await CreatorAsync(handler);

        var result = await creator.CreateOrFindAsync(Title(), "FRA", "1.77");

        Assert.Equal(TitleCreation.Found(EXISTING_TITLE_ID, "20 ANS D’ECART"), result);
        Assert.DoesNotContain(handler.Requests, r => r.Uri.EndsWith("/title-service/api/create"));
    }

    [Fact]
    public async Task A_new_title_is_created_with_its_metadata_and_the_normalised_name()
    {
        var handler = new ScriptedHandler(LOGIN, NO_TITLE, LANGUAGES, FIELDS, $$"""{"success":true,"objectId":{{CREATED_TITLE_ID}}}""");
        var creator = await CreatorAsync(handler);

        var result = await creator.CreateOrFindAsync(Title(), "FRA", "1.77");

        Assert.Equal(TitleCreationOutcome.Created, result.Outcome);
        Assert.Equal(CREATED_TITLE_ID, result.TitleId);
        var body = JsonNode.Parse(handler.Requests.Single(r => r.Uri.EndsWith("/title-service/api/create")).Body)!;
        Assert.Equal("20 ANS D’ECART", body["name"]!.ToString());
        Assert.Equal(TITLE_CUSTOMER_ID, body["titleCustomerId"]!.ToString());
        Assert.Equal($"#{TITLE_CUSTOMER_ID} 20 ANS D’ECART (2013)", body["nameAlt"]!.ToString());
        var language = body["metadata"]!.AsArray().Single(m => m!["fieldName"]!.ToString() == "titleOriginalLanguage")!;
        Assert.Equal("$type", language.AsObject().GetAt(0).Key);
        Assert.Equal("FRA", language["value"]!["value"]!.ToString());
    }

    [Fact]
    public async Task A_language_unknown_to_the_platform_stops_the_creation()
    {
        var handler = new ScriptedHandler(LOGIN, NO_TITLE, LANGUAGES);
        var creator = await CreatorAsync(handler);
        var title = Title();
        title.OriginalLanguage = "XYZ";

        var result = await creator.CreateOrFindAsync(title, "FRA", "1.77");

        Assert.Equal(TitleCreationOutcome.Failed, result.Outcome);
        Assert.Contains("XYZ", result.Error);
    }

    [Fact]
    public void A_title_without_year_cannot_be_created_but_the_title_id_is_optional()
    {
        var title = new TitleInfo { MainTitle = "20 ANS D'ECART" };

        Assert.Equal(["année manquante"], TitleCreator.Check(title));
    }

    [Fact]
    public async Task Without_a_title_id_an_existing_title_is_found_by_its_exact_name()
    {
        var handler = new ScriptedHandler(LOGIN,
            $$"""{"success":true,"results":[{"id":{{EXISTING_TITLE_ID}},"name":"20 ANS D’ECART","status":"Accepted"},{"id":7,"name":"20 ANS D’ECART 2","status":"Accepted"}]}""");
        var creator = await CreatorAsync(handler);
        var title = Title();
        title.TitleCustomId = null;

        var result = await creator.CreateOrFindAsync(title, "FRA", "1.77");

        Assert.Equal(TitleCreation.Found(EXISTING_TITLE_ID, "20 ANS D’ECART"), result);
    }

    [Fact]
    public async Task Without_a_title_id_a_new_title_is_created_without_customer_id()
    {
        var handler = new ScriptedHandler(LOGIN, NO_TITLE, LANGUAGES, FIELDS, $$"""{"success":true,"objectId":{{CREATED_TITLE_ID}}}""");
        var creator = await CreatorAsync(handler);
        var title = Title();
        title.TitleCustomId = null;

        var result = await creator.CreateOrFindAsync(title, "FRA", "1.77");

        Assert.Equal(CREATED_TITLE_ID, result.TitleId);
        var body = JsonNode.Parse(handler.Requests.Single(r => r.Uri.EndsWith("/title-service/api/create")).Body)!;
        Assert.Null(body["titleCustomerId"]);
        Assert.Equal("20 ANS D’ECART (2013)", body["nameAlt"]!.ToString());
    }

    [Theory]
    [InlineData("20 ans d'écart", "20 ANS D’ÉCART")]
    [InlineData("AC/DC live", "AC DC LIVE")]
    public void The_mediaspot_name_follows_the_importer_normalisation(string mainTitle, string expected)
    {
        Assert.Equal(expected, TitleCreator.MediaspotName(mainTitle));
    }

    private static TitleInfo Title() => new()
    {
        TitleKey = "20-ANS-D-ECART",
        TitleCustomId = TITLE_CUSTOMER_ID,
        MainTitle = "20 ans d'ecart",
        ProductionYear = 2013,
    };

    private static async Task<TitleCreator> CreatorAsync(ScriptedHandler handler)
    {
        var client = new MediaspotClient(new HttpClient(handler), "https://api.mediaspot.test");
        await client.LoginAsync("user", "password", 42);
        return new TitleCreator(client);
    }

    /// <summary>Answers requests in order with the scripted responses (the last one repeats).</summary>
    private sealed class ScriptedHandler(params string[] responses) : HttpMessageHandler
    {
        public List<(string Uri, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.RequestUri!.ToString(), body));
            var json = responses[Math.Min(Requests.Count - 1, responses.Length - 1)];
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
