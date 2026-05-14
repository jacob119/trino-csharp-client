using System.Data;
using Trino.Client;
using Trino.Client.Utils;
using Trino.Data.ADO.Server;

namespace Trino.Platform.Samples.Examples;

/// <summary>
/// 7. DataTable 변환 - 쿼리 결과를 DataTable로 변환하여 활용
/// </summary>
public static class DataTableExample
{
    public static async Task RunAsync(TrinoConnectionProperties properties)
    {
        Console.WriteLine("[7] DataTable 변환");
        Console.WriteLine();

        // ─────────────────────────────────────────────
        // SDK를 사용한 DataTable 생성
        // ─────────────────────────────────────────────
        Console.WriteLine("1) SDK RecordExecutor -> DataTable:");
        Console.WriteLine();

        var session = properties.GetSession();

        var records = await RecordExecutor.Execute(
            session: session,
            statement: "SELECT regionkey, name, comment FROM tpch.tiny.region")
            .ConfigureAwait(false);

        var dataTable = await records.BuildDataTableAsync().ConfigureAwait(false);

        // DataTable 정보 출력
        Console.WriteLine($"   테이블 행 수: {dataTable.Rows.Count}");
        Console.WriteLine($"   컬럼 수: {dataTable.Columns.Count}");
        Console.WriteLine();

        // 컬럼 스키마 정보
        Console.WriteLine("   컬럼 스키마:");
        foreach (DataColumn col in dataTable.Columns)
        {
            Console.WriteLine($"     {col.ColumnName,-20} {col.DataType.Name,-15}");
        }
        Console.WriteLine();

        // DataTable 내용 출력
        Console.WriteLine("   데이터:");
        foreach (DataRow row in dataTable.Rows)
        {
            var values = new List<string>();
            foreach (DataColumn col in dataTable.Columns)
            {
                values.Add(row[col]?.ToString() ?? "(null)");
            }
            Console.WriteLine($"     {string.Join(" | ", values)}");
        }

        // ─────────────────────────────────────────────
        // DataTable 활용: LINQ 쿼리
        // ─────────────────────────────────────────────
        Console.WriteLine();
        Console.WriteLine("2) DataTable LINQ 활용:");
        var filtered = dataTable.AsEnumerable()
            .Where(r => r.Field<string>("name")?.Contains("A") == true)
            .Select(r => r.Field<string>("name"));

        Console.WriteLine("   이름에 'A' 포함된 Region:");
        foreach (var name in filtered)
        {
            Console.WriteLine($"     - {name}");
        }
    }
}
