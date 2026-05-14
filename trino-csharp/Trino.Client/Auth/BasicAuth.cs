using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Trino.Client.Auth
{
    /// <summary>
    /// For setting up basic authentication with a username and optional password.
    /// </summary>
    public class BasicAuth : ITrinoAuthAsync
    {
        private string _cachedAuthHeader;

        public BasicAuth()
        {
        }

        public string User
        {
            get;
            set;
        }

        public string Password {
            get;
            set;
        }

        public virtual void AuthorizeAndValidate()
        {
            if (string.IsNullOrEmpty(User))
            {
                throw new ArgumentException("BasicAuth: username property is null or empty");
            }
            // Cache the header value after validation so AddCredentialToRequest is allocation-free.
            _cachedAuthHeader = BuildHeader();
        }

        /// <summary>
        /// Modify the request with authentication
        /// </summary>
        /// <param name="httpRequestMessage">Http request message</param>
        public virtual void AddCredentialToRequest(HttpRequestMessage httpRequestMessage)
        {
            httpRequestMessage.Headers.Add("Authorization", _cachedAuthHeader ?? BuildHeader());
        }

        public virtual Task AuthorizeAndValidateAsync(CancellationToken cancellationToken)
        {
            AuthorizeAndValidate();
            return Task.CompletedTask;
        }

        public virtual Task AddCredentialToRequestAsync(HttpRequestMessage httpRequestMessage, CancellationToken cancellationToken)
        {
            AddCredentialToRequest(httpRequestMessage);
            return Task.CompletedTask;
        }

        private string BuildHeader()
        {
            var bytes = string.IsNullOrEmpty(Password)
                ? Encoding.UTF8.GetBytes(User)
                : Encoding.UTF8.GetBytes($"{User}:{Password}");
            return "Basic " + Convert.ToBase64String(bytes);
        }
    }
}
