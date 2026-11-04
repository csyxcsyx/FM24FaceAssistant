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
& $pythonConsole -c 'import PIL' 2>$null
if ($LASTEXITCODE -ne 0) { throw '图库下载需要 Pillow。请运行 python -m pip install Pillow。' }

$pythonWindowless = Join-Path (Split-Path -Parent $pythonConsole) 'pythonw.exe'
if (-not (Test-Path -LiteralPath $pythonWindowless)) { $pythonWindowless = $pythonConsole }
$stateDirectory = Join-Path $env:LOCALAPPDATA 'FM24FaceAssistant'
$locationFile = Join-Path $PSScriptRoot 'data-location.json'
if (Test-Path -LiteralPath $locationFile) {
    $location = Get-Content -LiteralPath $locationFile -Raw | ConvertFrom-Json
    if ($location.Root) { $stateDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot $location.Root)) }
}
New-Item -ItemType Directory -Path $stateDirectory -Force | Out-Null
$assetsDirectory = Join-Path $stateDirectory 'library-download'
New-Item -ItemType Directory -Path $assetsDirectory -Force | Out-Null

# WMI owns the worker, keeping it independent of a terminal or monitor lifetime.
$quote = { param([string]$value) '"' + $value.Replace('"', '\"') + '"' }
$workerCommand = (& $quote $pythonWindowless) + ' ' + (& $quote $workerScript) + ' --state-dir ' + (& $quote $stateDirectory) + ' --assets-dir ' + (& $quote $assetsDirectory)
$result = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{CommandLine=$workerCommand;CurrentDirectory=$PSScriptRoot}
if ($result.ReturnValue -ne 0) { throw "启动图库任务失败，Windows 返回 $($result.ReturnValue)。" }
Write-Output "图库任务已启动，PID $($result.ProcessId)。"
