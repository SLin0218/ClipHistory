param(
    [ValidateSet("All", "CmdPal", "V1")]
    [string]$Target = "CmdPal",
    [switch]$Deploy,
    [switch]$Restart
)

$ErrorActionPreference = "Stop"

$dotnetPath = $null
$cmd = Get-Command dotnet -ErrorAction SilentlyContinue
if ($cmd) {
    $dotnetPath = $cmd.Source
}

if (-not $dotnetPath) {
    $candidatePaths = @(
        "$env:USERPROFILE\.dotnet\dotnet.exe",
        "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe",
        "C:\Program Files\dotnet\dotnet.exe"
    )
    foreach ($cand in $candidatePaths) {
        if (Test-Path $cand) {
            $dotnetPath = $cand
            $dir = Split-Path $cand
            $env:PATH = "$dir;$env:PATH"
            break
        }
    }
}

if (-not $dotnetPath) {
    Write-Error "Cannot find dotnet SDK."
    exit 1
}

$env:DOTNET_ROOT = Split-Path $dotnetPath
Write-Host ">> Using .NET SDK: $dotnetPath" -ForegroundColor Cyan

$root = $PSScriptRoot
if (-not $root) { $root = (Get-Item -Path ".\").FullName }

# -------------------------------------------------------------
# 1. Build & Deploy CmdPal (PowerToys Run V2) Extension
# -------------------------------------------------------------
if ($Target -eq "All" -or $Target -eq "CmdPal") {
    Write-Host ""
    Write-Host "=== [1] Building PowerToys Run V2 (CmdPal) Extension ===" -ForegroundColor Green

    $cmdPalProj = Join-Path $root "CmdPalExt\SlinClipCmdPal.csproj"
    $cmdPalPublish = Join-Path $root "CmdPalExt\publish"
    $assetsDir = Join-Path $root "CmdPalExt\Assets"
    $manifestPath = Join-Path $cmdPalPublish "AppxManifest.xml"

    if ($Deploy) {
        Stop-Process -Name "Microsoft.CmdPal.UI" -Force -ErrorAction SilentlyContinue
        Stop-Process -Name "SlinClipCmdPal" -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 600
    }

    Write-Host "Publishing SlinClipCmdPal -> $cmdPalPublish" -ForegroundColor Cyan
    & $dotnetPath publish $cmdPalProj -c Release -r win-x64 --self-contained false -o $cmdPalPublish --nologo
    if ($LASTEXITCODE -ne 0) {
        Write-Error "CmdPal extension publish failed!"
        exit $LASTEXITCODE
    }

    Copy-Item -Path $assetsDir -Destination "$cmdPalPublish\Assets" -Recurse -Force

    if ($Deploy) {
        Write-Host "Registering AppxPackage: $manifestPath" -ForegroundColor Cyan
        Add-AppxPackage -Register $manifestPath

        $settingsPath = "$env:LOCALAPPDATA\Packages\Microsoft.CommandPalette_8wekyb3d8bbwe\LocalState\settings.json"
        if (Test-Path $settingsPath) {
            $json = Get-Content -Raw -Encoding UTF8 -Path $settingsPath | ConvertFrom-Json
            $extKey = "SlinClip.CommandPalette_7gt9mds3x1cs6!App!SlinClipCmdPalExtension"
            if (-not $json.ProviderSettings.PSObject.Properties[$extKey]) {
                $provObj = [PSCustomObject]@{
                    IsEnabled = $true
                    FallbackCommands = [PSCustomObject]@{}
                    PinnedCommandIds = @()
                }
                $json.ProviderSettings | Add-Member -MemberType NoteProperty -Name $extKey -Value $provObj
            }
            if (-not $json.Aliases.PSObject.Properties["sc"]) {
                $scObj = [PSCustomObject]@{
                    CommandId = "SlinClipHistoryPage"
                    Alias = "sc"
                    IsDirect = $true
                }
                $json.Aliases | Add-Member -MemberType NoteProperty -Name "sc" -Value $scObj
            }
            if (-not $json.Aliases.PSObject.Properties["clip"]) {
                $clipObj = [PSCustomObject]@{
                    CommandId = "SlinClipHistoryPage"
                    Alias = "clip"
                    IsDirect = $true
                }
                $json.Aliases | Add-Member -MemberType NoteProperty -Name "clip" -Value $clipObj
            }
            $json | ConvertTo-Json -Depth 20 | Set-Content -Path $settingsPath -Encoding UTF8
        }

        $cachePath = "$env:LOCALAPPDATA\Packages\Microsoft.CommandPalette_8wekyb3d8bbwe\LocalState\commandProviderCache.json"
        if (Test-Path $cachePath) {
            $cacheJson = Get-Content -Raw -Encoding UTF8 -Path $cachePath | ConvertFrom-Json
            $extKey = "SlinClip.CommandPalette_7gt9mds3x1cs6!App!SlinClipCmdPalExtension"
            if (-not $cacheJson.Cache.PSObject.Properties[$extKey]) {
                $dispObj = [PSCustomObject]@{ DisplayName = "Slin-Clip Clipboard History" }
                $cacheJson.Cache | Add-Member -MemberType NoteProperty -Name $extKey -Value $dispObj
                $cacheJson | ConvertTo-Json -Depth 10 | Set-Content -Path $cachePath -Encoding UTF8
            }
        }

        Write-Host "CmdPal Extension registered and configured successfully!" -ForegroundColor Green

        if ($Restart) {
            Write-Host "Relaunching CmdPal UI..." -ForegroundColor Yellow
            $cmdPalExe = "C:\Program Files\WindowsApps\Microsoft.CommandPalette_0.11.11762.0_x64__8wekyb3d8bbwe\Microsoft.CmdPal.UI.exe"
            if (Test-Path $cmdPalExe) {
                Start-Process $cmdPalExe
                Write-Host "CmdPal UI restarted!" -ForegroundColor Green
            }
        }
    }
}

# -------------------------------------------------------------
# 2. Build & Deploy PowerToys Run V1 (Wox.Plugin) Plugin
# -------------------------------------------------------------
if ($Target -eq "All" -or $Target -eq "V1") {
    Write-Host ""
    Write-Host "=== [2] Building PowerToys Run V1 (Wox) Plugin ===" -ForegroundColor Green

    $v1Publish = Join-Path $root "bin\Release\publish"
    $v1Proj = Join-Path $root "SlinClip.csproj"

    & $dotnetPath publish $v1Proj -c Release -o $v1Publish --nologo
    if ($LASTEXITCODE -ne 0) {
        Write-Error "V1 Plugin publish failed!"
        exit $LASTEXITCODE
    }

    if ($Deploy) {
        $targetDir = "$env:LOCALAPPDATA\Microsoft\PowerToys\PowerToys Run\Plugins\Slin-Clip"
        Write-Host "Deploying V1 plugin to: $targetDir" -ForegroundColor Cyan
        Stop-Process -Name "PowerToys.PowerLauncher" -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 500

        if (-not (Test-Path $targetDir)) {
            New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
        }
        Copy-Item -Path "$v1Publish\*" -Destination $targetDir -Recurse -Force
        Write-Host "V1 Deploy completed successfully!" -ForegroundColor Green

        if ($Restart) {
            $launcherExe = "C:\Users\USER\scoop\apps\powertoys\current\PowerToys.PowerLauncher.exe"
            if (Test-Path $launcherExe) {
                Start-Process $launcherExe
                Write-Host "PowerLauncher restarted!" -ForegroundColor Green
            }
        }
    }
}

Write-Host ""
Write-Host "All operations completed successfully!" -ForegroundColor Green
