param(
    [Parameter(Mandatory=$true)]
    [string]$KeyStorePassword
)

# -- Which Freedom site ------------------------------------------------
# There are no credentials in the build any more: SMTP, Unity, Better Stack
# and the compliance address all come from Freedom once the tablet signs in.
# The one thing the build decides is WHICH Freedom site it asks, from the
# git-ignored appsettings.json. That is the test-or-live choice the old
# "Production build?" prompt used to make by baking dev credentials in, so
# it is shown here and confirmed rather than assumed.
$appSettingsPath = 'TheBleedingDeacons.Intergroup.Register\appsettings.json'
if (-not (Test-Path $appSettingsPath)) {
    Write-Host "No $appSettingsPath. It must name the Freedom site the tablet takes its settings from." -ForegroundColor Red
    exit 1
}

$freedomSite = (Get-Content $appSettingsPath -Raw | ConvertFrom-Json).Freedom.BaseUrl
if ([string]::IsNullOrWhiteSpace($freedomSite)) {
    Write-Host "$appSettingsPath names no Freedom site (Freedom:BaseUrl). A tablet built from it would have no settings at all." -ForegroundColor Red
    exit 1
}

Write-Host "This build takes its settings from the Freedom site: $freedomSite" -ForegroundColor Cyan
$answer = Read-Host "Build against that site? (yes/no) [no]"
if ($answer -ine 'yes') {
    Write-Host "Aborted. Change Freedom:BaseUrl in $appSettingsPath to build against another site." -ForegroundColor Yellow
    exit 1
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
    -p:AndroidSigningStorePass=$KeyStorePassword

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