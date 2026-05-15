# Troubleshooting

## Build Failures

### `CS0535: does not implement interface member 'IAsyncEnumeratorPlaceholder<T>.MoveNextAsync()'`

`IAsyncEnumeratorPlaceholder<T>` requires a parameterless `Task<bool> MoveNextAsync()`. Classes that override this with a `CancellationToken` default-parameter overload must also add an explicit interface implementation:

```csharp
Task<bool> IAsyncEnumeratorPlaceholder<T>.MoveNextAsync() =>
    MoveNextAsync(CancellationToken.None);
```

### `CS0246: type or namespace 'ITrinoAuthAsync' not found`

`ITrinoAuthAsync` lives in `Trino.Client.Auth` namespace. Add:

```csharp
using Trino.Client.Auth;
```

### Build succeeds for `netstandard2.0` but fails for `net48`

Some APIs (like `SslPolicyErrors`, `X509Certificate2`) behave differently between TFMs. Use the exact same qualified name and ensure the necessary `using` directives are present for both targets. Avoid `#if` blocks unless absolutely necessary.

---

## Runtime Errors

### `TrinoException: Invalid server URL: <null>`

`ClientSessionProperties.Server` is null. Ensure `Host` (and optionally `Port`, `EnableSsl`) are set before opening a connection:

```csharp
var props = new TrinoConnectionProperties
{
    Host = "localhost",
    Port = 8081,
    EnableSsl = false
};
```

### `TrinoException: HTTP 401 (Unauthorized)`

Authentication is not configured or the credentials are wrong. Check:

1. Is `Auth` set on `TrinoConnectionProperties`?
2. For `BasicAuth`: is the password correct?
3. For `TrinoJWTAuth`: has the token expired? Check `JWTToken` is set and not expired.
4. For `TrinoOauthClientSecretAuth`: is the token endpoint reachable? Are `ClientId` / `ClientSecret` / `Scope` correct?

### `TrinoException: HTTP 403 (Forbidden)` on impersonation

The authenticated user (`User`) does not have the `impersonation` system privilege for the target user (`AuthorizationUser`). Grant it in Trino:

```sql
GRANT IMPERSONATE ON USER target_user TO USER service_account;
```

### `TimeoutException: Trino query ran for N s, exceeding timeout of M s`

The query exceeded `TrinoCommand.CommandTimeout` (or `TrinoConnectionProperties.Timeout`). Options:

1. Increase the timeout: `command.CommandTimeout = 300;` (seconds)
2. Set per-command timeout without affecting the connection: `new TrinoCommand(connection, sql, TimeSpan.FromMinutes(5), ...)`
3. If no timeout should apply: `command.CommandTimeout = int.MaxValue`

### `OperationCanceledException`

`TrinoCommand.CancellationTokenSource.Cancel()` was called (or `TrinoCommand.Cancel()`). The server-side query is also cancelled via DELETE on the nextUri. This is the expected behavior.

If you are seeing unexpected cancellations, check:

- Is anyone holding a reference to `CancellationTokenSource` and calling `Cancel()`?
- Is a parent `CancellationToken` being linked to the command's token?

### `InvalidOperationException: RecordExecutor represents a forward-only stream and can only be enumerated once`

`RecordExecutor.GetEnumerator()` was called more than once. `RecordExecutor` is a forward-only stream. If you need to re-read data, execute the query again:

```csharp
// WRONG: calling GetEnumerator() twice
var executor = await cmd.RunQuery();
foreach (var row in executor) { ... }
foreach (var row in executor) { ... } // throws

// CORRECT: run query twice or materialize to a list first
var rows = executor.ToList();
```

### `TrinoException: Reading a stream that is already closed`

`Records.MoveNext()` was called after `Close()` or `Dispose()`. Do not call `reader.Read()` after `reader.Close()`.

### `TrinoException: Only one reader can advance pages at a time`

`Pages.MoveNextAsync()` was called concurrently from two threads. The page enumerator is not thread-safe. Use a single reader thread per query.

### `NullReferenceException` when reading columns before first `Read()`

`TrinoDataReader` requires at least one `Read()` call before accessing column values. The `Current` row is null until `Read()` returns `true`.

---

## Connection Issues

### SSL certificate errors

**Self-signed certificate:**
```csharp
props.AllowSelfSignedServerCert = true;
```

**CN mismatch (hostname doesn't match certificate):**
```csharp
props.AllowHostNameCNMismatch = true;
```

**Custom CA certificate:**
```csharp
props.TrustedCertPath = "/path/to/ca.pem";
// or inline PEM string:
props.TrustedCertificate = "-----BEGIN CERTIFICATE-----\n...";
```

**Corporate proxy / system trust store:**
```csharp
props.UseSystemTrustStore = true;
```

### `HttpRequestException: connection refused`

Trino is not running or not reachable on the configured host/port. Verify:

```bash
curl http://localhost:8081/v1/info
```

### `HttpRequestException: SSL handshake failed`

1. Trino is configured for HTTPS but you set `EnableSsl = false`, or vice versa
2. The certificate chain is not trusted → use one of the TLS options above
3. TLS version mismatch (unlikely with modern .NET)

---

## Performance Issues

### Queries are slow for small result sets

The default 50 ms initial backoff delay (`INITIAL_PAGE_READ_DELAY_MSEC` in `StatementClientV1`) means the client waits 50 ms before checking for the first page. For very fast queries (metadata, `SELECT 1`), this delay dominates. This is a known trade-off — reducing it further causes excessive polling on slow queries.

### Memory usage is high for large result sets

The read-ahead buffer defaults to 50 MB (`Constants.DefaultBufferSizeBytes`). To reduce memory usage, pass a smaller buffer:

```csharp
// 5 MB buffer
using var reader = cmd.ExecuteReader(5 * 1024 * 1024);
```

Setting `bufferSize = 0` is not allowed and will throw.

### Socket exhaustion under high concurrency

This should not occur for standard sessions (no custom TLS) because `StatementClientV1` reuses shared static `HttpClient` instances. If you are using custom TLS settings, a new `HttpClient` is created per query. Under very high concurrency with custom TLS, consider using a `SocketsHttpHandler` with connection pooling and sharing it across queries — this would require extending the constructor to accept an external `HttpClient`.

---

## Schema Discovery Issues

### `GetSchema("tables")` returns empty results

If no `Catalog` is set on the connection, `SchemaUtils` queries all catalogs in parallel and merges results. If one catalog is broken or slow, it is skipped with a warning. Set a specific catalog to avoid this:

```csharp
props.Catalog = "hive";
DataTable tables = connection.GetSchema("tables");
```

### `TrinoException: Illegal catalog identifier`

Catalog or schema names containing characters outside `[a-zA-Z0-9_]` are rejected by `SchemaUtils.ValidateIdentifier` to prevent SQL injection. If your catalog has a hyphen or dot in its name, the schema discovery queries cannot be used — query `information_schema` directly.

---

## Type Conversion Issues

### `InvalidCastException: Cannot cast or convert object of type X to type Y`

`GetValue<T>(i)` failed to convert the column value to the requested type. Common causes:

1. **Wrong type for column**: check the Trino column type against the [type mapping table](architecture.md#type-mapping)
2. **Decimal → double**: Trino `decimal` maps to `TrinoBigDecimal`, not `double`. Use `reader.GetValue<TrinoBigDecimal>(i).ToDecimal()` or cast explicitly
3. **DateTimeOffset → string**: `GetString(i)` returns the raw Trino string, not the converted `DateTimeOffset`. Use `GetString(i)` directly, not `GetValue<string>(i)`

### `interval year to month` unexpected type

The type maps to `TrinoIntervalYearToMonth` (not `DateTime`). Access year and month:

```csharp
var iym = (TrinoIntervalYearToMonth)reader.GetValue(colIndex);
Console.WriteLine($"{iym.Year} years, {iym.Month} months");
```

### Nested `map` type with complex key or value types fails

`SplitTopLevelComma` splits `map(K, V)` type parameters at the top-level comma. If the type string is malformed or not a two-argument map, it throws `TrinoException: Map type parameters must have exactly two type arguments`. Verify the raw type signature from `reader.GetSchemaTable()`.

---

## Test Failures

### Unit tests fail with `Address already in use`

`TrinoTestServer` binds to a random free port. If the port is occupied between the `TcpListener.Start()` call and first request, the test fails. Re-run the test — this is a transient race condition.

### Integration tests fail with `connection refused`

Trino is not running. Start it:

```bash
docker run -d --name trino -p 8081:8080 trinodb/trino
# Wait ~10 seconds
curl http://localhost:8081/v1/info
dotnet run --project Trino.Integration.Test
```

### Integration test `TestImpersonation` fails

Trino's default configuration does not allow impersonation. Configure `access-control.properties`:

```properties
access-control.name=allow-all
```

Or grant impersonation explicitly in your access control configuration.
