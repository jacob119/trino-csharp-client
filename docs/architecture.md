# Architecture

## Project Structure

```
trino-csharp-client/
├── trino-csharp/
│   ├── Trino.Client/              # Core SDK (netstandard2.0 + net48)
│   ├── Trino.Data.ADO/            # ADO.NET driver (netstandard2.0 + net48)
│   ├── Trino.Client.Auth/         # OAuth / Azure auth providers (netstandard2.0 + net48)
│   ├── Trino.Client.Test/         # Unit tests (38 tests, mock HTTP server)
│   ├── Trino.Integration.Test/    # Integration tests against real Trino
│   ├── Trino.Platform.Samples/    # Usage examples
│   ├── Trino.Platform.Samples.Tests/
│   ├── Trino.Client.Samples/
│   └── TrinoDriver.sln
├── docs/
├── README.md
└── deploy-nexus.sh / deploy-nexus.ps1
```

### Assembly dependency graph

```
Trino.Client.Auth ──┐
                    ├──► Trino.Client ──► (Newtonsoft.Json, MS.Ext.Logging)
Trino.Data.ADO    ──┘
```

- `Trino.Client` — zero dependencies beyond `Newtonsoft.Json` and `Microsoft.Extensions.Logging.Abstractions`
- `Trino.Data.ADO` — references `Trino.Client`; has `InternalsVisibleTo` access for shared utilities
- `Trino.Client.Auth` — references `Trino.Client` + `Azure.Identity`; kept separate to avoid pulling Azure SDK into the core package

---

## Execution Pipeline

A single query follows this path from top to bottom:

```
TrinoCommand.ExecuteReader()
  │
  ▼
RecordExecutor.Execute()              ← factory; runs auth + POST /v1/statement
  │  StatementClientV1.GetInitialResponse()
  │  PageQueue.StartReadAhead()       ← spawns background Task
  │
  ▼
TrinoDataReader                       ← ADO.NET surface returned to caller
  │  reader.Read()
  ▼
Records.MoveNextAsync()               ← row enumerator
  ▼
Pages.MoveNextAsync()                 ← page enumerator (single-reader semaphore)
  ▼
PageQueue.DequeueOrNull()             ← thread-safe buffering queue
  ▲
  │  (background read-ahead task)
StatementClientV1.Advance()           ← GET nextUri → deserialize page → enqueue
  │
  ▼
Trino REST API  /v1/statement/{id}/{token}
```

### Threading model

| Thread | Role |
|--------|------|
| **Caller thread** | Calls `reader.Read()` which blocks on `SemaphoreSlim` until a page is ready |
| **Read-ahead task** | Background `Task` continuously fetches next pages from Trino and pushes them into `PageQueue` |
| **Cancellation** | `CancellationTokenSource` in `TrinoCommand` propagates through the entire chain |

The read-ahead task runs ahead of the caller by up to `bufferSize` bytes (default 50 MB). When the buffer fills it pauses; when the caller consumes pages it resumes. This means network I/O and application processing overlap.

### HttpClient lifecycle

`StatementClientV1` manages its own `HttpClient`:

- **No custom TLS** (default): reuses one of two shared static instances (`sharedHttpClientCompressed` / `sharedHttpClientNoCompression`). These live for the process lifetime. No socket exhaustion.
- **Custom TLS** (`TrustedCertPath`, `AllowSelfSignedServerCert`, etc.): creates a per-query `HttpClient` with a custom `HttpClientHandler`, owned by `StatementClientV1` and disposed when the query ends.

---

## Key Classes

### `StatementClientV1`

The protocol handler. Responsible for:

- POST `/v1/statement` with the SQL body and all session headers
- GET `nextUri` to fetch subsequent pages (`Advance()`)
- DELETE `nextUri` to cancel a running query (`Cancel()`)
- Processing all response headers (`X-Trino-Set-*`, `X-Trino-Clear-*`, etc.) and updating `ClientSessionOutput`
- TLS configuration (custom certs, self-signed, CN mismatch)
- Exponential backoff retry on 502/503/504 (up to 5 attempts, max 3 s delay)
- Adaptive backoff delay between `Advance()` calls when no data is available yet (50 ms → 5 s)

### `PageQueue`

The async buffer between the read-ahead task and the caller. Key internals:

```
responseQueue      ConcurrentQueue<ResponseQueueStatement>
currentQueueBytes  long (Interlocked)
_hasErrors         volatile bool  — O(1) fast-path for ThrowIfErrors()
signalUpdatedQueue SemaphoreSlim  — read-ahead signals new pages; caller waits here
```

`DequeueOrNull()` is the caller-side entry point. It waits on `signalUpdatedQueue` with a timeout, then tries to dequeue. If the queue is empty after the timeout it retries once before returning null (allows `MoveNextAsync` to check for completion).

`ReadAhead()` is the background loop. It calls `Advance()` until the query finishes or an error occurs, then signals completion.

### `QueryState`

A simple state machine using `Interlocked.CompareExchange`:

```
RUNNING → FINISHED        (query completed normally)
RUNNING → CLIENT_ABORTED  (Cancel() called)
RUNNING → CLIENT_ERROR    (server returned error)
```

State properties use `Volatile.Read` for ARM64-safe cross-thread visibility without taking a lock.

### `Records` / `Pages`

Both implement `IEnumerator<T>` (sync) and `IAsyncEnumeratorPlaceholder<T>` (async). The async variant accepts a `CancellationToken` and threads it all the way into `PageQueue.DequeueOrNull()`.

`Pages` enforces single-reader access via a `SemaphoreSlim(1,1)`.

### `ClientSession` / `ClientSessionProperties`

`ClientSessionProperties` is the immutable snapshot of all session settings. It supports:

- `ShallowCopy()` — creates an independent copy for per-command timeout overrides without mutating the shared connection session
- `Combine(ClientSessionOutput)` — merges server-side session updates (SET CATALOG, SET SESSION, etc.) into a new instance

`ClientSession` wraps properties + auth and is passed through the entire pipeline.

---

## Authentication Architecture

```
ITrinoAuth                     ← sync interface (baseline)
    └── ITrinoAuthAsync        ← async extension (preferred)
            └── BasicAuth
            └── TrinoJWTAuth
            └── TrinoOauthClientSecretAuth   (Trino.Client.Auth)
            └── TrinoAzureDefaultAuth        (Trino.Client.Auth)
```

Call sites check `(auth is ITrinoAuthAsync)` and prefer the async path. The sync fallback exists for compatibility with custom `ITrinoAuth` implementations that predate the async interface.

`AuthorizeAndValidate` / `AuthorizeAndValidateAsync` is called once when `RecordExecutor.Execute()` starts (connection setup / token validation).

`AddCredentialToRequest` / `AddCredentialToRequestAsync` is called for every HTTP request (`AbstractClient.GetResourceAsync` → `AddHeadersAsync`).

---

## Protocol Headers

All protocol headers are centralised in `ProtocolHeaders.cs`. The class is constructed with a `serverType` string (`"Trino"` or `"Presto"`) and returns the correct header name with the appropriate prefix.

Headers sent per-request (selected):

| Header | Source |
|--------|--------|
| `X-Trino-User` | `session.Properties.User` |
| `X-Trino-Authorization-User` | `session.Properties.AuthorizationUser` |
| `X-Trino-Catalog` / `X-Trino-Schema` | session context |
| `X-Trino-Session` | `key=urlencoded(value)` — one header per property |
| `X-Trino-Transaction-Id` | `NONE` or active transaction id |
| `X-Trino-Client-Capabilities` | `PARAMETRIC_DATETIME` |
| `X-Trino-Query-Data-Encoding` | `json` (prevents binary fallback) |

Headers processed from each response and folded into `ClientSessionOutput`:

- `X-Trino-Set-Catalog`, `X-Trino-Set-Schema`, `X-Trino-Set-Path`
- `X-Trino-Set-Session` (multiple), `X-Trino-Clear-Session`
- `X-Trino-Set-Role`, `X-Trino-Set-Original-Roles`
- `X-Trino-Added-Prepare`, `X-Trino-Deallocated-Prepare`
- `X-Trino-Started-Transaction-Id`, `X-Trino-Clear-Transaction-Id`
- `X-Trino-Set-Authorization-User`, `X-Trino-Reset-Authorization-User`

---

## Type Mapping

`TrinoTypeConverters.ConvertToTrinoTypeFromJson` converts a raw JSON value (string, number, bool, or null from `Newtonsoft.Json`) into a .NET type. The conversion is driven by the Trino column `typeSignature`.

| Trino type | .NET type | Notes |
|------------|-----------|-------|
| `boolean` | `bool` | |
| `tinyint` | `sbyte` | |
| `smallint` | `short` | |
| `integer` | `int` | |
| `bigint` | `long` | |
| `real` | `float` | |
| `double` | `double` | |
| `decimal(p,s)` | `TrinoBigDecimal` | Arbitrary precision |
| `varchar`, `char` | `string` | |
| `varbinary` | `byte[]` | Base64 decoded |
| `date` | `DateTime` | |
| `time` | `TimeSpan` | |
| `time with time zone` | `string` | No .NET equivalent |
| `timestamp` | `DateTime` | |
| `timestamp with time zone` | `DateTimeOffset` | Preserves offset |
| `interval year to month` | `TrinoIntervalYearToMonth` | Custom type |
| `interval day to second` | `TimeSpan` | |
| `uuid` | `Guid` | |
| `ipaddress` | `string` | |
| `array(T)` | `List<object>` | Elements recursively converted |
| `map(K,V)` | `Dictionary<object,object>` | Keys and values recursively converted |
| `row(...)` | `List<object>` | Fields recursively converted |
| `json` | `string` | Raw JSON string |
| `HyperLogLog`, `P4HyperLogLog` | `byte[]` | |

Nested type parsing (for `array`, `map`, `row`) is done by `GetNestedTypes()` which uses `SplitTopLevelComma()` — a depth-counter parser that correctly handles deeply nested types like `map(row(a integer, b varchar), array(bigint))`.

---

## Error Handling

```
TrinoException            ← base; wraps optional TrinoError (server-side error detail)
TrinoAggregateException   ← multiple errors from read-ahead failures
TimeoutException          ← query exceeded Timeout
OperationCanceledException ← CancellationToken cancelled
```

Error propagation path:

1. `StatementClientV1.Advance()` throws `TrinoException` on server error or HTTP failure
2. `PageQueue.ReadAhead()` catches it, adds to `errors` bag, sets `_hasErrors = true`, signals the semaphore
3. Next call to `PageQueue.ThrowIfErrors()` (called from `Pages.MoveNextAsync`) rethrows as `TrinoAggregateException` on the caller thread

The background task never surfaces exceptions directly to the caller; they are always marshalled through the queue's error collection.
