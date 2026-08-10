# AI Coding Agent 執行順序

不要一次生成整個系統。

## Step 1

建立 solution / project / test project。

只完成：

- build
- startup
- Bootstrap

## Step 2

建立：

- User
- Beyblade
- Battle
- BattleLineup
- BattleRound
- BattleRoundEvent
- BattleRoundRevision

完成 EF migration。

## Step 3

Authentication。

## Step 4

Beyblade CRUD。

## Step 5

Battle Setup。

## Step 6

先完成 Battle domain service + unit tests。

必須測：

- 四種勝利分數。
- >=4。
- LaunchFault。
- 第二次 LaunchFaultPenalty。
- Round 不因 LaunchFault 結束。
- 三 Round 後 Reorder。
- Reorder 保留比分。
- Finish Battle。
- Revision。

## Step 7

接 Battle UI。

## Step 8

Statistics queries。

## Step 9

Responsive UI polish。

## Step 10

Docker。

## Step 11

Cloudflare Tunnel 文件與實測。

## Step 12

完整 acceptance test。

任何階段如果出現架構問題，優先修正核心規則，不得透過新增未需求功能繞過問題。
