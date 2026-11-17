# Builds the multi-architecture MSIX bundle for Microsoft Store submission.
# Usage: powershell -ExecutionPolicy Bypass -File Build-StoreBundle.ps1
# Output: StorePackages\wont-sleep_<version>_<arch>_<arch>.msixbundle (unsigned)

[CmdletBinding()]
param(
    [ValidateSet('x64', 'ARM64')]
    [string[]]$Platforms = @('x64', 'ARM64'),

    [string]$Configuration = 'Release',

    # Keep StorePackages\_staging (per-architecture .msix) for inspection.
    [switch]$KeepStaging
)

$ErrorActionPreference = 'Stop'

$ProjectDir = $PSScriptRoot
if (-not $ProjectDir) { $ProjectDir = Split-Path -Parent $MyInvocation.MyCommand.Path }
$Project = Join-Path $ProjectDir 'wont-sleep.csproj'
$StorePackages = Join-Path $ProjectDir 'StorePackages'
$StagingDir = Join-Path $StorePackages '_staging'

$Platforms = @($Platforms | ForEach-Object { $_.ToLowerInvariant() } | Select-Object -Unique)

# The manifest is the single source of truth for the version; keep it in sync with the bundle.
$manifestPath = Join-Path $ProjectDir 'Package.appxmanifest'
$manifest = [xml](Get-Content $manifestPath -Raw)
$ns = New-Object System.Xml.XmlNamespaceManager($manifest.NameTable)
$ns.AddNamespace('p', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10')
$Version = $manifest.SelectSingleNode('/p:Package/p:Identity', $ns).Version
Write-Host "Version: $Version"
Write-Host "Platforms: $($Platforms -join ', ')"

$makeappx = Get-ChildItem "$env:USERPROFILE\.nuget\packages\microsoft.windows.sdk.buildtools" -Recurse -Filter MakeAppx.exe -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match '\\x64\\' } |
    Sort-Object { if ($_.FullName -match '\\(\d+\.\d+\.\d+\.\d+)\\') { [version]$matches[1] } else { [version]'0.0.0.0' } } -Descending |
    Select-Object -First 1 -ExpandProperty FullName

if (-not $makeappx) {
    throw 'MakeAppx.exe not found in the NuGet package cache. Run "dotnet restore" first.'
}

if (Test-Path -LiteralPath $StagingDir) {
    Remove-Item -LiteralPath $StagingDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $StagingDir | Out-Null
New-Item -ItemType Directory -Force -Path $StorePackages | Out-Null

foreach ($platform in $Platforms) {
    $runtimeIdentifier = "win-$platform"
    $packageDir = Join-Path $StagingDir $platform

    Write-Host "Publishing $platform ($runtimeIdentifier)..."

    # A separate AppxPackageDir per architecture keeps the packages from overwriting each other.
    & dotnet publish $Project `
        -c $Configuration `
        "-p:Platform=$platform" `
        "-p:RuntimeIdentifier=$runtimeIdentifier" `
        -p:GenerateAppxPackageOnBuild=true `
        -p:AppxPackageSigningEnabled=false `
        "-p:AppxPackageDir=$packageDir\"

    if ($LASTEXITCODE -ne 0) {
        throw "Publish failed for $platform."
    }
}

# Only the application packages go into the bundle; Dependencies\* are framework
# packages and MakeAppx rejects them ("... is a framework package").
$bundleInput = Join-Path $StagingDir 'bundle-input'
New-Item -ItemType Directory -Force -Path $bundleInput | Out-Null

foreach ($platform in $Platforms) {
    $expectedName = "wont-sleep_${Version}_$platform.msix"
    $package = Get-ChildItem (Join-Path $StagingDir $platform) -Recurse -File -Filter '*.msix' |
        Where-Object { $_.Name -eq $expectedName } |
        Select-Object -First 1

    if (-not $package) {
        throw "Expected package '$expectedName' was not produced for $platform."
    }

    Write-Host "  $platform -> $($package.FullName)"
    Copy-Item -LiteralPath $package.FullName -Destination $bundleInput -Force
}

$archSuffix = ($Platforms | ForEach-Object { $_ }) -join '_'
$outputBundle = Join-Path $StorePackages "wont-sleep_${Version}_${archSuffix}.msixbundle"

if (Test-Path -LiteralPath $outputBundle) {
    Remove-Item -LiteralPath $outputBundle -Force
}

& $makeappx bundle /o "/bv" $Version "/d" $bundleInput "/p" $outputBundle | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw 'Bundle creation failed.'
}

if (-not $KeepStaging) {
    Remove-Item -LiteralPath $StagingDir -Recurse -Force
}

$sizeMb = [math]::Round((Get-Item -LiteralPath $outputBundle).Length / 1MB, 2)
Write-Host "Created: $outputBundle ($sizeMb MB, unsigned)"
