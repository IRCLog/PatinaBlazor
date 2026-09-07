using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using PatinaBlazor.Components.Emails;
using PatinaBlazor.Data;

namespace PatinaBlazor.Services
{
    public class IdentitySmtpEmailSender : IEmailSender<ApplicationUser>
    {
        private readonly IEmailSender _emailSender;
        private readonly EmailTemplateRenderer _templateRenderer;

        public IdentitySmtpEmailSender(IEmailSender emailSender, EmailTemplateRenderer templateRenderer)
        {
            _emailSender = emailSender;
            _templateRenderer = templateRenderer;
        }

        public async Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink)
        {
            var html = await _templateRenderer.RenderAsync<ConfirmEmailTemplate>(new()
            {
                [nameof(ConfirmEmailTemplate.ConfirmationLink)] = confirmationLink
            });
            await _emailSender.SendEmailAsync(email, "Confirm your email", html);
        }

        public async Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink)
        {
            var html = await _templateRenderer.RenderAsync<ResetPasswordLinkTemplate>(new()
            {
                [nameof(ResetPasswordLinkTemplate.ResetLink)] = resetLink
            });
            await _emailSender.SendEmailAsync(email, "Reset your password", html);
        }

        public async Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode)
        {
            var html = await _templateRenderer.RenderAsync<ResetPasswordCodeTemplate>(new()
            {
                [nameof(ResetPasswordCodeTemplate.ResetCode)] = resetCode
            });
            await _emailSender.SendEmailAsync(email, "Reset your password", html);
        }
    }
}