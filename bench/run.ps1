$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$projects = @(
  @{ Name = 'LiteAPI (managed)'; Path = 'Apps/LiteManaged/LiteManaged.csproj'; Port = 5101 },
  @{ Name = 'LiteAPI (Rust)';    Path = 'Apps/LiteRust/LiteRust.csproj';       Port = 5102 },
  @{ Name = 'Minimal API';       Path = 'Apps/MinimalApi/MinimalApi.csproj';   Port = 5103 }
)

Write-Host "Building all projects (Release) …" -ForegroundColor Cyan
foreach ($p in $projects) {
  dotnet build $p.Path -c Release --nologo -v minimal | Out-Null
  if ($LASTEXITCODE -ne 0) { throw "Build failed: $($p.Name)" }
}
dotnet build Bench/Bench.csproj -c Release --nologo -v minimal | Out-Null

$procs = @()
try {
  foreach ($p in $projects) {
    Write-Host "Starting $($p.Name) on :$($p.Port) …" -ForegroundColor Cyan
    $proc = Start-Process -FilePath 'dotnet' `
      -ArgumentList @('run', '--project', $p.Path, '-c', 'Release', '--no-build') `
      -PassThru -WindowStyle Hidden
    $procs += @{ Name = $p.Name; Port = $p.Port; Process = $proc }
  }

  Write-Host "Waiting for /healthz …" -ForegroundColor Cyan
  foreach ($entry in $procs) {
    $url = "http://127.0.0.1:$($entry.Port)/healthz"
    $deadline = (Get-Date).AddSeconds(30)
    while ((Get-Date) -lt $deadline) {
      try {
        $r = Invoke-WebRequest -UseBasicParsing -Uri $url -TimeoutSec 1 -ErrorAction Stop
        if ($r.StatusCode -eq 200) { break }
      } catch { Start-Sleep -Milliseconds 200 }
    }
    Write-Host "  $($entry.Name) ready." -ForegroundColor DarkGreen
  }

  Write-Host ""
  Write-Host "Running benchmark …" -ForegroundColor Cyan
  dotnet run --project Bench/Bench.csproj -c Release --no-build -- bench-all
} finally {
  foreach ($entry in $procs) {
    try { Stop-Process -Id $entry.Process.Id -Force -ErrorAction SilentlyContinue } catch {}
  }
}
