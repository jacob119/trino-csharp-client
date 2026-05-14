using System.Text.Json;
using System.Text.Json.Serialization;

namespace Trino.Platform.Samples.Http;

/// <summary>
/// Converts JSON primitives to native CLR types instead of JsonElement.
/// </summary>
public class ObjectAsPrimitiveConverter : JsonConverter<object>
{
    public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.True => true,
            JsonTokenType.False => false,
            JsonTokenType.Null => null,
            JsonTokenType.Number when reader.TryGetInt64(out long l) => l,
            JsonTokenType.Number => reader.GetDouble(),
            JsonTokenType.String => reader.GetString(),
            _ => JsonDocument.ParseValue(ref reader).RootElement.Clone()
        };

    public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value, value?.GetType() ?? typeof(object), options);
}

/// <summary>
/// Trino REST API 응답 모델 - 순수 HTTP 접속용
/// </summary>
public class TrinoQueryResult
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("infoUri")]
    public string? InfoUri { get; set; }

    [JsonPropertyName("nextUri")]
    public string? NextUri { get; set; }

    [JsonPropertyName("columns")]
    public List<TrinoColumnInfo>? Columns { get; set; }

    [JsonPropertyName("data")]
    public List<List<object?>>? Data { get; set; }

    [JsonPropertyName("stats")]
    public TrinoQueryStats? Stats { get; set; }

    [JsonPropertyName("error")]
    public TrinoQueryError? Error { get; set; }

    public bool HasNextPage => !string.IsNullOrEmpty(NextUri);
    public bool HasData => Data != null && Data.Count > 0;
    public bool HasError => Error != null;
}

public class TrinoColumnInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";
}

public class TrinoQueryStats
{
    [JsonPropertyName("state")]
    public string State { get; set; } = "";

    [JsonPropertyName("queued")]
    public bool Queued { get; set; }

    [JsonPropertyName("scheduled")]
    public bool Scheduled { get; set; }

    [JsonPropertyName("nodes")]
    public int Nodes { get; set; }

    [JsonPropertyName("totalSplits")]
    public int TotalSplits { get; set; }

    [JsonPropertyName("completedSplits")]
    public int CompletedSplits { get; set; }

    [JsonPropertyName("processedRows")]
    public long ProcessedRows { get; set; }

    [JsonPropertyName("processedBytes")]
    public long ProcessedBytes { get; set; }

    [JsonPropertyName("elapsedTimeMillis")]
    public long ElapsedTimeMillis { get; set; }

    [JsonPropertyName("progressPercentage")]
    public double ProgressPercentage { get; set; }
}

public class TrinoQueryError
{
    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("errorCode")]
    public int ErrorCode { get; set; }

    [JsonPropertyName("errorName")]
    public string ErrorName { get; set; } = "";

    [JsonPropertyName("errorType")]
    public string ErrorType { get; set; } = "";
}

/// <summary>
/// 전체 쿼리 실행 결과를 담는 컨테이너
/// </summary>
public class TrinoHttpQueryResponse
{
    public string QueryId { get; set; } = "";
    public List<TrinoColumnInfo> Columns { get; set; } = new();
    public List<List<object?>> Rows { get; set; } = new();
    public TrinoQueryStats? Stats { get; set; }
    public TrinoQueryError? Error { get; set; }
    public int TotalPages { get; set; }
    public bool Success => Error == null;
}
