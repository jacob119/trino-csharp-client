using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Trino.Client.Auth;

namespace Trino.Client.Auth
{
    public class TrinoOauthClientSecretAuth : ITrinoAuthAsync, IDisposable
    {
        private static readonly HttpClient _sharedHttpClient = new HttpClient();
        public string TokenEndpoint { get; set; }
        public string ClientId { get; set; }
        public string Scope { get; set; }

        public string ClientSecret { private get; set; }
        private string _accessToken;
        private DateTime _tokenExpiry;
        private readonly SemaphoreSlim _refreshLock = new SemaphoreSlim(1, 1);
        private bool _disposed;

        public TrinoOauthClientSecretAuth()
        {
            // parameterless constructor required for connection string use
        }

        public TrinoOauthClientSecretAuth(string tokenEndpoint, string clientId, string clientSecret, string scope)
        {
            TokenEndpoint = tokenEndpoint;
            ClientId = clientId;
            ClientSecret = clientSecret;
            Scope = scope;
        }

        public void AuthorizeAndValidate()
        {
            if (string.IsNullOrEmpty(TokenEndpoint) || string.IsNullOrEmpty(ClientId) ||
                string.IsNullOrEmpty(ClientSecret) || string.IsNullOrEmpty(Scope))
            {
                throw new InvalidOperationException("OAuth2 configuration is missing required properties.");
            }
            // Validate config only; token fetch is deferred to the async path.
        }

        public async Task AuthorizeAndValidateAsync(CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(TokenEndpoint) || string.IsNullOrEmpty(ClientId) ||
                string.IsNullOrEmpty(ClientSecret) || string.IsNullOrEmpty(Scope))
            {
                throw new InvalidOperationException("OAuth2 configuration is missing required properties.");
            }
            await RefreshIfExpiredAsync(cancellationToken).ConfigureAwait(false);
        }

        public void AddCredentialToRequest(HttpRequestMessage httpRequestMessage)
        {
            // Sync callers fall through here; token must have been pre-fetched via AuthorizeAndValidate.
            if (string.IsNullOrEmpty(_accessToken))
                RefreshIfExpiredAsync(CancellationToken.None).ConfigureAwait(false).GetAwaiter().GetResult();
            httpRequestMessage.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);
        }

        public async Task AddCredentialToRequestAsync(HttpRequestMessage httpRequestMessage, CancellationToken cancellationToken)
        {
            await RefreshIfExpiredAsync(cancellationToken).ConfigureAwait(false);
            httpRequestMessage.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);
        }

        private async Task RefreshIfExpiredAsync(CancellationToken cancellationToken)
        {
            // Fast path: token is still valid (30-second buffer before actual expiry).
            if (!string.IsNullOrEmpty(_accessToken) && DateTime.UtcNow < _tokenExpiry.AddSeconds(-30))
                return;

            await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // Double-check after acquiring the lock.
                if (string.IsNullOrEmpty(_accessToken) || DateTime.UtcNow >= _tokenExpiry.AddSeconds(-30))
                {
                    var tokenResponse = await GetTokenAsync(TokenEndpoint, ClientId, ClientSecret, Scope, cancellationToken)
                        .ConfigureAwait(false);
                    _accessToken = tokenResponse.AccessToken;
                    _tokenExpiry = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn);
                }
            }
            finally
            {
                _refreshLock.Release();
            }
        }

        private async Task<TokenResponse> GetTokenAsync(string tokenEndpoint, string clientId, string clientSecret, string scope, CancellationToken cancellationToken)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint)
            {
                Content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("client_id", clientId),
                    new KeyValuePair<string, string>("client_secret", clientSecret),
                    new KeyValuePair<string, string>("grant_type", "client_credentials"),
                    new KeyValuePair<string, string>("scope", scope)
                })
            };

            var response = await _sharedHttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return Newtonsoft.Json.JsonConvert.DeserializeObject<TokenResponse>(content);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _refreshLock.Dispose();
            }
        }

        private class TokenResponse
        {
            public string AccessToken { get; set; }
            public int ExpiresIn { get; set; }
        }
    }
}
