# Zombie Empire Lite installer
# Installs NeoForge 21.1.215 (official installer) into versions\, downloads the mods from Modrinth
# into mods\, copies configs and the bossworld map, and creates the "Zombie Empire" launcher profile.
# Works with any launcher that uses a .minecraft folder and launcher_profiles.json.
param(
    [string]$MinecraftDir = "",
    [switch]$Yes            # no questions (for automated tests)
)
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor 3072  # TLS 1.2

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$files = Join-Path $here 'files'
$NeoForge = '21.1.215'
$VersionId = "neoforge-$NeoForge"
$InstallerUrl = "https://maven.neoforged.net/releases/net/neoforged/neoforge/$NeoForge/neoforge-$NeoForge-installer.jar"

function Step($text) { Write-Host ''; Write-Host "==> $text" -ForegroundColor Green }
function Info($text) { Write-Host "    $text" }
function Fail($text) { Write-Host ''; Write-Host "ERROR: $text" -ForegroundColor Red; exit 1 }
function Get-Sha1($path) { (Get-FileHash $path -Algorithm SHA1).Hash.ToLower() }

Write-Host '==========================================' -ForegroundColor Red
Write-Host '   ZOMBIE EMPIRE LITE - installer v1.1'     -ForegroundColor Red
Write-Host '   Minecraft 1.21.1 + NeoForge 21.1.215'    -ForegroundColor Red
Write-Host '==========================================' -ForegroundColor Red
Write-Host 'A legit copy of Minecraft: Java Edition is required.'

# ---------- 1. Minecraft folder ----------
Step 'Minecraft folder'
if (-not $MinecraftDir) {
    $default = Join-Path $env:APPDATA '.minecraft'
    if ($Yes) { $MinecraftDir = $default }
    else {
        $answer = Read-Host "    Install into $default ? Press Enter, or type another game folder"
        $MinecraftDir = if ($answer.Trim()) { $answer.Trim().Trim('"') } else { $default }
    }
}
New-Item -ItemType Directory -Force $MinecraftDir | Out-Null
$MinecraftDir = (Resolve-Path $MinecraftDir).Path
Info $MinecraftDir
$profiles = Join-Path $MinecraftDir 'launcher_profiles.json'
if (-not (Test-Path $profiles)) {
    Set-Content $profiles '{"profiles":{}}' -Encoding ASCII
    Info 'Created launcher_profiles.json'
}

# ---------- 2. Java ----------
Step 'Looking for Java'
function Find-Java {
    $candidates = @()
    if ($env:JAVA_HOME) { $candidates += Join-Path $env:JAVA_HOME 'bin\java.exe' }
    $onPath = Get-Command java.exe -ErrorAction SilentlyContinue
    if ($onPath) { $candidates += $onPath.Source }
    $runtimes = @(
        (Join-Path $MinecraftDir 'runtime'),
        (Join-Path $env:APPDATA '.minecraft\runtime'),
        (Join-Path $env:LOCALAPPDATA 'Packages\Microsoft.4297127D64EC6_8wekyb3d8bbwe\LocalCache\Local\runtime'),
        (Join-Path ${env:ProgramFiles(x86)} 'Minecraft Launcher\runtime')
    )
    foreach ($r in $runtimes) {
        if ($r -and (Test-Path $r)) {
            $candidates += Get-ChildItem $r -Recurse -Filter java.exe -ErrorAction SilentlyContinue |
                Sort-Object { if ($_.FullName -match 'delta') { 0 } elseif ($_.FullName -match 'gamma') { 1 } else { 2 } } |
                ForEach-Object FullName
        }
    }
    foreach ($dir in @("$env:ProgramFiles\Eclipse Adoptium", "$env:ProgramFiles\Java", "$env:ProgramFiles\Microsoft")) {
        if (Test-Path $dir) { $candidates += Get-ChildItem $dir -Recurse -Filter java.exe -ErrorAction SilentlyContinue | ForEach-Object FullName }
    }
    $ErrorActionPreference = 'Continue'   # java -version writes to stderr
    foreach ($c in $candidates | Select-Object -Unique) {
        if (-not (Test-Path $c)) { continue }
        $v = try { & $c -version 2>&1 | Out-String } catch { '' }
        if ($v -match 'version "(\d+)') { if ([int]$Matches[1] -ge 17) { return $c } }
    }
    return $null
}
$java = Find-Java
if (-not $java) {
    Fail ("Java 17 or newer not found. Start Minecraft 1.21.1 once from your launcher, or install Java 21 from " +
          "https://adoptium.net (Temurin 21, Windows x64), then run install.bat again.")
}
Info $java

# ---------- 3. NeoForge ----------
Step "Installing NeoForge $NeoForge into versions\"
$versionJson = Join-Path $MinecraftDir "versions\$VersionId\$VersionId.json"
if (Test-Path $versionJson) {
    Info 'Already installed, skipping.'
} else {
    $tmp = Join-Path $env:TEMP "zombieempire-lite-$PID"
    New-Item -ItemType Directory -Force $tmp | Out-Null
    $jar = Join-Path $tmp "neoforge-$NeoForge-installer.jar"
    Info 'Downloading the official NeoForge installer...'
    Invoke-WebRequest $InstallerUrl -OutFile $jar -UseBasicParsing
    $sha = (Invoke-WebRequest "$InstallerUrl.sha1" -UseBasicParsing).Content
    if ($sha -is [byte[]]) { $sha = [Text.Encoding]::ASCII.GetString($sha) }  # served as binary
    $expected = $sha.Trim().Split()[0].ToLower()
    if ((Get-Sha1 $jar) -ne $expected) { Fail 'The NeoForge installer download is corrupted. Try again.' }
    Info 'Running the NeoForge installer (downloads Minecraft 1.21.1 files, takes a minute)...'
    Push-Location $tmp
    $ErrorActionPreference = 'Continue'   # the installer logs to stderr
    try { & $java -jar $jar --install-client $MinecraftDir 2>&1 | Out-File (Join-Path $tmp 'neoforge-install.log') }
    finally { Pop-Location; $ErrorActionPreference = 'Stop' }
    if (-not (Test-Path $versionJson)) { Fail "NeoForge installation failed. Log: $tmp\neoforge-install.log" }
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
    Info "Installed: versions\$VersionId"
}

# ---------- 4. Mods ----------
Step 'Mods'
$mods = Join-Path $MinecraftDir 'mods'
New-Item -ItemType Directory -Force $mods | Out-Null
$list = Get-Content (Join-Path $here 'mods.txt') | Where-Object { $_ -and -not $_.StartsWith('#') } | ForEach-Object { , ($_ -split "`t") }
$ours = @($list | ForEach-Object { $_[1] }) + @(Get-ChildItem (Join-Path $files 'mods') -Filter *.jar | ForEach-Object Name)
$foreign = @(Get-ChildItem $mods -Filter *.jar | Where-Object { $ours -notcontains $_.Name })
if ($foreign.Count) {
    $backup = Join-Path $MinecraftDir ('mods-backup-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
    New-Item -ItemType Directory -Force $backup | Out-Null
    $foreign | Move-Item -Destination $backup
    Info "Moved $($foreign.Count) other mods to $backup"
}
Copy-Item (Join-Path $files 'mods\*.jar') $mods -Force
$failed = @()
$i = 0
foreach ($m in $list) {
    $i++
    $sha1, $file, $url = $m
    $target = Join-Path $mods $file
    if ((Test-Path $target) -and (Get-Sha1 $target) -eq $sha1.ToLower()) { continue }
    Info ("[{0,2}/{1}] {2}" -f $i, $list.Count, $file)
    $part = "$target.part"
    try {
        Invoke-WebRequest -Uri $url -OutFile $part -UseBasicParsing -UserAgent 'ZombieEmpire-Lite/1.1'
        if ((Get-Sha1 $part) -ne $sha1.ToLower()) { throw 'corrupted file (SHA-1 mismatch)' }
        Move-Item $part $target -Force
    } catch {
        if (Test-Path $part) { Remove-Item $part -Force }
        Write-Host "    FAILED: $file - $($_.Exception.Message)" -ForegroundColor Red
        $failed += $file
    }
}
if ($failed.Count) { Fail "$($failed.Count) mods could not be downloaded. Check your connection and run install.bat again." }
Info "$($list.Count + 1) mods ready."

# ---------- 5. Configs and map ----------
Step 'Configs and map'
foreach ($dir in Get-ChildItem $files -Directory | Where-Object { $_.Name -notin 'mods', 'saves' }) {
    Copy-Item $dir.FullName $MinecraftDir -Recurse -Force
}
$world = Join-Path $MinecraftDir 'saves\bossworld'
if (Test-Path $world) {
    Info 'Map bossworld already exists, keeping your progress.'
} else {
    New-Item -ItemType Directory -Force (Join-Path $MinecraftDir 'saves') | Out-Null
    Copy-Item (Join-Path $files 'saves\bossworld') (Join-Path $MinecraftDir 'saves') -Recurse
    Info 'Map bossworld added to singleplayer.'
}

# ---------- 6. Launcher profile ----------
Step 'Launcher profile'
$json = Get-Content $profiles -Raw | ConvertFrom-Json
if (-not $json.profiles) { $json | Add-Member -NotePropertyName profiles -NotePropertyValue ([pscustomobject]@{}) -Force }
$icon = 'data:image/png;base64,' + [Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $here 'icon.png')))
$now = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ss.fffZ')
$profile = [pscustomobject]@{
    name          = 'Zombie Empire'
    type          = 'custom'
    lastVersionId = $VersionId
    icon          = $icon
    javaArgs      = '-Xmx4G -XX:+UnlockExperimentalVMOptions -XX:+UseG1GC -XX:G1NewSizePercent=20 -XX:G1ReservePercent=20 -XX:MaxGCPauseMillis=50 -XX:G1HeapRegionSize=32M'
    created       = $now
    lastUsed      = $now
}
$json.profiles | Add-Member -NotePropertyName 'zombieempire' -NotePropertyValue $profile -Force
[IO.File]::WriteAllText($profiles, ($json | ConvertTo-Json -Depth 20), (New-Object Text.UTF8Encoding $false))  # no BOM
Info 'Profile "Zombie Empire" (version neoforge-21.1.215, 4 GB RAM) added.'

Write-Host ''
Write-Host '==========================================' -ForegroundColor Green
Write-Host '   DONE! Open your Minecraft launcher,'     -ForegroundColor Green
Write-Host '   pick the "Zombie Empire" profile and'    -ForegroundColor Green
Write-Host '   play the "bossworld" map.'               -ForegroundColor Green
Write-Host '==========================================' -ForegroundColor Green
Write-Host 'Official server: zombieempire.serveminecraft.net'
