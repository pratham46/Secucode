<#
Adds a user to the secucode database with bcrypt-hashed password/PIN.

  .\Database\add-user.ps1 -Username alice -Password secret -Role Student -Pin 4321 -Branch "Computer Engineering" -Class TE -Batch A1
  .\Database\add-user.ps1 -Username bob -Password secret -Role Teacher

Branch/class/batch are created if they don't exist yet.
Needs: mysql.exe (MySQL Server 8.4 default path or on PATH) and a restored NuGet cache (dotnet build).
#>
param(
    [Parameter(Mandatory)][string]$Username,
    [Parameter(Mandatory)][string]$Password,
    [Parameter(Mandatory)][ValidateSet('Student', 'Teacher')][string]$Role,
    [string]$Pin,
    [string]$Branch,
    [string]$Class,
    [string]$Batch,
    [string]$DbUser = 'root',
    [string]$DbPassword = 'root'
)
$ErrorActionPreference = 'Stop'

if ($Role -eq 'Student' -and (-not $Pin -or -not $Branch -or -not $Class -or -not $Batch)) {
    throw 'Students need -Pin, -Branch, -Class and -Batch.'
}

# net35 build has no extra dependencies, so it loads in Windows PowerShell 5.1
Add-Type -Path "$env:USERPROFILE\.nuget\packages\bcrypt.net-next\4.0.3\lib\net35\BCrypt.Net-Next.dll"

$mysql = 'C:\Program Files\MySQL\MySQL Server 8.4\bin\mysql.exe'
if (-not (Test-Path $mysql)) { $mysql = 'mysql' }

function Q([string]$s) { if ($null -eq $s -or $s -eq '') { 'NULL' } else { "'" + $s.Replace('\', '\\').Replace("'", "''") + "'" } }

$hash = [BCrypt.Net.BCrypt]::HashPassword($Password)
$pinHash = if ($Pin) { [BCrypt.Net.BCrypt]::HashPassword($Pin) } else { $null }

$sql = @"
INSERT IGNORE INTO branches (branch_name) SELECT $(Q $Branch) FROM DUAL WHERE $(Q $Branch) IS NOT NULL;
INSERT IGNORE INTO classes  (class_name)  SELECT $(Q $Class)  FROM DUAL WHERE $(Q $Class)  IS NOT NULL;
INSERT IGNORE INTO batches  (batch_name)  SELECT $(Q $Batch)  FROM DUAL WHERE $(Q $Batch)  IS NOT NULL;
INSERT INTO users (username, password, role, pin, branch_id, class_id, batch_id) VALUES (
  $(Q $Username), $(Q $hash), $(Q $Role), $(Q $pinHash),
  (SELECT id FROM branches WHERE branch_name = $(Q $Branch)),
  (SELECT id FROM classes  WHERE class_name  = $(Q $Class)),
  (SELECT id FROM batches  WHERE batch_name  = $(Q $Batch)));
"@

$sql | & $mysql "-u$DbUser" "-p$DbPassword" secucode
if ($LASTEXITCODE -ne 0) { throw "mysql failed ($LASTEXITCODE)" }
Write-Host "Added $Role '$Username'."
