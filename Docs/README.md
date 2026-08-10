# BeybladeRecordSystem 開發文件

本文件集是 BeybladeRecordSystem 的唯一開發規格來源，供 AI Coding Agent 依序實作。

## 核心目標

在兩天內完成一個手機／平板優先的戰鬥陀螺 1v1 戰績工具。

技術方向：

- .NET 10 LTS
- ASP.NET Core
- Razor Pages
- Bootstrap
- Vanilla JavaScript（僅在必要處使用）
- Entity Framework Core
- SQLite
- Docker
- Cloudflare Tunnel

第一版刻意不採前後端分離、Blazor Server、React、Vue、獨立 API Server、獨立 DB Container 或其他非必要基礎設施。

## 開發原則

1. 需求完整性優先。
2. 最短可驗收路徑優先。
3. 所有戰鬥規則由後端服務判定。
4. 前端不可自行計算或信任分數。
5. 戰績由原始對戰資料查詢計算，不建立冗餘 Statistics Table。
6. 不自行增加需求。
7. 不為未提出的未來需求預留複雜架構。
8. 每完成一個階段即建置並測試。
9. 任何無法由需求明確推導的規則，不得自行決定；應停止並提出問題。

## 擴充功能規格

- [賽程功能規格與討論稿](11-Tournament-Schedule/tournament-schedule.md)：單人雙敗／單敗種子制，以及 2v2v2 循環／單敗制。

## 使用者已確認的關鍵規則

- 對戰由建立對戰的人負責全部操作。
- 雙方各選三顆陀螺並排列 1、2、3，雙方確認後不可更換。
- 依順位進行 1v1、2v2、3v3。
- 任一方累積達到或超過 4 分時，即取得勝利條件。
- 達成勝利條件後，不自動鎖定；操作人必須按「對戰結束」才正式完成並鎖定。
- 三顆打完若無人達到 4 分，可以重新排列三顆陀螺，但不能更換陀螺，分數繼續累計。
- 轉停 1 分、擊飛 2 分、爆裂 2 分、極限 3 分。
- 發射失誤是單一失分事件，不結束 BattleRound。
- 同一顆陀螺繼續對戰；同一顆陀螺的發射失誤累計達兩次時，對方得 1 分，失誤次數歸零。
- 發射失誤必須獨立記錄，並可統計個人歷史因發射失誤失掉的總分。
- 一個 BattleRound 最終可以同時包含勝負結果與發射失誤事件。
- 可以修改「該局」全部勝負紀錄，而非只修改上一筆。
- 修改後必須重新計算該局及整場對戰的有效分數與勝負狀態。
- 陀螺名稱對同一使用者不可重複；不同使用者可以使用相同名稱。
- User Account 與 Display Name 分離；Display Name 可修改。
- 使用者登入採帳號 + 密碼。
- 陀螺改名不改變陀螺身分；歷史對戰需保存名稱 Snapshot。
- SQLite 資料持久化於主機。
- 對外連線使用 Cloudflare Tunnel。
