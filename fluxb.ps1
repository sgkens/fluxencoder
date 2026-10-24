#!/usr/bin/env pwsh
#Requires -Version 7.2

<#
.SYNOPSIS
    FluxEncoder developer build, test, audit, format, and publish runner.

.DESCRIPTION
    Executes the FluxEncoder development workflow from the repository root or via
    dot-sourcing (exposing Invoke-FluxBuild with alias 'fluxb').

    Supported tasks:
      All      Restore, Build, and Test in sequence (default)
      Clean    Remove bin, obj, and artifacts directories
      Restore  Restore NuGet packages (locked mode when -Ci is set)
      Format   Verify or apply C# formatting with dotnet format
      Build    Compile fluxencoder.sln with warnings treated as errors
      Test     Run xUnit v3 test suites via Microsoft.Testing.Platform
      Audit    Scan direct and transitive NuGet packages for vulnerabilities
      Publish  Publish FluxEncoder.Cli for the target runtime identifier
      Run      Build and invoke FluxEncoder.Cli with pass-through arguments
      Doctor   Inspect local developer toolchain readiness

.PARAMETER Task
    One or more workflow tasks to execute. Defaults to 'All'.

.PARAMETER Configuration
    MSBuild configuration ('Release' or 'Debug'). Defaults to 'Release'.

.PARAMETER Project
    Test project scope when running 'Test': 'All', 'Core', 'Infrastructure', or 'Cli'.
    Defaults to 'All'.

.PARAMETER Filter
    Optional xUnit v3 / Microsoft.Testing.Platform filter expression for 'Test'.

.PARAMETER Runtime
    Target Runtime Identifier (RID) for 'Publish', e.g. 'linux-x64', 'win-x64', 'osx-arm64'.
    Defaults to the current host RID.

.PARAMETER Version
    Explicit SemVer version override passed as -p:Version=<Version>.

.PARAMETER AutoVersion
    Calculate the version from Conventional Commits in Git history.

.PARAMETER Hardware
    Set FLUXENCODER_HARDWARE ('amd', 'nvidia', 'intel', 'apple') during test execution.

.PARAMETER Ci
    Enable ContinuousIntegrationBuild=true and RestoreLockedMode=true.

.PARAMETER Fix
    Apply formatting changes during the 'Format' task instead of verifying only.

.PARAMETER SelfContained
    Publish a self-contained executable during the 'Publish' task.

.PARAMETER SingleFile
    Publish a single-file bundle during the 'Publish' task.

.PARAMETER Aot
    Publish with NativeAOT compilation (-p:PublishAot=true) during the 'Publish' task.

.PARAMETER NoBuild
    Skip implicit build before 'Test' or 'Run' when outputs are already compiled.

.PARAMETER NoColor
    Disable ANSI SGR colour sequences. Also honours NO_COLOR and TERM=dumb.

.PARAMETER PassThruArgs
    Arguments forwarded to FluxEncoder.Cli when running the 'Run' task.

.EXAMPLE
    ./fluxb.ps1
    Runs Restore, Build (Release), and Test across the solution.

.EXAMPLE
    ./fluxb.ps1 Test -Project Cli -Configuration Debug
    Builds and runs only the CLI test project in Debug configuration.

.EXAMPLE
    ./fluxb.ps1 Publish -Runtime linux-x64 -SelfContained -SingleFile -AutoVersion
    Publishes a versioned self-contained single-file binary to artifacts/publish/linux-x64.

.EXAMPLE
    ./fluxb.ps1 Doctor
    Displays the status, version, and path of all build and test dependencies.

.EXAMPLE
    ./fluxb.ps1 Run -PassThruArgs @('encode', '--help')
    Builds and executes FluxEncoder.Cli with 'encode --help'.
#>

[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string[]]$Task = @('All'),

    [Parameter()]
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',

    [Parameter()]
    [ValidateSet('All', 'Core', 'Infrastructure', 'Cli')]
    [string]$Project = 'All',

    [Parameter()]
    [string]$Filter,

    [Parameter()]
    [string]$Runtime = [System.Runtime.InteropServices.RuntimeInformation]::RuntimeIdentifier,

    [Parameter()]
    [string]$Version,

    [Parameter()]
    [switch]$AutoVersion,

    [Parameter()]
    [ValidateSet('amd', 'nvidia', 'intel', 'apple')]
    [string]$Hardware,

    [Parameter()]
    [switch]$Ci,

    [Parameter()]
    [switch]$Fix,

    [Parameter()]
    [switch]$SelfContained,

    [Parameter()]
    [switch]$SingleFile,

    [Parameter()]
    [switch]$Aot,

    [Parameter()]
    [switch]$NoBuild,

    [Parameter()]
    [switch]$NoColor,

    [Parameter(ValueFromRemainingArguments)]
    [string[]]$PassThruArgs
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# =====================================================================
#  UI & PALETTE OBJECTS (Padding-before-ANSI rule enforced)
# =====================================================================

function Format-FluxText {
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

function New-FluxUiContext {
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

function Write-FluxBanner {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Context,
        [Parameter(Mandatory)][string]$Title,
        [Parameter()][string]$Subtitle
    )

    $rule = [string]::new([char]$Context.Symbols.HorizontalRule[0], 72)
    $headerText = if ([string]::IsNullOrWhiteSpace($Subtitle)) {
        "$($Context.Symbols.Running) $Title"
    }
    else {
        "$($Context.Symbols.Running) $Title  $($Context.Symbols.Arrow)  $Subtitle"
    }

    [System.Console]::WriteLine((Format-FluxText -Palette $Context.Palette -Text $rule -Sgr $Context.Palette.Muted))
    [System.Console]::WriteLine((Format-FluxText -Palette $Context.Palette -Text $headerText -Sgr $Context.Palette.Header))
    [System.Console]::WriteLine((Format-FluxText -Palette $Context.Palette -Text $rule -Sgr $Context.Palette.Muted))
}

function Write-FluxStep {
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

    # Pad plain strings BEFORE applying ANSI sequences to prevent alignment drift
    $paddedLabel = $Label.PadRight(18)
    $styledIcon = Format-FluxText -Palette $Context.Palette -Text $sym -Sgr $sgr
    $styledLabel = Format-FluxText -Palette $Context.Palette -Text $paddedLabel -Sgr $sgr
    $styledDetail = if ([string]::IsNullOrWhiteSpace($Detail)) {
        ''
    }
    else {
        '  ' + (Format-FluxText -Palette $Context.Palette -Text $Detail -Sgr $Context.Palette.Muted)
    }

    [System.Console]::WriteLine("  $styledIcon $styledLabel$styledDetail")
}

# =====================================================================
#  CONVENTIONAL COMMIT SEMVER CALCULATOR
# =====================================================================

function Get-FluxGitSemVer {
    [CmdletBinding()]
    [OutputType([string])]
    param([Parameter(Mandatory)][string]$RepoRoot)

    $git = Get-Command git -ErrorAction SilentlyContinue
    if ($null -eq $git) {
        return '0.1.0'
    }

    Push-Location -LiteralPath $RepoRoot
    try {
        & git rev-parse --verify HEAD 2>$null | Out-Null
        if ($LASTEXITCODE -ne 0) {
            return '0.1.0'
        }

        $semVerPattern = '^(?:v)?(?<major>0|[1-9]\d*)\.(?<minor>0|[1-9]\d*)\.(?<patch>0|[1-9]\d*)$'
        $describeArgs = @('describe', '--tags', '--abbrev=0', '--match', '[0-9]*.[0-9]*.[0-9]*', '--match', 'v[0-9]*.[0-9]*.[0-9]*')
        $lastTag = $null
        $tagMatch = $null

        while ($true) {
            $candidate = & git @describeArgs 2>$null
            if ($LASTEXITCODE -ne 0 -or -not $candidate) { break }
            $candidate = ([string]$candidate).Trim()
            $m = [System.Text.RegularExpressions.Regex]::Match($candidate, $semVerPattern, [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)
            if ($m.Success) {
                $lastTag = $candidate
                $tagMatch = $m
                break
            }
            $describeArgs += @('--exclude', $candidate)
        }

        [uint64]$major = 0
        [uint64]$minor = 1
        [uint64]$patch = 0
        $range = $null

        if ($null -ne $lastTag) {
            $major = [uint64]$tagMatch.Groups['major'].Value
            $minor = [uint64]$tagMatch.Groups['minor'].Value
            $patch = [uint64]$tagMatch.Groups['patch'].Value
            $range = "$lastTag..HEAD"
        }

        $logArgs = [System.Collections.Generic.List[string]]::new()
        $logArgs.Add('log')
        $logArgs.Add('--no-color')
        $logArgs.Add('--format=%s')
        if ($null -ne $range) {
            $logArgs.Add($range)
        }

        $subjects = @(& git @logArgs 2>$null)
        if ($LASTEXITCODE -ne 0 -or $null -eq $lastTag) {
            return "$major.$minor.$patch"
        }

        $subjectRegex = [System.Text.RegularExpressions.Regex]::new(
            '^(?<type>[A-Za-z][A-Za-z0-9-]*)(?:\([^()\r\n]+\))?(?<breaking>!)?: \S',
            [System.Text.RegularExpressions.RegexOptions]::CultureInvariant
        )

        [int]$bump = 0
        foreach ($line in $subjects) {
            if ([string]::IsNullOrWhiteSpace($line)) { continue }
            $sm = $subjectRegex.Match($line)
            if (-not $sm.Success) { continue }
            if ($sm.Groups['breaking'].Success) {
                $bump = 3
                break
            }
            $t = $sm.Groups['type'].Value.ToLowerInvariant()
            if ($t -eq 'feat' -and $bump -lt 2) { $bump = 2 }
            elseif (($t -eq 'fix' -or $t -eq 'perf') -and $bump -lt 1) { $bump = 1 }
        }

        switch ($bump) {
            3 { $major++; $minor = 0; $patch = 0 }
            2 { $minor++; $patch = 0 }
            1 { $patch++ }
        }

        return "$major.$minor.$patch"
    }
    finally {
        Pop-Location
    }
}

# =====================================================================
#  NATIVE PROCESS HELPER
# =====================================================================

function Invoke-FluxDotNet {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Context,
        [Parameter(Mandatory)][string]$StepName,
        [Parameter(Mandatory)][string[]]$Arguments,
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [Parameter()][hashtable]$EnvironmentOverrides
    )

    $displayCmd = 'dotnet ' + ($Arguments -join ' ')
    Write-FluxStep -Context $Context -State 'Run' -Label $StepName -Detail $displayCmd

    $previousEnv = @{}
    if ($null -ne $EnvironmentOverrides) {
        foreach ($key in $EnvironmentOverrides.Keys) {
            $previousEnv[$key] = [System.Environment]::GetEnvironmentVariable($key)
            [System.Environment]::SetEnvironmentVariable($key, $EnvironmentOverrides[$key])
        }
    }

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    Push-Location -LiteralPath $WorkingDirectory
    try {
        & dotnet @Arguments | Out-Host
        $exitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
        $sw.Stop()
        foreach ($key in $previousEnv.Keys) {
            [System.Environment]::SetEnvironmentVariable($key, $previousEnv[$key])
        }
    }

    $elapsed = ('{0:0.00} s' -f $sw.Elapsed.TotalSeconds)
    if ($exitCode -ne 0) {
        Write-FluxStep -Context $Context -State 'Fail' -Label $StepName -Detail "exit $exitCode ($elapsed)"
        throw "Step '$StepName' failed with exit code $exitCode."
    }

    Write-FluxStep -Context $Context -State 'Ok' -Label $StepName -Detail $elapsed
    return [PSCustomObject]@{
        Step    = $StepName
        Status  = 'Succeeded'
        Elapsed = $sw.Elapsed
        Command = $displayCmd
    }
}

# =====================================================================
#  DOCTOR / TOOLCHAIN INSPECTION
# =====================================================================

function Invoke-FluxDoctor {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Context,
        [Parameter(Mandatory)][string]$RepoRoot
    )

    Write-FluxBanner -Context $Context -Title 'FluxEncoder Toolchain Doctor' -Subtitle $RepoRoot

    $globalJsonPath = [System.IO.Path]::Combine($RepoRoot, 'global.json')
    $requiredSdk = '10.0.100'
    if ([System.IO.File]::Exists($globalJsonPath)) {
        $globalJson = [System.IO.File]::ReadAllText($globalJsonPath) | ConvertFrom-Json
        if ($globalJson.sdk.version) {
            $requiredSdk = [string]$globalJson.sdk.version
        }
    }

    $probes = @(
        @{ Name = 'dotnet';      Args = @('--version'); Required = $true;  Note = "SDK >= $requiredSdk (global.json)" }
        @{ Name = 'ffmpeg';      Args = @('-version');  Required = $true;  Note = 'Media encoding and hardware probe tests' }
        @{ Name = 'ffprobe';     Args = @('-version');  Required = $true;  Note = 'Stream and container probing' }
        @{ Name = 'sqlite3';     Args = @('--version'); Required = $false; Note = 'External concurrent writer tests' }
        @{ Name = 'git';         Args = @('--version'); Required = $false; Note = 'Source control and SemVer calculation' }
        @{ Name = 'clang';       Args = @('--version'); Required = $false; Note = 'Linux NativeAOT linker prerequisite' }
        @{ Name = 'secret-tool'; Args = @('--version'); Required = $false; Note = 'Linux libsecret credential store' }
    )

    $rows = [System.Collections.Generic.List[PSCustomObject]]::new()
    $missingRequired = 0

    foreach ($probe in $probes) {
        $cmd = Get-Command $probe.Name -ErrorAction SilentlyContinue
        if ($null -eq $cmd) {
            $state = if ($probe.Required) { $missingRequired++; 'MISSING' } else { 'OPTIONAL' }
            $rows.Add([PSCustomObject]@{
                Tool    = $probe.Name
                Status  = $state
                Version = '-'
                Path    = $probe.Note
            })
            continue
        }

        $verLine = try {
            $probeArgs = $probe.Args
            $out = & $cmd.Source @probeArgs 2>&1 | Select-Object -First 1
            ([string]$out).Trim()
        }
        catch {
            'detected'
        }

        if ($verLine.Length -gt 38) {
            $verLine = $verLine.Substring(0, 35) + '...'
        }

        $rows.Add([PSCustomObject]@{
            Tool    = $probe.Name
            Status  = 'READY'
            Version = $verLine
            Path    = $cmd.Source
        })
    }

    $toolWidth = 14
    $statusWidth = 10
    $verWidth = 40

    $header = ('  {0}{1}{2}{3}' -f 'TOOL'.PadRight($toolWidth), 'STATUS'.PadRight($statusWidth), 'VERSION'.PadRight($verWidth), 'PATH / NOTE')
    [System.Console]::WriteLine((Format-FluxText -Palette $Context.Palette -Text $header -Sgr $Context.Palette.Muted))

    foreach ($row in $rows) {
        $cTool = $row.Tool.PadRight($toolWidth)
        $cStatus = $row.Status.PadRight($statusWidth)
        $cVer = $row.Version.PadRight($verWidth)

        $statusSgr = switch ($row.Status) {
            'READY'    { $Context.Palette.Success }
            'OPTIONAL' { $Context.Palette.Warning }
            default    { $Context.Palette.Failure }
        }

        $line = '  ' +
            (Format-FluxText -Palette $Context.Palette -Text $cTool -Sgr $Context.Palette.Bold) +
            (Format-FluxText -Palette $Context.Palette -Text $cStatus -Sgr $statusSgr) +
            (Format-FluxText -Palette $Context.Palette -Text $cVer -Sgr $Context.Palette.Muted) +
            $row.Path

        [System.Console]::WriteLine($line)
    }

    if ($missingRequired -gt 0) {
        [System.Console]::WriteLine('')
        Write-FluxStep -Context $Context -State 'Fail' -Label 'Doctor' -Detail "$missingRequired required tool(s) missing. Run ./bootstrap.ps1 to install dependencies."
        throw "$missingRequired required toolchain dependency(ies) missing."
    }

    [System.Console]::WriteLine('')
    Write-FluxStep -Context $Context -State 'Ok' -Label 'Doctor' -Detail 'All required build and runtime tools are available.'
}

# =====================================================================
#  MAIN BUILD ENTRYPOINT
# =====================================================================

function Invoke-FluxBuild {
    [CmdletBinding()]
    [Alias('fluxb')]
    param(
        [Parameter(Position = 0)]
        [string[]]$Task = @('All'),

        [Parameter()]
        [ValidateSet('Release', 'Debug')]
        [string]$Configuration = 'Release',

        [Parameter()]
        [ValidateSet('All', 'Core', 'Infrastructure', 'Cli')]
        [string]$Project = 'All',

        [Parameter()]
        [string]$Filter,

        [Parameter()]
        [string]$Runtime = [System.Runtime.InteropServices.RuntimeInformation]::RuntimeIdentifier,

        [Parameter()]
        [string]$Version,

        [Parameter()]
        [switch]$AutoVersion,

        [Parameter()]
        [ValidateSet('amd', 'nvidia', 'intel', 'apple')]
        [string]$Hardware,

        [Parameter()]
        [switch]$Ci,

        [Parameter()]
        [switch]$Fix,

        [Parameter()]
        [switch]$SelfContained,

        [Parameter()]
        [switch]$SingleFile,

        [Parameter()]
        [switch]$Aot,

        [Parameter()]
        [switch]$NoBuild,

        [Parameter()]
        [switch]$NoColor,

        [Parameter(ValueFromRemainingArguments)]
        [string[]]$PassThruArgs
    )

    $repoRoot = [System.IO.Path]::GetFullPath($PSScriptRoot)
    $slnPath = [System.IO.Path]::Combine($repoRoot, 'fluxencoder.sln')
    $cliProj = [System.IO.Path]::Combine($repoRoot, 'src', 'FluxEncoder.Cli', 'FluxEncoder.Cli.csproj')

    if (-not [System.IO.File]::Exists($slnPath)) {
        throw "Solution file not found at '$slnPath'."
    }

    $ctx = New-FluxUiContext -DisableColor:$NoColor

    $validTasks = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($v in @('All', 'Clean', 'Restore', 'Format', 'Build', 'Test', 'Audit', 'Publish', 'Run', 'Doctor')) {
        [void]$validTasks.Add($v)
    }

    # Support both array arguments and comma-joined strings from pwsh -File
    $expandedTasks = [System.Collections.Generic.List[string]]::new()
    foreach ($rawItem in $Task) {
        foreach ($item in ($rawItem -split ',')) {
            $trimmed = $item.Trim()
            if ([string]::IsNullOrEmpty($trimmed)) { continue }
            if (-not $validTasks.Contains($trimmed)) {
                throw "Invalid task '$trimmed'. Valid tasks: All, Clean, Restore, Format, Build, Test, Audit, Publish, Run, Doctor."
            }
            if ($trimmed -ieq 'All') {
                $expandedTasks.Add('Restore')
                $expandedTasks.Add('Build')
                $expandedTasks.Add('Test')
            }
            else {
                $expandedTasks.Add($trimmed)
            }
        }
    }

    $effectiveVersion = if (-not [string]::IsNullOrWhiteSpace($Version)) {
        $Version
    }
    elseif ($AutoVersion.IsPresent) {
        Get-FluxGitSemVer -RepoRoot $repoRoot
    }
    else {
        $null
    }

    $subtitleParts = [System.Collections.Generic.List[string]]::new()
    $subtitleParts.Add("config=$Configuration")
    if ($null -ne $effectiveVersion) { $subtitleParts.Add("version=$effectiveVersion") }
    if ($Ci.IsPresent) { $subtitleParts.Add('mode=CI') }
    Write-FluxBanner -Context $ctx -Title ('FluxEncoder Build (' + ($expandedTasks -join ' -> ') + ')') -Subtitle ($subtitleParts -join '  ')

    $results = [System.Collections.Generic.List[PSCustomObject]]::new()
    $totalWatch = [System.Diagnostics.Stopwatch]::StartNew()

    foreach ($currentTask in $expandedTasks) {
        switch ($currentTask) {
            'Doctor' {
                $sw = [System.Diagnostics.Stopwatch]::StartNew()
                Invoke-FluxDoctor -Context $ctx -RepoRoot $repoRoot
                $sw.Stop()
                $results.Add([PSCustomObject]@{ Step = 'Doctor'; Status = 'Succeeded'; Elapsed = $sw.Elapsed; Command = 'toolchain check' })
            }

            'Clean' {
                $sw = [System.Diagnostics.Stopwatch]::StartNew()
                Write-FluxStep -Context $ctx -State 'Run' -Label 'Clean' -Detail 'Removing bin/, obj/, artifacts/'
                $dirsToClean = [System.Collections.Generic.List[string]]::new()
                foreach ($sub in @('src', 'tests')) {
                    $baseDir = [System.IO.Path]::Combine($repoRoot, $sub)
                    if ([System.IO.Directory]::Exists($baseDir)) {
                        foreach ($projDir in [System.IO.Directory]::GetDirectories($baseDir)) {
                            foreach ($leaf in @('bin', 'obj')) {
                                $candidate = [System.IO.Path]::Combine($projDir, $leaf)
                                if ([System.IO.Directory]::Exists($candidate)) {
                                    $dirsToClean.Add($candidate)
                                }
                            }
                        }
                    }
                }
                $artifactsDir = [System.IO.Path]::Combine($repoRoot, 'artifacts')
                if ([System.IO.Directory]::Exists($artifactsDir)) {
                    $dirsToClean.Add($artifactsDir)
                }

                foreach ($dir in $dirsToClean) {
                    [System.IO.Directory]::Delete($dir, $true)
                }
                $sw.Stop()
                Write-FluxStep -Context $ctx -State 'Ok' -Label 'Clean' -Detail ("Removed $($dirsToClean.Count) directories ({0:0.00} s)" -f $sw.Elapsed.TotalSeconds)
                $results.Add([PSCustomObject]@{ Step = 'Clean'; Status = 'Succeeded'; Elapsed = $sw.Elapsed; Command = "clean ($($dirsToClean.Count) dirs)" })
            }

            'Restore' {
                $restoreArgs = [System.Collections.Generic.List[string]]::new()
                $restoreArgs.Add('restore')
                $restoreArgs.Add($slnPath)
                if ($Ci.IsPresent) {
                    $restoreArgs.Add('-p:ContinuousIntegrationBuild=true')
                    $restoreArgs.Add('--locked-mode')
                }
                $res = Invoke-FluxDotNet -Context $ctx -StepName 'Restore' -Arguments $restoreArgs.ToArray() -WorkingDirectory $repoRoot
                $results.Add($res)
            }

            'Format' {
                $formatArgs = [System.Collections.Generic.List[string]]::new()
                $formatArgs.Add('format')
                $formatArgs.Add($slnPath)
                if (-not $Fix.IsPresent) {
                    $formatArgs.Add('--verify-no-changes')
                }
                $res = Invoke-FluxDotNet -Context $ctx -StepName 'Format' -Arguments $formatArgs.ToArray() -WorkingDirectory $repoRoot
                $results.Add($res)
            }

            'Build' {
                $buildArgs = [System.Collections.Generic.List[string]]::new()
                $buildArgs.Add('build')
                $buildArgs.Add($slnPath)
                $buildArgs.Add('-c')
                $buildArgs.Add($Configuration)
                if ($expandedTasks.Contains('Restore')) {
                    $buildArgs.Add('--no-restore')
                }
                if ($Ci.IsPresent) {
                    $buildArgs.Add('-p:ContinuousIntegrationBuild=true')
                }
                if ($null -ne $effectiveVersion) {
                    $buildArgs.Add("-p:Version=$effectiveVersion")
                }
                $res = Invoke-FluxDotNet -Context $ctx -StepName 'Build' -Arguments $buildArgs.ToArray() -WorkingDirectory $repoRoot
                $results.Add($res)
            }

            'Test' {
                $testArgs = [System.Collections.Generic.List[string]]::new()
                $testArgs.Add('test')

                if ($Project -ieq 'All') {
                    $testArgs.Add('--solution')
                    $testArgs.Add($slnPath)
                }
                else {
                    $projFile = [System.IO.Path]::Combine($repoRoot, 'tests', "FluxEncoder.$Project.Tests", "FluxEncoder.$Project.Tests.csproj")
                    $testArgs.Add('--project')
                    $testArgs.Add($projFile)
                }

                $testArgs.Add('-c')
                $testArgs.Add($Configuration)

                if ($NoBuild.IsPresent -or $expandedTasks.Contains('Build')) {
                    $testArgs.Add('--no-build')
                }

                if (-not [string]::IsNullOrWhiteSpace($Filter)) {
                    $testArgs.Add('--filter-query')
                    $testArgs.Add($Filter)
                }

                $envMap = @{}
                if (-not [string]::IsNullOrWhiteSpace($Hardware)) {
                    $envMap['FLUXENCODER_HARDWARE'] = $Hardware
                }

                $res = Invoke-FluxDotNet -Context $ctx -StepName "Test ($Project)" -Arguments $testArgs.ToArray() -WorkingDirectory $repoRoot -EnvironmentOverrides $envMap
                $results.Add($res)
            }

            'Audit' {
                $auditArgs = @(
                    'list',
                    $slnPath,
                    'package',
                    '--vulnerable',
                    '--include-transitive'
                )
                $res = Invoke-FluxDotNet -Context $ctx -StepName 'Audit' -Arguments $auditArgs -WorkingDirectory $repoRoot
                $results.Add($res)
            }

            'Publish' {
                $outDir = [System.IO.Path]::Combine($repoRoot, 'artifacts', 'publish', $Runtime)
                $pubArgs = [System.Collections.Generic.List[string]]::new()
                $pubArgs.Add('publish')
                $pubArgs.Add($cliProj)
                $pubArgs.Add('-c')
                $pubArgs.Add($Configuration)
                $pubArgs.Add('-r')
                $pubArgs.Add($Runtime)
                $pubArgs.Add('-o')
                $pubArgs.Add($outDir)

                if ($Aot.IsPresent) {
                    $pubArgs.Add('-p:PublishAot=true')
                }
                else {
                    $scValue = if ($SelfContained.IsPresent -or $SingleFile.IsPresent) { 'true' } else { 'false' }
                    $pubArgs.Add("--self-contained=$scValue")
                    if ($SingleFile.IsPresent) {
                        $pubArgs.Add('-p:PublishSingleFile=true')
                    }
                }

                if ($null -ne $effectiveVersion) {
                    $pubArgs.Add("-p:Version=$effectiveVersion")
                }

                $res = Invoke-FluxDotNet -Context $ctx -StepName "Publish ($Runtime)" -Arguments $pubArgs.ToArray() -WorkingDirectory $repoRoot
                Write-FluxStep -Context $ctx -State 'Ok' -Label 'Artifact' -Detail $outDir
                $results.Add($res)
            }

            'Run' {
                $runArgs = [System.Collections.Generic.List[string]]::new()
                $runArgs.Add('run')
                $runArgs.Add('--project')
                $runArgs.Add($cliProj)
                $runArgs.Add('-c')
                $runArgs.Add($Configuration)
                if ($NoBuild.IsPresent -or $expandedTasks.Contains('Build')) {
                    $runArgs.Add('--no-build')
                }
                if ($null -ne $PassThruArgs -and $PassThruArgs.Count -gt 0) {
                    $runArgs.Add('--')
                    foreach ($arg in $PassThruArgs) {
                        $runArgs.Add($arg)
                    }
                }
                $res = Invoke-FluxDotNet -Context $ctx -StepName 'Run' -Arguments $runArgs.ToArray() -WorkingDirectory $repoRoot
                $results.Add($res)
            }
        }
    }

    $totalWatch.Stop()

    if ($results.Count -gt 1) {
        $rule = [string]::new([char]$ctx.Symbols.HorizontalRule[0], 72)
        [System.Console]::WriteLine((Format-FluxText -Palette $ctx.Palette -Text $rule -Sgr $ctx.Palette.Muted))
        foreach ($item in $results) {
            $stepCol = $item.Step.PadRight(22)
            $statusCol = $item.Status.PadRight(12)
            $timeCol = ('{0:0.00} s' -f $item.Elapsed.TotalSeconds).PadLeft(8)

            $line = '  ' +
                (Format-FluxText -Palette $ctx.Palette -Text $ctx.Symbols.Success -Sgr $ctx.Palette.Success) + ' ' +
                (Format-FluxText -Palette $ctx.Palette -Text $stepCol -Sgr $ctx.Palette.Bold) +
                (Format-FluxText -Palette $ctx.Palette -Text $statusCol -Sgr $ctx.Palette.Success) +
                (Format-FluxText -Palette $ctx.Palette -Text $timeCol -Sgr $ctx.Palette.Muted)
            [System.Console]::WriteLine($line)
        }
        [System.Console]::WriteLine((Format-FluxText -Palette $ctx.Palette -Text $rule -Sgr $ctx.Palette.Muted))
        $totalText = ('Total elapsed: {0:0.00} s' -f $totalWatch.Elapsed.TotalSeconds)
        [System.Console]::WriteLine('  ' + (Format-FluxText -Palette $ctx.Palette -Text $totalText -Sgr $ctx.Palette.Accent))
    }
}

# Execute immediately when invoked directly (not dot-sourced)
if ($MyInvocation.InvocationName -ne '.') {
    Invoke-FluxBuild @PSBoundParameters
}
