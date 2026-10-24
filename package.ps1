param([string]$Version = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'VERSION') -Raw).Trim())
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+(-[a-zA-Z0-9.]+)?$') { throw '版本格式无效。' }
$source = Join-Path $PSScriptRoot 'AnimusBookAssistant.cs'
$versionMatch = [regex]::Match([IO.File]::ReadAllText($source), 'ReleaseVersion = "([^"]+)"')
if (!$versionMatch.Success -or $versionMatch.Groups[1].Value -ne $Version) { throw '源码版本与VERSION不匹配。' }
$dll = Join-Path $PSScriptRoot 'dist/AnimusBookAssistant.dll'
if (!(Test-Path -LiteralPath $dll)) { throw '请先运行 build.ps1。' }
$artifacts = Join-Path $PSScriptRoot 'artifacts'
$stage = Join-Path $artifacts ('stage-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path (Join-Path $stage 'docs') -Force | Out-Null
Copy-Item -LiteralPath $dll -Destination (Join-Path $stage 'AnimusBookAssistant.dll')
foreach ($file in @('AnimusBookAssistant.cs','README.md','LICENSE','CHANGELOG.md','DATA-SOURCES.md','VERSION')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination (Join-Path $stage $file)
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs/BUILDING.md'),(Join-Path $PSScriptRoot 'docs/COMPATIBILITY.md') -Destination (Join-Path $stage 'docs')
$manifest = [ordered]@{
    version = $Version
    dllSha256 = (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash
    sourceSha256 = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
}
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $stage 'manifest.json')
$zip = Join-Path $artifacts "FF14Animus-$Version.zip"
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -Force
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
"$hash  $([IO.Path]::GetFileName($zip))" | Set-Content -LiteralPath "$zip.sha256"
Copy-Item -LiteralPath $dll -Destination (Join-Path $PSScriptRoot 'AnimusBookAssistant.dll')
$downloads = Join-Path $PSScriptRoot 'downloads'
New-Item -ItemType Directory -Path $downloads -Force | Out-Null
Copy-Item -LiteralPath $zip,"$zip.sha256" -Destination $downloads
Get-Item -LiteralPath $zip | Select-Object Name,Length
Get-Content -LiteralPath "$zip.sha256"
