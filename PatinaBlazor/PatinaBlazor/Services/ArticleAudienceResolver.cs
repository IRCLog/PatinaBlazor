using Microsoft.AspNetCore.Components.Authorization;
using PatinaBlazor.Data;

namespace PatinaBlazor.Services
{
    // Shared by every public-facing Articles page so audience gating logic lives in
    // one place instead of being re-derived per page.
    public static class ArticleAudienceResolver
    {
        public static async Task<List<ArticleAudience>> GetAllowedAudiencesAsync(AuthenticationStateProvider authenticationStateProvider)
        {
            var allowed = new List<ArticleAudience> { ArticleAudience.Public };

            var authState = await authenticationStateProvider.GetAuthenticationStateAsync();
            var user = authState.User;

            if (user.Identity?.IsAuthenticated == true)
            {
                allowed.Add(ArticleAudience.RegisteredUsers);

                if (user.IsInRole(StorageService.StorageCustomerRoleName))
                {
                    allowed.Add(ArticleAudience.StorageCustomers);
                }
            }

            return allowed;
        }
    }
}
