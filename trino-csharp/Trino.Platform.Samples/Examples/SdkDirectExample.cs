using Trino.Client;
using Trino.Data.ADO.Server;

namespace Trino.Platform.Samples.Examples;

/// <summary>
/// 3. SDK 직접 사용 - ADO.NET 없이 RecordExecutor로 쿼리 실행
/// </summary>
public static class SdkDirectExample
{
    public static async Task RunAsync(TrinoConnectionProperties properties)
    {
        Console.WriteLine("[3] SDK 직접 사용 (RecordExecutor)");
        Console.WriteLine();

        // TrinoConnectionProperties에서 ClientSession 생성
        var session = properties.GetSession();

        // 쿼리 실행 및 스트리밍
        Console.WriteLine("쿼리 실행 중: SELECT * FROM tpch.tiny.region");
        Console.WriteLine();

        var records = await RecordExecutor.Execute(
            session: session,
            statement: "SELECT * FROM tpch.tiny.region")
            .ConfigureAwait(false);

        // Row-by-Row 스트리밍 출력
        Console.WriteLine("결과:");
        int rowCount = 0;
        foreach (var row in records)
        {
            Console.WriteLine($"  [{rowCount}] {string.Join(" | ", row)}");
            rowCount++;
        }

        Console.WriteLine();
        Console.WriteLine($"총 {rowCount}건 조회 완료");
    }
}
