using Trino.Platform.Samples.Http;

namespace Trino.Platform.Samples.Tests;

/// <summary>
/// TrinoHttpClient 통합 테스트 - 실제 Trino 서버 필요
/// 테스트 실행 전: docker run -d --name trino -p 8080:8080 trinodb/trino:latest
/// </summary>
[TestClass]
public class TrinoHttpClientIntegrationTests
{
    private const string Host = "localhost";
    private const int Port = 8081;

    private static bool _isTrinoAvailable;

    [ClassInitialize]
    public static async Task ClassInit(TestContext context)
    {
        // Trino 서버 가용성 체크
        try
        {
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var response = await httpClient.GetAsync($"http://{Host}:{Port}/v1/info");
            _isTrinoAvailable = response.IsSuccessStatusCode;
        }
        catch
        {
            _isTrinoAvailable = false;
        }

        if (!_isTrinoAvailable)
        {
            Console.WriteLine("⚠️ Trino 서버 미가용 - 통합 테스트 건너뜀");
            Console.WriteLine("   테스트 실행: docker run -d --name trino -p 8080:8080 trinodb/trino:latest");
        }
    }

    private void SkipIfTrinoUnavailable()
    {
        if (!_isTrinoAvailable)
        {
            Assert.Inconclusive("Trino 서버가 실행 중이지 않습니다.");
        }
    }

    [TestMethod]
    public async Task ExecuteQueryAsync_BasicSelect_ReturnsData()
    {
        SkipIfTrinoUnavailable();

        // Arrange
        using var client = new TrinoHttpClient(Host, Port);

        // Act
        var result = await client.ExecuteQueryAsync(
            "SELECT custkey, name FROM tpch.tiny.customer LIMIT 5");

        // Assert
        Assert.IsTrue(result.Success, $"쿼리 실패: {result.Error?.Message}");
        Assert.AreEqual(2, result.Columns.Count);
        Assert.AreEqual("custkey", result.Columns[0].Name);
        Assert.AreEqual("name", result.Columns[1].Name);
        Assert.AreEqual(5, result.Rows.Count);
        Assert.IsFalse(string.IsNullOrEmpty(result.QueryId));
    }

    [TestMethod]
    public async Task ExecuteQueryAsync_SelectOne_SingleRow()
    {
        SkipIfTrinoUnavailable();

        // Arrange
        using var client = new TrinoHttpClient(Host, Port);

        // Act
        var result = await client.ExecuteQueryAsync("SELECT 1 AS num, 'hello' AS msg");

        // Assert
        Assert.IsTrue(result.Success);
        Assert.AreEqual(2, result.Columns.Count);
        Assert.AreEqual(1, result.Rows.Count);
        Assert.AreEqual(1L, Convert.ToInt64(result.Rows[0][0]));
        Assert.AreEqual("hello", result.Rows[0][1]?.ToString());
    }

    [TestMethod]
    public async Task ExecuteQueryAsync_InvalidSql_ReturnsError()
    {
        SkipIfTrinoUnavailable();

        // Arrange
        using var client = new TrinoHttpClient(Host, Port);

        // Act
        var result = await client.ExecuteQueryAsync(
            "SELECT * FROM nonexistent_catalog.nonexistent_schema.nonexistent_table");

        // Assert
        Assert.IsFalse(result.Success);
        Assert.IsNotNull(result.Error);
        Assert.IsFalse(string.IsNullOrEmpty(result.Error.Message));
        Assert.IsFalse(string.IsNullOrEmpty(result.Error.ErrorName));
    }

    [TestMethod]
    public async Task ExecuteQueryAsync_SyntaxError_ReturnsError()
    {
        SkipIfTrinoUnavailable();

        // Arrange
        using var client = new TrinoHttpClient(Host, Port);

        // Act
        var result = await client.ExecuteQueryAsync("SELECTT * FROM tpch.tiny.customer");

        // Assert
        Assert.IsFalse(result.Success);
        Assert.IsNotNull(result.Error);
        Assert.IsTrue(result.Error.ErrorType.Contains("ERROR") ||
                      result.Error.ErrorName.Contains("SYNTAX"));
    }

    [TestMethod]
    public async Task ExecuteQueryAsync_EmptyResult_Success()
    {
        SkipIfTrinoUnavailable();

        // Arrange
        using var client = new TrinoHttpClient(Host, Port);

        // Act
        var result = await client.ExecuteQueryAsync(
            "SELECT * FROM tpch.tiny.customer WHERE custkey = -999");

        // Assert
        Assert.IsTrue(result.Success);
        Assert.AreEqual(0, result.Rows.Count);
        Assert.IsTrue(result.Columns.Count > 0); // 컬럼 정보는 있음
    }

    [TestMethod]
    public async Task ExecuteQueryStreamingAsync_LargeDataset_StreamsPages()
    {
        SkipIfTrinoUnavailable();

        // Arrange
        using var client = new TrinoHttpClient(Host, Port);

        // Act
        int totalRows = 0;
        int pageCount = 0;
        List<TrinoColumnInfo>? columns = null;

        await foreach (var page in client.ExecuteQueryStreamingAsync(
            "SELECT * FROM tpch.sf1.customer"))
        {
            pageCount++;

            if (page.HasError)
            {
                Assert.Fail($"스트리밍 오류: {page.Error?.Message}");
            }

            if (page.Columns != null && columns == null)
            {
                columns = page.Columns;
            }

            if (page.HasData)
            {
                totalRows += page.Data!.Count;
            }
        }

        // Assert
        Assert.IsTrue(totalRows >= 150000, $"sf1.customer는 150,000건 이상이어야 함: {totalRows}");
        Assert.IsTrue(pageCount >= 2, $"여러 페이지로 스트리밍되어야 함: {pageCount}");
        Assert.IsNotNull(columns);
        Assert.IsTrue(columns.Count > 0);
    }

    [TestMethod]
    public async Task ExecuteQueryAsync_AllDataTypes_ParsesCorrectly()
    {
        SkipIfTrinoUnavailable();

        // Arrange
        using var client = new TrinoHttpClient(Host, Port);
        var sql = @"
            SELECT
                CAST(1 AS BOOLEAN) AS bool_col,
                CAST(42 AS INTEGER) AS int_col,
                CAST(9223372036854775807 AS BIGINT) AS bigint_col,
                CAST(3.14159 AS DOUBLE) AS double_col,
                'hello world' AS varchar_col,
                DATE '2024-01-15' AS date_col,
                TIMESTAMP '2024-01-15 10:30:00' AS timestamp_col
        ";

        // Act
        var result = await client.ExecuteQueryAsync(sql);

        // Assert
        Assert.IsTrue(result.Success, $"쿼리 실패: {result.Error?.Message}");
        Assert.AreEqual(7, result.Columns.Count);
        Assert.AreEqual(1, result.Rows.Count);

        // 컬럼 타입 확인
        Assert.AreEqual("boolean", result.Columns[0].Type);
        Assert.AreEqual("integer", result.Columns[1].Type);
        Assert.AreEqual("bigint", result.Columns[2].Type);
        Assert.AreEqual("double", result.Columns[3].Type);
    }

    [TestMethod]
    public async Task ExecuteQueryAsync_NullValues_HandledCorrectly()
    {
        SkipIfTrinoUnavailable();

        // Arrange
        using var client = new TrinoHttpClient(Host, Port);
        var sql = "SELECT NULL AS null_col, 'not null' AS not_null_col";

        // Act
        var result = await client.ExecuteQueryAsync(sql);

        // Assert
        Assert.IsTrue(result.Success);
        Assert.AreEqual(1, result.Rows.Count);
        Assert.IsNull(result.Rows[0][0]);
        Assert.AreEqual("not null", result.Rows[0][1]?.ToString());
    }

    [TestMethod]
    public async Task ExecuteQueryAsync_ShowTables_ReturnsMetadata()
    {
        SkipIfTrinoUnavailable();

        // Arrange
        using var client = new TrinoHttpClient(Host, Port, catalog: "tpch", schema: "tiny");

        // Act
        var result = await client.ExecuteQueryAsync("SHOW TABLES");

        // Assert
        Assert.IsTrue(result.Success);
        Assert.IsTrue(result.Rows.Count > 0, "tpch.tiny에는 테이블이 있어야 함");

        var tableNames = result.Rows.Select(r => r[0]?.ToString()).ToList();
        Assert.IsTrue(tableNames.Contains("customer"), "customer 테이블이 있어야 함");
        Assert.IsTrue(tableNames.Contains("orders"), "orders 테이블이 있어야 함");
    }

    [TestMethod]
    public async Task ExecuteQueryAsync_DescribeTable_ReturnsSchema()
    {
        SkipIfTrinoUnavailable();

        // Arrange
        using var client = new TrinoHttpClient(Host, Port);

        // Act
        var result = await client.ExecuteQueryAsync("DESCRIBE tpch.tiny.customer");

        // Assert
        Assert.IsTrue(result.Success, $"실패: {result.Error?.Message}");
        Assert.IsTrue(result.Rows.Count > 0);

        var columnNames = result.Rows.Select(r => r[0]?.ToString()).ToList();
        Assert.IsTrue(columnNames.Contains("custkey"), "custkey 컬럼이 있어야 함");
        Assert.IsTrue(columnNames.Contains("name"), "name 컬럼이 있어야 함");
    }

    [TestMethod]
    public async Task SubmitQueryAsync_ReturnsQueryId()
    {
        SkipIfTrinoUnavailable();

        // Arrange
        using var client = new TrinoHttpClient(Host, Port);

        // Act
        var result = await client.SubmitQueryAsync("SELECT 1");

        // Assert
        Assert.IsNotNull(result.Id);
        Assert.IsTrue(result.Id.Length > 0);
        Assert.IsNotNull(result.InfoUri);
    }

    [TestMethod]
    public async Task ExecuteQueryAsync_WithCancellation_Cancels()
    {
        SkipIfTrinoUnavailable();

        // Arrange
        using var client = new TrinoHttpClient(Host, Port);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        // Act & Assert
        // TaskCanceledException is a subtype of OperationCanceledException; MSTest 3.x requires exact type.
        await Assert.ThrowsExceptionAsync<TaskCanceledException>(async () =>
        {
            // 대용량 쿼리 실행 중 취소
            await client.ExecuteQueryAsync(
                "SELECT * FROM tpch.sf1.lineitem",
                cts.Token);
        });
    }

    [TestMethod]
    public async Task ExecuteQueryAsync_MultipleQueries_Sequential()
    {
        SkipIfTrinoUnavailable();

        // Arrange
        using var client = new TrinoHttpClient(Host, Port);

        // Act
        var result1 = await client.ExecuteQueryAsync("SELECT 1 AS num");
        var result2 = await client.ExecuteQueryAsync("SELECT 2 AS num");
        var result3 = await client.ExecuteQueryAsync("SELECT 3 AS num");

        // Assert
        Assert.IsTrue(result1.Success);
        Assert.IsTrue(result2.Success);
        Assert.IsTrue(result3.Success);
        Assert.AreEqual(1L, Convert.ToInt64(result1.Rows[0][0]));
        Assert.AreEqual(2L, Convert.ToInt64(result2.Rows[0][0]));
        Assert.AreEqual(3L, Convert.ToInt64(result3.Rows[0][0]));
    }

    [TestMethod]
    public async Task ExecuteQueryAsync_AggregateQuery_ReturnsResult()
    {
        SkipIfTrinoUnavailable();

        // Arrange
        using var client = new TrinoHttpClient(Host, Port);

        // Act
        var result = await client.ExecuteQueryAsync(
            "SELECT COUNT(*) AS cnt FROM tpch.tiny.customer");

        // Assert
        Assert.IsTrue(result.Success);
        Assert.AreEqual(1, result.Rows.Count);
        var count = Convert.ToInt64(result.Rows[0][0]);
        Assert.IsTrue(count > 0, "고객 수는 0보다 커야 함");
    }

    [TestMethod]
    public async Task ExecuteQueryAsync_JoinQuery_ReturnsResult()
    {
        SkipIfTrinoUnavailable();

        // Arrange
        using var client = new TrinoHttpClient(Host, Port);

        // Act
        var result = await client.ExecuteQueryAsync(@"
            SELECT c.custkey, c.name, n.name AS nation_name
            FROM tpch.tiny.customer c
            JOIN tpch.tiny.nation n ON c.nationkey = n.nationkey
            LIMIT 5
        ");

        // Assert
        Assert.IsTrue(result.Success, $"실패: {result.Error?.Message}");
        Assert.AreEqual(3, result.Columns.Count);
        Assert.AreEqual(5, result.Rows.Count);
    }

    [TestMethod]
    public async Task ExecuteQueryAsync_StatsProvided()
    {
        SkipIfTrinoUnavailable();

        // Arrange
        using var client = new TrinoHttpClient(Host, Port);

        // Act
        var result = await client.ExecuteQueryAsync(
            "SELECT * FROM tpch.tiny.region");

        // Assert
        Assert.IsTrue(result.Success);
        Assert.IsNotNull(result.Stats);
        Assert.AreEqual("FINISHED", result.Stats.State);
        Assert.IsTrue(result.Stats.ProcessedRows >= 0);
    }
}
