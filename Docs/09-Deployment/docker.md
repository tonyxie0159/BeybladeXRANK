# Docker 部署

## 架構

只容器化 ASP.NET Core Web Application；SQLite 不建立獨立 container。

- container port：8080
- runtime data：/app/data
- host bind mount：專案根目錄 data/
- restart policy：unless-stopped

data/ 同時包含 SQLite database 與 Data Protection keys，必須被 Git 忽略並在 container restart／recreate 後保留。

## 建置與啟動

```powershell
docker compose up -d --build
```

啟動時 Application 自動套用 EF Core migration。正式交付前必須確認：

1. image build 成功。
2. migration 完成且應用可在 localhost:8080 回應。
3. compose restart 後帳號、Battle、Tournament 與登入 keys 仍存在。
4. log 沒有 migration、SQLite lock、權限或 Data Protection 錯誤。

## 資料與權限

- 不把 database、keys 或 secret COPY 進 image。
- container 執行帳號必須能讀寫 /app/data，但不應取得不必要的主機目錄。
- bind mount 只指向明確的專案 data/，不掛載使用者家目錄或 repository 全部內容。
- production connection string 若覆寫，SQLite Data Source 仍須解析到 runtime data directory。

## 備份與還原

備份：

1. 停止 app container，避免複製中的 SQLite 寫入。
2. 複製完整 data/；至少包含 beyblade.db 與 keys/。
3. 記錄對應 image／migration 版本。
4. 完成後重新啟動並檢查。

還原必須在測試環境驗證 migration、登入 cookie／重新登入與歷史 Battle／Tournament 可讀。第一版不加入額外 backup service。

## 驗收界線

docker compose config 成功只證明 YAML 可解析，不等於 image build、migration、restart persistence 或外部連線已驗收。這些項目在 acceptance-tests.md 保持未完成，直到有實機證據。
