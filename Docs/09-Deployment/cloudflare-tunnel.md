# Cloudflare Tunnel

## 開發／臨時公開

本機 Application：

`http://localhost:8080`

Cloudflare Quick Tunnel：

`cloudflared tunnel --url http://localhost:8080`

產生 HTTPS URL 後提供給其他使用者。

## 正式長期使用

若未來有 Domain，再建立 named tunnel。

第一版不需要：

- Domain
- Reverse proxy container
- Nginx
- Caddy
- 開放 Router inbound port

## 注意

Quick Tunnel 適合開發與臨時測試，不應被 AI Agent 宣稱為正式 SLA 等級部署。

