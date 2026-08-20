# AI Coding Agent 強制規則

本文件約束所有程式與文件變更。產品規則以 Docs/README.md 定義的文件順序解讀；Agent 不得以自己的架構偏好、舊程式碼或暫時缺功能覆寫有效規格。

## Scope Lock

只實作有效文件與目前明確開發 Phase 內的功能。除非使用者另行核准，不加入：

- 好友、聊天、社群、公開玩家目錄或永久社群 Team。
- 排行榜、賽季、配對分、Elo 或其他未規範的強弱評分。
- OAuth、Google Login、Email、手機推播。
- QR Code、PWA 離線、WebSocket、SignalR。
- React、Vue、Blazor 或其他 SPA。
- Microservices、Redis、獨立 REST API、獨立 DB container。
- PostgreSQL、SQL Server、Admin Portal 或完整角色權限系統。
- 陀螺圖片／零件資料庫、第三方分析、自動雲端部署。

Tournament-scoped 雙人／三人臨時隊伍是已核准功能，不受「永久社群 Team」禁令限制；不得把它擴張成跨 Tournament 保存的 Team。

## Architecture Lock

固定使用：

- ASP.NET Core Razor Pages
- Bootstrap、必要的 Vanilla JavaScript
- EF Core + SQLite
- Docker 單一 Web container
- 主機側 Cloudflare Tunnel

不得為了「未來可能擴充」增加未需求的抽象或基礎設施。

## Canonical Battle Flow

- QuickBattleInvitation 與 Battle 是不同 aggregate；接受前不可建立 Battle。
- 快速賽前、雙方私密 Lineup、確認、edit request 與私密 Reorder 經 QuickBattleFlowService。
- BattleService 只負責已建立 Battle 的 Side、開始、事件、Round、Finish、棄權／取消、Revision 與授權讀取。
- 新程式不得呼叫 CreateDraftAsync、SetLineupAsync、LockLineupAsync 或 CreateReorderedLineupAsync 舊契約。
- Player A/B 是資料方向；B／X 是 SideADesignation，不能用玩家 Id 欄位重建舊 BSidePlayerId／XSidePlayerId 模型。

## Server Authority

Server 唯一決定：

- ResultType 對應分數。
- LaunchFault／LaunchFaultPenalty 與 fault count。
- SideAScore／SideBScore。
- Battle-specific ScoreToWin 與 VictoryPendingCompletion。
- Round 完成、Lineup Sequence、Reorder 合法性。
- WinningSide／WinningPlayer／WinnerEntry／LoserEntry。
- Tournament 配對、Bye、Walkover、下一場與排名。
- Revision replay、事件失效、下游撤銷與統計排除。

Client 不可提交任意 Score、整場比分、ScoreToWin、WinningSide、隊伍歸屬、晉級者或下一場 Entry。

## Data Integrity

不得因 UI 簡單而犧牲：

- FK、unique index、check constraint。
- 使用者及 aggregate ownership。
- 私密 Lineup 與 Account 隱私。
- Battle／Tournament／Match state validation。
- transaction 與 concurrency protection。
- Snapshot 與 Revision／Void audit。
- migration upgrade path。

最後名額、完成 Battle 並推進 Match、棄權、取消、Void／Reopen 與 Revision 必須考慮重複請求和並行操作。

## Statistics

- 不建立 Statistics Table 或 cache。
- 只聚合規格允許的 Battle、完成 Round 與有效 Event。
- 快速、個人、團體隊伍結果、團體實際小局分開。
- B／X Side 從 SideADesignation 與 Player／Entry A/B 推導；沒有 Side 的相容資料不得猜測。
- 來源／Side 篩選後在 Server 重新聚合。
- 不建立全域強弱排行榜。

## Revision／Cancellation

Revision 必須：

1. 驗證操作者、Battle、Round 與 Tournament 下游狀態。
2. 要求原因。
3. 保存 Round 及 Battle 修改前快照。
4. 重建有效 BattleResult。
5. 從最早受影響事件重播比分與狀態。
6. 保存修改後快照。
7. 使統計立即反映有效資料。

快速取消交易式硬刪除；Tournament 取消保留完成歷史；Tournament Void 保留 audit 且完全排除原 Battle。三者不得共用錯誤的資料保存策略。

## Privacy／Authorization

- 所有需要登入的 Page 使用 Authorize。
- PageModel 使用目前登入者 Id，不綁定 Client 提交的 owner userId。
- Service 再驗證使用者是否為擁有者、參賽者、代表人或主辦方。
- 公開 Tournament read model 與私密 Match workspace 分離。
- Lineup 未完成共同提交前，不向對手或觀眾公開。
- Account 只用於登入及精確邀請搜尋，不顯示在公開頁。

## UI

- 手機優先、按鈕可觸控、文字與顏色共同傳達狀態。
- 重要裁判操作不可藏在 dropdown。
- 達 ScoreToWin 顯示明確提示，不自動 Completed。
- 待處理／active Battle 有可發現返回入口。
- polling 只執行 GET／狀態刷新，不自動 POST。
- 取消、棄權、No-show、Void 及下游撤銷需要清楚影響說明與確認。

## No Premature Abstraction

不要建立：

- generic repository／unit of work 只為形式。
- generic CRUD framework。
- event bus、複雜 domain event infrastructure。
- 未需求 cache、backup service 或 deployment platform。

純 Domain schedule generator、Application Service 與專用 read model 是現行架構的一部分，不屬於過度抽象。

## Development Discipline

1. 開始前確認 branch 為 codex/*，不在 main 開發。
2. 一個 PR 只處理一個 coherent change，先開 draft。
3. 保留使用者既有 dirty worktree，不覆寫無關變更。
4. 先寫或更新 focused regression test，再完成 Service／UI。
5. 有 migration 時驗證 upgrade 與 model snapshot。
6. 執行完整 dotnet test。
7. 更新 acceptance-tests.md 與 development-plan.md。
8. 檢查文件沒有重新引入 Docs/README.md 列出的淘汰規格。

測試失敗不得先忽略並繼續堆功能。

## Ambiguity

若有效文件無法推導：

- 不猜測或保留兩套互斥流程。
- 列出衝突、資料影響與可選方案。
- 等使用者確認後，刪除被淘汰方案並同步所有文件。

## Deployment

最終需能使用：

```powershell
docker compose up -d --build
```

並在 localhost:8080 使用。SQLite 與 Data Protection keys 必須在 data/ 持久化。Cloudflare Tunnel 由主機側提供；外部 HTTPS 上線前驗證 forwarded headers、可信 proxy 與 Secure Cookie。

## 完成定義

MVP 完成必須同時具備：

- Account／Beyblade。
- 快速邀請、私密 Lineup、B／X Side、計分、Reorder、Finish、Revision、棄權／取消及 active return。
- Tournament 建立、邀請、報名／重開、整隊／配隊、四種賽制、多種 RuleSet、No-show、取消／Void、完整公開賽程及 polling。
- 單淘汰／雙敗名次與規範要求的循環／瑞士加賽。
- 來源與 B／X Side 玩家／陀螺／對手／歷史統計。
- concurrency、HTTP／authorization、手機 UI、Docker／SQLite／Cloudflare 驗收證據。
- 所有有效文件與程式一致，完整測試通過。
