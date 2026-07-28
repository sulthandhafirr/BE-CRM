using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CRM.Api.Data;
using CRM.Api.Services;
using System.Security.Claims;

namespace CRM.Api.Controllers
{
    [ApiController]
    [Route("api/rank")]
    public class AgentRankController : BaseController
    {
        private readonly AppDbContext _db;

        public AgentRankController(AppDbContext db, RoleService roleService) : base(roleService)
        {
            _db = db;
        }

        // GET /api/rank/agents-rank
        [HttpGet("agents-rank")]
        public async Task<IActionResult> GetAgentKpiRanking([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate)
        {
            var (role, companyId) = await GetCurrentUserRoleAndCompany();
            if (role != "admin" && role != "cs_agent") return Forbid();

            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

            var effectiveStart = startDate.HasValue 
                ? DateTime.SpecifyKind(startDate.Value, DateTimeKind.Utc)
                : (DateTime?)null;

            var effectiveEnd = endDate.HasValue
                ? DateTime.SpecifyKind(endDate.Value, DateTimeKind.Utc)
                : (DateTime?)null;

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
                    t.Status
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

                    // Response time penalty (ideal < 1hr = 3600sec, max -30pts)
                    if (t.ResponseTimeSec.HasValue)
                    {
                        double responseHours = t.ResponseTimeSec.Value / 3600.0;
                        double responsePenalty = Math.Min(responseHours / 1.0 * 10, 30);
                        score -= responsePenalty;
                        totalResponsePenalty += responsePenalty;
                    }
                    // else if (t.SlaBreached)
                    // {
                    //     score -= 30; // no response recorded = max penalty
                    // }

                    // Resolution time penalty relative to SLA (max -30pts)
                    if (t.ResolutionTimeSec.HasValue)
                    {
                        double slaHours = t.PriorityName switch
                        {
                            "Critical" => 24,
                            "High" => 48,
                            "Normal" => 72,
                            "Low" => 96,
                            _ => 72
                        };
                        double resolutionHours = t.ResolutionTimeSec.Value / 3600.0;
                        double resolutionRatio = resolutionHours / slaHours;
                        double resolutionPenalty = Math.Min(resolutionRatio * 15, 30);
                        score -= resolutionPenalty;
                        totalResolutionPenalty += resolutionPenalty;
                    }
                    // else if (t.SlaBreached)
                    // {
                    //     score -= 30; // unresolved = max penalty
                    // }

                    // 
                    if (t.SlaBreached)
                    {
                        score -= 30;
                        totalBreachPenalty += 30;
                    }

                    // Priority
                    double priorityWeight = t.PriorityName switch
                    {
                        "Critical" => 1.1,
                        "High" => 1.05,
                        "Normal" => 1.0,
                        "Low" => 1.0,
                        _ => 1.0
                    };

                    // Customer tier
                    double tierWeight = t.TierName switch
                    {
                        "Gold" => 1.1,
                        "Silver" => 1.05,
                        "Bronze" => 1.0,
                        _ => 1.0
                    };

                    score = score * priorityWeight * tierWeight;
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

                return new
                {
                    agentId = g.Key.AgentId,
                    agentName = g.Key.AgentName,
                    totalTickets = agentTickets.Count,
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
                    avgScore = Math.Round(scores.Average(), 2),
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
    }
}