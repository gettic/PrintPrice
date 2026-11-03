# Regenerates assets\app.ico and assets\logo.png from tools\MakeIcon.cs.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot)
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
New-Item -ItemType Directory -Force bin | Out-Null
& $csc /nologo /codepage:65001 /out:bin\MakeIcon.exe /r:System.Drawing.dll tools\MakeIcon.cs
if ($LASTEXITCODE) { throw 'MakeIcon build failed' }
& .\bin\MakeIcon.exe assets
