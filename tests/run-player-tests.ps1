param([switch]$Preview)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$frameworkRoot = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path -LiteralPath (Join-Path $frameworkRoot 'csc.exe'))) { $frameworkRoot = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319' }
$testRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'temp\player'))
$expectedRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'temp')) + '\'
if (-not $testRoot.StartsWith($expectedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid fixture boundary.' }
[IO.Directory]::CreateDirectory($testRoot) | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot $(if ($Preview) { 'dist\preview\Trill.exe' } else { 'dist\Trill.exe' })) -Destination (Join-Path $testRoot 'Trill.exe') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'App.config') -Destination (Join-Path $testRoot 'Player.Tests.exe.config') -Force
$testExecutable = Join-Path $testRoot 'Player.Tests.exe'
$testArguments = @('/nologo', '/langversion:5', '/target:exe', '/codepage:65001', "/out:$testExecutable", "/reference:$(Join-Path $testRoot 'Trill.exe')")
foreach ($reference in @('System.dll', 'System.Core.dll', 'System.Xaml.dll', 'WPF\WindowsBase.dll', 'WPF\PresentationCore.dll', 'WPF\PresentationFramework.dll')) {
    $testArguments += "/reference:$(Join-Path $frameworkRoot $reference)"
}
$testArguments += Join-Path $PSScriptRoot 'Player.SmokeTests.cs'
& (Join-Path $frameworkRoot 'csc.exe') $testArguments
if ($LASTEXITCODE -ne 0) { throw 'Player smoke tests failed to compile.' }
& $testExecutable (Join-Path $testRoot 'fixtures')
if ($LASTEXITCODE -ne 0) { throw 'Player smoke tests failed.' }
