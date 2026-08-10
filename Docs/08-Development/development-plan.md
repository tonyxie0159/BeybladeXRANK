# 兩天開發流程

## Phase 0：建立專案

1. 建立 .NET 10 ASP.NET Core Razor Pages。
2. 安裝 EF Core SQLite。
3. 建立測試專案。
4. 建立 Git repository。
5. 確認 `dotnet build` 成功。

驗收：空專案可啟動。

## Phase 1：Database

1. 建立 Entity。
2. 建立 DbContext。
3. 建立 EF Core Migration。
4. SQLite 建庫。
5. 建立必要索引與 FK。
6. 建立 Seed / Development user（僅開發環境）。

驗收：migration、startup、CRUD 基本成功。

## Phase 2：Authentication

1. 建立註冊。
2. 密碼 Hash。
3. Cookie Authentication。
4. Login / Logout。
5. DisplayName 修改。

驗收：未登入不能進入需要登入的頁面。

## Phase 3：Beyblade

完成：

- List
- Create
- Rename
- Delete

驗收：

- 同一 User 不可重複。
- 不同 User 可相同。
- Rename 不影響 Id。

## Phase 4：Battle Setup

完成：

- 選對手。
- 選三顆陀螺。
- 排列。
- 鎖定。

驗收：

- 不能選自己。
- 每位玩家恰好三顆。
- 同一玩家不能重複陀螺。
- Lock 後不能換。

## Phase 5：Battle Engine

依序完成：

1. 建立 Round。
2. LaunchFault。
3. LaunchFaultPenalty。
4. BattleResult。
5. 累計分數。
6. >=4 判定。
7. VictoryPendingCompletion。
8. Finish Battle。
9. 三局後重新排列。

這一階段必須先寫 Unit Tests，再接 UI。

## Phase 6：Round Revision

完成：

- 查看該局全部事件。
- 修改 BattleResult。
- 重新計算該局。
- 重新計算 Battle。
- 儲存 Revision。

## Phase 7：Statistics

完成：

- 玩家戰績。
- 陀螺戰績。
- 對手戰績。
- 對手陀螺戰績。
- 得分 / 失分 / 勝率排序。
- 發射失誤失分統計。

## Phase 8：Mobile UI

最後再處理：

- Bootstrap responsive。
- 大型裁判按鈕。
- 防止誤觸的確認。
- Sticky score。
- Battle event history。

## Phase 9：Docker

1. Dockerfile。
2. compose.yaml。
3. SQLite bind mount。
4. health check。
5. local test。

## Phase 10：Cloudflare Tunnel

本機確認：

`http://localhost:<port>`

再使用 Cloudflare Tunnel 對外。

## Phase 11：Final Acceptance

依 `acceptance-tests.md` 全部測試。

