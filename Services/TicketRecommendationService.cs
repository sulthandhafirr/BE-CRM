using Microsoft.EntityFrameworkCore;
using CRM.Api.Data;
using System.Text.Json.Serialization;

namespace CRM.Api.Services
{
    public class TicketRecommendationService
    {
        private readonly AppDbContext _db;
        private readonly HttpClient _httpClient;
        private readonly string _apiUrl;

        public TicketRecommendationService(AppDbContext db, IHttpClientFactory httpClientFactory, IConfiguration config)
        {
            _db = db;
            _httpClient = httpClientFactory.CreateClient();
            _apiUrl = config["TICKET_RECOMMENDATION_API_URL"] ?? throw new Exception("TICKET_RECOMMENDATION_API_URL not set");
        }

        public async Task<List<RecommendedTicketDto>> GetRecommendedTicketsAsync(Guid agentId, int? companyId)
        {
            var currentWorkload = await _db.Tickets
                .Where(t => t.AgentId == agentId && t.Status != "Resolved" && t.Customer!.CompanyId == companyId)
                .CountAsync();

            var avgResolutionSec = await _db.Tickets
                .Where(t => t.AgentId == agentId && t.ResolutionTimeSec.HasValue && t.Customer!.CompanyId == companyId)
                .AverageAsync(t => (double?)t.ResolutionTimeSec) ?? 3600; // default 1hr if no history

            var agentAvgResolutionHrs = avgResolutionSec / 3600.0;

            var avgResponseSec = await _db.Tickets
                .Where(t => t.AgentId == agentId && t.ResponseTimeSec.HasValue && t.Customer!.CompanyId == companyId)
                .AverageAsync(t => (double?)t.ResponseTimeSec) ?? 1800; // default 30min if no history

            var agentAvgResponseHrs = avgResponseSec / 3600.0;

            var unassignedTickets = await _db.Tickets
                .AsNoTracking()
                .Where(t => t.AgentId == null && t.Status == "Waiting" && t.Customer!.CompanyId == companyId)
                .Select(t => new
                {
                    t.Id,
                    t.Subject,
                    t.Status,
                    t.CreatedAt,
                    t.Priority!.PriorityName,
                    TierName = t.Customer!.ProfileTiers
                        .Select(pt => pt.Tier!.TierName)
                        .FirstOrDefault()
                })
                .ToListAsync();

            if (!unassignedTickets.Any())
                return new List<RecommendedTicketDto>();

            var ticketFeatures = unassignedTickets.Select(t => new
            {
                current_workload = currentWorkload,
                ticket_priority = t.PriorityName ?? "Normal",
                ticket_age_hours = (DateTime.UtcNow - t.CreatedAt).TotalHours,
                agent_avg_resolution_hrs = agentAvgResolutionHrs,
                agent_avg_response_hrs = agentAvgResponseHrs
            }).ToList();

            var payload = new
            {
                tickets = ticketFeatures,
                ticket_ids = unassignedTickets.Select(t => t.Id).ToList()
            };

            // Hugging Face API
            var response = await _httpClient.PostAsJsonAsync($"{_apiUrl}/recommend", payload);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<RecommendationApiResponse>();
            if (result?.Recommendations == null)
                return new List<RecommendedTicketDto>();

            var ticketMap = unassignedTickets.ToDictionary(t => t.Id);

            static double CalcEstimatedImpact(string? priority, string? tier, double ticketAgeHours, int currentWorkload)
            {
                double score = 100.0;

                double priorityBonus = priority switch
                {
                    "Critical" => 6,
                    "High" => 4,
                    "Normal" => 2,
                    "Low" => 1,
                    _ => 0
                };

                double tierBonus = tier switch
                {
                    "Gold" => 4,
                    "Silver" => 2,
                    "Bronze" => 1,
                    _ => 0
                };

                var agePenalty = Math.Min(Math.Log10(1 + Math.Max(0, ticketAgeHours)) * 8, 12);
                var workloadPenalty = Math.Min(currentWorkload * 1.5, 15);

                score += priorityBonus + tierBonus - agePenalty - workloadPenalty;
                return Math.Round(Math.Max(0, Math.Min(100, score)), 2);
            }

            return result.Recommendations.Select(r =>
            {
                var estimatedImpact = CalcEstimatedImpact(
                    ticketMap[r.TicketId].PriorityName,
                    ticketMap[r.TicketId].TierName,
                    (DateTime.UtcNow - ticketMap[r.TicketId].CreatedAt).TotalHours,
                    currentWorkload
                );
                var blended = (r.MatchScore * 0.5) + (estimatedImpact * 0.5);

                return new RecommendedTicketDto
                {
                    TicketId = r.TicketId,
                    Subject = ticketMap[r.TicketId].Subject ?? "",
                    Priority = ticketMap[r.TicketId].PriorityName ?? "Normal",
                    Status = ticketMap[r.TicketId].Status ?? "",
                    CreatedAt = ticketMap[r.TicketId].CreatedAt,
                    TicketAgeHours = Math.Round((DateTime.UtcNow - ticketMap[r.TicketId].CreatedAt).TotalHours, 1),
                    MatchScore = r.MatchScore,
                    EstimatedScoreImpact = estimatedImpact,
                    BlendedScore = Math.Round(blended, 2)
                };
            })
            .OrderByDescending(t => t.BlendedScore)
            .ThenByDescending(t => t.TicketAgeHours)
            .ToList();
        }
    }

    public class RecommendedTicketDto
    {
        public long TicketId { get; set; }
        public string Subject { get; set; } = "";
        public string Priority { get; set; } = "";
        public string Status { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public double TicketAgeHours { get; set; }
        public double MatchScore { get; set; }
        public double EstimatedScoreImpact { get; set; }
        public double BlendedScore { get; set; }

    }

    public class RecommendationApiResponse
    {
        [JsonPropertyName("recommendations")]
        public List<RecommendationItem>? Recommendations { get; set; }
    }

    public class RecommendationItem
    {
        [JsonPropertyName("ticket_id")]
        public long TicketId { get; set; }

        [JsonPropertyName("match_score")]
        public double MatchScore { get; set; }
    }
}