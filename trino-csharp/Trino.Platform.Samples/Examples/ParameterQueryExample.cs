using Trino.Client;
using Trino.Data.ADO;
using Trino.Data.ADO.Server;

namespace Trino.Platform.Samples.Examples;

/// <summary>
/// 6. 파라미터 쿼리 - SQL Injection 방지를 위한 파라미터 바인딩
/// </summary>
public static class ParameterQueryExample
{
    public static void Run(TrinoConnectionProperties properties)
    {
        Console.WriteLine("[6] 파라미터 쿼리");
        Console.WriteLine();

        // ─────────────────────────────────────────────
        // ADO.NET 방식의 파라미터 쿼리
        // ─────────────────────────────────────────────
        Console.WriteLine("1) ADO.NET 방식 파라미터 쿼리:");
        Console.WriteLine("   SQL: SELECT ... WHERE nationkey = ? (positional parameter)");
        Console.WriteLine();

        using var connection = new TrinoConnection(properties);
        using var command = new TrinoCommand(
            connection,
            "SELECT custkey, name, acctbal FROM tpch.tiny.customer WHERE nationkey = ? LIMIT 5");

        // Trino는 ? (positional) 파라미터 사용, 순서대로 바인딩
        command.Parameters.Add(new TrinoParameter { ParameterName = "p1", Value = 10 });

        using var reader = command.ExecuteReader();

        Console.Write($"{"custkey",-12}{"name",-25}{"acctbal",-15}");
        Console.WriteLine();
        Console.WriteLine(new string('-', 52));

        int rowCount = 0;
        while (reader.Read())
        {
            var custkey = reader.GetValue(0);
            var name = reader.GetValue(1);
            var acctbal = reader.GetValue(2);
            Console.WriteLine($"{custkey,-12}{name,-25}{acctbal,-15}");
            rowCount++;
        }

        Console.WriteLine();
        Console.WriteLine($"nationkey=10 조건으로 {rowCount}건 조회");

        // ─────────────────────────────────────────────
        // SDK 방식의 파라미터 쿼리
        // ─────────────────────────────────────────────
        Console.WriteLine();
        Console.WriteLine("2) SDK 방식 파라미터 쿼리 (코드 예시):");
        Console.WriteLine();
        Console.WriteLine("   var records = await RecordExecutor.Execute(");
        Console.WriteLine("       session: session,");
        Console.WriteLine("       statement: \"SELECT * FROM customer WHERE custkey = ?\",");
        Console.WriteLine("       queryParameters: new List<QueryParameter> { new(751) }");
        Console.WriteLine("   ).ConfigureAwait(false);");
    }
}
