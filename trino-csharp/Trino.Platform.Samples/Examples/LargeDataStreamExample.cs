using System.Diagnostics;
using Trino.Data.ADO.Server;

namespace Trino.Platform.Samples.Examples;

/// <summary>
/// 10. 대용량 스트리밍 - 큰 데이터셋을 스트리밍 방식으로 처리
/// </summary>
public static class LargeDataStreamExample
{
    public static void Run(TrinoConnectionProperties properties)
    {
        Console.WriteLine("[10] 대용량 스트리밍");
        Console.WriteLine();

        // 스케일별 테스트: tiny(1,500), sf1(150,000), sf10(1,500,000)
        RunScale(properties, "tpch.tiny.customer", "tiny (~1,500건)");
        Console.WriteLine();
        RunScale(properties, "tpch.sf1.customer", "sf1 (~150,000건)");
        Console.WriteLine();
        RunScale(properties, "tpch.sf1.lineitem", "sf1.lineitem (~6,000,000건)");
    }

    private static void RunScale(TrinoConnectionProperties properties, string table, string label)
    {
        var query = $"SELECT * FROM {table}";

        Console.WriteLine($"--- {label} ---");
        Console.WriteLine($"쿼리: {query}");

        var stopwatch = Stopwatch.StartNew();

        using var connection = new TrinoConnection(properties);
        using var command = new TrinoCommand(connection, query);

        // 버퍼 크기 지정 (100MB)
        using var reader = command.ExecuteReader(1024 * 1024 * 100);

        int rowCount = 0;
        int colCount = 0;
        long firstRowMs = 0;

        while (reader.Read())
        {
            rowCount++;

            if (rowCount == 1)
            {
                firstRowMs = stopwatch.ElapsedMilliseconds;
                colCount = reader.FieldCount;
            }

            if (rowCount % 500_000 == 0)
            {
                Console.WriteLine($"  {rowCount:N0}건 처리됨 ({stopwatch.Elapsed.TotalSeconds:F1}s)");
            }
        }

        stopwatch.Stop();
        var elapsed = stopwatch.Elapsed.TotalSeconds;
        var rowsPerSec = elapsed > 0 ? rowCount / elapsed : 0;

        Console.WriteLine($"  컬럼 수: {colCount}");
        Console.WriteLine($"  총 행 수: {rowCount:N0}");
        Console.WriteLine($"  첫 행까지: {firstRowMs}ms");
        Console.WriteLine($"  전체 소요: {elapsed:F2}s");
        Console.WriteLine($"  처리 속도: {rowsPerSec:N0} rows/sec");
    }
}
