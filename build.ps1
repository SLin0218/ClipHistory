param(
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

    $cmdPalProj = Join-Path $root "CmdPalExt\ClipHistoryCmdPal.csproj"
    $cmdPalPublish = Join-Path $root "CmdPalExt\publish"
    $assetsDir = Join-Path $root "CmdPalExt\Assets"
    $manifestPath = Join-Path $cmdPalPublish "AppxManifest.xml"

    # Always stop running extension process to unlock binary during publish
    Stop-Process -Name "ClipHistoryCmdPal", "SlinClipCmdPal" -Force -ErrorAction SilentlyContinue
    if ($Deploy) {
        Stop-Process -Name "Microsoft.CmdPal.UI" -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 600
    }

    Write-Host "Publishing ClipHistoryCmdPal -> $cmdPalPublish" -ForegroundColor Cyan
    & $dotnetPath publish $cmdPalProj -c Release -r win-x64 --self-contained false -o $cmdPalPublish --nologo
    if ($LASTEXITCODE -ne 0) {
        Write-Error "CmdPal extension publish failed!"
        exit $LASTEXITCODE
    }

    Copy-Item -Path (Join-Path $root "CmdPalExt\Package.appxmanifest") -Destination $manifestPath -Force
    $targetAssets = Join-Path $cmdPalPublish "Assets"
    if (-not (Test-Path $targetAssets)) { New-Item -ItemType Directory -Path $targetAssets -Force | Out-Null }
    Remove-Item -Path (Join-Path $targetAssets "Assets") -Recurse -Force -ErrorAction SilentlyContinue
    Copy-Item -Path "$assetsDir\*" -Destination $targetAssets -Recurse -Force

    # Ensure WinMD and native Extension proxy DLL are present for cross-process WinRT marshaling
    $cmdPalAppxDir = "C:\Program Files\WindowsApps\Microsoft.CommandPalette_0.11.11762.0_x64__8wekyb3d8bbwe"
    if (Test-Path "$cmdPalAppxDir\Microsoft.CommandPalette.Extensions.winmd") {
        try { Copy-Item -Path "$cmdPalAppxDir\Microsoft.CommandPalette.Extensions.winmd" -Destination $cmdPalPublish -Force -ErrorAction Stop } catch { }
    }
    if (Test-Path "$cmdPalAppxDir\Microsoft.CommandPalette.Extensions.dll") {
        try { Copy-Item -Path "$cmdPalAppxDir\Microsoft.CommandPalette.Extensions.dll" -Destination $cmdPalPublish -Force -ErrorAction Stop } catch { }
    }

    if ($Deploy) {
        # Clean up legacy V1 plugin folder if present
        $v1TargetDir = "$env:LOCALAPPDATA\Microsoft\PowerToys\PowerToys Run\Plugins\ClipHistory"
        if (Test-Path $v1TargetDir) {
            Remove-Item -Path $v1TargetDir -Recurse -Force -ErrorAction SilentlyContinue
        }

        # Unregister existing / older packages first to avoid 0x80073CFB re-registration conflict
        $packageFilters = @("*ClipHistory.CommandPalette*", "*SlinClip.CommandPalette*")
        foreach ($pkgFilter in $packageFilters) {
            $pkgs = Get-AppxPackage $pkgFilter -ErrorAction SilentlyContinue
            foreach ($pkg in $pkgs) {
                Write-Host "Unregistering existing package: $($pkg.PackageFullName)" -ForegroundColor Yellow
                Remove-AppxPackage -Package $pkg.PackageFullName -ErrorAction SilentlyContinue
            }
        }

        # Grant read permissions to ALL APPLICATION PACKAGES for AppContainer access
        icacls $cmdPalPublish /grant "*S-1-15-2-1:(OI)(CI)RX" /T /Q *>$null

        Write-Host "Registering AppxPackage: $manifestPath" -ForegroundColor Cyan
        Add-AppxPackage -Register $manifestPath -ForceApplicationShutdown

        $settingsPath = "$env:LOCALAPPDATA\Packages\Microsoft.CommandPalette_8wekyb3d8bbwe\LocalState\settings.json"
        if (Test-Path $settingsPath) {
            $json = Get-Content -Raw -Encoding UTF8 -Path $settingsPath | ConvertFrom-Json
            $currentPkg = Get-AppxPackage "*ClipHistory.CommandPalette*" -ErrorAction SilentlyContinue
            if ($currentPkg) {
                $extKey = "$($currentPkg.PackageFamilyName)!App!ClipHistoryCmdPalExtension"
                if (-not $json.ProviderSettings.PSObject.Properties[$extKey]) {
                    $provObj = [PSCustomObject]@{
                        IsEnabled = $true
                        FallbackCommands = [PSCustomObject]@{}
                        PinnedCommandIds = @()
                    }
                    $json.ProviderSettings | Add-Member -MemberType NoteProperty -Name $extKey -Value $provObj
                }
            }

            if (-not $json.Aliases.PSObject.Properties["ch"]) {
                $chObj = [PSCustomObject]@{
                    CommandId = "ClipHistoryPage"
                    Alias = "ch"
                    IsDirect = $true
                }
                $json.Aliases | Add-Member -MemberType NoteProperty -Name "ch" -Value $chObj
            }
            if (-not $json.Aliases.PSObject.Properties["clip"]) {
                $clipObj = [PSCustomObject]@{
                    CommandId = "ClipHistoryPage"
                    Alias = "clip"
                    IsDirect = $true
                }
                $json.Aliases | Add-Member -MemberType NoteProperty -Name "clip" -Value $clipObj
            }
            if (-not $json.Aliases.PSObject.Properties["sc"]) {
                $scObj = [PSCustomObject]@{
                    CommandId = "ClipHistoryPage"
                    Alias = "sc"
                    IsDirect = $true
                }
                $json.Aliases | Add-Member -MemberType NoteProperty -Name "sc" -Value $scObj
            }
            if (-not $json.Aliases.PSObject.Properties["ch-set"]) {
                $chsetObj = [PSCustomObject]@{
                    CommandId = "ClipHistorySettingsPage"
                    Alias = "ch-set"
                    IsDirect = $true
                }
                $json.Aliases | Add-Member -MemberType NoteProperty -Name "ch-set" -Value $chsetObj
            }
            if (-not $json.Aliases.PSObject.Properties["scs"]) {
                $scsObj = [PSCustomObject]@{
                    CommandId = "ClipHistorySettingsPage"
                    Alias = "scs"
                    IsDirect = $true
                }
                $json.Aliases | Add-Member -MemberType NoteProperty -Name "scs" -Value $scsObj
            }
            $newSettingsJson = ConvertTo-Json $json -Depth 20
            [System.IO.File]::WriteAllText($settingsPath, $newSettingsJson, [System.Text.Encoding]::UTF8)
        }

        Write-Host "CmdPal Extension registered and configured successfully!" -ForegroundColor Green

        if ($Restart) {
            Write-Host "Restarting PowerToys to host CmdPal V2..." -ForegroundColor Yellow
            Stop-Process -Name "PowerToys*", "ClipHistoryCmdPal*", "SlinClip*" -Force -ErrorAction SilentlyContinue
            Start-Sleep -Milliseconds 600
            $ptExe = "C:\Users\USER\scoop\apps\powertoys\current\PowerToys.exe"
            if (Test-Path $ptExe) {
                Start-Process $ptExe
                Write-Host "PowerToys restarted with CmdPal V2!" -ForegroundColor Green
            }
        }
    }
}

Write-Host ""
Write-Host "All operations completed successfully!" -ForegroundColor Green
