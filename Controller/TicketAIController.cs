using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CRM.Api.Data;
using CRM.Api.Models;
using CRM.Api.Prompts;
using CRM.Api.Helpers;
using CRM.Api.Services;
using System.Text;
using System.Text.Json;
namespace CRM.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/tickets/{ticketId}/ai")]
    public class TicketAIController : BaseController
    {
        private readonly AppDbContext _db;
        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;

        public TicketAIController(
            AppDbContext db,
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory,
            RoleService roleService)
            : base(roleService)
        {
            _db = db;
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
        }

        /// <summary>
        /// Generate an AI summary of the ticket conversation (Stella Summary).
        /// </summary>
        [HttpPost("summary")]
        public async Task<IActionResult> GenerateSummary(long ticketId)
        {
            var ticket = await _db.Tickets
                .Include(t => t.Comments.OrderBy(c => c.CreatedAt))
                    .ThenInclude(c => c.Sender)
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == ticketId);

            if (ticket == null)
                return NotFound(new { success = false, message = "Ticket not found" });

            var comments = ticket.Comments?.ToList() ?? new List<TicketComment>();

            // No messages — return a short status message without calling AI
            if (comments.Count == 0)
            {
                var isResolved = ticket.Status?.ToLower() is "solved" or "resolved" or "closed";
                var msg = isResolved
                    ? $"No messages. Ticket status: {ticket.Status?.ToLower()}."
                    : $"No messages yet. Ticket status: {ticket.Status?.ToLower() ?? "open"}. Our team will respond shortly.";
                return Ok(new { success = true, message = msg });
            }

            var chatLang = DetectLanguage(string.Join(" ", comments.Select(c => c.Message)));
            var historyText = string.Join("\n",
                comments.Select(c =>
                    $"[{c.CreatedAt?.ToString("dd MMMM yyyy, HH:mm")}] {c.Sender?.Name ?? "User"}: {c.Message}"));

            var prompt = chatLang == "Indonesian"
                ? $"""
                   RINGKASKAN PERCAKAPAN INI DALAM BAHASA INDONESIA. 2-3 kalimat saja.
                   Sertakan masalah utama dan update terbaru.

                   Ticket: {ticket.Subject ?? ""}
                   Status: {ticket.Status ?? ""}

                   Percakapan:
                   {historyText}

                   Ringkasan:
                   """
                : $"""
                   Summarize the following customer support conversation concisely in 2-3 sentences.
                   Include the main issue discussed and any key updates.

                   Ticket: {ticket.Subject ?? ""}
                   Status: {ticket.Status ?? ""}

                   Conversation:
                   {historyText}

                   Summary:
                   """;

            var result = await CallDeepSeek(prompt);
            return Ok(new { success = result.Success, message = result.Message });
        }

        /// <summary>
        /// Generate an AI draft reply for the agent (Stella Assist).
        /// </summary>
        [HttpPost("draft")]
        public async Task<IActionResult> GenerateDraft(long ticketId)
        {
            var ticket = await _db.Tickets
                .Include(t => t.Comments.OrderBy(c => c.CreatedAt))
                    .ThenInclude(c => c.Sender)
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == ticketId);

            if (ticket == null)
                return NotFound(new { success = false, message = "Ticket not found" });

            var comments = ticket.Comments?.ToList() ?? new List<TicketComment>();

            var chatLang = DetectLanguage(string.Join(" ", comments.Select(c => c.Message)));
            var historyText = string.Join("\n",
                comments.Select(c =>
                    $"[{c.CreatedAt?.ToString("dd MMMM yyyy, HH:mm")}] {c.Sender?.Name ?? "User"}: {c.Message}"));

            var prompt = chatLang == "Indonesian"
                ? $"""
                   BERTINDAKLAH SEBAGAI AGEN CUSTOMER SUPPORT yang profesional dan empatik untuk CRM ini.
                   Berdasarkan riwayat tiket berikut, buatlah draf balasan untuk customer.
                   Gunakan bahasa Indonesia yang sopan dan profesional. Akui keluhan mereka jika ada,
                   dan sampaikan langkah selanjutnya.

                   ID Tiket: {ticket.Id}
                   Judul Tiket: {ticket.Subject ?? ""}

                   Riwayat Percakapan:
                   {historyText}

                   Draf Balasan:
                   """
                : $"""
                   Act as an expert, empathetic, and highly professional customer support agent
                   for Enterprise AI CRM. Based on the following support ticket history, generate
                   a draft response to the customer. Keep it concise, professional, acknowledge
                   their frustration if present, and outline the next steps. Do not include
                   placeholders like [Your Name] or [Company Name] if possible, just write the
                   core message.

                   Ticket ID: {ticket.Id}
                   Ticket Subject: {ticket.Subject ?? ""}

                   Conversation History:
                   {historyText}

                   Draft Response:
                   """;

            var result = await CallDeepSeek(prompt);
            return Ok(new { success = result.Success, message = result.Message });
        }

        // ── Private helpers ──────────────────────────────────────────────────────

        private static string DetectLanguage(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "English";

            var indoWords = new[]
            {
                "saya", "anda", "kami", "kita", "tidak", "ada", "dapat",
                "akan", "sudah", "tolong", "terima", "kasih", "bisa",
                "untuk", "yang", "dengan", "pada", "dari", "ini", "itu",
                "dan", "di", "ke"
            };

            var matches = indoWords.Count(w =>
                text.Contains(w, StringComparison.OrdinalIgnoreCase));

            return matches >= 3 ? "Indonesian" : "English";
        }

        private async Task<(bool Success, string Message)> CallDeepSeek(string prompt)
        {
            var apiKey = _configuration["deepseek_api"];
            if (string.IsNullOrEmpty(apiKey))
                return (false, "AI service not configured");

            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

            var messages = new List<object>
            {
                new { role = "system", content = ChatPrompts.SystemPrompt },
                new { role = "user", content = prompt }
            };

            var payload = new
            {
                model = "deepseek-chat",
                messages,
                temperature = 0.7,
                max_tokens = 500
            };

            var jsonContent = JsonSerializer.Serialize(payload);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            var response = await client.PostAsync(
                "https://api.deepseek.com/v1/chat/completions", content);

            if (!response.IsSuccessStatusCode)
                return (false, "AI service error");

            var responseContent = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(responseContent);

            if (!document.RootElement.TryGetProperty("choices", out var choices)
                || choices.ValueKind != JsonValueKind.Array
                || choices.GetArrayLength() == 0)
                return (false, "No response from AI");

            var message = choices[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? "";

            return (true, MarkdownStripper.Strip(message));
        }
    }
}
