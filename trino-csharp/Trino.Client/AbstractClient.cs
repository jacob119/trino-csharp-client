using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net;
using System.Threading;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Trino.Client.Auth;
using Trino.Client.Logging;
using System.Net.Http.Headers;

namespace Trino.Client
{
    public abstract class AbstractClient<T>
    {
        private const string TrinoClientName = ".NET Trino Client";
        private const int MaxRetryCount = 5;
        private static readonly TimeSpan RetryBaseDelay = TimeSpan.FromMilliseconds(100);
        private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(3);
        private static readonly HashSet<HttpStatusCode> defaultExpectedResponseCodes = new HashSet<HttpStatusCode> { HttpStatusCode.OK };

        // Shared static HttpClient for sessions that do not require a custom HttpClientHandler
        // (e.g. InfoClientV1). Avoids socket exhaustion from per-instance HttpClient creation.
        private static readonly HttpClient sharedHttpClient = new HttpClient
        {
            Timeout = Constants.HttpConnectionTimeout
        };

        protected abstract string ResourcePath { get; }
        protected internal HttpClient httpClient;
        protected internal ClientSession Session { get; set; }
        protected internal ILoggerWrapper logger;
        protected internal CancellationToken cancellationToken;
        internal ProtocolHeaders protocolHeaders;

        // HTTP status codes that allow for a retry
        protected internal HashSet<HttpStatusCode> RetryableResponses = new HashSet<HttpStatusCode>() { HttpStatusCode.BadGateway, HttpStatusCode.ServiceUnavailable, HttpStatusCode.GatewayTimeout };

        protected AbstractClient(ClientSession session, ILoggerWrapper logger, CancellationToken cancellationToken)
        {
            this.httpClient = sharedHttpClient;
            Session = session;
            this.logger = logger;
            this.cancellationToken = cancellationToken;
            this.protocolHeaders = new ProtocolHeaders(session.Properties.ServerType);
        }

        /// <summary>
        /// The URI of the Trino resource.
        /// </summary>
        protected virtual internal Uri ResourceUri => new Uri($"{Session.Properties.Server}{this.ResourcePath}");

        /// <summary>
        /// Performs HTTP request to Trino to fetch the requested resource and deserializes it to the specified type.
        /// </summary>
        public T Get()
        {
            return this.GetAsync().SafeResult();
        }

        /// <summary>
        /// Performs HTTP request to Trino to fetch the requested resource and deserializes it to the specified type.
        /// </summary>
        protected internal async Task<T> GetAsync()
        {
            return await this.GetAsync(this.ResourceUri).ConfigureAwait(false);
        }

        /// <summary>
        /// Performs HTTP request to Trino to fetch the requested resource and deserializes it to the specified type.
        /// </summary>
        protected internal async Task<T> GetAsync(Uri uri)
        {
            string resourceContent = await GetAsync(uri, defaultExpectedResponseCodes).ConfigureAwait(false);
            T deserializedResult = JsonConvert.DeserializeObject<T>(resourceContent);
            return deserializedResult;
        }

        /// <summary>
        /// Perform actual HTTP request to Trino to fetch the requested resource.
        /// </summary>
        protected async Task<string> GetAsync(Uri uri, HashSet<HttpStatusCode> expectedResponses)
        {
            return await GetResourceAsync(
                httpClient,
                this.RetryableResponses,
                this.Session,
                () => new HttpRequestMessage(HttpMethod.Get, uri),
                expectedResponses,
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Performs an HTTP request to Trino, retrying on transient failures up to MaxRetryCount times
        /// with exponential backoff. A new HttpRequestMessage is created for each attempt via requestFactory.
        /// </summary>
        protected async Task<string> GetResourceAsync(
            HttpClient httpClient,
            HashSet<HttpStatusCode> retryableResponses,
            ClientSession session,
            Func<HttpRequestMessage> requestFactory,
            HashSet<HttpStatusCode> expectedResponses,
            CancellationToken token)
        {
            string responseContent = string.Empty;
            int retryCount = 0;

            // Continually retry until erroring or a valid response
            while (true)
            {
                using (HttpRequestMessage request = requestFactory())
                {
                    await AddHeadersAsync(protocolHeaders, request, session, token).ConfigureAwait(false);

                    try
                    {
                        HttpStatusCode statusCode;
                        using (HttpResponseMessage page = await httpClient.SendAsync(request, token).ConfigureAwait(false))
                        {
                            responseContent = await page.Content.ReadAsStringAsync().ConfigureAwait(false);
#if TEST_OUTPUT
                            // For UT generation, write the response to a file.
                            // First get the headers from the response and serialize them into k=v pairs
                            StringBuilder headers = new StringBuilder();
                            HashSet<string> excludedHeaders = new HashSet<string>() { "Connection", "X-Content-Type-Options", "Vary", "Strict-Transport-Security" };
                            for (int i = 0; i < page.Headers.Count(); i++)
                            {
                                string headerName = page.Headers.ElementAt(i).Key;
                                if (!excludedHeaders.Contains(headerName))
                                {
                                    string headerValue = string.Join(",", page.Headers.ElementAt(i).Value);
                                    headers.Append(headerName + "=" + headerValue + "|");
                                }
                            }
                            string responseStrWithUpdatedHost = responseContent.Replace(this.Session.Properties.Server.ToString(), "http://localhost/");
                            string fileName = "response.json";
                            File.AppendAllText(fileName, headers.ToString() + Environment.NewLine);
                            File.AppendAllText(fileName, responseStrWithUpdatedHost.Trim() + Environment.NewLine);
#endif
                            statusCode = page.StatusCode;
                            if (retryableResponses.Contains(statusCode))
                            {
                                if (retryCount >= MaxRetryCount)
                                {
                                    throw new TrinoException($"HTTP {(int)statusCode} ({statusCode}) after {MaxRetryCount} retries: {responseContent}");
                                }
                                retryCount++;
                                double delayMs = Math.Min(
                                    RetryBaseDelay.TotalMilliseconds * Math.Pow(2, retryCount - 1),
                                    MaxRetryDelay.TotalMilliseconds);
                                await Task.Delay((int)delayMs, token).ConfigureAwait(false);
                                continue;
                            }

                            if (!expectedResponses.Contains(statusCode))
                            {
                                throw new TrinoException($"HTTP {(int)statusCode} ({statusCode}): {responseContent}");
                            }

                            this.ProcessResponseHeaders(page.Headers);
                        }

                        return responseContent;
                    }
                    catch (WebException ex)
                    {
                        if (ex.Response != null)
                        {
                            using (var stream = ex.Response.GetResponseStream())
                            using (var reader = new StreamReader(stream))
                            {
                                throw new TrinoException(reader.ReadToEnd(), ex);
                            }
                        }
                        throw new TrinoException(ex.Message, ex);
                    }
                    catch (Exception ex) when (!(ex is TrinoException || ex is OperationCanceledException || ex is TimeoutException))
                    {
                        if (!string.IsNullOrEmpty(responseContent))
                        {
                            throw new TrinoException(responseContent, ex);
                        }
                        throw;
                    }
                }
            }
        }

        protected virtual void ProcessResponseHeaders(HttpResponseHeaders headers)
        {
        }

        /// <summary>
        /// Adds headers that are common to all requests.
        /// Uses the async credential path when the auth implementation supports it.
        /// </summary>
        internal static async Task AddHeadersAsync(ProtocolHeaders protocolHeaders, HttpRequestMessage request, ClientSession session, CancellationToken cancellationToken)
        {
            if (session.Auth is ITrinoAuthAsync asyncAuth)
                await asyncAuth.AddCredentialToRequestAsync(request, cancellationToken).ConfigureAwait(false);
            else
                session.Auth?.AddCredentialToRequest(request);

            if (!string.IsNullOrEmpty(session.Properties.User))
            {
                request.Headers.Add(protocolHeaders.RequestUser, session.Properties.User);
            }
            else if (session.Auth == null)
            {
                // A user is always required, if no user is provided, use the user agent
                request.Headers.Add(protocolHeaders.RequestUser, TrinoClientName);
            }

            if (!string.IsNullOrEmpty(session.Properties.AuthorizationUser))
            {
                request.Headers.Add(protocolHeaders.RequestAuthorizationUser, session.Properties.AuthorizationUser);
            }

            request.Headers.Add("User-Agent", TrinoClientName);
        }
    }
}
