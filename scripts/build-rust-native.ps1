Param(
  [ValidateSet('win-x64','win-arm64','linux-x64','linux-arm64','osx-x64','osx-arm64')]
  [string]$Rid = ''
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$csprojDir = Join-Path $repoRoot 'LiteAPI'
$rustDir = Join-Path $csprojDir 'liteapi_rust'

if ([string]::IsNullOrWhiteSpace($Rid)) {
  if ($IsWindows) { $Rid = 'win-x64' }
  elseif ($IsLinux) { $Rid = 'linux-x64' }
  elseif ($IsMacOS) {
    $arch = (uname -m)
    $Rid = if ($arch -eq 'arm64') { 'osx-arm64' } else { 'osx-x64' }
  }
  else { throw 'Unsupported OS' }
}

$target = $null
switch ($Rid) {
  'win-x64' { $target = 'x86_64-pc-windows-msvc' }
  'win-arm64' { $target = 'aarch64-pc-windows-msvc' }
  'linux-x64' { $target = 'x86_64-unknown-linux-gnu' }
  'linux-arm64' { $target = 'aarch64-unknown-linux-gnu' }
  'osx-x64' { $target = 'x86_64-apple-darwin' }
  'osx-arm64' { $target = 'aarch64-apple-darwin' }
}

Write-Host "Building Rust native for RID=$Rid (target=$target)" 
Push-Location $rustDir
try {
  if ($target) {
    rustup target add $target | Out-Null
    cargo build --release --target $target
  } else {
    cargo build --release
  }
} finally {
  Pop-Location
}

$runtimesDir = Join-Path $csprojDir (Join-Path 'runtimes' (Join-Path $Rid 'native'))
New-Item -ItemType Directory -Force -Path $runtimesDir | Out-Null

if ($Rid -like 'win-*') {
  $base = if ($target) { Join-Path $rustDir "target\$target\release" } else { Join-Path $rustDir 'target\release' }
  $candidate1 = Join-Path $base 'deps\liteapi_rust.dll'
  $candidate2 = Join-Path $base 'liteapi_rust.dll'
  $src = if (Test-Path $candidate1) { $candidate1 } elseif (Test-Path $candidate2) { $candidate2 } else { throw "liteapi_rust.dll not found under $base" }
  Copy-Item $src (Join-Path $runtimesDir 'liteapi_rust.dll') -Force
}
elseif ($Rid -like 'linux-*') {
  $base = if ($target) { Join-Path $rustDir "target/$target/release" } else { Join-Path $rustDir 'target/release' }
  $src = Join-Path $base 'libliteapi_rust.so'
  if (!(Test-Path $src)) { throw "libliteapi_rust.so not found under $base" }
  Copy-Item $src (Join-Path $runtimesDir 'libliteapi_rust.so') -Force
}
else {
  $base = if ($target) { Join-Path $rustDir "target/$target/release" } else { Join-Path $rustDir 'target/release' }
  $src = Join-Path $base 'libliteapi_rust.dylib'
  if (!(Test-Path $src)) { throw "libliteapi_rust.dylib not found under $base" }
  Copy-Item $src (Join-Path $runtimesDir 'libliteapi_rust.dylib') -Force
}

Write-Host "Copied native to $runtimesDir" 
