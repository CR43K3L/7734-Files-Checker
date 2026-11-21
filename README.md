# 7734 Files Checker

Analyseur d'espace disque pour Windows : il montre en quelques secondes ce qui remplit ton disque.

## État

- **Étape 1 (faite)** : moteur de scan
  - lecture directe de la table NTFS (MFT) quand l'app est lancée en administrateur, comme les analyseurs les plus rapides ;
  - scan classique dossier par dossier sinon (disques FAT/exFAT, réseau, pas de droits admin) ;
  - gère les fichiers creux, compressés, stockés dans la MFT et les liens durs (comptés une seule fois).
- Étape 2 : interface (arborescence, treemap, plus gros fichiers, extensions)
- Étape 3 : tests sur PC Windows
- Étape 4 : diffusion via GitHub Releases

## Structure

- `src/Files7734.Core` : moteur de scan et statistiques
- `src/Files7734.Cli` : petit outil console pour tester le moteur (`7734FilesChecker-Scan.exe`)
- `tests/Files7734.Tests` : tests automatiques (dont une vraie petite image NTFS)

## Compiler

```
dotnet test
dotnet publish src/Files7734.Cli -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o publish
```
