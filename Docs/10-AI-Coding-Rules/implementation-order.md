# AI Coding Agent 現行執行順序

專案基礎、核心 Battle、Tournament schema／schedule 與 Statistics 已存在。不得再從空專案 Step 1 重新建立，也不得用舊 Draft／雙邊 Lineup 流程覆蓋現行功能。

詳細範圍、測試與建議 PR 名稱以 08-Development/development-plan.md 為準。固定順序如下：

## Step 0：整理與保護現有變更

- 確認 codex/* branch、dirty worktree 及 migration 相依性。
- 文件一致化獨立 commit。
- 將既有大型變更切成可審查的 coherent commits／draft PR。
- 完整測試通過後才新增下一項功能。

## Step 1：快速對戰返回與舊契約清理

- active Battle 清單與狀態導向。
- 移除產品／測試對 Draft、建立者雙邊 Lineup／Reorder 方法的依賴。
- 驗證所有權與終止狀態排除。

## Step 2：Tournament 報名生命週期

1. [已完成] 主辦方 Tournament participant invitation。
2. [已完成] 接受／拒絕、容量保護與唯一 Entry；最後名額的雙 DbContext 壓力測試已於 Step 7 完成。
3. [已完成] 個人、整隊、系統配隊一致的 ReopenRegistration。

## Step 3：No-show 與 WaitingForMe

- [已完成] 主辦方二次確認未到 Walkover，限制在仍有 Pending 必要選手的確定 Entry。
- [已完成] 不建立虛構 Battle／比分，且只推進一次。
- [已完成] WaitingForMe filter、精確待辦、優先排序、列表完整摘要與身份按鈕。

## Step 4：公開賽程與 polling

- [已完成] 專用 public details read model，不載入 private submission。
- [已完成] 完整賽程、勝方、比分、Side、實際玩家及已公開陀螺。
- [已完成] Cancelled Tournament 保留取消前合法完成資料。
- [已完成] List／Details／Match 的 GET-only token polling 與手動刷新。

## Step 5：正式排名

1. [已完成] 單淘汰／雙敗名次。
2. [已完成] 循環／瑞士必要加賽。
3. [已完成] 防止仍需冠軍加賽時提前 Completed。

## Step 6：UI／UX、瀏覽器與 Web integration

- 全站 responsive 視覺、導覽、表單、表格、狀態及空狀態一致化。
- 桌面／手機逐頁操作及 Register、Login、Logout、Settings 完整流程。
- 多帳號驗收 Quick Battle、Tournament 私密資料、Side、polling 與完成流程。
- Authentication、authorization、anti-forgery、private/public data integration tests。

## Step 7：併發與壓力測試（已完成）

- [已完成] 兩個獨立 DbContext 的最後名額競爭，只允許一個成功。
- [已完成] Round／Tournament Battle／Match 重複 HTTP POST 不重複計分、建立 Round、推進或通知。
- [已完成] 所有賽制容量上限、List／Details／Match polling 與高頻 Battle event 寫入壓力測試。

## Step 8：部署安全與實測

- ForwardedHeaders、可信 proxy／network、Secure Cookie。
- Docker build、migration、restart、SQLite／keys persistence。
- backup／restore、LAN、Cloudflare Quick Tunnel。

## 每一步固定檢查

```powershell
dotnet test BeybladeXRANK-main/BeybladeRecordSystem.slnx
```

- 更新 acceptance-tests.md。
- 新增／修改規格時同步所有受影響 Markdown。
- git diff --check。
- draft PR 保持單一主題，驗證完成才 ready。
