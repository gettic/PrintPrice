# Builds PrintPrice.exe using the C# compiler that ships with Windows (.NET Framework 4.8).
# No SDK or Visual Studio needed. Usage:  powershell -ExecutionPolicy Bypass -File build.ps1
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
New-Item -ItemType Directory -Force bin | Out-Null

$refs = '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll', '/r:System.Web.Extensions.dll',
        '/r:System.IO.Compression.dll', '/r:System.IO.Compression.FileSystem.dll'
$core = 'src\Pricing.cs', 'src\AppSettings.cs', 'src\Num.cs', 'src\Theme.cs', 'src\Slicer.cs', 'src\Printables.cs', 'src\PowerPrice.cs', 'src\Updater.cs'

& $csc /nologo /codepage:65001 /optimize+ /target:exe /out:bin\PricingTests.exe $refs $core tests\PricingTests.cs
if ($LASTEXITCODE) { throw 'Test build failed' }
& .\bin\PricingTests.exe
if ($LASTEXITCODE) { throw 'Tests failed' }

if (-not (Test-Path assets\app.ico)) { & "$PSScriptRoot\tools\make-icon.ps1" }
& $csc /nologo /codepage:65001 /optimize+ /target:winexe /win32icon:assets\app.ico /out:bin\PrintPrice.exe $refs src\*.cs
if ($LASTEXITCODE) { throw 'App build failed' }
Write-Host "Built bin\PrintPrice.exe"
