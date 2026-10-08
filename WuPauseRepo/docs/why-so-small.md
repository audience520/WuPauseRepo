# 为什么只有 20 KB

一个 WinForms 程序做到 20.5 KB，需要先说明它**不是**怎么做到的——
而是说明常见做法为什么做不到，以及那个决定性的转折点在哪。

## 起点：49 MB

第一版用 .NET 10 + WinForms，`SelfContained=true`（自包含）：

```
WuPause.exe                    49.25 MB
```

发布目录拆开看：

```
270 个文件 / 118 MB
```

最大的几个依赖：

```
16.0 MB   System.Private.CoreLib.dll
14.0 MB   System.Windows.Forms.dll
 7.5 MB   System.Private.Xml.dll
 5.9 MB   System.Windows.Forms.Design.dll
 4.5 MB   coreclr.dll                ← .NET 虚拟机本体
 3.5 MB   System.Linq.Expressions.dll
 3.5 MB   System.Windows.Forms.Primitives.dll
 2.7 MB   System.Data.Common.dll
 2.1 MB   Microsoft.DiaSymReader.Native.amd64.dll   ← 压根没用，是 SDK 默认塞的
```

**功能代码本身约 20 KB。其余 49 MB 全部是"别人的运行时"。**

## 三条压缩路线（全试过）

### ① PublishTrimmed —— 失败

```xml
<PublishTrimmed>true</PublishTrimmed>
```

```
error NETSDK1175: 启用剪裁时，不支持或不推荐使用 Windows 窗体。
```

WinForms 靠反射加载资源，静态分析器无法确定哪些代码可删。

### ② PublishAot —— 同样失败

```xml
<PublishAot>true</PublishAot>
```

报同一个错。AOT 内部依赖 trim，而 trim 不支持 WinForms。

原生编译（AOT）本来能把体积压到几 MB，是最理想方案，但和 WinForms 互斥。

### ③ 排除无用依赖 —— 有效但杯水车薪

```xml
<BuiltInComInteropSupport>false</BuiltInComInteropSupport>
<SatelliteResourceLanguages>en</SatelliteResourceLanguages>
<EnableDefaultEmbeddedResourceItems>false</EnableDefaultEmbeddedResourceItems>
<DebugType>none</DebugType>
```

```
49.25 MB → 46.83 MB
```

只省了 2.4 MB。因为 `System.Windows.Forms.dll` 那 14 MB 无法排除——
程序真的在用它。

## 转折点：net48 不是 net10

当时正准备放弃 GUI。忽然意识到一件事：

> **.NET Framework 4.8 的 WinForms 是操作系统自带的，不需要打包。**

区别在这：

| 目标框架 | WinForms 来源 | 是否打包 | 结果 |
|---|---|---|---|
| net10 + 自包含 | 随包提供 | 必需 | 49 MB |
| **net48** | **系统 GAC** | **不需要** | **20.5 KB** |

于是只改了一行：

```xml
<TargetFramework>net48</TargetFramework>
```

同一套界面代码，从 49 MB 变成 20.5 KB。

`net48` 在 Windows 10 1607 以上和 Windows 11 全部自带，
32 位 / 64 位都有，所以对方不需要安装任何东西。

## 最终对比

| 版本 | 体积 | 说明 |
|---|---|---|
| 某开源工具 | 74.5 KB | 功能不全（只写 1 组键） |
| net10 + WinForms 自包含 | 49.25 MB | 打包了整个运行时 |
| └ 排除无用依赖 | 46.83 MB | 只省 2.4 MB |
| **net48 + WinForms** | **20.5 KB** | WinForms 由系统提供 |

比同类工具还小 3.6 倍，功能更完整。

## 附带的体积优化

```xml
<DebugType>none</DebugType>                          <!-- 不生成调试符号 -->
<SatelliteResourceLanguages>en</SatelliteResourceLanguages>  <!-- 只留英文资源，省掉语言包 -->
<AutoGenerateBindingRedirects>false</AutoGenerateBindingRedirects>
<PublishSingleFile>true</PublishSingleFile>          <!-- 合并成单文件 -->
<EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>
```

其中压缩开关单独贡献了很大一部分：

```
不压缩：116 MB →压缩后：49 MB
```

## 一个副产品：GUI 程序 vs 控制台程序

压体积过程中我一度把 `OutputType` 改成了 `Exe`（控制台），结果用户反馈"双击闪退"。

其实不是崩溃——控制台程序双击时黑窗口弹出、打印完、`Main` 返回、窗口关闭，
**看起来跟闪退一模一样**。

改回 `OutputType=WinExe` 即可（PE `Subsystem=2`，不打开黑窗口）。
这个插曲说明：**"双击"这个交互方式隐含了 GUI 假设**，
给非交互场景设计工具时要注意别破坏这个预期。

同时加了三层异常兜底，确保任何未捕获异常都弹窗而不是静默退出：

```csharp
Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
Application.ThreadException += (s, e) => MessageBox.Show(...);
AppDomain.CurrentDomain.UnhandledException += (s, e) => MessageBox.Show(...);
```

## 总结

让 WinForms 程序变小的关键，**不是压缩，是选对目标框架**。

```
net48  + 系统自带 WinForms  =  20 KB
net10  + 自包含 WinForms    =  49 MB
```

多写一行 csproj 属性，少打包 49 MB 运行时。