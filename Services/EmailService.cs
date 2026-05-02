using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
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

        private async Task SendAsync(string toEmail, string toName, string subject, string body)
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
                email.Body = new TextPart("html") { Text = body };

                using var smtp = new SmtpClient();
                await smtp.ConnectAsync(host, int.Parse(port), SecureSocketOptions.StartTls);
                await smtp.AuthenticateAsync(username, password);
                await smtp.SendAsync(email);
                await smtp.DisconnectAsync(true);
            }
            catch(Exception ex)
            {
                Console.WriteLine($"[EmailService] Failed to send email to {toEmail}: {ex.Message}");
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
    }
}