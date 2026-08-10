# AI Coding Agent 強制規則

這份文件優先級高於 AI Agent 自己的架構偏好。

## Scope Lock

只實作需求文件列出的功能。

禁止自行加入：

- 好友
- 聊天
- 排行榜
- 社群
- 團隊
- 公開玩家搜尋
- OAuth
- Google Login
- Email
- 推播
- QR Code
- PWA 離線
- WebSocket
- SignalR 即時對戰
- React
- Vue
- Blazor
- Microservices
- Redis
- PostgreSQL
- SQL Server
- 獨立 API 專案
- 獨立 DB Container
- Admin Portal
- 權限角色系統（需求沒有提出時）
- 陀螺圖片／零件資料庫
- 第三方分析工具
- 自動雲端部署

除非使用者後續明確要求，禁止加入。

## Architecture Lock

使用：

- ASP.NET Core
- Razor Pages
- Bootstrap
- Vanilla JS
- EF Core
- SQLite
- Docker
- Cloudflare Tunnel

不要因為「未來可能擴充」改成其他架構。

## Backend Rule

所有：

- 得分
- 勝負
- 發射失誤
- 4 分條件
- Round 完成
- Reorder
- Revision
- Battle 完成

都由 Server 判斷。

Client 不能送任意 Score。

## Data Integrity

不要因為 UI 簡單而犧牲：

- FK
- Unique constraint
- Authorization
- Battle state validation
- Transaction
- Concurrency protection（在可能造成雙重提交的操作上）

## Revision Rule

修改 Round 不可只修改畫面。

必須：

1. 讀取完整 Round。
2. 保存 Revision。
3. 重建該 Round 有效結果。
4. 重新計算 Battle。
5. 重新計算統計。

## No Premature Optimization

不要：

- 建 Statistics Table。
- 建 cache。
- 建 event bus。
- 建 repository abstraction 只為了形式。
- 建 generic CRUD framework。
- 建複雜 domain event infrastructure。

除非實作真的需要。

## Development Discipline

每一階段：

1. Build。
2. Unit Test。
3. 修正。
4. 才進下一階段。

如果測試失敗，不得用「先忽略」方式繼續堆功能。

## Ambiguity Rule

需求無法明確推導時：

- 不猜。
- 不自行新增規則。
- 列出問題。
- 等使用者確認。

## UI Rule

手機優先。

裁判操作最重要。

按鈕要大。

避免把重要操作藏在 dropdown。

達成 >=4 後要顯示明確狀態，但不可自動 Completed。

## Delivery Rule

最後必須能：

```text
docker compose up -d
```

啟動。

並能：

```text
http://localhost:8080
```

使用。

Cloudflare Tunnel 由主機側提供。

## Completion Definition

只有以下全部完成才算 MVP 完成：

- Account
- Login
- DisplayName
- Beyblade CRUD
- Battle setup
- Lineup lock
- Battle scoring
- Launch fault
- Reorder
- Finish Battle
- Round revision
- User statistics
- Beyblade statistics
- Opponent statistics
- Sorting
- Docker
- SQLite persistence
- Cloudflare Tunnel instructions
- Acceptance tests
