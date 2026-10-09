# Car park occupancy predictions

**Target framework: `net10.0`.** This is an ASP.NET Core Web API on the .NET 10 SDK.

The API serves about 50 car parks. Each park has about a year of occupancy snapshots. It predicts the occupancy rate for the next 1–6 hours, one value per hour, for a single car park. The same process serves a Razor Pages UI that calls these endpoints. This repository is the solution, the web UI, the HTTP API, and the pluggable prediction services.

Snapshots are read through `IOccupancySnapshotSource`. The default HTTP source consumes [car-park-occupancy-data-api](https://github.com/psl-rchan/car-park-occupancy-data-api) at `http://localhost:5095`. SQL Server and sample snapshots remain available.

Occupancy in every response is a **percent of capacity on a 0–100 scale** (0 empty, 100 full). The field is `predictedOccupancyPercent`. It is not a 0–1 fraction. The payload also includes `occupancyPercentScale` with the value `"0-100"`.

## English

### Run

```bash
dotnet restore CarParkOccupancy.slnx
dotnet test CarParkOccupancy.slnx
dotnet run --project src/CarParkOccupancy.Api
```

The development URL is `http://localhost:5080`. OpenAPI is served at `http://localhost:5080/openapi/v1.json`.

Run the sibling data API in a separate terminal first, with its database configured according to its README:

```bash
cd /workspace/car-park-occupancy-data-api
dotnet restore
dotnet run --project src/CarParkOccupancy.DataApi --urls http://localhost:5095
```

Then run the predictions API from `/workspace/car-park-occupancy-predictions` using the commands above. The committed HTTP settings already point at port 5095.

### Web UI

`dotnet run` serves the pages and the API on the same host. The pages call the HTTP API (they do not read SQL themselves).

- Dashboard: [http://localhost:5080/](http://localhost:5080/) — car parks and predicted occupancy for hours 1–6, plus two high-occupancy charts. It calls `GET /api/carparks` and `POST /api/carparks/predictions`. High occupancy is a percent of capacity at or above `CarParkData:HighOccupancyThresholdPercent` (default 80). One chart lists parks predicted to hit that level in any of the next 1–6 hours, and marks parks that stay high in every hour. The other lists parks whose latest snapshot is already high. The legend on the page names the setting.
- One car park: `http://localhost:5080/carparks/{code}` — bar chart and table, including `generatedAt` and `method`. It calls `GET /api/carparks/{code}/predictions`.

When the selected snapshot source cannot be read, those API calls return HTTP 503 (SQL Server is missing or down, the HTTP API is not configured, or the remote host cannot be reached) or HTTP 502 (the remote API returned an unexpected status or JSON). The page explains that car park data is unavailable. It does not show the connection string or an API key.

To click through the UI without SQL Server, turn on Development sample snapshots. This is ignored when a connection string is set, and it is ignored outside Development:

```bash
ASPNETCORE_ENVIRONMENT=Development CarParkData__Source=Sql CarParkData__UseSampleSnapshots=true dotnet run --project src/CarParkOccupancy.Api
```

Open `http://localhost:5080/`. The page shows a sample-data banner. Leave `CarParkData:UseSampleSnapshots` false (the committed default) for real data. `CarParkData:Source=Sample` forces that preview even when a connection string is set. The default `Http` source ignores the sample flag. See [Snapshot source](#snapshot-source).

`global.json` asks for the .NET 10 SDK (`10.0.100`, roll forward to the latest 10.0 feature band). Project files set `<TargetFramework>net10.0</TargetFramework>`.

### Snapshot source

`CarParkData:Source` selects who supplies occupancy snapshots. Prediction and the Razor Pages UI do not talk to SQL Server or the remote API themselves. They use `ICarParkReadStore`, and that store reads `IOccupancySnapshotSource`.

| `CarParkData:Source` | Implementation | Use |
| --- | --- | --- |
| `Sql` | `SqlServerOccupancySnapshotSource` | Existing Dapper / SQL Server path |
| `Http` (default) | `HttpOccupancySnapshotSource` | car-park-occupancy-data-api |
| `Sample` | `SampleOccupancySnapshotSource` | Generated local UI data |

`Sql` stays compatible with the current Development preview. When `CarParkData:UseSampleSnapshots` is true, the process is Development, and `ConnectionStrings:CarParkDb` is empty, sample snapshots are still selected. A configured connection string wins over that flag. `Source=Http` does not fall back to sample data. `Source=Sample` uses sample data in any environment.

```bash
# SQL Server (optional)
export CarParkData__Source=Sql
export ConnectionStrings__CarParkDb="Server=localhost;Database=CarPark;User Id=app;Password=<secret>;Encrypt=True;TrustServerCertificate=True"

# Local sample UI
CarParkData__Source=Sample dotnet run --project src/CarParkOccupancy.Api

# Sibling data API (default)
export CarParkData__Source=Http
export CarParkData__Http__BaseUrl="http://localhost:5095"
# Only if the deployed data API requires authentication:
export CarParkData__Http__AuthHeaderName="X-Api-Key"
export CarParkData__Http__AuthHeaderValue="<secret>"
```

`appsettings.json` sets `CarParkData:Http:BaseUrl` to `http://localhost:5095` and leaves `AuthHeaderName` and `AuthHeaderValue` empty. Override the URL for deployment. Put secret header values in environment variables or user secrets; do not commit them.

```bash
dotnet user-secrets set "CarParkData:Http:BaseUrl" "http://localhost:5095" --project src/CarParkOccupancy.Api
dotnet user-secrets set "CarParkData:Http:AuthHeaderName" "X-Api-Key" --project src/CarParkOccupancy.Api
dotnet user-secrets set "CarParkData:Http:AuthHeaderValue" "<secret>" --project src/CarParkOccupancy.Api
```

The HTTP client is a typed `HttpClient`. `CarParkData:Http:TimeoutSeconds` defaults to 30. Data endpoints return:

- HTTP 503 when the base URL is missing, the call times out, the host cannot be reached, or the remote API returns HTTP 502, 503, or 504.
- HTTP 502 when the remote API returns another error status, or JSON that does not match the snapshot contract.

The response does not include the auth header value. Paths under `CarParkData:Http` are relative to `BaseUrl`:

| Setting | Default path | Use |
| --- | --- | --- |
| `CarParksPath` | `api/carparks` | List `{ carParkCode, carparkNumber }` objects; extract distinct codes |
| `HistoryPathTemplate` | `api/carparks/{code}/occupancy` | Paged history |
| `LatestPathTemplate` | `api/carparks/{code}/occupancy/latest` | Existence check using the latest snapshot array |
| `LatestAllPath` | `api/occupancy/latest` | Data API's all-parks latest endpoint; reserved, not called by the current read-store flow |

History requests use `from` and `to` in UTC ISO-8601 round-trip format, `page` (starting at 1), `pageSize` (default 1000), `sampleEverySeconds` (default 300, downsampling the 15-second source cadence), and optional `category` (`CategoryQueryParameter`). Pages are merged until a short/empty page or `MaxPages` (default 50); reaching the limit logs a warning because history may be truncated. `SnapshotsPath` has been replaced by these endpoint-specific settings; migrate old configurations. The predictions API's own `countingCategory` parameter is unchanged.

History returns `{ page, pageSize, itemCount, items: [...] }`. `OccupancySnapshotJson` uses `CarParkData:Http:Json:Collection=items` and case-insensitive names; bare arrays and single snapshot objects are still accepted:

```json
{
  "page": 1,
  "pageSize": 1000,
  "itemCount": 1,
  "items": [
    {
      "carParkCode": "CP001",
      "snapshotTime": "2026-10-07T01:55:00Z",
      "countingCategory": "PrivateCar",
      "capacity": 200,
      "occupied": 140
    }
  ]
}
```

The data API maps DB `CarParkCode` → DTO `CarParkCode` (`carParkCode`), `Time` → `SnapshotTime` (`snapshotTime`), `Category` → `CountingCategory` (`countingCategory`), `Occupancy` → `Occupied` (`occupied`), and `Capacity` → `Capacity` (`capacity`). `SnapshotTime` is UTC; the committed `CarParkData:SnapshotTimeIsUtc=true` also treats offset-free timestamps as UTC. Set it to false explicitly when switching to SQL with local wall-clock timestamps. When category is omitted, rows that share a timestamp are summed.

**No same-day live data:** history endpoints reject UTC-today-or-later `from`/`to` with HTTP 400, and latest endpoints only return rows with `Time < start of today UTC`. The client clamps history's end to yesterday `23:59:59.999Z` and returns an empty list without an API call for windows entirely today or later. The live/current 1–6-hour predictions and “current high occupancy” chart therefore use the latest available historical (pre-today) data only, not same-day live polling. Prediction horizons still start at `asOf` (default now); check `latestSnapshotTime` to understand data age.

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

With `CarParkData:SnapshotTimeIsUtc=false`, SQL `SnapshotTime` is treated as local time in `Prediction:Timezone` (default `Asia/Hong_Kong`). The committed setting is true for the default HTTP contract; change it explicitly if your SQL data uses local timestamps. When `countingCategory` is omitted, rows that share a timestamp are summed across categories.

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
  -e CarParkData__Source=Sql \
  -e ConnectionStrings__CarParkDb="Server=...;Database=...;User Id=...;Password=...;Encrypt=True" \
  carpark-occupancy-api
```

The UI is on `http://localhost:8080/`. With an explicit `Sql` source, data pages return HTTP 503 until the connection string is set. For the default HTTP source, pass `CarParkData__Http__BaseUrl` pointing at the data API's reachable container/host address (container `localhost` is not the host) and any required auth header variables instead of baking secrets into the image.

### Data access

`IOccupancySnapshotSource` is the snapshot plug-in: `SqlServerOccupancySnapshotSource` (Dapper and `Microsoft.Data.SqlClient`), `HttpOccupancySnapshotSource` (typed `HttpClient`), and `SampleOccupancySnapshotSource`. `OccupancySnapshotReadStore` implements `ICarParkReadStore` for prediction. The SQL table already exists in the stakeholder database, and the table and column names are configuration, so the API does not ship EF Core migrations. Identifiers from configuration are allow-listed before they are composed into SQL. Filter values are parameters.

### Tests

```bash
dotnet test CarParkOccupancy.slnx
```

`HistoricalBaselinePredictor` unit tests cover the weekday/hour average, recent trend and decay, 0–100 clamping, the percent scale and JSON field name, hour bucketing, and empty history.

## 中文（香港）

**目標框架：`net10.0`。** 呢個係 .NET 10 上面嘅 ASP.NET Core Web API。

大約 50 個停車場，每個約有一年佔用快照。API 預測未來 1 至 6 小時、每小時一個佔用率。同一個行程會同時提供 Razor Pages 介面，介面會呼叫呢啲端點。呢個 repo 包括 solution、網頁、HTTP API，同可替換嘅預測服務。

快照經 `IOccupancySnapshotSource` 讀取。預設 HTTP 來源係 [car-park-occupancy-data-api](https://github.com/psl-rchan/car-park-occupancy-data-api)，網址 `http://localhost:5095`。SQL Server 同示範快照仍然可用。

所有回應入面嘅佔用率都係**容量百分比，刻度 0–100**（0 代表空，100 代表滿）。欄位名係 `predictedOccupancyPercent`，唔係 0–1 分數。回應亦有 `occupancyPercentScale`，值係 `"0-100"`。

### 執行

```bash
dotnet restore CarParkOccupancy.slnx
dotnet test CarParkOccupancy.slnx
dotnet run --project src/CarParkOccupancy.Api
```

開發網址係 `http://localhost:5080`。OpenAPI 文件係 `http://localhost:5080/openapi/v1.json`。

先喺另一個 terminal 啟動兄弟 data API，資料庫設定跟嗰個 repo 嘅 README：

```bash
cd /workspace/car-park-occupancy-data-api
dotnet restore
dotnet run --project src/CarParkOccupancy.DataApi --urls http://localhost:5095
```

然後喺 `/workspace/car-park-occupancy-predictions` 用上面指令啟動預測 API。repo 嘅 HTTP 設定已經指向 5095。

### 網頁

`dotnet run` 同一個 host 提供網頁同 API。網頁經 HTTP 呼叫 API，唔會自己讀 SQL。

- 總覽：[http://localhost:5080/](http://localhost:5080/) — 停車場列表同未來 1–6 小時預測佔用率，再加兩張高佔用圖。呼叫 `GET /api/carparks` 同 `POST /api/carparks/predictions`。高佔用係佔容量達到或超過 `CarParkData:HighOccupancyThresholdPercent`（預設 80）。一張圖列出未來 1–6 小時內任何一個小時會達到門檻嘅停車場，並標出每個小時都高佔用嘅場。另一張列出最新快照已經高佔用嘅場。頁面圖例寫明呢個設定鍵。
- 單一停車場：`http://localhost:5080/carparks/{code}` — 棒形圖同表格，顯示 `generatedAt` 同 `method`。呼叫 `GET /api/carparks/{code}/predictions`。

選定嘅快照來源讀唔到時，API 回 HTTP 503（未設定或連唔到 SQL Server、未設定 HTTP API，或者遠端主機連唔到）或 HTTP 502（遠端回咗預期之外嘅狀態碼或 JSON）。頁面會說明而家讀唔到停車場資料，亦唔會顯示連線字串或者 API 密鑰。

冇 SQL Server 又想撳吓介面，可以喺 Development 開示範快照。已設定連線字串，或者唔係 Development，呢個開關會被忽略：

```bash
ASPNETCORE_ENVIRONMENT=Development CarParkData__Source=Sql CarParkData__UseSampleSnapshots=true dotnet run --project src/CarParkOccupancy.Api
```

然後開 `http://localhost:5080/`。頁面會標明呢啲係示範數據。要用真實資料，保持 `CarParkData:UseSampleSnapshots` 為 false（repo 預設）。`CarParkData:Source=Sample` 可以強制用示範數據，即使已設定連線字串。預設 `Http` 來源會忽略示範開關。切換來源見下面「快照來源」。

`global.json` 指定 .NET 10 SDK。各 csproj 嘅 `<TargetFramework>` 係 `net10.0`。

### 快照來源

`CarParkData:Source` 決定邊度提供佔用快照。預測同 Razor Pages 唔會自己連 SQL Server 或者遠端 API。佢哋用 `ICarParkReadStore`，而呢個 store 再讀 `IOccupancySnapshotSource`。

| `CarParkData:Source` | 實作 | 用途 |
| --- | --- | --- |
| `Sql` | `SqlServerOccupancySnapshotSource` | 現有 Dapper / SQL Server 路徑 |
| `Http`（預設） | `HttpOccupancySnapshotSource` | car-park-occupancy-data-api |
| `Sample` | `SampleOccupancySnapshotSource` | 本機介面用嘅示範快照 |

`Sql` 仍然兼容而家嘅 Development 預覽。`CarParkData:UseSampleSnapshots` 為 true、行程係 Development、而且 `ConnectionStrings:CarParkDb` 係空，就會繼續用示範快照。已設定連線字串就以 SQL 為準。`Source=Http` 唔會退回示範數據。`Source=Sample` 喺任何環境都用示範數據。

```bash
# SQL Server（可選）
export CarParkData__Source=Sql
export ConnectionStrings__CarParkDb="Server=localhost;Database=CarPark;User Id=app;Password=<secret>;Encrypt=True;TrustServerCertificate=True"

# 本機示範介面
CarParkData__Source=Sample dotnet run --project src/CarParkOccupancy.Api

# 兄弟 data API（預設）
export CarParkData__Source=Http
export CarParkData__Http__BaseUrl="http://localhost:5095"
# 只有部署嘅 data API 要驗證先設定：
export CarParkData__Http__AuthHeaderName="X-Api-Key"
export CarParkData__Http__AuthHeaderValue="<secret>"
```

`appsettings.json` 入面 `CarParkData:Http:BaseUrl` 係 `http://localhost:5095`，`AuthHeaderName` 同 `AuthHeaderValue` 留空。部署時可以覆寫網址。秘密 header 值用環境變數或者 user secrets，唔好提交。

```bash
dotnet user-secrets set "CarParkData:Http:BaseUrl" "http://localhost:5095" --project src/CarParkOccupancy.Api
dotnet user-secrets set "CarParkData:Http:AuthHeaderName" "X-Api-Key" --project src/CarParkOccupancy.Api
dotnet user-secrets set "CarParkData:Http:AuthHeaderValue" "<secret>" --project src/CarParkOccupancy.Api
```

HTTP 用戶端係 typed `HttpClient`。`CarParkData:Http:TimeoutSeconds` 預設 30。資料端點：

- HTTP 503：未設定 base URL、呼叫逾時、連唔到主機，或者遠端回 HTTP 502、503、504。
- HTTP 502：遠端回其他錯誤狀態，或者 JSON 唔符合快照格式。

回應唔會帶 auth header 值。`CarParkData:Http` 路徑相對於 `BaseUrl`：

| 設定 | 預設路徑 | 用途 |
| --- | --- | --- |
| `CarParksPath` | `api/carparks` | 讀取 `{ carParkCode, carparkNumber }` 陣列，抽取不重複代號 |
| `HistoryPathTemplate` | `api/carparks/{code}/occupancy` | 分頁歷史快照 |
| `LatestPathTemplate` | `api/carparks/{code}/occupancy/latest` | 用最新快照陣列檢查係咪存在 |
| `LatestAllPath` | `api/occupancy/latest` | data API 所有場嘅最新快照端點；保留設定，目前 read-store 流程唔會呼叫 |

歷史查詢用 UTC ISO-8601 round-trip 格式嘅 `from`、`to`，以及 `page`（由 1 開始）、`pageSize`（預設 1000）、`sampleEverySeconds`（預設 300，將原本 15 秒頻率降採樣）、可選 `category`（`CategoryQueryParameter`）。分頁合併至短頁／空頁或者 `MaxPages`（預設 50）；達到上限會記錄警告，歷史可能被截斷。舊 `SnapshotsPath` 已由呢啲設定取代，舊配置要遷移。預測 API 自己嘅 `countingCategory` 參數不變。

歷史回應係 `{ page, pageSize, itemCount, items: [...] }`，`CarParkData:Http:Json:Collection` 預設 `items`。`OccupancySnapshotJson` 仍然接受裸陣列同單一快照物件，欄位名不分大小寫。data API 將 DB `CarParkCode` → DTO `CarParkCode`（`carParkCode`）、`Time` → `SnapshotTime`（`snapshotTime`）、`Category` → `CountingCategory`（`countingCategory`）、`Occupancy` → `Occupied`（`occupied`）、`Capacity` → `Capacity`（`capacity`）。時間係 UTC，repo 預設 `SnapshotTimeIsUtc=true`；冇 offset 嘅時間亦當作 UTC。冇傳 category 時，同一時間戳嘅各類別會加總。

**唔提供即日即時資料：**歷史端點嘅 `from`／`to` 如果係 UTC 今日或之後，會回 HTTP 400。最新端點只會回 `Time < UTC 今日開始` 嘅列。用戶端將歷史結束時間限制至昨日 `23:59:59.999Z`；完全喺今日或之後嘅窗口直接回空列表，唔呼叫 API。因此即時／目前嘅未來 1–6 小時預測，同「目前高佔用」圖，只用今日之前最新可用嘅歷史資料，唔係即日 live polling。預測時距仍然由 `asOf`（預設而家）開始，請睇 `latestSnapshotTime` 判斷資料有幾舊。

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

`CarParkData:SnapshotTimeIsUtc=false` 時，SQL `SnapshotTime` 當作 `Prediction:Timezone`（預設 `Asia/Hong_Kong`）嘅本地時間。repo 為預設 HTTP contract 設為 true；如果切換到用本地時間嘅 SQL 資料，請明確改為 false。冇傳 `countingCategory` 時，同一時間戳嘅各類別會加總。

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
  -e CarParkData__Source=Sql \
  -e ConnectionStrings__CarParkDb="Server=...;Database=...;User Id=...;Password=...;Encrypt=True" \
  carpark-occupancy-api
```

網頁係 `http://localhost:8080/`。明確選擇 `Sql` 來源而未設定連線字串時，資料頁會顯示 HTTP 503 說明。預設 HTTP 來源用 `CarParkData__Http__BaseUrl` 指向容器可以連到嘅 data API 地址（容器嘅 localhost 唔係宿主機），同所需 auth header 環境變數傳入，唔好將秘密寫入映像。

### 資料存取

`IOccupancySnapshotSource` 係快照插入點：`SqlServerOccupancySnapshotSource`（Dapper 同 `Microsoft.Data.SqlClient`）、`HttpOccupancySnapshotSource`（typed `HttpClient`）、`SampleOccupancySnapshotSource`。`OccupancySnapshotReadStore` 實作 `ICarParkReadStore` 畀預測用。SQL 資料表已經喺持份者資料庫，表名同欄位名嚟自設定，所以呢版冇 EF Core migration。設定入面嘅識別名稱會先做白名單檢查，篩選值用參數傳遞。
