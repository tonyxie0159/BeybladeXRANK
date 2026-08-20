# 程式架構

## 技術與部署邊界

- .NET 10 ASP.NET Core Razor Pages
- Bootstrap 與必要的 Vanilla JavaScript
- EF Core + SQLite
- xUnit
- Docker 單一 Web container
- 主機側 Cloudflare Tunnel

不建立 SPA、獨立 REST API 專案、獨立 DB container、WebSocket、SignalR、Redis 或微服務。

## 單體分層

```text
Browser
  |
Razor Pages / Cookie Authentication
  |
Application Services
  +-- AuthService
  +-- BeybladeService
  +-- QuickBattleFlowService
  +-- BattleService
  +-- TournamentService
  +-- TournamentMatchService
  +-- TournamentProgressionService
  +-- TournamentStandingsService
  +-- StatisticsService
  |
Domain rules / schedule generators
  |
AppDbContext / EF Core migrations
  |
SQLite + persisted Data Protection keys
```

### Web

Razor PageModel 只負責：

- Authentication／Authorization 入口。
- Model binding、基本輸入格式驗證與 anti-forgery。
- 呼叫 Application Service。
- 將 Service 結果轉為頁面、導向或錯誤訊息。
- 低頻率 polling 與手動刷新；不得在 JavaScript 計分或推進賽程。

### Application Services

- `AuthService`：註冊、密碼驗證、DisplayName。
- `BeybladeService`：目前使用者的 Beyblade CRUD 與軟刪除。
- `QuickBattleFlowService`：快速邀請、雙方私密 Lineup、確認、編輯請求、私密重排及 active battle 返回查詢。
- `BattleService`：Side 指定、開始、事件記錄、Round 完成、明確結束、快速棄權／取消、Revision 與授權讀取。
- `TournamentService`：建立、完整摘要列表、WaitingForMe 操作佇列、專用公開賽事 read model、主辦方參賽邀請、報名、通用重新開放、Tournament-scoped 組隊、賽程草稿、開始及整場取消。
- `TournamentMatchService`：精確待辦判斷、出賽確認、主辦方 No-show Walkover、個人／團體 Lineup、隊員順序、Side、棄權、撤銷及重開。
- `TournamentProgressionService`：完成 Match、解析勝敗來源、啟動下一場、瑞士輪後續配對，以及循環／瑞士冠軍完全同分時建立獨立 Playoff bracket 並延後 Tournament 完成。
- `TournamentStandingsService`：循環／瑞士固定 tie-break 與冠軍加賽覆寫，以及賽事完成後的單淘汰淘汰輪次、雙敗第二敗階段正式名次。
- `StatisticsService`：從有效 Battle／Round／Event 即時計算來源及 Side 戰績。

快速對戰的新建立、賽前流程與雙方重排只能經 `QuickBattleFlowService`。舊有 Draft、建立者雙邊 Lineup 與雙邊 Reorder 方法已從 `BattleService` 移除；新的 PageModel／測試不得重建這些契約。

### Domain

- Entity、Enum、BattleRules。
- TournamentRuleCatalog。
- 單淘汰、雙敗、循環與瑞士輪的純賽程／配對計算。
- 不依賴 HTTP 或頁面狀態。

### Infrastructure

- `AppDbContext`、Entity configuration、migration。
- `RuntimeStorage` 將 SQLite 與 Data Protection keys 統一放在 runtime data directory。
- Server-side PasswordHasher、Cookie Authentication。

## 所有權與隱私邊界

- Account 不公開；只有需要邀請的精確搜尋可使用 Account／完整 DisplayName。
- 使用者只能管理自己的 Beyblade 與私密 Lineup。
- 雙方／全體必要玩家都提交前，不可向對手或觀眾公開陣容。
- 公開 Tournament 詳情只顯示規範允許的 DisplayName、Entry、已公開 Lineup、實際選手、比分與賽程。
- `GetPublicDetailsAsync` 不載入 LineupSelection、TeamOrderSelection、Account、Invitation 或 Pending Entry；私密 Match workspace 仍只限主辦方與該場參賽者。
- Statistics 查詢由登入者 Id 限定，不接受 client 指定任意資料所有者。

## 交易與 concurrency

下列操作必須在交易中完成，並在可能競爭的 aggregate 使用 concurrency token：

- 接受快速邀請並建立 Battle。
- 最後名額報名、整隊正式報名與系統配隊。
- 產生／鎖定賽程與啟動首場。
- 記分、完成 Round、完成 Battle、推進 Match 與建立下一場通知。
- 快速對戰硬刪除、Tournament 取消、棄權、Void／Reopen 及 Revision。

重複請求必須是可拒絕或 idempotent，不得重複計分、晉級、通知或統計。

## 專案結構

```text
BeybladeXRANK-main/
├── src/BeybladeRecordSystem/
│   ├── Data/
│   ├── Domain/
│   │   ├── Entities/
│   │   ├── Enums/
│   │   └── Tournaments/
│   ├── Infrastructure/
│   ├── Migrations/
│   ├── Pages/
│   │   ├── Account/
│   │   ├── Battles/
│   │   ├── Beyblades/
│   │   ├── Statistics/
│   │   └── Tournaments/
│   ├── Services/
│   ├── ViewModels/
│   ├── wwwroot/
│   └── Program.cs
├── tests/BeybladeRecordSystem.Tests/
├── data/                     # runtime，Git 忽略
├── Dockerfile
├── compose.yaml
└── BeybladeRecordSystem.slnx
```
