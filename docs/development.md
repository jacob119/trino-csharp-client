# Development Guide

## Prerequisites

| Tool | Version | Purpose |
|------|---------|---------|
| .NET SDK | 8.0+ (10.0 recommended) | Build and test |
| Trino | 478+ | Integration tests |
| Docker | Any | Quick Trino setup |

Verify your setup:

```bash
dotnet --version   # 8.0.x or higher
```

---

## Build

```bash
cd trino-csharp-client/trino-csharp

# Build all projects
dotnet build

# Clean build (recommended when switching branches)
dotnet build --no-incremental
```

The solution targets `netstandard2.0` and `net48` simultaneously. Both TFMs must compile cleanly. The build also produces NuGet `.nupkg` files in `bin/Debug/` because `GeneratePackageOnBuild=True`.

---

## Run Tests

### Unit tests (no Trino required)

```bash
dotnet test
```

38 tests, ~20 seconds. Uses an in-process mock HTTP server (`TrinoTestServer`) that replays pre-recorded response fixtures from `Trino.Client.Test/scripts/`.

### Integration tests (requires Trino)

Start a local Trino cluster:

```bash
# Docker quick-start (no auth, includes tpch catalog)
docker run -d --name trino -p 8081:8080 trinodb/trino
```

Wait ~10 seconds for startup, then:

```bash
dotnet run --project Trino.Integration.Test
```

16 tests covering connection, data types, streaming, parameters, impersonation, and schema discovery.

The integration test target is hardcoded to `localhost:8081` (no TLS). Edit `Trino.Integration.Test/Program.cs` constants `Host`, `Port`, `Ssl` to point at a different cluster.

### Sample programs

```bash
cd Trino.Platform.Samples

# Interactive menu
dotnet run

# Non-interactive (runs all 10 samples, used in CI)
dotnet run -- --test
```

---

## Package Versions

Version is generated at build time from the current date:

```xml
<Version>$([System.DateTime]::Now.ToString("yyyy")).$([System.DateTime]::Now.ToString("MM")).$([System.DateTime]::Now.ToString("dd")).1</Version>
```

For a manual release with a specific version:

```bash
dotnet build -p:Version=2026.5.20.1
dotnet pack  -p:Version=2026.5.20.1
```

---

## NuGet Packages

Three packages are produced:

| Package | Project | Contents |
|---------|---------|---------|
| `Trino.Client` | `Trino.Client/` | Core SDK |
| `Trino.Data.ADO` | `Trino.Data.ADO/` | ADO.NET driver |
| `Trino.Client.Auth` | `Trino.Client.Auth/` | OAuth + Azure auth |

Pack manually:

```bash
dotnet pack Trino.Client/Trino.Client.csproj -c Release -o ./nupkgs
dotnet pack Trino.Data.ADO/Trino.Data.ADO.csproj -c Release -o ./nupkgs
dotnet pack Trino.Client.Auth/Trino.Client.Auth.csproj -c Release -o ./nupkgs
```

### Deploy to Nexus

```bash
# Linux / macOS
../../deploy-nexus.sh https://nexus.example.com/repository/nuget-hosted/ <API_KEY>

# Windows
..\..\deploy-nexus.ps1 -NexusUrl https://nexus.example.com/repository/nuget-hosted/ -ApiKey <API_KEY>
```

---

## Adding a Test Fixture

Unit tests replay HTTP response fixtures. To add a new test scenario:

1. Run a real query against Trino with `#define TEST_OUTPUT` enabled in `StatementClientV1.cs`  
   (uncomment line 2 `// #define TEST_OUTPUT`). This writes `response.json` with all responses.

2. Copy the output file to `Trino.Client.Test/scripts/<my_scenario>.txt`.

3. In `TrinoClientTests.cs` create a test method that calls `TrinoTestServer.Create("my_scenario.txt")`.

4. Re-comment `#define TEST_OUTPUT`.

---

## Adding a New Test to the Integration Suite

Edit `Trino.Integration.Test/Program.cs`:

1. Add `RunTest("N. description", TestMyFeature);` to `Main()`
2. Add `static void TestMyFeature() { ... }` using `MakeProps()` to create a connection
3. Run `dotnet run --project Trino.Integration.Test` to verify

---

## Code Conventions

### Null safety
Prefer `?.` and null-coalescing rather than null checks when chaining. Throw `ArgumentNullException` or `TrinoException` with descriptive messages at public API boundaries.

### Threading
- Cross-thread fields: use `Volatile.Read` / `Volatile.Write` (ARM64-safe; avoids full lock)
- Boolean flags shared with background tasks: `volatile bool`
- Atomic counters / state transitions: `Interlocked`
- Async: always `ConfigureAwait(false)` on library code; pass `CancellationToken` through every async method that does I/O

### Error handling
Never swallow exceptions silently. Background tasks (`PageQueue.ReadAhead`) catch exceptions, store them, and rethrow on the caller thread via `ThrowIfErrors()`. `Dispose()` methods may log warnings but must not throw.

### Comments
Only comment the non-obvious: threading invariants, protocol quirks, workarounds for specific bugs. Do not describe what the code does if the identifiers already make that clear.

---

## Project-Specific Gotchas

### `.NET Standard 2.0` limitations
- No `IAsyncEnumerable<T>` → uses `IAsyncEnumeratorPlaceholder<T>` (internal interface)
- No `SocketsHttpHandler` → separate shared `HttpClient` instances per compression mode
- `SafeResult()` extension in `TaskUtilities.cs` replaces `.GetAwaiter().GetResult()` with deadlock-safe unwrapping

### `net48` vs `netstandard2.0`
The projects multi-target both. Avoid API calls that exist only in one TFM. If you must, use `#if NET48` / `#if NETSTANDARD2_0` conditional compilation.

### `InternalsVisibleTo`
`Trino.Client.csproj` grants `Trino.Data.ADO` access to internal members via:

```xml
<AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleTo">
  <_Parameter1>Trino.Data.ADO</_Parameter1>
</AssemblyAttribute>
```

Do not remove this. `TaskUtilities` and other internal utilities are shared this way.

### Per-command timeout
`TrinoCommand` owns a `TimeSpan? _commandTimeout` field. `GetSessionForExecution()` creates a shallow-copied session with the overridden timeout when it is set — it does **not** mutate the shared `TrinoConnection.ConnectionSession`. This is intentional: `CommandTimeout` is per-command, not per-connection.

### `GetString()` behavior
`TrinoDataReader.GetString(i)` returns `records.Current[i]?.ToString()` — the raw JSON-deserialized value converted to string. This preserves Trino's original string format (e.g. `"2024-01-01 00:00:00.000 UTC"` for `timestamp with time zone`) instead of using the .NET type's `ToString()` which would apply locale formatting.

### `nextUri` mutation (fixed)
`StatementClientV1.Advance()` builds the target URL in a local `requestUri` variable and never mutates `this.Statement.nextUri`. This prevents subtle bugs when the `targetResultSize` query parameter is appended.
