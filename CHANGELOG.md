# 变更记录

格式：每条一行，版本号与 `src/GachaWidget/GachaWidget.csproj` 的 `<Version>` 对应。

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
