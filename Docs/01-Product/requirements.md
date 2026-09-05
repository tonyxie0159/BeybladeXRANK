# 產品需求規格

本文件定義全產品共通需求。快速對戰的完整狀態見 `04-Battle-Rules/state-machine.md`；Tournament 模式、賽制與團體對戰細節見 `11-Tournament-Schedule/tournament-schedule.md`。

## 1. 帳號與所有權

每位使用者具有：

- `Account`：唯一、不可修改的登入識別。
- `PasswordHash`：只保存 ASP.NET Core PasswordHasher 產生的雜湊，不保存明文。
- `DisplayName`：唯一且可修改的顯示名稱，不作為資料所有權識別。

`Account` 與 `DisplayName` 都以去除前後空白、英文不分大小寫的正規化值檢查唯一性。玩家搜尋、邀請與公開頁只顯示 DisplayName 及內部 UserId，不公開 Account。

使用者只能管理自己的帳號顯示名稱、Beyblade、Battle 操作及統計查詢範圍。所有 PageModel 與 Service 都必須重新驗證登入者、資源所有權及目前狀態，不信任 hidden field、route 或 query string。

## 2. 陀螺

使用者可以查看、新增、改名及刪除自己的陀螺。

現行資料保存規則：

- 同一 User 的 Name 不可重複，不同 User 可以同名。
- `BeybladeId` 是同一玩家同上蓋名稱的母陀螺識別。完整零件組合不同時新增配置版本；CX 超越／輔助戰刃改變但上蓋命名不變，也歸入同一母陀螺。相同組合重用原版本，上蓋名稱改變才新增陀螺。
- Delete 採軟刪除，避免破壞既有 Battle、Lineup 與 Round。
- 軟刪除後不出現在新 Lineup 選擇清單，但歷史資料仍可依 Id 與 Snapshot 顯示。
- BattleLineup 與 BattleRound 保存玩家及陀螺顯示名稱 Snapshot。
- 新增陀螺必須選齊零件；編輯頁可補登第一份完整配置或新增版本。出戰先選陀螺再選版本，保存當場版本；重排沿用原配置。舊對戰不因補登而取得零件配置。
- 陀螺總戰績彙總所有版本，點進詳情再分版本；未記錄配置的舊戰績另列「未記錄版本」。
- 零件目錄按類別＋名稱去重，支援一般、CX 三件式／四件式及固鎖一體式結構。已登錄配置的陀螺在快速對戰與 Tournament 提交時，會按同側全隊 `PartId` 拒絕重複零件；對手可使用相同零件。詳見 [零件系統](../03-Database/parts-system.md)。

## 3. 快速對戰建立流程

1. 發起人選擇不同於自己的玩家並建立 `QuickBattleInvitation`。
2. 接受邀請前不得建立 Battle；拒絕或撤回時硬刪除 invitation，不建立 Rejected／Cancelled Battle。
3. 接受時在同一交易建立 `Battle(SourceType = Quick, ScoreToWin = 4)` 並刪除 invitation。
4. 雙方各自在自己的帳號私密選擇三顆不同陀螺及 1、2、3 順位，任一方不得替另一方提交。
5. 雙方都提交後才同時公開。雙方各自確認，或每人每個 Lineup 版本至多提出一次重新編輯請求。
6. 接受重新編輯時建立下一個 `SequenceNo` 並使雙方重新提交；拒絕時維持原版本，提出者不得在同版本再次要求。
7. 雙方確認後物化並鎖定 BattleLineup，由發起人以臨時裁判身分明確指定資料 Side A 為 B Side 或 X Side，再開始對戰。

站內通知、待處理邀請、準備中與進行中的快速對戰都必須有可返回入口。流程狀態、私密提交、比分、Round、Event 與 fault count 全部保存於後端；刷新、登出或瀏覽其他頁面不得重建或重置 Battle。SignalR 在交易成功後即時通知相關玩家，斷線重連、頁面回到前景及低頻輪詢會向 Server 補齊狀態。

## 4. Battle 共通模型

- `Player A/B` 表示資料中的配對方向，不等於 B／X Side。
- `SideADesignation` 表示 Side A 的正式站位；另一側為相反站位。
- 比分使用 `SideAScore`／`SideBScore`。
- 快速對戰 `ScoreToWin = 4`；Tournament Battle 使用 Tournament 建立時鎖定的門檻。
- 開始後 Side、參賽者、首次陀螺集合與賽前資料不可任意修改。

當前 Lineup 依 Position 順序產生 Round。快速對戰固定三個 Position；Tournament 依 RuleSet 產生所需實際玩家／陀螺配對。

一組 Position 全部完成且雙方仍未達門檻時：

- 雙方各自私密重排首次鎖定的陀螺，不可更換。
- 團體 Battle 另由代表人私密重排本隊出戰者順序。
- 全部必要提交完成後直接物化新 `SequenceNo`，不再進行 LineupReview。
- 累積比分與舊 Lineup／Round 歷史保留。

## 5. 計分與發射失誤

| ResultType | 中文名稱 | 分數 |
|---|---|---:|
| SpinFinish | 轉停勝利 | 1 |
| KnockOut | 擊飛勝利 | 2 |
| Burst | 爆裂勝利 | 2 |
| Extreme | 極限勝利 | 3 |

Client 只提交事件意圖；`ScoreAwarded`、累積比分、Round 狀態與 Battle 狀態由 Server 決定。

對同一 Round、同一顆陀螺：

- 第一次有效 LaunchFault 不得分。
- 第二次有效 LaunchFault 建立 `LaunchFaultPenalty`，對手得 1 分，該顆陀螺的 fault count 歸零。
- LaunchFault 與 LaunchFaultPenalty 都不會自行完成 Round；正常完成仍需一個有效 BattleResult。
- fault count 從有效事件重建，不另存可漂移的計數欄位。

點擊正常勝利方式時，Server 在同一交易記錄 BattleResult、完成目前 Round、重新計分並在合法時建立下一 Round，不再要求逐局額外確認。每次有效得分後立即檢查 `ScoreToWin`；達標時進入 `VictoryPendingCompletion`，禁止新增後續正常得分，授權裁判檢查後明確完成整場 Battle。

## 6. 棄權、取消、撤銷與修正

### 快速對戰

- 發起人可在進行中指定棄權玩家；另一方成為勝者，不要求達到 4 分。
- 已完成 Round 保留並計入戰績；當下未完成 Round 的事件失效。
- 取消須二次確認並在單一交易硬刪除整個 Battle aggregate，不保留 Cancelled 紀錄，也不進入統計。

### Tournament

- 賽前拒絕、未到或棄權產生 Walkover，不建立虛構比分 Battle。
- 進行中棄權保留已完成 Round，排除未完成 Round 事件，整個 Entry 判負。
- 取消整個 Tournament 保留規則、報名、賽程、已完成 Battle／Round 與原因；未完成資料進入 Cancelled 終止狀態並排除統計。
- 主辦方撤銷本場並重開時，舊 Battle 進入 Voided 且保留操作者、時間、原因與快照；替代 Battle 必須重新通知與選螺。

### Revision

- 可查看指定 Round 的全部有效／無效事件並重新指定唯一有效 BattleResult。
- Reason 必填；保存 Round 與全場修改前後快照。
- 修改較早 Round 時，所有後續 Round 與事件保留原始資料並標記為 `EarlierRoundRevision` 失效，不直接刪除；從修改後的下一站位重新建立有效流程。
- 從最早受影響事件重播整場；若修改後已達門檻，直接進入整場結束確認且不建立後續 Round。
- Tournament 上游勝方變更必須依下游狀態重建、取得明確撤銷確認或阻擋修改。

## 7. Tournament

產品支援：

- 單人賽、雙人團體、三人團體。
- 整隊報名及個人報名後系統配隊；隊伍只存在於該 Tournament，不建立永久 Team。
- 單淘汰、雙敗、單循環與瑞士輪。
- 每場新 Tournament Match 重新選螺；同一 Battle 內重排不得換螺。
- 主辦方邀請、報名重開、未到場手動判定、賽程觀戰、同分加賽及正式名次均屬有效需求。

上述完整規則及操作上限以 `11-Tournament-Schedule/tournament-schedule.md` 為準。

## 8. 戰績

- 玩家主戰績分成快速對戰、Tournament 個人、Tournament 團體隊伍結果及團體實際小局，不合成單一主要勝率。
- 陀螺戰績預設合併有效來源，並可依來源篩選。
- 個人與陀螺都可依全部／B Side／X Side 重新聚合，並依勝率、得分、失分、得失分差、B Side 勝率與 X Side 勝率排序。
- 對戰歷史顯示來源及當場 Side；未保存 Side 的相容資料不得推測。
- 不建立冗餘 Statistics Table 或全域強弱排行榜。

## 9. 執行與部署

- PostgreSQL 保存於 Docker named volume；Data Protection keys 與 cutover 備份保存於 Git 忽略的 `data/`。
- 本機及 Docker 使用 8080；Docker 只將 `/app/data/keys` bind mount 到主機 `data/keys/`。
- Cloudflare Tunnel 由主機側連到 `http://localhost:8080`；正式公開前必須完成 forwarded headers、Secure Cookie 與實機連線驗收。
- Quick Tunnel 只用於開發／短期分享，不宣稱正式 SLA。
