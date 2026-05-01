using CarRental.Model;
using System.Net;
using System.Net.Mail;

namespace CarRental.Server
{
    public class EmailService : IEmailService
    {
        private readonly IConfiguration _config;

        public EmailService(IConfiguration config)
        {
            _config = config;
        }

        public async Task SendEmailAsync(string toEmail, string subject, string body)
        {
            var host = _config["EmailSettings:Host"];
            var port = int.Parse(_config["EmailSettings:Port"]);
            var email = _config["EmailSettings:Email"];
            var password = _config["EmailSettings:Password"];

            using var client = new SmtpClient(host, port)
            {
                Credentials = new NetworkCredential(email, password),
                EnableSsl = true
            };

            string formattedHtmlBody = GetEmailTemplate(subject, body);

            var mailMessage = new MailMessage
            {
                From = new MailAddress(email, "JKLM Car Rental"),
                Subject = subject,
                Body = formattedHtmlBody,
                IsBodyHtml = true
            };

            mailMessage.To.Add(toEmail);

            await client.SendMailAsync(mailMessage);
        }

        private string GetEmailTemplate(string subject, string body)
        {
            int currentYear = DateTime.Now.Year;

            return $@"
            <!DOCTYPE html>
            <html>
            <head>
                <meta charset='UTF-8'>
                <meta name='viewport' content='width=device-width, initial-scale=1.0'>
            </head>
            <body style='font-family: ""Helvetica Neue"", Helvetica, Arial, sans-serif; background-color: #f4f7f6; margin: 0; padding: 40px 20px;'>
                
                <div style='max-width: 600px; margin: 0 auto; background-color: #ffffff; border-radius: 8px; overflow: hidden; box-shadow: 0 4px 15px rgba(0,0,0,0.05);'>
                    
                    <div style='background-color: #0056b3; padding: 30px 20px; text-align: center;'>
                        <h1 style='color: #ffffff; margin: 0; font-size: 24px; letter-spacing: 1px;'>JKLM Car Rental</h1>
                    </div>

                    <div style='padding: 40px 30px; color: #333333;'>
                        <h2 style='margin-top: 0; font-size: 20px; color: #1a1a1a; border-bottom: 2px solid #f0f0f0; padding-bottom: 10px;'>
                            {subject}
                        </h2>
                        
                        <div style='font-size: 16px; line-height: 1.6; margin-top: 20px;'>
                            {body}
                        </div>
                    </div>

                    <div style='background-color: #f9fafb; padding: 20px; text-align: center; border-top: 1px solid #eeeeee;'>
                        <p style='margin: 0; font-size: 13px; color: #888888;'>
                            &copy; {currentYear} JKLM Car Rental. All rights reserved.
                        </p>
                        <p style='margin: 5px 0 0 0; font-size: 12px; color: #aaaaaa;'>
                            Cebu City, Philippines
                        </p>
                    </div>

                </div>

            </body>
            </html>";
        }
    }
}