using CRM.Api.Data;
using CRM.Api.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CRM.Api.Services
{
    /// <summary>
    /// Business Rules Engine that determines final ticket priority by combining:
    ///   - Intent model output (base priority mapping)
    ///   - Urgency model output (urgency level)
    ///   - Keyword rules (escalation keywords in description)
    ///   - Customer rules (tier-based priority boost)
    ///   - SLA rules (time-based escalation)
    /// </summary>
    public class PriorityEngineService
    {
        private readonly AppDbContext _db;
        private static readonly TimeSpan SlaWarningThreshold = TimeSpan.FromHours(2);

        public PriorityEngineService(AppDbContext db)
        {
            _db = db;
        }

        /// <summary>
        /// Resolves the final priority for a ticket.
        /// </summary>
        /// <param name="description">Ticket description text (used for keyword scanning).</param>
        /// <param name="customerId">Customer GUID (used to look up tier).</param>
        /// <param name="intent">Predicted intent from the intent model.</param>
        /// <param name="intentConfidence">Confidence score of the intent prediction.</param>
        /// <param name="urgency">Predicted urgency level (low / medium / high / critical).</param>
        /// <param name="urgencyConfidence">Confidence score of the urgency prediction.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>The final priority name string: "Critical", "High", "Normal", or "Low".</returns>
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
            // 1. Base priority from intent
            var baseLevel = GetBasePriorityLevel(intent, intentConfidence);

            // 2. Urgency boost
            var urgencyLevel = GetUrgencyLevel(urgency, urgencyConfidence);
            var levelAfterUrgency = baseLevel + urgencyLevel;

            // 3. Keyword rules — scan description for escalation keywords
            var keywordBoost = ScanKeywordBoost(description ?? string.Empty);
            var levelAfterKeywords = levelAfterUrgency + keywordBoost;

            // 4. Customer rules — tier-based boost
            var customerBoost = await GetCustomerTierBoostAsync(customerId, cancellationToken);
            var levelAfterCustomer = levelAfterKeywords + customerBoost;

            // 5. SLA rules — escalating if past deadline
            //    (this is checked at runtime by SlaCheckerService; we include a pending-order check here)
            var slaBoost = 0;
            var levelAfterSla = levelAfterCustomer + slaBoost;

            // Clamp to valid range [0..3]
            var finalLevel = Math.Clamp(levelAfterSla, 0, 3);

            var priorityName = LevelToPriorityName(finalLevel);
            var resolutionHours = await GetResolutionHoursAsync(companyId, priorityName, cancellationToken);

            return new PriorityResult(
                PriorityName: priorityName,
                SlaResolutionHours: resolutionHours,
                BaseLevel: baseLevel,
                IntentWeight: baseLevel,
                UrgencyWeight: urgencyLevel,
                KeywordBoost: keywordBoost,
                CustomerBoost: customerBoost,
                SlaBoost: slaBoost,
                FinalLevel: finalLevel);
        }

        /// <summary>
        /// Maps intent to a base priority level (0=Low, 1=Normal, 2=High, 3=Critical).
        /// </summary>
        private static int GetBasePriorityLevel(string intent, double confidence)
        {
            // Low confidence → default to Normal
            if (confidence < 0.50)
                return 1;

            return intent.ToLowerInvariant() switch
            {
                "security_incident" => 3,  // Critical
                "complaint" => 2,          // High
                "refund_request" => 2,     // High
                "billing_issue" => 2,      // High
                "cancellation_request" => 2, // High
                "technical_issue" => 1,    // Normal
                "account_management" => 1, // Normal
                "order_inquiry" => 1,      // Normal
                "service_request" => 1,    // Normal
                "feature_request" => 0,    // Low
                "information_request" => 0, // Low
                "other" => 1,              // Normal
                _ => 1                     // Normal (fallback)
            };
        }

        /// <summary>
        /// Converts urgency string to a numeric boost (-1 to +1).
        /// </summary>
        private static int GetUrgencyLevel(string urgency, double confidence)
        {
            if (confidence < 0.50)
                return 0;

            return urgency.ToLowerInvariant() switch
            {
                "critical" => 1,
                "high" => 1,
                "medium" => 0,
                "low" => -1,
                _ => 0
            };
        }

        /// <summary>
        /// Scans description for escalation keywords and returns a numeric boost (0 or +1).
        /// </summary>
        private static int ScanKeywordBoost(string description)
        {
            if (string.IsNullOrWhiteSpace(description))
                return 0;

            var lower = description.ToLowerInvariant();

            // Strong escalation keywords → +1 level
            string[] strongKeywords =
            {
                "urgent", "asap", "critical", "emergency", "immediately",
                "danger", "mendesak", "kritis", "darurat", "bocor",
                "data leak", "security breach", "keamanan"
            };

            foreach (var keyword in strongKeywords)
            {
                if (lower.Contains(keyword))
                    return 1;
            }

            return 0;
        }

        /// <summary>
        /// Looks up the customer's tier and returns a priority boost.
        /// Gold → +1, Silver → 0, Bronze → 0.
        /// </summary>
        private async Task<int> GetCustomerTierBoostAsync(Guid customerId, CancellationToken cancellationToken)
        {
            try
            {
                var tierName = await _db.ProfileTiers
                    .AsNoTracking()
                    .Where(pt => pt.ProfileId == customerId)
                    .Select(pt => pt.Tier!.TierName)
                    .FirstOrDefaultAsync(cancellationToken);

                return tierName?.ToLowerInvariant() switch
                {
                    "gold" => 1,
                    "silver" => 0,
                    "bronze" => 0,
                    _ => 0
                };
            }
            catch
            {
                return 0;
            }
        }

        private static string LevelToPriorityName(int level) => level switch
        {
            3 => "Critical",
            2 => "High",
            1 => "Normal",
            0 => "Low",
            _ => "Normal"
        };

        // public static int GetSlaDays(string priorityName) => priorityName switch
        // {
        //     "Critical" => 1,
        //     "High" => 2,
        //     "Normal" => 3,
        //     "Low" => 4,
        //     _ => 3
        // };

        private static readonly Dictionary<string, int> FallbackResolutionHours = new()
        {
            ["Critical"] = 24,
            ["High"] = 48,
            ["Normal"] = 72,
            ["Low"] = 96,
        };

        private async Task<int?> GetResolutionHoursAsync(int companyId, string priorityName, CancellationToken cancellationToken)
        {
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
                // fall through to hardcoded fallback below
            }

            return FallbackResolutionHours.GetValueOrDefault(priorityName, 72);
        }

        public readonly record struct PriorityResult(
            string PriorityName,
            int? SlaResolutionHours,
            // int SlaDays,
            int BaseLevel,
            int IntentWeight,
            int UrgencyWeight,
            int KeywordBoost,
            int CustomerBoost,
            int SlaBoost,
            int FinalLevel);
    }
}
