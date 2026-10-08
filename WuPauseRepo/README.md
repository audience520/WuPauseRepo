Windows Update Pause Tool
==========================

暂停 Windows 10 / 11 的自动更新，可自定义天数（最长 36500 天 ≈ 100 年）。

单文件 20.5 KB，双击即用，无需安装任何依赖。

![platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%20Windows%2011-0078D4)
![size](https://img.shields.io/badge/size-20.5%20KB-brightgreen)
![lang](https://img.shields.io/badge/language-C%23%20/.NET%20Framework%204.8-purple)
![license](https://img.shields.io/badge/license-MIT-blue)


特点
----

- **20.5 KB 单文件**，无需安装 .NET 运行时（net48 由 Windows 自带）
- **图形界面**，双击弹窗即用，自动请求管理员权限
- **写入完整暂停态**：三组注册表键协同，而非只改一个 UI 上限值
- **写完立刻回读验证**，把每项实际值打进日志，不做"我以为成功了"
- **操作前自动备份**注册表，双击 .reg 文件即可还原
- 支持暂停 / 取消暂停 / 随时刷新状态


效果
----

本机实测（Windows 11 家庭中文版 25H2Build 26200.9168）：

暂停 366 天后：

```
FlightSettingsMaxPauseDays = 367
PausedFeatureStatus        = 1
PausedQualityStatus        = 1
PauseUpdatesStartTime      = 2026-10-08T08:52:50Z
PauseUpdatesExpiryTime     = 2027-10-09T08:52:50Z
PauseFeatureUpdatesStartTime = 2026-10-08T08:52:50Z
PauseFeatureUpdatesEndTime   = 2027-10-09T08:52:50Z
PauseQualityUpdatesStartTime = 2026-10-08T08:52:50Z
PauseQualityUpdatesEndTime   = 2027-10-09T08:52:50Z
```

设置 → Windows 更新 页面将显示「更新已暂停」，并可正常「继续更新」。

> 界面截图见 [Releases](../../releases) 的 release 说明，或自己跑一下就知道了 —— 界面就一个状态栏、三个按钮、一个日志框。


使用
----

### 直接用编译好的版本

1. 从 [Releases](../../releases) 下载 `WuPause_v1.0.zip` 并解压
2. 双击 `WuPause.exe`
3. UAC 弹窗点「是」（需要管理员权限，写HKLM 必需）
4. 填入天数 → 点「设为暂停」
5. **完全关闭再打开**「设置」应用查看

### 自己编译

需要 .NET SDK（编译期依赖，运行时不需要）。

```bash
cd src
dotnet build -c Release -o ../out
```

产物在 `out/WuPause.exe`。

> 用的是 `net48` 目标框架而不是 `net10`。原因：net48 的 WinForms 由系统提供，
> 不需要把 14 MB 的 `System.Windows.Forms.dll` 打进包里。
> 换成 net10 + 自包含，同样的界面会变成 49 MB。
> 见 [为什么只有 20 KB](docs/why-so-small.md)。


工作原理
--------

Windows 的「暂停更新」不是单个开关，而是**三组注册表键协同**。多数工具只写第①组，
所以看起来"改了却没用"。

| 组 | 位置 | 值名 | 类型 | 作用 |
|---|---|---|---|---|
| ① | `WindowsUpdate\UX\Settings` | `FlightSettingsMaxPauseDays` | DWORD | UI 下拉框最大可选天数 |
| ② | `WindowsUpdate\UX\Settings` | `Pause*StartTime` / `Pause*EndTime` ×3 | REG_SZ | 暂停窗口起止时刻 |
| ③ | `WindowsUpdate\UpdatePolicy\Settings` | `PausedFeatureStatus` / `PausedQualityStatus` | DWORD | 状态位，1 = 暂停中 |

关键细节：

1. **②是 REG_SZ，格式 `yyyy-MM-ddTHH:mm:ssZ`** —— 结尾 `Z` 表示 UTC，不是北京时间
2. **① 必须严格大于 ② 的时间跨度**，超出则 ② ③ 被系统静默忽略
3. `Pause*EndTime` 和 `Pause*ExpiryTime` 命名不统一（历史遗留），**两种都要写**

更完整的机制分析（含策略引擎 `highrange=35` 为何封死组策略路径）见
[docs/how-it-works.md](docs/how-it-works.md)。


取消暂停
--------

界面上点「取消暂停」，或手动：

```powershell
$k = "HKLM\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings"
"PauseUpdatesStartTime","PauseUpdatesExpiryTime",
"PauseFeatureUpdatesStartTime","PauseFeatureUpdatesEndTime",
"PauseQualityUpdatesStartTime","PauseQualityUpdatesEndTime" |
  ForEach-Object { Remove-ItemProperty -Path $k -Name $_ -ErrorAction SilentlyContinue }

$p = "HKLM\SOFTWARE\Microsoft\WindowsUpdate\UpdatePolicy\Settings"
Set-ItemProperty -Path $p -Name PausedFeatureStatus -Value 0 -Type DWord
Set-ItemProperty -Path $p -Name PausedQualityStatus -Value 0 -Type DWord
```


注意事项
--------

⚠️ **请认真评估后再使用：**

- 这是**暂停**，不是禁用。到期后更新会正常恢复。
- 暂停期间**不会安装安全补丁**，这是真实的安全风险。建议最长 3–6 个月，
  之后主动检查更新。
- 累积更新 / 功能升级**可能重置**这些注册表值，需要重新执行。
- 请在修改前**创建系统还原点**，或直接使用程序自带的备份功能。
- 长时间不装安全补丁可能导致系统存在已知漏洞，请自行评估。

另：如果你曾手工禁用 `BITS` / `WaaSMedicSvc` / `TrustedInstaller` 服务
（`Start=3`），更新管线会半瘫，此时即使三组键都写对，
设置界面也可能不显示"已暂停"。


适用范围
--------

| 项 | 支持情况 |
|---|---|
| Windows 10 1607 以上 | ✅ |
| Windows 11（含 25H2） | ✅ |
| 家庭版 / 专业版 / 企业版 | ✅ |
| 32 位 / 64 位 | ✅ |
| Windows 7 / 8.1 | ❌ 无 .NET Framework 4.8 |


许可
----

MIT License

仅供个人技术研究使用。请勿用于违反 Windows 许可协议或绕过安全更新的用途。