# CLAUDE.md — Trino C# Client

Project context for AI-assisted development.

## Build & Test

```bash
cd trino-csharp-client/trino-csharp

# Build (must compile for both netstandard2.0 and net48)
dotnet build --no-incremental

# Unit tests (38 tests, no Trino required, ~20 s)
dotnet test

# Integration tests (16 tests, requires Trino at localhost:8081)
dotnet run --project Trino.Integration.Test
```

**After any code change: always run `dotnet build --no-incremental` and `dotnet test` before reporting done.**

## Project Layout

```
Trino.Client/        Core SDK: protocol, session, streaming pipeline
Trino.Data.ADO/      ADO.NET driver: DbConnection/DbCommand/IDataReader
Trino.Client.Auth/   OAuth + Azure auth (separate to avoid dependency conflicts)
Trino.Client.Test/   Unit tests (mock HTTP server)
Trino.Integration.Test/  Live tests against real Trino
```

## Critical Invariants

### Threading
- Cross-thread fields → `Volatile.Read` / `Volatile.Write` (ARM64-safe)
- `volatile bool` for flags shared with background tasks (`_hasErrors`, `isClosed`)
- State transitions → `Interlocked.CompareExchange`
- Token refresh guards → `SemaphoreSlim(1,1)` double-check lock
- Always `ConfigureAwait(false)` on library async methods

### Session immutability
`ClientSessionProperties.ShallowCopy()` creates an independent copy for per-command timeout overrides. **Never mutate `TrinoConnection.ConnectionSession` from `TrinoCommand`** — use `GetSessionForExecution()` which returns the shared session unchanged or a copy with the per-command timeout.

### HttpClient reuse
`StatementClientV1` reuses static shared `HttpClient` instances when no custom TLS is configured. Only create a per-instance `HttpClient` when `UseSystemTrustStore`, `TrustedCertPath`, `TrustedCertificate`, `AllowHostNameCNMismatch`, or `AllowSelfSignedServerCert` is set. Always set `_ownsHttpClient = false` when reusing a shared instance.

### `nextUri` mutation
**Never** append query parameters directly to `this.Statement.nextUri`. Build a local `requestUri` variable and append there.

### `GetString()` semantics
`TrinoDataReader.GetString(i)` returns `records.Current[i]?.ToString()` — the raw JSON-deserialized value, not the type-converted CLR value. This preserves Trino's string format (e.g. `"2024-01-01 00:00:00.000 UTC"` for timestamp with time zone).

### Error marshalling
Background task (`PageQueue.ReadAhead`) catches exceptions, stores them in `errors` bag, sets `_hasErrors = true`, and signals the semaphore. The caller thread rethrows via `ThrowIfErrors()`. **Never let background task exceptions surface directly to the caller.**

### `ITrinoAuthAsync`
All auth providers should implement `ITrinoAuthAsync` (not just `ITrinoAuth`). Call sites check `(auth is ITrinoAuthAsync)` and prefer async path. Sync methods remain for backward compatibility.

### `.NET Standard 2.0` constraints
- No `IAsyncEnumerable<T>` → use `IAsyncEnumeratorPlaceholder<T>` (internal)
- Classes that add a `CancellationToken` overload to `MoveNextAsync` **must also add an explicit interface implementation** for the parameterless version:
  ```csharp
  Task<bool> IAsyncEnumeratorPlaceholder<T>.MoveNextAsync() =>
      MoveNextAsync(CancellationToken.None);
  ```
- No `SocketsHttpHandler` → use two static shared `HttpClient` instances

### `net48` multi-targeting
Both `netstandard2.0` and `net48` must compile. Avoid API calls exclusive to one TFM. Use `#if NET48` only when unavoidable.

## Key Files

| File | Responsibility |
|------|----------------|
| `Trino.Client/StatementClientV1.cs` | HTTP protocol, headers, TLS, retry |
| `Trino.Client/PageQueue.cs` | Async read-ahead buffer |
| `Trino.Client/Pages.cs` | Page enumerator (single-reader) |
| `Trino.Client/Records.cs` | Row enumerator |
| `Trino.Client/RecordExecutor.cs` | Pipeline factory |
| `Trino.Client/AbstractClient.cs` | Base HTTP client, `AddHeadersAsync` |
| `Trino.Client/Auth/ITrinoAuth.cs` | Auth interfaces (`ITrinoAuth`, `ITrinoAuthAsync`) |
| `Trino.Client/Utils/TrinoTypeConverters.cs` | Trino → .NET type conversion |
| `Trino.Client/ClientSessionProperties.cs` | Immutable session config, `ShallowCopy()`, `Combine()` |
| `Trino.Client/ProtocolHeaders.cs` | All protocol header names |
| `Trino.Data.ADO/Server/TrinoCommand.cs` | ADO.NET command, per-command timeout |
| `Trino.Data.ADO/Utilities/SchemaUtils.cs` | Schema discovery queries |

## Type Mapping Reference

| Trino | .NET |
|-------|------|
| `boolean` | `bool` |
| `tinyint` | `sbyte` |
| `smallint` | `short` |
| `integer` | `int` |
| `bigint` | `long` |
| `real` | `float` |
| `double` | `double` |
| `decimal(p,s)` | `TrinoBigDecimal` |
| `varchar`, `char` | `string` |
| `varbinary` | `byte[]` |
| `date` | `DateTime` |
| `time` | `TimeSpan` |
| `time with time zone` | `string` |
| `timestamp` | `DateTime` |
| `timestamp with time zone` | `DateTimeOffset` |
| `interval year to month` | `TrinoIntervalYearToMonth` |
| `interval day to second` | `TimeSpan` |
| `uuid` | `Guid` |
| `ipaddress` | `string` |
| `array(T)` | `List<object>` |
| `map(K,V)` | `Dictionary<object,object>` |
| `row(...)` | `List<object>` |
| `json` | `string` |

## Developer Docs

- `docs/architecture.md` — pipeline internals, threading model, type mapping
- `docs/development.md` — build, test, release, fixtures, conventions
- `docs/extending.md` — add new auth, types, connection properties, protocol headers
- `docs/troubleshooting.md` — common errors and fixes
