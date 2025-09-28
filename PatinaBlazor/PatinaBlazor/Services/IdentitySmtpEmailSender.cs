using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using PatinaBlazor.Data;

namespace PatinaBlazor.Services
{
    public class IdentitySmtpEmailSender : IEmailSender<ApplicationUser>
    {
        private readonly IEmailSender _emailSender;

        public IdentitySmtpEmailSender(IEmailSender emailSender)
        {
            _emailSender = emailSender;
        }

        public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink) =>
            _emailSender.SendEmailAsync(email, "Confirm your email",
                $@"<h2>Welcome to Silzell.net!</h2>
                   <p>Please confirm your account by <a href='{confirmationLink}'>clicking here</a>.</p>
                   <p>If you did not create this account, please ignore this email.</p>");

        public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink) =>
            _emailSender.SendEmailAsync(email, "Reset your password",
                $@"<h2>Password Reset Request</h2>
                   <p>Please reset your password by <a href='{resetLink}'>clicking here</a>.</p>
                   <p>If you did not request a password reset, please ignore this email.</p>");

        public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode) =>
            _emailSender.SendEmailAsync(email, "Reset your password",
                $@"<h2>Password Reset Code</h2>
                   <p>Please reset your password using the following code:</p>
                   <p><strong>{resetCode}</strong></p>
                   <p>If you did not request a password reset, please ignore this email.</p>");
    }
}