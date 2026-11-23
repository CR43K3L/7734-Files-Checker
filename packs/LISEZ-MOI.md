# Ajouter un pack dans l'app

L'app lit `packs/packs.json` sur GitHub à chaque ouverture de la page **FiveM > Mes packs**. Pas besoin de nouvelle version de l'app pour ajouter ou mettre à jour un pack.

## Préparer le .zip

Dans le zip, range les fichiers dans ces dossiers (le zip peut aussi contenir un dossier qui les englobe) :

- `mods\` : copié dans `FiveM.app\mods`
- `plugins\` : copié dans `FiveM.app\plugins`
- `GTAV\` (ou `GTA\`) : copié dans le dossier de GTA V Legacy (ENB, ReShade, sons)
- `citizen\` : copié dans `FiveM.app\citizen` (rarement utile)

Mets le zip sur Google Drive, partage-le en « Tous les utilisateurs disposant du lien » et copie le lien.

## Ajouter le pack à la liste

```json
{
  "packs": [
    {
      "id": "pack-photo",
      "name": "Pack Photo",
      "version": "1.0",
      "kind": "Graphique",
      "description": "ENB + ReShade réaliste, sons d'armes.",
      "url": "https://drive.google.com/file/d/XXXXXXXX/view?usp=sharing",
      "size": 1500000000
    }
  ]
}
```

- `id` : ne le change jamais (l'app s'en sert pour savoir quel pack est installé).
- `version` : augmente-la quand tu mets à jour le zip, les joueurs verront « Nouvelle version ».
- `size` : taille du zip en octets (facultatif, sert à la barre de progression).
