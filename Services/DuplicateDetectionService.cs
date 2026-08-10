using System.Text.Json;

namespace CRM.Api.Services
{
    public class DuplicateDetectionService
    {
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(25);

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;

        public DuplicateDetectionService(IHttpClientFactory httpClientFactory, IConfiguration configuration)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
        }

        // POST /duplicate — satu tiket vs banyak kandidat, dipakai saat buka halaman detail duplikat
        public async Task<DuplicateCheckResult> CheckDuplicatesAsync(
            string? text,
            IReadOnlyList<DuplicateCandidate> candidates,
            double threshold = 0.80,
            CancellationToken cancellationToken = default)
        {
            var normalizedText = string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim();

            if (string.IsNullOrWhiteSpace(normalizedText))
                return DuplicateCheckResult.Fallback();

            if (candidates == null || candidates.Count == 0)
                return DuplicateCheckResult.Empty();

            try
            {
                var httpClient = _httpClientFactory.CreateClient();
                var endpoint = GetEndpoint("duplicate");   // ← path asli app.py

                if (endpoint == null)
                    return DuplicateCheckResult.Fallback();

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(RequestTimeout);

                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(new
                        {
                            text = normalizedText,
                            candidates = candidates.Select(c => new { id = c.Id, text = c.Text }),
                            threshold,
                        }),
                        System.Text.Encoding.UTF8,
                        "application/json")
                };

                using var response = await httpClient.SendAsync(request, timeoutCts.Token);
                if (!response.IsSuccessStatusCode)
                    return DuplicateCheckResult.Fallback();

                await using var responseStream = await response.Content.ReadAsStreamAsync(timeoutCts.Token);
                using var document = await JsonDocument.ParseAsync(responseStream, cancellationToken: timeoutCts.Token);

                // ── app.py balikin ARRAY LANGSUNG, bukan {"duplicates": [...]} ──
                var matches = new List<DuplicateMatch>();
                if (document.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in document.RootElement.EnumerateArray())
                    {
                        var id = item.TryGetProperty("id", out var idEl) && idEl.TryGetInt64(out var idValue) ? idValue : (long?)null;
                        var score = item.TryGetProperty("score", out var scoreEl) && scoreEl.TryGetDouble(out var scoreValue) ? scoreValue : 0d;
                        if (id.HasValue)
                            matches.Add(new DuplicateMatch(id.Value, score));
                    }
                }

                return new DuplicateCheckResult(Matches: matches, IsFallback: false);
            }
            catch
            {
                return DuplicateCheckResult.Fallback();
            }
        }

        // POST /duplicate-clusters — semua tiket dibandingkan sekaligus, dipakai untuk badge count di list
        public async Task<Dictionary<long, int>> GetDuplicateCountsAsync(
            IReadOnlyList<DuplicateCandidate> tickets,
            double threshold = 0.80,
            CancellationToken cancellationToken = default)
        {
            var result = new Dictionary<long, int>();

            if (tickets == null || tickets.Count < 2)
                return result;

            try
            {
                var httpClient = _httpClientFactory.CreateClient();
                var endpoint = GetEndpoint("duplicate-clusters");

                if (endpoint == null)
                    return result;

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(RequestTimeout);

                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(new
                        {
                            tickets = tickets.Select(t => new { id = t.Id, text = t.Text }),
                            threshold,
                        }),
                        System.Text.Encoding.UTF8,
                        "application/json")
                };

                using var response = await httpClient.SendAsync(request, timeoutCts.Token);
                if (!response.IsSuccessStatusCode)
                    return result;

                await using var responseStream = await response.Content.ReadAsStreamAsync(timeoutCts.Token);
                using var document = await JsonDocument.ParseAsync(responseStream, cancellationToken: timeoutCts.Token);

                if (document.RootElement.TryGetProperty("counts", out var countsElement))
                {
                    foreach (var prop in countsElement.EnumerateObject())
                    {
                        if (long.TryParse(prop.Name, out var id) && prop.Value.TryGetInt32(out var count))
                            result[id] = count;
                    }
                }

                return result;
            }
            catch
            {
                return result;
            }
        }

        private Uri? GetEndpoint(string path)
        {
            var configuredEndpoint = _configuration["DUPLICATE_API_URL"];

            if (string.IsNullOrWhiteSpace(configuredEndpoint))
                return null;

            var baseUri = Uri.TryCreate(configuredEndpoint, UriKind.Absolute, out var parsed) ? parsed : null;
            if (baseUri == null)
                return null;

            return new Uri(baseUri, path);
        }
    }

    public record DuplicateCandidate(long Id, string Text);

    public record DuplicateMatch(long Id, double Score);

    public sealed record DuplicateCheckResult(List<DuplicateMatch> Matches, bool IsFallback)
    {
        public static DuplicateCheckResult Fallback() =>
            new(new List<DuplicateMatch>(), true);

        public static DuplicateCheckResult Empty() =>
            new(new List<DuplicateMatch>(), false);
    }
}