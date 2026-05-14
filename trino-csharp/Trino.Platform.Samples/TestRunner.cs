using Microsoft.Extensions.Configuration;
using Trino.Data.ADO.Server;
using Trino.Platform.Samples.Examples;
using Trino.Platform.Samples.Http;

namespace Trino.Platform.Samples;

/// <summary>
/// 비대화형 테스트 실행기 - 각 예제를 순차적으로 실행하여 접속 검증
/// </summary>
public static class TestRunner
{
    public static async Task RunAll()
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        var properties = Program.BuildConnectionProperties(config);

        Console.WriteLine("==============================================");
        Console.WriteLine("  Trino C# Client - 접속 테스트");
        Console.WriteLine("==============================================");
        Console.WriteLine();

        await RunTest("1. ADO.NET 기본 쿼리", () => { BasicQueryExample.Run(properties); return Task.CompletedTask; });
        await RunTest("2. Connection String", () => { ConnectionStringExample.Run(config); return Task.CompletedTask; });
        await RunTest("3. SDK 직접 사용", () => SdkDirectExample.RunAsync(properties));
        await RunTest("5. 세션 관리", () => { SessionManageExample.Run(properties); return Task.CompletedTask; });
        await RunTest("6. 파라미터 쿼리", () => { ParameterQueryExample.Run(properties); return Task.CompletedTask; });
        await RunTest("7. DataTable 변환", () => DataTableExample.RunAsync(properties));
        await RunTest("8. 스키마 조회", () => { SchemaInfoExample.Run(properties); return Task.CompletedTask; });
        await RunTest("9. 병렬 쿼리", () => ParallelQueryExample.RunAsync(properties));

        var host = config.GetSection("Trino")["Host"] ?? "localhost";
        var port = int.Parse(config.GetSection("Trino")["Port"] ?? "8080");
        await RunTest("11. HTTP 직접 접속", () => HttpQueryExample.RunAsync(host, port));

        Console.WriteLine();
        Console.WriteLine("==============================================");
        Console.WriteLine("  전체 테스트 완료");
        Console.WriteLine("==============================================");
    }

    private static async Task RunTest(string name, Func<Task> action)
    {
        Console.WriteLine($">>> {name}");
        Console.WriteLine(new string('=', 50));
        try
        {
            await action();
            Console.WriteLine($"<<< {name}: 성공");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"<<< {name}: 실패 - {ex.GetType().Name}: {ex.Message}");
        }
        Console.WriteLine();
    }
}
