using System.Diagnostics;
using System.Text.Json;
using Trino.Platform.Samples.Http;

namespace Trino.Platform.Samples.Examples;

/// <summary>
/// 11. HTTP 직접 접속 - Trino C# Client 없이 순수 HttpClient로 REST API 호출
/// </summary>
public static class HttpQueryExample
{
    public static async Task RunAsync(string host, int port)
    {
        Console.WriteLine("[11] HTTP 직접 접속 (순수 HttpClient)");
        Console.WriteLine();

        using var client = new TrinoHttpClient(host: host, port: port);

        // ─────────────────────────────────────────────
        // 1) 기본 SELECT 쿼리
        // ─────────────────────────────────────────────
        Console.WriteLine("--- 1) 기본 SELECT 쿼리 ---");
        Console.WriteLine("POST /v1/statement → nextUri 폴링 → 결과 수신");
        Console.WriteLine();

        var result = await client.ExecuteQueryAsync(
            "SELECT custkey, name, acctbal FROM tpch.tiny.customer LIMIT 5");

        if (!result.Success)
        {
            Console.WriteLine($"오류: {result.Error!.Message}");
            return;
        }

        // 컬럼 정보
        Console.Write("  ");
        foreach (var col in result.Columns)
            Console.Write($"{col.Name} ({col.Type})  ");
        Console.WriteLine();
        Console.WriteLine("  " + new string('-', 60));

        // 데이터
        foreach (var row in result.Rows)
        {
            Console.WriteLine($"  {string.Join(" | ", row)}");
        }

        Console.WriteLine($"  총 {result.Rows.Count}건, 폴링 {result.TotalPages}페이지");
        Console.WriteLine($"  처리: {result.Stats?.ProcessedRows} rows, 상태: {result.Stats?.State}");

        // ─────────────────────────────────────────────
        // 2) 에러 핸들링
        // ─────────────────────────────────────────────
        Console.WriteLine();
        Console.WriteLine("--- 2) 에러 핸들링 (잘못된 SQL) ---");

        var errorResult = await client.ExecuteQueryAsync("SELECT * FROM nonexistent_table_xyz");

        if (!errorResult.Success)
        {
            Console.WriteLine($"  오류 코드: {errorResult.Error!.ErrorCode}");
            Console.WriteLine($"  오류 이름: {errorResult.Error.ErrorName}");
            Console.WriteLine($"  오류 타입: {errorResult.Error.ErrorType}");
            Console.WriteLine($"  메시지: {errorResult.Error.Message}");
        }

        // ─────────────────────────────────────────────
        // 3) 스트리밍 방식 (페이지 단위)
        // ─────────────────────────────────────────────
        Console.WriteLine();
        Console.WriteLine("--- 3) 스트리밍 방식 (sf1.customer 15만건) ---");

        var sw = Stopwatch.StartNew();
        int totalRows = 0;
        int pageCount = 0;

        await foreach (var page in client.ExecuteQueryStreamingAsync(
            "SELECT * FROM tpch.sf1.customer"))
        {
            if (page.HasError)
            {
                Console.WriteLine($"  오류: {page.Error!.Message}");
                break;
            }

            pageCount++;
            if (page.HasData)
                totalRows += page.Data!.Count;
        }

        sw.Stop();
        var rowsPerSec = totalRows / sw.Elapsed.TotalSeconds;
        Console.WriteLine($"  총 {totalRows:N0}건, {pageCount} 페이지");
        Console.WriteLine($"  소요: {sw.Elapsed.TotalSeconds:F2}s ({rowsPerSec:N0} rows/sec)");

        // ─────────────────────────────────────────────
        // 4) Raw JSON 응답 확인
        // ─────────────────────────────────────────────
        Console.WriteLine();
        Console.WriteLine("--- 4) Raw JSON 응답 구조 확인 ---");

        var rawResult = await client.SubmitQueryAsync("SELECT 1 AS num, 'hello' AS msg");
        var rawJson = JsonSerializer.Serialize(rawResult, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine($"  POST /v1/statement 첫 응답:");
        // 출력이 너무 길면 잘라냄
        if (rawJson.Length > 800)
            Console.WriteLine($"  {rawJson[..800]}...");
        else
            Console.WriteLine($"  {rawJson}");
    }
}
