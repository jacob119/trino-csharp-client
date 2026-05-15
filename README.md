# Trino C# Client

A streaming C# Trino client library with full ADO.NET interfaces, compatible with **Trino 478+**.

## Introduction

This library provides a high-performance, protocol-complete C# client for [Trino](https://trino.io/). Key priorities:

- **Performance** — streaming read-ahead with configurable buffer; fast first-row latency for both short metadata queries and long-running analytical queries.
- **Type fidelity** — precise mapping of all Trino types (timestamps with time zone, interval, decimal, complex types) to idiomatic .NET types.
- **Full protocol coverage** — all headers from `ProtocolHeaders.java` (Trino 478+) are implemented, including transaction management, role updates, session clear/set, and impersonation.
- **Flexible authentication** — pluggable `ITrinoAuth` interface with built-in JWT, Basic/LDAP, OAuth 2.0 client credentials, and Azure Default Credential providers.
- **ADO.NET compliance** — `DbConnection`, `DbCommand`, `IDataReader`, `DataTable`, connection string, schema discovery.
- **Minimal dependencies** — only `Newtonsoft.Json` and `Microsoft.Extensions.Logging` outside .NET core; authentication in a separate assembly.

## Resources

Project introduction at Trino Summit 2024 by George Fischer

[![YouTube](https://img.youtube.com/vi/x2rF6IEjFK0/0.jpg)](https://www.youtube.com/watch?v=x2rF6IEjFK0)

[Presentation deck](docs/assets/trino-summit-2024-csharp-client.pdf)

---

## Project Structure

```
trino-csharp/
├── Trino.Client/                   # Core SDK (netstandard2.0 / net48)
├── Trino.Data.ADO/                 # ADO.NET wrapper (netstandard2.0 / net48)
├── Trino.Client.Auth/              # Authentication providers (netstandard2.0 / net48)
├── Trino.Client.Test/              # Unit tests (38 tests, mock HTTP server)
├── Trino.Integration.Test/         # Integration tests against a real Trino cluster
└── Trino.Platform.Samples/         # Usage examples and sample programs
```

### Libraries

| Library | Description | Target | Notes |
|---|---|---|---|
| `Trino.Client` | Core Trino SDK | netstandard2.0 / net48 | Protocol, session management, query execution, result streaming |
| `Trino.Data.ADO` | ADO.NET wrapper | netstandard2.0 / net48 | `DbConnection`, `DbCommand`, `IDataReader`, schema discovery |
| `Trino.Client.Auth` | Authentication providers | netstandard2.0 / net48 | Separate package to avoid dependency conflicts |
| `Trino.Integration.Test` | Integration test runner | .NET 10 | 16 live tests against Trino cluster |
| `Trino.Platform.Samples` | Sample programs | .NET 10 | 10 example patterns with `appsettings.json` config |
| `Trino.Platform.Samples.Tests` | Sample integration tests | .NET 10 | xUnit tests for HTTP client samples |

> **Note:** Libraries target both netstandard2.0 (compatible with .NET Framework 4.7.2+) and net48 (native .NET Framework 4.8 support) in a single NuGet package.
> `IAsyncEnumerable` is not available in .NET Standard 2.0 but the async read-ahead buffer means you do not need to await every row.

---

## Building

### Command Line

```bash
# Install .NET SDK from https://dotnet.microsoft.com/en-us/download
dotnet build trino-csharp/TrinoDriver.sln
```

### NuGet Packages

```bash
dotnet pack trino-csharp/Trino.Client/Trino.Client.csproj -c Release
dotnet pack trino-csharp/Trino.Data.ADO/Trino.Data.ADO.csproj -c Release
dotnet pack trino-csharp/Trino.Client.Auth/Trino.Client.Auth.csproj -c Release
```

Each package contains both `netstandard2.0` and `net48` builds automatically.

### Visual Studio

Open `trino-csharp/TrinoDriver.sln` and build.

---

## Quick Start

### ADO.NET (Recommended)

```csharp
var properties = new TrinoConnectionProperties
{
    Catalog = "tpch",
    Server  = new Uri("https://trino.example.com/"),
    Auth    = new TrinoJWTAuth(token: "your-token")
};

using var connection = new TrinoConnection(properties);
using var command    = new TrinoCommand(connection, "SELECT * FROM tpch.tiny.customer LIMIT 5");
using var reader     = command.ExecuteReader();

while (reader.Read())
{
    for (int i = 0; i < reader.FieldCount; i++)
        Console.WriteLine($"{reader.GetName(i)}: {reader.GetValue(i)}");
}
```

### Trino SDK (Direct)

```csharp
var session = new ClientSession(new ClientSessionProperties
{
    Server = new Uri("http://localhost:8080/")
});

var records = await RecordExecutor.Execute(
    session:   session,
    statement: "SELECT * FROM tpch.tiny.customer"
).ConfigureAwait(false);

foreach (var row in records)
    Console.WriteLine(string.Join(", ", row));
```

### HTTP Client (Lightweight)

```csharp
var client = new TrinoHttpClient(new TrinoHttpClientOptions
{
    Host = "localhost",
    Port = 8081
});

await foreach (var row in client.ExecuteQueryStreamingAsync("SELECT 1"))
    Console.WriteLine(row[0]);
```

---

## ADO.NET Connection

### Connection via Properties

```csharp
var properties = new TrinoConnectionProperties
{
    Host      = "trino.example.com",
    Port      = 443,
    EnableSsl = true,
    Catalog   = "tpch",
    Schema    = "tiny",
    User      = "myuser",
    Auth      = new TrinoJWTAuth(token: "...")
};

using var connection = new TrinoConnection(properties);
```

### Connection via Connection String

```csharp
using var connection = new TrinoConnection();
connection.ConnectionString =
    "host=trino.example.com;port=443;catalog=tpch;schema=tiny;" +
    "user=myuser;auth=TrinoJWTAuth;accessToken=my-token;enableSsl=true";
```

### All Connection Properties

| Property | Description | Default | Connection String Key |
|---|---|---|---|
| `Host` | Trino cluster hostname | — | `host` |
| `Port` | Trino cluster port | `443` | `port` |
| `EnableSsl` | Use HTTPS | `true` | `enableSsl` |
| `HostPath` | URL path prefix | — | `path` |
| `Catalog` | Default catalog | — | `catalog` |
| `Schema` | Default schema | — | `schema` |
| `User` | Connecting user | — | `user` |
| `AuthorizationUser` | User to impersonate (`X-Trino-Authorization-User`) | — | — |
| `OriginalUser` | Original end-user for proxy/gateway scenarios | — | — |
| `OriginalRoles` | Original user roles for proxy scenarios | — | — |
| `Auth` | Auth provider (`ITrinoAuth`) | `null` | `auth=ClassName;...` |
| `ServerType` | Protocol header prefix (`Trino` or `Presto`) | `Trino` | `serverType` |
| `AdditionalHeaders` | Extra HTTP headers | — | — |
| `Roles` | Per-catalog role selection | — | — |
| `SessionProperties` | Initial session properties | — | `properties=k:v,...` |
| `ClientTags` | Query classification tags | — | `clienttags=a,b` |
| `ClientInfo` | Client application description | — | — |
| `Source` | Client source identifier | `.NET Trino Client` | `source` |
| `TraceToken` | Distributed trace token | — | `tracetoken` |
| `TimeZone` | Query processing timezone (Java ZoneId format) | — | `timezone` |
| `CompressionDisabled` | Disable GZip compression | `false` | `compressiondisabled` |
| `Timeout` | Query execution timeout | — | — |
| `TestConnection` | Test connection on `Open()` | `false` | `testconnection` |
| `AllowHostNameCNMismatch` | Ignore TLS hostname mismatch | `false` | `AllowHostNameCNMismatch` |
| `AllowSelfSignedServerCert` | Accept self-signed TLS certificates | `false` | `AllowSelfSignedServerCert` |
| `TrustedCertPath` | Path to trusted PEM certificate file | — | — |
| `TrustedCertificate` | Embedded PEM certificate string | — | — |
| `UseSystemTrustStore` | Use OS certificate store | `false` | — |

---

## Authentication

All auth providers implement `ITrinoAuth`:

```csharp
public interface ITrinoAuth
{
    void AuthorizeAndValidate();                        // called on connection open
    void AddCredentialToRequest(HttpRequestMessage r);  // called per request
}
```

### JWT Bearer Token

```csharp
Auth = new TrinoJWTAuth(token: "your-jwt-token")
```

With periodic refresh:

```csharp
var auth = new TrinoJWTAuth(GetInitialToken());
_ = Task.Run(async () =>
{
    while (true)
    {
        await Task.Delay(TimeSpan.FromMinutes(45));
        auth.AccessToken = GetNewToken();
    }
});
```

### Basic / LDAP

```csharp
// Basic (username + optional password)
Auth = new BasicAuth { User = "myuser", Password = "secret" }

// LDAP (requires both user and password)
Auth = new LDAPAuth { User = "myuser", Password = "secret" }
```

### OAuth 2.0 Client Credentials

```csharp
Auth = new TrinoOauthClientSecretAuth(
    tokenEndpoint: "https://login.example.com/oauth2/token",
    clientId:      "my-client-id",
    clientSecret:  "my-client-secret",
    scope:         "https://trino.example.com/.default"
)
```

### Azure Default Credentials

```csharp
Auth = new TrinoAzureDefaultAuth(scope: "https://myapp.example.com/.default")
```

Supports Managed Identity, Azure CLI, environment variables, and other Azure identity sources automatically.

### Custom Authentication

```csharp
public class CustomAuth : ITrinoAuth
{
    public void AuthorizeAndValidate() { /* refresh token */ }

    public void AddCredentialToRequest(HttpRequestMessage request)
    {
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", GetToken());
    }
}

properties.Auth = new CustomAuth();
```

---

## User Impersonation

Trino supports running queries as a different user via `X-Trino-Authorization-User`. The authenticated user must have impersonation privileges configured on the server.

```csharp
var properties = new TrinoConnectionProperties
{
    Host              = "trino.example.com",
    User              = "service_account",       // authenticated identity
    AuthorizationUser = "end_user",              // impersonated identity
    Auth              = new BasicAuth { User = "service_account", Password = "..." }
};

using var connection = new TrinoConnection(properties);
using var command    = new TrinoCommand(connection, "SELECT current_user");
using var reader     = command.ExecuteReader();
reader.Read();
Console.WriteLine(reader.GetString(0)); // → "end_user"
```

The session's `AuthorizationUser` is automatically updated when the server responds with `X-Trino-Set-Authorization-User`, and cleared when it responds with `X-Trino-Reset-Authorization-User`.

---

## Session Management

Session state is maintained on the `TrinoConnection` and updated after each query based on response headers from Trino.

```csharp
using var connection = new TrinoConnection(properties);

// Set session property via SQL
using (var cmd = new TrinoCommand(connection, "SET SESSION query_max_run_time = '1h'"))
    cmd.ExecuteNonQuery();

// Change catalog and schema
using (var cmd = new TrinoCommand(connection, "USE tpch.sf1"))
    cmd.ExecuteNonQuery();

// Read updated session state
Console.WriteLine(connection.ConnectionSession.Properties.Catalog); // tpch
Console.WriteLine(connection.ConnectionSession.Properties.Schema);  // sf1
Console.WriteLine(string.Join(", ",
    connection.ConnectionSession.Properties.Properties.Keys));      // query_max_run_time
```

### Session Properties Reference

| Property | Type | Description |
|---|---|---|
| `User` | `string` | Session user identity |
| `AuthorizationUser` | `string` | Impersonated user (`X-Trino-Authorization-User`) |
| `OriginalUser` | `string` | Original user for proxy scenarios |
| `OriginalRoles` | `HashSet<string>` | Original user's roles for proxy scenarios |
| `Catalog` | `string` | Default catalog for unqualified table names |
| `Schema` | `string` | Default schema |
| `Path` | `string` | SQL path (comma-separated catalog.schema entries) |
| `TransactionId` | `string` | Active transaction ID (`NONE` when no transaction) |
| `Properties` | `Dictionary<string,string>` | Trino session properties |
| `Roles` | `Dictionary<string,ClientSelectedRole>` | Per-catalog role selections |
| `PreparedStatements` | `Dictionary<string,string>` | Named prepared statements |
| `ResourceEstimates` | `Dictionary<string,string>` | Planner resource hints |
| `ExtraCredentials` | `Dictionary<string,string>` | Connector extra credentials |
| `ClientTags` | `HashSet<string>` | Query tags |
| `Source` | `string` | Client identifier shown in Trino UI |
| `TraceToken` | `string` | Distributed trace token |
| `TimeZone` | `string` | Query processing timezone |
| `Locale` | `string` | Language locale |
| `ClientInfo` | `string` | Application description |
| `Timeout` | `TimeSpan?` | Client-side query timeout |

---

## Parameterized Queries

Trino uses positional `?` parameters only. Named parameters (`@param`) are not supported.

```csharp
using var cmd = new TrinoCommand(connection,
    "SELECT name FROM tpch.tiny.nation WHERE nationkey = ? AND name != ?");

cmd.Parameters.Add(new TrinoParameter { Value = 1 });
cmd.Parameters.Add(new TrinoParameter { Value = "ARGENTINA" });

using var reader = cmd.ExecuteReader();
while (reader.Read())
    Console.WriteLine(reader.GetString(0));
```

Parameters are converted to Trino-typed literals and sent as a `PREPARE` / `EXECUTE` pair on the server.

---

## Retry Logic

The client automatically retries on transient HTTP errors with exponential backoff.

| Status Code | Behavior | Max Retries |
|---|---|---|
| `502 Bad Gateway` | Retry with backoff | 5 |
| `503 Service Unavailable` | Retry with backoff | 5 |
| `504 Gateway Timeout` | Retry with backoff | 5 |

Backoff starts at 100 ms and caps at 3 seconds. After 5 retries a `TrinoException` is thrown containing the status code.

---

## Streaming

Result rows are delivered via a read-ahead pipeline. The client fetches pages from Trino into an internal buffer asynchronously; `IDataReader.Read()` consumes from that buffer with no per-row network wait.

```csharp
// Default buffer: 50 MB
using var reader = cmd.ExecuteReader();

// Custom buffer: 100 MB
using var reader = cmd.ExecuteReader(bufferSizeBytes: 100 * 1024 * 1024);

// Minimal buffer: one page look-ahead (memory-conservative)
using var reader = cmd.ExecuteReader(bufferSizeBytes: 0);
```

### Streaming Performance (single-node Docker)

| Dataset | Rows | First Row | Total | Throughput |
|---|---|---|---|---|
| `tpch.tiny.customer` | 1,500 | 1,638 ms | 2.2 s | 682 rows/s |
| `tpch.sf1.customer` | 150,000 | 197 ms | 1.05 s | 142,683 rows/s |
| `tpch.sf1.lineitem` | 6,001,215 | 182 ms | 21.05 s | 285,074 rows/s |

> First-row latency on `tiny` is higher due to JVM warmup; subsequent queries stabilize at ~180–200 ms.

---

## Cancellation and Resource Cleanup

The client ensures server-side queries are cleaned up when a reader is disposed mid-stream:

```csharp
using var cmd    = new TrinoCommand(connection, "SELECT * FROM huge_table");
using var reader = cmd.ExecuteReader();

reader.Read();     // fetch a few rows
// Disposing reader sends DELETE to nextUri, cancelling the server query immediately.
```

Queries are also cancelled if a `CancellationToken` fires during execution:

```csharp
using var cmd = new TrinoCommand(connection, "SELECT * FROM huge_table");
cmd.CancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(10));

using var reader = cmd.ExecuteReader();
while (reader.Read()) { ... }
```

---

## Protocol Compatibility (Trino 478+)

All headers from `ProtocolHeaders.java` (Trino master) are implemented.

### Request Headers Sent

| Header | Purpose |
|---|---|
| `X-Trino-User` | Authenticated user identity |
| `X-Trino-Authorization-User` | Impersonated user |
| `X-Trino-Original-User` | Original end-user (proxy scenarios) |
| `X-Trino-Original-Roles` | Original user's roles (proxy scenarios) |
| `X-Trino-Source` | Client source identifier |
| `X-Trino-Catalog` | Default catalog |
| `X-Trino-Schema` | Default schema |
| `X-Trino-Path` | SQL path |
| `X-Trino-Time-Zone` | Session timezone |
| `X-Trino-Language` | Session locale |
| `X-Trino-Trace-Token` | Distributed trace token |
| `X-Trino-Session` | Session properties (multiple) |
| `X-Trino-Role` | Per-catalog roles (multiple) |
| `X-Trino-Prepared-Statement` | Prepared statements (multiple) |
| `X-Trino-Transaction-Id` | Active transaction ID or `NONE` |
| `X-Trino-Client-Info` | Application description |
| `X-Trino-Client-Tags` | Query tags |
| `X-Trino-Client-Capabilities` | `PARAMETRIC_DATETIME` |
| `X-Trino-Resource-Estimate` | Planner hints (multiple) |
| `X-Trino-Extra-Credential` | Connector credentials (multiple) |
| `X-Trino-Query-Data-Encoding` | `json` (explicit, prevents binary fallback) |

### Response Headers Processed

| Header | Action |
|---|---|
| `X-Trino-Set-Catalog` | Updates session catalog |
| `X-Trino-Set-Schema` | Updates session schema |
| `X-Trino-Set-Path` | Updates session path |
| `X-Trino-Set-Session` | Adds/updates session properties |
| `X-Trino-Clear-Session` | Removes session properties |
| `X-Trino-Set-Role` | Updates per-catalog roles |
| `X-Trino-Set-Original-Roles` | Updates original user's roles |
| `X-Trino-Added-Prepare` | Registers prepared statements |
| `X-Trino-Deallocated-Prepare` | Removes prepared statements |
| `X-Trino-Started-Transaction-Id` | Records new transaction ID |
| `X-Trino-Clear-Transaction-Id` | Clears transaction ID |
| `X-Trino-Set-Authorization-User` | Updates impersonated user |
| `X-Trino-Reset-Authorization-User` | Clears impersonated user |

---

## Type Mapping

| Trino Type | .NET Type |
|---|---|
| `boolean` | `bool` |
| `tinyint` | `sbyte` |
| `smallint` | `short` |
| `integer` | `int` |
| `bigint` | `long` |
| `real` | `float` |
| `double` | `double` |
| `decimal(p,s)` | `TrinoBigDecimal` |
| `varchar`, `char` | `string` / `char[]` |
| `varbinary` | `byte[]` |
| `date` | `DateTime` |
| `time` | `TimeSpan` |
| `time with time zone` | `string` |
| `timestamp` | `DateTime` |
| `timestamp with time zone` | `DateTimeOffset` |
| `interval year to month` | `TrinoIntervalYearToMonth` |
| `interval day to second` | `TimeSpan` |
| `uuid` | `string` |
| `ipaddress` | `string` |
| `array(T)` | `List<object>` |
| `map(K,V)` | `Dictionary<object,object>` |
| `row(...)` | `List<object>` |
| `json` | `string` |
| `HyperLogLog`, `P4HyperLogLog` | `byte[]` |

---

## Running Tests

### Unit Tests (mock HTTP server, no Trino required)

```bash
dotnet test trino-csharp/Trino.Client.Test/Trino.Client.Test.csproj
# 38 tests, ~20 s
```

### Integration Tests (requires a running Trino cluster)

```bash
# Default: localhost:8081 (no auth)
dotnet run --project trino-csharp/Trino.Integration.Test/Trino.Integration.Test.csproj
```

#### Integration Test Coverage

| # | Test | Validates |
|---|---|---|
| 1 | Basic SELECT 1 | Connection, query execution |
| 2 | SHOW CATALOGS | Multi-row result parsing |
| 3 | SHOW SCHEMAS | Catalog enumeration |
| 4 | tpch.tiny.nation | Data row reading |
| 5 | Aggregation query | COUNT / AVG / MAX |
| 6 | Large streaming | 60,175 rows, tpch.tiny.lineitem |
| 7 | SET SESSION | Non-query execution |
| 8 | GetSchema | ADO.NET schema discovery |
| 9 | Parameter binding | Positional `?` parameter |
| 10 | Timeout | Client-side timeout, no hang |
| 11 | Impersonation | `X-Trino-Authorization-User` |
| 12 | Numeric types | bigint, integer, smallint, tinyint, double, real, decimal, boolean |
| 13 | Date/time types | date, time, timestamp, timestamp with time zone, interval year to month, interval day to second |
| 14 | String/binary types | varchar, char, varbinary, uuid, ipaddress, json |
| 15 | Complex types | array, map, row |
| 16 | NULL handling | All null-typed columns return DBNull |

### Sample Programs

```bash
cd trino-csharp/Trino.Platform.Samples

# Interactive menu
dotnet run

# Run all 10 examples non-interactively
dotnet run -- --test
```

---

## Quick-Start with Docker

```bash
# Start Trino (exposes port 8081 to match integration test defaults)
docker run -d --name trino -p 8081:8080 trinodb/trino:latest

# Wait ~10 s for startup
curl http://localhost:8081/v1/info

# Run integration tests (16 tests)
dotnet run --project trino-csharp/Trino.Integration.Test/Trino.Integration.Test.csproj
```

Point the integration test at a different cluster by editing the `Host`, `Port`, and `Ssl` constants in `Trino.Integration.Test/Program.cs`.

---

## Changelog

### 2026-05 — Code Tuning: Async Auth, HttpClient Reuse, Type Fixes, Protocol Hardening

- **Added** `ITrinoAuthAsync` — async extension of `ITrinoAuth`; eliminates `GetAwaiter().GetResult()` deadlocks in auth providers. `BasicAuth`, `TrinoJWTAuth`, `TrinoOauthClientSecretAuth`, and `TrinoAzureDefaultAuth` all implement it; call sites prefer async path via `is ITrinoAuthAsync` check.
- **Fixed** `StatementClientV1` socket exhaustion — sessions without custom TLS now reuse two shared static `HttpClient` instances (with/without GZip compression) instead of creating a new instance per query.
- **Fixed** `interval year to month` type now maps to `TrinoIntervalYearToMonth` instead of `DateTime`. Access `Year` and `Month` properties directly.
- **Fixed** `StatementClientV1.Advance()` no longer mutates `Statement.nextUri` — URL is built in a local `requestUri` variable.
- **Fixed** `TrinoCommand.CommandTimeout` is now per-command (stored in `_commandTimeout`); does not mutate the shared `TrinoConnection.ConnectionSession`. `GetSessionForExecution()` creates a shallow-copied session when a per-command timeout is set.
- **Fixed** `LoggingExtensions` removed dependency on internal `Microsoft.Extensions.Logging.Internal.FormattedLogValues` type; replaced with `string.Format`.
- **Fixed** `PageQueue._hasErrors` is now a `volatile bool` — O(1) fast path for `ThrowIfErrors()` and `ShouldStopReading()` instead of `ConcurrentBag.Any()` LINQ scan.
- **Fixed** `QueryState` state properties use `Volatile.Read` for ARM64-safe cross-thread visibility.
- **Fixed** `Records.Columns` uses `Volatile.Read/Write` for safe publication from background read-ahead task.
- **Fixed** `BigDecimal.Equals()` operates on local copies before `AlignScales` to prevent mutation of `this`.
- **Fixed** `SchemaUtils.ValidateIdentifier` prevents SQL injection in catalog/schema names passed to `information_schema` queries.
- **Fixed** `SchemaUtils.GetAllInformationSchemaWithTimeout` uses `Task.WhenAll` instead of `Parallel.ForEach` for true async I/O without blocking thread pool threads.
- **Fixed** `BasicAuth` caches computed `Authorization` header after first use.
- **Fixed** `Pages.MoveNextAsync` and `Records.MoveNextAsync` accept a `CancellationToken`; token is threaded from `TrinoDataReader.ReadAsync` all the way into `PageQueue.DequeueOrNull`.
- **Fixed** `Pages.Dispose()` fire-and-forgets the server-side DELETE (Dispose must not block on network I/O).
- **Fixed** `TrinoConnection.ServerVersion` uses `Lazy<string>` for thread-safe lazy initialization.
- **Fixed** `ConnectionStringUtils.TrySetSessionProperty` uses `TryGetValue` (single dictionary lookup instead of double lookup).
- **Fixed** `TrinoTypeConverters.GetNestedTypes` normalizes `baseType` once with `ToLowerInvariant()` to eliminate repeated allocations per cell.
- **Fixed** map type parameter splitting uses `SplitTopLevelComma` depth-counter parser — correctly handles nested types like `map(row(a integer, b varchar), array(bigint))`.
- **Fixed** `TrinoDataReader.GetBytes` / `GetChars` use `Array.Copy` / `CopyTo` with proper bounds checking.
- **Removed** dead code: `PageExecutor.cs` and duplicate `Trino.Data.ADO/Utilities/TaskUtilities.cs`.
- **Added** `ClientSessionProperties.ShallowCopy()` via `MemberwiseClone` for per-command timeout overrides.
- **Added** `InternalsVisibleTo("Trino.Data.ADO")` in `Trino.Client.csproj` for shared utility access.
- **Added** developer documentation in `docs/` — architecture, development guide, extension recipes, troubleshooting.

### 2026-05 — Concurrency Safety, Performance, and Multi-targeting

- **Fixed** `Pages.MoveNextAsync` semaphore acquire/release bug (SemaphoreFullException corruption).
- **Fixed** `PageQueue.Dispose` race condition (now waits for ReadAhead background task to avoid ObjectDisposedException).
- **Fixed** cross-thread field safety with `Volatile.Read/Write` on `Columns`, `LastStatement`, `HasResults` for ARM64 compatibility.
- **Fixed** `TrinoConnection.GetSchema()` `CancellationTokenSource` resource leak (added proper disposal).
- **Fixed** `OperationCanceledException` now propagates directly instead of being wrapped in `TrinoAggregateException`.
- **Fixed** `Records.isClosed` uses `Volatile.Read/Write` for thread-safe visibility.
- **Fixed** `H6` — `CastWithNullCheck` replaced exception-driven casting with `is` pattern matching (major performance improvement for large result sets).
- **Fixed** `TrinoOauthClientSecretAuth` token refresh is now thread-safe (SemaphoreSlim double-check lock pattern).
- **Fixed** `signalFoundResult` semaphore now properly released exactly once.
- **Changed** `TrinoCommand.CancellationToken` property renamed to `TrinoCommand.CancellationTokenSource` (type: `System.Threading.CancellationTokenSource`).
- **Changed** `RecordExecutor` now implements `IDisposable`.
- **Changed** `RecordExecutor.GetEnumerator()` now enforces one-shot enumeration (throws `InvalidOperationException` if called more than once).
- **Added** Multi-targeting: all three library projects (Trino.Client, Trino.Data.ADO, Trino.Client.Auth) now target **both** `netstandard2.0` AND `net48` in a single NuGet package.
- **Changed** NuGet packaging: use `dotnet pack` instead of legacy `.nuspec` files.
- **Updated** `System.Web.HttpUtility` replaced with `System.Net.WebUtility` in StatementClientV1.cs (no behavior change).

### 2026-05 — Trino 478 Protocol Compatibility

- **Fixed** inverted `Transaction-Id` header condition; always sends `NONE` when no active transaction.
- **Fixed** `ClientSelectedRole.ToString()` was emitting the C# class name instead of `ALL` / `NONE` / `ROLE{name}`.
- **Fixed** disposal chain (`TrinoDataReader → Records → PageQueue → StatementClientV1`) to cancel server-side queries on `Close()`.
- **Fixed** cancellation during HTTP POST: now submits with `CancellationToken.None` then immediately cancels server query if user token fired.
- **Added** processing for `X-Trino-Clear-Session`, `X-Trino-Set-Role`, `X-Trino-Started-Transaction-Id`, `X-Trino-Clear-Transaction-Id` response headers.
- **Added** `X-Trino-Query-Data-Encoding: json` request header to prevent binary encoding fallback.
- **Added** `X-Trino-Original-User` / `X-Trino-Original-Roles` headers for proxy/gateway scenarios.
- **Added** `X-Trino-Set-Original-Roles` response header processing.
- **Added** user impersonation via `AuthorizationUser` connection property.
- **Added** automatic retry with exponential backoff for 502 / 503 / 504.
- **Added** `Trino.Integration.Test` project (11 live integration tests).
- **Added** `Trino.Platform.Samples` project (10 usage examples).

---

## License

Apache License 2.0 — same as the upstream [trinodb/trino-csharp-client](https://github.com/trinodb/trino-csharp-client).
