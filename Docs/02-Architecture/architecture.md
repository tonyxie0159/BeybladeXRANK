# 程式架構

## 技術

- .NET 10 LTS
- ASP.NET Core
- Razor Pages
- Bootstrap
- Vanilla JavaScript
- EF Core
- SQLite
- Docker

## 架構

採單體 Web Application。

```text
Browser
  |
ASP.NET Core
  |
  +-- Razor Pages
  |
  +-- Application Services
  |     +-- AuthService
  |     +-- BeybladeService
  |     +-- BattleService
  |     +-- StatisticsService
  |
  +-- EF Core
        |
      SQLite
```

## 分層

### Web

只負責：

- HTTP Request
- Model Binding
- Authorization
- 顯示 ViewModel
- 呼叫 Application Service
- 顯示錯誤與結果

不得把戰鬥規則寫在 PageModel 或 JavaScript。

### Application

負責：

- BattleService
- BeybladeService
- AuthService
- StatisticsService

所有核心規則集中在 Service。

### Domain

放置：

- Entity
- Enum
- Battle rule value objects / helpers
- 純規則計算

### Infrastructure

負責：

- AppDbContext
- EF Core configuration
- SQLite
- Password hashing / ASP.NET Core Identity 所需實作

## 建議專案結構

```text
BeybladeRecordSystem/
├── src/
│   └── BeybladeRecordSystem/
│       ├── Areas/
│       ├── Data/
│       ├── Domain/
│       │   ├── Entities/
│       │   └── Enums/
│       ├── Services/
│       ├── Pages/
│       │   ├── Account/
│       │   ├── Beyblades/
│       │   ├── Battles/
│       │   └── Statistics/
│       ├── ViewModels/
│       ├── wwwroot/
│       ├── Program.cs
│       └── appsettings.json
├── tests/
│   └── BeybladeRecordSystem.Tests/
├── data/
├── Dockerfile
├── compose.yaml
└── README.md
```

不建立獨立 API 專案。

