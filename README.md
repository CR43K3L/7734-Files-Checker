# 7734 Files Checker

Analyseur d'espace disque pour Windows : il lit directement la table NTFS pour analyser un disque entier en quelques secondes, puis t'aide à faire de la place sans risque.

## Télécharger

Va dans [Releases](https://github.com/CR43K3L/7734-Files-Checker/releases/latest) et télécharge **7734FilesChecker.exe**. Il n'y a rien à installer : double-clique dessus.

> Windows peut afficher « Windows a protégé votre ordinateur » parce que l'app est récente et pas encore signée. Clique sur **Informations complémentaires** puis **Exécuter quand même**.

L'app se met à jour toute seule : quand une nouvelle version sort, un bandeau apparaît en bas à gauche.

## Fonctionnalités

- **Analyse ultra-rapide** (lecture directe de la table NTFS en administrateur), arborescence, carte du disque avec zoom animé et fil d'Ariane, types de fichiers (clic = surbrillance sur la carte), plus gros fichiers, recherche, export CSV.
- **Faire de la place** : tu dis combien de Go il te faut, l'app propose un plan.
- **Jeux** : Steam, Epic, Riot, Battle.net, Ubisoft, EA, GOG, Rockstar, Xbox. Taille de chaque jeu, déplacement vers un autre disque sans réinstaller, nettoyage par jeu, restes de jeux désinstallés, jeux installés en double.
- **Nettoyage** : caches, temporaires, Windows Update, navigateurs, Discord, lanceurs… rien n'est coché d'office.
- **Doublons**, **vieux fichiers**, **captures et clips de jeux**, avec aperçu.
- **Applications** : désinstallation et recherche des restes oubliés.
- **Ce fichier est-il sûr ?** : signature numérique, indices de malware et vérification sur VirusTotal (seule l'empreinte est envoyée).
- **Qui bloque ce fichier ?**, fichiers cachés de Windows (hibernation, mémoire virtuelle, points de restauration), **santé des disques**, nouveautés depuis la dernière analyse, alerte disque presque plein.
- Thème sombre et clair.

## Compiler

.NET 8 SDK sous Windows :

```
dotnet publish src/Files7734.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
```

Tests : `dotnet test tests/Files7734.Tests`
