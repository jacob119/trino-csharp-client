using System.Net;
using Trino.Data.ADO.Server;

namespace Trino.Client.Test
{
    internal class TrinoTestServer : IDisposable
    {
        public int Port { get; private set; }

        /// <summary>
        /// Captures all request headers received by the test server, in order.
        /// Each element is the headers of one HTTP request.
        /// </summary>
        public List<Dictionary<string, string>> ReceivedRequestHeaders { get; } = new();

        private readonly HttpListener listener = new();
        private Task? serverTask;
        private bool cancelled = false;

        private TrinoTestServer()
        {
            // Pick a random port to listen on
            Port = new Random().Next(10000) + 10000;
        }

        public static TrinoTestServer Create(string testFile)
        {
            return Create(testFile, TimeSpan.Zero);
        }

        /// <summary>
        /// Create a server to respond with pre-recorded Trino responses.
        /// </summary>
        /// <param name="waitBetweenResponses">Allows for the simulation of a slow server.</param>
        public static TrinoTestServer Create(string testFile, TimeSpan waitBetweenResponses)
        {
            TrinoTestServer server = new();
            server.StartServer(testFile, waitBetweenResponses);
            return server;
        }

        private void StartServer(string testFile, TimeSpan waitBetweenResponses)
        {
            if (!File.Exists(testFile))
            {
                throw new FileNotFoundException(testFile);
            }

            // Parse responses synchronously so errors surface before the server starts.
            List<TestStep> testSteps = ParseTestSteps(testFile);

            // Start listening synchronously so the port is bound before Create() returns,
            // eliminating the race condition where clients connect before the listener is ready.
            Console.WriteLine("Starting test server on port " + this.Port);
            listener.Prefixes.Add($"http://localhost:{Port}/v1/");
            listener.Start();
            Console.WriteLine("Listening...");

            this.serverTask = Task.Run(() => ServeResponses(testSteps, waitBetweenResponses));
        }

        /// <summary>
        /// Represents a Trino HTTP response: headers and payload
        /// </summary>
        internal class TestStep
        {
            public Dictionary<string, List<string>> Headers;
            public string Payload { get; set; }

            internal TestStep()
            {
                Headers = [];
                Payload = string.Empty;
            }
        }

        private List<TestStep> ParseTestSteps(string testFile)
        {
            bool isHeader = true;
            TestStep current = new();
            List<TestStep> testSteps = [];

            foreach (string line in File.ReadAllLines(testFile))
            {
                if (isHeader)
                {
                    isHeader = PrepareHeaders(current, line);
                }
                else
                {
                    current.Payload = line.Replace("localhost", "localhost:" + this.Port);
                    testSteps.Add(current);
                    current = new TestStep();
                    isHeader = true;
                }
            }

            return testSteps;
        }

        private static bool PrepareHeaders(TestStep current, string line)
        {
            bool isHeader;
            IEnumerable<KeyValuePair<string, string>> headerValues = line.Split('|')
                                        .Where(l => !string.IsNullOrEmpty(l))
                                        .Select(l => new KeyValuePair<string, string>(l[..l.IndexOf('=')], l[(l.IndexOf('=') + 1)..]));
            foreach (KeyValuePair<string, string> header in headerValues)
            {
                if (!current.Headers.TryGetValue(header.Key, out List<string>? value))
                {
                    value = [];
                    current.Headers.Add(header.Key, value);
                }

                value.Add(header.Value);
            }
            isHeader = false;
            return isHeader;
        }

        // Special header used in test scripts to set the HTTP response status code.
        // Example: X-Test-Status-Code=503
        // This header is consumed by the test server and is NOT forwarded to the client.
        private const string TestStatusCodeHeader = "X-Test-Status-Code";

        /// <summary>
        /// Runs local webserver to respond with Trino HTTP responses.
        /// </summary>
        private void ServeResponses(List<TestStep> responses, TimeSpan waitBetweenResponses)
        {
            // Note: The GetContext method blocks while waiting for a request.
            foreach (TestStep response in responses)
            {
                // Listener.GetContext() blocks while waiting for a request.
                Console.WriteLine("Waiting for requests...");
                Task<HttpListenerContext> contextTask = listener.GetContextAsync();
                while (!contextTask.IsCompleted)
                {
                    if (cancelled)
                    {
                        return;
                    }
                    contextTask.Wait(1000);
                }

                HttpListenerRequest request = contextTask.Result.Request;

                // Capture request headers for test verification
                var capturedHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string key in request.Headers.AllKeys)
                {
                    capturedHeaders[key] = request.Headers[key];
                }
                ReceivedRequestHeaders.Add(capturedHeaders);

                long contentLength = request.ContentLength64;
                if (contentLength > 0)
                {
                    byte[] buffer = new byte[contentLength];
                    request.InputStream.Read(buffer, 0, (int)contentLength);
                    string requestContent = System.Text.Encoding.UTF8.GetString(buffer);
                    Console.WriteLine($"Request recieved with body: {requestContent}");
                }
                else
                {
                    Console.WriteLine("Request recieved.");
                }
                // Obtain a response object.
                using (HttpListenerResponse httpListenerResponse = contextTask.Result.Response)
                {
                    // Construct a response.
                    byte[] buffer = System.Text.Encoding.UTF8.GetBytes(response.Payload);

                    // Check for X-Test-Status-Code to override default 200 status.
                    if (response.Headers.TryGetValue(TestStatusCodeHeader, out List<string>? statusCodes)
                        && int.TryParse(statusCodes.Count > 0 ? statusCodes[0] : null, out int overrideStatus))
                    {
                        httpListenerResponse.StatusCode = overrideStatus;
                    }

                    // add headers (skip the test-only status-code meta-header)
                    foreach (KeyValuePair<string, List<string>> header in response.Headers)
                    {
                        if (header.Key == TestStatusCodeHeader) continue;
                        foreach (string value in header.Value)
                        {
                            httpListenerResponse.Headers.Add(header.Key, value);
                        }
                    }

                    Console.WriteLine("Starting response.");
                    // Get a response stream and write the response to it.
                    httpListenerResponse.ContentLength64 = buffer.Length;
                    using (Stream output = httpListenerResponse.OutputStream)
                    {
                        output.Write(buffer, 0, buffer.Length);
                    }
                    Console.WriteLine("Written response.");
                }
                // Sleep after the response is fully committed so HttpListener does not
                // serve incoming keep-alive requests with an empty body while the
                // response object is still open.
                if (waitBetweenResponses.Ticks > 0)
                {
                    Thread.Sleep(waitBetweenResponses);
                }
            }
        }

        internal TrinoConnectionProperties GetConnectionProperties()
        {
            TrinoConnectionProperties properties = new()
            {
                Catalog = "tpch",
                Host = "localhost",
                Port = this.Port,
                EnableSsl = false
            };
            return properties;
        }

        public void Dispose()
        {
            if (this.serverTask != null)
            {
                this.cancelled = true;
                this.serverTask.Wait();
            }
            listener.Close();
        }
    }
}
