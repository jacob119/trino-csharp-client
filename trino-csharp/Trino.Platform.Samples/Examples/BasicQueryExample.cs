using System.Data;
using Trino.Data.ADO.Server;

namespace Trino.Platform.Samples.Examples;

/// <summary>
/// 1. ADO.NET 기본 쿼리 - 가장 기본적인 Trino 접속 및 SELECT 실행
/// </summary>
public static class BasicQueryExample
{
    public static void Run(TrinoConnectionProperties properties)
    {
        Console.WriteLine("[1] ADO.NET 기본 쿼리");
        Console.WriteLine();

        using var connection = new TrinoConnection(properties);
        using var command = new TrinoCommand(
            connection,
            "SELECT custkey, name, nationkey, acctbal FROM tpch.tiny.customer LIMIT 10");

        using var reader = command.ExecuteReader();

        // 컬럼 헤더 출력
        for (int i = 0; i < reader.FieldCount; i++)
        {
            Console.Write($"{reader.GetName(i),-20}");
        }
        Console.WriteLine();
        Console.WriteLine(new string('-', 80));

        // 데이터 출력
        int rowCount = 0;
        while (reader.Read())
        {
            for (int i = 0; i < reader.FieldCount; i++)
            {
                var value = reader.IsDBNull(i) ? "(null)" : reader.GetValue(i)?.ToString();
                Console.Write($"{value,-20}");
            }
            Console.WriteLine();
            rowCount++;
        }

        Console.WriteLine();
        Console.WriteLine($"총 {rowCount}건 조회 완료");
    }
}
