# 驗收與證據狀態

## 狀態定義

- [x]：目前已有對應自動測試，且最近一次完整測試通過。
- [ ]：有效需求仍待實作、補自動測試或完成指定人工驗收。

最近一次基準（2026-08-23）：

```powershell
dotnet test BeybladeXRANK-main/BeybladeRecordSystem.slnx
```

結果：155 passed、0 failed、0 skipped。現有測試包含 Domain／Service／Persistence／PageModel、HTTP 與 SignalR 整合測試，仍不能代替兩支實機手機或實際部署驗收。

## 已有自動證據

### Account／Beyblade

- [x] 註冊保存 PasswordHash、拒絕重複 Account 並可驗證登入。
- [x] Account 與 DisplayName 使用 trim、英文不分大小寫的正規化唯一索引；修改 DisplayName 也套用相同規則。
- [x] Login／Register 驗證失敗後按鈕恢復，可立即再次提交；登入後 Layout 載入通知鈴鐺、提示區與 SignalR client。
- [x] Beyblade CRUD 限制在擁有者，Delete 採軟刪除。
- [x] 同一 User 名稱不可重複，不同 User 可同名。
- [x] Rename 維持同一 BeybladeId，Lineup／Round 保存 Snapshot。

### 快速邀請與 Lineup

- [x] 不可與自己對戰；只有受邀者可接受／拒絕，只有發起人可撤回。
- [x] 拒絕／撤回會硬刪除 invitation，接受前不建立 Battle。
- [x] 接受時建立持久化 Quick Battle 並刪除 invitation。
- [x] 玩家只能提交自己的三顆不同 Beyblade。
- [x] 雙方都提交前保持私密，之後才同時公開。
- [x] 每人每個 Sequence 最多一次 edit request；接受會使雙方重新提交，拒絕保留原版本。
- [x] 雙方確認後物化 Lineup；必須明確指定 B／X Side 才能開始。
- [x] Reorder 只能使用首次鎖定陀螺，雙方私密提交後才生效並保留比分／歷史。
- [x] Home／快速邀請頁可列出所有準備中、InProgress、ReorderSelection、VictoryPendingCompletion Quick Battle，並導向正確目的頁。
- [x] 非參與者、Tournament Battle 與終止狀態不會出現在 active quick battle 查詢。
- [x] BattleService 的 Draft、雙邊 Lineup 與雙邊 Reorder 舊契約已移除，既有規則測試改走 QuickBattleFlowService。

### 計分、棄權、取消與 Revision

- [x] SpinFinish = 1、KnockOut = 2、Burst = 2、Extreme = 3。
- [x] 快速 4 分與 Battle-specific ScoreToWin 都由 Server 判定。
- [x] 達門檻只進入 VictoryPendingCompletion，必須明確 Finish。
- [x] 第一次 LaunchFault 不得分；第二次建立對手 +1 Penalty 並重設該顆 fault。
- [x] LaunchFault／Penalty 不完成 Round。
- [x] 完成一組且未達門檻才可重排。
- [x] 快速棄權保留完成 Round、排除當前未完成 Event，並限制操作者。
- [x] 快速取消需要確認，交易式硬刪除 aggregate 且不進統計。
- [x] Revision 保存 audit、重算比分／狀態，能截斷及恢復門檻後事件。
- [x] Tournament Revision／Void 依下游狀態重建、要求確認或阻擋。

### Tournament 資料、賽程與對局

- [x] 個人賽主辦方可依唯一 DisplayName 模糊搜尋並提交 UserId 發送 Tournament invitation；接受才建立／恢復 Entry，拒絕保留歷史。
- [x] 手動先報名、額滿或關閉報名會使不再有效的 pending invitation 進入 Invalidated。
- [x] invitation WaitingForMe 篩選只顯示登入者待處理邀請，非受邀者不可回覆。
- [x] 個人、整隊與系統配隊使用同一 ReopenRegistration；只限主辦方及正式開始前操作。
- [x] Reopen 會交易式清除未開始 Match／SchedulePosition、重算 Open／CapacityReached，且不復活已失效邀請。
- [x] RuleSet、TeamSize、BeybladesPerPlayer、ScoreToWin 與賽制上限由 Server catalog 推導。
- [x] 個人報名拒絕重複並在 sequential capacity 測試停止於額滿。
- [x] 整隊全員接受後才正式占名額，接受其中一隊會使其他 team invitation 失效。
- [x] 代表人轉讓、成員退出、名額釋放與重新組隊資料規則。
- [x] 系統配隊只產生完整隊伍，支援等待補足、重新開放及合法交換成員。
- [x] 單淘汰 N-1、Bye／種子資格賽及下游來源。
- [x] 雙敗固定勝／敗部映射、第二敗淘汰與 conditional Reset Final。
- [x] 單循環完整配對。
- [x] 瑞士輪數、首輪隨機、避免可避免重賽及 Bye 規則。
- [x] 賽程草稿保存、調整 Entry 位置、退出使草稿失效及正式開始鎖定。
- [x] 每個 Tournament 的 service flow 一次只啟動第一個 ready Match。
- [x] 個人與所有團體 RuleSet 的私密 Lineup、實際 Player／Beyblade 配對及重排。
- [x] 拒絕出賽形成 Walkover；進行中團體任一成員棄權使整個 Entry 判負。
- [x] 主辦方可二次確認手動判定仍有選手未回覆的 Entry 為 No-show Walkover；非主辦方、錯誤 Entry、已回覆 Entry、錯誤狀態與重複操作均被拒絕。
- [x] 個人與團體 No-show 都只推進一次、不建立 Battle，且不產生個人／陀螺／Side 假戰績。
- [x] Tournament List WaitingForMe 精確涵蓋邀請、組隊、Match 與裁判操作，待辦優先排序並回傳完整摘要、身份及操作目的地。
- [x] 專用 PublicDetails read model 顯示完整賽程、Winner／Loser、合法比分、Side、實際玩家與已公開陀螺，且 spectator 不能開啟 private Match workspace。
- [x] 任一方尚未完成私密提交時，PublicDetails 不回傳 Battle 或 Lineup；全部物化後才同步公開 Snapshot。
- [x] Cancelled Tournament 公開資料保留取消前完成 Match／Battle／比分，未準備完成的對局不產生公開 Battle。
- [x] List／Details／Match polling handler 為授權後的 GET-only 最小 JSON 查詢，不修改 Tournament／Match／Battle 或重送 POST。
- [x] 已驗證使用者可用 Cookie 連線 `/hubs/realtime` 私人群組，快速邀請寫入後立即收到通知事件。
- [x] 指定 B／X Side 的即時事件會讓雙方留在 Setup；建立第一 Round 後才一起導向 Battle。
- [x] 快速與賽事邀請的接受、拒絕、取消、額滿或關閉造成的失效通知具防重複鍵；通知與業務狀態同交易保存，提交後才推送。
- [x] Tournament 取消保留完成 Round、排除當前 Event，並保存原因／報名／賽程。
- [x] Void／Reopen 保留舊 Battle audit、排除統計並建立乾淨替代流程。
- [x] 循環與瑞士既有 tie-break 計算可重現；Bye／Walkover 不虛構比分。

### Statistics／Persistence

- [x] 快速、個人、團體隊伍結果與團體實際小局分開。
- [x] 玩家與陀螺 B／X Side 勝敗、勝率、篩選與排序。
- [x] 陀螺來源篩選、樣本數、得失分、平均值、ResultType 與 LaunchFault。
- [x] 對手／對手陀螺使用實際團體小局對手。
- [x] 歷史顯示來源與 Side，取消／Voided 規則正確排除。
- [x] Migration 可建立現行表、相容資料 backfill，且 EF Model 無 pending migration。
- [x] Tournament／Match／Participant Version 已配置 concurrency token。
- [x] runtime SQLite 路徑與 Data Protection key 目錄落在統一 data directory。

## 最近完成的 P1 流程

- [x] 單循環／瑞士完成固定 tie-break 後若冠軍仍完全同分，自動建立平衡 Playoff、保持 InProgress，完成後只覆寫冠軍且不改寫例行排名統計。
- [x] 單淘汰依決賽／淘汰輪次、雙敗依決勝 Grand Final／第二敗階段產生正式名次與並列，且完成前不提前公布。

## 有效需求但尚未完成

### P2 UI／UX 與 Web functional 證據

- [x] Register、Login 與主要登入頁 HTTP／瀏覽器基本操作；驗證失敗按鈕恢復與主要 Layout 資源已覆蓋。
- [ ] Logout、Settings 修改唯一玩家名稱以實際瀏覽器完整操作。
- [ ] 所有登入後主要頁面以桌面與手機尺寸通過導覽、可讀性、基本表單與水平溢出檢查。
- [ ] 快速對戰與 Tournament 以多帳號完成主要流程。
- [x] 建立 Razor Pages authentication／authorization／anti-forgery integration tests，並以兩個獨立 Cookie client 建立快速對戰至可計分頁。
- [x] 首頁功能卡不顯示編號、鈴鐺位於收合導覽外；對戰紀錄只有一個返回入口，結算頁顯示勝方、最終比分及回到首頁。
- [ ] 建立私密 Lineup 不外洩及公開 Tournament read model 的 integration tests。

### 已延後的併發與壓力證據

- [ ] 使用兩個獨立 DbContext 模擬最後名額並行報名，只允許一個成功。
- [ ] 對完成 Round／Battle／Match 的重複 HTTP POST 驗證不重複計分、晉級或通知。
- [ ] 容量、長時間 SignalR／polling 與高頻寫入壓力測試。

以上項目依目前產品優先順序延後，不阻擋 UI／UX 與一般使用者功能驗收。

## 人工與部署驗收

2026-08-20 UI 本機瀏覽器證據：公開首頁、Login 與登入後首頁可正常載入；登入後主導覽的 Home、Battles Create／Invitations、Beyblades Index／Create、Tournaments Index／Create、Statistics Index、Account Settings、Privacy 共 10 條 GET 路徑均有正確標題、無頁面錯誤、無桌面版水平溢出，且瀏覽器 console 無 error／warning。Register POST 以獨立測試資料目錄及有效 anti-forgery token 驗證成功。手機 viewport 套用與 Logout POST 遭本機瀏覽器控制安全層阻擋，仍保留未驗收狀態。

- [ ] 快速邀請、私密提交、edit request、Side、計分、重排、Revision、棄權與取消以兩個帳號操作。
- [ ] Tournament 個人、雙人六顆／四顆、三人 4／5 分制以多帳號完成至少一場。
- [ ] 手機與平板尺寸不水平溢出，重要裁判按鈕可安全操作且不只依顏色傳達狀態。
- [ ] Docker image build 成功，compose 啟動後 migration 完成。
- [ ] Container restart 後 SQLite 與登入 Cookie keys 仍持久化。
- [ ] SQLite／keys bind mount 與停止容器後備份／還原成功。
- [ ] 同網路其他裝置可連線。
- [ ] Cloudflare Tunnel HTTPS 可連線，forwarded headers 與 Secure Cookie 行為正確。
- [ ] Quick Tunnel 僅標示短期分享；正式 named tunnel 的 host／proxy allow-list 經實機設定。
