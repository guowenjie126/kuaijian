$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
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
$publishDir = Join-Path $projectRoot "publish\win-x64"
$installerDir = Join-Path $projectRoot "installer"
$issPath = Join-Path $projectRoot "installer.iss"
$packageRoot = Join-Path $projectRoot ".nuget\packages"
$iconPath = Join-Path $projectRoot "Assets\KuaiJian.ico"

if (-not (Test-Path $iconPath)) {
    & (Join-Path $projectRoot "tools\GenerateIcon.ps1") -OutputPath $iconPath
}

Write-Host "Publishing the current Release build from an ASCII staging path..."
    $stageRoot = Join-Path $env:TEMP ("KuaiJian-WinUI-build-" + [Guid]::NewGuid().ToString("N"))
    $stageProjectRoot = Join-Path $stageRoot "src"
    try {
        New-Item -ItemType Directory -Force -Path $stageProjectRoot | Out-Null

        Get-ChildItem -LiteralPath $projectRoot -Force |
            Where-Object { $_.Name -notin @("bin", "obj", "publish", "installer", ".nuget") } |
            Copy-Item -Destination $stageProjectRoot -Recurse -Force

        $stageProject = Join-Path $stageProjectRoot "KuaiJian.WinUI.csproj"
        $restoreArgs = @(
            "restore", $stageProject,
            "-p:RestorePackagesPath=$packageRoot", "-p:NuGetPackageRoot=$packageRoot",
            "--ignore-failed-sources"
        )
        $restoreProcess = Start-Process -FilePath $dotnetExe -ArgumentList $restoreArgs -Wait -PassThru -NoNewWindow
        if ($restoreProcess.ExitCode -ne 0) {
            throw "dotnet restore failed with exit code $($restoreProcess.ExitCode)."
        }

        $publishArgs = @(
            "publish", $stageProject, "-c", "Release", "-r", "win-x64",
            "--self-contained", "true", "--no-restore", "-p:Platform=x64",
            "-p:WindowsAppSDKSelfContained=true", "-p:RestorePackagesPath=$packageRoot",
            "-p:NuGetPackageRoot=$packageRoot", "-o", $publishDir
        )
        $publishProcess = Start-Process -FilePath $dotnetExe -ArgumentList $publishArgs -Wait -PassThru -NoNewWindow
        if ($publishProcess.ExitCode -ne 0) {
            throw "dotnet publish failed with exit code $($publishProcess.ExitCode)."
        }
    }
    finally {
        if (Test-Path -LiteralPath $stageRoot) {
            $tempRootFull = [IO.Path]::GetFullPath($env:TEMP).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
            $stageRootFull = [IO.Path]::GetFullPath($stageRoot)
            $stageName = Split-Path -Leaf $stageRootFull
            if ($stageRootFull.StartsWith($tempRootFull, [StringComparison]::OrdinalIgnoreCase) -and
                $stageName -match '^KuaiJian-WinUI-build-[0-9a-f]{32}$') {
                Remove-Item -LiteralPath $stageRootFull -Recurse -Force
            }
            else {
                Write-Warning "Refusing to remove an unexpected staging path: $stageRootFull"
            }
        }
    }
$isccCandidates = @(
    (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source,
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) }

if (-not $isccCandidates) {
    throw "Inno Setup 6 was not found. Install Inno Setup 6 and run this script again."
}

New-Item -ItemType Directory -Force -Path $installerDir | Out-Null
$isccPath = $isccCandidates | Select-Object -First 1
$isccProcess = Start-Process -FilePath $isccPath -ArgumentList @($issPath) -Wait -PassThru -NoNewWindow
if ($isccProcess.ExitCode -ne 0) {
    throw "Inno Setup compilation failed with exit code $($isccProcess.ExitCode)."
}

Write-Host ("Installer generated: " + (Join-Path $installerDir "KuaiJian-0.4.0-Windows-x64-Setup.exe"))
