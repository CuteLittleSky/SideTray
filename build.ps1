# 本地编译 SideTray。
# 只用 Windows 自带 .NET Framework 的 csc，不需要装 SDK / Visual Studio。
# 产物：bin\SideTray.exe
#
# 想跟 CI 完全一致，也可以用 MSBuild：
#   msbuild SideTray.csproj /p:Configuration=Release
#   -> bin\Release\SideTray.exe

$ErrorActionPreference = 'Stop'

$root   = $PSScriptRoot
$srcDir = Join-Path $root 'src'
$outDir = Join-Path $root 'bin'
$out    = Join-Path $outDir 'SideTray.exe'

$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) {
    throw "找不到 csc.exe：$csc（需要 .NET Framework 4.x）"
}

New-Item -ItemType Directory -Force -Path $outDir | Out-Null
if (Test-Path $out) {
    try { Remove-Item $out -Force -ErrorAction Stop }
    catch { throw "产物被占用（程序是不是正在运行？）。先退出 SideTray 再重试。" }
}

& $csc /nologo /target:winexe /platform:x64 /optimize+ `
       "/out:$out" `
       "/win32manifest:$srcDir\app.manifest" `
       /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
       "$srcDir\AssemblyInfo.cs" "$srcDir\AudioCore.cs" "$srcDir\Program.cs"

if ($LASTEXITCODE -ne 0 -or -not (Test-Path $out)) { throw '编译失败' }

$size = [math]::Round((Get-Item $out).Length / 1KB, 1)
Write-Host "编译完成：$out（$size KB）" -ForegroundColor Green
Write-Host '如果程序正在运行，先退出再启动新的。'
