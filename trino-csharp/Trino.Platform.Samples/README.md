# Trino C# Client - Platform Samples

Trino C# Client 라이브러리([trinodb/trino-csharp-client](https://github.com/trinodb/trino-csharp-client))를 활용한 플랫폼 사용자용 샘플 프로젝트.

## 테스트 환경

| 항목 | 값 |
|---|---|
| Trino Server | v479 (Docker `trinodb/trino:latest`) |
| C# Client | trinodb/trino-csharp-client (netstandard2.0) |
| Runtime | .NET 8.0.417 |
| OS | macOS (Darwin 25.2.0, ARM64) |
| 접속 | HTTP localhost:8080, 인증 없음 |

## 전체 테스트 결과

| # | 예제 | 결과 | 검증 내용 |
|---|---|---|---|
| 1 | ADO.NET 기본 쿼리 | **PASS** | customer 10건 SELECT |
| 2 | Connection String 접속 | **PASS** | 문자열 기반 접속, nation 5건 조회 |
| 3 | SDK 직접 사용 | **PASS** | RecordExecutor 스트리밍, region 5건 |
| 4 | 인증 방식별 예제 | **PASS** | 6가지 인증 패턴 코드 출력 |
| 5 | 세션 관리 | **PASS** | SET SESSION, USE, sf1 customer 15만건 카운트 |
| 6 | 파라미터 쿼리 | **PASS** | positional `?` 파라미터, 5건 필터 조회 |
| 7 | DataTable 변환 | **PASS** | BuildDataTableAsync + LINQ 필터링 |
| 8 | 스키마/메타데이터 조회 | **PASS** | GetSchema 8개 테이블, 61개 컬럼, SHOW TABLES |
| 9 | 병렬 쿼리 | **PASS** | 5스레드 동시 실행, 평균 274ms |
| 10 | 대용량 스트리밍 | **PASS** | 600만건 스트리밍, 285K rows/sec |

## 대용량 스트리밍 성능

Docker 단일 노드(1 worker) 환경 기준.

| 데이터셋 | 컬럼 | 행 수 | 첫 행 수신 | 전체 소요 | 처리 속도 |
|---|---|---|---|---|---|
| tpch.tiny.customer | 8 | 1,500 | 1,638ms | 2.20s | 682 rows/sec |
| tpch.sf1.customer | 8 | 150,000 | 197ms | 1.05s | 142,683 rows/sec |
| tpch.sf1.lineitem | 16 | 6,001,215 | 182ms | 21.05s | 285,074 rows/sec |

> tiny에서 첫 행 수신이 느린 것은 JVM 웜업에 의한 것이며, 이후 쿼리에서는 182~197ms로 안정화됨.

## 프로젝트 구조

```
Trino.Platform.Samples/
├── Trino.Platform.Samples.csproj   # .NET 8.0 콘솔 프로젝트
├── appsettings.json                # Trino 접속 설정
├── Program.cs                      # 메뉴 기반 진입점
├── TestRunner.cs                   # 비대화형 전체 테스트 실행기
└── Examples/
    ├── BasicQueryExample.cs        #  1. ADO.NET 기본 쿼리
    ├── ConnectionStringExample.cs  #  2. Connection String 접속
    ├── SdkDirectExample.cs         #  3. SDK 직접 사용 (RecordExecutor)
    ├── AuthenticationExample.cs    #  4. 인증 방식별 예제
    ├── SessionManageExample.cs     #  5. 세션 관리
    ├── ParameterQueryExample.cs    #  6. 파라미터 쿼리
    ├── DataTableExample.cs         #  7. DataTable 변환
    ├── SchemaInfoExample.cs        #  8. 스키마/메타데이터 조회
    ├── ParallelQueryExample.cs     #  9. 병렬 쿼리
    └── LargeDataStreamExample.cs   # 10. 대용량 스트리밍
```

## 의존성

| 패키지 | 버전 | 용도 |
|---|---|---|
| Trino.Client | netstandard2.0 | 코어 클라이언트 (ProjectReference) |
| Trino.Data.ADO | netstandard2.0 | ADO.NET 래퍼 (ProjectReference) |
| Trino.Client.Auth | netstandard2.0 | 인증 모듈 (ProjectReference) |
| Newtonsoft.Json | 13.0.1 | JSON 처리 (Trino.Client 의존) |
| Microsoft.Extensions.Configuration.Json | 8.0.1 | appsettings.json 로딩 |

## 빠른 시작

### 1. Trino 서버 실행

```bash
docker run -d --name trino -p 8080:8080 trinodb/trino:latest
```

### 2. 접속 설정

`appsettings.json`을 환경에 맞게 수정:

```json
{
  "Trino": {
    "Host": "localhost",
    "Port": 8080,
    "Catalog": "tpch",
    "Schema": "tiny",
    "EnableSsl": false,
    "User": "trino-user",
    "Auth": {
      "Type": "None"
    }
  }
}
```

### 3. 빌드 및 실행

```bash
# 대화형 메뉴 모드
dotnet run --project Trino.Platform.Samples.csproj

# 비대화형 전체 테스트
dotnet run --project Trino.Platform.Samples.csproj -- --test
```

## 사용 패턴별 코드 예제

### 1. ADO.NET 기본 쿼리

가장 일반적인 사용 방식. SQL Server, MySQL 등 다른 DB와 동일한 ADO.NET 패턴.

```csharp
var properties = new TrinoConnectionProperties
{
    Catalog = "tpch",
    Server = new Uri("http://localhost:8080/"),
};

using var connection = new TrinoConnection(properties);
using var command = new TrinoCommand(connection,
    "SELECT custkey, name FROM tpch.tiny.customer LIMIT 10");
using var reader = command.ExecuteReader();

while (reader.Read())
{
    Console.WriteLine($"{reader.GetName(0)}: {reader.GetValue(0)}");
}
```

### 2. Connection String 접속

```csharp
using var connection = new TrinoConnection();
connection.ConnectionString =
    "host=localhost;port=8080;catalog=tpch;schema=tiny;user=trino-user;enableSsl=False";

using var command = new TrinoCommand(connection, "SELECT * FROM nation LIMIT 5");
using var reader = command.ExecuteReader();
```

### 3. SDK 직접 사용 (RecordExecutor)

ADO.NET 없이 직접 Trino 프로토콜에 접근. 비동기 스트리밍 처리.

```csharp
var session = properties.GetSession();

var records = await RecordExecutor.Execute(
    session: session,
    statement: "SELECT * FROM tpch.tiny.region")
    .ConfigureAwait(false);

foreach (var row in records)
{
    Console.WriteLine(string.Join(" | ", row));
}
```

### 4. 파라미터 쿼리

Trino는 `?` (positional) 파라미터만 지원. `@param` 형식은 사용 불가.

```csharp
using var command = new TrinoCommand(connection,
    "SELECT * FROM customer WHERE nationkey = ? LIMIT 5");

command.Parameters.Add(new TrinoParameter { ParameterName = "p1", Value = 10 });
using var reader = command.ExecuteReader();
```

### 5. 세션 관리

```csharp
using var connection = new TrinoConnection(properties);

// SQL 명령으로 세션 설정
using (var cmd = new TrinoCommand(connection, "SET SESSION query_max_run_time = '10m'"))
    cmd.ExecuteNonQuery();

// USE로 카탈로그/스키마 변경
using (var cmd = new TrinoCommand(connection, "USE tpch.sf1"))
    cmd.ExecuteNonQuery();

// 프로퍼티 직접 접근
connection.ConnectionSession.Properties.Source = "MyApp";
```

### 6. DataTable 변환

```csharp
var session = properties.GetSession();
var records = await RecordExecutor.Execute(session: session,
    statement: "SELECT * FROM tpch.tiny.region").ConfigureAwait(false);

DataTable dt = await records.BuildDataTableAsync().ConfigureAwait(false);

// LINQ 활용
var filtered = dt.AsEnumerable()
    .Where(r => r.Field<string>("name").Contains("A"));
```

### 7. 대용량 스트리밍

```csharp
using var command = new TrinoCommand(connection,
    "SELECT * FROM tpch.sf1.lineitem");

// 버퍼 크기 지정 (100MB) - row-by-row 스트리밍, 메모리 효율적
using var reader = command.ExecuteReader(1024 * 1024 * 100);

while (reader.Read())
{
    // 600만건도 메모리 부담 없이 처리
}
```

## 인증 방식

| 방식 | appsettings Auth:Type | 코드 |
|---|---|---|
| 인증 없음 | `None` | Auth 설정 생략 |
| JWT Bearer | `jwt` | `new TrinoJWTAuth { AccessToken = "..." }` |
| Basic (ID/PW) | `basic` | `new BasicAuth { User = "...", Password = "..." }` |
| OAuth 2.0 | `oauth` | `new TrinoOauthClientSecretAuth(endpoint, id, secret, scope)` |
| Azure Default | `azure` | `new TrinoAzureDefaultAuth(scope: "...")` |
| 커스텀 | - | `ITrinoAuth` 인터페이스 구현 |

## 주요 Connection Properties

| 속성 | 설명 | 기본값 |
|---|---|---|
| `Server` / `Host` | Trino 서버 주소 | - |
| `Port` | 포트 | 443 |
| `Catalog` | 기본 카탈로그 | - |
| `Schema` | 기본 스키마 | - |
| `EnableSsl` | HTTPS 사용 | true |
| `User` | 접속 사용자 | - |
| `Auth` | 인증 객체 (ITrinoAuth) | null |
| `TestConnection` | Open() 시 접속 테스트 | false |
| `CompressionDisabled` | 압축 비활성화 | false |
| `SessionProperties` | 세션 프로퍼티 초기값 | - |

## 배포 시 필요한 DLL

플랫폼에 라이브러리로 통합할 때 최소 필요 파일:

```
Trino.Client.dll          (92 KB)  -- 필수 (코어)
Trino.Data.ADO.dll        (52 KB)  -- ADO.NET 사용 시
Trino.Client.Auth.dll     (9.5 KB) -- Azure/OAuth 인증 사용 시
Newtonsoft.Json.dll                -- 필수 의존성
```

## 주의사항

- Trino 파라미터 쿼리는 `?` (positional) 방식만 지원, `@param` 형식 사용 불가
- `TrinoConnection`은 HTTP 기반으로 매 쿼리마다 새 연결 생성 (커넥션 풀 불필요)
- 대용량 데이터 처리 시 `ExecuteReader(bufferSizeBytes)`로 버퍼 크기 조절 권장
- SSL 인증서 관련: `AllowSelfSignedServerCert`, `AllowHostNameCNMismatch` 옵션 제공
