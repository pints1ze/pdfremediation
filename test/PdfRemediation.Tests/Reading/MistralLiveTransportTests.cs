using System.Net;
using System.Text.Json.Nodes;
using PdfRemediation.Infrastructure.Reading;
using PdfRemediation.Infrastructure.Reading.Mistral;

namespace PdfRemediation.Tests.Reading;

/// <summary>
/// The live Mistral transport against a fake HTTP handler: the network boundary, so retries, timeouts and recording
/// are checked without a real call. Delays are captured, not waited for.
/// </summary>
public class MistralLiveTransportTests
{
    private const string ApiKey = "test-key-not-real-0123456789";
    private const string Body = """{"pages":[],"model":"mistral-ocr-4-1","usage_info":{"pages_processed":1}}""";
    private static readonly ReaderPage Page = new(1, 612, 792, [0x89, 0x50, 0x4E, 0x47]);

    private readonly string recordings = Directory.CreateTempSubdirectory("mistral-live-").FullName;
    private readonly List<TimeSpan> delays = [];

    [Fact]
    public async Task Transient_failures_are_retried_twice_after_2_and_8_seconds()
    {
        var handler = new FakeHandler(Status(HttpStatusCode.ServiceUnavailable), Status(HttpStatusCode.TooManyRequests), Ok(Body));

        var exchange = await Transport(handler).SendAsync(Page, CancellationToken.None);

        Assert.Equal((Body, 3, false), (exchange.Body, exchange.Tries, exchange.Replayed));
        Assert.Equal([TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(8)], delays);
    }

    [Fact]
    public async Task Three_transient_failures_give_READER_UNAVAILABLE()
    {
        var handler = new FakeHandler(Status(HttpStatusCode.BadGateway), Status(HttpStatusCode.ServiceUnavailable), Status(HttpStatusCode.ServiceUnavailable));

        var ex = await Assert.ThrowsAsync<ReaderException>(() => Transport(handler).SendAsync(Page, CancellationToken.None));

        Assert.Equal("READER_UNAVAILABLE", ex.Code);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Empty(Directory.GetFiles(recordings));
    }

    [Fact]
    public async Task A_request_that_times_out_is_retried_like_any_transient_failure()
    {
        var handler = new FakeHandler(Hang, Hang, Ok(Body));

        var exchange = await Transport(handler, requestTimeout: TimeSpan.FromMilliseconds(50)).SendAsync(Page, CancellationToken.None);

        Assert.Equal(3, exchange.Tries);
    }

    [Fact]
    public async Task A_client_error_is_not_retried()
    {
        var handler = new FakeHandler(Status(HttpStatusCode.Unauthorized, """{"message":"Unauthorized"}"""));

        var ex = await Assert.ThrowsAsync<ReaderException>(() => Transport(handler).SendAsync(Page, CancellationToken.None));

        Assert.Equal("READER_REQUEST_FAILED", ex.Code);
        Assert.Contains("401", ex.Message);
        Assert.Single(handler.Requests);
        Assert.Empty(delays);
    }

    [Fact]
    public async Task The_request_sends_the_page_render_and_asks_for_blocks_with_block_confidence()
    {
        var handler = new FakeHandler(Ok(Body));

        await Transport(handler).SendAsync(Page, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal((HttpMethod.Post, "https://ocr.example.test/v1/ocr"), (request.Method, request.Uri));
        Assert.Equal("Bearer " + ApiKey, request.Authorization);
        var json = JsonNode.Parse(request.Body)!;
        Assert.Equal("mistral-ocr-4-1", (string)json["model"]!);
        Assert.True((bool)json["include_blocks"]!);
        Assert.Equal("block", (string)json["confidence_scores_granularity"]!);
        Assert.Equal("image_url", (string)json["document"]!["type"]!);
        Assert.Equal("data:image/png;base64," + Convert.ToBase64String(Page.Png), (string)json["document"]!["image_url"]!);
    }

    [Fact]
    public async Task A_successful_response_is_recorded_verbatim_with_its_request_ID_and_without_the_key()
    {
        var response = Ok(Body);
        var handler = new FakeHandler(Status(HttpStatusCode.ServiceUnavailable), _ =>
        {
            var message = response(default);
            message.Headers.Add("x-request-id", "req-123");
            return message;
        });

        var exchange = await Transport(handler).SendAsync(Page, CancellationToken.None);

        Assert.Equal("req-123", exchange.RequestId);
        Assert.Equal(Body, File.ReadAllText(MistralRecording.BodyPath(recordings, 1)));
        var meta = JsonNode.Parse(File.ReadAllText(MistralRecording.MetaPath(recordings, 1)))!;
        Assert.Equal(("req-123", 2, "mistral-ocr-4-1"), ((string)meta["requestId"]!, (int)meta["tries"]!, (string)meta["model"]!));
        Assert.All(Directory.GetFiles(recordings), f => Assert.DoesNotContain(ApiKey, File.ReadAllText(f)));

        // And a replay of that recording returns the same exchange, without HTTP.
        var replayed = await new MistralReplayTransport(recordings).SendAsync(Page, CancellationToken.None);
        Assert.Equal((Body, "req-123", 2, true), (replayed.Body, replayed.RequestId, replayed.Tries, replayed.Replayed));
    }

    private MistralLiveTransport Transport(FakeHandler handler, TimeSpan? requestTimeout = null)
    {
        var options = MistralOptions.Default with
        {
            Endpoint = new Uri("https://ocr.example.test/v1/ocr"),
            RequestTimeout = requestTimeout ?? TimeSpan.FromSeconds(5),
        };
        return new MistralLiveTransport(new HttpClient(handler), ApiKey, options, recordings, (delay, _) =>
        {
            delays.Add(delay);
            return Task.CompletedTask;
        });
    }

    private static Func<CancellationToken, HttpResponseMessage> Ok(string body) => _ => new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private static Func<CancellationToken, HttpResponseMessage> Status(HttpStatusCode status, string body = "") => _ => new(status) { Content = new StringContent(body) };

    private static HttpResponseMessage Hang(CancellationToken token)
    {
        Task.Delay(Timeout.Infinite, token).GetAwaiter().GetResult();
        throw new InvalidOperationException("unreachable");
    }

    private sealed record SentRequest(HttpMethod Method, string Uri, string? Authorization, string Body);

    /// <summary>Answers each request with the next response in order and keeps what was sent.</summary>
    private sealed class FakeHandler(params Func<CancellationToken, HttpResponseMessage>[] responses) : HttpMessageHandler
    {
        private int next;

        public List<SentRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new(request.Method, request.RequestUri!.ToString(), request.Headers.Authorization?.ToString(), await request.Content!.ReadAsStringAsync(cancellationToken)));
            var respond = responses[next++];
            return await Task.Run(() => respond(cancellationToken), cancellationToken);
        }
    }
}
