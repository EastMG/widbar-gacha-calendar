#Requires -Version 5.1
<#
.SYNOPSIS
    构建 WidBar 小组件（单项目 MSIX）并就地注册，供本地调试。

.DESCRIPTION
    本脚本按本机（DSH 工作区）已验证的约束工作：
      1) 环境变量重定向：DOTNET_CLI_HOME / NUGET_PACKAGES / TEMP / TMP 全部指向工作区内。
         （本机 MSIX 工具链 makepri.exe / makeappx.exe 只能写 Low 完整性标签目录，
           默认 %TEMP% 在 C: 是 Medium 标签，PRI 生成会报 0x80070005；
           把 TEMP 指到工作区内可 100% 规避。）
      2) dotnet build src\GachaWidget\GachaWidget.csproj -c <Configuration> -p:Platform=<Platform>
      3) 在 layout 目录（含 AppxManifest.xml 的那个目录）执行
         Add-AppxPackage -Register AppxManifest.xml（开发者模式，无需签名）
      4) 打印 PFN / 版本 / Status / 插件清单信息。

    注意：同一个 Version 已注册过时，Windows 会拒绝重复注册（0x80073CFB），
          所以脚本会先 Remove-AppxPackage 再重新注册（只删注册，不删文件）。

.PARAMETER Configuration
    构建配置，默认 Release。

.PARAMETER Platform
    目标平台，默认 x64。

.PARAMETER Clean
    先删除 GachaWidget / GachaCalendar.Core 的 bin、obj 再构建。

.PARAMETER NoRegister
    只构建、不注册（CI / 只想看编译结果时用）。

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\build-and-register.ps1
.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\build-and-register.ps1 -Clean
.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\build-and-register.ps1 -NoRegister
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [ValidateSet('x64', 'ARM64')]
    [string]$Platform = 'x64',

    [switch]$Clean,

    [switch]$NoRegister
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

# ---------------------------------------------------------------------------
# 0. 编码（中文提示不乱码）
# ---------------------------------------------------------------------------
try {
    chcp 65001 | Out-Null
    [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
    $OutputEncoding = [System.Text.Encoding]::UTF8
} catch { }

function Write-Step([string]$Text) { Write-Host ''; Write-Host "==> $Text" -ForegroundColor Cyan }
function Write-Ok([string]$Text) { Write-Host "    [OK] $Text" -ForegroundColor Green }
function Write-Warn2([string]$Text) { Write-Host "    [WARN] $Text" -ForegroundColor Yellow }
function Write-Info([string]$Text) { Write-Host "    $Text" }

# ---------------------------------------------------------------------------
# 1. 路径与环境
# ---------------------------------------------------------------------------
$RepoRoot = Split-Path -Parent $PSScriptRoot
$WorkspaceRoot = Split-Path -Parent $RepoRoot

$ProjectPath = Join-Path $RepoRoot 'src\GachaWidget\GachaWidget.csproj'
$CoreProject = Join-Path $RepoRoot 'src\GachaCalendar.Core\GachaCalendar.Core.csproj'

if (-not (Test-Path $ProjectPath)) {
    throw "找不到 widget 工程：$ProjectPath"
}

# 工作区根目录（放 NuGet 缓存/CLI home）。若本脚本被单独拷走，退化为仓库根。
if (-not (Test-Path (Join-Path $WorkspaceRoot '.nuget-packages'))) {
    $WorkspaceRoot = $RepoRoot
}

$DotnetHome = Join-Path $WorkspaceRoot '.dotnet-home'
$NugetPackages = Join-Path $WorkspaceRoot '.nuget-packages'
$BuildTemp = Join-Path $RepoRoot '.tmp'
$TempPrefix = 'widbar-build-'

$env:DOTNET_CLI_HOME = $DotnetHome
$env:NUGET_PACKAGES = $NugetPackages
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:TEMP = $BuildTemp
$env:TMP = $BuildTemp

foreach ($d in @($DotnetHome, $NugetPackages, $BuildTemp)) {
    if (-not (Test-Path $d)) { New-Item -ItemType Directory -Force -Path $d | Out-Null }
}

Write-Step "环境"
Write-Info "仓库          : $RepoRoot"
Write-Info "工作区（缓存）: $WorkspaceRoot"
Write-Info "DOTNET_CLI_HOME = $env:DOTNET_CLI_HOME"
Write-Info "NUGET_PACKAGES  = $env:NUGET_PACKAGES"
Write-Info "TEMP/TMP        = $env:TEMP   （必须在工作区内，否则 MakePri 会 0x80070005）"
Write-Info "Configuration   = $Configuration   Platform = $Platform"

# ---------------------------------------------------------------------------
# 2. 可选：清理
# ---------------------------------------------------------------------------
if ($Clean) {
    Write-Step "清理 bin/obj"
    $targets = @(
        (Join-Path $RepoRoot 'src\GachaWidget\bin'),
        (Join-Path $RepoRoot 'src\GachaWidget\obj'),
        (Join-Path $RepoRoot 'src\GachaCalendar.Core\bin'),
        (Join-Path $RepoRoot 'src\GachaCalendar.Core\obj')
    )
    foreach ($t in $targets) {
        if (Test-Path $t) {
            Remove-Item -Recurse -Force $t -ErrorAction SilentlyContinue
            Write-Info "已删除 $t"
        }
    }
}

# ---------------------------------------------------------------------------
# 3. 构建
# ---------------------------------------------------------------------------
Write-Step "dotnet build $ProjectPath"
$buildArgs = @('build', $ProjectPath, '-c', $Configuration, ('-p:Platform=' + $Platform), '-nologo', '-v:minimal')
Write-Info ('dotnet ' + ($buildArgs -join ' '))
$sw = [Diagnostics.Stopwatch]::StartNew()
& dotnet @buildArgs
$buildExit = $LASTEXITCODE
$sw.Stop()

if ($buildExit -ne 0) {
    Write-Host ''
    Write-Host "构建失败（exit=$buildExit，用时 $([math]::Round($sw.Elapsed.TotalSeconds,1))s）。" -ForegroundColor Red
    Write-Host '提示：若是 MakePri 报 0x80070005，检查 TEMP/TMP 是否真的指向工作区内。' -ForegroundColor Red
    exit $buildExit
}
Write-Ok ("构建成功，用时 " + [math]::Round($sw.Elapsed.TotalSeconds, 1) + "s")

# ---------------------------------------------------------------------------
# 4. 找 layout（含 AppxManifest.xml 的输出目录）
# ---------------------------------------------------------------------------
Write-Step "定位 layout"
$binRoot = Join-Path $RepoRoot 'src\GachaWidget\bin'
if (-not (Test-Path $binRoot)) { throw "没有 bin 目录：$binRoot" }

$manifests = @(Get-ChildItem -Path $binRoot -Recurse -Filter 'AppxManifest.xml' -File -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match [regex]::Escape($Configuration) } |
    Sort-Object LastWriteTime -Descending)

if ($manifests.Count -eq 0) {
    throw "在 $binRoot 下没找到 AppxManifest.xml（构建没走到 MSIX 打包？）"
}

$layoutDir = $manifests[0].Directory.FullName
Write-Ok "layout = $layoutDir"

# 读生成的 manifest（Identity / AppExtension）
$manifestPath = Join-Path $layoutDir 'AppxManifest.xml'
[xml]$manifestXml = Get-Content $manifestPath -Encoding UTF8
$nsm = New-Object System.Xml.XmlNamespaceManager($manifestXml.NameTable)
$nsm.AddNamespace('f', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10')
$nsm.AddNamespace('uap3', 'http://schemas.microsoft.com/appx/manifest/uap/windows10/3')
$identity = $manifestXml.SelectSingleNode('/f:Package/f:Identity', $nsm)
$pkgName = $identity.Name
$pkgVersion = $identity.Version
Write-Info "包名    = $pkgName"
Write-Info "版本    = $pkgVersion"

$pluginJson = Join-Path $layoutDir 'Public\plugin.json'
if (Test-Path $pluginJson) {
    Write-Ok "Public\plugin.json 存在（WidBar 发现插件用）"
    try {
        $pj = Get-Content $pluginJson -Raw -Encoding UTF8 | ConvertFrom-Json
        Write-Info ("plugin.json: id=" + $pj.id + " name=" + $pj.name + " exe=" + $pj.executable + " previewWidth=" + $pj.previewWidth + " configurable=" + $pj.configurable)
    } catch { Write-Warn2 'plugin.json 解析失败（内容可能不完整）' }
} else {
    Write-Warn2 "缺少 Public\plugin.json —— WidBar 将发现不了这个 widget！"
}

$extNodes = @($manifestXml.SelectNodes('//uap3:AppExtension', $nsm))
if ($extNodes.Count -gt 0) {
    foreach ($ext in $extNodes) {
        Write-Info ("AppExtension: Name=" + $ext.Name + " Id=" + $ext.Id + " PublicFolder=" + $ext.PublicFolder)
        if ($ext.Name -ne 'com.widbar.widget') { Write-Warn2 "AppExtension Name 不是 com.widbar.widget，WidBar 不会识别" }
    }
} else {
    Write-Warn2 'manifest 里没有 windows.appExtension —— WidBar 不会识别这个包'
}

# layout 体积（便于盯包体）
$layoutBytes = (Get-ChildItem -Recurse $layoutDir -File -ErrorAction SilentlyContinue | Measure-Object -Property Length -Sum).Sum
if ($layoutBytes) { Write-Info ("layout 体积 = " + [math]::Round($layoutBytes / 1MB, 2) + " MB") }

# ---------------------------------------------------------------------------
# 5. 注册（松散注册，需开发者模式）
# ---------------------------------------------------------------------------
if ($NoRegister) {
    Write-Step "-NoRegister：跳过注册"
    Write-Host ''
    Write-Host 'layout 路径：' -ForegroundColor Cyan
    Write-Host "    $layoutDir" -ForegroundColor Cyan
    exit 0
}

Write-Step "注册到当前用户（Add-AppxPackage -Register）"

# 同版本重复注册会 0x80073CFB，先移除旧注册（只删注册，不删文件）
$existing = @(Get-AppxPackage -Name $pkgName -ErrorAction SilentlyContinue)
if ($existing.Count -gt 0) {
    foreach ($e in $existing) {
        Write-Info ("移除旧注册：" + $e.PackageFullName)
        try { Remove-AppxPackage -Package $e.PackageFullName -ErrorAction Stop }
        catch { Write-Warn2 ("移除失败（继续尝试注册）：" + $_.Exception.Message.Split([char]10)[0]) }
    }
}

try {
    Add-AppxPackage -Register $manifestPath -ErrorAction Stop
} catch {
    Write-Host ''
    Write-Host '注册失败。' -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host ''
    Write-Host '常见原因：' -ForegroundColor Yellow
    Write-Host '  - 开发者模式没开：设置 → 隐私和安全性 → 开发者选项 → 开发人员模式' -ForegroundColor Yellow
    Write-Host '  - 包依赖的 WindowsAppRuntime 框架包缺失（框架依赖模式下需要安装 Microsoft.WindowsAppRuntime.2）' -ForegroundColor Yellow
    Write-Host '  - manifest 校验失败（看上面完整的 Deployment failed 详情）' -ForegroundColor Yellow
    exit 1
}

# ---------------------------------------------------------------------------
# 6. 结果
# ---------------------------------------------------------------------------
Write-Step "注册结果"
$pkg = Get-AppxPackage -Name $pkgName
if (-not $pkg) {
    Write-Host '注册后仍查不到包，请查看 AppXDeployment 事件日志。' -ForegroundColor Red
    exit 1
}

Write-Ok ("PFN       = " + $pkg.PackageFullName)
Write-Ok ("Name      = " + $pkg.Name)
Write-Ok ("Version   = " + $pkg.Version)
Write-Ok ("Status    = " + $pkg.Status + "   SignatureKind = " + $pkg.SignatureKind)
Write-Ok ("Install   = " + $pkg.InstallLocation)

if ($pkg.Status -ne 'Ok') {
    Write-Warn2 "包状态不是 Ok（$($pkg.Status)），WidBar 可能无法启动它。"
}

Write-Host ''
Write-Host '下一步：' -ForegroundColor Cyan
Write-Host '  1) 启动 WidBar（已安装的话它会自动扫到新 widget；必要时重启 WidBar）' -ForegroundColor Cyan
Write-Host '  2) 在 WidBar 的 Layout 页把「二游日历」拖到任务栏' -ForegroundColor Cyan
Write-Host '  3) 改代码后重跑本脚本（内置先移除旧注册，避免 0x80073CFB）' -ForegroundColor Cyan
Write-Host ('  4) 卸载：Get-AppxPackage -Name ' + $pkgName + ' | Remove-AppxPackage') -ForegroundColor Cyan
Write-Host ''

exit 0
