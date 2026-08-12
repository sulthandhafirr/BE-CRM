using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using CRM.Api.Data;
using CRM.Api.Services;
using System.Text.Json;

namespace CRM.Api.Controllers
{
    [ApiController]
    [Route("api/payments")]
    public class PaymentsController : BaseController
    {
        private readonly AppDbContext _db;
        private readonly PaymentService _paymentService;
        private readonly IConfiguration _config;
        private readonly EmailService _emailService;
        private readonly NotificationService _notificationService;

        public PaymentsController(RoleService roleService, AppDbContext db, PaymentService paymentService, IConfiguration config, EmailService emailService, NotificationService notificationService)
             : base(roleService)
        {
            _db = db;
            _paymentService = paymentService;
            _config = config;
            _emailService = emailService;
            _notificationService = notificationService;
        }

        // POST api/payments/ticket/{ticketId}
        [Authorize]
        [HttpPost("ticket/{ticketId}")]
        public async Task<IActionResult> CreatePayment(long ticketId)
        {
            var role = await GetCurrentUserRole();
            if (role != "customer") return Forbid();

            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

            var ticket = await _db.Tickets
                .FirstOrDefaultAsync(t => t.Id == ticketId && t.CustomerId == userId);
            if (ticket == null) return NotFound("Ticket not found");

            if (!ticket.IsBillable || !ticket.BillAmount.HasValue)
                return BadRequest(new { message = "This ticket is not billable." });

            var alreadyPaid = await _db.Payments
                .AnyAsync(p => p.TicketId == ticketId && p.Status == "paid");
            if (alreadyPaid)
                return Conflict(new { message = "This ticket has already been paid." });

            List<BillItemRequest>? items = null;
            if (!string.IsNullOrEmpty(ticket.BillItems))
                items = JsonSerializer.Deserialize<List<BillItemRequest>>(ticket.BillItems, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            try
            {
                var (token, redirectUrl) = await _paymentService.CreatePaymentAsync(ticketId, ticket.BillAmount.Value, items);
                return Ok(new { token, redirectUrl });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
        }

        // POST api/payments/webhook/midtrans
        [HttpPost("webhook/midtrans")]
        public async Task<IActionResult> MidtransWebhook([FromBody] JsonElement payload)
        {
            var orderId = payload.GetProperty("order_id").GetString();
            var statusCode = payload.GetProperty("status_code").GetString();
            var grossAmount = payload.GetProperty("gross_amount").GetString();
            var signatureKey = payload.GetProperty("signature_key").GetString();
            var transactionStatus = payload.GetProperty("transaction_status").GetString();

            if (orderId == null || statusCode == null || grossAmount == null || signatureKey == null)
                return BadRequest();

            var serverKey = _config["Midtrans:ServerKey"];
            var raw = $"{orderId}{statusCode}{grossAmount}{serverKey}";
            var computedSignature = Convert.ToHexString(
                System.Security.Cryptography.SHA512.HashData(System.Text.Encoding.UTF8.GetBytes(raw))
            ).ToLower();

            if (computedSignature != signatureKey)
                return Unauthorized();

            var payment = await _db.Payments
                .Include(p => p.Ticket)
                    .ThenInclude(t => t!.Customer)
                .Include(p => p.Ticket)
                    .ThenInclude(t => t!.Agent)
                .FirstOrDefaultAsync(p => p.MidtransOrderId == orderId);
            if (payment == null)
            {
                return Ok();
            }
            ;

            // If the payment is already marked as "paid" or "failed", we don't need to update it again
            if (payment.Status == "paid" || payment.Status == "failed")
                return Ok();

            if (transactionStatus == "settlement" || transactionStatus == "capture")
            {
                payment.Status = "paid";
                payment.PaidAt = DateTime.UtcNow;
            }
            else if (transactionStatus == "expire" || transactionStatus == "deny" || transactionStatus == "cancel")
            {
                payment.Status = "failed";
            }
            // "pending" status > leave as pending, no change needed

            payment.PaymentMethod = payload.TryGetProperty("payment_type", out var pt) ? pt.GetString() : payment.PaymentMethod;
            payment.MidtransTransactionId = payload.TryGetProperty("transaction_id", out var tid) ? tid.GetString() : payment.MidtransTransactionId;

            // Email and web notification
            if (transactionStatus == "settlement" || transactionStatus == "capture")
            {
                payment.Status = "paid";
                payment.PaidAt = DateTime.UtcNow;

                if (payment.Ticket?.Customer?.Email != null)
                {
                    List<BillItemRequest>? emailItems = null;
                    if (!string.IsNullOrEmpty(payment.Ticket.BillItems))
                        emailItems = JsonSerializer.Deserialize<List<BillItemRequest>>(payment.Ticket.BillItems, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                    _ = _emailService.SendPaymentSuccessAsync(
                        payment.Ticket.Customer.Email,
                        payment.Ticket.Customer.Name ?? "Customer",
                        payment.Ticket.Subject ?? "Your Ticket",
                        payment.Ticket.Id,
                        payment.Amount,
                        emailItems);
                }

                if (payment.Ticket?.AgentId.HasValue == true)
                    await _notificationService.CreateAsync(
                        payment.Ticket.AgentId.Value,
                        $"Payment of Rp {payment.Amount:N0} received for ticket #{payment.Ticket.Id} '{payment.Ticket.Subject}'.");
            }
            else if (transactionStatus == "expire" || transactionStatus == "deny" || transactionStatus == "cancel")
            {
                payment.Status = "failed";

                if (payment.Ticket?.Customer?.Email != null)
                    _ = _emailService.SendPaymentFailedAsync(
                        payment.Ticket.Customer.Email,
                        payment.Ticket.Customer.Name ?? "Customer",
                        payment.Ticket.Subject ?? "Your Ticket",
                        payment.Ticket.Id);
            }

            await _db.SaveChangesAsync();

            return Ok();
        }

        // GET api/payments
        [Authorize]
        [HttpGet]
        public async Task<IActionResult> GetPayments()
        {
            var (role, companyId) = await GetCurrentUserRoleAndCompany();
            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

            var query = _db.Payments
                .Include(p => p.Ticket)
                    .ThenInclude(t => t!.Customer)
                .Include(p => p.Ticket)
                    .ThenInclude(t => t!.Agent)
                .Include(p => p.Ticket)
                    .ThenInclude(t => t!.Technician)
                .AsNoTracking()
                .Where(p => p.Ticket!.Customer!.CompanyId == companyId);

            switch (role)
            {
                case "customer":
                    query = query.Where(p => p.Ticket!.CustomerId == userId && p.Status != "failed");
                    break;
                case "technician":
                    query = query.Where(p => p.Ticket!.TechnicianId == userId);
                    break;
                case "cs_agent":
                    query = query.Where(p => p.Ticket!.AgentId == userId);
                    break;
                case "admin":
                    // admin can see all payments
                    break;
                default:
                    return Forbid();
            }

            var payments = await query
                .OrderByDescending(p => p.CreatedAt)
                .Select(p => new
                {
                    id = p.Id,
                    midtransOrderId = p.MidtransOrderId,
                    ticketId = p.TicketId,
                    ticketSubject = p.Ticket!.Subject,
                    amount = p.Amount,
                    status = p.Status,
                    customerName = p.Ticket.Customer != null ? p.Ticket.Customer.Name : null,
                    agentName = p.Ticket.Agent != null ? p.Ticket.Agent.Name : null,
                    technicianName = p.Ticket.Technician != null ? p.Ticket.Technician.Name : null,
                    paymentMethod = p.PaymentMethod,
                    createdAt = p.CreatedAt,
                    dueDate = p.DueDate,
                    paidAt = p.PaidAt,
                })
                .ToListAsync();

            return Ok(payments);
        }
    }
}
