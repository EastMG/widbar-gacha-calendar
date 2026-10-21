# 03 · WidBar 插件契约

基于 `WidBar.SDK 2.1.0`（本机 NuGet 包实测 dump，见 `_widbar_spike/api/WidBar.SDK.api.md`）与官方模板
`andelby/widbar-widget-template`。**文档与 DLL 实际 API 已交叉核对。**

## 1. 身份

| 项 | 值 |
|---|---|
| `WidBarPluginId` | `com.eastmg.gachacalendar` |
| `WidBarPluginName` | 二游日历 |
| `WidBarPluginDescription` | 28 款二游的卡池与活动倒计时，常驻 Windows 11 任务栏。 |
| `WidBarPluginCategory` | `Information`（枚举：Utility/System/Media/Productivity/Information/Developer/Entertainment） |
| `WidBarPluginPreviewWidth` | `236`（逻辑像素；设置里"紧凑模式"可改成 160） |
| `WidBarPluginConfigurable` | `true` |
| `WidBarPluginVersion` | 与 `<Version>` 同源，`1.0.0` 起步 |
| Package Identity Name | `EastMG.GachaCalendar`（与商店一致） |

这些属性写在 `src/GachaWidget/GachaWidget.csproj`，SDK 的 build target 生成 `obj\widbar\plugin.json`，
打包工程把它作为 `Public\plugin.json` 塞进包；**不要手写 plugin.json**。

## 2. 发现机制

`Package.appxmanifest` 里必须有：

```xml
<uap3:Extension Category="windows.appExtension">
  <uap3:AppExtension Name="com.widbar.widget" Id="widget"
                     PublicFolder="Public"
                     DisplayName="二游日历" Description="…">
    <uap3:Properties>
      <WidBarPluginId>com.eastmg.gachacalendar</WidBarPluginId>
      <WidBarSdkVersion>2</WidBarSdkVersion>
    </uap3:Properties>
  </uap3:AppExtension>
</uap3:Extension>
```

加 `rescap:Capability Name="runFullTrust"` 与 `uap:Capability Name="internetClient"`。
WidBar 扫到 AppExtension 就会把组件列进布局页，用户拖到任务栏空位即生效。

## 3. 三块 UI

### 预览（`CreatePreviewContent`）
- 宽由 `PreviewLogicalWidth` 决定（重读时机：设置变更后 + `Context.RequestPreviewRefresh()`）。
- 高 = 任务栏高度（普通 ~48px，小任务栏 ~26px）——布局**不要写死高度**，用相对布局；
  行高用固定 `LineHeight` 而不是根元素 `MinHeight`。
- 内容：`previewItems`（1–3）个 chip，每个 = 游戏简称 + 短倒计时（`1天` / `5时` / `37分`），
  按"最近结束"升序。无进行中项时显示一句"暂无进行中"。
- 预览根 `Background=Transparent`，颜色全用 `ThemeResource`。
- 预览是活的：可以在 chip 上放交互（点击 chip 打开 flyout 用 `Context.RequestOpenFlyout()`；
  点击非交互区由 WidBar 自动开 flyout）。

### 展开面板（`CreateFlyoutContent` + `FlyoutWidth/Height`）
- 尺寸：`FlyoutWidth=760`、`FlyoutHeight=560`（用户可再拉大，WidBar 记住尺寸）。
- 内容：六列表格（游戏 / 状态 / 卡池 / 卡池起止 / 活动 / 活动起止），28 款可见行，行悬停显示
  `bannerHover`/`eventHover` 多行明细（`ToolTipService`），顶部状态条 + 刷新按钮，底部数据时间与来源说明。
- 材质由用户选（默认 Acrylic），**不要设背景、不要 override `FlyoutBackdrop`（已 obsolete）**；
  自己的内容用 `CardBackgroundFillColorDefaultBrush` 之类的主题画刷画卡片。
- 内容只创建一次、复用；关掉时用 `IWidgetFlyoutLifecycle.OnFlyoutHidden` 停掉跑马灯/滚动等。

### 设置（`IConfigurableWidgetPlugin.CreateSettingsContent`）
- 宿主给 Mica 窗口 + 保存/取消按钮（按钮文案由 WidBar 本地化），尺寸自适应。
- 字段见 `docs/02-数据契约.md §4`：预览条数、自动刷新、刷新间隔、feed URL、列开关、条目显隐/排序。
- 编辑时通过 `IWidgetSettingsContext.SaveSettings(json)` 提交；同时
  `OnSettingsDraftChanged(json)` 用于让预览实时跟随（取消时宿主会用旧 JSON 再调一次）。

## 4. 生命周期（本项目的实现方式）

```
App.OnLaunched → WidgetHostApplication 建连接 → CreatePlugin()   // 每个实例一次
  → InitializeAsync(ctx)
       settings = WidgetSettings.FromJson(ctx.SettingsJson).Normalize()
       store = new CatalogStore();  vm = new WidgetViewModel(store.Catalog)
       立刻读 ctx.DataDirectory\feed.json（缓存）→ vm.Apply → 预览先出内容
       后台 RefreshAsync()（首次立即 + 之后按 refreshMinutes，最小 15 分钟）
  → CreatePreviewContent() / CreateFlyoutContent() / CreateSettingsContent()  // 各一次，复用
  → 每分钟 Timer.Tick → vm.Tick(now) → 倒计时文本更新（只改文本，不重建视觉树）
  → DisposeAsync()：停 Timer、取消进行中的 HTTP、释放图标缓存
```

要点：
- **所有 HTTP 在后台线程**，回 UI 用 `DispatcherQueue.TryEnqueue`。
- 刷新完成后调 `Context.RequestPreviewRefresh()`（预览宽度可能随"紧凑模式"变化）。
- `Context.IsPreviewVisible == false`（Smart Stack 后台）时：只保留 tick，不下载图标、不跑动画。
- 首帧必须来自缓存（毫秒级），不能等网络——任务栏小组件卡顿最影响观感。

## 5. 已知限制

- v0 的数据源是静态快照（最多滞后 6 小时）——UI 上必须标注"刷新：<时间>"，不能伪装成实时。
- feed 里没有时区偏移；倒计时按源站墙钟文本与本地时间比较（与 WPF 版口径一致）。
  日服/国际服的实际绝对时刻可能差 1 小时，这一点在 v1 接入 core 的 `tz` 后修正。
- 图标是外链（moegirl 等），失败时退化为"首字 + 色块"，**不能阻塞行渲染**。

## 6. 构建与侧载（本机环境实测记录）

- 本机 DSH 会话运行在 **Low 完整性级别**：MakePri.exe 写 `%TEMP%`（Medium IL）会被拒（0x80070005），
  所以 `TEMP`/`TMP` 必须指向工作区内目录。
- 一键构建 + 注册：`tools\build-and-register.ps1`（已封装上述环境变量与"先移除旧包"的规避）。
  实测输出：`PFN=EastMG.GachaCalendar_0.1.4.0_x64__8c3jes7x0pqng`、`Status=Ok`、layout 95.67MB。
- 手工等价命令：

```powershell
$env:TEMP='F:\DeepSeek\DeepSeek-Workplace\DSH-Workplace-01\widbar-gacha-calendar\.tmp'
$env:TMP=$env:TEMP
$env:DOTNET_CLI_HOME='F:\DeepSeek\DeepSeek-Workplace\DSH-Workplace-01\.dotnet-home'
$env:NUGET_PACKAGES='F:\DeepSeek\DeepSeek-Workplace\DSH-Workplace-01\.nuget-packages'
dotnet build src\GachaWidget\GachaWidget.csproj -c Release -p:Platform=x64
Add-AppxPackage -Register <layout>\AppxManifest.xml
```

- 包的两种形态（都是单项目 MSIX，不需要 `.wapproj`）：
  - `dotnet build`（默认）：出 layout，用 `-Register` 松散注册，适合快速迭代；
  - `-p:GenerateAppxPackageOnBuild=true -p:UapAppxPackageBuildMode=SideloadOnly`：出 `.msix`
    （自包含 WindowsAppSDK 后约 **32.7MB**），签名后 `Add-AppxPackage -Path` 正式安装。
  - `.msixupload`（StoreUpload）在本机 dotnet CLI 会因 `Microsoft.Build.Msix` 依赖 VS/net472 收尾崩溃，
    **发版走 CI**（GitHub Actions windows-latest 带 VS）。

- **WindowsAppSDK 必须自包含**（`WindowsAppSDKSelfContained=true`）。框架依赖模式在本机实测：
  自动初始化阶段抛 `REGDB_E_CLASSNOTREG`（`DeploymentInitializeOptions`），进程直接退出。
  代价是包体（msix 33MB / layout 96MB），已用"只引 WinUI 子包 + 剔除 AI/ML"压到可接受范围
  （裁剪前后 85.3MB → 32.7MB，见 `_widbar_spike/REPORT.md §10`）。

## 7. 本机联调记录（2026-10-21，已跑通）

### 7.1 结论

**签名安装（Developer）+ 0.1.9 起，widget 已在 WidBar 1.2.8 里跑起来。**
决定性证据来自 SDK 自己的日志
`%LOCALAPPDATA%\Packages\EastMG.GachaCalendar_8c3jes7x0pqng\LocalState\Logs\widbar-sdk.*.log`：

```
OnLaunched, pipe='widbar.com.eastmg.gachacalendar.c327…'
Pipe connected.
Hello sent for 'com.eastmg.gachacalendar'.
initInstance '648ce38d…'.
Initializing plugin for '648ce38d…'.
Plugin initialized for '648ce38d…'.
Creating preview content for '648ce38d…'.
Preview content created for '648ce38d…'.
DirectPreview: created hwnd=0x4D0D88 parent=0x30054
Instance '648ce38d…' ready (width=344).
DirectPreview: layout win=378x72px dpi=144 rasterScale=1.5 content=252x47dip
Flyout prewarmed for '648ce38d…'.
```

`plugin-health.json` 里 `com.eastmg.gachacalendar@0.1.9` 为 0 失败、未禁用；进程常驻、`Responding=True`。

### 7.2 两个真实坑（都修了）

1. **开发包（松散注册、未签名）连不上宿主**
   - 症状：`UnauthorizedAccessException: Access to the path is denied`，WidBar 侧 `Plugin process exited`；
     官方 Store 组件同时正常。
   - 根因：宿主管道用 `PipeOptions.CurrentUserOnly`（见反编译 `PluginProcessClient.StartAsync`）；
     未签名开发包被激活成 **Low IL** 进程（实测 `S-1-16-4096`），写不到 Medium 的管道对象。
   - 修法：**签名安装**。`SignatureKind: None → Developer` 后进程恢复 Medium，连接成功。
     开发流程因此改为 `tools\install-signed.ps1`（管理员一次导入证书，之后不需要）。

2. **自己加的启动探针会抢占单实例管道**（0.1.8 及以前）
   - 症状：签名后变成 `Pipe disconnected`，SDK 日志只有 `OnLaunched`、没有 `Pipe connected.`。
   - 根因：排障时在 `Program.Main` 里加过"先自己连一次管道"的探针；它和 SDK 抢**同一个单实例**管道，
     连上又立刻断开，宿主与 SDK 都握不上手。
   - 修法：0.1.9 删除该探针。**教训：不要替 SDK 预先连接宿主通道。**

### 7.3 现在怎么排障

- 运行期诊断：`DataDirectory\widget.log`（宿主给的目录，通常
  `%LOCALAPPDATA%\Packages\30113AndreaDelBello.WidBar-WidgetTaskbarsystem_2mj5svk4jk01m\LocalCache\Roaming\WidBar\Data\com.eastmg.gachacalendar\<instance>\widget.log`）
  记录初始化、首帧来源、每次刷新结果（status/error/耗时/条数）。
- SDK 日志：`%LOCALAPPDATA%\Packages\EastMG.GachaCalendar_8c3jes7x0pqng\LocalState\Logs\widbar-sdk.*.log`
- 宿主日志：`…WidBar…\LocalState\Logs\widbar.log`

### 7.4 数据源：已验证抓到真实数据

- widget 进程令牌：`integrity=S-1-16-8192`（Medium）、`isAppContainer=false` —— 签名安装后不再被降级。
- 首帧与刷新链路正常，`startup.log` 里可见：
  `InitializeAsync end` → `CreatePreviewContent; chips=3` → `CreateFlyoutContent`。
- **真实 feed 已缓存**（28 款、`refreshedAt` 为当天、`原神` 的 UP 角色是真实数据）：
  `%LOCALAPPDATA%\Packages\EastMG.GachaCalendar_8c3jes7x0pqng\LocalCache\Roaming\WidBar\Data\com.eastmg.gachacalendar\<instance>\feed.json`
- 为什么"宿主侧"的数据目录看起来是空的：宿主给的 DataDirectory 是
  `%APPDATA%\WidBar\Data\…`，而打包进程写 `%APPDATA%` 会被 AppModel **重定向**到自己包的
  `LocalCache\Roaming`。宿主与 widget 各自看到自己那份，这是**正常隔离，不是 bug**。
  （0.1.11 起 `RuntimePaths` 会显式探测可写目录并记录选择结果，避免下次再误判。）
- 网络本身没问题：`tools/FeedProbe` 用与 widget 完全相同的 `FeedClient` 抓真实 URL，
  实测 `Fresh`、28 条、约 1.2s，并写出 `feed.json/feed.etag/feed.lastmod`。

### 7.5 排障工具

| 工具 | 用途 |
|---|---|
| `tools/FeedProbe` | `dotnet run --project tools\FeedProbe -- <url> <cacheDir>`：单独验证数据源可达性（区分网络问题 vs widget 环境问题） |
| `tools/smoke-host-pipe.ps1` | 假宿主握手回归（需普通终端；DSH 会话里子进程会被降成 Low IL，连不上 CurrentUserOnly 管道） |
| `DataDirectory\widget.log`（0.1.10+） | 初始化、首帧来源、每次刷新的 status/error/耗时/条数 |
| `…\EastMG.GachaCalendar_8c3jes7x0pqng\LocalState\Logs\widbar-sdk.*.log` | SDK 侧握手与 preview 创建过程 |

### 7.6 两个只有实机才会暴露的 UI 坑（0.1.15 / 0.1.16 修）

1. **首帧状态不会刷新**：`WidgetViewModel.Apply` 里 `_status/_isSample` 是**直接改字段**，
   没有发 `PropertyChanged`，于是预览条的「示例」标记永远停在首帧（刷新成功后也不消失）。
   修法：Apply 里显式 `Raise(Status/IsSample/IsCached)`，并补了单测锁住。
   **教训：WinUI 的 x:Bind OneWay 只认 INotifyPropertyChanged，字段赋值不算。**
2. **预览宽度不是"设了就生效"**：`PreviewLogicalWidth` 在 `InitializeAsync` 阶段请求重排时，
   预览窗口还没创建，宿主会忽略；必须在 `CreatePreviewContent()` **之后**再
   `RequestPreviewRefresh()`。另外 chip 实际宽度约 117dip，按 120dip 预留（3 条 = 372dip），
   名称限宽 62dip —— 按 112dip 估算会把第 3 条裁掉。

### 7.7 设计冒烟 harness（不装 WidBar 也能看界面）

```powershell
dotnet build src\GachaWidget\GachaWidget.csproj -c Release -p:Platform=x64
& .\src\GachaWidget\bin\x64\Release\net10.0-windows10.0.19041.0\win-x64\GachaWidget.exe --ui-preview=flyout
```

- `--ui-preview=preview|flyout|settings`：在独立窗口里按**真实 dip 尺寸**渲染对应界面
  （preview 372、flyout 830×580、settings 900×940），用内置示例数据，**不连宿主、不发网络请求**。
- 意义：视觉迭代不必反复签名安装；`docs/images/design-*.png` 就是这么截的
  （Win32 `PrintWindow(PW_RENDERFULLCONTENT)` + `SetProcessDpiAwarenessContext(-4)` 拿物理像素）。
- 注意：这是开发期入口，`App.OnLaunched` 里检测到参数就**不进** `base.OnLaunched`，
  因此不会创建宿主管道；打包进商店的包不受影响（正常启动无该参数）。

