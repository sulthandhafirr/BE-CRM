/*
using System.Text.Json;

namespace CRM.Api.Services
{
    public class SentimentAnalysisService
    {
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(8);

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;

        public SentimentAnalysisService(IHttpClientFactory httpClientFactory, IConfiguration configuration)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
        }

        public async Task<SentimentAnalysisResult> AnalyzeAsync(string? text, CancellationToken cancellationToken = default)
        {
            var normalizedText = string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim();

            if (string.IsNullOrWhiteSpace(normalizedText))
                return SentimentAnalysisResult.Fallback();

            try
            {
                var httpClient = _httpClientFactory.CreateClient();
                var endpoint = GetSentimentEndpoint();

                if (endpoint == null)
                    return SentimentAnalysisResult.Fallback();

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
                    return SentimentAnalysisResult.Fallback();

                await using var responseStream = await response.Content.ReadAsStreamAsync(timeoutCts.Token);
                using var document = await JsonDocument.ParseAsync(responseStream, cancellationToken: timeoutCts.Token);

                var root = document.RootElement;
                var sentiment = root.TryGetProperty("sentiment", out var sentimentElement)
                    ? sentimentElement.GetString()
                    : null;

                var confidence = root.TryGetProperty("confidence", out var confidenceElement) &&
                                  confidenceElement.TryGetDouble(out var confidenceValue)
                    ? confidenceValue
                    : 0d;

                if (string.IsNullOrWhiteSpace(sentiment))
                    return SentimentAnalysisResult.Fallback();

                return new SentimentAnalysisResult(
                    Text: normalizedText,
                    Sentiment: sentiment.Trim().ToUpperInvariant(),
                    Confidence: confidence,
                    IsFallback: false);
            }
            catch
            {
                return SentimentAnalysisResult.Fallback();
            }
        }

        private Uri? GetSentimentEndpoint()
        {
            var configuredEndpoint = _configuration["SENTIMENT_API_URL"];

            if (string.IsNullOrWhiteSpace(configuredEndpoint))
                return null;

            return Uri.TryCreate(configuredEndpoint, UriKind.Absolute, out var parsedEndpoint)
                ? parsedEndpoint
                : null;
        }

        public static string ResolvePriorityName(string sentiment, double confidence)
        {
            var normalizedSentiment = sentiment?.Trim().ToUpperInvariant();

            return normalizedSentiment switch
            {
                "POSITIVE" => "Low",
                "NEGATIVE" when confidence < 0.70 => "Normal",
                "NEGATIVE" when confidence < 0.90 => "High",
                "NEGATIVE" => "Critical",
                _ => "Normal"
            };
        }

        public sealed record SentimentAnalysisResult(string Text, string Sentiment, double Confidence, bool IsFallback)
        {
            public static SentimentAnalysisResult Fallback() => new(string.Empty, "UNKNOWN", 0d, true);
        }
    }
}
*/