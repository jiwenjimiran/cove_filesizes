param([string]$CoveSourceRoot = '', [switch]$NoBuild)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src/Filesizes/Filesizes.csproj'
$manifest = Get-Content (Join-Path $root 'src/Filesizes/extension.json') -Raw | ConvertFrom-Json
$artifacts = Join-Path $root 'artifacts'
$stage = Join-Path $artifacts ([Guid]::NewGuid().ToString('N'))
$arguments = @('publish', $project, '-c', 'Release', '-o', $stage, '-p:CoveCiVersion=true')
if ($CoveSourceRoot) { $arguments += "-p:CoveSourceRoot=$CoveSourceRoot" }
if ($NoBuild) { $arguments += '--no-build' }
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw 'Extension publish failed.' }
# Cove supplies host assemblies. Ship only our DLL and assets, never replace its dependencies.
$allowed = @('Cove.Filesizes.dll', 'Cove.Filesizes.deps.json', 'Cove.Filesizes.runtimeconfig.json', 'extension.json', 'assets')
foreach ($name in $allowed) {
    if (!(Test-Path -LiteralPath (Join-Path $stage $name))) { throw "Missing package file: $name" }
}
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = Join-Path $artifacts "io.github.jiwenjimiran.filesizes-$($manifest.version).zip"
$stream = [IO.MemoryStream]::new()
$archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $true)
try {
    foreach ($name in $allowed) {
        foreach ($file in Get-ChildItem -LiteralPath (Join-Path $stage $name) -File -Recurse) {
            $entry = $file.FullName.Substring($stage.Length + 1).Replace('\', '/')
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $entry, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    }
} finally { $archive.Dispose() }
try { [IO.File]::WriteAllBytes($zip, $stream.ToArray()) } finally { $stream.Dispose() }
Get-FileHash -LiteralPath $zip -Algorithm SHA256
Write-Output "Package: $zip"
