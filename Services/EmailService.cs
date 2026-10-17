using System.Threading.Tasks;

namespace AgritechPortal.Services
{
    public class EmailService
    {
        public Task SendEmailAsync(string to, string subject, string body)
        {
            // Implement SMTP or SendGrid here
            return Task.CompletedTask;
        }
    }
}