# Cloudflare Tunnel

## 使用範圍

Cloudflare Tunnel 由主機側連到本機 Application：

```powershell
cloudflared tunnel --url http://localhost:8080
```

Quick Tunnel 產生的 HTTPS URL只適合開發、驗收與短期分享，不是正式 SLA 等級部署，也不保證固定網址。

## HTTPS 與 reverse proxy 要求

外部使用者看到 HTTPS，但 cloudflared 到 Kestrel 的 origin 可是 HTTP。Application 必須正確處理：

- X-Forwarded-Proto／Host 等 forwarded headers。
- 只信任明確的 proxy／network，或使用平台核准的環境設定。
- Authentication Cookie 為 HttpOnly，使用合適 SameSite，外部 HTTPS 時標記 Secure。
- 產生 redirect／absolute URL 時使用外部 HTTPS scheme。
- HSTS／HTTPS 行為不得只根據未處理的 origin HTTP scheme 誤判。

在上述設定與瀏覽器實測完成前，不得把 Cloudflare HTTPS URL 視為安全部署驗收通過。

## 網路邊界

- 不需開放 Router inbound port。
- Quick Tunnel 不需 Domain、Nginx、Caddy 或 reverse proxy container。
- compose 的 8080 host port 仍可能被同 LAN 裝置存取；若不需要 LAN，正式環境應限制 bind address 或 host firewall。
- 不在文件、commit、issue 或 log 中貼出長期 tunnel credential。

## Named Tunnel

只有需要固定 Domain／長期服務時才建立 named tunnel。屆時必須另外記錄：

- hostname 與 tunnel routing。
- credential 保存與輪替位置。
- Cloudflare Access／其他存取控制是否需要。
- host firewall、proxy allow-list 與監控／復原方式。

這些設定需由使用者明確核准，不由 Agent 自動建立雲端資源。

## 實機驗收

- 外部 HTTPS 可以 Register／Login／Logout。
- Cookie 具有預期 Secure／HttpOnly／SameSite。
- refresh、redirect 及 anti-forgery POST 正常。
- 兩個帳號可完成快速邀請與至少一場 Battle。
- container restart 後資料仍在；舊 session 或重新登入行為符合預期。
- tunnel 中斷不會改變 Battle／Tournament server-side 狀態。
