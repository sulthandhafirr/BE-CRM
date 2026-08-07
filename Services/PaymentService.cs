using System.Text;
using System.Text.Json;
using CRM.Api.Data;
using CRM.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CRM.Api.Services
{
    public class PaymentService
    {
        private readonly AppDbContext _db;
        private readonly HttpClient _httpClient;
        private readonly string _serverKey;

        public PaymentService(AppDbContext db, IConfiguration config, HttpClient httpClient)
        {
            _db = db;
            _httpClient = httpClient;
            _serverKey = config["Midtrans:ServerKey"]!;
        }

        public async Task<(string Token, string RedirectUrl)> CreatePaymentAsync(long ticketId, decimal amount)
        {
            var existingPending = await _db.Payments
                .FirstOrDefaultAsync(p => p.TicketId == ticketId && p.Status == "pending");
            if (existingPending != null)
                throw new InvalidOperationException("A pending payment already exists for this ticket.");

            var orderId = $"STELLA-{ticketId}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";

            var requestBody = new
            {
                transaction_details = new
                {
                    order_id = orderId,
                    gross_amount = (int)amount // Midtrans expects integer IDR, no decimals
                }
            };
            
            if (amount <= 0)
            {
                throw new InvalidOperationException("Amount must be greater than zero.");
            }

            var authHeader = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_serverKey}:"));
            var request = new HttpRequestMessage(HttpMethod.Post, "https://app.sandbox.midtrans.com/snap/v1/transactions")
            {
                Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
            };
            request.Headers.Add("Authorization", $"Basic {authHeader}");

            var response = await _httpClient.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new Exception($"Midtrans error: {responseBody}");

            using var doc = JsonDocument.Parse(responseBody);
            var token = doc.RootElement.GetProperty("token").GetString()!;
            var redirectUrl = doc.RootElement.GetProperty("redirect_url").GetString()!;

            _db.Payments.Add(new Payment
            {
                TicketId = ticketId,
                Amount = amount,
                Status = "pending",
                MidtransOrderId = orderId,
                CreatedAt = DateTime.UtcNow,
                DueDate = DateTime.UtcNow.AddDays(3) // set due date for 3 days for now, later change based on company settings
            });
            await _db.SaveChangesAsync();

            return (token, redirectUrl);
        }
    }
}