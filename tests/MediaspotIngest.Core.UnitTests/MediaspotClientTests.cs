using System.Net;
using System.Text;
using MediaspotIngest.Core.Mediaspot;

namespace MediaspotIngest.Core.UnitTests;

public sealed class MediaspotClientTests
{
    private const string API_BASE_URL = "https://api.mediaspot.test";
    private const string SESSION_KEY = "session-key";

    [Fact]
    public async Task Every_request_carries_a_user_agent_because_cloudfront_rejects_those_without()
    {
        var handler = new RecordingHandler(LoginResponse());
        using var client = new MediaspotClient(new HttpClient(handler), API_BASE_URL);

        await client.LoginAsync("user", "password", 42);

        Assert.Equal(MediaspotClient.UserAgent, handler.Requests.Single().Headers.UserAgent.ToString());
    }

    [Fact]
    public async Task A_refused_login_reports_the_api_message()
    {
        var handler = new RecordingHandler("""{"success":false,"error":{"message":"Bad credentials"}}""");
        using var client = new MediaspotClient(new HttpClient(handler), API_BASE_URL);

        var error = await Assert.ThrowsAsync<MediaspotException>(() => client.LoginAsync("user", "wrong", 42));

        Assert.Contains("Bad credentials", error.Message);
    }

    [Fact]
    public async Task Login_posts_to_the_user_service_and_keeps_the_session_key()
    {
        var handler = new RecordingHandler(LoginResponse());
        using var client = new MediaspotClient(new HttpClient(handler), API_BASE_URL);

        await client.LoginAsync("user", "password", 42);

        Assert.Equal($"{API_BASE_URL}/user-service/api/login", handler.Requests.Single().RequestUri!.ToString());
        Assert.True(client.IsConnected);
    }

    [Fact]
    public async Task The_aspera_host_and_port_of_a_provider_are_read_from_the_setup_service_even_when_credentials_are_hidden()
    {
        var handler = new RecordingHandler(LoginResponse(),
            """{"success":true,"providersAndStorages":[{"provider":{"id":12,"labName":"Other","asperaAccount":{"asperaHost":"other.host"}}},{"provider":{"id":759,"labName":"VDM Europa","asperaAccount":{"asperaHost":"aspera.vdm.test","asperaSshPort":33001,"asperaTcpPort":33001,"asperaLogin":null,"asperaPassword":null}},"storageSource":{"storage":{"bucketName":"vdm-europa","docRoot":"/msp-asp-vdm-europa/"}}}]}""");
        using var client = new MediaspotClient(new HttpClient(handler), API_BASE_URL);
        await client.LoginAsync("user", "password", 42);

        var aspera = await client.GetProviderAsperaAsync(759);

        Assert.Equal(new ProviderAspera("VDM Europa", "aspera.vdm.test", 33001, null, "s3://vdm-europa/msp-asp-vdm-europa/temp/"), aspera);
        Assert.Equal($"{API_BASE_URL}/setup-service/api/provider", handler.Requests[1].RequestUri!.ToString());
    }

    private static string LoginResponse() =>
        $$$"""{"success":true,"sessionCreated":{"key":"{{{SESSION_KEY}}}","expireDate":"{{{DateTime.UtcNow.AddHours(4):O}}}"},"user":{"login":"user"}}""";

    /// <summary>Answers each request with the next response (the last one repeats).</summary>
    private sealed class RecordingHandler(params string[] responses) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var json = responses[Math.Min(Requests.Count - 1, responses.Length - 1)];
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }
}
