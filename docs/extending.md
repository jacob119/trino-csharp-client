# Extending the Client

Recipes for the most common extension points.

---

## Adding a New Authentication Provider

### 1. Decide where it lives

- Providers that have zero extra dependencies → add to `Trino.Client/Auth/`
- Providers that require a third-party package (Azure SDK, AWS SDK, etc.) → create a new project or add to `Trino.Client.Auth/`

### 2. Implement `ITrinoAuthAsync`

`ITrinoAuthAsync` extends `ITrinoAuth`. Implement both for full compatibility:

```csharp
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Trino.Client.Auth;

namespace Trino.Client.Auth
{
    public class MyCustomAuth : ITrinoAuthAsync
    {
        private readonly string _token;

        public MyCustomAuth(string token)
        {
            _token = token ?? throw new ArgumentNullException(nameof(token));
        }

        // Sync baseline (called when the async path is not available)
        public void AuthorizeAndValidate() { /* validate config */ }

        public void AddCredentialToRequest(HttpRequestMessage request)
        {
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _token);
        }

        // Async preferred path
        public Task AuthorizeAndValidateAsync(CancellationToken _)
        {
            AuthorizeAndValidate();
            return Task.CompletedTask;
        }

        public Task AddCredentialToRequestAsync(HttpRequestMessage request, CancellationToken _)
        {
            AddCredentialToRequest(request);
            return Task.CompletedTask;
        }
    }
}
```

### 3. Wire it up

```csharp
var props = new TrinoConnectionProperties
{
    Host = "trino.example.com",
    Auth = new MyCustomAuth("my-token")
};
```

### 4. Token refresh (async providers)

For providers that fetch tokens from a remote service, follow `TrinoOauthClientSecretAuth` as the reference implementation:

- Cache the token with an expiry timestamp
- In `AddCredentialToRequestAsync`, check if expired and call `RefreshIfExpiredAsync`
- Guard concurrent refreshes with a `SemaphoreSlim(1,1)` double-check lock to avoid token thundering herd
- Pass `CancellationToken` through to the token endpoint HTTP call

```csharp
private SemaphoreSlim _refreshLock = new SemaphoreSlim(1, 1);
private string _cachedToken;
private DateTimeOffset _tokenExpiry;

private async Task RefreshIfExpiredAsync(CancellationToken ct)
{
    if (_cachedToken != null && DateTimeOffset.UtcNow < _tokenExpiry) return;
    await _refreshLock.WaitAsync(ct).ConfigureAwait(false);
    try
    {
        if (_cachedToken != null && DateTimeOffset.UtcNow < _tokenExpiry) return; // double-check
        (_cachedToken, _tokenExpiry) = await FetchTokenAsync(ct).ConfigureAwait(false);
    }
    finally
    {
        _refreshLock.Release();
    }
}
```

---

## Adding a New Trino Type

When Trino adds a new type (or you find an unmapped one), follow these steps.

### 1. Add a constant in `TrinoTypeConverters.cs`

```csharp
private const string TRINO_MY_NEW_TYPE = "mynewtype";
```

### 2. Add a case in `ConvertToTrinoTypeFromJson`

```csharp
case TRINO_MY_NEW_TYPE:
    return ParseMyNewType(value.ToString());
```

For complex types with type parameters (like `array(T)` or `map(K,V)`), the type parameter string is in `typeParameters`. Use `GetNestedTypes()` to extract individual parameter strings and call `ConvertToTrinoTypeFromJson` recursively.

### 3. Add a case in `GetClrTypeFromTrinoType`

```csharp
case TRINO_MY_NEW_TYPE:
    return typeof(MyNewTypeClass);
```

This is used by `GetSchema()` to populate `DataTable` column types.

### 4. Create a custom .NET type if needed

If there is no existing .NET type that maps cleanly, create a new class in `Trino.Client/Types/`:

```csharp
namespace Trino.Client.Types
{
    public class MyNewTypeClass
    {
        public MyNewTypeClass(/* constructor params */) { ... }
        public override string ToString() => /* canonical string representation */;
        public override bool Equals(object obj) { ... }
        public override int GetHashCode() { ... }
    }
}
```

### 5. Add a unit test fixture

Add a test case in `Trino.Client.Test/TrinoClientTests.cs` and a matching fixture in `scripts/`. The `TestAllTypes` test is the best reference — it covers all types in a single query.

### 6. Add an integration test assertion

Add the new type to `TestDateTimeTypes`, `TestNumericTypes`, or `TestStringBinaryTypes` in `Trino.Integration.Test/Program.cs` with a cast assertion:

```csharp
var val = r.GetValue(colIndex);
AssertType(val, typeof(MyNewTypeClass), "mynewtype");
```

---

## Adding a New Connection Property

Connection properties flow through three layers. All three must be updated.

### Layer 1: `TrinoConnectionProperties` (`Trino.Data.ADO`)

Add the property with XML doc comment:

```csharp
/// <summary>
/// Description of what this property does.
/// </summary>
public string MyNewProperty { get; set; }
```

### Layer 2: `TrinoConnectionProperties.GetSession()` mapping

Map the property into `ClientSessionProperties`:

```csharp
properties.MyNewProperty = this.MyNewProperty;
```

### Layer 3: `ClientSessionProperties` (`Trino.Client`)

Add the backing field:

```csharp
public string MyNewProperty { get; set; }
```

If the property should survive server-side session updates (most do), include it in the `Combine()` method:

```csharp
MyNewProperty = this.MyNewProperty,
```

### Layer 4: Wire into HTTP headers (if protocol-level)

If the property maps to a request header, add it to `StatementClientV1.AddHeadersToRequest()`:

```csharp
if (!string.IsNullOrEmpty(Session.Properties.MyNewProperty))
{
    request.Headers.Add(protocolHeaders.RequestMyNewHeader, Session.Properties.MyNewProperty);
}
```

And add the header constant to `ProtocolHeaders.cs`:

```csharp
public string RequestMyNewHeader => $"X-{serverType}-My-New-Header";
```

### Layer 5: Connection string support (optional)

If the property should be settable via a connection string, add a handler in `ConnectionStringUtils.cs`:

```csharp
{ "mynewproperty", new PropertyHandler(
    s => s.MyNewProperty,
    (s, v) => s.MyNewProperty = v.ToString()) },
```

---

## Supporting a New Protocol Header from the Server

When Trino adds a new response header that should update the session:

### 1. Add the header constant to `ProtocolHeaders.cs`

```csharp
public string ResponseSetMyNewThing => $"X-{serverType}-Set-My-New-Thing";
```

### 2. Add a field to `ClientSessionOutput.cs`

```csharp
public string SetMyNewThing { get; set; }
```

### 3. Process the header in `StatementClientV1.ProcessResponseHeaders()`

```csharp
string setMyNewThing = headers.GetValuesOrEmpty(protocolHeaders.ResponseSetMyNewThing).FirstOrDefault();
if (setMyNewThing != null)
{
    this.sessionSet.SetMyNewThing = setMyNewThing;
}
```

### 4. Apply it in `ClientSessionProperties.Combine()`

```csharp
MyNewThing = updates.SetMyNewThing ?? MyNewThing,
```

---

## Upgrading Target Frameworks

The solution currently targets `netstandard2.0` and `net48`. To add a new TFM (e.g., `net9.0`):

1. Edit each `.csproj` — change `<TargetFrameworks>` to include the new TFM
2. Run `dotnet build` and fix any API gaps
3. Run `dotnet test` and confirm all tests pass on the new TFM
4. Update `README.md` compatibility table

To **drop** `net48` in the future:
- Remove `net48` from `<TargetFrameworks>` in all three library projects
- Remove any `#if NET48` / `#if NETSTANDARD2_0` conditional blocks
- Consider migrating `TaskUtilities.SafeResult()` to `Task.Run().GetAwaiter().GetResult()` or full async

---

## Upgrading Newtonsoft.Json

`Newtonsoft.Json` is the only serialization dependency. It is pinned to `13.0.1` for `netstandard2.0` / `net48` compatibility.

If you want to migrate to `System.Text.Json`:
- The main usage is in `AbstractClient.GetResourceAsync` (response deserialization) and `StatementClientV1.GetInitialResponse`
- JSON response models in `Trino.Client/Model/` use `[JsonProperty]` attributes that would need equivalent `[JsonPropertyName]` annotations
- `System.Text.Json` is not available in `net48` — this would require dropping .NET Framework support or using a polyfill
