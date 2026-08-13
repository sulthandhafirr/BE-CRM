using System.Text;
using System.Text.Json;
using CRM.Api.Data;
using CRM.Api.Models;
using Microsoft.EntityFrameworkCore;
using CRM.Api.Controllers;

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

        public async Task<(string Token, string RedirectUrl)> CreatePaymentAsync(long ticketId, decimal amount, List<BillItemRequest>? items)
        {
            var existingPending = await _db.Payments
                .FirstOrDefaultAsync(p => p.TicketId == ticketId && p.Status == "pending");
            if (existingPending != null)
            {
                var isStillFresh = (DateTime.UtcNow - existingPending.CreatedAt).TotalHours < 24;
                if (isStillFresh && !string.IsNullOrEmpty(existingPending.SnapToken))
                    return (existingPending.SnapToken, string.Empty);

                existingPending.Status = "failed";
            }

            var orderId = $"STELLA-{ticketId}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";

            var itemDetails = items != null && items.Count > 0
                ? items.Select(i => new { id = i.Name ?? "item", price = (int)i.Amount, quantity = 1, name = i.Name ?? "Item" }).ToArray()
                : new[] { new { id = "ticket-" + ticketId, price = (int)amount, quantity = 1, name = "Service Charge" } };

            var grossAmount = itemDetails.Sum(i => i.price * i.quantity);

            var requestBody = new
            {
                transaction_details = new
                {
                    order_id = orderId,
                    gross_amount = grossAmount
                },
                item_details = itemDetails
            };

            if (amount <= 0)
            {
                throw new InvalidOperationException("Amount must be greater than zero.");
            }

            var authHeader = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_serverKey}:"));
            var request = new HttpRequestMessage(HttpMethod.Post, "https://app.sandbox.midtrans.com/snap/v1/transactions") // still sandbox
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

            var slaConfig = await _db.Tickets
                .AsNoTracking()
                .Where(t => t.Id == ticketId)
                .Select(t => t.Customer!.Company!.SlaConfig)
                .FirstOrDefaultAsync();
            var paymentDueDays = CompanyService.GetPaymentDueDays(slaConfig);

            _db.Payments.Add(new Payment
            {
                TicketId = ticketId,
                Amount = amount,
                Status = "pending",
                MidtransOrderId = orderId,
                SnapToken = token,
                CreatedAt = DateTime.UtcNow,
                DueDate = DateTime.UtcNow.AddDays(paymentDueDays)
            });
            await _db.SaveChangesAsync();

            return (token, redirectUrl);
        }

        public async Task<(string Token, string RedirectUrl)> CreateSubscriptionPaymentAsync(int companyId, string plan, decimal amount)
        {
            if (amount <= 0)
                throw new InvalidOperationException("Subscription amount must be greater than zero.");

            var orderId = $"CRM-REG-{companyId}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
            var items = new object[]
            {
                new
                {
                    id = $"subscription-{plan}",
                    price = (int)amount,
                    quantity = 1,
                    name = $"CRM {plan} subscription",
                }
            };

            return await CreateSubscriptionSnapPaymentAsync(companyId, plan, amount, orderId, items);
        }

        public async Task<(string Token, string RedirectUrl)> CreateRenewalPaymentAsync(int companyId, string plan, decimal amount)
        {
            if (amount <= 0)
                throw new InvalidOperationException("Subscription amount must be greater than zero.");

            var orderId = $"CRM-REN-{companyId}-{plan}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
            var items = new object[]
            {
                new
                {
                    id = $"subscription-{plan}",
                    price = (int)amount,
                    quantity = 1,
                    name = $"CRM {plan} subscription renewal",
                }
            };

            return await CreateSubscriptionSnapPaymentAsync(companyId, plan, amount, orderId, items);
        }

        private async Task<(string Token, string RedirectUrl)> CreateSubscriptionSnapPaymentAsync(
            int companyId,
            string plan,
            decimal amount,
            string orderId,
            object[] items)
        {
            var payment = new SubscriptionPayment
            {
                CompanyId = companyId,
                SubscriptionPlan = plan,
                Amount = amount,
                Status = "pending",
                MidtransOrderId = orderId,
                CreatedAt = DateTime.UtcNow,
            };
            _db.SubscriptionPayments.Add(payment);
            await _db.SaveChangesAsync();

            try
            {
                return await CreateSnapPaymentAsync(orderId, (int)amount, items);
            }
            catch
            {
                payment.Status = "failed";
                await _db.SaveChangesAsync();
                throw;
            }
        }

        private async Task<(string Token, string RedirectUrl)> CreateSnapPaymentAsync(
            string orderId,
            int grossAmount,
            object[] itemDetails)
        {
            var requestBody = new
            {
                transaction_details = new
                {
                    order_id = orderId,
                    gross_amount = grossAmount,
                },
                item_details = itemDetails,
            };

            var authHeader = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_serverKey}:"));
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://app.sandbox.midtrans.com/snap/v1/transactions")
            {
                Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
            };
            request.Headers.Add("Authorization", $"Basic {authHeader}");

            using var response = await _httpClient.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new Exception($"Midtrans error: {responseBody}");

            using var doc = JsonDocument.Parse(responseBody);
            return (
                doc.RootElement.GetProperty("token").GetString()!,
                doc.RootElement.GetProperty("redirect_url").GetString()!);
        }
    }
}
