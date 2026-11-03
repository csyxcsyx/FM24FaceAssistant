param([string]$Source)
$ErrorActionPreference = 'Stop'
$taskProject = Split-Path -Parent $PSScriptRoot
$taskGame = Split-Path -Parent (Split-Path -Parent $taskProject)
$taskName = 'FLAYUS skin 24 YUXIANde'
if (!$Source) { $Source = Join-Path $taskGame ('skins\' + $taskName) }
$Source = [IO.Path]::GetFullPath($Source)
if (!(Test-Path -LiteralPath (Join-Path $Source 'skin_config.xml'))) { throw '找不到皮肤来源目录。' }
$taskBundled = Join-Path $taskProject 'Bundled'
New-Item -ItemType Directory -Path $taskBundled -Force | Out-Null
$taskArchive = Join-Path $taskBundled ($taskName + '.zip')
$taskTemporary = $taskArchive + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
$taskFiles = @(Get-ChildItem -LiteralPath $Source -File -Recurse | Sort-Object FullName)
$taskCount = 0
$taskBytes = 0L
try {
    $taskZip = [IO.Compression.ZipFile]::Open($taskTemporary, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($taskFile in $taskFiles) {
            if (($taskFile.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw '来源中包含链接，停止打包。' }
            $taskRelative = [IO.Path]::GetRelativePath($Source, $taskFile.FullName).Replace('\', '/')
            $taskData = [IO.File]::ReadAllBytes($taskFile.FullName)
            if ($taskFile.Extension -eq '.xml') {
                $taskText = [Text.Encoding]::UTF8.GetString($taskData)
                if ($taskText.Contains('F24A')) {
                    $taskXml = [System.Xml.XmlDocument]::new()
                    $taskStream = [IO.MemoryStream]::new($taskData)
                    try { $taskXml.Load($taskStream) } finally { $taskStream.Dispose() }
                    foreach ($taskNode in @($taskXml.SelectNodes('//*[@id="F24A"]'))) {
                        if ($taskNode.OuterXml -match 'fm24_face_assistant|fm24face://open|fmfaceassistant://open') { $taskNode.ParentNode.RemoveChild($taskNode) | Out-Null }
                    }
                    $taskData = [Text.UTF8Encoding]::new($false).GetBytes($taskXml.OuterXml)
                }
            }
            $taskEntry = $taskZip.CreateEntry($taskName + '/' + $taskRelative, [IO.Compression.CompressionLevel]::Optimal)
            $taskStream = $taskEntry.Open()
            try { $taskStream.Write($taskData, 0, $taskData.Length) } finally { $taskStream.Dispose() }
            $taskCount++
            $taskBytes += $taskData.Length
        }
    } finally { $taskZip.Dispose() }
    [IO.File]::Move($taskTemporary, $taskArchive, $true)
    $taskManifest = [ordered]@{ Sha256 = (Get-FileHash -LiteralPath $taskArchive -Algorithm SHA256).Hash; Files = $taskCount; Bytes = $taskBytes }
    $taskManifest | ConvertTo-Json | Set-Content -LiteralPath ([IO.Path]::ChangeExtension($taskArchive, '.json')) -Encoding utf8
    [pscustomobject]@{ Files = $taskCount; UnpackedMB = [math]::Round($taskBytes / 1MB, 2); ArchiveMB = [math]::Round((Get-Item -LiteralPath $taskArchive).Length / 1MB, 2); Path = $taskArchive } | Format-List
} finally { if (Test-Path -LiteralPath $taskTemporary) { Remove-Item -LiteralPath $taskTemporary } }
