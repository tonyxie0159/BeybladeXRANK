# BeybladeRecordSystem 開發文件

本目錄是 BeybladeRecordSystem 的唯一產品與工程規格來源。所有新功能、資料模型、UI、測試與 Pull Request 都必須以本目錄的有效規格為準。

## 文件狀態與適用順序

以下文件全部描述同一套現行有效規格，不保留舊版流程作為替代方案：

1. `01-Product/requirements.md`：全產品共通需求與邊界。
2. `11-Tournament-Schedule/tournament-schedule.md`：Tournament 專用流程；在 Tournament 情境下，其明確規則優先於快速對戰的固定值。
3. `04-Battle-Rules/`：所有 Battle 共用的計分、事件、狀態與修正原則。
4. `03-Database/schema.md`、`06-API/endpoints.md`：現行資料與 Application Service 契約。
5. `05-UI-UX/screens.md`、`07-Statistics/statistics.md`：畫面與查詢輸出規格。
6. `08-Development/`：實作狀態、待補功能、驗收證據與開發順序；它不會覆寫產品規則。
7. `09-Deployment/`、`10-AI-Coding-Rules/`：部署及開發紀律。

第三方套件的 `LICENSE.md`、repository 的 `AGENTS.md` 與一般 README 不屬於產品規格，不受上述內容合併規則影響。

若文件內容無法依上述順序消除歧義，停止實作並由使用者確認，不得同時保留互斥規格。

## GitHub 同步原則

GitHub repository 是程式碼與本文件集的共享版本來源。Codex 本地 checkout 不會因綁定 GitHub 帳號而自動同步。

每次變更必須：

1. 從 GitHub 預設分支建立 `codex/<short-description>` 分支。
2. 只處理一個可獨立審查的主題。
3. 完成建置、測試與文件一致性檢查。
4. 建立 commit、推送分支並開啟 draft Pull Request。
5. 驗證完成後才將 PR 轉為 ready；合併後才是其他環境可取得的正式版本。

不得提交密碼、Token、API Key、SQLite 資料庫、Data Protection keys、`data/`、建置輸出或使用者設定。

## 核心產品

這是一個手機／平板優先的戰鬥陀螺賽事與戰績工具，包含：

- 帳號、Cookie 登入與個人陀螺管理。
- 以站內邀請建立的快速對戰。
- 單人與 Tournament-scoped 團體賽。
- 單淘汰、雙敗、單循環與瑞士輪。
- Battle、Round、事件、修正紀錄及來源／B／X Side 戰績。

技術固定為 .NET 10、ASP.NET Core Razor Pages、Bootstrap、Vanilla JavaScript、SignalR、EF Core、SQLite、Docker 與主機側 Cloudflare Tunnel。不採 SPA、獨立 API Server、獨立 DB Container 或 Redis；即時事件只負責通知狀態已變更，所有狀態與規則仍由 Server 與資料庫決定。

## 全域有效規則

- 快速對戰固定每人三顆、`ScoreToWin = 4`；Tournament 依建立時鎖定的 RuleSet 使用 4、5、6 或 8 分。
- 分數只保存為 `Battle.SideAScore`／`SideBScore`；`SideADesignation` 表示資料 Side A 是 B Side 或 X Side，另一側必為相反站位。
- `Player A/B` 是資料配對方向，`B/X Side` 是開賽前鎖定的站位，兩者不可混用。
- 轉停 1 分、擊飛 2 分、爆裂 2 分、極限 3 分；第二次有效發射失誤使對手得 1 分且 fault count 歸零。
- 達 `ScoreToWin` 只進入 `VictoryPendingCompletion`，必須由授權裁判明確完成。
- 正常勝利方式以單一交易記錄結果並完成該 Round，不再要求逐局第二次確認；修改較早 Round 時，所有後續 Round 保留稽核資料但失效並從下一站位重開。
- 同一 Battle 的重排只能使用首次鎖定的陀螺，保存新 `SequenceNo`，分數不歸零。
- `Account` 與 `DisplayName` 均使用去除前後空白、英文不分大小寫的正規化唯一值；玩家搜尋只顯示 DisplayName 與內部 UserId。
- 快速對戰取消會交易式硬刪除 aggregate；Tournament 取消、棄權或撤銷必須保留規範要求的歷史與 audit。
- 戰績由 Battle、Round 與有效 Event 即時計算，不建立 Statistics Table。
- 快速、Tournament 個人、Tournament 團體隊伍結果與團體實際小局分開計算；B／X Side 可獨立篩選與排序。

## 已淘汰且不得重新引入

- 接受邀請前先建立 `Battle`，或把 InvitationPending／Rejected 當成快速 Battle 狀態。
- 由建立者替對手選陀螺，或以單一表單同時提交雙方 Lineup／Reorder。
- 以 `PlayerAScore`／`PlayerBScore` 作為資料庫正式欄位。
- 保存 `BSidePlayerId`、`XSidePlayerId` 或 `ForfeitedPlayerId`；站位與棄權方由現行 Side／Winner／Tournament Match 資料推導。
- 假設所有 Tournament 都是三顆 4 分制。
- 對所有 Battle 一律硬刪除取消資料；硬刪除只適用快速對戰取消。
- 把 Tournament-scoped 臨時隊伍誤當成永久社群 Team 功能。

目前實作與有效規格之間的差距只記錄在 `08-Development/development-plan.md`，不得以舊文件內容填補。
