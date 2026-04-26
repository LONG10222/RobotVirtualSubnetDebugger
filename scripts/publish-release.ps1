param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SkipSign,
    [string]$SignToolPath = $env:SIGNTOOL_PATH,
    [string]$CertificatePath = $env:SIGN_CERT_PATH,
    [string]$CertificatePassword = $env:SIGN_CERT_PASSWORD,
    [string]$TimestampUrl = "http://timestamp.digicert.com"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$projectPath = Join-Path $repoRoot "RobotVirtualSubnetDebugger\RobotVirtualSubnetDebugger.csproj"
$artifactsRoot = Join-Path $repoRoot "artifacts"
$releaseRoot = Join-Path $artifactsRoot "release"
$frameworkDir = Join-Path $releaseRoot "$Runtime-framework"
$selfContainedDir = Join-Path $releaseRoot "$Runtime-self-contained"

if (-not (Test-Path -LiteralPath $projectPath)) {
    throw "Project file not found: $projectPath"
}

$resolvedRepoRoot = (Resolve-Path -LiteralPath $repoRoot).Path
if (Test-Path -LiteralPath $releaseRoot) {
    $resolvedReleaseRoot = (Resolve-Path -LiteralPath $releaseRoot).Path
    if (-not $resolvedReleaseRoot.StartsWith($resolvedRepoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to delete release directory outside repository: $resolvedReleaseRoot"
    }

    Remove-Item -LiteralPath $resolvedReleaseRoot -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $frameworkDir, $selfContainedDir | Out-Null

[xml]$projectXml = Get-Content -LiteralPath $projectPath
$version = $projectXml.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version)) {
    $version = "0.0.0"
}

Write-Host "Building RobotNet.Windows.Wpf $version"
dotnet build $projectPath -c $Configuration

Write-Host "Publishing framework-dependent package"
dotnet publish $projectPath -c $Configuration -r $Runtime --self-contained false -o $frameworkDir

Write-Host "Publishing self-contained single-file package"
dotnet publish $projectPath -c $Configuration -r $Runtime --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $selfContainedDir

function Invoke-CodeSign {
    param([string]$FilePath)

    if ($SkipSign) {
        return
    }

    if ([string]::IsNullOrWhiteSpace($SignToolPath) -or
        [string]::IsNullOrWhiteSpace($CertificatePath) -or
        [string]::IsNullOrWhiteSpace($CertificatePassword)) {
        Write-Host "Code signing skipped. Configure SIGNTOOL_PATH, SIGN_CERT_PATH and SIGN_CERT_PASSWORD to enable signing."
        return
    }

    if (-not (Test-Path -LiteralPath $SignToolPath)) {
        throw "SignTool not found: $SignToolPath"
    }

    if (-not (Test-Path -LiteralPath $CertificatePath)) {
        throw "Signing certificate not found: $CertificatePath"
    }

    & $SignToolPath sign /f $CertificatePath /p $CertificatePassword /fd SHA256 /tr $TimestampUrl /td SHA256 $FilePath
}

Get-ChildItem -LiteralPath $frameworkDir -Filter "*.exe" | ForEach-Object { Invoke-CodeSign $_.FullName }
Get-ChildItem -LiteralPath $selfContainedDir -Filter "*.exe" | ForEach-Object { Invoke-CodeSign $_.FullName }

$frameworkZip = Join-Path $releaseRoot "RobotNet.Windows.Wpf-$version-$Runtime-framework.zip"
$selfContainedExe = Join-Path $selfContainedDir "RobotNet.Windows.Wpf.exe"
$selfContainedAsset = Join-Path $releaseRoot "RobotNet.Windows.Wpf-$version-$Runtime-self-contained.exe"
$checksumsPath = Join-Path $releaseRoot "checksums.sha256"

Compress-Archive -Path (Join-Path $frameworkDir "*") -DestinationPath $frameworkZip -Force
Copy-Item -LiteralPath $selfContainedExe -Destination $selfContainedAsset -Force

$assets = @($frameworkZip, $selfContainedAsset)
$checksumLines = foreach ($asset in $assets) {
    $hash = Get-FileHash -LiteralPath $asset -Algorithm SHA256
    "$($hash.Hash.ToLowerInvariant())  $(Split-Path -Leaf $asset)"
}

$checksumLines | Set-Content -LiteralPath $checksumsPath -Encoding UTF8

Write-Host "Release artifacts:"
Get-ChildItem -LiteralPath $releaseRoot -File | Select-Object Name, Length, LastWriteTime | Format-Table -AutoSize
