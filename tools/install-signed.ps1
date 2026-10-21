<#
.SYNOPSIS
    构建并安装「签名版」MSIX（开发期联调用；松散注册 -Register 的包在部分机器上连不上宿主管道）。

.DESCRIPTION
    为什么需要它：本机实测，`Add-AppxPackage -Register`（松散注册）的开发包能被 WidBar 发现并拉起，
    但进程连接宿主管道的 `NamedPipeClientStream.Connect` 会抛 UnauthorizedAccessException，
    WidBar 侧只看到 "Plugin process exited"。官方 Store 签名组件正常。
    因此开发期如果 `-Register` 跑不起来，改用"签名 + 正式安装"这条标准路径验证。

    步骤：
      1) 构建 MSIX（GenerateAppxPackageOnBuild，SideloadOnly，不签名）
      2) 准备代码签名证书（默认用已存在的 CN=EastMG 证书；没有则新建自签证书）
      3) 把证书公钥导入 **LocalMachine\TrustedPeople**（需要管理员；脚本会检查）
      4) signtool 签名 msix
      5) Add-AppxPackage -Path 安装

    注意：签名的证书 Subject 必须与 Package.appxmanifest 的 Publisher 完全一致（当前 CN=EastMG）。

.PARAMETER PfxPath
    已有 .pfx 证书路径（可选）。给了就不再新建证书。

.PARAMETER PfxPassword
    .pfx 密码（可选，配合 -PfxPath）。

.PARAMETER CertThumbprint
    已导入当前用户证书库的证书指纹（可选）。与 -PfxPath 二选一。

.PARAMETER SkipInstall
    只构建 + 签名，不安装。

.EXAMPLE
    # 管理员终端里一次性跑（会把证书装进 LocalMachine\TrustedPeople 并安装）
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\install-signed.ps1

.EXAMPLE
    # 用已有 pfx
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\install-signed.ps1 -PfxPath D:\dev.pfx -PfxPassword pass

.NOTES
    - 必须**在普通桌面终端**里运行（不要在本 DSH 沙箱会话里）：本会话是 Low IL，
      CryptoAPI 被限制，signtool 会报 Store::ImportCertObject() failed (0x80090010)。
    - 脚本会先移除同名旧包，避免 0x80073CFB。
#>
[CmdletBinding()]
param(
    [string]$PfxPath,
    [string]$PfxPassword,
    [string]$CertThumbprint,
    [switch]$SkipInstall
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $repo 'src\GachaWidget\GachaWidget.csproj'
$manifestPath = Join-Path $repo 'src\GachaWidget\Package.appxmanifest'
$publisher = 'CN=EastMG'

# ---- 构建环境（本机 MakePri 需要 TEMP 在工作区内） ----
$env:TEMP = Join-Path $repo '.tmp'
$env:TMP = $env:TEMP
New-Item -ItemType Directory -Force -Path $env:TEMP | Out-Null
$env:DOTNET_CLI_HOME = Join-Path (Split-Path -Parent $repo) '.dotnet-home'
$env:NUGET_PACKAGES = Join-Path (Split-Path -Parent $repo) '.nuget-packages'

Write-Host "==> 构建 MSIX" -ForegroundColor Cyan
& dotnet build $proj -c Release -p:Platform=x64 `
    -p:GenerateAppxPackageOnBuild=true `
    -p:UapAppxPackageBuildMode=SideloadOnly `
    -p:AppxPackageSigningEnabled=false --nologo -v:minimal
if ($LASTEXITCODE -ne 0) { throw "构建失败（exit=$LASTEXITCODE）" }

$msix = Get-ChildItem (Join-Path $repo 'src\GachaWidget\AppPackages') -Recurse -Filter '*.msix' |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $msix) { throw '找不到构建出的 .msix' }
Write-Host "    msix = $($msix.FullName)（$([Math]::Round($msix.Length / 1MB, 1)) MB）" -ForegroundColor DarkGray

# ---- 证书 ----
if ($PfxPath -or $CertThumbprint) {
    if ($PfxPath) {
        if (-not (Test-Path $PfxPath)) { throw "pfx 不存在：$PfxPath" }
        $secure = ConvertTo-SecureString -String $PfxPassword -Force -AsPlainText
        $cert = Import-PfxCertificate -FilePath $PfxPath -Password $secure -CertStoreLocation 'Cert:\CurrentUser\My'
    } else {
        $cert = Get-Item "Cert:\CurrentUser\My\$CertThumbprint"
    }
} else {
    $cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $publisher } | Select-Object -First 1
    if (-not $cert) {
        Write-Host "==> 新建自签证书（$publisher）" -ForegroundColor Cyan
        $cert = New-SelfSignedCertificate -Type Custom -Subject $publisher `
            -KeyUsage DigitalSignature -FriendlyName 'WidBar Gacha Dev' `
            -CertStoreLocation 'Cert:\CurrentUser\My' `
            -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
    }
}
if ($cert.Subject -ne $publisher) {
    throw "证书 Subject '$($cert.Subject)' 与 manifest Publisher '$publisher' 不一致"
}
Write-Host "==> 证书 $($cert.Thumbprint)" -ForegroundColor Cyan

# ---- 导出 pfx 供 signtool 使用 ----
$pfx = Join-Path $env:TEMP 'widbar-dev.pfx'
$pfxPwdPlain = [guid]::NewGuid().ToString('N').Substring(0, 16)
$pfxPwd = ConvertTo-SecureString -String $pfxPwdPlain -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath $pfx -Password $pfxPwd | Out-Null

# ---- 证书必须进 LocalMachine\TrustedPeople 才能装 ----
$trusted = Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object { $_.Thumbprint -eq $cert.Thumbprint }
if (-not $trusted) {
    Write-Host '==> 导入 LocalMachine\TrustedPeople（需要管理员）' -ForegroundColor Yellow
    $cer = Join-Path $env:TEMP 'widbar-dev.cer'
    Export-Certificate -Cert $cert -FilePath $cer -Type CERT | Out-Null
    try {
        Import-Certificate -FilePath $cer -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null
        Write-Host '    已导入' -ForegroundColor Green
    } catch {
        throw "导入 LocalMachine\TrustedPeople 失败：$($_.Exception.Message)`n请用管理员终端重跑本脚本，或手动执行：`n  Import-Certificate -FilePath '$cer' -CertStoreLocation Cert:\LocalMachine\TrustedPeople"
    }
} else {
    Write-Host '==> 证书已在 LocalMachine\TrustedPeople' -ForegroundColor DarkGray
}

# ---- 签名 ----
$signtool = Get-ChildItem (Join-Path $env:NUGET_PACKAGES 'microsoft.windows.sdk.buildtools') -Recurse -Filter 'signtool.exe' |
    Where-Object { $_.FullName -match '\\x64\\' } | Select-Object -First 1
if (-not $signtool) { throw '找不到 signtool.exe（Windows SDK BuildTools 未还原？）' }

Write-Host '==> 签名' -ForegroundColor Cyan
$signed = $msix.FullName -replace '\.msix$', '.signed.msix'
Copy-Item $msix.FullName $signed -Force
& $signtool.FullName sign /fd SHA256 /f $pfx /p $pfxPwdPlain /td SHA256 $signed
if ($LASTEXITCODE -ne 0) { throw "signtool 失败（exit=$LASTEXITCODE）。若是 0x80090010 / Store::ImportCertObject，说明当前会话被限制了 CryptoAPI，请在普通终端重试。" }

if ($SkipInstall) {
    Write-Host "==> 跳过安装；签名包：$signed" -ForegroundColor Yellow
    return
}

# ---- 安装 ----
Write-Host '==> 移除同名旧包并安装' -ForegroundColor Cyan
Get-AppxPackage -Name 'EastMG.GachaCalendar' -ErrorAction SilentlyContinue |
    ForEach-Object { Remove-AppxPackage -Package $_.PackageFullName -ErrorAction SilentlyContinue }
Add-AppxPackage -Path $signed -ForceApplicationShutdown

$pkg = Get-AppxPackage -Name 'EastMG.GachaCalendar'
Write-Host ''
Write-Host '==> 完成' -ForegroundColor Green
Write-Host "    PFN        = $($pkg.PackageFamilyName)"
Write-Host "    Version    = $($pkg.Version)"
Write-Host "    Signature  = $($pkg.SignatureKind)"
Write-Host "    Install    = $($pkg.InstallLocation)"
Write-Host ''
Write-Host '下一步：打开 WidBar，在 Layout 页把「二游日历」拖到任务栏空位；若仍失败，看 WidBar 日志里是否还有 "Plugin process exited"。'
