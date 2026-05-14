using Microsoft.Extensions.Configuration;
using Trino.Client.Auth;
using Trino.Data.ADO.Server;

namespace Trino.Platform.Samples.Examples;

/// <summary>
/// 4. 인증 방식별 예제 - JWT, Basic, OAuth, Azure, 커스텀
/// </summary>
public static class AuthenticationExample
{
    public static void Run(IConfiguration config)
    {
        Console.WriteLine("[4] 인증 방식별 예제");
        Console.WriteLine();

        // ─────────────────────────────────────────────
        // 방식 1: 인증 없음 (로컬 개발용)
        // ─────────────────────────────────────────────
        Console.WriteLine("--- 1) 인증 없음 (로컬 개발용) ---");
        Console.WriteLine();
        Console.WriteLine("var properties = new TrinoConnectionProperties");
        Console.WriteLine("{");
        Console.WriteLine("    Server = new Uri(\"http://localhost:8080/\"),");
        Console.WriteLine("    Catalog = \"tpch\"");
        Console.WriteLine("    // Auth 설정 생략 시 인증 없이 접속");
        Console.WriteLine("};");

        var noAuthProps = new TrinoConnectionProperties
        {
            Server = new Uri("http://localhost:8080/"),
            Catalog = "tpch"
        };
        TryConnect(noAuthProps, config);

        // ─────────────────────────────────────────────
        // 방식 2: JWT Bearer Token
        // ─────────────────────────────────────────────
        Console.WriteLine();
        Console.WriteLine("--- 2) JWT Bearer Token ---");
        Console.WriteLine();
        Console.WriteLine("var auth = new TrinoJWTAuth { AccessToken = \"your-jwt-token\" };");

        var jwtAuth = new TrinoJWTAuth { AccessToken = "sample-token" };
        Console.WriteLine($"  생성됨: {jwtAuth.GetType().Name}");

        // 토큰 자동 갱신 패턴
        Console.WriteLine();
        Console.WriteLine("  [토큰 자동 갱신 패턴]");
        Console.WriteLine("  Task.Run(async () => {");
        Console.WriteLine("      while (true) {");
        Console.WriteLine("          await Task.Delay(TimeSpan.FromMinutes(45));");
        Console.WriteLine("          jwtAuth.AccessToken = GetNewToken();");
        Console.WriteLine("      }");
        Console.WriteLine("  });");

        // ─────────────────────────────────────────────
        // 방식 3: Basic Auth (ID/PW)
        // ─────────────────────────────────────────────
        Console.WriteLine();
        Console.WriteLine("--- 3) Basic Auth (ID/PW) ---");
        Console.WriteLine();
        Console.WriteLine("var auth = new BasicAuth { User = \"admin\", Password = \"pass\" };");

        var basicAuth = new BasicAuth { User = "admin", Password = "password" };
        Console.WriteLine($"  생성됨: {basicAuth.GetType().Name}");

        // ─────────────────────────────────────────────
        // 방식 4: OAuth 2.0 Client Credentials
        // ─────────────────────────────────────────────
        Console.WriteLine();
        Console.WriteLine("--- 4) OAuth 2.0 Client Credentials ---");
        Console.WriteLine();
        Console.WriteLine("var auth = new TrinoOauthClientSecretAuth(");
        Console.WriteLine("    tokenEndpoint: \"login.microsoftonline.com\",");
        Console.WriteLine("    clientId: \"your-client-id\",");
        Console.WriteLine("    clientSecret: \"your-secret\",");
        Console.WriteLine("    scope: \"https://your-scope/\");");
        Console.WriteLine($"  (실제 접속하려면 appsettings.json의 Auth:Type을 \"oauth\"로 설정)");

        // ─────────────────────────────────────────────
        // 방식 5: Azure Default Credentials
        // ─────────────────────────────────────────────
        Console.WriteLine();
        Console.WriteLine("--- 5) Azure Default Credentials ---");
        Console.WriteLine();
        Console.WriteLine("var auth = new TrinoAzureDefaultAuth(scope: \"https://myapp/.default\");");
        Console.WriteLine($"  (Azure 환경에서만 동작, appsettings.json의 Auth:Type을 \"azure\"로 설정)");

        // ─────────────────────────────────────────────
        // 방식 6: 커스텀 인증 구현
        // ─────────────────────────────────────────────
        Console.WriteLine();
        Console.WriteLine("--- 6) 커스텀 인증 구현 ---");
        Console.WriteLine();
        Console.WriteLine("public class MyCustomAuth : ITrinoAuth");
        Console.WriteLine("{");
        Console.WriteLine("    public void AuthorizeAndValidate()");
        Console.WriteLine("    {");
        Console.WriteLine("        // 인증 초기화 또는 갱신 로직");
        Console.WriteLine("    }");
        Console.WriteLine();
        Console.WriteLine("    public void AddCredentialToRequest(HttpRequestMessage request)");
        Console.WriteLine("    {");
        Console.WriteLine("        // 매 요청마다 인증 헤더 추가");
        Console.WriteLine("        request.Headers.Add(\"Authorization\", \"Custom \" + GetToken());");
        Console.WriteLine("    }");
        Console.WriteLine("}");

        Console.WriteLine();
        Console.WriteLine("인증 예제 출력 완료");
    }

    private static void TryConnect(TrinoConnectionProperties properties, IConfiguration config)
    {
        var section = config.GetSection("Trino");
        var host = section["Host"] ?? "localhost";
        var port = section["Port"] ?? "8080";
        Console.WriteLine($"  대상 서버: {host}:{port}");
    }
}
