[CmdletBinding()]
param(
    [switch]$Run,
    [switch]$Preview
)

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$frameworkRoot = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path -LiteralPath (Join-Path $frameworkRoot 'csc.exe'))) {
    $frameworkRoot = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
}
$compiler = Join-Path $frameworkRoot 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'The .NET Framework C# compiler was not found. Trill requires Windows with .NET Framework 4.8 installed.'
}

$sources = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Recurse -File -Filter '*.cs' | Sort-Object FullName)
if ($sources.Count -eq 0) {
    throw 'No C# source files were found in the src folder.'
}

$referenceNames = @(
    'System.dll',
    'System.Core.dll',
    'System.Xaml.dll',
    'System.Xml.dll',
    'System.Runtime.Serialization.dll',
    'System.Windows.Forms.dll',
    'System.Drawing.dll',
    'WPF\WindowsBase.dll',
    'WPF\PresentationCore.dll',
    'WPF\PresentationFramework.dll'
)
$references = foreach ($referenceName in $referenceNames) {
    $referencePath = Join-Path $frameworkRoot $referenceName
    if (-not (Test-Path -LiteralPath $referencePath)) {
        throw "A required Windows framework assembly was not found: $referencePath"
    }
    $referencePath
}

$outputDirectory = Join-Path $projectRoot $(if ($Preview) { 'dist\preview' } else { 'dist' })
[void](New-Item -ItemType Directory -Path $outputDirectory -Force)
$executablePath = Join-Path $outputDirectory 'Trill.exe'
$responsePath = Join-Path $outputDirectory 'build.rsp'

# A response file preserves paths with spaces without shell-dependent quoting.
$compilerOptions = @(
    '/nologo',
    '/target:winexe',
    '/main:Trill.App',
    '/platform:anycpu',
    '/langversion:5',
    '/codepage:65001',
    '/optimize+',
    '/warn:4',
    ('/win32icon:"{0}"' -f (Join-Path $projectRoot 'assets\trill.ico')),
    ('/out:"{0}"' -f $executablePath),
    ('/win32manifest:"{0}"' -f (Join-Path $projectRoot 'app.manifest'))
)
foreach ($referencePath in $references) {
    $compilerOptions += '/reference:"{0}"' -f $referencePath
}
$windowResource = Join-Path $projectRoot 'src\MainWindow.xaml'
if (Test-Path -LiteralPath $windowResource) {
    $compilerOptions += '/resource:"{0}",Trill.MainWindow.xaml' -f $windowResource
}
$compilerOptions += '/resource:"{0}",Trill.Logo.png' -f (Join-Path $projectRoot 'assets\trill-logo-v1.png')
$controlResource = Join-Path $projectRoot 'src\ControlStyles.xaml'
$compilerOptions += '/resource:"{0}",Trill.ControlStyles.xaml' -f $controlResource
foreach ($source in $sources) {
    $compilerOptions += '"{0}"' -f $source.FullName
}
$utf8 = New-Object System.Text.UTF8Encoding($true)
[System.IO.File]::WriteAllLines($responsePath, [string[]]$compilerOptions, $utf8)

Write-Host 'Building Trill from local source files...'
& $compiler ('@' + $responsePath)
if ($LASTEXITCODE -ne 0) {
    throw "The build failed (compiler exit code $LASTEXITCODE)."
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'App.config') -Destination ($executablePath + '.config') -Force
Write-Host "Built $executablePath"

if ($Run) {
    Start-Process -FilePath $executablePath -WorkingDirectory $outputDirectory
}
