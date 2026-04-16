param(
    [Parameter(Mandatory=$true)]
    [string]$KeyStorePassword,

    [Parameter(Mandatory=$true)]
    [string]$Production = "no"
)

# ── Resolve production flag ───────────────────────────────────────────
# The parameter is mandatory so the operator is always prompted, but
# defaults to "no" so hitting Enter produces a dev build.
$isProduction = $Production -ieq 'yes'

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