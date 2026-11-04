#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$taskProject = Split-Path -Parent $PSScriptRoot
[xml]$taskProjectXml = Get-Content -LiteralPath (Join-Path $taskProject 'FM24FaceAssistant.csproj') -Raw
$taskVersion = [string]$taskProjectXml.Project.PropertyGroup.Version
if ($taskVersion -notmatch '^\d+\.\d+\.\d+$') { throw '项目版本号必须为 x.y.z。' }
$taskArtifacts = [IO.Path]::GetFullPath((Join-Path $taskProject 'artifacts'))
$taskOutput = Join-Path $taskArtifacts ('v' + $taskVersion)
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
$taskStage = Join-Path $taskArtifacts ('.package-' + [guid]::NewGuid().ToString('N'))
$taskPackage = Join-Path $taskStage 'FM24FaceAssistant'
$taskArchive = Join-Path $taskOutput ('FM24FaceAssistant-v' + $taskVersion + '-win-x64.zip')
try {
    & dotnet publish (Join-Path $taskProject 'FM24FaceAssistant.csproj') -c Release -r win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false -o $taskPackage
    if ($LASTEXITCODE -ne 0) { throw '程序构建失败，未生成发布包。' }
    Copy-Item -LiteralPath (Join-Path $taskPackage 'FM24FaceAssistant.exe') -Destination (Join-Path $taskPackage 'FM24头像助手.exe')
    if ((Get-FileHash -LiteralPath (Join-Path $taskPackage 'FM24FaceAssistant.exe')).Hash -ne (Get-FileHash -LiteralPath (Join-Path $taskPackage 'FM24头像助手.exe')).Hash) { throw '中英文启动文件内容不一致。' }
    foreach ($taskRequired in @('FM24头像助手.exe', 'FM24FaceAssistant.dll', 'coreclr.dll', 'README.md', 'LICENSE', 'THIRD-PARTY-NOTICES.md',  'Bundled/FLAYUS skin 24 YUXIANde.zip', 'Bundled/FLAYUS skin 24 YUXIANde.json', 'Data/Names/LICENSE.txt', 'Data/Names/GNU_DOC.TXT', 'Data/Names/NOTICE')) {
        if (!(Test-Path -LiteralPath (Join-Path $taskPackage $taskRequired) -PathType Leaf)) { throw ('发布包缺少文件：' + $taskRequired) }
    }
    $taskForbidden = @(Get-ChildItem -LiteralPath $taskPackage -Recurse -Force | Where-Object {
        $_.Name -in @('data-location.json', 'last-start.json', 'assets', 'user-data', '.git', '__pycache__', 'download_ngregens.py', 'start_library_worker.ps1', 'python.exe', 'pythonw.exe', 'curl.exe') -or $_.Extension -in @('.pdb', '.log', '.db')
    })
    if ($taskForbidden.Count) { throw '发布输出包含本机数据或调试产物，停止打包。' }
    [IO.Compression.ZipFile]::CreateFromDirectory($taskStage, $taskArchive + '.tmp', [IO.Compression.CompressionLevel]::Optimal, $false)
    Move-Item -LiteralPath ($taskArchive + '.tmp') -Destination $taskArchive -Force
    $taskHash = (Get-FileHash -LiteralPath $taskArchive -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText($taskArchive + '.sha256', $taskHash + '  ' + [IO.Path]::GetFileName($taskArchive) + "`n", [Text.UTF8Encoding]::new($false))
    [pscustomobject]@{ Version = $taskVersion; Archive = $taskArchive; SHA256 = $taskHash; MiB = [math]::Round((Get-Item -LiteralPath $taskArchive).Length / 1MB, 2) } | Format-List
} finally {
    $taskResolved = [IO.Path]::GetFullPath($taskStage)
    if (!$taskResolved.StartsWith($taskArtifacts + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw '临时目录越界，未清理。' }
    if (Test-Path -LiteralPath $taskResolved) { Remove-Item -LiteralPath $taskResolved -Recurse -Force }
    if (Test-Path -LiteralPath ($taskArchive + '.tmp')) { Remove-Item -LiteralPath ($taskArchive + '.tmp') -Force }
}
