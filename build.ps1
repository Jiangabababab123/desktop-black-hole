$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler)) {
    throw '未找到 .NET Framework C# 编译器。请安装 .NET Framework 4.x。'
}

$source = Join-Path $root 'src\BlackHole.cs'
$art = Join-Path $root 'assets\BlackHoleArt.png'
$icon = Join-Path $root 'assets\DesktopBlackHole.ico'
$output = Join-Path $root 'dist\DesktopBlackHole.exe'
New-Item -ItemType Directory -Force (Split-Path -Parent $output) | Out-Null

& $compiler /nologo /unsafe /target:winexe /optimize+ "/out:$output" `
    /reference:System.Windows.Forms.dll /reference:System.Drawing.dll `
    "/resource:$art,BlackHoleArt" "/win32icon:$icon" $source
if ($LASTEXITCODE -ne 0) { throw "编译失败，退出码 $LASTEXITCODE" }
Write-Host "构建完成：$output"
