using System.Collections.Concurrent;
using System.Diagnostics;
using Trino.Data.ADO.Client;
using Trino.Data.ADO.Server;

namespace Trino.Platform.Samples.Examples;

/// <summary>
/// 9. 병렬 쿼리 - 멀티스레드 동시 쿼리 실행 및 성능 측정
/// </summary>
public static class ParallelQueryExample
{
    public static async Task RunAsync(TrinoConnectionProperties properties)
    {
        Console.WriteLine("[9] 병렬 쿼리");
        Console.WriteLine();

        const int threadCount = 5;
        const string query = @"
            SELECT sum(acctbal), avg(acctbal), nationkey, mktsegment
            FROM tpch.tiny.customer
            WHERE mktsegment IN ('AUTOMOBILE', 'BUILDING')
            GROUP BY nationkey, mktsegment";

        var results = new ConcurrentBag<(int ThreadId, int RowCount, double ElapsedMs)>();
        var totalStopwatch = Stopwatch.StartNew();

        Console.WriteLine($"스레드 수: {threadCount}");
        Console.WriteLine($"쿼리: SELECT sum(acctbal), avg(acctbal) ... GROUP BY nationkey, mktsegment");
        Console.WriteLine();
        Console.WriteLine("실행 중...");

        // 병렬 실행
        var tasks = Enumerable.Range(0, threadCount).Select(i => Task.Run(() =>
        {
            var sw = Stopwatch.StartNew();

            var localProps = new TrinoConnectionProperties
            {
                Catalog = properties.Catalog,
                Schema = properties.Schema,
                Server = properties.Server,
                Auth = properties.Auth,
                User = properties.User,
                TestConnection = false
            };

            using var connection = new TrinoConnection(localProps);
            using var command = new TrinoCommand(connection, query);
            using var reader = command.ExecuteReader();

            int rowCount = 0;
            while (reader.Read())
            {
                rowCount++;
            }

            sw.Stop();
            results.Add((i, rowCount, sw.Elapsed.TotalMilliseconds));
        })).ToArray();

        await Task.WhenAll(tasks);
        totalStopwatch.Stop();

        // 결과 출력
        Console.WriteLine();
        Console.Write($"{"Thread",-10}{"Rows",-10}{"Time (ms)",-15}");
        Console.WriteLine();
        Console.WriteLine(new string('-', 35));

        foreach (var r in results.OrderBy(x => x.ThreadId))
        {
            Console.WriteLine($"{r.ThreadId,-10}{r.RowCount,-10}{r.ElapsedMs:F1}ms");
        }

        Console.WriteLine();
        Console.WriteLine($"전체 소요 시간: {totalStopwatch.Elapsed.TotalMilliseconds:F1}ms");
        Console.WriteLine($"총 쿼리 수: {results.Count}");
        Console.WriteLine($"평균 응답 시간: {results.Average(r => r.ElapsedMs):F1}ms");
        Console.WriteLine($"최대 응답 시간: {results.Max(r => r.ElapsedMs):F1}ms");
        Console.WriteLine($"최소 응답 시간: {results.Min(r => r.ElapsedMs):F1}ms");
    }
}
