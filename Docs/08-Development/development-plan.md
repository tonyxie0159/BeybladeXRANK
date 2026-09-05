# 開發工作流程與待辦

本文件只定義現行開發方式與尚待補齊的驗收範圍。產品行為以 `Docs/README.md` 列出的規格文件為準；完成證據以 `acceptance-tests.md` 為準。

零件目錄、完整組裝、通用名稱與搜尋選單的開發參考集中於 [parts-system.md](../03-Database/parts-system.md)。2026-09-05 已確認同上蓋多版本、CX 隱含組件的版本歸屬，以及先選陀螺再選版本流程；實作與驗收依該文件進行。

專案不再使用封測批次、Phase 1–8 或固定 Step 0–8 作為開發順序。GitHub issue 與 Pull Request 是工作範圍及優先順序的唯一來源；沒有對應 issue 的歷史清單不得自動恢復成待辦。

## 文件分工

- `01-Product` 至 `07-Statistics` 與 `11-Tournament-Schedule`：產品、資料與介面規格。
- `08-Development/acceptance-tests.md`：已驗證能力與尚缺證據。
- `09-Deployment`：Docker、PostgreSQL、備份與對外連線。
- `10-AI-Coding-Rules/agent-rules.md`：實作時不可突破的架構、安全與資料完整性規則。
- 本文件：從 issue 到 draft PR 的共同工作流程，以及仍可建立 issue 的驗收缺口。

歷史封測修正計畫與已結案 QA 清單已從有效文件移除；其中仍成立的產品規則及驗收證據已併入上述文件。

## 工作來源與範圍

1. 從 GitHub issue 或使用者明確需求確認目標、非目標與驗收條件。
2. 一個 branch／PR 只處理一個可獨立審查的主題。
3. 若需求與有效規格衝突，先列出資料與相容性影響，取得確認後再修改規格及程式。
4. 不以歷史程式碼、已刪除文件或暫時缺少測試為理由恢復舊流程。

## 標準開發流程

### 1. 準備

- 確認目前 branch、Git 狀態與未提交變更，不覆寫其他工作的內容。
- 不在 `main` 開發；從正確基準建立 `codex/<short-description>`。
- 閱讀受影響的產品規格、資料契約、驗收狀態及 GitHub issue。
- 先界定需要新增或更新的 regression test；涉及資料時先確認備份與 migration 路徑。

### 2. 實作

- 優先做最小且完整的垂直變更，不夾帶無關重構。
- Domain、authentication、authorization、persistence 與 migration 變更必須有 focused regression test。
- 保持 Account、Beyblade、Battle、Tournament 與 Statistics 的使用者所有權及私密資料邊界。
- Battle 計分、Round revision history、Lineup ordering 與 Tournament progression 屬相容性敏感行為；任何語意改變都要有明確 migration 或測試。
- UI 仍以 Server 驗證為準；Client 不得成為分數、狀態、晉級或所有權的權威來源。

### 3. 資料庫變更

- 正式 provider 只有 PostgreSQL 18／Npgsql；SQLite 只可用於既有 cutover 工具或隔離測試。
- 使用 EF Core migration 演進 schema，不以刪除 volume、清空資料庫或重建正式資料作為一般開發步驟。
- 產生 migration 後檢查 SQL、model snapshot、upgrade path、constraint、index、foreign key 與資料 backfill。
- 套用正式 migration 由一次性 Docker `migrate` service 負責，Web 一般啟動不得自行變更 schema。
- 任何資料搬移或修復先備份、在拋棄式目標演練、核對內容，再操作正式環境。

### 4. 驗證

至少執行：

```powershell
dotnet test BeybladeXRANK-main/BeybladeRecordSystem.slnx
git diff --check
```

依變更範圍追加：

- Razor Pages／authorization：HTTP integration test 與必要的瀏覽器操作。
- 手機介面：桌面與手機 viewport、觸控、可讀性及水平溢出檢查。
- migration／部署：PostgreSQL migration、Docker healthcheck、restart persistence 與 backup／restore 演練。
- 即時流程：兩個獨立登入工作階段、重新連線及唯讀同步備援。
- 併發敏感操作：獨立 DbContext、重複請求及 transaction 邊界測試。

驗證完成後更新 `acceptance-tests.md`；沒有實際證據的項目保持未完成。

### 5. 交付

- 檢查 diff 只包含 issue 範圍，且沒有 secret、`.env`、資料庫、dump、Data Protection keys 或建置輸出。
- 建立清楚的 commit，推送 `codex/*` branch 並開啟 draft PR。
- PR 說明包含行為變更、資料影響、migration／rollback 注意事項及驗證結果。
- CI 與人工驗收完成後才將 PR 轉為 ready；合併前不得宣稱其他環境已取得變更。

## 尚待建立或完成的驗收工作

以下是仍缺證據的範圍，不代表固定執行順序；是否開始與優先級由 GitHub issue 或使用者決定。

### UI 與完整網站流程

- Logout、Settings 與玩家名稱唯一性實機操作。
- 所有登入後主要頁面的桌面／手機導覽、表單、觸控與水平溢出檢查。
- 兩個獨立帳號完成快速對戰與主要 Tournament 流程。
- 補齊私密 Lineup 不外洩與公開 Tournament read model 的 HTTP integration test。

### 併發與可靠性

- 兩個獨立 DbContext 競爭最後一個 Tournament 名額。
- Round／Battle／Match 的重複 HTTP POST 不得重複計分、晉級或通知。
- 容量、長時間 SignalR／polling 與高頻寫入壓力測試。

### 部署與外部連線

- Data Protection keys 的獨立還原演練。
- 同網路其他裝置連線。
- Cloudflare Tunnel HTTPS、forwarded headers、可信 proxy 與 Secure Cookie。
- 正式 named tunnel 的 hostname、proxy allow-list 與操作文件。

詳細狀態只在 `acceptance-tests.md` 維護，避免在多份文件重複建立待辦清單。

## 完成定義

一項工作只有在下列條件全部成立時才完成：

1. GitHub issue 的驗收條件已滿足，且沒有擴張未核准範圍。
2. focused tests 與完整 solution tests 通過。
3. 資料、權限、隱私及相容性影響已有驗證。
4. 相關規格與 `acceptance-tests.md` 已同步，且沒有舊流程或互斥說法。
5. draft PR 內容可獨立審查，CI 與必要人工證據均可追溯。
