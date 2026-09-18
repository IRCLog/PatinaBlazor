namespace PatinaBlazor.Services.PayPal
{
    // Caches PayPal-issued tokens across calls. Registered as a singleton, separate from
    // PayPalClient itself - typed HttpClients (AddHttpClient<T,I>) are transient by design
    // (the pooled handler is what's actually reused), so any state meant to survive across
    // calls has to live somewhere with a longer lifetime than the client instance itself.
    //
    // Holds two independent cache slots, not one: the server-side OAuth2 client-credentials
    // access token (used to authorize every REST call PayPalClient makes) and the separate
    // "browser-safe" client token Card Fields needs client-side (a different token, with its
    // own TTL - see PayPalClient.GetBrowserSafeClientTokenAsync). Never share one slot for
    // both; PayPal's own docs are explicit that these are not interchangeable.
    public class PayPalTokenCache
    {
        private readonly CachedTokenSlot _accessTokenSlot = new();
        private readonly CachedTokenSlot _clientTokenSlot = new();

        public Task<string> GetOrFetchAccessTokenAsync(Func<Task<(string Token, TimeSpan ExpiresIn)>> fetch) =>
            _accessTokenSlot.GetOrFetchAsync(fetch);

        public Task<string> GetOrFetchClientTokenAsync(Func<Task<(string Token, TimeSpan ExpiresIn)>> fetch) =>
            _clientTokenSlot.GetOrFetchAsync(fetch);

        private class CachedTokenSlot
        {
            private readonly SemaphoreSlim _lock = new(1, 1);
            private string? _token;
            private DateTime _expiresAtUtc = DateTime.MinValue;

            // A safety margin so a token already in flight to be used doesn't expire mid-request.
            private static readonly TimeSpan RefreshMargin = TimeSpan.FromSeconds(60);

            public async Task<string> GetOrFetchAsync(Func<Task<(string Token, TimeSpan ExpiresIn)>> fetch)
            {
                if (_token != null && DateTime.UtcNow < _expiresAtUtc - RefreshMargin)
                {
                    return _token;
                }

                await _lock.WaitAsync();
                try
                {
                    if (_token != null && DateTime.UtcNow < _expiresAtUtc - RefreshMargin)
                    {
                        return _token;
                    }

                    var (token, expiresIn) = await fetch();
                    _token = token;
                    _expiresAtUtc = DateTime.UtcNow.Add(expiresIn);
                    return _token;
                }
                finally
                {
                    _lock.Release();
                }
            }
        }
    }
}
