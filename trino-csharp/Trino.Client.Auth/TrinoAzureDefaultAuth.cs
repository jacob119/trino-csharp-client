using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;
using Trino.Client.Auth;

namespace Trino.Client.Auth
{
    public class TrinoAzureDefaultAuth : ITrinoAuthAsync, IDisposable
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
            // Token is fetched lazily on first use to avoid blocking in the constructor.
        }

        public void AuthorizeAndValidate()
        {
            // Validate config only; token fetch deferred to async path.
            if (string.IsNullOrEmpty(_scope))
                throw new InvalidOperationException("TrinoAzureDefaultAuth: scope is required.");
        }

        public async Task AuthorizeAndValidateAsync(CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(_scope))
                throw new InvalidOperationException("TrinoAzureDefaultAuth: scope is required.");
            await RefreshIfExpiredAsync(cancellationToken).ConfigureAwait(false);
        }

        public void AddCredentialToRequest(HttpRequestMessage httpRequestMessage)
        {
            if (_accessToken.ExpiresOn <= DateTimeOffset.UtcNow.AddSeconds(30))
                RefreshIfExpiredAsync(CancellationToken.None).ConfigureAwait(false).GetAwaiter().GetResult();
            httpRequestMessage.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken.Token);
        }

        public async Task AddCredentialToRequestAsync(HttpRequestMessage httpRequestMessage, CancellationToken cancellationToken)
        {
            await RefreshIfExpiredAsync(cancellationToken).ConfigureAwait(false);
            httpRequestMessage.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken.Token);
        }

        private async Task RefreshIfExpiredAsync(CancellationToken cancellationToken)
        {
            if (_accessToken.ExpiresOn > DateTimeOffset.UtcNow.AddSeconds(30))
                return;

            await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // double-check after acquiring the lock
                if (_accessToken.ExpiresOn <= DateTimeOffset.UtcNow.AddSeconds(30))
                {
                    _accessToken = await _credential.GetTokenAsync(
                        new TokenRequestContext(new[] { _scope }),
                        cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                _refreshLock.Release();
            }
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
