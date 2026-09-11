# Drive the Linux build on the Mint VM over SSH from this Windows checkout.
#
#   tools\linux.ps1 sync            copy docs/, linux/, the shared C# projects and VERSION to the VM
#   tools\linux.ps1 test            sync, then dotnet test (the test csproj, never the solution - see below)
#   tools\linux.ps1 build           sync, then dotnet publish the Avalonia app for linux-x64
#   tools\linux.ps1 run             build, quit any running copy, launch it on the VM's display
#   tools\linux.ps1 shot [name]     capture the VM's screen and copy it back to tools\shots\
#   tools\linux.ps1 ssh <command>   run one command on the VM
#   tools\linux.ps1 vm <up|down>    start or save the VM
#
# Host, user, key, port and remote directory come from tools\linux.local.ps1 (ignored by git).
#
# Two things that bite here. `dotnet test windows/Tokendial.slnx` cannot run on Linux: that solution
# includes the WPF app, which targets net10.0-windows. The test project itself targets net10.0 and
# references only Core, so it runs anywhere - always name the csproj. And a command sent through `ssh`
# loses its quotes and expands nothing, so anything with a variable or a `~` goes through Invoke-LinuxScript.

param(
    [Parameter(Position = 0)][ValidateSet("sync", "test", "build", "run", "shot", "ssh", "quit", "vm")][string]$Verb = "sync",
    [Parameter(Position = 1, ValueFromRemainingArguments = $true)][string[]]$Rest
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
. (Join-Path $root "tools\linux.local.ps1")
$VBoxManage = "C:\Program Files\Oracle\VirtualBox\VBoxManage.exe"
# A non-interactive ssh session reads neither .bashrc nor .profile, so the toolchain has to be
# named on every command - the same reason tools\mac.ps1 carries $remotePath.
$remotePath = 'export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$DOTNET_ROOT:$HOME/.local/bin:$PATH"; '

function Invoke-Linux([string]$Command) {
    & ssh -o BatchMode=yes -o ConnectTimeout=8 -p $LinuxPort -i $LinuxKey "$LinuxUser@$LinuxHost" ($remotePath + $Command)
    if ($LASTEXITCODE -ne 0) { throw "ssh exited with $LASTEXITCODE" }
}

# The only safe way to send anything with quotes, variables or a tilde.
#
# The script is copied rather than piped. PowerShell has no `<` redirection, and piping a string into a
# native process re-encodes it: 5.1 emitted a BOM, which bash read as part of the first word, and appended
# a CRLF, which reached the last command as a literal carriage return. scp moves bytes and invents nothing.
function Invoke-LinuxScript([string]$Script) {
    $file = Join-Path $env:TEMP "tokendial-linux.sh"
    $body = ($remotePath.TrimEnd() + "`n" + $Script).Replace("`r`n", "`n")
    [IO.File]::WriteAllBytes($file, (New-Object Text.UTF8Encoding $false).GetBytes($body))
    & scp -q -o BatchMode=yes -P $LinuxPort -i $LinuxKey $file "${LinuxUser}@${LinuxHost}:/tmp/tokendial-run.sh"
    if ($LASTEXITCODE -ne 0) { throw "scp failed" }
    & ssh -o BatchMode=yes -o ConnectTimeout=8 -p $LinuxPort -i $LinuxKey "$LinuxUser@$LinuxHost" "bash /tmp/tokendial-run.sh"
    if ($LASTEXITCODE -ne 0) { throw "ssh exited with $LASTEXITCODE" }
}

function Sync-Linux {
    $bundle = Join-Path $env:TEMP "tokendial-linux-sync.tgz"
    if (Test-Path $bundle) { Remove-Item $bundle -Force }
    Push-Location $root
    try {
        # linux/ only exists once the Avalonia project does; until then the core and its tests are
        # still worth syncing, because they are what proves the toolchain on the VM.
        $paths = @("docs", "windows/Tokendial.Core", "windows/Tokendial.Tests", "VERSION")
        if (Test-Path (Join-Path $root "linux")) { $paths += "linux" }
        & tar -czf $bundle --exclude "bin" --exclude "obj" --exclude "linux/dist" --exclude "linux/publish" $paths
        if ($LASTEXITCODE -ne 0) { throw "tar failed" }
    } finally { Pop-Location }
    Invoke-Linux "mkdir -p $LinuxDir"
    & scp -q -o BatchMode=yes -P $LinuxPort -i $LinuxKey $bundle "${LinuxUser}@${LinuxHost}:$LinuxDir/sync.tgz"
    if ($LASTEXITCODE -ne 0) { throw "scp failed" }
    Invoke-Linux "cd $LinuxDir && rm -rf docs linux windows && tar -xzf sync.tgz && rm sync.tgz"
    Write-Host "synced to $LinuxUser@${LinuxHost}:$LinuxDir"
}

function Test-Linux {
    Sync-Linux
    Invoke-LinuxScript @"
cd "$LinuxDir"
dotnet test windows/Tokendial.Tests/Tokendial.Tests.csproj --configuration Release 2>&1 | tail -n 25
"@
}

function Build-Linux {
    Sync-Linux
    Invoke-LinuxScript @"
cd "$LinuxDir"
if [ ! -d linux/Tokendial.Avalonia ]; then echo "no Avalonia project yet - nothing to build"; exit 0; fi
dotnet publish linux/Tokendial.Avalonia -c Release -r linux-x64 --self-contained true -o linux/publish 2>&1 | tail -n 15
"@
}

function Quit-Linux {
    Invoke-Linux "pkill -x Tokendial 2>/dev/null; true"
}

function Run-Linux {
    Build-Linux
    Quit-Linux
    # An SSH session has no display of its own; the app has to be told which one to draw on.
    Invoke-LinuxScript @"
cd "$LinuxDir"
if [ ! -x linux/publish/Tokendial ]; then echo "nothing published to run"; exit 1; fi
DISPLAY=:0 setsid linux/publish/Tokendial >/tmp/tokendial.log 2>&1 &
sleep 3
pgrep -x Tokendial >/dev/null && echo running || { echo "did not start:"; tail -n 20 /tmp/tokendial.log; exit 1; }
"@
}

# Taken from the host, not the guest: VirtualBox can photograph the framebuffer with nothing installed
# inside the VM, which also means it works before the desktop is up and when the app has wedged.
function Shot-Linux([string]$Name) {
    if (-not $Name) { $Name = "linux-" + (Get-Date -Format "HHmmss") }
    $local = Join-Path $root "tools\shots"
    New-Item -ItemType Directory -Force $local | Out-Null
    $out = Join-Path $local "$Name.png"
    & $VBoxManage controlvm $LinuxVm screenshotpng $out
    if ($LASTEXITCODE -ne 0) { throw "screenshotpng failed - is the VM running?" }
    Write-Host $out
}

function Vm-Linux([string]$Action) {
    switch ($Action) {
        "up"   { & $VBoxManage startvm $LinuxVm --type gui }
        "down" { & $VBoxManage controlvm $LinuxVm savestate }
        default { Write-Host "vm up | vm down" }
    }
}

switch ($Verb) {
    "sync"  { Sync-Linux }
    "test"  { Test-Linux }
    "build" { Build-Linux }
    "run"   { Run-Linux }
    "quit"  { Quit-Linux }
    "shot"  { Shot-Linux ($Rest -join "") }
    "ssh"   { Invoke-Linux ($Rest -join " ") }
    "vm"    { Vm-Linux ($Rest -join "") }
}
