# 零件目錄、陀螺版本與出戰配置

本文件是零件功能的開發參考。2026-09-05 定案：同一玩家的相同上蓋名稱歸為同一顆陀螺，完整零件組合不同則為不同配置版本；出戰先選陀螺，再選版本。此規則取代原本「換零件必須新增陀螺」的限制。

## 身分與版本

- 母陀螺保存玩家自訂名称及系統決定的 UpperName。一般上蓋用上蓋名稱；CX 使用紋章＋主要戰刃，或紋章＋金屬戰刃。
- 同一玩家不能同時建立兩顆相同上蓋名稱的有效陀螺；新增頁遇到已存在的上蓋，提示前往該陀螺新增版本。不同玩家各自擁有自己的陀螺。
- 更換任何實際零件，只要 UpperName 不變就新增版本；包含 CX 超越／輔助戰刃。UpperName 改變則建立另一顆陀螺。
- 以完整 PartId 集合識別版本，排序後以逗號串接為 PartsKey；換回相同組合沿用原版本，不改寫其建立時間或快照。順序不影響版本身分。
- 版本建立後不再修改或刪除。自訂名稱可修改，所有版本共用母陀螺名稱。
- 軟刪除的陀螺退出選用清單，原配置與戰績保留；歷史不同 BeybladeId 不自動合併。

## 資料表

| 表 | 欄位與限制 |
|---|---|
| Part | Id PK、Category、Name varchar(100)、IntegratesRatchet、IsActive、CreatedAtUtc、UpdatedAtUtc；Category＋Name 唯一 |
| PartSeries | PartId FK＋Series（BX/UX/CX）複合 PK；系列只供篩選 |
| Beyblade | 原有欄位＋UpperName nullable varchar(200)；有效且已確認上蓋的資料以 UserId＋UpperName 唯一 |
| BeybladeConfiguration | Id PK、BeybladeId FK、VersionNo 正整數、PartsKey varchar(65)、CreatedAtUtc；BeybladeId＋VersionNo 及 BeybladeId＋PartsKey 各自唯一 |
| BeybladeConfigurationPart | ConfigurationId FK＋PartId FK 複合 PK、PartNameSnapshot varchar(100) |

Beyblade.Configurations 為一對多；Configuration 只是取最高 VersionNo 的方便顯示屬性，不是 EF 關聯，也不是當場對戰版本。Id＋BeybladeId alternate key 繼續支援歷史配對外鍵。

Category 分為 Blade、Ratchet、Bit、LockChip、MainBlade、OverBlade、MetalBlade、AssistBlade。IntegratesRatchet 只可用於 Blade／Bit；已保存的 Category 與 IntegratesRatchet 不直接更改。目錄名稱依類別＋名稱去重，商品代碼、配色、版本註記不影響身分。清单共 279 筆，見 [parts-catalog.md](parts-catalog.md)。

所有零件／配置外鍵使用 Restrict。停用零件不刪除既有版本，也不影響既有對戰；新配置不能使用停用零件。重新匯入不重新啟用已停用零件。

## 完整組裝

| 結構 | 必要零件 |
|---|---|
| 一般 | 上蓋＋固鎖＋軸心 |
| 上蓋整合固鎖 | 一體式上蓋＋一般軸心 |
| CX 三件式上蓋 | 紋章＋主要戰刃＋輔助戰刃，再配固鎖及軸心 |
| CX 四件式上蓋 | 紋章＋超越戰刃＋金屬戰刃＋輔助戰刃，再配固鎖及軸心 |
| 軸心整合固鎖 | 完整上蓋結構＋一體式軸心 |

一般上蓋、CX 三件式及四件式互斥。軸心必填，固鎖位置恰好一次；不得同時使用兩個固鎖一體式零件，或同時使用一般固鎖與一體式零件。每類別最多一件；缺件、未知 ID、重複 ID 與非法混搭均拒絕。系列不限制混搭。

## 通用名稱

通用名稱為唯讀衍生屬性，使用配置建立時的 PartNameSnapshot，不能由 Client 提交或修改。目錄後來修正拼字不會改寫既有配置名稱。

| 結構 | 命名 | 範例 |
|---|---|---|
| 一般 | 上蓋＋固鎖＋軸心 | 鮫鯊狂鱗1-50J |
| 上蓋整合固鎖 | 上蓋＋軸心 | 榮耀武神LR |
| 軸心整合固鎖 | 上蓋＋一體式軸心 | 武士星劍Op |
| CX 三件式 | 紋章＋主要戰刃＋固鎖（如需要）＋軸心 | 帝王閃焰4-55S |
| CX 四件式 | 紋章＋金屬戰刃＋固鎖（如需要）＋軸心 | 腕龍極變1-50J |

「極變」是金屬戰刃，「閃焰」是主要戰刃；Op 保留代碼大小寫。CX 超越／輔助戰刃不寫入通用名稱，但仍為必要零件。不同版本可能通用名稱相同，需以 v1/v2 與完整零件摘要區分。

## 新增／編輯 UI 與服務

- /Beyblades/Create：自訂名稱與完整 PartIds 必填；先驗證，再在同一交易保存母陀螺與 v1。不得留下只有名稱的新陀螺。
- /Beyblades/Edit/{id}：顯示歷次版本，可選擇任一版本作為換裝基礎。儲存時依完整組合建立或沿用版本；改名與版本保存同一交易完成。若已選的零件被停用，須改用啟用零件才能保存新配置。
- /Beyblades/Configuration/{id}：保留原補登入口，已配置時提供編輯／新增版本連結。
- 自訂名稱、上蓋結構、各零件選单及唯讀通用名稱預覽。原生下拉搭配搜尋框，支援依序部分字元匹配；搜尋忽略大小寫、空白與全半形，但保存原名稱。
- 搜尋不改變目前已選零件。前端提示缺件／固鎖衝突；伺服器重新驗證。JavaScript 關閉仍可使用原生選單及伺服器驗證。
- 所有頁面與服務驗證擁有者；POST 使用防偽驗證。URL 的陀螺 ID 不受表單內另送 Id 改寫。

BeybladeConfigurationService 提供 GetActivePartsAsync、GetMineAsync（最新版本）、GetVersionsAsync、RecordAsync。RecordAsync 在 PostgreSQL 先鎖住使用者資料列，序列化同一玩家的上蓋歸屬與版本編號分配；新增陀螺也使用相同鎖順序。唯一索引是重複上蓋、版本號與零件組合的第二道保護。

## 出戰選版與歷史

快速對戰與 Tournament 都先選陀螺，再選版本，預帶最新版本並顯示完整零件摘要。每個位置提交 BladeIds 與相同順序的 ConfigurationIds；Server 確認版本屬於該陀螺、該陀螺屬於登入者且未刪除。同顆陀螺的不同版本不能占用同一陣容的多個位置。

BattleLineupSelection.BeybladeConfigurationId 與 BattleLineup.PlayerAConfigurationId／PlayerBConfigurationId 均以「配置 Id＋陀螺 Id」外鍵連接。Round 由 LineupId 取得當場版本。

- 初次提交保存選定版本及「自訂名稱 · vN · 通用名稱」快照；名稱欄位擴至 varchar(520)，容納版本編號。
- 重複提交只有陀螺與版本都相同才視為同一請求；不能偷偷替換版本。
- 對戰重排只調整原陀螺順序，複製當場原配置 Id 與名稱；不重新讀取最新版本。
- 新增版本、改名或目錄改名，都不影響已提交對戰。
- 舊對戰配置關聯保持 null，後來補登不回填歷史。
- 原有未配置陀螺的出戰流程保留，ConfigurationIds 用 0 表示未記錄版本。已配置陀螺不能用 0 跳過版本。
- 舊服務呼叫可省略 configurationIds，但只在零或單一版本時相容；多版本必須明確指定。
- 公開前只提供自己的陣容與可選資料，維持既有私密與隊友所有權邊界。

## 戰績

總覽以 BeybladeId 彙總所有有效事件。點選陀螺進入 /Statistics/Beyblade/{id}，顯示總戰績與各版本勝敗、勝率、得失分、小局數、每局平均、失誤及 B/X Side、勝利方式，支援來源與站位篩選。

版本分組依 Round 的當場 Lineup 配置 Id；null 顯示「未記錄版本」，仍計入總戰績。所有版本的勝敗與分數加總應等於母陀螺總數；總勝率從總勝敗重新計算，不能平均各版本勝率。沿用原有有效事件與對戰狀態篩選，不改變 scoring 或修正歷史。

對手陀螺比較改用雙方 BeybladeId＋當場配置 Id 分組，避免同名 CX 版本混淆或自訂改名切散同一版本。顯示該組最新一筆歷史名稱快照，不回寫歷史資料。

## Migration 與匯入

Migration 順序：

1. 20260903154059_PostgreSqlInitial
2. 20260904145457_AddPartsCatalog
3. 20260904154411_ExpandBeybladeNameSnapshot
4. 20260904164254_AddBeybladeConfigurationVersions

第 4 個 migration 將既有單一配置設為 v1，依原 PartId 產生 PartsKey，依名稱快照補上 UpperName。保持配置 Id、零件快照及所有對戰關聯不變。舊名稱陀螺尚無配置時 UpperName 保持 null。若既有有效陀螺有同玩家同上蓋的不同 Id，migration 會完整回滾並提示先處理歸戶，不擅自合併歷史。若已有多版本或名稱超過舊欄位長度，拒絕回退至單一配置 schema，避免資料損失。

在既有 migration service 執行 dotnet BeybladeRecordSystem.dll --migrate，套用 schema 後匯入內嵌目錄。只匯入時使用 --import-parts。沿用 ConnectionStrings__DefaultConnection，不提交機密。匯入為交易式且使用 PostgreSQL advisory lock；可重複執行，不改已有 Id、名稱、停用狀態或建立時間。

從舊 SQLite 切換時先執行 DataMigration，再執行 --migrate／--import-parts；不能先將目錄匯入需要空庫的切換目標。LegacyLineupReader 明確只讀舊欄位，包含尚無 UpperName 的 Beyblade，配置關聯保持 null。

## 後續工作邊界

同側全隊 PartId 去重、跨隊友並行衝突檢查，以及新對戰強制完整配置另案接入；目前不變更舊對戰可續打的條件。收藏三顆完整陣容、手動歸戶舊陀螺、版本封存及自訂預設版本尚未加入。本階段預帶最高 VersionNo，玩家仍可選擇任一既有版本。

