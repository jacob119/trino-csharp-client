using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Trino.Client.Auth
{
    /// <summary>
    /// Interface defining a Trino user
    /// </summary>
    public interface ITrinoAuth
    {
        /// <summary>
        /// Triggers manual authorization.
        /// </summary>
        void AuthorizeAndValidate();

        /// <summary>
        /// Customize adding credential to request
        /// </summary>
        /// <param name="httpRequestMessage">Http request definition</param>
        void AddCredentialToRequest(HttpRequestMessage httpRequestMessage);
    }

    /// <summary>
    /// Optional async extension of ITrinoAuth.
    /// Implement this interface to avoid blocking the thread pool when refreshing tokens.
    /// Call sites check (auth is ITrinoAuthAsync) and prefer async paths.
    /// </summary>
    public interface ITrinoAuthAsync : ITrinoAuth
    {
        /// <summary>
        /// Async version of AuthorizeAndValidate. Preferred over the synchronous overload.
        /// </summary>
        Task AuthorizeAndValidateAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Async version of AddCredentialToRequest. Preferred over the synchronous overload.
        /// </summary>
        Task AddCredentialToRequestAsync(HttpRequestMessage httpRequestMessage, CancellationToken cancellationToken);
    }
}
