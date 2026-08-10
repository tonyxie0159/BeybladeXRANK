# Beyblade Record System

手機／平板優先的戰鬥陀螺 1v1 戰績工具，使用 ASP.NET Core Razor Pages、EF Core 與 SQLite。

## 本機執行

```powershell
dotnet run --project src/BeybladeRecordSystem --urls http://localhost:8080
```

開啟 <http://localhost:8080>。首次啟動會自動套用 EF Core migration，資料庫位於 `src/BeybladeRecordSystem/data/beyblade.db`。

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
