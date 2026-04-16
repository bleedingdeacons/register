param(
    [Parameter(Mandatory=$true)]
    [string]$KeyStorePassword
)

# ── Always prompt for production flag ─────────────────────────────────
# PowerShell's Mandatory + default don't play well together, so we
# prompt manually. Pressing Enter without typing anything defaults to no.
$productionInput = Read-Host "Production build? (yes/no) [no]"
if ([string]::IsNullOrWhiteSpace($productionInput)) {
    $productionInput = 'no'
}

if ($productionInput -inotin @('yes','no')) {
    Write-Host "Invalid input '$productionInput'. Must be 'yes' or 'no'." -ForegroundColor Yellow
    exit 1
}

$isProduction = $productionInput -ieq 'yes'

if ($isProduction) {
    $answer = Read-Host "PRODUCTION build requested. Type 'YES' to confirm"
    if ($answer -cne 'YES') {
        Write-Host "Aborted — production build not confirmed." -ForegroundColor Yellow
        exit 1
    }
    $useDevCredentials = 'false'
    Write-Host "Building PRODUCTION (no dev credentials baked in)..." -ForegroundColor Red
} else {
    $useDevCredentials = 'true'
    Write-Host "Building with DEV credentials baked in..." -ForegroundColor Cyan
}

dotnet restore TheBleedingDeacons.Unity.Intergroup\TheBleedingDeacons.Unity.Intergroup.csproj -p:TargetFramework=net9.0-android
dotnet restore TheBleedingDeacons.Intergroup.Register\TheBleedingDeacons.Intergroup.Register.csproj -p:TargetFramework=net9.0-android

dotnet publish TheBleedingDeacons.Intergroup.Register\TheBleedingDeacons.Intergroup.Register.csproj `
    -f net9.0-android `
    -c Release `
    -p:AndroidKeyStore=true `
    -p:AndroidSigningKeyStore=..\..\badi.keystore `
    -p:AndroidSigningKeyAlias=badi `
    -p:AndroidSigningKeyPass=$KeyStorePassword `
    -p:AndroidSigningStorePass=$KeyStorePassword `
    -p:UseDevCredentials=$useDevCredentials

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed." -ForegroundColor Red
    exit $LASTEXITCODE
}

Copy-Item TheBleedingDeacons.Intergroup.Register\bin\Release\net9.0-android\publish\*.apk C:\Data\dev\register\ -Force

Write-Host "Build complete." -ForegroundColor Green