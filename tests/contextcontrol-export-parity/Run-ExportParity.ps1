[CmdletBinding()]
param(
    [string]$RepositoryRoot = "",
    [string]$OutputRoot = "",
    [string]$NativeExe = "",
    [ValidateSet("hash", "functional")]
    [string]$NativeCompare = "hash",
    [switch]$RequireNative,
    [switch]$KeepFixture
)

$ErrorActionPreference = "Stop"
$script:Utf8NoBom = New-Object System.Text.UTF8Encoding $false

function Resolve-RepositoryRoot {
    param([string]$RequestedRoot)

    if (-not [string]::IsNullOrWhiteSpace($RequestedRoot)) {
        return (Resolve-Path -LiteralPath $RequestedRoot).Path
    }

    $directory = Get-Item -LiteralPath $PSScriptRoot
    while ($null -ne $directory) {
        if ((Test-Path -LiteralPath (Join-Path $directory.FullName "ccDir.ps1")) -and
            (Test-Path -LiteralPath (Join-Path $directory.FullName "cc.ps1"))) {
            return $directory.FullName
        }

        $directory = $directory.Parent
    }

    throw "Could not locate repository root from $PSScriptRoot."
}

function Get-DefaultPowerShellExecutable {
    if ($env:OS -eq "Windows_NT") {
        return "powershell"
    }

    return "pwsh"
}

function New-CleanDirectory {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        [void](New-Item -ItemType Directory -Path $Path)
        return
    }

    Get-ChildItem -LiteralPath $Path -Force | Remove-Item -Recurse -Force
}

function Write-FixtureFile {
    param(
        [string]$Root,
        [string]$RelativePath,
        [string]$Text
    )

    $fullPath = Join-Path $Root ($RelativePath -replace '/', [System.IO.Path]::DirectorySeparatorChar)
    $parent = Split-Path -Parent $fullPath
    if (-not (Test-Path -LiteralPath $parent)) {
        [void](New-Item -ItemType Directory -Path $parent)
    }

    [System.IO.File]::WriteAllText($fullPath, $Text, $script:Utf8NoBom)
}

function New-FixtureProject {
    param([string]$Root)

    New-CleanDirectory $Root

    Write-FixtureFile $Root "ccDir.ps1" "function Invoke-FixtureDir { }`n"
    Write-FixtureFile $Root "cc.ps1" "function Invoke-FixtureCc { }`n"
    Write-FixtureFile $Root "CMakeLists.txt" @'
cmake_minimum_required(VERSION 3.20)
project(ContextControlParityFixture LANGUAGES CXX)
add_executable(parity src/app/Main.cpp src/app/Renderer.cpp)
'@
    Write-FixtureFile $Root "package.json" @'
{
  "scripts": {
    "dev": "vite"
  }
}
'@
    Write-FixtureFile $Root "src/app/Main.cpp" @'
// CC-DESC: Parity fixture C++ entrypoint.
#include "Renderer.h"

int helper()
{
    return 7;
}

int main()
{
    Renderer renderer;
    renderer.drawFrame();
    return helper();
}
'@
    Write-FixtureFile $Root "src/app/Renderer.h" @'
// CC-DESC: Parity fixture renderer header.
#pragma once

class Renderer
{
public:
    void drawFrame();
};
'@
    Write-FixtureFile $Root "src/app/Renderer.cpp" @'
// CC-DESC: Parity fixture renderer implementation.
#include "Renderer.h"

void Renderer::drawFrame()
{
    // Keep this token searchable: RenderFrameMarker.
}
'@
    Write-FixtureFile $Root "src/app/PromptViewModel.cs" @'
// CC-DESC: Prompt controls used by ContextControl parity tests.
namespace ContextControl.Parity;

public sealed partial class PromptViewModel
{
    public bool IsCodexPromptMode { get; set; }

    public string PromptSendButtonLabel => IsCodexPromptMode
        ? "Send to Codex"
        : "Send";

    public string ParsePhase1RequestLines(string text)
    {
        return text.Trim();
    }
}
'@
    Write-FixtureFile $Root "src/app/PromptViewModel.Actions.cs" @'
// CC-DESC: Prompt actions used by ContextControl parity tests.
namespace ContextControl.Parity;

public sealed partial class PromptViewModel
{
    public void SendPrompt()
    {
        var label = PromptSendButtonLabel;
    }
}
'@
    Write-FixtureFile $Root "src/app/contextcontrol/PromptViewModel.ContextControl.cs" @'
// CC-DESC: Explicit request fixture inside an ignored directory name.
namespace ContextControl.Parity;

public sealed partial class PromptViewModel
{
    public string NestedPromptSendButtonLabel => "Nested Send to Codex";

    private async Task NestedSendAsync()
    {
        await Task.CompletedTask;
    }
}
'@
    Write-FixtureFile $Root "src/app/WorkbenchAlpha.cs" @'
// CC-DESC: Split workbench family alpha.
namespace ContextControl.Parity;

public sealed class WorkbenchAlpha
{
    public void RunAlpha() { }
}
'@
    Write-FixtureFile $Root "src/app/WorkbenchBeta.cs" @'
// CC-DESC: Split workbench family beta.
namespace ContextControl.Parity;

public sealed class WorkbenchBeta
{
    public void RunBeta() { }
}
'@
    Write-FixtureFile $Root "src/app/ui/MainWindow.axaml" @'
<Window>
  <Button Content="Send to Codex" />
  <Button Classes="cc-prompt-send" Content="{Binding PromptSendButtonLabel}" />
</Window>
'@
    Write-FixtureFile $Root "src/app/ui/PromptComposer.axaml" @'
<Styles>
  <Style Selector="Button.cc-prompt-send">
    <Setter Property="Background" Value="#18222E" />
  </Style>
</Styles>
'@
    Write-FixtureFile $Root "shaders/common.glsl" @'
// CC-DESC: Shared shader fixture include.
vec4 sharedColor()
{
    return vec4(1.0);
}
'@
    Write-FixtureFile $Root "shaders/fullscreen.vert" @'
// CC-DESC: Fullscreen shader fixture.
#include "common.glsl"
void main()
{
}
'@
    Write-FixtureFile $Root "docs/readme.md" "# Fixture docs`n"
    Write-FixtureFile $Root "custom-visible/Feature.specialcc" "RuleSurfaceMarker from custom supported extension.`n"
    Write-FixtureFile $Root "custom-hidden/Hidden.cs" "RuleSurfaceMarker from ignored directory.`n"
    Write-FixtureFile $Root "src/app/HiddenByRules.cs" "RuleSurfaceMarker HiddenByRulesMarker from ignored file name.`n"
    Write-FixtureFile $Root "src/app/LegacyHidden.cs" "RuleSurfaceMarker LegacyHiddenMarker from ignored file name alias.`n"
    Write-FixtureFile $Root "src/app/ShownDespiteName.tmp.txt" "RuleSurfaceMarker shown file override.`n"
    Write-FixtureFile $Root "generated/Generated.cs" "public sealed class Generated { }`n"
    Write-FixtureFile $Root ".ccWorkbench.chat-history.123.json" "{ `"PromptText`": `"Send to Codex`" }`n"
    Write-FixtureFile $Root "patch.txt" "Send to Codex patch artifact`n"

    $large = New-Object System.Text.StringBuilder
    foreach ($index in 1..180) {
        [void]$large.AppendLine("public string LargeLine$index => `"This line keeps LargeFixtureExport searchable.`";")
    }
    Write-FixtureFile $Root "src/app/LargeFixture.cs" $large.ToString()
}

function New-ExportCase {
    param(
        [string]$Name,
        [ValidateSet("dir", "cc")]
        [string]$Kind,
        [string[]]$Arguments = @(),
        [string[]]$InputLines = @(),
        [string]$FileRulesJson = "",
        [string]$Extension = ".md",
        [bool]$ExpectSuccess = $true,
        [string[]]$RequiredText = @(),
        [string[]]$ForbiddenText = @()
    )

    [pscustomobject]@{
        Name = $Name
        Kind = $Kind
        Arguments = [string[]]$Arguments
        InputLines = [string[]]$InputLines
        FileRulesJson = $FileRulesJson
        Extension = $Extension
        ExpectSuccess = $ExpectSuccess
        RequiredText = [string[]]$RequiredText
        ForbiddenText = [string[]]$ForbiddenText
    }
}

function Get-ExportCases {
    @(
        New-ExportCase `
            -Name "DIR_ProfileOnly" `
            -Kind "dir" `
            -Arguments @("-ProfileOnly", "-ProfileOutput", "{OUT}") `
            -Extension ".json" `
            -RequiredText @('"SchemaVersion":', '"Roots":', '"Files":', '"Families":')
        New-ExportCase `
            -Name "DIR_ProfileVulkanTopLevelFilter" `
            -Kind "dir" `
            -Arguments @("-ProfileOnly", "-Profile", "vulkanvx", "-ProfileFile", "{OUT}", "-ProfileOutput", "{OUT}") `
            -Extension ".json" `
            -RequiredText @('"CMakeLists.txt"') `
            -ForbiddenText @('"Path":  "ccDir.ps1"', '"Path": "ccDir.ps1"', '"Path":  "package.json"', '"Path": "package.json"', '"Path":  "custom-visible/Feature.specialcc"', '"Path": "custom-visible/Feature.specialcc"')
        New-ExportCase `
            -Name "DIR_ProfileVulkanIncludeAllTopLevel" `
            -Kind "dir" `
            -Arguments @("-ProfileOnly", "-Profile", "vulkanvx", "-IncludeAllTopLevel", "-ProfileFile", "{OUT}", "-ProfileOutput", "{OUT}") `
            -Extension ".json" `
            -RequiredText @('"ccDir.ps1"', '"custom-visible/Feature.specialcc"')
        New-ExportCase `
            -Name "DIR_L0_ProfileBacked" `
            -Kind "dir" `
            -Arguments @("-Lod", "0", "-ProfileFile", "{PROFILE}") `
            -RequiredText @("CC-DIR-MANIFEST-V2", "LOD: L0_GLOBAL", "ROOT path=`"src/`"", "FILE path=`"ccDir.ps1`"", "FAMILY path=`"src/app/Workbench*.cs`"", "FIND lines may repeat before END; do not mix FIND with source or EXPAND lines.") `
            -ForbiddenText @("generated/Generated.cs", ".ccWorkbench.chat-history", "patch.txt", '```')
        New-ExportCase `
            -Name "DIR_MaxDepthOne" `
            -Kind "dir" `
            -Arguments @("-Lod", "0", "-MaxDepth", "1") `
            -RequiredText @("CC-DIR-MANIFEST-V2", "FILE path=`"ccDir.ps1`"") `
            -ForbiddenText @("src/app/Main.cpp", "shaders/fullscreen.vert")
        New-ExportCase `
            -Name "DIR_L1_SourceScope" `
            -Kind "dir" `
            -Arguments @("-Lod", "1", "-Scope", "src/app") `
            -RequiredText @("CC-DIR-MANIFEST-V2", "LOD: L1_SCOPED", "SCOPE: src/app/", "FILE path=`"src/app/PromptViewModel.cs`"", "FILE path=`"src/app/Renderer.cpp`"") `
            -ForbiddenText @("docs/readme.md", "generated/Generated.cs", '```')
        New-ExportCase `
            -Name "DIR_L1_ShaderScope" `
            -Kind "dir" `
            -Arguments @("-Lod", "1", "-Scope", "shaders") `
            -RequiredText @("LOD: L1_SCOPED", "SCOPE: shaders/", "FILE path=`"shaders/common.glsl`"", "FILE path=`"shaders/fullscreen.vert`"")
        New-ExportCase `
            -Name "DIR_FileRulesAliases" `
            -Kind "dir" `
            -Arguments @("-Lod", "1", "-Scope", "src/app") `
            -FileRulesJson @'
{
  "IgnoredFileNames": [ "LegacyHidden.cs" ],
  "IgnoredFiles": [ "HiddenByRules.cs" ]
}
'@ `
            -RequiredText @("LOD: L1_SCOPED", "FILE path=`"src/app/PromptViewModel.cs`"") `
            -ForbiddenText @("LegacyHidden.cs", "HiddenByRules.cs")
        New-ExportCase `
            -Name "DIR_IncludeArtifacts" `
            -Kind "dir" `
            -Arguments @("-Lod", "1", "-Scope", "generated", "-IncludeArtifacts") `
            -RequiredText @("LOD: L1_SCOPED", "SCOPE: generated/", "FILES:", "(none)") `
            -ForbiddenText @("Generated.cs")
        New-ExportCase `
            -Name "CC_FullFile" `
            -Kind "cc" `
            -InputLines @("src/app/PromptViewModel.cs") `
            -RequiredText @("# Code export", "PromptViewModel.cs", "PromptSendButtonLabel", "ParsePhase1RequestLines")
        New-ExportCase `
            -Name "CC_FolderTree" `
            -Kind "cc" `
            -InputLines @("src/app/ui") `
            -RequiredText @("## Folder tree:", "MainWindow.axaml", "PromptComposer.axaml")
        New-ExportCase `
            -Name "CC_CppAutoHeader" `
            -Kind "cc" `
            -InputLines @("src/app/Renderer.cpp") `
            -RequiredText @("Renderer.cpp", "Renderer.h", "Renderer::drawFrame")
        New-ExportCase `
            -Name "CC_ShaderAutoInclude" `
            -Kind "cc" `
            -InputLines @("shaders/fullscreen.vert") `
            -RequiredText @("fullscreen.vert", "common.glsl", "sharedColor")
        New-ExportCase `
            -Name "CC_ScopedFunctionMethod" `
            -Kind "cc" `
            -InputLines @("FUNCTION src/app/PromptViewModel.cs :: ParsePhase1RequestLines") `
            -RequiredText @("## FUNCTION src/app/PromptViewModel.cs :: ParsePhase1RequestLines", "Source: src/app/PromptViewModel.cs", "return text.Trim();")
        New-ExportCase `
            -Name "CC_ScopedFunctionProperty" `
            -Kind "cc" `
            -InputLines @("FUNCTION src/app/PromptViewModel.cs :: PromptSendButtonLabel") `
            -RequiredText @("## FUNCTION src/app/PromptViewModel.cs :: PromptSendButtonLabel", "Send to Codex")
        New-ExportCase `
            -Name "CC_WildcardScopedFunction" `
            -Kind "cc" `
            -InputLines @("FUNCTION src/app/PromptViewModel*.cs :: PromptSendButtonLabel") `
            -RequiredText @("## FUNCTION src/app/PromptViewModel*.cs :: PromptSendButtonLabel", "Source: src/app/PromptViewModel.cs")
        New-ExportCase `
            -Name "CC_ExactFunctionIgnoredDirectory" `
            -Kind "cc" `
            -FileRulesJson @'
{
  "IgnoredDirectories": [ "contextcontrol" ]
}
'@ `
            -InputLines @(
                "FUNCTION src/app/contextcontrol/PromptViewModel.ContextControl.cs :: NestedPromptSendButtonLabel",
                "FUNCTION src/app/contextcontrol/PromptViewModel.ContextControl.cs :: NestedSendAsync"
            ) `
            -RequiredText @(
                "Source: src/app/contextcontrol/PromptViewModel.ContextControl.cs",
                "Nested Send to Codex",
                "private async Task NestedSendAsync()"
            ) `
            -ForbiddenText @("SKIPPED EXCLUDED", "No matching function body found")
        New-ExportCase `
            -Name "CC_GlobalFuncColon" `
            -Kind "cc" `
            -InputLines @("FUNC: ParsePhase1RequestLines") `
            -RequiredText @("## FOUND FUNCTION: ParsePhase1RequestLines", "Source: src/app/PromptViewModel.cs")
        New-ExportCase `
            -Name "CC_GlobalFunctionColon" `
            -Kind "cc" `
            -InputLines @("FUNCTION: ParsePhase1RequestLines") `
            -RequiredText @("## FOUND FUNCTION: ParsePhase1RequestLines", "Source: src/app/PromptViewModel.cs")
        New-ExportCase `
            -Name "CC_FindSingle" `
            -Kind "cc" `
            -InputLines @("FIND: Send to Codex") `
            -RequiredText @("## FIND: Send to Codex", "Matched code files:", "- src/app/PromptViewModel.cs", "- src/app/ui/MainWindow.axaml") `
            -ForbiddenText @("file contents were exported")
        New-ExportCase `
            -Name "CC_FindMultiple" `
            -Kind "cc" `
            -InputLines @("FIND: Send to Codex", "FIND: RenderFrameMarker") `
            -RequiredText @("## FIND: Send to Codex", "## FIND: RenderFrameMarker", "- src/app/Renderer.cpp")
        New-ExportCase `
            -Name "CC_HashHintsFile" `
            -Kind "cc" `
            -Arguments @("-HashHints") `
            -InputLines @("src/app/PromptViewModel.cs") `
            -RequiredText @(
                "Hash hints for optional HASH: patch headers:",
                "- whole_file HASH: c7ab8aa3",
                "- function IsCodexPromptMode HASH: 44c7e783",
                "- function PromptSendButtonLabel HASH: 123f1b9f",
                "- function ParsePhase1RequestLines HASH: e4b60dd7"
            ) `
            -ForbiddenText @("native-file-size-", "native-function-")
        New-ExportCase `
            -Name "CC_HashHintsFunction" `
            -Kind "cc" `
            -Arguments @("-HashHints") `
            -InputLines @("FUNCTION src/app/PromptViewModel.cs :: ParsePhase1RequestLines") `
            -RequiredText @(
                "Hash hint for optional CC-REPLACE header: MODE: function | NAME: ParsePhase1RequestLines | HASH: e4b60dd7"
            ) `
            -ForbiddenText @("native-function-")
        New-ExportCase `
            -Name "CC_LargeFileSkipped" `
            -Kind "cc" `
            -Arguments @("-MaxFileKB", "1") `
            -InputLines @("src/app/LargeFixture.cs") `
            -RequiredText @("Skipped large text file:", "LargeFixture.cs", "Re-run cc.ps1 with -ForceLargeFiles")
        New-ExportCase `
            -Name "CC_LargeFileForced" `
            -Kind "cc" `
            -Arguments @("-MaxFileKB", "1", "-ForceLargeFiles") `
            -InputLines @("src/app/LargeFixture.cs") `
            -RequiredText @("LargeFixtureExport") `
            -ForbiddenText @("Skipped large text file")
        New-ExportCase `
            -Name "CC_FileRules" `
            -Kind "cc" `
            -FileRulesJson @'
{
  "IgnoredDirectories": [ "custom-hidden" ],
  "IgnoredFileNames": [ "LegacyHidden.cs" ],
  "IgnoredFiles": [ "HiddenByRules.cs", "*.tmp.txt" ],
  "IgnoredExtensions": [ ".skipcc" ],
  "SupportedExtensions": [ ".axaml", ".cs", ".cpp", ".glsl", ".h", ".json", ".md", ".ps1", ".specialcc", ".txt", ".vert" ]
}
'@ `
            -InputLines @("FIND: RuleSurfaceMarker", "custom-visible/Feature.specialcc", "src/app/HiddenByRules.cs") `
            -RequiredText @("## FIND: RuleSurfaceMarker", "- custom-visible/Feature.specialcc", "RuleSurfaceMarker from custom supported extension.", "LegacyHiddenMarker") `
            -ForbiddenText @("HiddenByRulesMarker", "custom-hidden/Hidden.cs", "ShownDespiteName.tmp.txt")
        New-ExportCase `
            -Name "CC_MissingPath" `
            -Kind "cc" `
            -InputLines @("missing/Nope.cs") `
            -RequiredText @("## MISSING: missing/Nope.cs")
        New-ExportCase `
            -Name "CC_SymbolDisabled" `
            -Kind "cc" `
            -InputLines @("SYMBOL: SendCommand") `
            -ExpectSuccess $false `
            -RequiredText @("SYMBOL: is disabled")
    )
}

function Resolve-CaseArgument {
    param(
        [string]$Value,
        [string]$OutputPath,
        [string]$ProfilePath
    )

    return $Value.Replace("{OUT}", $OutputPath).Replace("{PROFILE}", $ProfilePath)
}

function Get-CaseOutputPath {
    param(
        $Case,
        [string]$SuiteRoot,
        [string]$ProfilePath
    )

    if ($Case.Name -eq "DIR_ProfileOnly") {
        return $ProfilePath
    }

    return Join-Path $SuiteRoot ($Case.Name + $Case.Extension)
}

function Invoke-ExternalCase {
    param(
        [string]$Executable,
        [string[]]$Arguments,
        [string]$ProjectRoot,
        [string]$FileRulesPath,
        [string[]]$InputLines,
        [string]$ConsolePath
    )

    $oldProjectRoot = $env:CC_WORKBENCH_PROJECT_ROOT
    $oldFileRulesPath = $env:CC_WORKBENCH_FILE_RULES_PATH
    $env:CC_WORKBENCH_PROJECT_ROOT = $ProjectRoot
    if ([string]::IsNullOrWhiteSpace($FileRulesPath)) {
        Remove-Item Env:\CC_WORKBENCH_FILE_RULES_PATH -ErrorAction SilentlyContinue
    }
    else {
        $env:CC_WORKBENCH_FILE_RULES_PATH = $FileRulesPath
    }
    $global:LASTEXITCODE = 0
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $consoleText = ""
    $exitCode = 0

    try {
        if ($InputLines.Count -gt 0) {
            $consoleText = (@($InputLines) + "END" | & $Executable @Arguments 2>&1 | Out-String)
        }
        else {
            $consoleText = (& $Executable @Arguments 2>&1 | Out-String)
        }

        $exitCode = if ($null -eq $global:LASTEXITCODE) { 0 } else { [int]$global:LASTEXITCODE }
    }
    catch {
        $consoleText = ($_ | Out-String)
        $exitCode = 1
    }
    finally {
        $stopwatch.Stop()
        if ($null -eq $oldProjectRoot) {
            Remove-Item Env:\CC_WORKBENCH_PROJECT_ROOT -ErrorAction SilentlyContinue
        }
        else {
            $env:CC_WORKBENCH_PROJECT_ROOT = $oldProjectRoot
        }

        if ($null -eq $oldFileRulesPath) {
            Remove-Item Env:\CC_WORKBENCH_FILE_RULES_PATH -ErrorAction SilentlyContinue
        }
        else {
            $env:CC_WORKBENCH_FILE_RULES_PATH = $oldFileRulesPath
        }
    }

    Set-Content -LiteralPath $ConsolePath -Value $consoleText -Encoding UTF8

    [pscustomobject]@{
        ExitCode = $exitCode
        ElapsedMs = [math]::Round($stopwatch.Elapsed.TotalMilliseconds, 2)
        ConsolePath = $ConsolePath
        ConsoleText = $consoleText
    }
}

function Get-Sha256 {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return ""
    }

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-TextSha256 {
    param([string]$Text)

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Text)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hash = $sha.ComputeHash($bytes)
        return (($hash | ForEach-Object { $_.ToString("x2") }) -join "")
    }
    finally {
        $sha.Dispose()
    }
}

function Get-NormalizedCaseHash {
    param(
        $Case,
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return ""
    }

    $text = Get-Content -LiteralPath $Path -Raw
    if ($Case.Name -eq "DIR_ProfileOnly") {
        $text = $text -replace '"GeneratedUtc"\s*:\s*"[^"]+"', '"GeneratedUtc": "<normalized>"'
    }

    return Get-TextSha256 $text
}

function Assert-CaseResult {
    param(
        $Case,
        $RunResult,
        [string]$OutputPath
    )

    $succeeded = $RunResult.ExitCode -eq 0
    if ($Case.ExpectSuccess -and -not $succeeded) {
        throw "$($Case.Name) failed with exit $($RunResult.ExitCode). Console: $($RunResult.ConsolePath)"
    }

    if ((-not $Case.ExpectSuccess) -and $succeeded) {
        throw "$($Case.Name) was expected to fail but exited 0. Console: $($RunResult.ConsolePath)"
    }

    $text = $RunResult.ConsoleText
    if ($Case.ExpectSuccess) {
        if (-not (Test-Path -LiteralPath $OutputPath)) {
            throw "$($Case.Name) did not create expected output: $OutputPath"
        }

        $text = Get-Content -LiteralPath $OutputPath -Raw
    }

    foreach ($required in $Case.RequiredText) {
        if (-not $text.Contains($required)) {
            throw "$($Case.Name) missing required text '$required'. Output: $OutputPath Console: $($RunResult.ConsolePath)"
        }
    }

    foreach ($forbidden in $Case.ForbiddenText) {
        if ($text.Contains($forbidden)) {
            throw "$($Case.Name) contained forbidden text '$forbidden'. Output: $OutputPath Console: $($RunResult.ConsolePath)"
        }
    }
}

function Invoke-CaseSuite {
    param(
        [string]$SuiteName,
        [string]$SuiteRoot,
        [string]$ProjectRoot,
        [string]$RepositoryRoot,
        [object[]]$Cases,
        [string]$NativeExecutable = ""
    )

    New-CleanDirectory $SuiteRoot
    $profilePath = Join-Path $SuiteRoot "dir_profile.json"
    $powershellExe = Get-DefaultPowerShellExecutable
    $results = New-Object System.Collections.Generic.List[object]

    foreach ($case in $Cases) {
        $outputPath = Get-CaseOutputPath $case $SuiteRoot $profilePath
        $consolePath = Join-Path $SuiteRoot ($case.Name + ".console.txt")
        $fileRulesPath = ""
        if (-not [string]::IsNullOrWhiteSpace($case.FileRulesJson)) {
            $fileRulesPath = Join-Path $SuiteRoot ($case.Name + ".ccFileRules.json")
            [System.IO.File]::WriteAllText($fileRulesPath, $case.FileRulesJson, $script:Utf8NoBom)
        }
        $resolvedCaseArgs = @()
        foreach ($argument in $case.Arguments) {
            $resolvedCaseArgs += Resolve-CaseArgument $argument $outputPath $profilePath
        }

        if ([string]::IsNullOrWhiteSpace($NativeExecutable)) {
            if ($case.Kind -eq "dir") {
                $arguments = @("-NoProfile")
                if ($env:OS -eq "Windows_NT") {
                    $arguments += @("-ExecutionPolicy", "Bypass")
                }

                $arguments += @("-File", (Join-Path $RepositoryRoot "ccDir.ps1"), "-OutputFile", $outputPath)
                $arguments += $resolvedCaseArgs
            }
            else {
                $arguments = @("-NoProfile")
                if ($env:OS -eq "Windows_NT") {
                    $arguments += @("-ExecutionPolicy", "Bypass")
                }

                $arguments += @("-File", (Join-Path $RepositoryRoot "cc.ps1"), "-OutputFile", $outputPath, "-NoClipboard")
                $arguments += $resolvedCaseArgs
            }

            $run = Invoke-ExternalCase `
                -Executable $powershellExe `
                -Arguments $arguments `
                -ProjectRoot $ProjectRoot `
                -FileRulesPath $fileRulesPath `
                -InputLines $case.InputLines `
                -ConsolePath $consolePath
        }
        else {
            $nativeVerb = if ($case.Kind -eq "dir") { "dir" } else { "cc" }
            $arguments = @($nativeVerb, "-OutputFile", $outputPath)
            if ($case.Kind -eq "cc") {
                $arguments += "-NoClipboard"
            }

            $arguments += $resolvedCaseArgs
            $run = Invoke-ExternalCase `
                -Executable $NativeExecutable `
                -Arguments $arguments `
                -ProjectRoot $ProjectRoot `
                -FileRulesPath $fileRulesPath `
                -InputLines $case.InputLines `
                -ConsolePath $consolePath
        }

        Assert-CaseResult $case $run $outputPath

        $result = [pscustomobject]@{
            Suite = $SuiteName
            Name = $case.Name
            Kind = $case.Kind
            ExitCode = $run.ExitCode
            ElapsedMs = $run.ElapsedMs
            OutputPath = $outputPath
            ConsolePath = $consolePath
            Sha256 = if ($case.ExpectSuccess) { Get-Sha256 $outputPath } else { "" }
            NormalizedSha256 = if ($case.ExpectSuccess) { Get-NormalizedCaseHash $case $outputPath } else { "" }
        }
        $results.Add($result)
        Write-Host ("{0,-8} {1,-28} {2,8} ms {3}" -f $SuiteName, $case.Name, $run.ElapsedMs, $result.Sha256)
    }

    return @($results.ToArray())
}

function Compare-NativeResults {
    param(
        [object[]]$Baseline,
        [object[]]$Native,
        [string]$Mode
    )

    function Get-ProfileSemanticSignature {
        param([string]$Path)

        $profile = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
        return [pscustomobject]@{
            VisibleFileCount = [int]$profile.VisibleFileCount
            FileRuleFingerprint = [int]$profile.FileRuleFingerprint
            FileListFingerprint = [string]$profile.FileListFingerprint
            RootCounts = (@($profile.RootCounts.PSObject.Properties | ForEach-Object { "$($_.Name)=$($_.Value)" } | Sort-Object) -join "|")
            MajorManifestHashes = (@($profile.MajorManifestHashes.PSObject.Properties | ForEach-Object { "$($_.Name)=$($_.Value)" } | Sort-Object) -join "|")
            Languages = (@($profile.Languages.PSObject.Properties | ForEach-Object { "$($_.Name)=$($_.Value)" } | Sort-Object) -join "|")
            Stacks = (@($profile.Stacks) -join "|")
            Roots = (@($profile.Roots | ForEach-Object { "$($_.Path)|$($_.Role)|$($_.Files)" }) -join "`n")
            Files = (@($profile.Files | ForEach-Object { "$($_.Path)|$($_.Kind)|$($_.Role)|$($_.Exports)|$($_.Score)" }) -join "`n")
            Families = (@($profile.Families | ForEach-Object { "$($_.Path)|$($_.Role)|$($_.Exports)" }) -join "`n")
        }
    }

    foreach ($baselineResult in $Baseline) {
        $nativeResult = $Native | Where-Object { $_.Name -eq $baselineResult.Name } | Select-Object -First 1
        if ($null -eq $nativeResult) {
            throw "Native result missing case $($baselineResult.Name)."
        }

        if ($Mode -eq "functional") {
            if ($baselineResult.Name -eq "DIR_ProfileOnly") {
                $baselineProfile = Get-ProfileSemanticSignature $baselineResult.OutputPath
                $nativeProfile = Get-ProfileSemanticSignature $nativeResult.OutputPath
                foreach ($property in $baselineProfile.PSObject.Properties.Name) {
                    if ([string]$baselineProfile.$property -ne [string]$nativeProfile.$property) {
                        throw "Native profile semantic mismatch for $property. Baseline=$($baselineResult.OutputPath) Native=$($nativeResult.OutputPath)"
                    }
                }
            }
            continue
        }

        if ($baselineResult.NormalizedSha256 -eq "" -and $nativeResult.NormalizedSha256 -eq "") {
            continue
        }

        if ($baselineResult.NormalizedSha256 -ne $nativeResult.NormalizedSha256) {
            throw "Native output mismatch for $($baselineResult.Name). Baseline=$($baselineResult.OutputPath) Native=$($nativeResult.OutputPath)"
        }
    }
}

$repoRoot = Resolve-RepositoryRoot $RepositoryRoot
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot ".tmp/contextcontrol-export-parity"
}

$runRoot = Join-Path $OutputRoot (Get-Date -Format "yyyyMMdd-HHmmss")
$fixtureRoot = Join-Path $runRoot "fixture"
$baselineRoot = Join-Path $runRoot "baseline"
$nativeRoot = Join-Path $runRoot "native"

[void](New-Item -ItemType Directory -Path $runRoot -Force)
New-FixtureProject $fixtureRoot

$cases = @(Get-ExportCases)
Write-Host "ContextControl export parity run"
Write-Host "Repository: $repoRoot"
Write-Host "Fixture:    $fixtureRoot"
Write-Host "Run root:   $runRoot"
Write-Host ""

$baselineResults = @(Invoke-CaseSuite `
    -SuiteName "baseline" `
    -SuiteRoot $baselineRoot `
    -ProjectRoot $fixtureRoot `
    -RepositoryRoot $repoRoot `
    -Cases $cases)

$nativeResults = @()
if (-not [string]::IsNullOrWhiteSpace($NativeExe)) {
    $nativePath = (Resolve-Path -LiteralPath $NativeExe).Path
    Write-Host ""
    Write-Host "Native executable: $nativePath"
    $nativeResults = @(Invoke-CaseSuite `
        -SuiteName "native" `
        -SuiteRoot $nativeRoot `
        -ProjectRoot $fixtureRoot `
        -RepositoryRoot $repoRoot `
        -Cases $cases `
        -NativeExecutable $nativePath)
    Compare-NativeResults $baselineResults $nativeResults $NativeCompare
}
elseif ($RequireNative) {
    throw "-RequireNative was supplied but -NativeExe is empty."
}

$summary = [pscustomobject]@{
    RepositoryRoot = $repoRoot
    FixtureRoot = $fixtureRoot
    RunRoot = $runRoot
    NativeExe = $NativeExe
    NativeCompare = $NativeCompare
    CaseCount = $cases.Count
    Baseline = $baselineResults
    Native = $nativeResults
}
$summaryPath = Join-Path $runRoot "summary.json"
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $summaryPath -Encoding UTF8

Write-Host ""
Write-Host "Summary: $summaryPath"
Write-Host "Baseline cases passed: $($baselineResults.Count)"
if ($nativeResults.Count -gt 0) {
    Write-Host "Native parity cases passed: $($nativeResults.Count)"
}

if (-not $KeepFixture) {
    Write-Host "Fixture kept for audit because outputs reference it: $fixtureRoot"
}
