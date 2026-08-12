using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using CRM.Api.Controllers;
// using Microsoft.Extensions.Configuration;

namespace CRM.Api.Services
{
    public class EmailService
    {
        private readonly IConfiguration _config;

        public EmailService(IConfiguration config)
        {
            _config = config;
        }

        /// <returns>True when the email was accepted by the SMTP server, otherwise false.</returns>
        private async Task<bool> SendAsync(
            string toEmail,
            string toName,
            string subject,
            string body,
            List<(string FileName, byte[] Content, string MimeType)>? attachments = null)
        {
            try
            {
                var host = _config["Email:Host"] ?? throw new InvalidOperationException("Email:Host is not configured");
                var port = _config["Email:Port"] ?? throw new InvalidOperationException("Email:Port is not configured");
                var username = _config["Email:Username"] ?? throw new InvalidOperationException("Email:Username is not configured");
                var password = _config["Email:Password"] ?? throw new InvalidOperationException("Email:Password is not configured");
                var sender = _config["Email:SenderName"] ?? "CRM Support";

                var email = new MimeMessage();
                email.From.Add(new MailboxAddress(sender, username));
                email.To.Add(new MailboxAddress(toName, toEmail));
                email.Subject = subject;

                var builder = new BodyBuilder { HtmlBody = body };
                if (attachments is { Count: > 0 })
                {
                    foreach (var attachment in attachments)
                    {
                        builder.Attachments.Add(
                            attachment.FileName,
                            attachment.Content,
                            ContentType.Parse(attachment.MimeType));
                    }
                }
                email.Body = builder.ToMessageBody();

                using var smtp = new SmtpClient();
                await smtp.ConnectAsync(host, int.Parse(port), SecureSocketOptions.StartTls);
                await smtp.AuthenticateAsync(username, password);
                await smtp.SendAsync(email);
                await smtp.DisconnectAsync(true);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EmailService] Failed to send email to {toEmail}: {ex.Message}");
                return false;
            }
        }



        public Task SendTicketAssignedAsync(string toEmail, string toName, string ticketSubject, long ticketId)
            => SendAsync(
                toEmail, toName,
                "Your Ticket Has Been Assigned",
                $@"<p>Hi <b>{toName}</b>,</p>
                    <p>Your ticket <b>'{ticketSubject}'</b> <i>(#{ticketId})</i> has been assigned to a CS Agent and is now being handled.</p>
                    <p>We will get back to you as soon as possible.</p>
                    <p>Thank you for your patience.</p>"
            );

        public Task SendTicketResolvedAsync(string toEmail, string toName, string ticketSubject, long ticketId)
            => SendAsync(
                toEmail, toName,
                "Your Ticket Has Been Resolved",
                $@"<p>Hi <b>{toName}</b>,</p>
                    <p>Your ticket <b>'{ticketSubject}'</b> <i>(#{ticketId})</i> has been marked as resolved.</p>
                    <p>If you have further questions, feel free to open a new ticket.</p>
                    <p>Thank you for using our service.</p>"
            );

        public Task SendNewMessageAsync(string toEmail, string toName, string ticketSubject, long ticketId, string senderName, string message)
            => SendAsync(
                toEmail, toName,
                "New Message on Your Ticket",
                $@"<p>Hi <b>{toName}</b>,</p>
                    <p>You have a new message on your ticket <b>'{ticketSubject}'</b> <i>(#{ticketId})</i>.</p>
                    <hr/>
                    <p><b>{senderName}</b> says:</p>
                    <blockquote style='border-left: 3px solid #FF8040; padding-left: 10px; color: #555;'>
                        {message}
                    </blockquote>
                    <hr/>
                    <p>Please log in to view and reply.</p>
                    <p>Thank you.</p>"
                );

        public Task SendTechnicianAssignedAsync(string toEmail, string toName, string ticketSubject, long ticketId)
            => SendAsync(
                toEmail, toName,
                "A Technician Has Been Assigned to Your Ticket",
                $@"<p>Hi <b>{toName}</b>,</p>
                    <p>A technician has been assigned to your ticket <b>'{ticketSubject}'</b> <i>(#{ticketId})</i>.</p>
                    <p>They will reach out to you shortly.</p>
                    <p>Thank you for your patience.</p>"
            );

        public Task SendAssignedToTechnicianAsync(string toEmail, string toName, string ticketSubject, long ticketId)
            => SendAsync(
                toEmail, toName,
                "You Have Been Assigned a New Ticket",
                $@"<p>Hi <b>{toName}</b>,</p>
                    <p>You have been assigned to a new ticket <b>'{ticketSubject}'</b> <i>(#{ticketId})</i>.</p>
                    <p>Please log in to view the ticket and take necessary actions.</p>
                    <p>Thank you for your support.</p>"
            );

        public Task SendTechnicianResolvedAsync(string toEmail, string toName, string ticketSubject, long ticketId)
            => SendAsync(
                toEmail, toName,
                "Ticket Resolved",
                $@"<p>Hi <b>{toName}</b>,</p>
                    <p>The ticket <b>'{ticketSubject}'</b> <i>(#{ticketId})</i> you were assigned to has been marked as resolved.</p>
                    <p>Thank you for your support.</p>"
            );
        public Task SendSlaBreachedAsync(string toEmail, string toName, string ticketSubject, long ticketId)
            => SendAsync(
                toEmail, toName,
                "SLA Deadline Breached",
                $@"<p>Hi <b>{toName}</b>,</p>
                    <p>The ticket <b>'{ticketSubject}'</b> <i>(#{ticketId})</i> has breached its SLA deadline.</p>
                    <p>Please review and take action as soon as possible.</p>
                    <p>Thank you.</p>"
            );
        public Task SendSlaWarningAsync(string toEmail, string toName, string ticketSubject, long ticketId, int minutesRemaining)
            => SendAsync(
                toEmail, toName,
                "SLA Deadline Approaching",
                $@"<p>Hi <b>{toName}</b>,</p>
                    <p>The ticket <b>'{ticketSubject}'</b> <i>(#{ticketId})</i> is approaching its SLA deadline, breaching in approximately <b>{minutesRemaining} minutes</b>.</p>
                    <p>Please review and take action soon to avoid an SLA breach.</p>
                    <p>Thank you.</p>"
            );
        public Task SendSlaBreachedCustomerAsync(string toEmail, string toName, string ticketSubject, long ticketId)
            => SendAsync(
                toEmail, toName,
                "Update on Your Ticket",
                $@"<p>Hi <b>{toName}</b>,</p>
                    <p>We wanted to let you know that your ticket <b>'{ticketSubject}'</b> <i>(#{ticketId})</i> is taking a bit longer than expected to resolve.</p>
                    <p>Our team is actively working on it and will get back to you as soon as possible. Thank you for your patience.</p>"
            );
        public Task SendPaymentSuccessAsync(string toEmail, string toName, string ticketSubject, long ticketId, decimal amount, List<BillItemRequest>? items)
        {
            var itemsHtml = "";
            if (items != null && items.Count > 0)
            {
                var rows = string.Join("", items.Select(i =>
                    $"<tr><td style='padding:6px 0;color:#374151;'>{i.Name}</td><td style='padding:6px 0;text-align:right;color:#374151;'>Rp {i.Amount:N0}</td></tr>"));

                itemsHtml = $@"
            <table style='width:100%;border-collapse:collapse;margin:12px 0;'>
                {rows}
                <tr style='border-top:1px solid #E5E7EB;font-weight:bold;'>
                    <td style='padding:8px 0;'>Total</td>
                    <td style='padding:8px 0;text-align:right;'>Rp {amount:N0}</td>
                </tr>
            </table>";
            }

            return SendAsync(
                toEmail, toName,
                "Payment Successful",
                $@"<p>Hi <b>{toName}</b>,</p>
            <p>We've received your payment for ticket <b>'{ticketSubject}'</b> <i>(#{ticketId})</i>.</p>
            {itemsHtml}
            <p>Thank you for your payment.</p>"
            );
        }

        public Task SendPaymentFailedAsync(string toEmail, string toName, string ticketSubject, long ticketId)
            => SendAsync(
                toEmail, toName,
                "Payment Unsuccessful",
                $@"<p>Hi <b>{toName}</b>,</p>
                    <p>Your payment for ticket <b>'{ticketSubject}'</b> <i>(#{ticketId})</i> was not successful.</p>
                    <p>Please try again from your ticket page.</p>"
            );

        /// <summary>
        /// Sends a scheduled data export report with Excel file(s) attached.
        /// </summary>
        /// <returns>True when the email was accepted by the SMTP server, otherwise false.</returns>
        public Task<bool> SendExportReportAsync(
            string toEmail,
            string toName,
            string companyName,
            string reportLabel,
            string period,
            List<(string FileName, byte[] Content)> attachments)
        {
            const string spreadsheetMime =
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

            var fileList = string.Join(
                "", attachments.Select(a => $"<li><b>{a.FileName}</b> ({a.Content.Length / 1024} KB)</li>"));

            return SendAsync(
                toEmail,
                toName,
                $"{char.ToUpperInvariant(reportLabel[0]) + reportLabel[1..]} Data Export Report",
                $@"<p>Hi <b>{toName}</b>,</p>
                    <p>Here is the {reportLabel} data export for <b>{companyName}</b> covering <b>{period}</b>.</p>
                    <ul>{fileList}</ul>
                    <p>Please review the attached file(s).</p>
                    <p>Thank you.</p>",
                attachments
                    .Select(a => (a.FileName, a.Content, spreadsheetMime))
                    .ToList());
        }
    }
}