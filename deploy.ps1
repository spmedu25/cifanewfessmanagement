# Dobara deploy (update) ke liye - Administrator PowerShell me chalayein
# Use: .\deploy.ps1            (project root se)
param(
  [string]$Site    = "Coaching",
  [string]$Pool    = "CoachingPool",
  [string]$Target  = "C:\inetpub\coaching",
  [string]$Staging = "C:\inetpub\coaching_publish"
)
$ErrorActionPreference = "Stop"
Import-Module WebAdministration
Set-Location "$PSScriptRoot\server"

dotnet publish -c Release -o $Staging
if ($LASTEXITCODE) { throw "publish fail" }

Stop-WebAppPool $Pool; Start-Sleep 3
# uploads folder aur appsettings.Production.json ko chhedta nahi
robocopy $Staging $Target /E /XD uploads /XF appsettings.Production.json /NFL /NDL /NJH /NJS
Start-WebAppPool $Pool
Write-Host "Deploy ho gaya. DB migrations app start par apne aap apply ho jate hain." -ForegroundColor Green
