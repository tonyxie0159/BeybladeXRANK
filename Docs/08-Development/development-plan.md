# 現行開發計畫

本文件只追蹤「有效規格與目前實作的差距」。不再使用已完成的兩天建置階段，也不保留舊版 Battle Setup 作為替代流程。

## 已建立的基準

- .NET 10 Razor Pages、EF Core SQLite、Cookie Authentication。
- User／Beyblade 與軟刪除。
- QuickBattleInvitation、雙方私密 Lineup／Reorder、B／X Side 與完整快速計分。
- Battle-specific ScoreToWin、實際 Player／Beyblade Round、Revision replay、棄權／取消。
- Tournament／Entry／Member／Invitation／Match 資料與 migrations。
- 單淘汰、雙敗、單循環、瑞士輪賽程及多種個人／團體 RuleSet。
- Tournament 出賽、Lineup、推進、取消、Void／Reopen 與下游保護。
- 來源分區、B／X Side、個人／陀螺／對手／歷史統計。
- runtime data directory、Dockerfile、compose 與 Cloudflare 說明。
- 123 項 Domain／Service／Persistence／PageModel 測試通過。

上述代表已有自動證據，不代表 UI、實機部署或下列差距已驗收。

## 開發前置 Gate：整理目前分支

目前工作樹同時包含快速對戰、Tournament、統計、Migration、文件與部署調整。新增功能前必須：

1. 依依賴關係整理現有變更，不遺失 migration 或測試。
2. 將文件一致化獨立成可審查 commit。
3. 將既有功能拆成聚焦 commit／stacked branches；每個 PR 只包含一個 coherent change。
4. 每個分支重跑完整測試，先以 draft PR 送審。

不得在未整理的大型 dirty worktree 上持續混入所有後續功能。

## Phase 1：快速對戰唯一流程與返回入口（已完成）

完成證據：`GetActiveBattlesAsync`、Home／Invitations 狀態導向、Quick／Tournament／所有權／終止狀態回歸測試，以及舊 BattleService 契約移除均已完成。

目標：

- Active battle query 包含準備、InProgress、ReorderSelection、VictoryPendingCompletion。
- Home／Invitations 依狀態導向 Setup、Reorder 或 Battle。
- 玩家刷新、登出、查看其他頁面後不需要記住 BattleId。
- 將 BattleService 的 CreateDraft、雙邊 SetLineup／Reorder 舊方法移出有效 public contract，遷移舊測試到 QuickBattleFlowService。

Regression tests：

- 每個 active 狀態都能被正確玩家查到並取得目的頁。
- 其他使用者不可看到或開啟。
- Completed／Forfeited／Voided／Cancelled 不出現在 active 清單。
- 產品測試不再建立 Draft quick Battle。

建議 PR：quick-battle-resume-and-contract-cleanup。

## Phase 2：Tournament 報名生命週期

### 2A 主辦方 Tournament invitation（已完成）

完成證據：精確 Account／DisplayName 搜尋、Tournament invitation 狀態歷史、接受時建立或恢復唯一 Entry、容量失效、所有權與 invitation WaitingForMe 測試均已完成。

- 依 Account／完整 DisplayName 精確搜尋。
- 建立 Type = Tournament invitation，保留 Pending／Accepted／Declined／Invalidated。
- 接受時才建立有效 Entry，並套用容量、唯一 Entry 與 concurrency 規則。
- WaitingForMe 能看見並處理。

建議 PR：tournament-participant-invitations。

### 2B 通用重新開放報名（已完成）

完成證據：個人、整隊、系統配隊、賽程草稿清理、重複操作、主辦方權限與正式開始後拒絕測試均已完成；舊 system-pairing 專用 reopen 契約已移除。

- 個人、整隊及草稿失效後可由主辦方 ReopenRegistration。
- 清理失效草稿／SchedulePosition，不變更已正式開始的 Tournament。
- 系統配隊既有 reopen 行為整併到一致狀態轉移。

建議 PR：tournament-registration-reopen。

## Phase 3：出賽未到與操作佇列（已完成）

完成證據：個人／團體 No-show、權限與狀態拒絕、單次 progression、零 Battle／零假戰績，以及邀請／組隊／Match／裁判 WaitingForMe 精確待辦、優先排序、完整摘要與身份操作測試均已完成。

- 主辦方只可對 AwaitingParticipationConfirmation 的確定 Entry 操作。
- 二次確認、原因選填；未回應不自動倒數。
- 產生 Walkover、Winner／Loser 與 progression，不建立有比分 Battle。
- Tournament List 新增 WaitingForMe filter 與待處理優先排序。
- 摘要補齊 Mode、RegistrationMode、Format、Rule、Notes 與身份按鈕。

Regression tests：

- 參賽者未回覆前不自動判負。
- 非主辦方、錯誤 Entry、錯誤狀態與重複操作被拒絕。
- No-show 只推進一次且不產生個人／陀螺假戰績。

建議 PR：tournament-no-show-and-action-queue。

## Phase 4：公開 Tournament read model 與 polling（已完成）

完成證據：spectator 公開資料與 private workspace 邊界、私密 submission 公開時機、完成結果／Side／比分／實際 Lineup、Cancelled 保留資料，以及 List／Details／Match GET-only polling 不修改狀態測試均已完成。

- 建立專用 public details ViewModel／query，不放寬 private Match workspace。
- 顯示完整賽程、Entry、勝方、比分、目前 Match、實際玩家與已公開陀螺。
- Cancelled Tournament 保留取消前合法完成資料。
- Lineup 未全部提交前不外洩任何對手或觀眾不可見資料。
- List 低頻率 polling；Details／current Match polling 並保留手動刷新。

Regression tests：

- spectator 可讀公開結果但不能取得 private selections。
- participant／organizer 權限不回歸。
- polling endpoint 只讀，不造成重複 POST。

建議 PR：tournament-public-details。

## Phase 5：正式排名與必要加賽

### 5A 淘汰賽名次

- [已完成] 單淘汰非決賽者依淘汰輪次並列。
- [已完成] 雙敗依第二敗淘汰階段及決賽結果形成正式名次。
- [已完成] Bye／Walkover 不虛構比分，且淘汰賽只在 Tournament Completed 後公布正式名次。

### 5B 循環／瑞士必要加賽

- [已完成] 保留既有 tie-break 順序，加賽結果不反向改寫例行排名統計。
- [已完成] 完全同分時只對冠軍候選建立平衡單淘汰 Playoff bracket。
- [已完成] 一般非冠軍同分維持並列，多名冠軍候選的非冠軍者在加賽後維持第二名並列。
- [已完成] Tournament 在冠軍加賽完成前保持 InProgress，完成後才寫入 Completed。

建議拆成兩個 PR：elimination-standings、tournament-required-playoffs。

## Phase 6：併發、HTTP 與 UI 驗收

- 使用兩個 DbContext 驗證最後名額並行報名。
- 補完成 Round／Battle／Match 重複 POST 的 integration test。
- 使用 WebApplicationFactory 或等效方法測 authentication、authorization、anti-forgery、route state。
- 以瀏覽器／多帳號人工驗收私密 Lineup、polling 與手機版 UI。

建議 PR：concurrency-and-web-integration-tests。

## Phase 7：部署安全與實機驗收

- 明確設定 ForwardedHeaders、可信 proxy／network 與外部 HTTPS scheme。
- Authentication Cookie 使用 HttpOnly、適當 SameSite，外部 HTTPS 使用 Secure。
- Docker build、migration、restart、SQLite／keys persistence、backup／restore。
- LAN 與 Cloudflare Quick Tunnel 實測。
- 有固定 Domain 時再建立 named tunnel；不宣稱 Quick Tunnel 為正式 SLA。

建議 PR：cloudflare-forwarding-and-deployment-verification。

## 每個 Phase 的完成條件

1. 只修改該 coherent scope 的程式與文件。
2. Domain／Service mutation 有 focused regression test。
3. 涉及 Razor Pages 時補 HTTP 或明確人工驗收證據。
4. migration 變更須驗證 upgrade path 與 CurrentModel_HasNoPendingMigrationChanges。
5. 執行：

```powershell
dotnet test BeybladeXRANK-main/BeybladeRecordSystem.slnx
```

6. 更新 acceptance-tests.md 的證據狀態。
7. 開啟 draft PR，檢查完成後才轉 ready。
