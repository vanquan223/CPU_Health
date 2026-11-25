param([string]$OutputName = 'CPU Health.exe')

$ErrorActionPreference = 'Stop'

$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework'
$compiler = Join-Path $framework 'v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'The .NET Framework C# compiler (csc.exe) was not found.'
}

$output = Join-Path $PSScriptRoot 'bin'
$executable = Join-Path $output $OutputName
$manifest = Join-Path $PSScriptRoot 'src\app.manifest'
$icon = Join-Path $PSScriptRoot 'src\app.ico'
$sources = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' -File
New-Item -ItemType Directory -Path $output -Force | Out-Null

& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /warnaserror+ /codepage:65001 `
    "/out:$executable" "/win32manifest:$manifest" "/win32icon:$icon" `
    /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Management.dll `
    $sources.FullName
if ($LASTEXITCODE -ne 0) {
    throw "Build failed (csc exit code $LASTEXITCODE)."
}

Write-Output $executable
