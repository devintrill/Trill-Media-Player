$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$frameworkRoot = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$testRoot = Join-Path $PSScriptRoot 'temp\frames'
[IO.Directory]::CreateDirectory($testRoot) | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'dist\Trill.exe') -Destination (Join-Path $testRoot 'Trill.exe') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'App.config') -Destination (Join-Path $testRoot 'Frames.Tests.exe.config') -Force
$testExecutable = Join-Path $testRoot 'Frames.Tests.exe'
$arguments = @('/nologo', '/langversion:5', '/optimize+', '/target:exe', '/codepage:65001', "/out:$testExecutable", "/reference:$(Join-Path $testRoot 'Trill.exe')")
foreach ($reference in @('System.dll', 'System.Core.dll', 'WPF\WindowsBase.dll', 'WPF\PresentationCore.dll', 'WPF\PresentationFramework.dll')) {
    $arguments += "/reference:$(Join-Path $frameworkRoot $reference)"
}
$arguments += Join-Path $PSScriptRoot 'FramePreview.Tests.cs'
$arguments += Join-Path $projectRoot 'src\NativeVideoReader.cs'
$arguments += Join-Path $projectRoot 'src\ScrubPreview.cs'
& (Join-Path $frameworkRoot 'csc.exe') $arguments
if ($LASTEXITCODE -ne 0) { throw 'Frame preview checks failed to compile.' }
& $testExecutable (Join-Path $testRoot 'generated-h264.mp4')
if ($LASTEXITCODE -ne 0) { throw 'Frame preview checks failed.' }

