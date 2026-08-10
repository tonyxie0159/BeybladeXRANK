# Docker 部署

## 原則

只容器化 Web Application。

SQLite 不建立獨立 container。

資料透過 bind mount 保存。

## Dockerfile

使用 .NET 10 SDK multi-stage build：

1. restore
2. build
3. publish
4. ASP.NET runtime image

Application listening port 使用容器內 8080。

## compose

概念：

```text
services:
  app:
    build: .
    ports:
      - "8080:8080"
    volumes:
      - ./data:/app/data
    restart: unless-stopped
```

SQLite：

`/app/data/beyblade.db`

## 資料備份

第一版只需要：

- 停止 container。
- 複製 `data/beyblade.db`。

不要導入額外 backup service。

