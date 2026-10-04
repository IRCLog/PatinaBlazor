using Microsoft.Extensions.Caching.Memory;

namespace PatinaBlazor.Services
{
    // A short-lived, single-use handoff token that lets the storage signup wizard's own
    // browser tab become authenticated once its user's email is confirmed - normally
    // impossible directly, since an already-interactive Blazor Server circuit has no live
    // HTTP response left to write an auth cookie onto (see Login.razor's own static-SSR
    // requirement for the same reason). The wizard navigates its own tab (a real top-level
    // browser navigation, not a Blazor in-circuit one) to a real HTTP endpoint
    // (Endpoints/StorageAutoSignInEndpoints.cs) carrying this token; that endpoint IS a
    // genuine HTTP request/response, so it can call SignInManager.SignInAsync and set the
    // cookie for real.
    //
    // Deliberately scoped to this one flow, not a general-purpose "sign in via link"
    // mechanism - the token has no purpose/action encoded, just a userId, and the only
    // consumer is the one endpoint this exists for.
    //
    // In-memory only (IMemoryCache), same "correct for this app's current single-instance
    // deployment" reasoning already documented on EmailConfirmationNotifier - a token minted
    // on one server instance wouldn't be redeemable on another without a shared cache/
    // backplane, not a real concern here.
    public class StorageAutoSignInTokenService
    {
        private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(2);

        private readonly IMemoryCache _cache;

        public StorageAutoSignInTokenService(IMemoryCache cache)
        {
            _cache = cache;
        }

        public string CreateToken(string userId)
        {
            var token = Guid.NewGuid().ToString("N");
            _cache.Set(CacheKey(token), userId, TokenLifetime);
            return token;
        }

        // Single-use - removes the token whether or not it was found, so replaying the URL
        // (e.g. the browser back button re-hitting the endpoint) never signs anyone in twice.
        public string? ConsumeToken(string token)
        {
            var key = CacheKey(token);
            if (_cache.TryGetValue(key, out string? userId))
            {
                _cache.Remove(key);
                return userId;
            }

            return null;
        }

        private static string CacheKey(string token) => $"storage-auto-signin:{token}";
    }
}
