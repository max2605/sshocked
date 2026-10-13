#Requires -RunAsAdministrator

$ErrorActionPreference = "Stop"
$Repo = "ms-sk/sshocked"
$Binary = "ssk.exe"
$InstallDir = "$env:ProgramFiles\sshocked"

function Info  { Write-Host "==>" -ForegroundColor Blue -NoNewline; Write-Host " $args" }
function Ok    { Write-Host " ✓" -ForegroundColor Green -NoNewline; Write-Host " $args" }
function Die   { Write-Host "!! $args" -ForegroundColor Red; exit 1 }

$Arch = switch ([Environment]::GetEnvironmentVariable("PROCESSOR_ARCHITECTURE")) {
  "AMD64" { "x64" }
  default { Die "No Windows release package for architecture: $_. Supported: AMD64 (win-x64)." }
}

$Rid = "win-$Arch"
Info "Detected: $Rid"

Info "Fetching latest release..."
$Release = Invoke-RestMethod "https://api.github.com/repos/$Repo/releases/latest" -Headers @{ Accept = "application/json" }
$Tag = $Release.tag_name
if (-not $Tag) { Die "Could not determine the latest release tag." }

$DownloadUrl = "https://github.com/$Repo/releases/download/$Tag/sshocked-$Rid.zip"
$TempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
$TempPath = [IO.Path]::GetFullPath([IO.Path]::Combine($TempRoot, "sshocked-install-$([Guid]::NewGuid().ToString('N'))"))
$ArchivePath = [IO.Path]::Combine($TempPath, "sshocked-$Rid.zip")
$ExtractPath = [IO.Path]::Combine($TempPath, "extract")

function Assert-TemporaryPath {
  param([string]$Path)
  $AbsolutePath = [IO.Path]::GetFullPath($Path)
  if (-not [string]::Equals([IO.Path]::GetDirectoryName($AbsolutePath), $TempRoot, [StringComparison]::OrdinalIgnoreCase) -or
      [IO.Path]::GetFileName($AbsolutePath) -notmatch '^sshocked-install-[a-f0-9]{32}$') {
    throw "Refusing temporary cleanup outside the intended temp directory."
  }
  if ((Test-Path -LiteralPath $AbsolutePath) -and
      ((Get-Item -LiteralPath $AbsolutePath -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
    throw "Refusing temporary cleanup of a symbolic link or reparse point."
  }
}

Assert-TemporaryPath $TempPath
$TempCreated = $false
try {
  New-Item -ItemType Directory -Path $TempPath | Out-Null
  $TempCreated = $true
  Info "Downloading sshocked $Tag..."
  Invoke-WebRequest -Uri $DownloadUrl -OutFile $ArchivePath

  Info "Extracting..."
  Expand-Archive -LiteralPath $ArchivePath -DestinationPath $ExtractPath
  $ExtractedBinary = [IO.Path]::Combine($ExtractPath, $Binary)
  if (-not (Test-Path -LiteralPath $ExtractedBinary -PathType Leaf)) {
    throw "The release archive does not contain $Binary."
  }

  Info "Installing to $InstallDir..."
  New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
  Copy-Item -LiteralPath $ExtractedBinary -Destination ([IO.Path]::Combine($InstallDir, $Binary)) -Force

  $UserPath = [Environment]::GetEnvironmentVariable("PATH", "User")
  if ($UserPath -notlike "*$InstallDir*") {
    [Environment]::SetEnvironmentVariable("PATH", "$UserPath;$InstallDir", "User")
    $env:PATH += ";$InstallDir"
  }
}
finally {
  if ($TempCreated) {
    Assert-TemporaryPath $TempPath
    if (Test-Path -LiteralPath $TempPath) {
      Remove-Item -LiteralPath $TempPath -Recurse -Force
    }
  }
}

Ok "sshocked $Tag installed to $InstallDir\$Binary"
Info "Run 'ssk --help' to get started."
