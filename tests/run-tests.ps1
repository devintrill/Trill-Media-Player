param()

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) {
    $compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compilerPath)) {
    throw 'The .NET Framework C# compiler was not found. Install .NET Framework 4.8 to run tests.'
}

$temporaryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'temp'))
$runDirectory = [IO.Path]::GetFullPath((Join-Path $temporaryRoot ([Guid]::NewGuid().ToString('N'))))
$expectedBoundary = $temporaryRoot.TrimEnd('\') + '\'
if (-not $runDirectory.StartsWith($expectedBoundary, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Test fixture path is outside the intended test directory.'
}
[IO.Directory]::CreateDirectory($runDirectory) | Out-Null

try {
    $testExecutable = Join-Path $runDirectory 'MediaFiles.Tests.exe'
    $frameworkDirectory = Split-Path -Parent $compilerPath
    # MediaItem exposes a preview handle, so the media tests link the same WPF
    # assemblies the settings tests already use.
    $mediaArguments = @(
        '/nologo', '/langversion:5', '/target:exe',
        ('/out:' + $testExecutable),
        ('/reference:' + (Join-Path $frameworkDirectory 'WPF\WindowsBase.dll')),
        ('/reference:' + (Join-Path $frameworkDirectory 'WPF\PresentationCore.dll')),
        ('/reference:' + (Join-Path $frameworkDirectory 'WPF\PresentationFramework.dll')),
        ('/reference:' + (Join-Path $frameworkDirectory 'System.Xaml.dll')),
        (Join-Path $projectRoot 'src\MediaFiles.cs'),
        (Join-Path $projectRoot 'src\Thumbnails.cs'),
        (Join-Path $projectRoot 'src\NativeVideoReader.cs'),
        (Join-Path $PSScriptRoot 'MediaFiles.Tests.cs')
    )
    & $compilerPath $mediaArguments
    if ($LASTEXITCODE -ne 0) { throw 'Media-file tests failed to compile.' }
    $fixtures = Join-Path $runDirectory 'fixtures'
    $junctionTarget = Join-Path $runDirectory 'junction-target'
    [IO.Directory]::CreateDirectory($fixtures) | Out-Null
    [IO.Directory]::CreateDirectory($junctionTarget) | Out-Null
    [IO.File]::WriteAllText((Join-Path $junctionTarget 'linked.jpg'), 'Local junction test fixture')
    New-Item -ItemType Junction -Path (Join-Path $fixtures 'linked-folder') -Target $junctionTarget | Out-Null
    & $testExecutable $fixtures
    if ($LASTEXITCODE -ne 0) { throw 'Media-file tests failed.' }

    $settingsExecutable = Join-Path $runDirectory 'Settings.Tests.exe'
    $settingsArguments = @(
        '/nologo', '/langversion:5', '/target:exe',
        ('/out:' + $settingsExecutable),
        ('/reference:' + (Join-Path $frameworkDirectory 'WPF\WindowsBase.dll')),
        ('/reference:' + (Join-Path $frameworkDirectory 'WPF\PresentationCore.dll')),
        (Join-Path $projectRoot 'src\Models.cs'),
        (Join-Path $projectRoot 'src\SettingsStore.cs'),
        (Join-Path $PSScriptRoot 'Settings.Tests.cs')
    )
    & $compilerPath $settingsArguments
    if ($LASTEXITCODE -ne 0) { throw 'Settings tests failed to compile.' }
    & $settingsExecutable
    if ($LASTEXITCODE -ne 0) { throw 'Settings tests failed.' }
}
finally {
    $resolvedRunDirectory = [IO.Path]::GetFullPath($runDirectory)
    if (-not $resolvedRunDirectory.StartsWith($expectedBoundary, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing to remove a fixture directory outside tests\temp.'
    }
    if (Test-Path -LiteralPath $resolvedRunDirectory) {
        Remove-Item -LiteralPath $resolvedRunDirectory -Recurse -Force
    }
}
