# widbar-gacha-calendar

[![license](https://img.shields.io/github/license/EastMG/widbar-gacha-calendar.svg)](./LICENSE)
![Windows 11](https://img.shields.io/badge/Windows-11-0078D4?logo=windows11&logoColor=white)
![WidBar](https://img.shields.io/badge/WidBar-widget-4D6BFE)

**WidBar 任务栏小组件版的二游卡池 / 活动日历。**

把 `dsh-gacha-calendar` 的 28 款二游排期，做成 WidBar 里的一块任务栏小组件：
任务栏上直接看到"哪款游戏快结束了、还剩多久"，点一下展开完整面板。

> 同系列其它形态：
> [`dsh-gacha-calendar`](https://github.com/EastMG/dsh-gacha-calendar)（DSH 桌面端插件，core 真源）、
> [`gacha-calendar-extension`](https://github.com/EastMG/gacha-calendar-extension)（浏览器扩展）、
> [`gacha-calendar`](https://github.com/EastMG/gacha-calendar)（WPF 版 Windows 任务栏小组件）、
> [`gacha-calendar-app`](https://github.com/EastMG/gacha-calendar-app)（PWA / 移动端）。

## 当前状态

**v0.1 开发中**：WinUI 3 widget 骨架 + 数据层 + 预览条 / 展开面板 / 设置页。

| 能力 | 状态 |
|---|---|
| WidBar 插件契约接入（`com.widbar.widget` AppExtension / `plugin.json`） | 🚧 |
| 任务栏预览条（1–3 条最近结束 + 短倒计时） | 🚧 |
| 展开面板（28 款六列：卡池 / 起止 / 活动 / 倒计时 / 失败三态） | 🚧 |
| 设置页（显隐 / 排序 / 预览条数 / 刷新间隔 / 数据源 / 数据自检） | 🚧 |
| 数据源：静态快照 JSON（`gacha-calendar-app` CI，每 6h） | 🚧 |
| 数据源：内嵌 `gacha-calendar-core` 各端自行抓取（v1） | 📋 |
| 上架微软商店 + 进 WidBar 组件目录 | 📋 |

## 数据从哪来

```
dsh-gacha-calendar（core 真源）
  └─ packages/core → npm: gacha-calendar-core
          ├─ gacha-calendar-app 的 CI（每 6h）→ GitHub Pages 上的 latest.json
          └─ 本仓库（WidBar widget）v0：只读那个 latest.json
```

- feed：`https://eastmg.github.io/gacha-calendar-app/data/latest.json`（形状 = core 的 Result JSON + `meta`）
- 每 6 小时一轮，28 款全量；widget 只读它，**不直接抓任何游戏站**（不触发 CORS / 反爬 / 驱动问题）。
- 倒计时在本地按当前时间实时计算，所以"数据 6 小时前抓的"不影响"还剩多久"的精度。
- 首次运行 / 断网时回退到内置示例快照，并在状态栏标注「示例数据」。

v1 计划：在 widget 进程内嵌 `gacha-calendar-core`（WebView2 或 ClearScript），
由每台机器自己抓 28 个源，恢复"无服务端"的原始设计。届时"来源切换 / 自定义条目 / 解析器自检"才完整可用。

## 快速开始

> 环境：Windows 11 x64、.NET SDK 8（WidBar SDK 目标 `net8.0-windows10.0.19041`）、
> 已安装 [WidBar](https://apps.microsoft.com/detail/9PKLDNM83TP9)、开启开发者模式（侧载需要）。

```powershell
# 1) 数据层单测（零 NuGet，最快）
dotnet run --project tests\GachaCalendar.Core.Tests -c Release

# 2) 编译 widget
dotnet build src\GachaWidget\GachaWidget.csproj -c Release -p:Platform=x64

# 3) 注册 MSIX（开发者模式）后，在 WidBar 的布局页把「二游日历」拖到任务栏
```

详细步骤与打包链路见 [`docs/03-WidBar插件契约.md`](docs/03-WidBar插件契约.md)。

## 项目结构

```
src/GachaCalendar.Core/     壳无关数据层：feed 模型 / HTTP+缓存 / catalog / 可见性 / 行文案 / 视图模型
src/GachaWidget/            WinUI 3 widget：Preview / Flyout / Settings + WidBar 插件入口
tests/GachaCalendar.Core.Tests/  零依赖 console 测试
tools/gen-catalog.mjs       从 core 抽 28 款元信息 → data/catalog.json
data/catalog.json           游戏元信息（名称/图标/默认顺序/默认隐藏/时区）
data/sample-feed.json       真实快照（首次离线兜底）
docs/                       架构 / 数据契约 / WidBar 契约 / 上架清单
```

## 与 WPF 版的关系

- **复用**：数据契约、行文案与倒计时口径（`RowBuilder`）、失败三态语义、catalog 抽取方式。
- **不复用**：WPF 的窗口/任务栏/托盘/单实例/自启——这些在 WidBar 里由宿主负责
  （WidBar 管位置、悬停、主题、生命周期），widget 只提供三块 `UIElement`。
- 因此本仓库**不修改** `gacha-calendar`（WPF 版）与 `dsh-gacha-calendar`。

## 免责声明

- 本项目为非营利性质的个人项目，与各游戏厂商、发行商及官方/社区 Wiki 均不存在隶属或合作关系。
- 卡池、活动等排期数据均取自各游戏**公开的官方公告与社区 Wiki、第三方站**；图标与相关内容的版权归原权利方所有。
- 本项目仅作信息聚合展示，不提供亦不存储任何游戏资源；不收集任何用户数据。
- 如权利人认为本项目展示的内容侵犯其合法权益，请通过 Issue 告知，核实后将尽快删除相关内容。

## License

MIT © 2026 EastMG

[![](https://img.shields.io/badge/powered_by-dsh-4D6BFE.svg?logo=deepseek&logoColor=white)](https://github.com/deepseek-ai/deepseek-harness)
