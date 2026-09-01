# Beyblade Record System

手機／平板優先的戰鬥陀螺快速對戰、賽事與戰績工具，使用 ASP.NET Core Razor Pages、EF Core、SignalR 與 SQLite。

第一次封測後的現行產品與工程規格，以 [`../Docs/08-Development/first-closed-beta-experience-fixes.md`](../Docs/08-Development/first-closed-beta-experience-fixes.md) 為準。README 只說明執行與驗證方式，不覆寫該規格。

## 本機執行

```powershell
dotnet run --project src/BeybladeRecordSystem --urls http://localhost:8080
```

開啟 <http://localhost:8080>。首次啟動會自動套用 EF Core migration，資料庫與登入金鑰位於專案根目錄的 `data/`；此目錄已由 Git 忽略。

## Docker

```powershell
docker compose up -d --build
```

開啟 <http://localhost:8080>。SQLite 資料會持久化在專案根目錄的 `data/beyblade.db`；備份時停止容器後複製該檔案即可。

## Cloudflare Quick Tunnel

確認 Docker 應用可在本機連線後執行：

```powershell
cloudflared tunnel --url http://localhost:8080
```

此指令會輸出一個臨時 HTTPS URL，適合測試與短期分享，不是正式 SLA 等級部署。

正式外部使用前仍須完成 forwarded headers、Secure Cookie、資料庫備份及多裝置瀏覽器驗收；產生 HTTPS URL 本身不代表部署已通過。

## 驗證

```powershell
dotnet test BeybladeRecordSystem.slnx
docker compose config --quiet
```

`docker compose config` 只驗證 YAML；image build、migration、restart persistence、LAN 與 Tunnel 必須另行實測。
