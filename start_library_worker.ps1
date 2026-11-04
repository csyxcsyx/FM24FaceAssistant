$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$workerScript = Join-Path $PSScriptRoot 'download_ngregens.py'
if (-not (Test-Path -LiteralPath $workerScript)) { throw '未找到图库下载组件。' }

$pythonCommand = Get-Command py.exe -ErrorAction SilentlyContinue
$pythonConsole = $null
if ($pythonCommand) {
    $pythonConsole = (& $pythonCommand.Source -3 -c 'import sys; print(sys.executable)' 2>$null | Select-Object -First 1)
    if ($pythonConsole) { $pythonConsole = $pythonConsole.Trim() }
}
if (-not $pythonConsole) {
    $pythonCommand = Get-Command python.exe -ErrorAction SilentlyContinue
    if ($pythonCommand) { $pythonConsole = $pythonCommand.Source }
}
if (-not $pythonConsole -or -not (Test-Path -LiteralPath $pythonConsole)) {
    throw '未找到 Python 3。请安装 Python，并确保 py 或 python 命令可用。'
}
& $pythonConsole -c 'import sys, PIL; raise SystemExit(0 if sys.version_info >= (3, 10) else 2)' 2>$null
if ($LASTEXITCODE -eq 2) { throw '图库下载需要 Python 3.10 或更新版本。' }
if ($LASTEXITCODE -ne 0) { throw '图库下载需要 Pillow。请在 Python 3.10+ 环境运行 python -m pip install Pillow。' }

$pythonWindowless = Join-Path (Split-Path -Parent $pythonConsole) 'pythonw.exe'
if (-not (Test-Path -LiteralPath $pythonWindowless)) { $pythonWindowless = $pythonConsole }
$localDataRoot = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
if ([string]::IsNullOrWhiteSpace($localDataRoot)) { throw '无法确定当前 Windows 用户的数据目录。' }
$stateDirectory = Join-Path $localDataRoot 'FM24FaceAssistant'
$locationFile = Join-Path $PSScriptRoot 'data-location.json'
if (Test-Path -LiteralPath $locationFile) {
    $location = Get-Content -LiteralPath $locationFile -Raw | ConvertFrom-Json
    if ($location.Root) { $stateDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot $location.Root)) }
}
New-Item -ItemType Directory -Path $stateDirectory -Force | Out-Null
$legacyAssets = Join-Path $PSScriptRoot 'assets'
$legacyFiles = @('download-status.json', 'NG_Regens_MainPack.zip', 'NG_Regens_MainPack.zip.part', 'library-report.json')
$hasLegacyAssets = @($legacyFiles | Where-Object { Test-Path -LiteralPath (Join-Path $legacyAssets $_) }).Count -gt 0 -or (Test-Path -LiteralPath (Join-Path $legacyAssets 'NG_Regens_Library'))
$settingsPath = Join-Path $stateDirectory 'settings.json'
$savedDownloadPath = if (Test-Path -LiteralPath $settingsPath) { (Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json).LibraryDownloadPath } else { $null }
$assetsDirectory = if ($savedDownloadPath) { [IO.Path]::GetFullPath($savedDownloadPath) } elseif ($hasLegacyAssets) { $legacyAssets } else { Join-Path $stateDirectory 'library-download' }
New-Item -ItemType Directory -Path $assetsDirectory -Force | Out-Null

# WMI owns the worker, keeping it independent of a terminal or monitor lifetime.
$quote = { param([string]$value) '"' + $value.Replace('"', '\"') + '"' }
$workerCommand = (& $quote $pythonWindowless) + ' ' + (& $quote $workerScript) + ' --state-dir ' + (& $quote $stateDirectory) + ' --assets-dir ' + (& $quote $assetsDirectory)
$result = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{CommandLine=$workerCommand;CurrentDirectory=$PSScriptRoot}
if ($result.ReturnValue -ne 0) { throw "启动图库任务失败，Windows 返回 $($result.ReturnValue)。" }
Write-Output "图库任务已启动，PID $($result.ProcessId)。"
