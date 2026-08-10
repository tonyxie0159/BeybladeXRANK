# 驗收測試

## Account

- [ ] 可註冊。
- [ ] Account 不可重複。
- [ ] 密碼不可明文保存。
- [ ] 可登入。
- [ ] 可登出。
- [ ] DisplayName 可修改。
- [ ] 修改 DisplayName 不影響歷史戰績。

## Beyblade

- [ ] 可新增。
- [ ] 可修改名稱。
- [ ] 同一使用者名稱不可重複。
- [ ] 不同使用者可有相同名稱。
- [ ] 改名後仍是同一 BeybladeId。
- [ ] 歷史 Battle 顯示 Snapshot 名稱。

## Battle Setup

- [ ] 不可與自己對戰。
- [ ] 每方三顆。
- [ ] 同一方三顆不得重複。
- [ ] 可排列 1/2/3。
- [ ] Lock 後不可更換。
- [ ] 建立者可操作整場。

## Scoring

- [ ] SpinFinish = 1。
- [ ] KnockOut = 2。
- [ ] Burst = 2。
- [ ] Extreme = 3。
- [ ] >=4 達成勝利條件。
- [ ] 達成條件不自動 Completed。
- [ ] Finish Battle 才 Completed。

## Launch Fault

- [ ] 第一次不給分。
- [ ] 第二次對手 +1。
- [ ] 第二次後 fault reset。
- [ ] LaunchFault 不結束 Round。
- [ ] 同一陀螺繼續。
- [ ] 發射失誤分數計入陀螺失分。
- [ ] 玩家可查歷史因發射失誤失分。

## Reorder

- [ ] 三顆 Round 完成且無人 >=4 才能重排。
- [ ] 只能使用原本三顆。
- [ ] 可改順序。
- [ ] 分數保留。
- [ ] 舊順位歷史保留。

## Revision

- [ ] 可查看指定 Round 全部事件。
- [ ] 可修改該局勝負結果。
- [ ] 修改後該局分數正確。
- [ ] 修改後整場比分正確。
- [ ] 修改可能使 >=4 狀態消失。
- [ ] 修改可能使另一方達 >=4。
- [ ] Revision 有 audit record。

## Statistics

- [ ] 玩家勝敗。
- [ ] 玩家勝率。
- [ ] 玩家得失分。
- [ ] 玩家發射失誤失分。
- [ ] 每顆陀螺勝敗。
- [ ] 每顆陀螺得失分。
- [ ] 每顆陀螺勝率。
- [ ] 對手戰績。
- [ ] 對手陀螺戰績。
- [ ] 得分排序。
- [ ] 失分排序。
- [ ] 勝率排序。

## Deployment

- [ ] Docker build 成功。
- [ ] Container restart 後資料仍存在。
- [ ] SQLite bind mount 成功。
- [ ] 同網路其他裝置可連線。
- [ ] Cloudflare Tunnel 可連線。
- [ ] 手機瀏覽器可正常操作。
