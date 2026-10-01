# Drive the macOS build on the user's Mac over SSH from this Windows checkout.
#
#   tools\mac.ps1 sync            copy docs/ and macos/ to the Mac
#   tools\mac.ps1 test            sync, then `swift test` in macos/TokendialCore
#   tools\mac.ps1 build           sync, xcodegen, xcodebuild (Debug, ad-hoc signed)
#   tools\mac.ps1 run             build, quit any running copy, open the app
#   tools\mac.ps1 shot [name]     screencapture on the Mac and copy it back to tools\shots\
#   tools\mac.ps1 ssh <command>   run one command on the Mac
#
# Host, user, key and remote directory come from tools\mac.local.ps1 (ignored by git).

param(
    [Parameter(Position = 0)][ValidateSet("sync", "test", "build", "run", "shot", "ssh", "quit")][string]$Verb = "sync",
    [Parameter(Position = 1, ValueFromRemainingArguments = $true)][string[]]$Rest
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
. (Join-Path $root "tools\mac.local.ps1")
$remotePath = 'export PATH=/opt/homebrew/bin:/usr/local/bin:$PATH; '

function Invoke-Mac([string]$Command) {
    & ssh -o BatchMode=yes -o ConnectTimeout=8 -i $MacKey "$MacUser@$MacHost" ($remotePath + $Command)
    if ($LASTEXITCODE -ne 0) { throw "ssh exited with $LASTEXITCODE" }
}

function Sync-Mac {
    $bundle = Join-Path $env:TEMP "tokendial-sync.tgz"
    if (Test-Path $bundle) { Remove-Item $bundle -Force }
    Push-Location $root
    try {
        # Windows' own tar, named absolutely, for the reason linux.ps1 gives.
        $tar = Join-Path $env:SystemRoot "System32\tar.exe"
        if (-not (Test-Path $tar)) { throw "no tar at $tar" }
        & $tar -czf $bundle --exclude "macos/build" --exclude "macos/*.xcodeproj" --exclude ".build" --exclude "DerivedData" docs macos VERSION
        if ($LASTEXITCODE -ne 0) { throw "tar failed" }
    } finally { Pop-Location }
    Invoke-Mac "mkdir -p $MacDir"
    & scp -q -o BatchMode=yes -i $MacKey $bundle "$MacUser@${MacHost}:$MacDir/sync.tgz"
    if ($LASTEXITCODE -ne 0) { throw "scp failed" }
    # Extracting over macos/ kept every file deleted or renamed here, and SwiftPM and xcodegen compile
    # whatever the folder holds, so a test could pass or fail on code that no longer exists. The tree is
    # mirrored with --delete instead; the build caches are excluded so a sync does not force a cold build.
    Invoke-Mac ("cd $MacDir && rm -rf sync.new && mkdir sync.new && tar -xzf sync.tgz -C sync.new && rm sync.tgz" +
        " && rm -rf docs && mv sync.new/docs docs && mv -f sync.new/VERSION VERSION && mkdir -p macos" +
        " && rsync -a --delete --exclude build --exclude '*.xcodeproj' --exclude .build --exclude DerivedData sync.new/macos/ macos/" +
        " && rm -rf sync.new")
    Write-Host "synced docs/ and macos/ to $MacUser@${MacHost}:$MacDir"
}

function Test-Mac {
    Sync-Mac
    Invoke-Mac "cd $MacDir/macos/TokendialCore && swift test 2>&1 | tail -40"
}

function Build-Mac {
    Sync-Mac
    Invoke-Mac "chmod +x $MacDir/macos/tools/*.sh && $MacDir/macos/tools/build-gui.sh Debug"
}

function Quit-Mac {
    Invoke-Mac "pkill -x Tokendial 2>/dev/null; true"
}

function Run-Mac {
    Build-Mac
    Quit-Mac
    Invoke-Mac "open $MacDir/macos/build/Build/Products/Debug/Tokendial.app && sleep 3 && pgrep -x Tokendial >/dev/null && echo running"
}

function Shot-Mac([string]$Name) {
    if (-not $Name) { $Name = "shot-" + (Get-Date -Format "HHmmss") }
    $local = Join-Path $root "tools\shots"
    New-Item -ItemType Directory -Force $local | Out-Null
    Invoke-Mac "screencapture -x /tmp/$Name.png 2>/dev/null || (chmod +x $MacDir/macos/tools/shot-gui.sh && $MacDir/macos/tools/shot-gui.sh $Name)"
    & scp -q -o BatchMode=yes -i $MacKey "$MacUser@${MacHost}:/tmp/$Name.png" (Join-Path $local "$Name.png")
    if ($LASTEXITCODE -ne 0) { throw "scp failed" }
    Write-Host (Join-Path $local "$Name.png")
}

switch ($Verb) {
    "sync"  { Sync-Mac }
    "test"  { Test-Mac }
    "build" { Build-Mac }
    "run"   { Run-Mac }
    "quit"  { Quit-Mac }
    "shot"  { Shot-Mac ($Rest -join "") }
    "ssh"   { Invoke-Mac ($Rest -join " ") }
}
