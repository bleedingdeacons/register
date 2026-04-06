param(
    [Parameter(Mandatory=$true)]
    [string]$KeyStorePassword
)

dotnet restore TheBleedingDeacons.Unity.Intergroup\TheBleedingDeacons.Unity.Intergroup.csproj -p:TargetFramework=net9.0-android
dotnet restore TheBleedingDeacons.Intergroup.Register\TheBleedingDeacons.Intergroup.Register.csproj -p:TargetFramework=net9.0-android
dotnet publish TheBleedingDeacons.Intergroup.Register\TheBleedingDeacons.Intergroup.Register.csproj -f net9.0-android -c Release -p:AndroidKeyStore=true -p:AndroidSigningKeyStore=badi.keystore -p:AndroidSigningKeyAlias=intergroup-register -p:AndroidSigningKeyPass=$KeyStorePassword -p:AndroidSigningStorePass=$KeyStorePassword

Copy-Item TheBleedingDeacons.Intergroup.Register\bin\Release\net9.0-android\publish\*.apk C:\Data\dev\register\ -Force