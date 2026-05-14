using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using Trino.Client.Types;
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

            RunTest("1.  기본 연결 및 SELECT 1", TestBasicSelect);
            RunTest("2.  SHOW CATALOGS", TestShowCatalogs);
            RunTest("3.  tpch 카탈로그 쿼리 (SHOW SCHEMAS)", TestShowSchemas);
            RunTest("4.  tpch.tiny.nation 데이터 조회", TestSelectData);
            RunTest("5.  집계 쿼리", TestAggregation);
            RunTest("6.  다중 행 스트리밍 읽기", TestStreaming);
            RunTest("7.  ExecuteNonQuery (SET SESSION)", TestSetSession);
            RunTest("8.  GetSchema (information_schema)", TestGetSchema);
            RunTest("9.  파라미터 바인딩", TestParameters);
            RunTest("10. 연결 타임아웃", TestTimeout);
            RunTest("11. 사용자 임퍼소네이션 (X-Trino-Authorization-User)", TestImpersonation);
            RunTest("12. 숫자/불리언 타입 매핑", TestNumericTypes);
            RunTest("13. 날짜/시간 타입 매핑", TestDateTimeTypes);
            RunTest("14. 문자열/바이너리/특수 타입 매핑", TestStringBinaryTypes);
            RunTest("15. 복합 타입 매핑 (array, map, row)", TestComplexTypes);
            RunTest("16. NULL 처리", TestNullHandling);

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

        // ── 기존 테스트 ────────────────────────────────────────────────────────────

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

        // ── 타입 매핑 테스트 ─────────────────────────────────────────────────────

        static void AssertType(object value, Type expectedClrType, string label)
        {
            if (value == null)
                throw new Exception($"[{label}] value is null");
            if (value.GetType() != expectedClrType)
                throw new Exception($"[{label}] expected CLR type {expectedClrType.Name} but got {value.GetType().Name} (value={value})");
        }

        static void TestNumericTypes()
        {
            string sql = @"SELECT
                CAST(9223372036854775807 AS bigint)     AS bigint_col,
                CAST(2147483647 AS integer)             AS int_col,
                CAST(32767 AS smallint)                 AS smallint_col,
                CAST(127 AS tinyint)                    AS tinyint_col,
                CAST(-127 AS tinyint)                   AS neg_tinyint_col,
                CAST(1.7976931348623158E+308 AS double) AS double_col,
                CAST(3.402823E+38 AS real)              AS real_col,
                CAST(123.456 AS decimal(10,3))          AS decimal_col,
                CAST(99999999999999999999.12345 AS decimal(38,5)) AS big_decimal_col,
                true                                    AS bool_true,
                false                                   AS bool_false";

            using var tc = new TrinoConnection(MakeProps());
            using var cmd = new TrinoCommand(tc, sql);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) throw new Exception("No row");

            var bigint = r.GetValue(0);
            AssertType(bigint, typeof(long), "bigint");
            if ((long)bigint != 9223372036854775807L) throw new Exception($"bigint mismatch: {bigint}");

            var intVal = r.GetValue(1);
            AssertType(intVal, typeof(int), "integer");
            if ((int)intVal != 2147483647) throw new Exception($"integer mismatch: {intVal}");

            var smallint = r.GetValue(2);
            AssertType(smallint, typeof(short), "smallint");
            if ((short)smallint != 32767) throw new Exception($"smallint mismatch: {smallint}");

            var tinyint = r.GetValue(3);
            AssertType(tinyint, typeof(sbyte), "tinyint");
            if ((sbyte)tinyint != 127) throw new Exception($"tinyint mismatch: {tinyint}");

            var negTinyint = r.GetValue(4);
            AssertType(negTinyint, typeof(sbyte), "tinyint(neg)");
            if ((sbyte)negTinyint != -127) throw new Exception($"tinyint(neg) mismatch: {negTinyint}");

            var dbl = r.GetValue(5);
            AssertType(dbl, typeof(double), "double");

            var real = r.GetValue(6);
            AssertType(real, typeof(float), "real");

            var dec = r.GetValue(7);
            AssertType(dec, typeof(TrinoBigDecimal), "decimal");
            var decVal = (TrinoBigDecimal)dec;
            if (decVal.ToDecimal() != 123.456m) throw new Exception($"decimal mismatch: {decVal}");

            var bigDec = r.GetValue(8);
            AssertType(bigDec, typeof(TrinoBigDecimal), "big_decimal");

            var boolTrue = r.GetValue(9);
            AssertType(boolTrue, typeof(bool), "bool_true");
            if (!(bool)boolTrue) throw new Exception("Expected true");

            var boolFalse = r.GetValue(10);
            AssertType(boolFalse, typeof(bool), "bool_false");
            if ((bool)boolFalse) throw new Exception("Expected false");

            Console.Write($"\n    bigint={bigint}, int={intVal}, smallint={smallint}, tinyint={tinyint}(neg={negTinyint})");
            Console.Write($"\n    double={dbl}, real={real}, decimal={dec}, big_decimal={bigDec}");
        }

        static void TestDateTimeTypes()
        {
            string sql = @"SELECT
                DATE '2024-01-15'                                   AS date_col,
                TIME '12:30:45.123'                                 AS time_col,
                TIMESTAMP '2024-01-15 12:30:45.123'                 AS ts_col,
                TIMESTAMP '2024-01-15 12:30:45.123456789 UTC'       AS tstz_col,
                TIMESTAMP '2024-01-15 12:30:45.123456789 +09:00'    AS tstz_offset_col,
                INTERVAL '3' YEAR + INTERVAL '5' MONTH              AS interval_ym,
                INTERVAL '2' DAY + INTERVAL '1' HOUR                AS interval_ds";

            using var tc = new TrinoConnection(MakeProps());
            using var cmd = new TrinoCommand(tc, sql);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) throw new Exception("No row");

            var date = r.GetValue(0);
            AssertType(date, typeof(DateTime), "date");
            var dateVal = (DateTime)date;
            if (dateVal.Year != 2024 || dateVal.Month != 1 || dateVal.Day != 15)
                throw new Exception($"date mismatch: {date}");

            var time = r.GetValue(1);
            AssertType(time, typeof(TimeSpan), "time");

            var ts = r.GetValue(2);
            AssertType(ts, typeof(DateTime), "timestamp");
            var tsVal = (DateTime)ts;
            if (tsVal.Year != 2024 || tsVal.Month != 1 || tsVal.Day != 15)
                throw new Exception($"timestamp mismatch: {ts}");

            // Trino 478 timestamp supports up to 12 digits; we truncate to 7 (ticks)
            var tstz = r.GetValue(3);
            AssertType(tstz, typeof(DateTimeOffset), "timestamp with time zone (UTC)");
            var tstzVal = (DateTimeOffset)tstz;
            if (tstzVal.Year != 2024 || tstzVal.Month != 1 || tstzVal.Day != 15)
                throw new Exception($"tstz mismatch: {tstz}");
            if (tstzVal.Offset != TimeSpan.Zero)
                throw new Exception($"tstz offset mismatch: {tstzVal.Offset}");

            var tstzOffset = r.GetValue(4);
            AssertType(tstzOffset, typeof(DateTimeOffset), "timestamp with time zone (+09:00)");
            var tstzOffsetVal = (DateTimeOffset)tstzOffset;
            if (tstzOffsetVal.Offset != TimeSpan.FromHours(9))
                throw new Exception($"tstz +09:00 offset mismatch: {tstzOffsetVal.Offset}");

            var iym = r.GetValue(5);
            AssertType(iym, typeof(TrinoIntervalYearToMonth), "interval year to month");
            var iymVal = (TrinoIntervalYearToMonth)iym;
            if (iymVal.Year != 3 || iymVal.Month != 5)
                throw new Exception($"interval_ym mismatch: year={iymVal.Year} month={iymVal.Month}");

            var ids = r.GetValue(6);
            AssertType(ids, typeof(TimeSpan), "interval day to second");
            var idsVal = (TimeSpan)ids;
            if (idsVal.Days != 2) throw new Exception($"interval_ds days mismatch: {idsVal.Days}");

            Console.Write($"\n    date={dateVal:yyyy-MM-dd}, time={time}, ts={tsVal:yyyy-MM-dd HH:mm:ss}");
            Console.Write($"\n    tstz(UTC)={tstzVal:yyyy-MM-dd HH:mm:ss zzz}, tstz(+09)={tstzOffsetVal:yyyy-MM-dd HH:mm:ss zzz}");
            Console.Write($"\n    interval_ym={iym}, interval_ds={ids}");
        }

        static void TestStringBinaryTypes()
        {
            string sql = @"SELECT
                CAST('hello world' AS varchar)          AS varchar_col,
                CAST('ABCDE' AS char(10))               AS char_col,
                X'48656C6C6F'                           AS varbinary_col,
                UUID '12151fd2-7586-11e9-8f9e-2a86e4085a59' AS uuid_col,
                CAST('192.168.1.1' AS ipaddress)        AS ip_col,
                JSON '{""key"":""value""}'              AS json_col";

            using var tc = new TrinoConnection(MakeProps());
            using var cmd = new TrinoCommand(tc, sql);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) throw new Exception("No row");

            var varchar = r.GetValue(0);
            AssertType(varchar, typeof(string), "varchar");
            if ((string)varchar != "hello world") throw new Exception($"varchar mismatch: {varchar}");

            var charVal = r.GetValue(1);
            AssertType(charVal, typeof(string), "char");
            // Trino char(10) pads with spaces; trim for assertion
            if (!((string)charVal).TrimEnd().Equals("ABCDE"))
                throw new Exception($"char mismatch: '{charVal}'");

            var varbinary = r.GetValue(2);
            AssertType(varbinary, typeof(byte[]), "varbinary");
            var bytes = (byte[])varbinary;
            var decoded = System.Text.Encoding.UTF8.GetString(bytes);
            if (decoded != "Hello") throw new Exception($"varbinary mismatch: {decoded}");

            var uuid = r.GetValue(3);
            AssertType(uuid, typeof(Guid), "uuid");
            var expectedGuid = Guid.Parse("12151fd2-7586-11e9-8f9e-2a86e4085a59");
            if ((Guid)uuid != expectedGuid) throw new Exception($"uuid mismatch: {uuid}");

            var ip = r.GetValue(4);
            AssertType(ip, typeof(string), "ipaddress");
            if ((string)ip != "192.168.1.1") throw new Exception($"ipaddress mismatch: {ip}");

            var json = r.GetValue(5);
            AssertType(json, typeof(string), "json");

            Console.Write($"\n    varchar='{varchar}', char='{charVal}', varbinary(decoded)='{decoded}'");
            Console.Write($"\n    uuid={uuid}, ip={ip}, json={json}");
        }

        static void TestComplexTypes()
        {
            string sql = @"SELECT
                ARRAY[1, 2, 3]                              AS int_array,
                ARRAY['a', 'b', 'c']                        AS str_array,
                ARRAY[ARRAY[1,2], ARRAY[3,4]]               AS nested_array,
                MAP(ARRAY['x','y'], ARRAY[10, 20])          AS str_int_map,
                MAP(ARRAY[1,2], ARRAY['one','two'])          AS int_str_map,
                CAST(ROW(42, 'hello', true) AS ROW(num integer, str varchar, flag boolean)) AS row_col";

            using var tc = new TrinoConnection(MakeProps());
            using var cmd = new TrinoCommand(tc, sql);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) throw new Exception("No row");

            var intArray = r.GetValue(0);
            AssertType(intArray, typeof(List<object>), "array(int)");
            var intList = (List<object>)intArray;
            if (intList.Count != 3) throw new Exception($"array count mismatch: {intList.Count}");
            if ((int)intList[0] != 1) throw new Exception($"array[0] mismatch: {intList[0]}");

            var strArray = r.GetValue(1);
            AssertType(strArray, typeof(List<object>), "array(varchar)");
            var strList = (List<object>)strArray;
            if ((string)strList[0] != "a") throw new Exception($"str_array[0] mismatch: {strList[0]}");

            var nestedArray = r.GetValue(2);
            AssertType(nestedArray, typeof(List<object>), "array(array)");
            var nested = (List<object>)nestedArray;
            if (nested.Count != 2) throw new Exception($"nested array count mismatch: {nested.Count}");

            var strIntMap = r.GetValue(3);
            AssertType(strIntMap, typeof(Dictionary<object, object>), "map(varchar,int)");
            var mapObj = (Dictionary<object, object>)strIntMap;
            if (mapObj.Count != 2) throw new Exception($"map count mismatch: {mapObj.Count}");

            var intStrMap = r.GetValue(4);
            AssertType(intStrMap, typeof(Dictionary<object, object>), "map(int,varchar)");

            var rowVal = r.GetValue(5);
            AssertType(rowVal, typeof(List<object>), "row");
            var rowList = (List<object>)rowVal;
            if (rowList.Count != 3) throw new Exception($"row field count mismatch: {rowList.Count}");

            Console.Write($"\n    int_array=[{string.Join(",", intList)}]");
            Console.Write($"\n    str_array=[{string.Join(",", strList)}]");
            Console.Write($"\n    map entries={mapObj.Count}");
            Console.Write($"\n    row fields={rowList.Count}: [{string.Join(",", rowList)}]");
        }

        static void TestNullHandling()
        {
            string sql = @"SELECT
                CAST(NULL AS bigint)     AS null_bigint,
                CAST(NULL AS varchar)    AS null_varchar,
                CAST(NULL AS boolean)    AS null_bool,
                CAST(NULL AS double)     AS null_double,
                CAST(NULL AS date)       AS null_date,
                CAST(NULL AS varbinary)  AS null_varbinary,
                CAST(NULL AS uuid)       AS null_uuid,
                1                        AS non_null_after";

            using var tc = new TrinoConnection(MakeProps());
            using var cmd = new TrinoCommand(tc, sql);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) throw new Exception("No row");

            string[] nullCols = { "null_bigint", "null_varchar", "null_bool", "null_double", "null_date", "null_varbinary", "null_uuid" };
            for (int i = 0; i < nullCols.Length; i++)
            {
                if (!r.IsDBNull(i))
                    throw new Exception($"Expected NULL for {nullCols[i]} but got {r.GetValue(i)}");
            }

            var nonNull = r.GetValue(7);
            if (nonNull == null || (int)nonNull != 1)
                throw new Exception($"non_null_after mismatch: {nonNull}");

            Console.Write($"\n    All {nullCols.Length} null columns confirmed NULL, non_null={nonNull}");
        }
    }
}
