using System.Text.Json;
using Trino.Platform.Samples.Http;

namespace Trino.Platform.Samples.Tests;

/// <summary>
/// TrinoHttpModels JSON 역직렬화 단위 테스트
/// </summary>
[TestClass]
public class TrinoHttpModelsTests
{
    [TestMethod]
    public void TrinoQueryResult_DeserializeBasicResponse()
    {
        // Arrange
        var json = """
        {
            "id": "20240101_123456_00001_abcde",
            "infoUri": "http://localhost:8080/ui/query/20240101_123456_00001_abcde",
            "nextUri": "http://localhost:8080/v1/statement/executing/20240101_123456_00001_abcde/y123",
            "columns": [
                {"name": "custkey", "type": "bigint"},
                {"name": "name", "type": "varchar(25)"}
            ],
            "data": [
                [1, "Customer#000000001"],
                [2, "Customer#000000002"]
            ],
            "stats": {
                "state": "RUNNING",
                "queued": false,
                "scheduled": true,
                "nodes": 1,
                "totalSplits": 10,
                "completedSplits": 5,
                "processedRows": 2,
                "processedBytes": 100
            }
        }
        """;

        // Act
        var result = JsonSerializer.Deserialize<TrinoQueryResult>(json);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual("20240101_123456_00001_abcde", result.Id);
        Assert.IsTrue(result.HasNextPage);
        Assert.AreEqual(2, result.Columns?.Count);
        Assert.AreEqual("custkey", result.Columns?[0].Name);
        Assert.AreEqual("bigint", result.Columns?[0].Type);
        Assert.IsTrue(result.HasData);
        Assert.AreEqual(2, result.Data?.Count);
        Assert.AreEqual("RUNNING", result.Stats?.State);
        Assert.AreEqual(2, result.Stats?.ProcessedRows);
        Assert.IsFalse(result.HasError);
    }

    [TestMethod]
    public void TrinoQueryResult_DeserializeWithError()
    {
        // Arrange
        var json = """
        {
            "id": "20240101_123456_00002_xyz",
            "stats": {
                "state": "FAILED"
            },
            "error": {
                "message": "Table 'tpch.tiny.nonexistent' does not exist",
                "errorCode": 1,
                "errorName": "TABLE_NOT_FOUND",
                "errorType": "USER_ERROR"
            }
        }
        """;

        // Act
        var result = JsonSerializer.Deserialize<TrinoQueryResult>(json);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(result.HasError);
        Assert.IsFalse(result.HasNextPage);
        Assert.IsFalse(result.HasData);
        Assert.AreEqual("TABLE_NOT_FOUND", result.Error?.ErrorName);
        Assert.AreEqual("USER_ERROR", result.Error?.ErrorType);
        Assert.AreEqual(1, result.Error?.ErrorCode);
    }

    [TestMethod]
    public void TrinoQueryResult_DeserializeEmptyData()
    {
        // Arrange
        var json = """
        {
            "id": "query_id",
            "columns": [{"name": "col1", "type": "integer"}],
            "stats": {"state": "FINISHED"}
        }
        """;

        // Act
        var result = JsonSerializer.Deserialize<TrinoQueryResult>(json);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsFalse(result.HasData);
        Assert.IsFalse(result.HasNextPage);
        Assert.IsNotNull(result.Columns);
        Assert.AreEqual(1, result.Columns.Count);
    }

    [TestMethod]
    public void TrinoQueryResult_HasNextPage_TrueWhenNextUriExists()
    {
        // Arrange
        var result = new TrinoQueryResult
        {
            NextUri = "http://localhost:8080/v1/statement/next"
        };

        // Assert
        Assert.IsTrue(result.HasNextPage);
    }

    [TestMethod]
    public void TrinoQueryResult_HasNextPage_FalseWhenNextUriNull()
    {
        // Arrange
        var result = new TrinoQueryResult { NextUri = null };

        // Assert
        Assert.IsFalse(result.HasNextPage);
    }

    [TestMethod]
    public void TrinoQueryResult_HasNextPage_FalseWhenNextUriEmpty()
    {
        // Arrange
        var result = new TrinoQueryResult { NextUri = "" };

        // Assert
        Assert.IsFalse(result.HasNextPage);
    }

    [TestMethod]
    public void TrinoHttpQueryResponse_Success_WhenNoError()
    {
        // Arrange
        var response = new TrinoHttpQueryResponse
        {
            QueryId = "test_query",
            Columns = new List<TrinoColumnInfo>
            {
                new() { Name = "col1", Type = "varchar" }
            },
            Rows = new List<List<object?>>
            {
                new() { "value1" }
            },
            Error = null
        };

        // Assert
        Assert.IsTrue(response.Success);
    }

    [TestMethod]
    public void TrinoHttpQueryResponse_NotSuccess_WhenError()
    {
        // Arrange
        var response = new TrinoHttpQueryResponse
        {
            Error = new TrinoQueryError
            {
                Message = "Some error",
                ErrorCode = 1,
                ErrorName = "TEST_ERROR",
                ErrorType = "USER_ERROR"
            }
        };

        // Assert
        Assert.IsFalse(response.Success);
    }

    [TestMethod]
    public void TrinoColumnInfo_DeserializeAllTypes()
    {
        // Arrange - 다양한 Trino 데이터 타입 테스트
        var json = """
        [
            {"name": "bool_col", "type": "boolean"},
            {"name": "int_col", "type": "integer"},
            {"name": "bigint_col", "type": "bigint"},
            {"name": "double_col", "type": "double"},
            {"name": "varchar_col", "type": "varchar(100)"},
            {"name": "date_col", "type": "date"},
            {"name": "timestamp_col", "type": "timestamp(3)"},
            {"name": "array_col", "type": "array(varchar)"},
            {"name": "map_col", "type": "map(varchar, integer)"}
        ]
        """;

        // Act
        var columns = JsonSerializer.Deserialize<List<TrinoColumnInfo>>(json);

        // Assert
        Assert.IsNotNull(columns);
        Assert.AreEqual(9, columns.Count);
        Assert.AreEqual("bool_col", columns[0].Name);
        Assert.AreEqual("boolean", columns[0].Type);
        Assert.AreEqual("timestamp(3)", columns[6].Type);
        Assert.AreEqual("map(varchar, integer)", columns[8].Type);
    }

    [TestMethod]
    public void TrinoQueryStats_DeserializeAllFields()
    {
        // Arrange
        var json = """
        {
            "state": "FINISHED",
            "queued": false,
            "scheduled": true,
            "nodes": 3,
            "totalSplits": 100,
            "completedSplits": 100,
            "processedRows": 1000000,
            "processedBytes": 50000000,
            "elapsedTimeMillis": 5000,
            "progressPercentage": 100.0
        }
        """;

        // Act
        var stats = JsonSerializer.Deserialize<TrinoQueryStats>(json);

        // Assert
        Assert.IsNotNull(stats);
        Assert.AreEqual("FINISHED", stats.State);
        Assert.IsFalse(stats.Queued);
        Assert.IsTrue(stats.Scheduled);
        Assert.AreEqual(3, stats.Nodes);
        Assert.AreEqual(100, stats.TotalSplits);
        Assert.AreEqual(100, stats.CompletedSplits);
        Assert.AreEqual(1000000, stats.ProcessedRows);
        Assert.AreEqual(50000000, stats.ProcessedBytes);
        Assert.AreEqual(5000, stats.ElapsedTimeMillis);
        Assert.AreEqual(100.0, stats.ProgressPercentage);
    }

    [TestMethod]
    public void TrinoQueryError_DeserializeWithAllFields()
    {
        // Arrange
        var json = """
        {
            "message": "line 1:15: Table 'catalog.schema.table' does not exist",
            "errorCode": 1,
            "errorName": "TABLE_NOT_FOUND",
            "errorType": "USER_ERROR"
        }
        """;

        // Act
        var error = JsonSerializer.Deserialize<TrinoQueryError>(json);

        // Assert
        Assert.IsNotNull(error);
        Assert.IsTrue(error.Message.Contains("does not exist"));
        Assert.AreEqual(1, error.ErrorCode);
        Assert.AreEqual("TABLE_NOT_FOUND", error.ErrorName);
        Assert.AreEqual("USER_ERROR", error.ErrorType);
    }

    [TestMethod]
    public void TrinoQueryResult_DataWithNullValues()
    {
        // Arrange
        var json = """
        {
            "id": "query_id",
            "columns": [
                {"name": "nullable_col", "type": "varchar"}
            ],
            "data": [
                [null],
                ["value"],
                [null]
            ]
        }
        """;

        // Act
        var result = JsonSerializer.Deserialize<TrinoQueryResult>(json);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(result.HasData);
        Assert.AreEqual(3, result.Data?.Count);
        Assert.IsNull(result.Data?[0][0]);
        Assert.AreEqual("value", result.Data?[1][0]?.ToString());
        Assert.IsNull(result.Data?[2][0]);
    }
}
