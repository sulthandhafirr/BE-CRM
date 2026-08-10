using CRM.Api.Data;
using CRM.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace CRM.Api.Services
{
    /// Calculates the final ticket priority: Priority Score + Intent Weight + Tier Score.
    public class PriorityEngineService
    {
        /// Score per priority level ("medium" = "normal").
        private static readonly Dictionary<string, int> PriorityScores = new(StringComparer.OrdinalIgnoreCase)
        {
            ["low"] = 25,
            ["normal"] = 50,
            ["medium"] = 50,
            ["high"] = 75,
            ["critical"] = 100,
        };

        private const int TierScoreStep = 5;
        private const int MaxTierScore = 20;

        private readonly AppDbContext _db;
        private readonly Dictionary<string, int> _intentWeights;

        public PriorityEngineService(AppDbContext db, IOptions<PriorityEngineOptions> options)
        {
            _db = db;
            // Case-insensitive copy.
            _intentWeights = new Dictionary<string, int>(options.Value.IntentWeights, StringComparer.OrdinalIgnoreCase);
        }

        /// Computes the final priority for a new ticket.
        public async Task<PriorityResult> ResolvePriorityAsync(
            string? description,
            Guid customerId,
            int companyId,
            string intent,
            double intentConfidence,
            string urgency,
            double urgencyConfidence,
            CancellationToken cancellationToken = default)
        {
            // 1. Priority score from AI
            var priorityScore = GetPriorityScore(urgency);

            // 2. Intent weight from config
            var intentWeight = GetIntentWeight(intent);

            // 3. Tier score from customer tier
            var tierScore = await GetTierScoreAsync(customerId, cancellationToken);

            // Final score → priority
            var finalScore = priorityScore + intentWeight + tierScore;
            var priorityName = MapScoreToPriorityName(finalScore);
            var resolutionHours = await GetResolutionHoursAsync(companyId, priorityName, cancellationToken);

            return new PriorityResult(
                PriorityName: priorityName,
                SlaResolutionHours: resolutionHours,
                PriorityScore: priorityScore,
                IntentWeight: intentWeight,
                TierScore: tierScore,
                FinalScore: finalScore);
        }

        /// Priority level → score.
        private static int GetPriorityScore(string? priority)
        {
            if (string.IsNullOrWhiteSpace(priority))
                return PriorityScores["normal"];

            return PriorityScores.TryGetValue(priority.Trim(), out var score) ? score : PriorityScores["normal"];
        }

        /// Weight for the intent (0 if unknown).
        private int GetIntentWeight(string? intent)
        {
            if (string.IsNullOrWhiteSpace(intent))
                return 0;

            return _intentWeights.TryGetValue(intent.Trim(), out var weight) ? weight : 0;
        }

        /// Tier score: min((Level - 1) × 5, 20).
        private async Task<int> GetTierScoreAsync(Guid customerId, CancellationToken cancellationToken)
        {
            try
            {
                var level = await _db.ProfileTiers
                    .AsNoTracking()
                    .Where(pt => pt.ProfileId == customerId)
                    .Select(pt => (int?)pt.Tier!.Level)
                    .FirstOrDefaultAsync(cancellationToken);

                if (!level.HasValue || level.Value <= 1)
                    return 0;

                return Math.Min((level.Value - 1) * TierScoreStep, MaxTierScore);
            }
            catch
            {
                return 0;
            }
        }

        /// Score → Low/Normal/High/Critical.
        private static string MapScoreToPriorityName(int score) => score switch
        {
            >= 100 => "Critical",
            >= 70 => "High",
            >= 40 => "Normal",
            _ => "Low"
        };

        private static readonly Dictionary<string, int> FallbackResolutionHours = new()
        {
            ["Critical"] = 24,
            ["High"] = 48,
            ["Normal"] = 72,
            ["Low"] = 96,
        };

        public async Task<bool> IsSlaEnabledAsync(int companyId, CancellationToken cancellationToken = default)
        {
            try
            {
                var company = await _db.Companies
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Id == companyId, cancellationToken);

                if (company?.SlaConfig is null)
                    return true; // no config = SLA enabled by default

                var config = JsonSerializer.Deserialize<SlaRulesConfigDto>(
                    company.SlaConfig,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                return config?.EnableSlaMonitoring ?? true;
            }
            catch
            {
                return true; // fall back to enabled
            }
        }

        private async Task<int?> GetResolutionHoursAsync(int companyId, string priorityName, CancellationToken cancellationToken)
        {
            if (!await IsSlaEnabledAsync(companyId, cancellationToken))
                return null;

            try
            {
                var company = await _db.Companies
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Id == companyId, cancellationToken);

                if (company?.SlaConfig is not null)
                {
                    var config = JsonSerializer.Deserialize<SlaRulesConfigDto>(
                        company.SlaConfig,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (config is not null && !config.EnableSlaMonitoring)
                        return null;

                    var rule = config?.Rules?.FirstOrDefault(r =>
                        string.Equals(r.Priority, priorityName, StringComparison.OrdinalIgnoreCase));

                    if (rule is not null)
                        return rule.ResolutionHours;
                }
            }
            catch
            {
                // fallback below
            }

            return FallbackResolutionHours.GetValueOrDefault(priorityName, 72);
        }

        public readonly record struct PriorityResult(
            string PriorityName,
            int? SlaResolutionHours,
            int PriorityScore,
            int IntentWeight,
            int TierScore,
            int FinalScore);
    }
}
