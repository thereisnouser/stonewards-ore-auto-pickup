[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$GameDir,

    [string]$ProfileDir = '',

    [string]$OutputDir = ''
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$repoDir = $PSScriptRoot
$GameDir = (Resolve-Path -LiteralPath $GameDir).Path
$installRoot = $GameDir
if (-not [string]::IsNullOrWhiteSpace($ProfileDir)) {
    $installRoot = (Resolve-Path -LiteralPath $ProfileDir).Path
}
if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $repoDir 'dist'
}

$core = Join-Path $installRoot 'BepInEx\core'
$project = Join-Path $repoDir 'StonewardsOreAutoPickup.csproj'
$releaseDir = Join-Path $repoDir 'thunderstore'
$manifestPath = Join-Path $releaseDir 'manifest.json'
$readmePath = Join-Path $releaseDir 'README.md'
$changelogPath = Join-Path $releaseDir 'CHANGELOG.md'
$iconPath = Join-Path $releaseDir 'icon.png'
$dllPath = Join-Path $repoDir 'bin\Release\netstandard2.1\StonewardsOreAutoPickup.dll'
$pluginSourcePath = Join-Path $repoDir 'src\OreAutoPickupPlugin.cs'
$dropSourcePath = Join-Path $repoDir 'src\OwnDroppedOre.cs'

foreach ($required in @(
    $project, $manifestPath, $readmePath, $changelogPath,
    $pluginSourcePath, $dropSourcePath, $iconPath,
    (Join-Path $GameDir 'StoneWards_Data\Managed\Assembly-CSharp.dll'),
    (Join-Path $core 'BepInEx.dll'),
    (Join-Path $core '0Harmony.dll')
)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Required release file missing: $required"
    }
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'The .NET SDK is required to build a Thunderstore release.'
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$version = [string]$manifest.version_number
if ($manifest.name -ne 'OreAutoPickup' -or $version -notmatch '^\d+\.\d+\.\d+$') {
    throw 'Unexpected package name or invalid semantic version in manifest.json.'
}
if ($manifest.website_url -ne 'https://github.com/thereisnouser/stonewards-ore-auto-pickup') {
    throw 'Unexpected package repository URL in manifest.json.'
}

$pluginCode = Get-Content -LiteralPath $pluginSourcePath -Raw
$projectCode = Get-Content -LiteralPath $project -Raw
$pluginVersion = [regex]::Match($pluginCode, 'public const string PluginVersion\s*=\s*"([^"]+)"')
$projectVersion = [regex]::Match($projectCode, '<Version>([^<]+)</Version>')
if (-not $pluginVersion.Success -or -not $projectVersion.Success -or
    $pluginVersion.Groups[1].Value -ne $version -or
    $projectVersion.Groups[1].Value -ne $version) {
    throw 'Plugin, .csproj and Thunderstore manifest versions do not match.'
}
if ((Get-Content -LiteralPath $changelogPath -Raw) -notmatch ('(?m)^##\s+' + [regex]::Escape($version) + '\s*$')) {
    throw 'CHANGELOG.md has no entry for the package version.'
}

# Thunderstore requires an actual 256x256 PNG, not a JPG or a placeholder.
$png = [System.IO.File]::ReadAllBytes($iconPath)
$header = [byte[]](137, 80, 78, 71, 13, 10, 26, 10)
if ($png.Length -lt 24) {
    throw 'Thunderstore icon.png is not a valid PNG.'
}
for ($i = 0; $i -lt $header.Length; $i++) {
    if ($png[$i] -ne $header[$i]) {
        throw 'Thunderstore icon.png must be a real PNG file.'
    }
}
$iconWidth = [System.Net.IPAddress]::NetworkToHostOrder([BitConverter]::ToInt32($png, 16))
$iconHeight = [System.Net.IPAddress]::NetworkToHostOrder([BitConverter]::ToInt32($png, 20))
if ($iconWidth -ne 256 -or $iconHeight -ne 256) {
    throw "Thunderstore icon must be 256x256 pixels. Actual: $iconWidth x $iconHeight."
}

Write-Host "Building OreAutoPickup $version..." -ForegroundColor Cyan
& dotnet build $project --configuration Release "-p:GameDir=$GameDir" "-p:BepInExCoreDir=$core" --nologo
if ($LASTEXITCODE -ne 0) {
    throw 'Build failed. Thunderstore ZIP was NOT created.'
}
if (-not (Test-Path -LiteralPath $dllPath)) {
    throw "Compiled plugin DLL not found: $dllPath"
}

# Read assembly metadata without loading the game-dependent plugin.
$assemblyVersion = [System.Reflection.AssemblyName]::GetAssemblyName($dllPath).Version.ToString()
if ($assemblyVersion -notmatch ('^' + [regex]::Escape($version) + '\.\d+$')) {
    throw "DLL version is $assemblyVersion but the manifest requires $version."
}
foreach ($src in @($pluginSourcePath, $dropSourcePath, $project)) {
    if ((Get-Item -LiteralPath $dllPath).LastWriteTimeUtc -lt
        (Get-Item -LiteralPath $src).LastWriteTimeUtc) {
        throw "DLL is older than source file $src. Rebuild the project."
    }
}

$temp = Join-Path ([IO.Path]::GetTempPath()) ('ore-auto-pickup-release-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path (Join-Path $temp 'plugins') -Force | Out-Null

try {
    foreach ($name in @('manifest.json', 'README.md', 'CHANGELOG.md', 'icon.png')) {
        Copy-Item -LiteralPath (Join-Path $releaseDir $name) -Destination (Join-Path $temp $name)
    }
    Copy-Item -LiteralPath $dllPath -Destination (Join-Path $temp 'plugins\StonewardsOreAutoPickup.dll')

    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
    $output = Join-Path $OutputDir ("OreAutoPickup-" + $version + ".zip")
    if (Test-Path -LiteralPath $output) {
        Remove-Item -LiteralPath $output -Force
    }
    Compress-Archive -Path (Join-Path $temp '*') -DestinationPath $output -CompressionLevel Optimal

    $zip = [IO.Compression.ZipFile]::OpenRead($output)
    try {
        $entries = @($zip.Entries | ForEach-Object { $_.FullName.Replace([char]92, [char]47) })
        foreach ($required in @(
            'manifest.json', 'README.md', 'CHANGELOG.md', 'icon.png',
            'plugins/StonewardsOreAutoPickup.dll'
        )) {
            if ($entries -notcontains $required) {
                throw "Invalid release ZIP: missing $required (found: $($entries -join ', '))."
            }
        }

        # Guard against packaging the GitHub README instead of the store README.
        $storeReadme = [IO.File]::ReadAllText($readmePath)
        $zipReadme = $zip.GetEntry('README.md')
        $reader = New-Object IO.StreamReader($zipReadme.Open())
        try {
            if ($reader.ReadToEnd() -cne $storeReadme) {
                throw 'Packaged README differs from thunderstore/README.md.'
            }
        } finally {
            $reader.Dispose()
        }
    } finally {
        $zip.Dispose()
    }

    Write-Host "Thunderstore package ready: $output" -ForegroundColor Green
    Write-Host "DLL version: $assemblyVersion"
    Write-Host "ZIP SHA256: $((Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash)"
} finally {
    if (Test-Path -LiteralPath $temp) {
        Remove-Item -LiteralPath $temp -Recurse -Force
    }
}
