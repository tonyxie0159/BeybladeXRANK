# Beyblade Record System

手機／平板優先的戰鬥陀螺快速對戰、賽事與戰績工具，使用 ASP.NET Core Razor Pages、EF Core、SignalR 與 PostgreSQL。

現行產品與工程規格入口是 [`../Docs/README.md`](../Docs/README.md)；開發流程與待補驗收記錄於 [`../Docs/08-Development/development-plan.md`](../Docs/08-Development/development-plan.md)。本 README 只說明執行與驗證方式，不覆寫規格。

## Docker 執行

```powershell
Copy-Item .env.example .env
# 將 .env 內的 POSTGRES_PASSWORD 改成長隨機密碼
docker compose up -d --build
```

開啟 <http://localhost:8080>。PostgreSQL 18 資料保存在具名 volume `beybladexrank-postgres-data`；Data Protection keys 繼續保存在 Git 忽略的 `data/keys/`。

Compose 會先等待 PostgreSQL healthcheck，再由一次性的 `migrate` service 套用 EF Core migration 並冪等匯入 279 個零件；Web Application 本身不持有 schema 變更責任。不要對有正式資料的環境執行 `docker compose down -v`。

## 從 SQLite 搬移既有資料

先停止舊版應用程式，確認 `data/beyblade.db` 不再被寫入，再複製一份備份。資料搬移工具只接受已套用至 `20260901161104_RepairLegacyIdentityConflicts` 的 SQLite，並以唯讀方式開啟來源檔。

```powershell
New-Item -ItemType Directory -Force data/backups/postgresql-cutover
Copy-Item data/beyblade.db data/backups/postgresql-cutover/beyblade.db

docker compose up -d db

$target = "Host=localhost;Port=5432;Database=beyblade;Username=beyblade;Password=<與 .env 相同的密碼>;GSS Encryption Mode=Disable"
dotnet run --project tools/BeybladeRecordSystem.DataMigration -- `
  --source data/backups/postgresql-cutover/beyblade.db `
  --target $target `
  --confirm-empty-target

docker compose run --rm migrate
docker compose up -d app
```

工具會先執行 SQLite `integrity_check` 與 `foreign_key_check`，拒絕非空或不相符的 PostgreSQL schema，保留所有主鍵／外鍵，在單一交易內匯入，校正 identity sequence，並核對 17 張應用資料表的筆數與所有純量欄位。匯入錯誤會 rollback 全部資料列（已建立的空 schema 可能保留）；來源 SQLite 不會被修改。

正式切換前另需保存 SQLite 的 `.db`、可能存在的 `.db-wal`／`.db-shm` 與 `data/keys/`。如果來源仍有 WAL，應先讓舊版應用正常停止並完成 checkpoint，不能只複製主 `.db`。

## 不使用 Docker 的本機執行

先啟動可連線的 PostgreSQL、套用 migration，再提供連線字串：

```powershell
$env:ConnectionStrings__DefaultConnection = "Host=localhost;Port=5432;Database=beyblade;Username=beyblade;Password=<password>;GSS Encryption Mode=Disable"
dotnet run --project src/BeybladeRecordSystem -- --migrate
dotnet run --project src/BeybladeRecordSystem --urls http://localhost:8080
```

## PostgreSQL 備份

具名 volume 是持久化空間，不是備份。定期將 dump 寫到 volume 外：

```powershell
New-Item -ItemType Directory -Force data/backups
docker compose exec -T db pg_dump -U beyblade -d beyblade --format=custom --file=/tmp/beyblade.dump
docker compose cp db:/tmp/beyblade.dump data/backups/beyblade-postgresql.dump
docker compose exec -T db rm -f /tmp/beyblade.dump
```

還原必須先在拋棄式資料庫演練並核對資料，再操作正式資料庫。

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

`docker compose config` 需要 `.env`。它只驗證 YAML；image build、PostgreSQL migration、資料搬移、restart persistence、備份還原、LAN 與 Tunnel 必須另行實測。
