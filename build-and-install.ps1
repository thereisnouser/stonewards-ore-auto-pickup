param(
    [Parameter(Mandatory = $true)]
    [string]$GameDir,
    [string]$ProfileDir
)

$ErrorActionPreference = 'Stop'
$GameDir = (Resolve-Path -LiteralPath $GameDir).Path
$managed = Join-Path $GameDir 'StoneWards_Data\Managed'
$installRoot = $GameDir
if (-not [string]::IsNullOrWhiteSpace($ProfileDir)) {
    $installRoot = (Resolve-Path -LiteralPath $ProfileDir).Path
}
$bepinexCore = Join-Path $installRoot 'BepInEx\core'

if (-not (Test-Path -LiteralPath (Join-Path $managed 'Assembly-CSharp.dll'))) {
    throw "Stonewards assemblies not found: $managed"
}
if (-not (Test-Path -LiteralPath (Join-Path $bepinexCore 'BepInEx.dll'))) {
    throw "BepInEx 5 missing in active install/profile: $bepinexCore"
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'The .NET SDK is required to build this mod.'
}

$project = Join-Path $PSScriptRoot 'StonewardsOreAutoPickup.csproj'
$env:STONEWARDS_GAME_DIR = $GameDir
Write-Host 'Building Stonewards Ore Auto Pickup (Release)...'
& dotnet build $project --configuration Release "-p:BepInExCoreDir=$bepinexCore"
if ($LASTEXITCODE -ne 0) {
    throw 'Build failed; plugin was NOT installed.'
}

$built = Join-Path $PSScriptRoot 'bin\Release\netstandard2.1\StonewardsOreAutoPickup.dll'
$target = Join-Path $installRoot 'BepInEx\plugins\StonewardsOreAutoPickup'
New-Item -ItemType Directory -Path $target -Force | Out-Null
$installed = Join-Path $target 'StonewardsOreAutoPickup.dll'
Copy-Item -LiteralPath $built -Destination $installed -Force

$sourceHash = (Get-FileHash $built -Algorithm SHA256).Hash
$targetHash = (Get-FileHash $installed -Algorithm SHA256).Hash
if ($sourceHash -ne $targetHash) {
    throw 'DLL integrity check failed.'
}
Write-Host "Installed: $installed" -ForegroundColor Green
Write-Host "SHA256: $targetHash"
Write-Host 'Launch modded through the SAME r2modman profile. Press F10 in gameplay to configure ores.'

$duplicates = @(Get-ChildItem -LiteralPath (Join-Path $installRoot 'BepInEx\plugins') `
    -Filter 'StonewardsOreAutoPickup.dll' -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -ne $installed })
if ($duplicates.Count -gt 0) {
    Write-Warning ('Duplicate copies: ' + (($duplicates | ForEach-Object { $_.FullName }) -join '; '))
}
