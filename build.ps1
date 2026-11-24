[CmdletBinding()]
param(
    [string] $OutputDirectory = (Join-Path $PSScriptRoot 'artifacts\Biandan'),
    [switch] $Package,
    [string] $ReleaseDirectory = (Join-Path $PSScriptRoot 'release')
)

$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:windir 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
if (!(Test-Path -LiteralPath $compiler -PathType Leaf)) {
    throw 'The Windows x64 .NET Framework compiler is required to build Biandan.'
}

$references = @(
    'System', 'System.Core', 'System.Drawing', 'System.IO.Compression', 'System.Net.Http',
    'System.Security', 'System.Web.Extensions', 'System.Windows.Forms', 'System.Xaml', 'System.Xml',
    'WPF\WindowsBase', 'WPF\PresentationCore', 'WPF\PresentationFramework', 'WPF\System.Windows.Presentation'
) | ForEach-Object { '/reference:' + (Join-Path $framework "$_.dll") }

$sourceRoot = Join-Path $PSScriptRoot 'src'
$sources = @(Get-ChildItem -LiteralPath $sourceRoot -Filter '*.cs' -Recurse | ForEach-Object FullName)
$output = (New-Item -ItemType Directory -Path $OutputDirectory -Force).FullName
$icon = Join-Path $PSScriptRoot 'assets\app.ico'
$theme = Join-Path $sourceRoot 'Views\Theme.xaml'
$appImage = Join-Path $PSScriptRoot 'assets\app.png'
$service = Join-Path $PSScriptRoot 'backend\biandan-mcp.exe'
$assistantSkill = Join-Path $PSScriptRoot 'assets\biandan-assistant.zip'
$exe = Join-Path $output 'Biandan.exe'
$arguments = @(
    '/nologo', '/utf8output', '/codepage:65001', '/langversion:5', '/target:winexe',
    '/platform:x64', '/optimize+', "/out:$exe", "/win32icon:$icon", "/resource:$theme,Biandan.Theme.xaml",
    "/resource:$appImage,Biandan.App.png", "/resource:$icon,Biandan.App.ico", "/resource:$service,Biandan.Backend.exe",
    "/resource:$assistantSkill,Biandan.AssistantSkill.zip"
)
& $compiler @arguments @references @sources
if ($LASTEXITCODE -ne 0) {
    throw "Biandan compilation failed with exit code $LASTEXITCODE."
}

$licenseOutput = (New-Item -ItemType Directory -Path (Join-Path $output 'licenses') -Force).FullName
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'backend\licenses\Python.txt'), (Join-Path $PSScriptRoot 'backend\licenses\PyInstaller.txt') -Destination $licenseOutput -Force

$readme = Join-Path $output 'README.md'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination $readme -Force
Write-Output $exe

if ($Package) {
    $target = (New-Item -ItemType Directory -Path $ReleaseDirectory -Force).FullName
    $version = [Reflection.AssemblyName]::GetAssemblyName($exe).Version.ToString(3)
    $archive = Join-Path $target "Biandan-Windows-x64-$version.zip"
    Compress-Archive -LiteralPath $exe, $readme, $licenseOutput -DestinationPath $archive -Force
    Write-Output $archive
}
