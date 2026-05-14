using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;

namespace Trino.Client.Auth
{
    public class TrinoAzureDefaultAuth : ITrinoAuth, IDisposable
    {
        private readonly DefaultAzureCredential _credential;
        private readonly string _scope;
        private readonly SemaphoreSlim _refreshLock = new SemaphoreSlim(1, 1);
        private AccessToken _accessToken;
        private bool _disposed;

        public TrinoAzureDefaultAuth(string scope)
        {
            _credential = new DefaultAzureCredential();
            _scope = scope;
            _accessToken = GetTokenAsync().GetAwaiter().GetResult();
        }

        public void AuthorizeAndValidate()
        {
            RefreshIfExpiredAsync().GetAwaiter().GetResult();
        }

        public void AddCredentialToRequest(HttpRequestMessage httpRequestMessage)
        {
            RefreshIfExpiredAsync().GetAwaiter().GetResult();
            httpRequestMessage.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken.Token);
        }

        private async Task RefreshIfExpiredAsync()
        {
            if (_accessToken.ExpiresOn > DateTimeOffset.UtcNow.AddSeconds(30))
                return;

            await _refreshLock.WaitAsync().ConfigureAwait(false);
            try
            {
                // double-check after acquiring the lock
                if (_accessToken.ExpiresOn <= DateTimeOffset.UtcNow.AddSeconds(30))
                {
                    _accessToken = await GetTokenAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                _refreshLock.Release();
            }
        }

        private async Task<AccessToken> GetTokenAsync()
        {
            return await _credential.GetTokenAsync(
                new TokenRequestContext(new[] { _scope }),
                CancellationToken.None).ConfigureAwait(false);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _refreshLock.Dispose();
            }
        }
    }
}
