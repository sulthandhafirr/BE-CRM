using System.Text.Json;

namespace CRM.Api.Services
{
    public class UrgencyAnalysisService
    {
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(8);

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;

        public UrgencyAnalysisService(IHttpClientFactory httpClientFactory, IConfiguration configuration)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
        }

        public async Task<UrgencyAnalysisResult> AnalyzeAsync(string? text, CancellationToken cancellationToken = default)
        {
            var normalizedText = string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim();

            if (string.IsNullOrWhiteSpace(normalizedText))
                return UrgencyAnalysisResult.Fallback();

            try
            {
                var httpClient = _httpClientFactory.CreateClient();
                var endpoint = GetUrgencyEndpoint();

                if (endpoint == null)
                    return UrgencyAnalysisResult.Fallback();

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
                    return UrgencyAnalysisResult.Fallback();

                await using var responseStream = await response.Content.ReadAsStreamAsync(timeoutCts.Token);
                using var document = await JsonDocument.ParseAsync(responseStream, cancellationToken: timeoutCts.Token);

                var root = document.RootElement;

                // Attempt to parse "urgency" or "predicted_urgency" from response
                var urgency = root.TryGetProperty("urgency", out var urgencyEl)
                    ? urgencyEl.GetString()
                    : root.TryGetProperty("predicted_urgency", out var predUrgencyEl)
                        ? predUrgencyEl.GetString()
                        : null;

                var confidence = root.TryGetProperty("confidence", out var confidenceEl) &&
                                  confidenceEl.TryGetDouble(out var confidenceVal)
                    ? confidenceVal
                    : 0d;

                if (string.IsNullOrWhiteSpace(urgency))
                    return UrgencyAnalysisResult.Fallback();

                return new UrgencyAnalysisResult(
                    Text: normalizedText,
                    Urgency: urgency.Trim().ToLowerInvariant(),
                    Confidence: confidence,
                    IsFallback: false);
            }
            catch
            {
                return UrgencyAnalysisResult.Fallback();
            }
        }

        private Uri? GetUrgencyEndpoint()
        {
            var configuredEndpoint = _configuration["URGENCY_API_URL"];

            if (string.IsNullOrWhiteSpace(configuredEndpoint))
                return null;

            return Uri.TryCreate(configuredEndpoint, UriKind.Absolute, out var parsedEndpoint)
                ? parsedEndpoint
                : null;
        }

        public sealed record UrgencyAnalysisResult(
            string Text,
            string Urgency,
            double Confidence,
            bool IsFallback)
        {
            public static UrgencyAnalysisResult Fallback() =>
                new(string.Empty, "medium", 1.0, true);
        }
    }
}
