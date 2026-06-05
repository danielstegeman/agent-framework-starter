<#
.SYNOPSIS
    Installs the Code-First Agent Starter skills + agent into GitHub Copilot and/or Claude Code.

.DESCRIPTION
    Each skill is a SKILL.md file under skills/<name>/. The agent is agents/code-first-agent.md.
    The installer symlinks (preferred) or copies these into the locations each tool reads from.

    Copilot (user scope):    %USERPROFILE%\.personalcopilot\skills\<name>\SKILL.md
                             %USERPROFILE%\.personalcopilot\agents\<name>.agent.md
    Claude (user scope):     %USERPROFILE%\.claude\skills\<name>\SKILL.md
                             %USERPROFILE%\.claude\agents\<name>.md
    Project scope:           <cwd>\.github\skills\<name>\ and <cwd>\.claude\skills\<name>\
                             <cwd>\.github\agents\ and <cwd>\.claude\agents\

.PARAMETER Tool
    copilot, claude, or both (default).

.PARAMETER Scope
    user (default) or project.

.PARAMETER Force
    Replace existing skills/agents with the same name.

.EXAMPLE
    ./install.ps1
    ./install.ps1 -Tool copilot
    ./install.ps1 -Scope project -Force
#>
[CmdletBinding()]
param(
    [ValidateSet('copilot', 'claude', 'both')]
    [string]$Tool = 'both',

    [ValidateSet('user', 'project')]
    [string]$Scope = 'user',

    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$skillsSrc = Join-Path $repoRoot 'skills'
$agentsSrc = Join-Path $repoRoot 'agents'

function Get-TargetRoots {
    param([string]$tool, [string]$scope)
    $base = if ($scope -eq 'user') { $env:USERPROFILE } else { (Get-Location).Path }
    switch ($tool) {
        'copilot' {
            if ($scope -eq 'user') {
                @{ Skills = "$base\.personalcopilot\skills"; Agents = "$base\.personalcopilot\agents"; AgentSuffix = '.agent.md' }
            }
            else {
                @{ Skills = "$base\.github\skills"; Agents = "$base\.github\agents"; AgentSuffix = '.agent.md' }
            }
        }
        'claude' {
            if ($scope -eq 'user') {
                @{ Skills = "$base\.claude\skills"; Agents = "$base\.claude\agents"; AgentSuffix = '.md' }
            }
            else {
                @{ Skills = "$base\.claude\skills"; Agents = "$base\.claude\agents"; AgentSuffix = '.md' }
            }
        }
    }
}

function Install-Link {
    param([string]$source, [string]$target, [switch]$force)
    if (Test-Path -LiteralPath $target) {
        if (-not $force) {
            Write-Host "  skip (exists): $target" -ForegroundColor DarkYellow
            return
        }
        Remove-Item -LiteralPath $target -Recurse -Force
    }
    $parent = Split-Path -Parent $target
    if (-not (Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Force -Path $parent | Out-Null
    }
    try {
        New-Item -ItemType SymbolicLink -Path $target -Value $source -ErrorAction Stop | Out-Null
        Write-Host "  linked: $target" -ForegroundColor Green
    }
    catch {
        # Fall back to copy (no symlink privilege on Windows)
        if (Test-Path -LiteralPath $source -PathType Container) {
            Copy-Item -LiteralPath $source -Destination $target -Recurse -Force
        }
        else {
            Copy-Item -LiteralPath $source -Destination $target -Force
        }
        Write-Host "  copied: $target" -ForegroundColor Cyan
    }
}

function Install-ForTool {
    param([string]$tool, [string]$scope)
    Write-Host "==> Installing for $tool ($scope scope)" -ForegroundColor White

    $targets = Get-TargetRoots -tool $tool -scope $scope

    # Skills
    Get-ChildItem -Path $skillsSrc -Directory | ForEach-Object {
        $name = $_.Name
        $skillFile = Join-Path $_.FullName 'SKILL.md'
        if (-not (Test-Path -LiteralPath $skillFile)) {
            Write-Warning "  $name has no SKILL.md, skipping"
            return
        }
        # Link the whole skill folder so bundled resources travel with it
        $targetDir = Join-Path $targets.Skills $name
        Install-Link -source $_.FullName -target $targetDir -force:$Force
    }

    # Agents
    if (Test-Path -LiteralPath $agentsSrc) {
        Get-ChildItem -Path $agentsSrc -File -Filter '*.md' | ForEach-Object {
            $baseName = [System.IO.Path]::GetFileNameWithoutExtension($_.Name) -replace '\.agent$', ''
            $targetFile = Join-Path $targets.Agents "$baseName$($targets.AgentSuffix)"
            Install-Link -source $_.FullName -target $targetFile -force:$Force
        }
    }
}

$tools = if ($Tool -eq 'both') { @('copilot', 'claude') } else { @($Tool) }
foreach ($t in $tools) {
    Install-ForTool -tool $t -scope $Scope
}

Write-Host "`nDone. Open a fresh chat to pick up the new skills/agents." -ForegroundColor Green
