using Microsoft.AspNetCore.Identity;
using PatinaBlazor.Data;
using PatinaBlazor.Services;

namespace PatinaBlazor.Endpoints;

public static class StorageAutoSignInEndpoints
{
    // A plain minimal-API endpoint, not a Razor page, specifically because it needs a real
    // HTTP request/response to actually write the auth cookie - see
    // StorageAutoSignInTokenService's own comment for the full "why this exists" reasoning.
    // Always redirects to /storage/signup regardless of outcome - on any failure (missing/
    // expired/already-used token, unknown user), the wizard's own OnInitializedAsync falls
    // back to the normal anonymous Step 1 flow, so there's no dead end either way.
    public static void MapStorageAutoSignInEndpoints(this WebApplication app)
    {
        app.MapGet("/api/storage/auto-signin", async (
            string? token,
            StorageAutoSignInTokenService tokenService,
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            ILogger<Program> logger) =>
        {
            var userId = string.IsNullOrEmpty(token) ? null : tokenService.ConsumeToken(token);
            if (userId is null)
            {
                logger.LogSecurityWarning("Storage auto-signin attempted with a missing, expired, or already-used token.");
                return Results.Redirect("/storage/signup");
            }

            var user = await userManager.FindByIdAsync(userId);
            if (user is null)
            {
                logger.LogSecurityWarning("Storage auto-signin token resolved to unknown user id {UserId}.", userId);
                return Results.Redirect("/storage/signup");
            }

            await signInManager.SignInAsync(user, isPersistent: false);
            logger.LogSecurityInformation("Storage customer {Email} ({UserId}) auto-signed-in after email confirmation.", user.Email, user.Id);
            return Results.Redirect("/storage/signup");
        });
    }
}
