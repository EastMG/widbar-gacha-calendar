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

## 6. 构建与侧载（本机 Low IL 环境的注意点）

- 本机 DSH 会话运行在 **Low 完整性级别**，MakePri.exe 写 `%TEMP%`（Medium IL）会被拒（0x80070005）。
  构建前必须把 `TEMP`/`TMP` 指到工作区内目录：

```powershell
$env:TEMP='F:\DeepSeek\DeepSeek-Workplace\DSH-Workplace-01\widbar-gacha-calendar\.tmp'
$env:TMP=$env:TEMP
$env:DOTNET_CLI_HOME='F:\DeepSeek\DeepSeek-Workplace\DSH-Workplace-01\.dotnet-home'
$env:NUGET_PACKAGES='F:\DeepSeek\DeepSeek-Workplace\DSH-Workplace-01\.nuget-packages'
dotnet build src\GachaWidget\GachaWidget.csproj -c Release -p:Platform=x64
```

- 侧载：开发者模式下 `Add-AppxPackage -Register <layout>\AppxManifest.xml`；若被 Low IL 挡住，
  在**普通终端**用同一命令注册（`docs/05-验收清单.md` 有逐步清单）。
