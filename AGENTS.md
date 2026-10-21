# AGENTS.md — 本仓库的开发约定

> 面向接手本仓库的 AI 会话 / 贡献者。**先读这个，再动代码。**
> 与姊妹仓库 `dsh-gacha-calendar` / `gacha-calendar` / `gacha-calendar-extension` 保持同一口径。

## 0. 这个仓库是什么

**WidBar 任务栏小组件版的二游卡池 / 活动日历。**

它不重复实现抓取：v0 的排期数据来自静态快照 JSON
（由 `gacha-calendar-app` 仓库的 GitHub Actions 每 6 小时抓一轮产出），
v1 计划升级为内嵌 [`gacha-calendar-core`](https://www.npmjs.com/package/gacha-calendar-core)
由每台机器自己联网。

本仓库负责：WidBar 插件契约、WinUI 3 界面、设置、打包、上架材料。

## 1. 目录职责（唯一真源）

| 目录 | 职责 | 允许手改 |
|---|---|---|
| `src/GachaCalendar.Core/` | 壳无关数据层：feed 模型 / 抓取缓存 / catalog / 可见性 / 行文案 / 视图模型 | ✅ |
| `src/GachaWidget/` | WinUI 3 widget（WidBar 插件）：预览条 / 展开面板 / 设置页 | ✅ |
| `tests/GachaCalendar.Core.Tests/` | 数据层与视图模型测试（零 NuGet 依赖的 console 运行器） | ✅ |
| `tools/gen-catalog.mjs` | 从 core 抽取 28 款元信息 → `data/catalog.json` | ✅ |
| `data/catalog.json` | 游戏元信息（名称 / 图标 / 默认顺序 / 默认隐藏 / 时区） | ⚠️ 由工具生成 |
| `data/sample-feed.json` | 真实抓取的示例快照（首次离线兜底） | ⚠️ 由 `tools/fetch-sample-feed.mjs` 生成 |
| `docs/` | 架构 / 数据契约 / WidBar 契约 / 上架清单 | ✅ |

## 2. 构建与验证门禁（每次改动都要绿）

```powershell
# 1) 数据层 + 视图模型单测（零 NuGet，最快）
dotnet run --project tests\GachaCalendar.Core.Tests -c Release

# 2) 数据层编译
dotnet build src\GachaCalendar.Core\GachaCalendar.Core.csproj -c Release

# 3) widget 编译（需要 Windows App SDK 包还原；见 docs/03）
dotnet build src\GachaWidget\GachaWidget.csproj -c Release -p:Platform=x64

# 4) catalog 生成幂等（改了 data/catalog.json 时）
node tools\gen-catalog.mjs --check
```

CI 跑 1 + 2 + 3；真抓数据不进 CI（受源站可用性影响）。

## 3. 版本号与发布纪律

- **代码改动 = 升 `Version`**（`GachaWidget.csproj` / `GachaCalendar.Core.csproj`），并在 `CHANGELOG.md` 记一行。
- 版本号持续在 `0.1.x` 内递增；**没有用户明确指令，不得升到 `0.2.0` 或 `1.0`**。
- 纯文档 / 文案改动不升版本号。
- `git tag` / GitHub Release / 商店提交：**只有用户当轮明确点名才做**；「改完了」「继续」不算授权。
- 提交信息格式：`<type>: vX.Y.Z <一句中文>`（type ∈ feat/fix/refactor/docs/chore/build/style），
  body 用 `- ` 分条，最后一条写验证结果。

## 4. 硬边界（别踩）

- **不在本仓库复制抓取逻辑**。解析器只在 `gacha-calendar-core` 里改；本仓库只消费数据。
- **v0 不在 UI 层发第三方源请求**。只允许请求 `feedUrl`（默认 `gacha-calendar-app` 的静态快照）。
  这样不触发 CORS / Referer / 反爬问题，也不需要内核驱动与 WebView2。
- **不收集任何用户数据、不加遥测**。隐私口径见 `docs/04-商店上架清单.md`。
- **包体要控**：不打包 Node / WebView2 / JS 引擎（v0）。上架微软商店，包体直接影响审核与安装体验。
- 图标外链、排期内容版权归原权利方；只做信息聚合展示，权利人可要求下架。

## 5. 契约

- feed JSON 形状与 core 的 Result JSON 一致，见 `docs/02-数据契约.md`；改形状要同时改文档并升 `schemaVersion`。
- WidBar 插件契约见 `docs/03-WidBar插件契约.md`；SDK 版本固定在 `WidBar.SDK 2.1.0`，升级需单独提交并记 CHANGELOG。

## 6. 提交身份（沿用姊妹仓库事故后的规矩）

```powershell
git config user.name  'EastMG'
git config user.email 'eastmg@users.noreply.github.com'
git log -1 --format='%h | %an <%ae>'
```

- GitHub 账号名、npm 账号名、提交邮箱是三样不同的东西，不要混用。
- **不要为了过 CI 去改身份检查白名单。**
