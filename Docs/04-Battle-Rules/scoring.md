# 計分規則

## BattleResult

| ResultType | Score |
|---|---:|
| SpinFinish | 1 |
| KnockOut | 2 |
| Burst | 2 |
| Extreme | 3 |

Score 由後端唯一決定。

Client 不可提交任意分數。

例如 Client 只能送：

`ResultType = Burst`

後端決定：

`ScoreAwarded = 2`

## 發射失誤

一次 LaunchFault 不立即得分。

第二次同一顆陀螺的有效 LaunchFault：

- 對手 +1。
- 建立 LaunchFaultPenalty event。
- fault counter reset。

## 範例

A 龍騎士 vs B 霸王：

1. A LaunchFault
2. A LaunchFault
3. B +1
4. fault reset
5. A SpinFinish
6. A +1

Round 對 A/B：

- A 得 1
- B 得 1

陀螺統計：

A 龍騎士：
- 得分 1
- 失分 1

B 霸王：
- 得分 1
- 失分 1

玩家額外統計：

A：
- 因發射失誤失分 1

B：
- 因發射失誤得分 1

