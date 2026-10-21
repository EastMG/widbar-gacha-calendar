# 变更记录

格式：每条一行，版本号与 `src/GachaWidget/GachaWidget.csproj` 的 `<Version>` 对应。

## v0.1.23（2026-10-21）

- style: **任务栏预览条去掉卡片/边框，改为原生"图标 + 两行文字"**（对齐 Windows 11 天气小组件形态）：
  - 左：游戏图标（20dip，失败退化首字块）；右：主行「剩余时间（紧迫度上色）+ 截止日」，
    次行「◆卡池 / ⚑活动 + 内容标题」；不再画白底圆角卡片，任务栏上不再像"贴了三块膏药"。
  - 3 条总宽 474dip（原 582dip），更接近原生小组件的体量。
- fix: 补 `ChipVm.IconUrl`（图标由 catalog 带入），修正 XAML chip 宽度与插件计算不一致导致的裁切。
- 验证：build 0 warning/0 error；tests 445；深浅色 harness 预览截图正常。

## v0.1.22（2026-10-21）

- feat: **任务栏预览条信息量重做**（用户反馈"展示了个寂寞"）：
  - chip 从单行"游戏 + 几小时"改成两行：① 游戏色点 + 游戏名 + 倒计时（紧迫度上色）+ 截止日；
    ② 类型图案（◆ 卡池 / ⚑ 活动）+ 内容标题（卡池=UP 角色、活动=活动名，长标题折两行）。
  - chip 宽 120 → 180（3 条共 582dip），并始终预留"示例/缓存"小圆点宽度，避免状态切换把最后一条挤出窗口。
  - tooltip 增加"截止：MM-dd HH:mm"。
- test: 新增 11 条 chip 断言（标题 / 类型 / 图案 / 截止 / 紧迫度 / 代表色 / tooltip），共 **445** 断言。
- docs: README 与 C 段验收预期更新为新预览条。

## v0.1.21（2026-10-21）

- fix: 设置页「任务栏预览条显示几条」从 NumberBox 换成 **ComboBox（1/2/3 条）**。
  原因：WinUI NumberBox 的 `Compact` 模式在点右侧箭头时会弹出独立的数字滚动 Popup
  （用户反馈"右上角这个数字滚动按钮怎么这么大"）；1–3 三选一本来就不需要步进器，
  换成下拉后与「刷新间隔」一致，也不再有弹层。
- docs: `docs/07-设置页设计.md` 记一条控件选型规则；设置页截图更新。

## v0.1.20（2026-10-21）

- fix: 设置页对齐 **宿主 chrome 契约**（`SettingsHostWindow` 1120×820 / 滚动区 Padding 24,16,24,8）：
  去掉内层 ScrollViewer（双滚动）、去掉自带 Padding 与 MaxWidth=760 的窄栏，内容铺满宿主宽度；
  卡片行高 40 → 56，与 Windows 设置节奏一致。
- feat: 设计 harness 的 settings 视图 1:1 模拟宿主 chrome（48 标题栏 + 滚动区 + 底部保存/取消），
  截图与真机一致；新增 `docs/07-设置页设计.md`（宿主契约、设计令牌、方案对比与备选）。
- docs: 归档旧表单 `docs/design/settings-form-0.1.0.xaml`（仅参考）；docs/03 补 §7.8。
- 验证：build 0 warning/0 error；Core 单测 434；harness 深浅色设置页截图正常。

## v0.1.19（2026-10-21）

- feat: 设计 harness 支持 `--theme=light|dark`，并实拍浅色主题三张截图（`docs/images/design-*-light.png`），
  验证 ThemeResource 在浅色下同样成立（用户 WidBar 配置为浅色）。
- docs: README 补充浅色截图与 harness 用法。

## v0.1.18（2026-10-21）

- style: 面板密度微调 —— 「近期」保留 4 条（不裁切、也不抢表格高度），「下一个截止」卡完整显示；
  表格标题加 SemiBold 强化层级；图例改成中性色 + 图案语义（不再用强调色，避免误读"蓝色=卡池"）。
- docs: `docs/images/design-flyout.png` 更新为 0.1.18 截图。

## v0.1.17（2026-10-21）

- feat: **视觉与信息层级重做**（对齐 DSH `newPanel` 日程视图 + WinUI 3 Fluent）：
  - 展开面板：头部总览（24h/7d）+ 日期胶囊 + 刷新；「近期 · 按紧迫度」跨游戏列表；
    「下一个截止」卡（游戏色顶条、大号剩余、下方两件）；**14 天迷你条游戏表**
    （颜色=游戏、卡池深/活动浅、实心=在期 / 半透明=已结束 / 短条=长期 / 描边=未开始）；
    中性色图例 + 抓取状态 + 上次更新。
  - 预览条：游戏色点 + 名称 + 加粗倒计时胶囊（去掉边框噪音），示例/缓存状态改成小圆点。
  - 设置页：Windows 设置式 Fluent 卡片（分区标题 + 说明 + 卡片行 + ToggleSwitch/NumberBox/ComboBox），
    深浅色全走 `ThemeResource`。
- feat: Core 新增 `ScheduleBuilder` / `ScheduleItem` / `ScheduleSnapshot`（五态、紧迫度、Recent/NextUp/NextTwo、
  14 天窗口），catalog 增加每款游戏代表色（28/28）；VM 暴露 `Schedule/Recent/NextUp/NextTwo/GlanceText`，
  `RowVm.Gacha/Event` 挂载到表格；单测 276 → **434 断言**。
- feat: 设计冒烟 harness `--ui-preview=preview|flyout|settings`（按真实尺寸渲染、可截图迭代，
  不连宿主、不发网络请求），截图进 `docs/images/design-*.png`。
- docs: README 界面区换成新版截图；docs/06 保持 v1 路线不变。

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
