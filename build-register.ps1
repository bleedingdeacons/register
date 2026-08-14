param(
    [Parameter(Mandatory=$true)]
    [string]$KeyStorePassword
)

# -- Always prompt for production flag ---------------------------------
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
        Write-Host "Aborted -- production build not confirmed." -ForegroundColor Yellow
        exit 1
    }
    $useDevCredentials = 'false'
    Write-Host "Building PRODUCTION (no dev credentials baked in)..." -ForegroundColor Red
} else {
    $useDevCredentials = 'true'
    Write-Host "Building with DEV credentials baked in..." -ForegroundColor Cyan
}

# -- Android head TFM --------------------------------------------------
# Single source of truth. It appeared in four places before, which is four
# chances to half-upgrade the script; on a .NET version bump this is now the
# only line that changes. It also names the output APKs (see the copy below).
$targetFramework = 'net10.0-android'

$registerProject = 'TheBleedingDeacons.Intergroup.Register\TheBleedingDeacons.Intergroup.Register.csproj'

dotnet restore TheBleedingDeacons.Unity.Intergroup\TheBleedingDeacons.Unity.Intergroup.csproj -p:TargetFramework=$targetFramework
dotnet restore $registerProject -p:TargetFramework=$targetFramework

dotnet publish $registerProject `
    -f $targetFramework `
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

# -- Copy the APKs out, stamped ----------------------------------------
# The publish output is named after the application id alone, so every build
# used to overwrite the last one in the drop folder and there was no way to
# tell two APKs apart. Stamping the display version and the TFM into the
# filename means builds from different branches sit side by side, and the
# file says which .NET it was built against before you install it. The app
# reports the same pair on its Settings page once running (see BuildInfo).
#
# ApplicationDisplayVersion is read straight from the csproj, where it is
# deliberately kept unconditional -- see the long comment on that property.
$displayVersion = ([xml](Get-Content $registerProject)).Project.PropertyGroup.ApplicationDisplayVersion |
    Where-Object { $_ } | Select-Object -First 1

if ([string]::IsNullOrWhiteSpace($displayVersion)) {
    Write-Host "Could not read ApplicationDisplayVersion from the csproj." -ForegroundColor Red
    exit 1
}

$publishDir = "TheBleedingDeacons.Intergroup.Register\bin\Release\$targetFramework\publish"
$destination = 'C:\Data\dev\register'

Get-ChildItem "$publishDir\*.apk" | ForEach-Object {
    $stampedName = '{0}-{1}-{2}{3}' -f $_.BaseName, $displayVersion, $targetFramework, $_.Extension
    Copy-Item $_.FullName (Join-Path $destination $stampedName) -Force
    Write-Host "  $stampedName" -ForegroundColor Green
}

Write-Host "Build complete." -ForegroundColor Green