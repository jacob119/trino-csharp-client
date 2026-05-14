using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Trino.Platform.Samples.Http;

/// <summary>
/// 순수 HttpClient 기반 Trino REST API 클라이언트.
/// Trino C# Client 라이브러리 없이 HTTP만으로 Trino에 접속.
/// </summary>
public class TrinoHttpClient : IDisposable
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        Converters = { new ObjectAsPrimitiveConverter() }
    };

    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly string _user;
    private readonly string _catalog;
    private readonly string _schema;

    /// <summary>
    /// Trino HTTP 클라이언트 생성
    /// </summary>
    /// <param name="host">Trino 서버 호스트 (예: "localhost")</param>
    /// <param name="port">포트 (기본 8080)</param>
    /// <param name="user">사용자명</param>
    /// <param name="catalog">기본 카탈로그</param>
    /// <param name="schema">기본 스키마</param>
    /// <param name="useSsl">HTTPS 사용 여부</param>
    public TrinoHttpClient(
        string host = "localhost",
        int port = 8080,
        string user = "trino-user",
        string catalog = "tpch",
        string schema = "tiny",
        bool useSsl = false)
    {
        var scheme = useSsl ? "https" : "http";
        _baseUrl = $"{scheme}://{host}:{port}";
        _user = user;
        _catalog = catalog;
        _schema = schema;

        var handler = new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.GZip
                                   | System.Net.DecompressionMethods.Deflate
        };
        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(300)
        };
    }

    /// <summary>
    /// SQL 쿼리를 실행하고 전체 결과를 반환.
    /// 취소 시 서버 측 쿼리도 DELETE 요청으로 종료.
    /// </summary>
    public async Task<TrinoHttpQueryResponse> ExecuteQueryAsync(string sql, CancellationToken ct = default)
    {
        var response = new TrinoHttpQueryResponse();

        // 1단계: POST /v1/statement 로 쿼리 제출.
        // CancellationToken.None 사용 이유: ct가 POST 응답 수신 전에 취소되면
        // 서버는 쿼리를 이미 시작했지만 nextUri를 모르므로 서버 측 취소가 불가능해짐.
        // 응답 수신 후 ct 상태를 확인해 즉시 DELETE로 서버 쿼리를 종료한다.
        var firstResult = await SubmitQueryAsync(sql, CancellationToken.None).ConfigureAwait(false);

        // 제출 직후 취소 요청이 있으면 서버 쿼리를 즉시 취소
        if (ct.IsCancellationRequested && firstResult.NextUri != null)
        {
            await CancelQueryAsync(firstResult.NextUri).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
        }

        response.QueryId = firstResult.Id ?? "";
        response.Stats = firstResult.Stats;
        response.TotalPages = 1;

        if (firstResult.HasError)
        {
            response.Error = firstResult.Error;
            return response;
        }

        if (firstResult.Columns != null)
            response.Columns = firstResult.Columns;
        if (firstResult.HasData)
            response.Rows.AddRange(firstResult.Data!);

        // 2단계: nextUri 폴링으로 나머지 데이터 수신
        var currentResult = firstResult;
        try
        {
            while (currentResult.HasNextPage)
            {
                ct.ThrowIfCancellationRequested();
                currentResult = await PollNextAsync(currentResult.NextUri!, ct).ConfigureAwait(false);
                response.TotalPages++;

                if (currentResult.HasError)
                {
                    response.Error = currentResult.Error;
                    break;
                }

                if (currentResult.Columns != null && response.Columns.Count == 0)
                    response.Columns = currentResult.Columns;
                if (currentResult.HasData)
                    response.Rows.AddRange(currentResult.Data!);

                response.Stats = currentResult.Stats;
            }
        }
        catch (OperationCanceledException)
        {
            // 서버 측 쿼리 취소: nextUri 에 DELETE 전송
            if (currentResult.NextUri != null)
                await CancelQueryAsync(currentResult.NextUri).ConfigureAwait(false);
            throw;
        }

        return response;
    }

    /// <summary>
    /// 실행 중인 쿼리를 서버 측에서 취소 (best-effort).
    /// </summary>
    public async Task CancelQueryAsync(string nextUri)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Delete, nextUri);
            AddTrinoHeaders(request);
            await _httpClient.SendAsync(request, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // best-effort: 서버가 응답 없어도 무시
        }
    }

    /// <summary>
    /// SQL 쿼리를 실행하고 페이지 단위로 스트리밍 (대용량용)
    /// </summary>
    public async IAsyncEnumerable<TrinoQueryResult> ExecuteQueryStreamingAsync(
        string sql,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var result = await SubmitQueryAsync(sql, CancellationToken.None).ConfigureAwait(false);
        if (ct.IsCancellationRequested && result.NextUri != null)
        {
            await CancelQueryAsync(result.NextUri).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
        }
        yield return result;

        while (result.HasNextPage && !result.HasError)
        {
            ct.ThrowIfCancellationRequested();
            result = await PollNextAsync(result.NextUri!, ct).ConfigureAwait(false);
            yield return result;
        }
    }

    /// <summary>
    /// 1단계: POST /v1/statement 로 쿼리 제출
    /// </summary>
    public async Task<TrinoQueryResult> SubmitQueryAsync(string sql, CancellationToken ct = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v1/statement")
        {
            Content = new StringContent(sql, Encoding.UTF8, "text/plain")
        };
        AddTrinoHeaders(request);

        var httpResponse = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        httpResponse.EnsureSuccessStatusCode();

        var json = await httpResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
        return JsonSerializer.Deserialize<TrinoQueryResult>(json, _jsonOptions)
            ?? throw new InvalidOperationException("Empty response from Trino");
    }

    /// <summary>
    /// 2단계: GET nextUri 로 다음 페이지 폴링
    /// </summary>
    public async Task<TrinoQueryResult> PollNextAsync(string nextUri, CancellationToken ct = default)
    {
        // 폴링 간 짧은 대기 (서버 부하 방지)
        await Task.Delay(50, ct).ConfigureAwait(false);

        var request = new HttpRequestMessage(HttpMethod.Get, nextUri);
        AddTrinoHeaders(request);

        var httpResponse = await SendWithRetryAsync(request, ct).ConfigureAwait(false);
        httpResponse.EnsureSuccessStatusCode();

        var json = await httpResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
        return JsonSerializer.Deserialize<TrinoQueryResult>(json, _jsonOptions)
            ?? throw new InvalidOperationException("Empty response from Trino");
    }

    /// <summary>
    /// Trino 필수 HTTP 헤더 추가
    /// </summary>
    private void AddTrinoHeaders(HttpRequestMessage request)
    {
        request.Headers.Add("X-Trino-User", _user);
        request.Headers.Add("X-Trino-Catalog", _catalog);
        request.Headers.Add("X-Trino-Schema", _schema);
        request.Headers.Add("X-Trino-Client-Capabilities", "PARAMETRIC_DATETIME");
        request.Headers.Add("X-Trino-Source", "Trino.Platform.Samples.Http");
        request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
    }

    /// <summary>
    /// 502/503/504 에러 시 재시도
    /// </summary>
    private async Task<HttpResponseMessage> SendWithRetryAsync(
        HttpRequestMessage request, CancellationToken ct, int maxRetries = 5)
    {
        int delay = 50;
        for (int i = 0; i < maxRetries; i++)
        {
            // 재시도 시 요청을 복제해야 함 (HttpRequestMessage는 한번만 전송 가능)
            HttpRequestMessage req;
            if (i == 0)
            {
                req = request;
            }
            else
            {
                req = new HttpRequestMessage(request.Method, request.RequestUri);
                foreach (var header in request.Headers)
                    req.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            var response = await _httpClient.SendAsync(req, ct).ConfigureAwait(false);
            var status = (int)response.StatusCode;

            if (status != 502 && status != 503 && status != 504)
                return response;

            await Task.Delay(delay, ct).ConfigureAwait(false);
            delay = Math.Min((int)(delay * 1.2), 5000);
        }

        // 마지막 시도
        var lastReq = new HttpRequestMessage(request.Method, request.RequestUri);
        foreach (var header in request.Headers)
            lastReq.Headers.TryAddWithoutValidation(header.Key, header.Value);
        return await _httpClient.SendAsync(lastReq, ct).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
