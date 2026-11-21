# 7734 Files Checker

Analyseur d'espace disque pour Windows : il montre en quelques secondes ce qui remplit ton disque.

## État

- **Étape 1 (faite)** : moteur de scan
  - lecture directe de la table NTFS (MFT) quand l'app est lancée en administrateur, comme les analyseurs les plus rapides ;
  - scan classique dossier par dossier sinon (disques FAT/exFAT, réseau, pas de droits admin) ;
  - gère les fichiers creux, compressés, stockés dans la MFT et les liens durs (comptés une seule fois).
- **Étape 2 (faite)** : interface `7734FilesChecker.exe`
  - arborescence triée par taille (pourcentage du parent, espace occupé, nombre de fichiers, date) ;
  - carte en rectangles colorés par type de fichier (survol = détails, clic = sélection, double-clic = zoom) ;
  - onglets « Types de fichiers » et « Plus gros fichiers » (1 000 premiers) ;
  - clic droit : ouvrir, afficher dans l'Explorateur, copier le chemin, analyser ce dossier, envoyer à la corbeille ;
  - demande les droits admin au démarrage pour le scan rapide, sinon scan classique avec un bouton pour relancer en admin.
- Étape 3 : tests sur PC Windows
- Étape 4 : diffusion via GitHub Releases

## Structure

- `src/Files7734.Core` : moteur de scan et statistiques
- `src/Files7734.App` : l'application Windows (WPF)
- `src/Files7734.Cli` : petit outil console pour tester le moteur (`7734FilesChecker-Scan.exe`)
- `tests/Files7734.Tests` : tests automatiques (dont une vraie petite image NTFS)

## Compiler

Il faut le SDK .NET 8 de Microsoft (celui des dépôts Ubuntu ne sait pas compiler WPF).

```
dotnet test
dotnet publish src/Files7734.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
dotnet publish src/Files7734.Cli -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o publish
```
