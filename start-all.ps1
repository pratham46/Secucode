# One-click test run: MySQL + API (serves the web app) in Docker, demo data, browser, and the Windows app.
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

if (-not (Test-Path .env)) {
    $jwt = -join ((48..57) + (97..122) | Get-Random -Count 48 | ForEach-Object { [char]$_ })
    (Get-Content .env.example) `
        -replace '^JWT_KEY=.*', "JWT_KEY=$jwt" `
        -replace '^Seed__AdminPassword=.*', 'Seed__AdminPassword=Admin@123' |
        Set-Content .env -Encoding ascii
    Write-Host 'Created .env (test settings: superadmin password Admin@123).'
}

docker info *> $null
if ($LASTEXITCODE -ne 0) {
    Write-Host 'Starting Docker Desktop...'
    Start-Process "$env:ProgramFiles\Docker\Docker\Docker Desktop.exe"
    do { Start-Sleep 3; docker info *> $null } until ($LASTEXITCODE -eq 0)
}

Write-Host 'Building and starting MySQL + API (first run takes a few minutes)...'
docker compose up -d --build
if ($LASTEXITCODE -ne 0) { throw 'docker compose failed (is port 8080 or 3307 already in use?)' }

Write-Host 'Waiting for the API...'
do {
    Start-Sleep 2
    try { $ok = (Invoke-WebRequest http://localhost:8080/ -UseBasicParsing -TimeoutSec 3).StatusCode -eq 200 } catch { $ok = $false }
} until ($ok)

docker compose exec api dotnet Secucode.Api.dll seed   # no-op once users exist

Start-Process http://localhost:8080/
# ponytail: Debug build, so focus-loss checks are off while testing; use -c Release to test them
Start-Process powershell -ArgumentList '-NoExit', '-Command', "dotnet run --project '$PSScriptRoot\clients\Secucode.Desktop'"

Write-Host ''
Write-Host 'Web app:   http://localhost:8080   (Swagger: /swagger)'
Write-Host 'MySQL:     localhost:3307  user root  password secucode-root  database secucode_api'
Write-Host 'Logins:    superadmin/Admin@123  hod.cse/Hod@123  faculty.cse/Faculty@123  student1/Student@123 (PIN 1234)'
Write-Host 'Stop:      docker compose down   (add -v to wipe the database)'
