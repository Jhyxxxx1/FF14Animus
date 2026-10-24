param([string]$OmniDirectory, [string]$DalamudDirectory)
$ErrorActionPreference = 'Stop'
function Find-InstalledDirectory([string]$ParentPath, [string]$RequiredFile) {
    if (!(Test-Path -LiteralPath $ParentPath)) { throw "未找到目录：$ParentPath。请显式传入安装路径。" }
    $candidate = Get-ChildItem -LiteralPath $ParentPath -Directory |
        Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName $RequiredFile) } |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (!$candidate) { throw "未找到 $RequiredFile。请安装依赖或显式传入路径。" }
    return $candidate.FullName
}
if (!$OmniDirectory) { $OmniDirectory = Find-InstalledDirectory (Join-Path $env:APPDATA 'XIVLauncherCN/installedPlugins/OmniToolbox') 'OmniToolbox.dll' }
if (!$DalamudDirectory) { $DalamudDirectory = Find-InstalledDirectory (Join-Path $env:APPDATA 'XIVLauncherCN/addon/Hooks') 'Dalamud.dll' }
foreach ($item in @(@($OmniDirectory,'OmniToolbox.Common.dll'), @($DalamudDirectory,'Dalamud.dll'))) {
    if (!(Test-Path -LiteralPath (Join-Path $item[0] $item[1]))) { throw "缺少依赖 $($item[1])：$($item[0])" }
}
if (!('Microsoft.CodeAnalysis.CSharp.CSharpCompilation' -as [type])) {
    Add-Type -LiteralPath (Join-Path $OmniDirectory 'Microsoft.CodeAnalysis.dll')
    Add-Type -LiteralPath (Join-Path $OmniDirectory 'Microsoft.CodeAnalysis.CSharp.dll')
}
$paths = @{}
$runtimeDirectory = [System.Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()
foreach ($directory in @($runtimeDirectory, $DalamudDirectory, $OmniDirectory)) {
    foreach ($file in Get-ChildItem -LiteralPath $directory -Filter '*.dll' -File) {
        try {
            $identity = [System.Reflection.AssemblyName]::GetAssemblyName($file.FullName)
            if (!$paths.ContainsKey($identity.Name)) { $paths[$identity.Name] = $file.FullName }
        } catch [System.BadImageFormatException] { }
    }
}
$references = [System.Collections.Generic.List[Microsoft.CodeAnalysis.MetadataReference]]::new()
foreach ($path in $paths.Values) {
    $references.Add([Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($path))
}
$sourcePath = Join-Path $PSScriptRoot 'AnimusBookAssistant.cs'
$parseOptions = [Microsoft.CodeAnalysis.CSharp.CSharpParseOptions]::Default.WithLanguageVersion([Microsoft.CodeAnalysis.CSharp.LanguageVersion]::Preview)
$tree = [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText($sourcePath), $parseOptions, $sourcePath)
$options = [Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new([Microsoft.CodeAnalysis.OutputKind]::DynamicallyLinkedLibrary).WithAllowUnsafe($true)
# A unique identity avoids the host reusing an older assembly with the same name
# when reloading a DLL in a long-running Dalamud process. The module type stays stable.
$assemblyIdentity = 'AnimusBookAssistant_' + [DateTime]::UtcNow.Ticks
$compilation = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create($assemblyIdentity, [Microsoft.CodeAnalysis.SyntaxTree[]]@($tree), $references, $options)
$outputDirectory = Join-Path $PSScriptRoot 'dist'
[IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
$temporaryOutput = Join-Path $outputDirectory 'AnimusBookAssistant.build.tmp'
$stream = [IO.File]::Create($temporaryOutput)
try { $result = $compilation.Emit($stream) } finally { $stream.Dispose() }
$result.Diagnostics | ForEach-Object { $_.ToString() }
if (!$result.Success) { throw 'Module compilation failed.' }
[IO.File]::Move($temporaryOutput, (Join-Path $outputDirectory 'AnimusBookAssistant.dll'), $true)
Copy-Item -LiteralPath $sourcePath -Destination (Join-Path $outputDirectory 'AnimusBookAssistant.cs')
Get-FileHash -LiteralPath (Join-Path $outputDirectory 'AnimusBookAssistant.dll') -Algorithm SHA256
