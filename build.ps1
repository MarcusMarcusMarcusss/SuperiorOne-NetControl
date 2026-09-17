param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist'))
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /out:"$OutputDirectory\SuperiorOneNet.exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll "$PSScriptRoot\src\*.cs"
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
Write-Output "Built: $OutputDirectory\SuperiorOneNet.exe"
