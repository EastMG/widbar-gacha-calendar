# 变更记录

格式：每条一行，版本号与 `src/GachaWidget/GachaWidget.csproj` 的 `<Version>` 对应。

## v0.1.0（开发中）

- feat: 首个可运行版本 —— WidBar 插件契约接入（`com.eastmg.gachacalendar`）、任务栏预览条、28 款六列展开面板、完整设置页（显隐/排序/预览条数/刷新间隔/数据源/数据检查）。
- feat: 壳无关数据层 `GachaCalendar.Core`（net8.0，零 NuGet）：feed 模型、HTTP+ETag+原子缓存+示例兜底、catalog（28 款，来自 `gacha-calendar-core` 0.12.5）、可见性/排序、行文案与失败三态（从 WPF 版逐条移植）。
- feat: `tools/gen-catalog.mjs` 从 core 抽取目录（28/28 图标、默认隐藏 5 款、时区偏移）。
- test: 数据层 268 条断言（ParseRange 跨年、文案口径、失败三态、可见性、chips、FeedClient 的 200/304/500/断网/示例兜底、设置 Normalize）。
- build: 单项目 MSIX（net10.0-windows10.0.19041.0），本机 `dotnet build` + `Add-AppxPackage -Register` 已验证；`.msixupload` 与符号包留给 CI（本机无 VS/Windows SDK）。
