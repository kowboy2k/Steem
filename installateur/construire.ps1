# Construit les deux installateurs de Steem :
#  - complet : exe autonome (.NET inclus, rien à installer chez l'ami, ~65 Mo) ;
#  - léger   : exe seul (~200 Ko), propose de télécharger .NET 8 Desktop Runtime s'il manque.
$ErrorActionPreference = 'Stop'
$ici = $PSScriptRoot
$projet = Split-Path $ici
$csprojChemin = Join-Path $projet 'Steem.csproj'
[xml]$csproj = Get-Content $csprojChemin
$version = $csproj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$iscc = "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
$commun = '-p:PublishSingleFile=true', '-c', 'Release', '-r', 'win-x64', '--ignore-failed-sources'

# Ludusavi (bouton Désinstaller et disquette) : ludusavi.exe livré à côté de Steem. Pris dans $env:LUDUSAVI,
# sinon dans Ressources\Apps\Ludusavi deux dossiers au-dessus du projet, sinon dans le PATH.
$ludusavi = @($env:LUDUSAVI, (Join-Path $projet '..\..\Ressources\Apps\Ludusavi\ludusavi.exe'),
              (Get-Command ludusavi.exe -ErrorAction SilentlyContinue).Source) |
            Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $ludusavi) { throw "ludusavi.exe introuvable (variable LUDUSAVI)" }
Copy-Item $ludusavi (Join-Path $ici 'ludusavi\ludusavi.exe') -Force

dotnet publish $csprojChemin @commun --self-contained true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -o (Join-Path $ici 'publication')
if ($LASTEXITCODE) { throw "dotnet publish (complet) a échoué" }
dotnet publish $csprojChemin @commun --self-contained false -o (Join-Path $ici 'publication-leger')
if ($LASTEXITCODE) { throw "dotnet publish (léger) a échoué" }

& $iscc /Q "/DVersion=$version" (Join-Path $ici 'steem.iss')
if ($LASTEXITCODE) { throw "ISCC (complet) a échoué" }
& $iscc /Q "/DVersion=$version" /DLeger (Join-Path $ici 'steem.iss')
if ($LASTEXITCODE) { throw "ISCC (léger) a échoué" }

# Une seule version dans Sortie : les anciens installateurs partent à la corbeille.
$garder = "Steem_Setup_$version.exe", "Steem_Setup_${version}_leger.exe"
Add-Type -AssemblyName Microsoft.VisualBasic
Get-ChildItem (Join-Path $ici 'Sortie') -Filter 'Steem_Setup_*.exe' | Where-Object { $garder -notcontains $_.Name } | ForEach-Object {
  [Microsoft.VisualBasic.FileIO.FileSystem]::DeleteFile($_.FullName, 'OnlyErrorDialogs', 'SendToRecycleBin')
}
$garder | ForEach-Object { Get-Item (Join-Path $ici "Sortie\$_") }
