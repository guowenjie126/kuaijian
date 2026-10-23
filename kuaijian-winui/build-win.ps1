$ErrorActionPreference = "Stop"

$programFilesPath = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)
$programFilesX86Path = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFilesX86)
$programDataPath = [Environment]::GetFolderPath([Environment+SpecialFolder]::CommonApplicationData)
if ([string]::IsNullOrWhiteSpace($env:ProgramFiles)) { $env:ProgramFiles = $programFilesPath }
if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable('ProgramFiles(x86)'))) {
    [Environment]::SetEnvironmentVariable('ProgramFiles(x86)', $programFilesX86Path, 'Process')
}
if ([string]::IsNullOrWhiteSpace($env:ProgramData)) { $env:ProgramData = $programDataPath }
if ([string]::IsNullOrWhiteSpace($env:APPDATA)) { $env:APPDATA = [Environment]::GetFolderPath([Environment+SpecialFolder]::ApplicationData) }
if ([string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) { $env:LOCALAPPDATA = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData) }
$dotnetExe = Join-Path $programFilesPath "dotnet\dotnet.exe"
if (-not (Test-Path -LiteralPath $dotnetExe)) {
    $dotnetCommand = Get-Command dotnet.exe -ErrorAction SilentlyContinue
    if ($dotnetCommand) { $dotnetExe = $dotnetCommand.Source }
    else { throw ".NET SDK executable was not found. Install .NET 8 SDK and try again." }
}
$env:PATH = (Split-Path -Parent $dotnetExe) + ";" + $env:PATH

$restoreProcess = Start-Process -FilePath $dotnetExe -ArgumentList @("restore") -Wait -PassThru -NoNewWindow
if ($restoreProcess.ExitCode -ne 0) { throw "dotnet restore failed with exit code $($restoreProcess.ExitCode)." }
$buildProcess = Start-Process -FilePath $dotnetExe -ArgumentList @("build", "-c", "Release", "-p:Platform=x64") -Wait -PassThru -NoNewWindow
if ($buildProcess.ExitCode -ne 0) { throw "dotnet build failed with exit code $($buildProcess.ExitCode)." }
$publishProcess = Start-Process -FilePath $dotnetExe -ArgumentList @("publish", "-c", "Release", "-r", "win-x64", "--self-contained", "true", "-p:Platform=x64", "-p:WindowsAppSDKSelfContained=true", "-o", "publish\win-x64") -Wait -PassThru -NoNewWindow
if ($publishProcess.ExitCode -ne 0) { throw "dotnet publish failed with exit code $($publishProcess.ExitCode)." }

Write-Host "构建完成：publish\win-x64"
Write-Host "如需生成安装包，请继续运行：.\build-installer.ps1"
