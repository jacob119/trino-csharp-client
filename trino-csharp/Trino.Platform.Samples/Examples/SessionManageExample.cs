using Trino.Data.ADO.Server;

namespace Trino.Platform.Samples.Examples;

/// <summary>
/// 5. 세션 관리 - SET SESSION, USE, 세션 프로퍼티 직접 접근
/// </summary>
public static class SessionManageExample
{
    public static void Run(TrinoConnectionProperties properties)
    {
        Console.WriteLine("[5] 세션 관리");
        Console.WriteLine();

        using var connection = new TrinoConnection(properties);

        // 세션 프로퍼티 직접 설정
        Console.WriteLine("1) 세션 프로퍼티 직접 설정:");
        connection.ConnectionSession.Properties.Source = "PlatformSample";
        Console.WriteLine($"   Source = {connection.ConnectionSession.Properties.Source}");
        Console.WriteLine();

        // SET SESSION SQL 명령
        Console.WriteLine("2) SET SESSION SQL 명령:");
        using (var cmd = new TrinoCommand(connection, "SET SESSION query_max_run_time = '10m'"))
        {
            cmd.ExecuteNonQuery();
            Console.WriteLine("   SET SESSION query_max_run_time = '10m' 실행 완료");
        }
        Console.WriteLine();

        // USE로 카탈로그/스키마 변경
        Console.WriteLine("3) USE 명령으로 카탈로그/스키마 변경:");
        using (var cmd = new TrinoCommand(connection, "USE tpch.sf1"))
        {
            cmd.ExecuteNonQuery();
        }

        var sessionProps = connection.ConnectionSession.Properties;
        Console.WriteLine($"   Catalog: {sessionProps.Catalog}");
        Console.WriteLine($"   Schema:  {sessionProps.Schema}");
        Console.WriteLine($"   Source:  {sessionProps.Source}");
        Console.WriteLine();

        // 현재 세션 프로퍼티 목록 출력
        Console.WriteLine("4) 현재 세션 프로퍼티 목록:");
        if (sessionProps.Properties.Count > 0)
        {
            foreach (var kv in sessionProps.Properties)
            {
                Console.WriteLine($"   {kv.Key} = {kv.Value}");
            }
        }
        else
        {
            Console.WriteLine("   (설정된 프로퍼티 없음)");
        }

        // 변경된 스키마에서 쿼리 실행
        Console.WriteLine();
        Console.WriteLine("5) 변경된 스키마(sf1)에서 쿼리 실행:");
        using var queryCmd = new TrinoCommand(connection, "SELECT count(*) AS cnt FROM customer");
        using var reader = queryCmd.ExecuteReader();

        if (reader.Read())
        {
            Console.WriteLine($"   customer 테이블 행 수: {reader.GetValue(0)}");
        }
    }
}
