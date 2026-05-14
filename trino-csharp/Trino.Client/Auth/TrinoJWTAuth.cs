using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Trino.Client.Auth
{
    /// <summary>
    /// A credential containing a bearer token
    /// </summary>
    public class TrinoJWTAuth : ITrinoAuthAsync
    {
        public const string AccessTokenProperty = "AccessToken";

        /// <summary>
        /// Create an JWT credential.
        /// </summary>
        public TrinoJWTAuth()
        {
        }

        /// <summary>
        /// Bearer token (JWT)
        /// </summary>
        public string AccessToken
        {
            get;
            set;
        }

        public void AuthorizeAndValidate()
        {
            if (string.IsNullOrEmpty(AccessToken))
            {
                throw new System.ArgumentException("TrinoJWTAuth: AccessToken property is null or empty");
            }
        }

        /// <summary>
        /// Modify the Trino request with authentication
        /// </summary>
        /// <param name="httpRequestMessage">Http request message</param>
        public virtual void AddCredentialToRequest(HttpRequestMessage httpRequestMessage)
        {
            if (!string.IsNullOrEmpty(AccessToken))
            {
                httpRequestMessage.Headers.Add("Authorization", "Bearer " + AccessToken);
            }
        }

        public Task AuthorizeAndValidateAsync(CancellationToken cancellationToken)
        {
            AuthorizeAndValidate();
            return Task.CompletedTask;
        }

        public Task AddCredentialToRequestAsync(HttpRequestMessage httpRequestMessage, CancellationToken cancellationToken)
        {
            AddCredentialToRequest(httpRequestMessage);
            return Task.CompletedTask;
        }
    }
}
