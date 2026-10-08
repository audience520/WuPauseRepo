# 深度剖析：为什么改了 `FlightSettingsMaxPauseDays`，Windows 11 暂停更新还是只有 35 天

> 实测环境：Windows 11 家庭中文版 25H2（Build 26200.9168），.NET Framework 4.8.09221
> 所有数据均为本机注册表实时读取，非推测

## 一、问题现象

网上教程普遍说，修改这个注册表值就能突破限制：

```
HKLM\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings
FlightSettingsMaxPauseDays (DWORD)
```

但很多朋友（包括我）改完之后发现：**设置 → Windows 更新 → 暂停更新，下拉框最多还是只能选到 5 周（35 天）**，把值改成 9999 也一样。

更让人困惑的是：有些教程说改了确实生效，有些说无效。到底谁在撒谎？

我花了几天时间逐个注册表键翻查、读元数据库、逆向现成工具，最后找到了答案。**答案不在注册表里，而在"你以为改了，其实只改了三分之一"这个事实上。**

## 二、先看一个反直觉的事实

我找到的第一个关键证据，是这个键：

```
HKLM\SOFTWARE\Microsoft\PolicyManager\default\Update\SetMaxPauseDays
```

展开后：

| 值名 | 数据 | 含义 |
|------|------|------|
| `GPBlockingRegKeyPath` | `Software\Policies\Microsoft\Windows\WindowsUpdate` | 组策略路径 |
| `GPBlockingRegValueName` | `SetMaxPauseDays` | **策略真名** |
| **`highrange`** | **35** | ← **合法区间硬上限** |
| `lowrange` | 1 | 合法区间下限 |
| `mergealgorithm` | 3 | — |
| `value` | 35 | 当前生效值 |

注意 `highrange = 35`。这是 Windows Update **策略引擎自己声明的合法区间上限**，写死在系统里。

如果你读到这里以为"那突破口就是 `SetMaxPauseDays`，写个大的不就行了"——**我一开始也是这么想的，但我错了。** 我实测写了之后发现，即使写进去，策略引擎仍会 clamp 回 35。而且这个策略只在**专业版/企业版/教育版**的组策略编辑器里暴露，家庭版根本没有这个入口。

所以结论是：**微软从策略层面封死了这条路，35 天是设计意图，不是 bug。**

那为什么网上有人说改 `FlightSettingsMaxPauseDays` 能生效？

## 三、真正的答案：暂停不是开关，是三组键的协同

我把这台机器上所有与暂停相关的注册表键全导出来对照，发现"已暂停"状态需要**三组键同时存在**才成立：

| 组 | 位置 | 值名 | 类型 | 作用 |
|---|---|---|---|---|
| **①** | `WindowsUpdate\UX\Settings` | `FlightSettingsMaxPauseDays` | DWORD | UI 下拉框**最大可选天数** |
| **②** | `WindowsUpdate\UX\Settings` | `Pause*StartTime`<br>`Pause*EndTime` ×3 | **REG_SZ** | 暂停窗口的**起止时刻** |
| **③** | `WindowsUpdate\UpdatePolicy\Settings` | `PausedFeatureStatus`<br>`PausedQualityStatus` | DWORD | **状态位**，1 = 暂停中 |

本机实测（未暂停状态下）：

```
=== SOFTWARE\Microsoft\WindowsUpdate\UX\Settings ===
  FlightSettingsMaxPauseDays       = 367        ← 存在
  PauseUpdatesStartTime            -不存在-     ← 不存在
  PauseUpdatesExpiryTime           -不存在-     ← 不存在
  PauseFeatureUpdatesStartTime     -不存在-     ← 不存在
  PauseFeatureUpdatesEndTime       -不存在-     ← 不存在
  PauseQualityUpdatesStartTime     -不存在-     ← 不存在
  PauseQualityUpdatesEndTime       -不存在-     ← 不存在

=== SOFTWARE\Microsoft\WindowsUpdate\UpdatePolicy\Settings ===
  PausedFeatureStatus              = 0          ← 0 = 未暂停
  PausedQualityStatus              = 0          ← 0 = 未暂停
  PausedFeatureDate                = '2026-10-08 07:42:03'
  PausedQualityDate                = '2026-10-08 07:42:03'
```

**这就是答案。** 那些"只改 `FlightSettingsMaxPauseDays` 就好"的说法，准确的表述应该是：

> 改这个值只是**解锁了下拉框的可选范围**。至于系统**是否真的处于暂停态**，取决于第 ② ③ 组键。缺了它们，Windows Update 客户端根本不知道"从什么时候暂停到什么时候"，会照常下载安装。

## 四、一个更隐蔽的坑：时间跨度不得超过上限

这是我在 NTLite 社区实测帖里找到的，也是我自己踩过的：

> `Pause*StartTime` 到 `Pause*EndTime` 的跨度**不得超过** `FlightSettingsMaxPauseDays` 的值，超出的话其余键会被**静默忽略**。

也就是说，很多人写的"进阶脚本"是这样：

```
FlightSettingsMaxPauseDays = 3650   # 上限改大
PauseUpdatesExpiryTime    = 2035年  # 结束时间设到 10 年后
```

**这依然是无效的。** 上限是 3650，跨度是 3000，看起来没问题；但如果有人写 `FlightSettingsMaxPauseDays = 999` 而结束时间设到 2035 年（跨度 3000+），那这个暂停就是无效的，且**没有任何报错提示**。

我自己的第一版实现就踩了这个坑。我写的是：

```csharp
if (days < 1) days = 1;
if (days > MAX_PAUSE_DAYS_CAP) days = MAX_PAUSE_DAYS_CAP;   // days 钳到 36500
int cap = days + 1;
if (cap > MAX_PAUSE_DAYS_CAP) cap = MAX_PAUSE_DAYS_CAP;   // cap 又被钳回 36500
```

结果当 `days = 36500`（上限）时：`cap = 36500`，而跨度也是 36500 天 → **浮点比较下 `36500 >= 36500.0000001` 为假 → 约束不成立 → 暂停直接失效。**

修复方法是让 `cap` 保持 `days + 1`，不再二次钳制：

```csharp
int cap = days + 1;   // 严格大于跨度，且不再对 cap 单独钳制
```

所以正确的约束是：**`FlightSettingsMaxPauseDays` 必须严格大于（不是大于等于）时间跨度。**

## 五、时间戳格式：必须是 ISO-8601 UTC

第 ② 组键的类型是 **REG_SZ**，不是 DWORD。格式必须是：

```
yyyy-MM-ddTHH:mm:ssZ
```

结尾的 `Z` 表示 Zulu 时间，也就是 **UTC/GMT**（不是北京时间！）。如果误写成本地时间，偏移量会导致暂停时长偏差。

本机成功暂停后的实测值：

```
PauseUpdatesStartTime    = '2026-10-08T08:52:50Z'   ← UTC
PauseUpdatesExpiryTime   = '2027-10-09T08:52:50Z'   ← UTC
PauseFeatureUpdatesStartTime = '2026-10-08T08:52:50Z'
PauseFeatureUpdatesEndTime   = '2027-10-09T08:52:50Z'
PauseQualityUpdatesStartTime = '2026-10-08T08:52:50Z'
PauseQualityUpdatesEndTime   = '2027-10-09T08:52:50Z'
```

共 6 个时间戳键，缺一不可。注意 `Pause*EndTime` 和 `Pause*ExpiryTime` 命名不统一，这是历史遗留，**两种都要写**。

计算校验（这组值是自洽的）：

```
span = 2027-10-09 - 2026-10-08 = 366 天
cap  = 367
367 > 366 ✓  约束满足
```

## 六、完整方案

综上，要让暂停真正生效，需要按顺序做三件事：

### 第 1 步：解锁 UI 上限（可选，但建议）

```
键：HKLM\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings
值：FlightSettingsMaxPauseDays  (DWORD)
数据：必须严格大于你要设置的暂停天数
```

### 第 2 步：写入暂停窗口时间戳（关键）

```
键：HKLM\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings
值：
  PauseUpdatesStartTime          (REG_SZ)  = 当前UTC时间
  PauseUpdatesExpiryTime         (REG_SZ)  = 当前UTC时间 + N天
  PauseFeatureUpdatesStartTime   (REG_SZ)  = 当前UTC时间
  PauseFeatureUpdatesEndTime     (REG_SZ)  = 当前UTC时间 + N天
  PauseQualityUpdatesStartTime   (REG_SZ)  = 当前UTC时间
  PauseQualityUpdatesEndTime     (REG_SZ)  = 当前UTC时间 + N天
```

### 第 3 步：置状态位（关键）

```
键：HKLM\SOFTWARE\Microsoft\WindowsUpdate\UpdatePolicy\Settings
值：
  PausedFeatureStatus  (DWORD) = 1
  PausedQualityStatus  (DWORD) = 1
```

### 恢复更新

```powershell
# 删时间戳
reg delete "HKLM\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings" /v PauseUpdatesStartTime  /f
reg delete "HKLM\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings" /v PauseUpdatesExpiryTime /f
reg delete "HKLM\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings" /v PauseFeatureUpdatesStartTime /f
reg delete "HKLM\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings" /v PauseFeatureUpdatesEndTime /f
reg delete "HKLM\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings" /v PauseQualityUpdatesStartTime /f
reg delete "HKLM\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings" /v PauseQualityUpdatesEndTime /f

# 状态位清零
reg add "HKLM\SOFTWARE\Microsoft\WindowsUpdate\UpdatePolicy\Settings" /v PausedFeatureStatus /t reg_dword /d 0 /f
reg add "HKLM\SOFTWARE\Microsoft\WindowsUpdate\UpdatePolicy\Settings" /v PausedQualityStatus /t reg_dword /d 0 /f
```

## 七、注意事项

1. **这是"暂停"不是"禁用"。** 到期后更新会正常恢复。
2. **累积更新/功能升级可能重置这些值**，需要重新执行。
3. **暂停期间不装安全补丁**，别长期开启不管，建议 3-6 个月检查一次。
4. **写 HKLM 需要管理员权限。**
5. 家庭版没有组策略编辑器，只能走注册表。

## 八、一些额外发现

写这个工具时，我顺手看了下这台机器的更新相关服务状态，发现一个有意思的现象：

```
wuauserv         Start=2  (手动)
UsoSvc           Start=2  (手动)
DoSvc            Start=2  (手动)
WaaSMedicSvc     Start=3  (禁用)
BITS             Start=3  (禁用)
TrustedInstaller Start=3  (禁用)
```

`BITS` / `WaaSMedicSvc` / `TrustedInstaller` 已被禁用（`Start=3`）。**这会让 Windows Update 的管线状态机半瘫**，导致"暂停状态算不出来"——也就是 `PausedFeatureStatus` 恒为 0，`PausedQualityStatus` 恒为 0。

这解释了另一个常见现象：**如果你手工禁用过这几个服务，即使把三组键都写对了，设置界面也可能不显示"已暂停"。** 因为暂停状态本身就是靠这条管线计算出来的。

不过要注意，`TrustedInstaller` 被禁用会连带影响 Defender 的更新、商店安装、以及很多依赖它的组件，别随手禁。

## 九、总结

| 常见说法 | 实际情况 |
|----------|----------|
| 改 `FlightSettingsMaxPauseDays` 就能暂停 | ❌ 只解锁下拉框，不产生暂停态 |
| 写 `SetMaxPauseDays` 组策略能突破 | ❌ `highrange=35` 硬上限，且家庭版无入口 |
| 时间跨度可以随便设 | ❌ 必须 ≤ 上限值，且上限要**严格大于**跨度 |
| 时间戳是整数时间 | ❌ 是 REG_SZ 的 ISO-8601 **UTC** 字符串 |

一句话：**暂停态由三组键协同决定，任何单一改动都是无效的。**

---

**声明**：本文所有注册表数据均在本机 Windows 11 25H2 (Build 26200.9168) 上实时读取验证，仅供技术研究参考。请勿将其用于违反 Windows 许可协议或绕过安全更新的用途。安全补丁有实际价值，长期暂停更新会增加系统风险，请自行评估风险并控制暂停时长。

如果你觉得这篇分析有价值，欢迎点赞收藏。有技术问题评论区见，我会尽量解答。