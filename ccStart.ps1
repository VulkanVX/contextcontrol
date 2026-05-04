# CC-DESC: Starts Context Control patch watching in agent mode.
# ccStart.ps1
# Run from your project root or from the folder containing the Context Control scripts.

param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$ForwardedArgs
)

$ErrorActionPreference = "Stop"

$candidates = @()

if ($PSScriptRoot -ne "") {
    $candidates += (Join-Path $PSScriptRoot "ccReplace.ps1")
}

$candidates += (Join-Path (Get-Location).Path "ccReplace.ps1")

$ccReplace = ""
foreach ($candidate in $candidates) {
    if (Test-Path -LiteralPath $candidate) {
        $ccReplace = (Resolve-Path -LiteralPath $candidate).Path
        break
    }
}

if ($ccReplace -eq "") {
    throw "ccReplace.ps1 was not found next to ccStart.ps1 or in the current directory."
}

& $ccReplace -AgentMode @ForwardedArgs
exit $LASTEXITCODE
