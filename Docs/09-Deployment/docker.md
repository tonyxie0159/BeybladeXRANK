# Docker 部署

## 架構

Compose 包含 PostgreSQL、一次性 EF migration service 與 ASP.NET Core Web Application。

- container port：8080
- PostgreSQL port：僅綁定 localhost，預設 5432
- database volume：beybladexrank-postgres-data
- Data Protection keys：主機 `data/keys/` → `/app/data/keys`
- restart policy：unless-stopped

PostgreSQL 資料與登入金鑰必須在 container restart／recreate 後保留。具名 volume 不是備份，正式資料環境不得使用 `docker compose down -v`。

## 建置與啟動

```powershell
Copy-Item .env.example .env
# 修改 .env 的 POSTGRES_PASSWORD
docker compose up -d --build
```

`db` healthcheck 通過後，`migrate` service 套用 EF Core migration、冪等匯入零件目錄並成功結束，才啟動 `app`。Web Application 本身不在啟動時修改 schema。若從舊 SQLite 搬移，先用 DataMigration 工具完成空目標匯入，再執行 migrate service，避免零件預先匯入使目標不再為空。正式交付前必須確認：

1. image build 成功。
2. migration 完成且應用可在 localhost:8080 回應。
3. compose restart 後帳號、Battle、Tournament 與登入 keys 仍存在。
4. log 沒有 migration、PostgreSQL 連線、權限或 Data Protection 錯誤。

## 資料與權限

- 不把 database、keys 或 secret COPY 進 image。
- PostgreSQL 密碼只由 `.env`、環境變數或正式 secret store 提供，不提交 Git。
- app bind mount 只指向明確的 `data/keys/`，不掛載使用者家目錄或 repository 全部內容。
- production connection string 必須指向 `db` service，且不在 log 顯示密碼。

## 備份與還原

備份以 `pg_dump -Fc` 寫到 PostgreSQL volume 外，並另外備份 `data/keys/`。每份備份記錄 image、PostgreSQL major 與 EF migration 版本。

還原必須先在測試資料庫驗證 migration、登入 cookie／重新登入與歷史 Battle／Tournament 可讀，再操作正式資料庫。SQLite cutover 備份保持唯讀，至少保留至 PostgreSQL 備份與還原演練完成。

## 驗收界線

docker compose config 成功只證明 YAML 可解析，不等於 image build、migration、SQLite 匯入、restart persistence、備份還原或外部連線已驗收。這些項目在 acceptance-tests.md 保持未完成，直到有實機證據。
