[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$RuntimeIdentifier = 'win-x64',
    [string]$Version = '',
    [switch]$SkipTests,
    [switch]$SkipInstallerExe,
    [switch]$NoZip
)

$ErrorActionPreference = 'Stop'

function Resolve-RepoRoot {
    $scriptDir = Split-Path -Parent $PSCommandPath
    return (Resolve-Path (Join-Path $scriptDir '..')).Path
}

function Remove-DirectorySafely {
    param(
        [Parameter(Mandatory)]
        [string]$Root,
        [Parameter(Mandatory)]
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    $rootFull = [System.IO.Path]::GetFullPath($Root).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    $pathFull = [System.IO.Path]::GetFullPath($Path)
    if (-not $pathFull.StartsWith($rootFull, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove path outside ${rootFull}: $pathFull"
    }

    Remove-Item -LiteralPath $pathFull -Recurse -Force
}

function Remove-FileIfExists {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Force
    }
}

function New-ZipFromDirectory {
    param(
        [Parameter(Mandatory)]
        [string]$SourceDir,
        [Parameter(Mandatory)]
        [string]$OutputPath
    )

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    Remove-FileIfExists -Path $OutputPath
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $SourceDir,
        $OutputPath,
        [System.IO.Compression.CompressionLevel]::Optimal,
        $false)
}

function Copy-ReleaseItem {
    param(
        [Parameter(Mandatory)]
        [string]$Source,
        [Parameter(Mandatory)]
        [string]$Destination
    )

    if (-not (Test-Path -LiteralPath $Source)) {
        throw "Release source item was not found: $Source"
    }

    Copy-Item -LiteralPath $Source -Destination $Destination -Recurse -Force
}

function Invoke-CodeSigningIfConfigured {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    $pfxBase64 = $env:CONTEXTCONTROL_SIGNING_PFX_BASE64
    $pfxPassword = $env:CONTEXTCONTROL_SIGNING_PFX_PASSWORD
    if ([string]::IsNullOrWhiteSpace($pfxBase64) -or [string]::IsNullOrWhiteSpace($pfxPassword)) {
        Write-Host 'Code signing skipped: CONTEXTCONTROL_SIGNING_PFX_BASE64 and CONTEXTCONTROL_SIGNING_PFX_PASSWORD are not configured.'
        return
    }

    $timestampServer = if ([string]::IsNullOrWhiteSpace($env:CONTEXTCONTROL_TIMESTAMP_SERVER)) {
        'http://timestamp.digicert.com'
    }
    else {
        $env:CONTEXTCONTROL_TIMESTAMP_SERVER
    }

    $pfxPath = Join-Path ([System.IO.Path]::GetTempPath()) "ContextControlSigning-$([guid]::NewGuid().ToString('N')).pfx"
    try {
        [System.IO.File]::WriteAllBytes($pfxPath, [System.Convert]::FromBase64String($pfxBase64))
        $certificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new(
            $pfxPath,
            $pfxPassword,
            [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet)
        $signature = Set-AuthenticodeSignature -FilePath $Path -Certificate $certificate -TimestampServer $timestampServer
        if ($signature.Status -ne 'Valid') {
            throw "Code signing failed with status $($signature.Status): $($signature.StatusMessage)"
        }

        Write-Host "Signed $Path"
    }
    finally {
        Remove-FileIfExists -Path $pfxPath
    }
}

$repoRoot = Resolve-RepoRoot
$project = Join-Path $repoRoot 'ide\ContextControl.Workbench\ContextControl.Workbench.csproj'
$setupProject = Join-Path $repoRoot 'ide\ContextControl.Setup\ContextControl.Setup.csproj'
$testProject = Join-Path $repoRoot 'ide\ContextControl.Workbench.Tests\ContextControl.Workbench.Tests.csproj'
$releaseRoot = Join-Path $repoRoot '.tmp\release'
$publishDir = Join-Path $releaseRoot "publish\$RuntimeIdentifier"
$setupPublishDir = Join-Path $releaseRoot "setup-publish\$RuntimeIdentifier"
$packageName = "ContextControl-$RuntimeIdentifier"
$stageDir = Join-Path $releaseRoot $packageName
$zipPath = Join-Path $releaseRoot "$packageName.zip"
$shaPath = "$zipPath.sha256.txt"
$payloadZipPath = Join-Path $releaseRoot "$packageName-payload.zip"
$installerPath = Join-Path $releaseRoot "$packageName-Setup.exe"
$installerShaPath = "$installerPath.sha256.txt"

if ([string]::IsNullOrWhiteSpace($Version)) {
    $versionFile = Join-Path $repoRoot 'packaging\VERSION'
    $fileVersion = if (Test-Path -LiteralPath $versionFile) {
        (Get-Content -LiteralPath $versionFile -TotalCount 1).Trim()
    }
    else {
        ''
    }

    $Version = if ([string]::IsNullOrWhiteSpace($fileVersion)) { '0.0.0-dev' } else { $fileVersion }
}

$displayVersion = $Version.Trim()
$packageVersion = $displayVersion -replace '^v(?=\d+\.\d+\.\d+)', ''
$numericVersionMatch = [regex]::Match($packageVersion, '(\d+)\.(\d+)\.(\d+)')
$assemblyVersion = if ($numericVersionMatch.Success) {
    "$($numericVersionMatch.Groups[1].Value).$($numericVersionMatch.Groups[2].Value).$($numericVersionMatch.Groups[3].Value).0"
}
else {
    '0.0.0.0'
}

New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
Remove-DirectorySafely -Root $releaseRoot -Path $publishDir
Remove-DirectorySafely -Root $releaseRoot -Path $setupPublishDir
Remove-DirectorySafely -Root $releaseRoot -Path $stageDir
Remove-FileIfExists -Path $zipPath
Remove-FileIfExists -Path $shaPath
Remove-FileIfExists -Path $payloadZipPath
Remove-FileIfExists -Path $installerPath
Remove-FileIfExists -Path $installerShaPath

if (-not $SkipTests) {
    dotnet run --project $testProject --configuration $Configuration
}

dotnet publish $project `
    --configuration $Configuration `
    --runtime $RuntimeIdentifier `
    --self-contained true `
    --output $publishDir `
    -p:PublishSingleFile=false `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -p:Version=$packageVersion `
    -p:AssemblyVersion=$assemblyVersion `
    -p:FileVersion=$assemblyVersion `
    -p:InformationalVersion=$displayVersion

New-Item -ItemType Directory -Path $stageDir -Force | Out-Null
Get-ChildItem -LiteralPath $publishDir -Force | Where-Object {
    $_.Extension -notin @('.pdb', '.xml')
} | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $stageDir -Recurse -Force
}

$packageDir = Join-Path $repoRoot 'packaging\package'
Copy-Item -LiteralPath (Join-Path $packageDir 'INSTALL.md') -Destination $stageDir -Force
Copy-Item -LiteralPath (Join-Path $packageDir 'Install-ContextControl.ps1') -Destination $stageDir -Force
Copy-Item -LiteralPath (Join-Path $packageDir 'Start-ContextControl.cmd') -Destination $stageDir -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'packaging\release-settings.template.json') -Destination (Join-Path $stageDir '.ccWorkbench.settings.json') -Force

$contextControlRuntimeItems = @(
    'cc',
    'cc.cmd',
    'cc.ps1',
    'ccDir',
    'ccDir.cmd',
    'ccDir.ps1',
    'ccReplace',
    'ccReplace.cmd',
    'ccReplace.ps1',
    'ccStart',
    'ccStart.cmd',
    'ccStart.ps1',
    'lib',
    'skillbook',
    'Context-Control-Coding-Flow.txt',
    'LICENSE.txt'
)
foreach ($item in $contextControlRuntimeItems) {
    Copy-ReleaseItem -Source (Join-Path $repoRoot $item) -Destination $stageDir
}

$manifest = [ordered]@{
    Name = 'ContextControl'
    RuntimeIdentifier = $RuntimeIdentifier
    Configuration = $Configuration
    Version = $packageVersion
    DisplayVersion = $displayVersion
    BuiltUtc = (Get-Date).ToUniversalTime().ToString('o')
    EntryPoint = 'ContextControl.Workbench.exe'
    DotNetRuntime = 'self-contained app folder'
    Installer = 'ContextControl.Setup WinForms installer with folder picker and shortcuts'
    Notes = @(
        'No .NET runtime install required.',
        'No LLM weights are bundled.',
        'ContextControl CLI scripts, lib modules, and the v1 Skillbook flow library UI are included; built-in CC Flow contracts remain read-only.',
        'The installer registers a per-user Windows uninstall entry and Start Menu uninstaller.',
        'Use the app Dependencies and Local LLM pages to install runtimes and download models.',
        'The Workbench checks GitHub releases on startup and can download/start the newest setup EXE.',
        'Managed Python dependencies ignore Microsoft Store python aliases and can bootstrap Python 3.12 through winget.',
        'The setup EXE embeds this full app folder and extracts it to the folder selected by the user.'
    )
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $stageDir 'release-manifest.json') -Encoding utf8

$payloadForSetup = $zipPath
if (-not $NoZip) {
    New-ZipFromDirectory -SourceDir $stageDir -OutputPath $zipPath
    $hash = Get-FileHash -Algorithm SHA256 -LiteralPath $zipPath
    "$($hash.Hash)  $([System.IO.Path]::GetFileName($zipPath))" | Set-Content -LiteralPath $shaPath -Encoding ascii
}
elseif (-not $SkipInstallerExe) {
    New-ZipFromDirectory -SourceDir $stageDir -OutputPath $payloadZipPath
    $payloadForSetup = $payloadZipPath
}

if (-not $SkipInstallerExe) {
    if (-not (Test-Path -LiteralPath $payloadForSetup)) {
        throw "Installer payload zip was not created: $payloadForSetup"
    }

    dotnet publish $setupProject `
        --configuration Release `
        --runtime $RuntimeIdentifier `
        --self-contained true `
        --output $setupPublishDir `
        -p:PublishSingleFile=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=none `
        -p:DebugSymbols=false `
        -p:Version=$packageVersion `
        -p:AssemblyVersion=$assemblyVersion `
        -p:FileVersion=$assemblyVersion `
        -p:InformationalVersion=$displayVersion `
        "-p:SetupPayloadZip=$payloadForSetup"

    $setupExePath = Join-Path $setupPublishDir 'ContextControl.Setup.exe'
    if (-not (Test-Path -LiteralPath $setupExePath)) {
        Get-ChildItem -LiteralPath $setupPublishDir -Force | Format-Table Name,Length,LastWriteTime
        throw "Setup executable was not created: $setupExePath"
    }

    Copy-Item -LiteralPath $setupExePath -Destination $installerPath -Force
    Invoke-CodeSigningIfConfigured -Path $installerPath
    $installerHash = Get-FileHash -Algorithm SHA256 -LiteralPath $installerPath
    "$($installerHash.Hash)  $([System.IO.Path]::GetFileName($installerPath))" | Set-Content -LiteralPath $installerShaPath -Encoding ascii
}

$exePath = Join-Path $stageDir 'ContextControl.Workbench.exe'
$folderSizeMb = if (Test-Path -LiteralPath $stageDir) {
    $totalBytes = (Get-ChildItem -LiteralPath $stageDir -Recurse -File -Force | Measure-Object -Property Length -Sum).Sum
    [math]::Round($totalBytes / 1MB, 1)
}
else {
    0
}
$exeSizeMb = if (Test-Path -LiteralPath $exePath) {
    [math]::Round((Get-Item -LiteralPath $exePath).Length / 1MB, 1)
}
else {
    0
}

Write-Host "Release folder: $stageDir"
if (-not $NoZip) {
    Write-Host "Release zip: $zipPath"
    Write-Host "SHA256: $shaPath"
}
if (-not $SkipInstallerExe) {
    Write-Host "Installer EXE: $installerPath"
    Write-Host "Installer SHA256: $installerShaPath"
}
Write-Host "App folder size: $folderSizeMb MB"
Write-Host "Launcher executable size: $exeSizeMb MB"
