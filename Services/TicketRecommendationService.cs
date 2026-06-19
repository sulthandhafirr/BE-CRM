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

        public async Task<List<RecommendedTicketDto>> GetRecommendedTicketsAsync(Guid agentId)
        {
            var currentWorkload = await _db.Tickets
                .Where(t => t.AgentId == agentId && t.Status != "Resolved")
                .CountAsync();

            var avgResolutionSec = await _db.Tickets
                .Where(t => t.AgentId == agentId && t.ResolutionTimeSec.HasValue)
                .AverageAsync(t => (double?)t.ResolutionTimeSec) ?? 3600; // default 1hr if no history

            var agentAvgResolutionHrs = avgResolutionSec / 3600.0;

            var unassignedTickets = await _db.Tickets
                .AsNoTracking()
                .Where(t => t.AgentId == null && t.Status == "Waiting")
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
                agent_avg_resolution_hrs = agentAvgResolutionHrs
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

            static double CalcEstimatedImpact(string? priority, string? tier)
            {
                double score = 65.0; // assume base score

                double priorityWeight = priority switch
                {
                    "Critical" => 1.1,
                    "High" => 1.05,
                    "Normal" => 1.0,
                    "Low" => 1.0,
                    _ => 1.0
                };

                double tierWeight = tier switch
                {
                    "Gold" => 1.1,
                    "Silver" => 1.05,
                    "Bronze" => 1.0,
                    _ => 1.0
                };

                return Math.Round(Math.Min(100, score * priorityWeight * tierWeight), 2); // cap at 100, 2 decimal places
            }

            return result.Recommendations.Select(r => new RecommendedTicketDto
            {
                TicketId = r.TicketId,
                Subject = ticketMap[r.TicketId].Subject ?? "",
                Priority = ticketMap[r.TicketId].PriorityName ?? "Normal",
                Status = ticketMap[r.TicketId].Status ?? "",
                CreatedAt = ticketMap[r.TicketId].CreatedAt,
                TicketAgeHours = Math.Round((DateTime.UtcNow - ticketMap[r.TicketId].CreatedAt).TotalHours, 1),
                MatchScore = r.MatchScore,
                EstimatedScoreImpact = CalcEstimatedImpact(
                    ticketMap[r.TicketId].PriorityName,
                    ticketMap[r.TicketId].TierName
                )
            })
            .OrderByDescending(t => t.EstimatedScoreImpact)
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