#!/usr/bin/env pwsh
#Requires -Version 7.2

<#
.SYNOPSIS
    Cross-platform Windows and Linux bootstrap script for FluxEncoder development.

.DESCRIPTION
    Detects the host operating system and package manager, checks for required
    SDKs, native libraries, and media tools needed to build, test, and publish
    FluxEncoder, and installs any missing components.

    Components managed:
      1. .NET 10 SDK (>= 10.0.100 per global.json; package manager or dotnet-install fallback)
      2. FFmpeg & FFprobe (runtime media probing, encoding, and integration tests)
      3. NativeAOT & C/C++ linker libraries (clang, zlib, krb5 on Linux; VS C++ BuildTools on Windows)
      4. SQLite CLI (sqlite3 for history database inspection and concurrency tests)
      5. OS Secret Store CLI (libsecret-tools / secret-tool on Linux)
      6. JSON Schema validator (python3-jsonschema for Fluxfile schema tests)
      7. Solution NuGet restore (dotnet restore fluxencoder.sln)

.PARAMETER CheckOnly
    Inspect and report the current installation status and print the install commands
    that would run without modifying the machine.

.PARAMETER IncludeNativeAot
    Install NativeAOT C/C++ linker prerequisites (enabled by default on Linux;
    installs Visual Studio C++ Build Tools on Windows when specified).

.PARAMETER SkipDotNet
    Skip .NET 10 SDK installation.

.PARAMETER SkipFFmpeg
    Skip FFmpeg/FFprobe installation.

.PARAMETER SkipRestore
    Skip running 'dotnet restore fluxencoder.sln' after verifying dependencies.

.PARAMETER DotNetInstallDir
    Target directory when using the Microsoft dotnet-install script fallback.
    Defaults to ~/.dotnet on Linux and $env:LOCALAPPDATA\Microsoft\dotnet on Windows.

.PARAMETER NoColor
    Disable ANSI SGR colour sequences. Also honours NO_COLOR and TERM=dumb.

.EXAMPLE
    ./bootstrap.ps1 -CheckOnly
    Audits the current Windows or Linux machine and displays missing packages without installing.

.EXAMPLE
    ./bootstrap.ps1
    Installs all missing build, test, and runtime dependencies for the current OS and restores NuGet packages.

.EXAMPLE
    ./bootstrap.ps1 -IncludeNativeAot
    Installs all dependencies including NativeAOT linker toolchains (clang/zlib on Linux, VS C++ BuildTools on Windows).
#>

[CmdletBinding()]
param(
    [Parameter()]
    [Alias('DryRun')]
    [switch]$CheckOnly,

    [Parameter()]
    [switch]$IncludeNativeAot,

    [Parameter()]
    [switch]$SkipDotNet,

    [Parameter()]
    [switch]$SkipFFmpeg,

    [Parameter()]
    [switch]$SkipRestore,

    [Parameter()]
    [string]$DotNetInstallDir,

    [Parameter()]
    [switch]$NoColor
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# =====================================================================
#  UI & PALETTE OBJECTS (Padding-before-ANSI rule enforced)
# =====================================================================

function Format-BootstrapText {
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)]$Palette,
        [Parameter()][AllowEmptyString()][string]$Text,
        [Parameter()][AllowEmptyString()][string]$Sgr
    )

    if (-not $Palette.Enabled -or [string]::IsNullOrEmpty($Text) -or [string]::IsNullOrEmpty($Sgr)) {
        return $Text
    }
    return "$($Palette.Esc)[$($Sgr)m$Text$($Palette.Reset)"
}

function New-BootstrapUiContext {
    [CmdletBinding()]
    param([switch]$DisableColor)

    $term = [System.Environment]::GetEnvironmentVariable('TERM')
    $noColorEnv = [System.Environment]::GetEnvironmentVariable('NO_COLOR')
    $isDumb = [string]::Equals($term, 'dumb', [System.StringComparison]::OrdinalIgnoreCase)
    $isLinuxConsole = [string]::Equals($term, 'linux', [System.StringComparison]::OrdinalIgnoreCase)
    $colorEnabled = (-not $DisableColor.IsPresent) -and (-not $isDumb) -and [string]::IsNullOrEmpty($noColorEnv) -and (-not [System.Console]::IsOutputRedirected)

    $esc = [char]27
    $reset = "$esc[0m"

    $palette = [PSCustomObject]@{
        Enabled = $colorEnabled
        Esc     = $esc
        Reset   = $reset
        Accent  = '38;5;39'
        Success = '38;5;114'
        Warning = '38;5;214'
        Failure = '38;5;203'
        Muted   = '38;5;244'
        Header  = '1;38;5;39'
        Bold    = '1'
    }

    $asciiMode = $isDumb -or $isLinuxConsole
    $ui = [PSCustomObject]@{
        Running        = if ($asciiMode) { '>' } else { '▶' }
        Success        = if ($asciiMode) { '+' } else { '■' }
        Skipped        = if ($asciiMode) { '-' } else { '□' }
        Warning        = if ($asciiMode) { '!' } else { '◆' }
        Failure        = if ($asciiMode) { 'x' } else { '✕' }
        Arrow          = if ($asciiMode) { '->' } else { '→' }
        HorizontalRule = if ($asciiMode) { '-' } else { '─' }
    }

    return [PSCustomObject]@{
        Palette = $palette
        Symbols = $ui
    }
}

function Write-BootstrapBanner {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Context,
        [Parameter(Mandatory)][string]$Title,
        [Parameter()][string]$Subtitle
    )

    $rule = [string]::new([char]$Context.Symbols.HorizontalRule[0], 76)
    $headerText = if ([string]::IsNullOrWhiteSpace($Subtitle)) {
        "$($Context.Symbols.Running) $Title"
    }
    else {
        "$($Context.Symbols.Running) $Title  $($Context.Symbols.Arrow)  $Subtitle"
    }

    [System.Console]::WriteLine((Format-BootstrapText -Palette $Context.Palette -Text $rule -Sgr $Context.Palette.Muted))
    [System.Console]::WriteLine((Format-BootstrapText -Palette $Context.Palette -Text $headerText -Sgr $Context.Palette.Header))
    [System.Console]::WriteLine((Format-BootstrapText -Palette $Context.Palette -Text $rule -Sgr $Context.Palette.Muted))
}

function Write-BootstrapStatus {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Context,
        [Parameter(Mandatory)][ValidateSet('Run', 'Ok', 'Warn', 'Fail', 'Skip')][string]$State,
        [Parameter(Mandatory)][string]$Label,
        [Parameter()][string]$Detail
    )

    $sym = switch ($State) {
        'Run'  { $Context.Symbols.Running }
        'Ok'   { $Context.Symbols.Success }
        'Warn' { $Context.Symbols.Warning }
        'Fail' { $Context.Symbols.Failure }
        'Skip' { $Context.Symbols.Skipped }
    }
    $sgr = switch ($State) {
        'Run'  { $Context.Palette.Accent }
        'Ok'   { $Context.Palette.Success }
        'Warn' { $Context.Palette.Warning }
        'Fail' { $Context.Palette.Failure }
        'Skip' { $Context.Palette.Muted }
    }

    $paddedLabel = $Label.PadRight(22)
    $styledIcon = Format-BootstrapText -Palette $Context.Palette -Text $sym -Sgr $sgr
    $styledLabel = Format-BootstrapText -Palette $Context.Palette -Text $paddedLabel -Sgr $sgr
    $styledDetail = if ([string]::IsNullOrWhiteSpace($Detail)) {
        ''
    }
    else {
        '  ' + (Format-BootstrapText -Palette $Context.Palette -Text $Detail -Sgr $Context.Palette.Muted)
    }

    [System.Console]::WriteLine("  $styledIcon $styledLabel$styledDetail")
}

# =====================================================================
#  HOST & PACKAGE MANAGER DETECTION
# =====================================================================

function Get-HostEnvironmentInfo {
    [CmdletBinding()]
    param()

    $winOs = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)
    $linuxOs = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Linux)
    $macOs = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::OSX)

    $osName = if ($winOs) { 'Windows' } elseif ($linuxOs) { 'Linux' } elseif ($macOs) { 'macOS' } else { 'Unknown' }
    $arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()

    $pkgManager = $null
    if ($winOs) {
        foreach ($candidate in @('winget', 'scoop', 'choco')) {
            if ($null -ne (Get-Command $candidate -ErrorAction SilentlyContinue)) {
                $pkgManager = $candidate
                break
            }
        }
    }
    elseif ($linuxOs) {
        foreach ($candidate in @('apt-get', 'dnf', 'pacman', 'zypper', 'apk', 'brew')) {
            if ($null -ne (Get-Command $candidate -ErrorAction SilentlyContinue)) {
                $pkgManager = $candidate
                break
            }
        }
    }
    elseif ($macOs) {
        if ($null -ne (Get-Command 'brew' -ErrorAction SilentlyContinue)) {
            $pkgManager = 'brew'
        }
    }

    $isRoot = $false
    if (-not $winOs) {
        $idOut = try { (& id -u 2>$null) } catch { '1000' }
        $isRoot = ([string]$idOut).Trim() -eq '0'
    }

    $hasSudo = (-not $winOs) -and ($null -ne (Get-Command 'sudo' -ErrorAction SilentlyContinue))

    return [PSCustomObject]@{
        OS             = $osName
        IsWindows      = $winOs
        IsLinux        = $linuxOs
        IsMacOS        = $macOs
        Architecture   = $arch
        PackageManager = $pkgManager
        IsRoot         = $isRoot
        HasSudo        = $hasSudo
    }
}

# =====================================================================
#  SDK & TOOL PROBES
# =====================================================================

function Test-DotNetSdkInstalled {
    [CmdletBinding()]
    [OutputType([PSCustomObject])]
    param([Parameter(Mandatory)][string]$RequiredVersion)

    $dotnet = Get-Command 'dotnet' -ErrorAction SilentlyContinue
    if ($null -eq $dotnet) {
        return [PSCustomObject]@{ Installed = $false; Version = $null; Path = $null }
    }

    $reqParts = $RequiredVersion.Split('.')
    $reqMajor = [int]$reqParts[0]
    $sdks = @(& $dotnet.Source --list-sdks 2>$null)
    foreach ($line in $sdks) {
        if ($line -match '^(?<ver>\d+\.\d+\.\d+[^\s]*)\s+\[(?<dir>.+)\]') {
            $verText = $Matches['ver']
            $major = [int]($verText.Split('.')[0])
            if ($major -eq $reqMajor) {
                return [PSCustomObject]@{
                    Installed = $true
                    Version   = $verText
                    Path      = $dotnet.Source
                }
            }
        }
    }

    return [PSCustomObject]@{ Installed = $false; Version = $null; Path = $dotnet.Source }
}

function Test-LinuxHeaderInstalled {
    [CmdletBinding()]
    [OutputType([bool])]
    param([Parameter(Mandatory)][string]$HeaderName)

    $searchPaths = @(
        '/usr/include',
        '/usr/local/include',
        '/usr/include/x86_64-linux-gnu',
        '/usr/include/aarch64-linux-gnu'
    )
    foreach ($dir in $searchPaths) {
        if ([System.IO.File]::Exists([System.IO.Path]::Combine($dir, $HeaderName))) {
            return $true
        }
    }
    return $false
}

function Test-PythonJsonSchemaInstalled {
    [CmdletBinding()]
    [OutputType([bool])]
    param()

    $py = Get-Command 'python3' -ErrorAction SilentlyContinue
    if ($null -eq $py) {
        $py = Get-Command 'python' -ErrorAction SilentlyContinue
    }
    if ($null -eq $py) {
        return $false
    }

    & $py.Source -c 'import jsonschema' 2>$null | Out-Null
    return ($LASTEXITCODE -eq 0)
}

# =====================================================================
#  INSTALLATION HELPERS
# =====================================================================

function Invoke-PrivilegeCommand {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Context,
        [Parameter(Mandatory)]$HostInfo,
        [Parameter(Mandatory)][string]$Executable,
        [Parameter(Mandatory)][string[]]$Arguments,
        [Parameter()][switch]$DryRun
    )

    $useSudo = (-not $HostInfo.IsWindows) -and (-not $HostInfo.IsRoot) -and $HostInfo.HasSudo
    $cmdDisplay = if ($useSudo) {
        "sudo $Executable " + ($Arguments -join ' ')
    }
    else {
        "$Executable " + ($Arguments -join ' ')
    }

    if ($DryRun.IsPresent) {
        Write-BootstrapStatus -Context $Context -State 'Skip' -Label 'DryRun Command' -Detail $cmdDisplay
        return
    }

    Write-BootstrapStatus -Context $Context -State 'Run' -Label 'Executing' -Detail $cmdDisplay
    if ($useSudo) {
        & sudo $Executable @Arguments
    }
    else {
        & $Executable @Arguments
    }

    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $cmdDisplay"
    }
}

function Install-DotNetSdkViaScript {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Context,
        [Parameter(Mandatory)]$HostInfo,
        [Parameter(Mandatory)][string]$Channel,
        [Parameter()][string]$CustomInstallDir,
        [Parameter()][switch]$DryRun
    )

    $targetDir = if (-not [string]::IsNullOrWhiteSpace($CustomInstallDir)) {
        $CustomInstallDir
    }
    elseif ($HostInfo.IsWindows) {
        [System.IO.Path]::Combine([System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::LocalApplicationData), 'Microsoft', 'dotnet')
    }
    else {
        [System.IO.Path]::Combine([System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::UserProfile), '.dotnet')
    }

    if ($HostInfo.IsWindows) {
        $scriptUrl = 'https://dot.net/v1/dotnet-install.ps1'
        $display = "Invoke-WebRequest $scriptUrl -> dotnet-install.ps1 -Channel $Channel -InstallDir '$targetDir'"
        if ($DryRun.IsPresent) {
            Write-BootstrapStatus -Context $Context -State 'Skip' -Label '.NET SDK (script)' -Detail $display
            return
        }

        Write-BootstrapStatus -Context $Context -State 'Run' -Label '.NET SDK (script)' -Detail $display
        $tempScript = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), 'dotnet-install.ps1')
        Invoke-WebRequest -Uri $scriptUrl -OutFile $tempScript -UseBasicParsing
        & $tempScript -Channel $Channel -InstallDir $targetDir -NoPath:$false
    }
    else {
        $scriptUrl = 'https://dot.net/v1/dotnet-install.sh'
        $display = "curl -fsSL $scriptUrl | bash -s -- --channel $Channel --install-dir '$targetDir'"
        if ($DryRun.IsPresent) {
            Write-BootstrapStatus -Context $Context -State 'Skip' -Label '.NET SDK (script)' -Detail $display
            return
        }

        Write-BootstrapStatus -Context $Context -State 'Run' -Label '.NET SDK (script)' -Detail $display
        $tempScript = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), 'dotnet-install.sh')
        & curl -fsSL $scriptUrl -o $tempScript
        if ($LASTEXITCODE -ne 0) {
            throw 'Failed to download dotnet-install.sh.'
        }
        & bash $tempScript --channel $Channel --install-dir $targetDir
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet-install.sh failed with exit code $LASTEXITCODE."
        }
    }

    # Prepend to current process PATH so subsequent steps find dotnet immediately
    $pathSep = [System.IO.Path]::PathSeparator
    $currentPath = [System.Environment]::GetEnvironmentVariable('PATH') ?? ''
    if (-not ($currentPath.Split($pathSep) -contains $targetDir)) {
        [System.Environment]::SetEnvironmentVariable('PATH', "$targetDir$pathSep$currentPath")
    }
    [System.Environment]::SetEnvironmentVariable('DOTNET_ROOT', $targetDir)
    Write-BootstrapStatus -Context $Context -State 'Ok' -Label '.NET SDK Path' -Detail "DOTNET_ROOT=$targetDir"
}

function Install-LinuxPackages {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Context,
        [Parameter(Mandatory)]$HostInfo,
        [Parameter()][switch]$NeedFFmpeg,
        [Parameter()][switch]$NeedSqlite,
        [Parameter()][switch]$NeedSecretTool,
        [Parameter()][switch]$NeedNativeAot,
        [Parameter()][switch]$NeedJsonSchema,
        [Parameter()][switch]$DryRun
    )

    $pm = $HostInfo.PackageManager
    if ($null -eq $pm) {
        throw 'No supported Linux package manager (apt-get, dnf, pacman, zypper, apk, brew) was found on PATH.'
    }

    $packages = [System.Collections.Generic.List[string]]::new()

    switch ($pm) {
        'apt-get' {
            if ($NeedFFmpeg)     { $packages.Add('ffmpeg') }
            if ($NeedSqlite)     { $packages.Add('sqlite3'); $packages.Add('libsqlite3-dev') }
            if ($NeedSecretTool) { $packages.Add('libsecret-tools') }
            if ($NeedNativeAot)  { $packages.Add('clang'); $packages.Add('zlib1g-dev'); $packages.Add('libkrb5-dev'); $packages.Add('build-essential') }
            if ($NeedJsonSchema) { $packages.Add('python3'); $packages.Add('python3-jsonschema') }
            if ($packages.Count -gt 0) {
                Invoke-PrivilegeCommand -Context $Context -HostInfo $HostInfo -Executable 'apt-get' -Arguments @('update', '-qq') -DryRun:$DryRun
                $installArgs = [System.Collections.Generic.List[string]]::new()
                $installArgs.Add('install')
                $installArgs.Add('-y')
                $installArgs.Add('--no-install-recommends')
                foreach ($p in $packages) { $installArgs.Add($p) }
                Invoke-PrivilegeCommand -Context $Context -HostInfo $HostInfo -Executable 'apt-get' -Arguments $installArgs.ToArray() -DryRun:$DryRun
            }
        }

        'dnf' {
            if ($NeedFFmpeg)     { $packages.Add('ffmpeg') }
            if ($NeedSqlite)     { $packages.Add('sqlite'); $packages.Add('sqlite-devel') }
            if ($NeedSecretTool) { $packages.Add('libsecret') }
            if ($NeedNativeAot)  { $packages.Add('clang'); $packages.Add('zlib-devel'); $packages.Add('krb5-devel'); $packages.Add('gcc'); $packages.Add('make') }
            if ($NeedJsonSchema) { $packages.Add('python3'); $packages.Add('python3-jsonschema') }
            if ($packages.Count -gt 0) {
                $installArgs = [System.Collections.Generic.List[string]]::new()
                $installArgs.Add('install')
                $installArgs.Add('-y')
                foreach ($p in $packages) { $installArgs.Add($p) }
                Invoke-PrivilegeCommand -Context $Context -HostInfo $HostInfo -Executable 'dnf' -Arguments $installArgs.ToArray() -DryRun:$DryRun
            }
        }

        'pacman' {
            if ($NeedFFmpeg)     { $packages.Add('ffmpeg') }
            if ($NeedSqlite)     { $packages.Add('sqlite') }
            if ($NeedSecretTool) { $packages.Add('libsecret') }
            if ($NeedNativeAot)  { $packages.Add('clang'); $packages.Add('zlib'); $packages.Add('krb5'); $packages.Add('base-devel') }
            if ($NeedJsonSchema) { $packages.Add('python'); $packages.Add('python-jsonschema') }
            if ($packages.Count -gt 0) {
                $installArgs = [System.Collections.Generic.List[string]]::new()
                $installArgs.Add('-S')
                $installArgs.Add('--needed')
                $installArgs.Add('--noconfirm')
                foreach ($p in $packages) { $installArgs.Add($p) }
                Invoke-PrivilegeCommand -Context $Context -HostInfo $HostInfo -Executable 'pacman' -Arguments $installArgs.ToArray() -DryRun:$DryRun
            }
        }

        'zypper' {
            if ($NeedFFmpeg)     { $packages.Add('ffmpeg') }
            if ($NeedSqlite)     { $packages.Add('sqlite3'); $packages.Add('sqlite3-devel') }
            if ($NeedSecretTool) { $packages.Add('libsecret-tools') }
            if ($NeedNativeAot)  { $packages.Add('clang'); $packages.Add('zlib-devel'); $packages.Add('krb5-devel'); $packages.Add('gcc'); $packages.Add('make') }
            if ($NeedJsonSchema) { $packages.Add('python3'); $packages.Add('python3-jsonschema') }
            if ($packages.Count -gt 0) {
                $installArgs = [System.Collections.Generic.List[string]]::new()
                $installArgs.Add('--non-interactive')
                $installArgs.Add('install')
                foreach ($p in $packages) { $installArgs.Add($p) }
                Invoke-PrivilegeCommand -Context $Context -HostInfo $HostInfo -Executable 'zypper' -Arguments $installArgs.ToArray() -DryRun:$DryRun
            }
        }

        'apk' {
            if ($NeedFFmpeg)     { $packages.Add('ffmpeg') }
            if ($NeedSqlite)     { $packages.Add('sqlite'); $packages.Add('sqlite-dev') }
            if ($NeedSecretTool) { $packages.Add('libsecret-tools') }
            if ($NeedNativeAot)  { $packages.Add('clang'); $packages.Add('zlib-dev'); $packages.Add('krb5-dev'); $packages.Add('build-base'); $packages.Add('icu-libs') }
            if ($NeedJsonSchema) { $packages.Add('python3'); $packages.Add('py3-jsonschema') }
            if ($packages.Count -gt 0) {
                $installArgs = [System.Collections.Generic.List[string]]::new()
                $installArgs.Add('add')
                $installArgs.Add('--no-cache')
                foreach ($p in $packages) { $installArgs.Add($p) }
                Invoke-PrivilegeCommand -Context $Context -HostInfo $HostInfo -Executable 'apk' -Arguments $installArgs.ToArray() -DryRun:$DryRun
            }
        }

        'brew' {
            if ($NeedFFmpeg)     { $packages.Add('ffmpeg') }
            if ($NeedSqlite)     { $packages.Add('sqlite') }
            if ($NeedSecretTool) { $packages.Add('libsecret') }
            if ($NeedNativeAot)  { $packages.Add('llvm'); $packages.Add('zlib'); $packages.Add('krb5') }
            if ($NeedJsonSchema) { $packages.Add('python3') }
            if ($packages.Count -gt 0) {
                $installArgs = [System.Collections.Generic.List[string]]::new()
                $installArgs.Add('install')
                foreach ($p in $packages) { $installArgs.Add($p) }
                Invoke-PrivilegeCommand -Context $Context -HostInfo ($HostInfo | Select-Object *, @{Name = 'HasSudo'; Expression = { $false } } -ExcludeProperty HasSudo) -Executable 'brew' -Arguments $installArgs.ToArray() -DryRun:$DryRun
            }
        }
    }
}

function Install-WindowsPackages {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Context,
        [Parameter(Mandatory)]$HostInfo,
        [Parameter()][switch]$NeedDotNet,
        [Parameter()][string]$DotNetChannel,
        [Parameter()][string]$DotNetInstallDir,
        [Parameter()][switch]$NeedFFmpeg,
        [Parameter()][switch]$NeedSqlite,
        [Parameter()][switch]$NeedNativeAot,
        [Parameter()][switch]$DryRun
    )

    $pm = $HostInfo.PackageManager

    if ($NeedDotNet) {
        if ($pm -eq 'winget') {
            $wingetArgs = @('install', '--id', 'Microsoft.DotNet.SDK.10', '--exact', '--silent', '--accept-package-agreements', '--accept-source-agreements')
            Invoke-PrivilegeCommand -Context $Context -HostInfo $HostInfo -Executable 'winget' -Arguments $wingetArgs -DryRun:$DryRun
        }
        else {
            Install-DotNetSdkViaScript -Context $Context -HostInfo $HostInfo -Channel $DotNetChannel -CustomInstallDir $DotNetInstallDir -DryRun:$DryRun
        }
    }

    if ($NeedFFmpeg) {
        switch ($pm) {
            'winget' {
                $wingetArgs = @('install', '--id', 'Gyan.FFmpeg', '--exact', '--silent', '--accept-package-agreements', '--accept-source-agreements')
                Invoke-PrivilegeCommand -Context $Context -HostInfo $HostInfo -Executable 'winget' -Arguments $wingetArgs -DryRun:$DryRun
            }
            'scoop' {
                Invoke-PrivilegeCommand -Context $Context -HostInfo $HostInfo -Executable 'scoop' -Arguments @('install', 'ffmpeg') -DryRun:$DryRun
            }
            'choco' {
                Invoke-PrivilegeCommand -Context $Context -HostInfo $HostInfo -Executable 'choco' -Arguments @('install', 'ffmpeg', '-y') -DryRun:$DryRun
            }
            default {
                throw 'No Windows package manager (winget, scoop, choco) found to install FFmpeg. Install winget or add ffmpeg/ffprobe to PATH.'
            }
        }
    }

    if ($NeedSqlite) {
        switch ($pm) {
            'winget' {
                $wingetArgs = @('install', '--id', 'SQLite.SQLite', '--exact', '--silent', '--accept-package-agreements', '--accept-source-agreements')
                Invoke-PrivilegeCommand -Context $Context -HostInfo $HostInfo -Executable 'winget' -Arguments $wingetArgs -DryRun:$DryRun
            }
            'scoop' {
                Invoke-PrivilegeCommand -Context $Context -HostInfo $HostInfo -Executable 'scoop' -Arguments @('install', 'sqlite') -DryRun:$DryRun
            }
            'choco' {
                Invoke-PrivilegeCommand -Context $Context -HostInfo $HostInfo -Executable 'choco' -Arguments @('install', 'sqlite', '-y') -DryRun:$DryRun
            }
        }
    }

    if ($NeedNativeAot) {
        if ($pm -eq 'winget') {
            $vsArgs = @(
                'install',
                '--id', 'Microsoft.VisualStudio.2022.BuildTools',
                '--exact',
                '--silent',
                '--accept-package-agreements',
                '--accept-source-agreements',
                '--override', '--wait --passive --add Microsoft.VisualStudio.Workload.VCTools --includeRecommended'
            )
            Invoke-PrivilegeCommand -Context $Context -HostInfo $HostInfo -Executable 'winget' -Arguments $vsArgs -DryRun:$DryRun
        }
        else {
            Write-BootstrapStatus -Context $Context -State 'Warn' -Label 'NativeAOT (Windows)' -Detail 'Install Visual Studio 2022 Build Tools with Desktop development with C++ workload.'
        }
    }
}

# =====================================================================
#  MAIN BOOTSTRAP FLOW
# =====================================================================

function Invoke-FluxBootstrap {
    [CmdletBinding()]
    param()

    $repoRoot = [System.IO.Path]::GetFullPath($PSScriptRoot)
    $globalJsonPath = [System.IO.Path]::Combine($repoRoot, 'global.json')
    $slnPath = [System.IO.Path]::Combine($repoRoot, 'fluxencoder.sln')

    $requiredSdk = '10.0.100'
    $dotnetChannel = '10.0'
    if ([System.IO.File]::Exists($globalJsonPath)) {
        $globalJson = [System.IO.File]::ReadAllText($globalJsonPath) | ConvertFrom-Json
        if ($globalJson.sdk.version) {
            $requiredSdk = [string]$globalJson.sdk.version
            $parts = $requiredSdk.Split('.')
            if ($parts.Length -ge 2) {
                $dotnetChannel = "$($parts[0]).$($parts[1])"
            }
        }
    }

    $ctx = New-BootstrapUiContext -DisableColor:$NoColor
    $hostInfo = Get-HostEnvironmentInfo
    $modeLabel = if ($CheckOnly.IsPresent) { 'CHECK ONLY' } else { 'INSTALL' }
    $pmLabel = if ($null -ne $hostInfo.PackageManager) { $hostInfo.PackageManager } else { 'none' }

    Write-BootstrapBanner -Context $ctx -Title 'FluxEncoder Environment Bootstrap' -Subtitle "$($hostInfo.OS) $($hostInfo.Architecture) | pkg=$pmLabel | mode=$modeLabel"

    # 1. Inspect current state
    $sdkState = Test-DotNetSdkInstalled -RequiredVersion $requiredSdk
    $hasFFmpeg = $null -ne (Get-Command 'ffmpeg' -ErrorAction SilentlyContinue)
    $hasFFprobe = $null -ne (Get-Command 'ffprobe' -ErrorAction SilentlyContinue)
    $hasSqlite = $null -ne (Get-Command 'sqlite3' -ErrorAction SilentlyContinue)
    $hasSecretTool = (-not $hostInfo.IsLinux) -or ($null -ne (Get-Command 'secret-tool' -ErrorAction SilentlyContinue))
    $hasClang = (-not $hostInfo.IsLinux) -or ($null -ne (Get-Command 'clang' -ErrorAction SilentlyContinue))
    $hasZlibHeader = (-not $hostInfo.IsLinux) -or (Test-LinuxHeaderInstalled -HeaderName 'zlib.h')
    $hasJsonSchema = Test-PythonJsonSchemaInstalled

    $needDotNet = (-not $SkipDotNet.IsPresent) -and (-not $sdkState.Installed)
    $needFFmpeg = (-not $SkipFFmpeg.IsPresent) -and ((-not $hasFFmpeg) -or (-not $hasFFprobe))
    $needSqlite = -not $hasSqlite
    $needSecretTool = $hostInfo.IsLinux -and (-not $hasSecretTool)
    $wantNativeAot = $IncludeNativeAot.IsPresent -or $hostInfo.IsLinux
    $needNativeAot = $wantNativeAot -and ($hostInfo.IsWindows -or (-not $hasClang) -or (-not $hasZlibHeader))
    $needJsonSchema = $hostInfo.IsLinux -and (-not $hasJsonSchema)

    Write-BootstrapStatus -Context $ctx -State $(if ($sdkState.Installed) { 'Ok' } else { 'Warn' }) -Label ".NET SDK ($dotnetChannel)" -Detail $(if ($sdkState.Installed) { "v$($sdkState.Version) ($($sdkState.Path))" } else { "Missing (requires >= $requiredSdk)" })
    Write-BootstrapStatus -Context $ctx -State $(if ($hasFFmpeg -and $hasFFprobe) { 'Ok' } else { 'Warn' }) -Label 'FFmpeg + FFprobe' -Detail $(if ($hasFFmpeg -and $hasFFprobe) { (Get-Command 'ffmpeg').Source } else { 'Missing' })
    Write-BootstrapStatus -Context $ctx -State $(if ($hasSqlite) { 'Ok' } else { 'Warn' }) -Label 'SQLite CLI (sqlite3)' -Detail $(if ($hasSqlite) { (Get-Command 'sqlite3').Source } else { 'Missing (used for concurrency tests)' })

    if ($hostInfo.IsLinux) {
        Write-BootstrapStatus -Context $ctx -State $(if ($hasClang -and $hasZlibHeader) { 'Ok' } else { 'Warn' }) -Label 'NativeAOT Linker' -Detail $(if ($hasClang -and $hasZlibHeader) { 'clang + zlib.h present' } else { 'Missing clang or zlib1g-dev' })
        Write-BootstrapStatus -Context $ctx -State $(if ($hasSecretTool) { 'Ok' } else { 'Warn' }) -Label 'libsecret (secret-tool)' -Detail $(if ($hasSecretTool) { (Get-Command 'secret-tool').Source } else { 'Missing libsecret-tools' })
        Write-BootstrapStatus -Context $ctx -State $(if ($hasJsonSchema) { 'Ok' } else { 'Warn' }) -Label 'Python jsonschema' -Detail $(if ($hasJsonSchema) { 'python3 -m jsonschema ready' } else { 'Missing python3-jsonschema' })
    }

    # 2. Install missing components
    if ($hostInfo.IsLinux) {
        if ($needFFmpeg -or $needSqlite -or $needSecretTool -or $needNativeAot -or $needJsonSchema) {
            Install-LinuxPackages `
                -Context $ctx `
                -HostInfo $hostInfo `
                -NeedFFmpeg:$needFFmpeg `
                -NeedSqlite:$needSqlite `
                -NeedSecretTool:$needSecretTool `
                -NeedNativeAot:$needNativeAot `
                -NeedJsonSchema:$needJsonSchema `
                -DryRun:$CheckOnly
        }

        if ($needDotNet) {
            Install-DotNetSdkViaScript `
                -Context $ctx `
                -HostInfo $hostInfo `
                -Channel $dotnetChannel `
                -CustomInstallDir $DotNetInstallDir `
                -DryRun:$CheckOnly
        }
    }
    elseif ($hostInfo.IsWindows) {
        if ($needDotNet -or $needFFmpeg -or $needSqlite -or $needNativeAot) {
            Install-WindowsPackages `
                -Context $ctx `
                -HostInfo $hostInfo `
                -NeedDotNet:$needDotNet `
                -DotNetChannel $dotnetChannel `
                -DotNetInstallDir $DotNetInstallDir `
                -NeedFFmpeg:$needFFmpeg `
                -NeedSqlite:$needSqlite `
                -NeedNativeAot:$needNativeAot `
                -DryRun:$CheckOnly
        }
    }
    elseif ($hostInfo.IsMacOS) {
        if ($needFFmpeg -or $needSqlite -or $needNativeAot) {
            Install-LinuxPackages `
                -Context $ctx `
                -HostInfo $hostInfo `
                -NeedFFmpeg:$needFFmpeg `
                -NeedSqlite:$needSqlite `
                -NeedNativeAot:$needNativeAot `
                -DryRun:$CheckOnly
        }
        if ($needDotNet) {
            Install-DotNetSdkViaScript `
                -Context $ctx `
                -HostInfo $hostInfo `
                -Channel $dotnetChannel `
                -CustomInstallDir $DotNetInstallDir `
                -DryRun:$CheckOnly
        }
    }

    # 3. Restore NuGet packages when not in CheckOnly or SkipRestore mode
    if (-not $CheckOnly.IsPresent -and -not $SkipRestore.IsPresent -and [System.IO.File]::Exists($slnPath)) {
        $postSdk = Test-DotNetSdkInstalled -RequiredVersion $requiredSdk
        if ($postSdk.Installed) {
            Write-BootstrapStatus -Context $ctx -State 'Run' -Label 'NuGet Restore' -Detail "dotnet restore $slnPath"
            Push-Location -LiteralPath $repoRoot
            try {
                & dotnet restore $slnPath
                if ($LASTEXITCODE -ne 0) {
                    throw "dotnet restore failed with exit code $LASTEXITCODE."
                }
            }
            finally {
                Pop-Location
            }
            Write-BootstrapStatus -Context $ctx -State 'Ok' -Label 'NuGet Restore' -Detail 'Packages restored and verified.'
        }
    }

    $rule = [string]::new([char]$ctx.Symbols.HorizontalRule[0], 76)
    [System.Console]::WriteLine((Format-BootstrapText -Palette $ctx.Palette -Text $rule -Sgr $ctx.Palette.Muted))

    if ($CheckOnly.IsPresent) {
        $missingCount = @($needDotNet, $needFFmpeg, $needSqlite, $needSecretTool, $needNativeAot, $needJsonSchema | Where-Object { $_ }).Count
        if ($missingCount -eq 0) {
            Write-BootstrapStatus -Context $ctx -State 'Ok' -Label 'Bootstrap Audit' -Detail 'All dependencies are installed. Run ./fluxb.ps1 to build and test.'
        }
        else {
            Write-BootstrapStatus -Context $ctx -State 'Warn' -Label 'Bootstrap Audit' -Detail "$missingCount component group(s) missing. Run ./bootstrap.ps1 without -CheckOnly to install."
        }
    }
    else {
        Write-BootstrapStatus -Context $ctx -State 'Ok' -Label 'Bootstrap Complete' -Detail 'Environment ready. Run ./fluxb.ps1 to build and test FluxEncoder.'
    }
}

if ($MyInvocation.InvocationName -ne '.') {
    Invoke-FluxBootstrap
}
