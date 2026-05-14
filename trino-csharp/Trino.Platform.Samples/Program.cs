using Microsoft.Extensions.Configuration;
using Trino.Data.ADO.Server;
using Trino.Client.Auth;
using Trino.Platform.Samples.Examples;

namespace Trino.Platform.Samples;

public class Program
{
    public static async Task Main(string[] args)
    {
        // --test 인수로 비대화형 전체 테스트 실행
        if (args.Length > 0 && args[0] == "--test")
        {
            await TestRunner.RunAll();
            return;
        }

        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        var properties = BuildConnectionProperties(config);

        Console.WriteLine("==============================================");
        Console.WriteLine("  Trino C# Client - Platform Sample");
        Console.WriteLine("==============================================");
        Console.WriteLine();

        while (true)
        {
            PrintMenu();
            Console.Write("선택 (0~11): ");
            var input = Console.ReadLine()?.Trim();

            if (input == "0")
            {
                Console.WriteLine("종료합니다.");
                break;
            }

            Console.WriteLine();
            Console.WriteLine("----------------------------------------------");

            try
            {
                switch (input)
                {
                    case "1":
                        BasicQueryExample.Run(properties);
                        break;
                    case "2":
                        ConnectionStringExample.Run(config);
                        break;
                    case "3":
                        await SdkDirectExample.RunAsync(properties);
                        break;
                    case "4":
                        AuthenticationExample.Run(config);
                        break;
                    case "5":
                        SessionManageExample.Run(properties);
                        break;
                    case "6":
                        ParameterQueryExample.Run(properties);
                        break;
                    case "7":
                        await DataTableExample.RunAsync(properties);
                        break;
                    case "8":
                        SchemaInfoExample.Run(properties);
                        break;
                    case "9":
                        await ParallelQueryExample.RunAsync(properties);
                        break;
                    case "10":
                        LargeDataStreamExample.Run(properties);
                        break;
                    case "11":
                        var host = config.GetSection("Trino")["Host"] ?? "localhost";
                        var port = int.Parse(config.GetSection("Trino")["Port"] ?? "8080");
                        await HttpQueryExample.RunAsync(host, port);
                        break;
                    default:
                        Console.WriteLine("잘못된 입력입니다.");
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[오류] {ex.GetType().Name}: {ex.Message}");
            }

            Console.WriteLine("----------------------------------------------");
            Console.WriteLine();
        }
    }

    private static void PrintMenu()
    {
        Console.WriteLine("[예제 목록]");
        Console.WriteLine("  1.  ADO.NET 기본 쿼리");
        Console.WriteLine("  2.  Connection String 접속");
        Console.WriteLine("  3.  SDK 직접 사용 (RecordExecutor)");
        Console.WriteLine("  4.  인증 방식별 예제");
        Console.WriteLine("  5.  세션 관리");
        Console.WriteLine("  6.  파라미터 쿼리");
        Console.WriteLine("  7.  DataTable 변환");
        Console.WriteLine("  8.  스키마/메타데이터 조회");
        Console.WriteLine("  9.  병렬 쿼리");
        Console.WriteLine("  10. 대용량 스트리밍");
        Console.WriteLine("  11. HTTP 직접 접속 (순수 HttpClient)");
        Console.WriteLine("  0.  종료");
        Console.WriteLine();
    }

    /// <summary>
    /// appsettings.json 설정으로 TrinoConnectionProperties 생성
    /// </summary>
    public static TrinoConnectionProperties BuildConnectionProperties(IConfiguration config)
    {
        var section = config.GetSection("Trino");

        var host = section["Host"] ?? "localhost";
        var port = int.Parse(section["Port"] ?? "8080");
        var enableSsl = bool.Parse(section["EnableSsl"] ?? "false");
        var scheme = enableSsl ? "https" : "http";

        var properties = new TrinoConnectionProperties
        {
            Catalog = section["Catalog"] ?? "tpch",
            Schema = section["Schema"] ?? "tiny",
            Server = new Uri($"{scheme}://{host}:{port}/"),
            User = section["User"] ?? "trino-user"
        };

        // 인증 설정
        var authType = section["Auth:Type"] ?? "None";
        properties.Auth = CreateAuth(authType, section);

        return properties;
    }

    private static ITrinoAuth? CreateAuth(string authType, IConfigurationSection section)
    {
        return authType.ToLowerInvariant() switch
        {
            "jwt" => new TrinoJWTAuth { AccessToken = section["Auth:Token"] ?? "" },
            "basic" => new BasicAuth
            {
                User = section["User"] ?? "",
                Password = section["Auth:Password"] ?? ""
            },
            "oauth" => new TrinoOauthClientSecretAuth(
                tokenEndpoint: section["Auth:OAuthTokenEndpoint"] ?? "",
                clientId: section["Auth:OAuthClientId"] ?? "",
                clientSecret: section["Auth:OAuthClientSecret"] ?? "",
                scope: section["Auth:OAuthScope"] ?? ""),
            "azure" => new TrinoAzureDefaultAuth(
                scope: section["Auth:AzureScope"] ?? ""),
            _ => null
        };
    }
}
