using System;
using System.Data;
using System.Diagnostics;
using Trino.Data.ADO.Server;

namespace Trino.Integration.Test
{
    class Program
    {
        const string Host = "localhost";
        const int Port = 8081;
        const bool Ssl = false;

        static int pass = 0, fail = 0;

        static void Main()
        {
            Console.WriteLine("=== Trino C# Client Integration Test ===");
            Console.WriteLine($"Target: http://{Host}:{Port}");
            Console.WriteLine();

            RunTest("1. 기본 연결 및 SELECT 1", TestBasicSelect);
            RunTest("2. SHOW CATALOGS", TestShowCatalogs);
            RunTest("3. tpch 카탈로그 쿼리 (SHOW SCHEMAS)", TestShowSchemas);
            RunTest("4. tpch.tiny.nation 데이터 조회", TestSelectData);
            RunTest("5. 집계 쿼리", TestAggregation);
            RunTest("6. 다중 행 스트리밍 읽기", TestStreaming);
            RunTest("7. ExecuteNonQuery (SET SESSION)", TestSetSession);
            RunTest("8. GetSchema (information_schema)", TestGetSchema);
            RunTest("9. 파라미터 바인딩", TestParameters);
            RunTest("10. 연결 타임아웃", TestTimeout);
            RunTest("11. 사용자 임퍼소네이션 (X-Trino-Authorization-User)", TestImpersonation);

            Console.WriteLine();
            Console.WriteLine("=== 결과 ===");
            Console.WriteLine($"통과: {pass}  실패: {fail}  전체: {pass + fail}");
            Console.WriteLine(fail == 0 ? "✓ 모든 테스트 통과" : $"✗ {fail}개 실패");
        }

        static void RunTest(string name, Action action)
        {
            Console.Write($"  [{name}] ... ");
            var sw = Stopwatch.StartNew();
            try
            {
                action();
                sw.Stop();
                Console.WriteLine($"PASS ({sw.ElapsedMilliseconds}ms)");
                pass++;
            }
            catch (Exception ex)
            {
                sw.Stop();
                Console.WriteLine($"FAIL ({sw.ElapsedMilliseconds}ms)");
                Console.WriteLine($"    → {ex.GetType().Name}: {ex.Message}");
                if (ex.InnerException != null)
                    Console.WriteLine($"    → Inner: {ex.InnerException.Message}");
                fail++;
            }
        }

        static TrinoConnectionProperties MakeProps(string catalog = "tpch", string? schema = null)
        {
            var p = new TrinoConnectionProperties
            {
                Host = Host,
                Port = Port,
                EnableSsl = Ssl,
                Catalog = catalog,
            };
            if (schema != null) p.Schema = schema;
            return p;
        }

        static void TestBasicSelect()
        {
            using var tc = new TrinoConnection(MakeProps());
            using var cmd = new TrinoCommand(tc, "SELECT 1 AS val");
            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) throw new Exception("No row returned");
            var val = reader.GetInt32(0);
            if (val != 1) throw new Exception($"Expected 1, got {val}");
        }

        static void TestShowCatalogs()
        {
            using var tc = new TrinoConnection(MakeProps());
            using var cmd = new TrinoCommand(tc, "SHOW CATALOGS");
            using var reader = cmd.ExecuteReader();
            int count = 0;
            while (reader.Read())
            {
                Console.Write($"\n    catalog: {reader.GetString(0)}");
                count++;
            }
            Console.WriteLine();
            if (count == 0) throw new Exception("No catalogs returned");
        }

        static void TestShowSchemas()
        {
            using var tc = new TrinoConnection(MakeProps("tpch"));
            using var cmd = new TrinoCommand(tc, "SHOW SCHEMAS FROM tpch");
            using var reader = cmd.ExecuteReader();
            int count = 0;
            while (reader.Read()) { count++; }
            if (count == 0) throw new Exception("No schemas returned");
            Console.Write($"\n    tpch schema count: {count}");
        }

        static void TestSelectData()
        {
            using var tc = new TrinoConnection(MakeProps("tpch", "tiny"));
            using var cmd = new TrinoCommand(tc, "SELECT nationkey, name, regionkey FROM tpch.tiny.nation ORDER BY nationkey LIMIT 5");
            using var reader = cmd.ExecuteReader();
            int count = 0;
            while (reader.Read())
            {
                var key = reader.GetValue(0);
                var name = reader.GetString(1);
                Console.Write($"\n    {key}: {name}");
                count++;
            }
            Console.WriteLine();
            if (count == 0) throw new Exception("No rows returned");
        }

        static void TestAggregation()
        {
            using var tc = new TrinoConnection(MakeProps("tpch", "tiny"));
            using var cmd = new TrinoCommand(tc, "SELECT COUNT(*), AVG(acctbal), MAX(acctbal) FROM tpch.tiny.customer");
            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) throw new Exception("No row");
            long cnt = (long)reader.GetValue(0);
            Console.Write($"\n    customer count={cnt}, avg_acctbal={reader.GetValue(1):F2}, max_acctbal={reader.GetValue(2)}");
            if (cnt <= 0) throw new Exception("Count should be > 0");
        }

        static void TestStreaming()
        {
            using var tc = new TrinoConnection(MakeProps("tpch", "tiny"));
            using var cmd = new TrinoCommand(tc, "SELECT * FROM tpch.tiny.lineitem");
            using var reader = cmd.ExecuteReader();
            int count = 0;
            while (reader.Read()) count++;
            Console.Write($"\n    lineitem rows: {count:N0}");
            if (count == 0) throw new Exception("No rows");
        }

        static void TestSetSession()
        {
            using var tc = new TrinoConnection(MakeProps("tpch"));
            using var cmd = new TrinoCommand(tc, "SET SESSION task_concurrency=4");
            cmd.ExecuteNonQuery();
        }

        static void TestGetSchema()
        {
            using var tc = new TrinoConnection(MakeProps("tpch", "tiny"));
            DataTable schemas = tc.GetSchema();
            if (schemas.Rows.Count == 0) throw new Exception("No schema collections");
            Console.Write($"\n    schema collections: {schemas.Rows.Count}");

            DataTable columns = tc.GetSchema("columns");
            if (columns.Rows.Count == 0) throw new Exception("No columns");
            Console.Write($", columns: {columns.Rows.Count}");
        }

        static void TestParameters()
        {
            using var tc = new TrinoConnection(MakeProps("tpch", "tiny"));
            using var cmd = new TrinoCommand(tc, "SELECT name FROM tpch.tiny.nation WHERE name = ?");
            var p = cmd.CreateParameter();
            p.Value = "FRANCE";
            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) throw new Exception("No row for FRANCE");
            Console.Write($"\n    result: {reader.GetString(0)}");
        }

        static void TestTimeout()
        {
            using var tc = new TrinoConnection(MakeProps("tpch"));
            using var cmd = new TrinoCommand(tc, "SELECT 1", TimeSpan.FromSeconds(30), null, null);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) throw new Exception("No result");
            Console.Write($"\n    timeout test passed (val={reader.GetValue(0)})");
        }

        static void TestImpersonation()
        {
            var props = MakeProps("tpch");
            props.User = "admin";
            props.AuthorizationUser = "admin";

            using var tc = new TrinoConnection(props);
            using var cmd = new TrinoCommand(tc, "SELECT current_user");
            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) throw new Exception("No row returned");
            var currentUser = reader.GetString(0);
            Console.Write($"\n    current_user with impersonation: {currentUser}");
            if (currentUser != "admin")
                throw new Exception($"Expected 'admin' but got '{currentUser}'");
        }
    }
}
