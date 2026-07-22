using System.Text.Json;

namespace CRM.Api.Services
{
    public class IntentAnalysisService
    {
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(8);

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;

        public IntentAnalysisService(IHttpClientFactory httpClientFactory, IConfiguration configuration)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
        }

        public async Task<IntentAnalysisResult> AnalyzeAsync(string? text, CancellationToken cancellationToken = default)
        {
            var normalizedText = string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim();

            if (string.IsNullOrWhiteSpace(normalizedText))
                return IntentAnalysisResult.Fallback();

            try
            {
                var httpClient = _httpClientFactory.CreateClient();
                var endpoint = GetIntentEndpoint();

                if (endpoint == null)
                    return IntentAnalysisResult.Fallback();

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(RequestTimeout);

                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(new { text = normalizedText }),
                        System.Text.Encoding.UTF8,
                        "application/json")
                };

                using var response = await httpClient.SendAsync(request, timeoutCts.Token);
                if (!response.IsSuccessStatusCode)
                    return IntentAnalysisResult.Fallback();

                await using var responseStream = await response.Content.ReadAsStreamAsync(timeoutCts.Token);
                using var document = await JsonDocument.ParseAsync(responseStream, cancellationToken: timeoutCts.Token);

                var root = document.RootElement;

                var intent = root.TryGetProperty("predicted_intent", out var intentElement)
                    ? intentElement.GetString()
                    : null;

                var confidence = root.TryGetProperty("confidence", out var confidenceElement) &&
                                  confidenceElement.TryGetDouble(out var confidenceValue)
                    ? confidenceValue
                    : 0d;

                if (string.IsNullOrWhiteSpace(intent))
                    return IntentAnalysisResult.Fallback();

                return new IntentAnalysisResult(
                    Text: normalizedText,
                    Intent: intent.Trim().ToLowerInvariant(),
                    Confidence: confidence,
                    AllScores: ExtractAllScores(root),
                    IsFallback: false);
            }
            catch
            {
                return IntentAnalysisResult.Fallback();
            }
        }

        private Dictionary<string, double> ExtractAllScores(JsonElement root)
        {
            var scores = new Dictionary<string, double>();
            if (root.TryGetProperty("all_scores", out var allScoresElement) && allScoresElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in allScoresElement.EnumerateArray())
                {
                    var label = item.TryGetProperty("label", out var labelEl) ? labelEl.GetString() : null;
                    var score = item.TryGetProperty("score", out var scoreEl) && scoreEl.TryGetDouble(out var sv) ? sv : 0d;
                    if (label != null)
                        scores[label] = score;
                }
            }
            return scores;
        }

        private Uri? GetIntentEndpoint()
        {
            var configuredEndpoint = _configuration["INTENT_API_URL"];

            if (string.IsNullOrWhiteSpace(configuredEndpoint))
                return null;

            return Uri.TryCreate(configuredEndpoint, UriKind.Absolute, out var parsedEndpoint)
                ? parsedEndpoint
                : null;
        }

        public sealed record IntentAnalysisResult(
            string Text,
            string Intent,
            double Confidence,
            Dictionary<string, double> AllScores,
            bool IsFallback)
        {
            public static IntentAnalysisResult Fallback() =>
                new(string.Empty, "unknown", 0d, new Dictionary<string, double>(), true);
        }
    }
}
