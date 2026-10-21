# 变更记录

格式：每条一行，版本号与 `src/GachaWidget/GachaWidget.csproj` 的 `<Version>` 对应。

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
