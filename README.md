# Car park occupancy predictions

**Target framework: `net10.0`.** This is an ASP.NET Core Web API on the .NET 10 SDK.

The API serves about 50 car parks. Each park has about a year of occupancy snapshots in SQL Server. It predicts the occupancy rate for the next 1–6 hours, one value per hour, for a single car park. A web page can call this API later. This repository is the solution, the HTTP API, and the pluggable prediction services.

Occupancy in every response is a **percent of capacity on a 0–100 scale** (0 empty, 100 full). The field is `predictedOccupancyPercent`. It is not a 0–1 fraction. The payload also includes `occupancyPercentScale` with the value `"0-100"`.

## English

### Run

```bash
dotnet restore CarParkOccupancy.slnx
dotnet test CarParkOccupancy.slnx
dotnet run --project src/CarParkOccupancy.Api
```

The development URL is `http://localhost:5080`. OpenAPI is served at `http://localhost:5080/openapi/v1.json`.

`global.json` asks for the .NET 10 SDK (`10.0.100`, roll forward to the latest 10.0 feature band). Project files set `<TargetFramework>net10.0</TargetFramework>`.

### Connection string

The API reads `ConnectionStrings:CarParkDb`. The committed `appsettings.json` leaves that value empty. Do not commit passwords or other secrets.

User secrets (development):

```bash
dotnet user-secrets set "ConnectionStrings:CarParkDb" "Server=localhost;Database=CarPark;User Id=app;Password=<secret>;Encrypt=True;TrustServerCertificate=True" --project src/CarParkOccupancy.Api
```

Environment variable (shell or container):

```bash
export ConnectionStrings__CarParkDb="Server=localhost;Database=CarPark;User Id=app;Password=<secret>;Encrypt=True;TrustServerCertificate=True"
```

`GET /health` does not open SQL Server. Data endpoints return HTTP 503 with a short message when the connection string is missing or the query fails. The response does not include the connection string.

### SQL Server table

Suggested script for a new database: `db/001_CarParkOccupancySnapshot.sql`.

| Config key | Default column | Meaning |
| --- | --- | --- |
| `CarParkData:Columns:CarParkCode` | `CarParkCode` | Car park id |
| `CarParkData:Columns:SnapshotTime` | `SnapshotTime` | When the snapshot was taken |
| `CarParkData:Columns:CountingCategory` | `CountingCategory` | Optional slice such as vehicle type |
| `CarParkData:Columns:Capacity` | `Capacity` | Space count for that snapshot |
| `CarParkData:Columns:Occupied` | `Occupied` | Occupied space count |

Stakeholder columns are `CarParkCode`, `SnapshotTime`, `CountingCategory`, and `Capacity`. `Occupied` is required as well, because the rate is `Occupied / Capacity * 100`. If the count lives in another column, change `CarParkData:Columns:Occupied`. Table name defaults to `CarParkOccupancySnapshot` (`CarParkData:Schema` + `CarParkData:TableName`). Only letters, digits, and underscores are accepted for those names.

`SnapshotTime` is treated as local time in `Prediction:Timezone` (default `Asia/Hong_Kong`) unless `CarParkData:SnapshotTimeIsUtc` is true. When `countingCategory` is omitted, rows that share a timestamp are summed across categories.

### Endpoints

`GET /health`

```json
{ "status": "ok", "service": "CarParkOccupancy.Api", "framework": "net10.0" }
```

`GET /api/carparks`

Distinct `CarParkCode` values.

`GET /api/carparks/{code}/predictions`

Query:

- `horizons` — integers 1 through 6. Repeat the key or pass a comma list (`horizons=1&horizons=3` or `horizons=1,3`). Default is 1,2,3,4,5,6.
- `countingCategory` — optional.
- `asOf` — optional ISO-8601 reference time. Default is the server UTC time. Horizons are measured from this instant (`generatedAt` in the response).
- `predictor` — `HistoricalBaseline` (default) or `GeminiVertex`.

`POST /api/carparks/predictions`

```json
{
  "carParkCodes": ["CP001", "CP002"],
  "countingCategory": "PrivateCar",
  "asOf": "2026-10-07T02:00:00Z",
  "horizons": [1, 2, 3],
  "predictor": "HistoricalBaseline"
}
```

At most 50 codes. Unknown codes are returned in the batch with `found: false` rather than failing the whole call.

### Sample curl

```bash
curl -s http://localhost:5080/health

curl -s http://localhost:5080/api/carparks

curl -s "http://localhost:5080/api/carparks/CP001/predictions"

curl -s "http://localhost:5080/api/carparks/CP001/predictions?horizons=1&horizons=3&countingCategory=PrivateCar"

curl -s -X POST http://localhost:5080/api/carparks/predictions \
  -H "Content-Type: application/json" \
  -d '{"carParkCodes":["CP001","CP002"],"horizons":[1,2,3]}'
```

Example prediction (numbers are illustrative):

```json
{
  "carParkCode": "CP001",
  "countingCategory": "PrivateCar",
  "generatedAt": "2026-10-07T02:00:00+00:00",
  "timezone": "Asia/Hong_Kong",
  "occupancyPercentScale": "0-100",
  "method": "HistoricalBaseline",
  "predictor": "HistoricalBaseline",
  "liveModelInvoked": false,
  "recentSampleCount": 6,
  "recentTrendHours": 6,
  "latestSnapshotTime": "2026-10-07T01:55:00+00:00",
  "latestCapacity": 200,
  "latestOccupied": 140,
  "latestOccupancyPercent": 70.0,
  "predictions": [
    {
      "horizonHours": 1,
      "targetTime": "2026-10-07T03:00:00+00:00",
      "predictedOccupancyPercent": 68.5,
      "baselinePercent": 64.0,
      "recentTrendPercent": 4.5,
      "appliedTrendPercent": 4.5,
      "historicalSampleCount": 40,
      "baselineSource": "SameWeekdayHour"
    }
  ]
}
```

`predictedOccupancyPercent`, `baselinePercent`, and `latestOccupancyPercent` use the 0–100 scale. `recentTrendPercent` is a difference on that same point scale. Null `predictedOccupancyPercent` means there was no usable history.

### Predictors

`IOccupancyPredictor` is the plug-in point. `OccupancyPredictorResolver` selects an implementation from `Prediction:DefaultPredictor` or the `predictor` query/body field.

`HistoricalBaselinePredictor` (default, `method` = `HistoricalBaseline`):

1. Keep snapshots at or before `asOf`. Drop non-positive capacity.
2. Hold out the last `Prediction:RecentTrendHours` (default 6). Older snapshots are the baseline pool.
3. Group each pool into local clock hours in `Prediction:Timezone`. One hour is one sample: sum of occupied spaces divided by sum of capacity. Polling frequency does not change the weight of that hour.
4. For the target hour (asOf + horizon), average samples with the same local weekday and clock hour (`baselineSource` = `SameWeekdayHour`). If none exist, use the same clock hour on any weekday (`SameHour`), then the overall historical average (`Overall`).
5. Trend = recent hourly average minus the historical average for the reference hour. Add `trend * TrendDecay ^ (horizon - 1)` (default decay 0.7). Clamp to 0–100.
6. If the baseline pool is empty but the recent window is not, the prediction is the recent average (`RecentOnly`) and no trend is applied.

`GeminiVertexPredictor` (`predictor` = `GeminiVertex`) reads `Prediction:Vertex:ProjectId`, `Location`, and `Model`. If any of those is empty, it falls back to the historical baseline and sets `fallbackReason`. Version 1 does not call Vertex AI even when those values are set, so the process builds and runs with no Gemini credentials. In that case `method` stays `HistoricalBaseline` (the algorithm that produced the numbers), `liveModelInvoked` is false, and `fallbackReason` says the predictor is a stub. A later version can implement the live call behind the same interface.

To add a predictor, implement `IOccupancyPredictor`, register it in `Program.cs`, and add its `Name` to `OccupancyPredictorResolver`.

### Docker

The image is `net10.0` (`mcr.microsoft.com/dotnet/sdk:10.0` and `mcr.microsoft.com/dotnet/aspnet:10.0`):

```bash
docker build -t carpark-occupancy-api .
docker run --rm -p 8080:8080 \
  -e ConnectionStrings__CarParkDb="Server=...;Database=...;User Id=...;Password=...;Encrypt=True" \
  carpark-occupancy-api
```

### Data access

Reads go through Dapper and `Microsoft.Data.SqlClient`. The table already exists in the stakeholder database, and the table and column names are configuration, so the API does not ship EF Core migrations. Identifiers from configuration are allow-listed before they are composed into SQL. Filter values are parameters.

### Tests

```bash
dotnet test CarParkOccupancy.slnx
```

`HistoricalBaselinePredictor` unit tests cover the weekday/hour average, recent trend and decay, 0–100 clamping, the percent scale and JSON field name, hour bucketing, and empty history.

## 中文（香港）

**目標框架：`net10.0`。** 呢個係 .NET 10 上面嘅 ASP.NET Core Web API。

大約 50 個停車場，每個約有一年佔用快照（SQL Server）。API 預測未來 1 至 6 小時、每小時一個佔用率。網頁之後先至接上嚟。呢個 repo 包括 solution、HTTP API，同可替換嘅預測服務。

所有回應入面嘅佔用率都係**容量百分比，刻度 0–100**（0 代表空，100 代表滿）。欄位名係 `predictedOccupancyPercent`，唔係 0–1 分數。回應亦有 `occupancyPercentScale`，值係 `"0-100"`。

### 執行

```bash
dotnet restore CarParkOccupancy.slnx
dotnet test CarParkOccupancy.slnx
dotnet run --project src/CarParkOccupancy.Api
```

開發網址係 `http://localhost:5080`。OpenAPI 文件係 `http://localhost:5080/openapi/v1.json`。

`global.json` 指定 .NET 10 SDK。各 csproj 嘅 `<TargetFramework>` 係 `net10.0`。

### 連線字串

設定鍵係 `ConnectionStrings:CarParkDb`。repo 入面嘅 `appsettings.json` 只留空字串，唔好提交密碼或者其他秘密。

```bash
dotnet user-secrets set "ConnectionStrings:CarParkDb" "Server=localhost;Database=CarPark;User Id=app;Password=<secret>;Encrypt=True;TrustServerCertificate=True" --project src/CarParkOccupancy.Api
```

或者：

```bash
export ConnectionStrings__CarParkDb="Server=localhost;Database=CarPark;User Id=app;Password=<secret>;Encrypt=True;TrustServerCertificate=True"
```

`GET /health` 唔會連 SQL Server。未設定連線或者查詢失敗時，資料端點回 HTTP 503，內容唔會帶連線字串。

### 資料表

新庫可以用 `db/001_CarParkOccupancySnapshot.sql`。持份者提供嘅欄位係 `CarParkCode`、`SnapshotTime`、`CountingCategory`、`Capacity`。計算佔用率仲需要佔用車位數，預設欄位名 `Occupied`（`佔用 / 容量 * 100`）。如果實際欄位名唔同，改 `CarParkData:Columns`。資料表名預設 `CarParkOccupancySnapshot`，可用 `CarParkData:Schema` 同 `CarParkData:TableName` 改。名稱只接受英文字母、數字同底線。

除非 `CarParkData:SnapshotTimeIsUtc` 設為 true，否則 `SnapshotTime` 當作 `Prediction:Timezone`（預設 `Asia/Hong_Kong`）嘅本地時間。冇傳 `countingCategory` 時，同一時間戳嘅各類別會加總。

### 端點

- `GET /health` — 健康檢查，`framework` 係 `net10.0`。
- `GET /api/carparks` — 不重複嘅 `CarParkCode`。
- `GET /api/carparks/{code}/predictions` — 未來 1–6 小時預測。查詢參數：`horizons`（1–6，可重複或逗號分隔，預設 1 至 6）、`countingCategory`、`asOf`（ISO-8601，預設而家）、`predictor`（`HistoricalBaseline` 或 `GeminiVertex`）。
- `POST /api/carparks/predictions` — 一次最多 50 個停車場。搵唔到嘅代號會喺結果入面標 `found: false`。

```bash
curl -s http://localhost:5080/health
curl -s http://localhost:5080/api/carparks
curl -s "http://localhost:5080/api/carparks/CP001/predictions"
curl -s "http://localhost:5080/api/carparks/CP001/predictions?horizons=1&horizons=3&countingCategory=PrivateCar"
curl -s -X POST http://localhost:5080/api/carparks/predictions \
  -H "Content-Type: application/json" \
  -d '{"carParkCodes":["CP001","CP002"],"horizons":[1,2,3]}'
```

`predictedOccupancyPercent` 係 0–100。冇可用歷史時，呢個欄位係 null。

### 預測器

`IOccupancyPredictor` 係插入點。`Prediction:DefaultPredictor` 或請求入面嘅 `predictor` 揀實作。

`HistoricalBaselinePredictor`（預設）：用同一個本地星期同鐘點嘅歷史平均，再加上最近 N 小時（預設 6）相對呢個鐘點歷史平均嘅趨勢。趨勢會隨預測時距用 `TrendDecay ^ (時距 - 1)` 遞減（預設 0.7），結果限制喺 0–100。每個本地鐘點先合成一個樣本，所以取樣密度唔會改變權重。最近 N 小時唔會計入歷史基線，避免趨勢計兩次。

`GeminiVertexPredictor` 讀 `Prediction:Vertex` 嘅 `ProjectId`、`Location`、`Model`。未設定就退回歷史基線。v1 即使已設定都唔會呼叫 Vertex AI，因此唔使 Gemini 憑證都可以建置同執行。此時 `method` 仍然係 `HistoricalBaseline`，`liveModelInvoked` 係 false。

### Docker

映像用 .NET 10（`mcr.microsoft.com/dotnet/sdk:10.0` 同 `aspnet:10.0`）。連線字串用環境變數傳入，唔好寫入映像。

```bash
docker build -t carpark-occupancy-api .
docker run --rm -p 8080:8080 \
  -e ConnectionStrings__CarParkDb="Server=...;Database=...;User Id=...;Password=...;Encrypt=True" \
  carpark-occupancy-api
```

### 資料存取

用 Dapper 同 `Microsoft.Data.SqlClient` 讀取現有資料表。表名同欄位名嚟自設定，所以呢版冇 EF Core migration。設定入面嘅識別名稱會先做白名單檢查，篩選值用參數傳遞。
