# 变更记录

格式：每条一行，版本号与 `src/GachaWidget/GachaWidget.csproj` 的 `<Version>` 对应。

## v0.1.16（2026-10-21）

- fix: 预览 chip 宽度收紧到 120dip（游戏名 62dip 截断），3 条正好 372dip；metadata 同步 372。
- test: 补「状态变更通知」单测（Apply Sample→Fresh→Cached 必须发 PropertyChanged），共 276 断言。
- ci: 推送到 GitHub 后 CI 用 VS MSBuild 出 `.msixupload`（dotnet CLI 会因 System.Security.Permissions 崩溃）。

## v0.1.15（2026-10-21）

- fix: 预览条「示例 / 缓存」标记跟随数据状态刷新 —— 根因是 `WidgetViewModel.Apply` 直接改字段
  没有发 `PropertyChanged`（Status/IsSample/IsCached），外壳绑定永远停在首帧状态。
- fix: 预览创建完成后再 `RequestPreviewRefresh()`（初始化阶段的请求发生在窗口创建前，宿主会忽略）。

## v0.1.14（2026-10-21）

- refactor: 抽出 `IFeedProvider` 数据源接口（`FeedClient` 为其 v0 实现），插件只依赖接口 ——
  v1 换成"内嵌 gacha-calendar-core 的 EngineFeedProvider"时 UI/视图模型零改动。
- docs: 新增 [`docs/06-v1路线.md`](docs/06-v1路线.md)：引擎选型（WebView2 优先）、transport/storage 注入、
  分阶段任务与验收标准。

## v0.1.13（2026-10-21）

- fix: 预览宽度按实际 chip 数重算（124dip/条），初始化后 `RequestPreviewRefresh()` 让宿主重排，
  解决 3 条只显示 1.5 条被裁掉的问题；`WidBarPluginPreviewWidth` 同步为 384。
- fix: 面板列宽重排（150/175/125/175/*），活动起止不再被挤成两行；右上角给设置齿轮留 46dip。
- docs: 实拍三张界面截图进 `docs/images/`（150% 缩放下用 PrintWindow + PMv2 DPI 捕获）。

## v0.1.12（2026-10-21）

- feat: 商店图标素材（`tools/make-icons.py` → 44/48/50/71/150/310/620），替换占位图。
- docs: docs/03 §7.4 更正数据源结论（真实 feed 已缓存；宿主目录"空"是 AppModel 重定向的正常表现），
  新增 §7.5 排障工具表；README 状态更新为"已跑通"。

## v0.1.11（2026-10-21）

- fix: 宿主给的 DataDirectory 未必可写（widget 与宿主是两个 MSIX 包）→ 新增
  `RuntimePaths`：宿主目录 → 本包 LocalState → 临时目录，逐级做写测试，缓存与日志落到真正可写的位置。
- feat: 预览条在数据非实时时显示「示例 / 缓存」小标记，不再把兜底数据伪装成实时。
- feat: 引入 `tools/FeedProbe`：用与 widget 完全相同的 `FeedClient` 抓真实 feed，
  区分"网络/TLS 问题"与"widget 运行环境问题"（实测 Fresh、28 条、1.2s）。
- test: `tools/smoke-host-pipe.ps1`（假宿主握手回归）。

## v0.1.10（2026-10-21）

- feat: 运行期诊断日志落到 `DataDirectory\widget.log`（初始化、首帧来源、刷新 status/error/耗时/条数、
  面板/设置创建）。
- docs: docs/03 §7 记录"已在 WidBar 内跑通"的 SDK 日志证据与两个真实坑（未签名包 Low IL、
  自诊断探针抢占管道）。
## v0.1.4（2026-10-21）

- build: WindowsAppSDK 改为自包含（修复框架依赖模式下 `REGDB_E_CLASSNOTREG` 无法启动），
  并只引 WinUI 所需子包 + 剔除 AI/ML 传递依赖：msix **85.31MB → 32.71MB**，layout 222.66MB → 95.67MB。
- feat: 任务栏预览条 / 28 款六列展开面板 / 完整设置页（显隐、排序、预览条数、刷新间隔、数据源、数据检查）落地。
- feat: `tools/build-and-register.ps1` 一键构建+注册；`tools/install-signed.ps1` 签名安装（开发期联调用）。
- docs: 记录本机联调阻塞点（松散注册的 dev 包连宿主管道被 ACL 拒绝）与签名安装的下一步。
- 验证：Core 268 断言通过；`dotnet build` 0 error；`-Register` 后 WidBar 日志出现
  `Discovered 'com.eastmg.gachacalendar' v0.1.4` 与插件图标；管道连接在当前部署形态下被拒（见 docs/03 §7）。

## v0.1.0（首个可运行版本，已被 0.1.4 取代）

- feat: 首个可运行版本 —— WidBar 插件契约接入（`com.eastmg.gachacalendar`）、任务栏预览条、28 款六列展开面板、完整设置页（显隐/排序/预览条数/刷新间隔/数据源/数据检查）。
- feat: 壳无关数据层 `GachaCalendar.Core`（net8.0，零 NuGet）：feed 模型、HTTP+ETag+原子缓存+示例兜底、catalog（28 款，来自 `gacha-calendar-core` 0.12.5）、可见性/排序、行文案与失败三态（从 WPF 版逐条移植）。
- feat: `tools/gen-catalog.mjs` 从 core 抽取目录（28/28 图标、默认隐藏 5 款、时区偏移）。
- test: 数据层 268 条断言（ParseRange 跨年、文案口径、失败三态、可见性、chips、FeedClient 的 200/304/500/断网/示例兜底、设置 Normalize）。
- build: 单项目 MSIX（net10.0-windows10.0.19041.0），本机 `dotnet build` + `Add-AppxPackage -Register` 已验证；`.msixupload` 与符号包留给 CI（本机无 VS/Windows SDK）。
