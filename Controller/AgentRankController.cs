using System.Security.Claims;
using System.Text.Json;
using CRM.Api.Data;
using CRM.Api.Models;
using CRM.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CRM.Api.Controllers
{
    [ApiController]
    [Route("api/rank")]
    public class AgentRankController : BaseController
    {
        private readonly AppDbContext _db;
        private readonly PriorityEngineService _priorityEngineService;
        private static readonly JsonSerializerOptions SlaJsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };


        public AgentRankController(AppDbContext db, RoleService roleService, PriorityEngineService priorityEngineService) : base(roleService)
        {
            _db = db;
            _priorityEngineService = priorityEngineService;
        }

        // GET /api/rank/agents-rank
        [HttpGet("agents-rank")]
        public async Task<IActionResult> GetAgentKpiRanking([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate)
        {
            var (role, companyId) = await GetCurrentUserRoleAndCompany();
            if (role != "admin" && role != "cs_agent") return Forbid();
            if (companyId is null) return Unauthorized("User is not associated with a company.");

            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

            var effectiveStart = startDate.HasValue
                ? DateTime.SpecifyKind(startDate.Value, DateTimeKind.Utc)
                : (DateTime?)null;

            var effectiveEnd = endDate.HasValue
                ? DateTime.SpecifyKind(endDate.Value, DateTimeKind.Utc)
                : (DateTime?)null;

            if (!await _priorityEngineService.IsSlaEnabledAsync(companyId.Value))
            {
                return Ok(new { slaDisabled = true, message = "SLA monitoring is disabled for this company." });
            }

            var company = await _db.Companies
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == companyId);

            if (company is null)
            {
                return Unauthorized("User is not associated with a company.");
            }

            var slaConfig = ParseSlaConfig(company.SlaConfig);
            var slaRulesByPriority = slaConfig.Rules
                .Where(rule => !string.IsNullOrWhiteSpace(rule.Priority))
                .ToDictionary(rule => rule.Priority, rule => rule, StringComparer.OrdinalIgnoreCase);

            var currentWorkingCounts = await _db.Tickets
                .AsNoTracking()
                .Where(t => t.AgentId != null
                    && t.Customer!.CompanyId == companyId
                    && t.Status != "Solved"
                    && t.Status != "Resolved")
                .GroupBy(t => new { t.AgentId, AgentName = t.Agent!.Name })
                .Select(g => new
                {
                    AgentId = g.Key.AgentId!.Value,
                    g.Key.AgentName,
                    CurrentWorkingTickets = g.Count()
                })
                .ToListAsync();

            var currentWorkingMap = currentWorkingCounts.ToDictionary(x => x.AgentId, x => x.CurrentWorkingTickets);

            // only consider tickets that have an agent assigned
            var tickets = await _db.Tickets
                .AsNoTracking()
                .Where(t => t.AgentId != null
                    && t.Customer!.CompanyId == companyId
                    && t.ResolvedAt != null
                    && (!effectiveStart.HasValue || t.ResolvedAt >= effectiveStart)
                    && (!effectiveEnd.HasValue || t.ResolvedAt <= effectiveEnd))
                .Select(t => new
                {
                    t.AgentId,
                    AgentName = t.Agent!.Name,
                    PriorityName = t.Priority!.PriorityName,
                    TierName = t.Customer!.ProfileTiers
                        .Select(pt => pt.Tier!.TierName)
                        .FirstOrDefault(),
                    t.ResponseTimeSec,
                    t.ResolutionTimeSec,
                    t.SlaBreached,
                    t.Status,
                    t.SlaDeadline,
                    t.CreatedAt,
                })
                .ToListAsync();

            var agentGroups = tickets.GroupBy(t => new { t.AgentId, t.AgentName });

            var rankings = agentGroups.Select(g =>
            {
                var agentTickets = g.ToList();

                double totalResponsePenalty = 0;
                double totalResolutionPenalty = 0;
                double totalBreachPenalty = 0;

                var scores = agentTickets.Select(t =>
                {
                    double score = 100.0;
                    var resolutionTargetHours = GetResolutionTargetHours(t.PriorityName, slaRulesByPriority);

                    // Response time penalty only applies after the fixed one-hour target.
                    if (t.ResponseTimeSec.HasValue)
                    {
                        double responseHours = t.ResponseTimeSec.Value / 3600.0;
                        double responseExcessHours = Math.Max(0, responseHours - 1.0);
                        if (responseExcessHours > 0)
                        {
                            double responsePenalty = Math.Min(Math.Log10(1 + responseExcessHours * 1.5) * 40, 35);
                            score -= responsePenalty;
                            totalResponsePenalty += responsePenalty;
                        }
                    }
                    // else if (t.SlaBreached)
                    // {
                    //     score -= 30; // no response recorded = max penalty
                    // }

                    // Resolution time penalty only applies after the configured SLA target.
                    if (t.ResolutionTimeSec.HasValue && resolutionTargetHours > 0)
                    {
                        double resolutionHours = t.ResolutionTimeSec.Value / 3600.0;
                        double resolutionExcessRatio = Math.Max(0, resolutionHours / resolutionTargetHours - 1);
                        if (resolutionExcessRatio > 0)
                        {
                            double resolutionPenalty = Math.Min(Math.Log10(1 + resolutionExcessRatio) * 40, 40);
                            score -= resolutionPenalty;
                            totalResolutionPenalty += resolutionPenalty;
                        }
                    }

                    if (t.SlaBreached)
                    {
                        score -= 35;
                        totalBreachPenalty += 35;
                    }

                    double priorityBonus = t.PriorityName switch
                    {
                        "Critical" => 6,
                        "High" => 4,
                        "Normal" => 2,
                        "Low" => 1,
                        _ => 0
                    };

                    double tierBonus = t.TierName switch
                    {
                        "Gold" => 4,
                        "Silver" => 2,
                        "Bronze" => 1,
                        _ => 0
                    };

                    score += priorityBonus + tierBonus;
                    return Math.Max(0, Math.Min(100, score));
                }).ToList();

                var reasons = new Dictionary<string, double>
                {
                    { "slowResponse", totalResponsePenalty },
                    { "slowResolution", totalResolutionPenalty },
                    { "frequentBreaches", totalBreachPenalty },
                };
                var topReasonKey = reasons.Values.Max() > 0
                    ? reasons.OrderByDescending(r => r.Value).First().Key
                    : "consistentPerformance";

                var currentWorkingTickets = currentWorkingMap.TryGetValue(g.Key.AgentId!.Value, out var workingCount)
                    ? workingCount
                    : 0;

                return new
                {
                    agentId = g.Key.AgentId,
                    agentName = g.Key.AgentName,
                    totalTickets = agentTickets.Count,
                    currentWorkingTickets,
                    slaBreachedCount = agentTickets.Count(t => t.SlaBreached),
                    avgResponseTimeSec = agentTickets
                        .Where(t => t.ResponseTimeSec.HasValue)
                        .Select(t => (double?)t.ResponseTimeSec!.Value)
                        .DefaultIfEmpty(null)
                        .Average(),
                    avgResolutionTimeSec = agentTickets
                        .Where(t => t.Status == "Solved" && t.ResolutionTimeSec.HasValue)
                        .Select(t => (double?)t.ResolutionTimeSec!.Value)
                        .DefaultIfEmpty(null)
                        .Average(),
                    avgScore = Math.Round(Math.Max(0, scores.Average() - Math.Min(6.0, 6.0 / Math.Sqrt(agentTickets.Count))), 2),
                    slaBreachRate = agentTickets.Count > 0
                        ? Math.Round((double)agentTickets.Count(t => t.SlaBreached) / agentTickets.Count * 100, 1)
                        : 0,
                    topReasonKey,
                };
            })
            .OrderByDescending(a => a.avgScore)
            .ToList();

            // Admin sees everyone
            if (role == "admin")
            {
                return Ok(rankings);
            }

            // CS Agent only sees their own score
            var myRank = rankings.FindIndex(a => a.agentId == userId) + 1; // +1 for 1-based rank
            var myEntry = rankings.FirstOrDefault(a => a.agentId == userId);

            if (myEntry == null)
            {
                return Ok(new { hasData = false });
            }

            return Ok(new
            {
                hasData = true,
                rank = myRank,
                totalAgents = rankings.Count,
                agent = myEntry
            });
        }

        private static SlaRulesConfigDto ParseSlaConfig(string? json)
        {
            try
            {
                var config = string.IsNullOrWhiteSpace(json)
                    ? new SlaRulesConfigDto()
                    : JsonSerializer.Deserialize<SlaRulesConfigDto>(json, SlaJsonOptions) ?? new SlaRulesConfigDto();

                if (config.Rules.Count == 0)
                {
                    config.Rules = GetDefaultSlaRules();
                }

                return config;
            }
            catch
            {
                return new SlaRulesConfigDto
                {
                    EnableSlaMonitoring = true,
                    Rules = GetDefaultSlaRules()
                };
            }
        }

        private static List<SlaRuleItem> GetDefaultSlaRules()
        {
            return new List<SlaRuleItem>
            {
                new() { Priority = "Critical", FirstResponseHours = 1, ResolutionHours = 24 },
                new() { Priority = "High", FirstResponseHours = 2, ResolutionHours = 48 },
                new() { Priority = "Normal", FirstResponseHours = 8, ResolutionHours = 72 },
                new() { Priority = "Low", FirstResponseHours = 24, ResolutionHours = 96 },
            };
        }

        private static double GetResolutionTargetHours(string? priorityName, IReadOnlyDictionary<string, SlaRuleItem> rulesByPriority)
        {
            return rulesByPriority.TryGetValue(priorityName ?? string.Empty, out var rule)
                ? rule.ResolutionHours
                : 0;
        }
    }
}