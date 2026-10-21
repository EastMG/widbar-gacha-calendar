<#
.SYNOPSIS
    本地"假宿主"冒烟测试：不依赖 WidBar，验证 widget 进程能连上宿主管道并完成握手/初始化。

.DESCRIPTION
    WidBar 的插件协议是**换行分隔的 JSON**（WireProtocol v2）：
      宿主：创建 CurrentUserOnly 命名管道 → 启动 widget exe（--widbar-pipe=<name>）
      widget：连上管道 → 发 hello → 等 hostInfo → 等 initInstance → 回 instanceReady
    本脚本扮演宿主，把这条链路跑一遍，用来在无法操作 WidBar 时回归"进程能连上、能初始化"。

    验证输出：脚本会打印每一条收到的消息；出现 `instanceReady` 即表示
    InitializeAsync + CreatePreviewContent 都跑通了。

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\smoke-host-pipe.ps1
#>
[CmdletBinding()]
param(
    [string]$ExePath,
    [int]$TimeoutSeconds = 12
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (-not $ExePath) {
    $ExePath = Join-Path $repo 'src\GachaWidget\bin\x64\Release\net10.0-windows10.0.19041.0\win-x64\GachaWidget.exe'
}
if (-not (Test-Path $ExePath)) {
    throw "找不到 widget exe：$ExePath（先用 tools\build-and-register.ps1 或 dotnet build 构建）"
}

$pill = [guid]::NewGuid().ToString('N')
$pipeName = "widbar.com.eastmg.gachacalendar.smoke$pill"
Write-Host "==> 假宿主：$pipeName" -ForegroundColor Cyan

$server = [System.IO.Pipes.NamedPipeServerStream]::new(
    $pipeName,
    [System.IO.Pipes.PipeDirection]::InOut,
    1,
    [System.IO.Pipes.PipeTransmissionMode]::Byte,
    [System.IO.Pipes.PipeOptions]::Asynchronous -bor [System.IO.Pipes.PipeOptions]::CurrentUserOnly)

$proc = $null
try {
    $proc = Start-Process -FilePath $ExePath -ArgumentList "--widbar-pipe=$pipeName" -PassThru `
        -RedirectStandardError (Join-Path $env:TEMP 'smoke-widget.err.txt') `
        -RedirectStandardOutput (Join-Path $env:TEMP 'smoke-widget.out.txt')
    Write-Host "    widget pid=$($proc.Id)"

    $wait = $server.WaitForConnectionAsync()
    if (-not $wait.Wait($TimeoutSeconds * 1000)) { throw "widget 在 $TimeoutSeconds 秒内没有连上管道" }
    Write-Host '    [OK] 管道已连接' -ForegroundColor Green

    $reader = [System.IO.StreamReader]::new($server, [System.Text.Encoding]::UTF8)
    $writer = [System.IO.StreamWriter]::new($server, [System.Text.Encoding]::UTF8)
    $writer.AutoFlush = $true

    function Send-Message([hashtable]$message) {
        $json = $message | ConvertTo-Json -Compress -Depth 6
        Write-Host "  -> $json" -ForegroundColor DarkGray
        $writer.WriteLine($json)
    }

    $helloSeen = $false
    $initSent = $false
    $readySeen = $false
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)

    while ((Get-Date) -lt $deadline) {
        $readTask = $reader.ReadLineAsync()
        if (-not $readTask.Wait(3000)) { continue }
        $line = $readTask.Result
        if ($null -eq $line) { Write-Host '  (管道被对端关闭)'; break }
        Write-Host "  <- $line"

        try { $msg = $line | ConvertFrom-Json } catch { continue }

        switch ($msg.type) {
            'hello' {
                $helloSeen = $true
                Send-Message @{
                    type = 'hostInfo'; protocol = 2; theme = 'Dark'
                    saveLabel = '保存'; cancelLabel = '取消'
                    flyoutAnimation = 'ControlCenter'; flyoutBackdrop = 'Default'
                }
            }
            'previewWidth' { }
            'instanceReady' {
                $readySeen = $true
                Send-Message @{ type = 'showFlyout'; instanceId = $msg.instanceId }
                Start-Sleep -Seconds 2
                Send-Message @{ type = 'openSettings'; instanceId = $msg.instanceId }
                Start-Sleep -Seconds 1
            }
            'log' { if ($msg.level -in @('error', 'warning', 'warn')) { Write-Host "  !! plugin $($msg.level): $($msg.message)" -ForegroundColor Yellow } }
        }

        if ($helloSeen -and -not $initSent) {
            $initSent = $true
            $dataDir = Join-Path $env:LOCALAPPDATA "WidBarGachaCalendar\Smoke\$pill"
            New-Item -ItemType Directory -Force -Path $dataDir | Out-Null
            Send-Message @{
                type = 'initInstance'; instanceId = 'smoke' + $pill.Substring(0, 8)
                settingsJson = '{}'; dataDirectory = $dataDir
                parentHwnd = 0; x = 0; y = 0; w = 252; h = 48; visible = $true
            }
        }

        if ($readySeen) { break }
    }

    Write-Host ''
    if ($helloSeen -and $readySeen) {
        Write-Host '==> PASS：hello + instanceReady 都收到（widget 能连宿主、能初始化）' -ForegroundColor Green
        exit 0
    }
    if ($helloSeen) {
        Write-Host '==> PARTIAL：收到 hello，但没等到 instanceReady（看上面消息）' -ForegroundColor Yellow
        exit 2
    }
    Write-Host '==> FAIL：没收到 hello' -ForegroundColor Red
    exit 1
}
finally {
    if ($proc -and -not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
    $server.Dispose()
}
