using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CRM.Api.Data;
using CRM.Api.Services;

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
        public async Task<IActionResult> GetAgentKpiRanking()
        {
            var (role, companyId) = await GetCurrentUserRoleAndCompany();
            if (role != "admin" && role != "cs_agent") return Forbid();

            var tickets = await _db.Tickets
                .AsNoTracking()
                .Where(t => t.AgentId != null && t.Customer!.CompanyId == companyId)
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

                var scores = agentTickets.Select(t =>
                {
                    double score = 100.0;

                    // Response time penalty (ideal < 1hr = 3600sec, max -30pts)
                    if (t.ResponseTimeSec.HasValue)
                    {
                        double responseHours = t.ResponseTimeSec.Value / 3600.0;
                        double responsePenalty = Math.Min(responseHours / 1.0 * 10, 30);
                        score -= responsePenalty;
                    }
                    else
                    {
                        score -= 30; // no response recorded = max penalty
                    }

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
                    }
                    else
                    {
                        score -= 30; // unresolved = max penalty
                    }

                    // SLA breach penalty
                    if (t.SlaBreached) score -= 30;

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
                });

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
                    avgScore = Math.Round(scores.Average(), 2)
                };
            })
            .OrderByDescending(a => a.avgScore)
            .ToList();

            return Ok(rankings);
        }
    }
}