using System.Data;
using Trino.Data.ADO.Server;

namespace Trino.Platform.Samples.Examples;

/// <summary>
/// 8. 스키마/메타데이터 조회 - ADO.NET GetSchema()를 활용한 메타데이터 탐색
/// </summary>
public static class SchemaInfoExample
{
    public static void Run(TrinoConnectionProperties properties)
    {
        Console.WriteLine("[8] 스키마/메타데이터 조회");
        Console.WriteLine();

        using var connection = new TrinoConnection(properties);

        // ─────────────────────────────────────────────
        // 사용 가능한 스키마 컬렉션 목록
        // ─────────────────────────────────────────────
        Console.WriteLine("1) 사용 가능한 스키마 컬렉션:");
        var schemas = connection.GetSchema();
        PrintTable(schemas, maxRows: 10);
        Console.WriteLine();

        // ─────────────────────────────────────────────
        // 테이블 목록 조회
        // ─────────────────────────────────────────────
        Console.WriteLine("2) 테이블 목록:");
        var tables = connection.GetSchema("tables");
        PrintTable(tables, maxRows: 15);
        Console.WriteLine();

        // ─────────────────────────────────────────────
        // 컬럼 정보 조회
        // ─────────────────────────────────────────────
        Console.WriteLine("3) 컬럼 정보 (상위 20개):");
        var columns = connection.GetSchema("columns");
        PrintTable(columns, maxRows: 20);
        Console.WriteLine();

        // ─────────────────────────────────────────────
        // SQL로 직접 메타데이터 조회
        // ─────────────────────────────────────────────
        Console.WriteLine("4) SQL 기반 메타데이터 조회:");
        Console.WriteLine("   SHOW TABLES:");
        using var cmd = new TrinoCommand(connection, "SHOW TABLES FROM tpch.tiny");
        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            Console.WriteLine($"     - {reader.GetValue(0)}");
        }
    }

    private static void PrintTable(DataTable table, int maxRows = 10)
    {
        // 헤더
        foreach (DataColumn col in table.Columns)
        {
            Console.Write($"   {col.ColumnName,-25}");
        }
        Console.WriteLine();
        Console.WriteLine("   " + new string('-', table.Columns.Count * 25));

        // 데이터 (최대 maxRows 행)
        int count = 0;
        foreach (DataRow row in table.Rows)
        {
            if (count >= maxRows)
            {
                Console.WriteLine($"   ... 외 {table.Rows.Count - maxRows}건 추가");
                break;
            }

            foreach (DataColumn col in table.Columns)
            {
                var val = row[col]?.ToString() ?? "(null)";
                if (val.Length > 23) val = val[..23] + "..";
                Console.Write($"   {val,-25}");
            }
            Console.WriteLine();
            count++;
        }

        Console.WriteLine($"   총 {table.Rows.Count}건");
    }
}
