# ADR-0004 — Exécuter les tests en lançant l'exécutable du projet de test

**Statut :** accepté · **Date :** 2026-08-31

## Contexte

Un projet de test xUnit 4 se compile en exécutable. En cherchant comment cet exécutable atteint
Microsoft Testing Platform, nous avons fait la découverte qui a déterminé cette décision. Le point
d'entrée généré par `xunit.v3.mtp-v2` est, en substance :

```csharp
if (args.Any(a => a == "--server" || a == "--internal-msbuild-node"))
    // hôte Microsoft Testing Platform
else
    // runner console in-process de xUnit
```

L'hôte MTP n'est donc atteignable **que** par le mode serveur JSON-RPC (`--server --client-port N`,
qui lève une exception sans port) ou par MSBuild (`dotnet test`). Lancer simplement l'exécutable
utilise le runner console propre à xUnit.

Trois options, toutes mesurées :

| Option | Coût par run | Code de sortie en cas d'échec |
|---|---|---|
| Lancer l'exécutable directement | **~0,6 s** | 1 |
| `dotnet test --no-build` | ~1,5 s | 2 |
| `--server --client-port N` (JSON-RPC) | non mesuré | protocole |

Le propriétaire du projet a été consulté sur le sens à donner à « Microsoft Testing Platform 2
uniquement », et a tranché : ce sont les *projets testés* qui doivent être des projets xUnit 4 /
MTP 2 ; KillMutants lui-même n'a pas à parler le protocole MTP.

## Décision

**Lancer l'exécutable du projet de test comme processus enfant.** Ne pas utiliser `dotnet test`. Ne
pas implémenter de client JSON-RPC MTP.

Lire le résultat depuis le **fichier de résultats structuré** (`-result-xml`), et non depuis le seul
code de sortie.

## Conséquences

- L'option la plus rapide, de 2,5× devant `dotnet test`, sur l'opération qui domine la durée totale.
- Aucune dépendance envers un paquet xUnit ou MTP. Le couplage est un contrat de ligne de commande,
  confiné à `KillMutants.Testing.XUnit`.
- `dotnet test` et `dotnet build` ne doivent jamais s'exécuter après l'injection d'un mutant : tous
  deux recopient l'assembly d'origine par-dessus. Lancer l'exécutable directement est ce qui rend
  l'injection stable.
- Les options dont nous aurons besoin plus tard existent déjà : `-stopOnFail` (un mutant est tué par
  son premier test en échec), `-list tests /json` (découverte des tests, M4), `-id <uid>` (exécuter
  un cas de test précis, pour l'association test↔mutant en M5).
- Nous renonçons au flux d'événements par test, plus riche, de MTP. Si M5 montre que l'association
  test↔mutant en a réellement besoin, `ITestRunner` est le point de couture où une implémentation en
  mode serveur viendrait se greffer — une couture volontairement mince, pas un système de plugins.

## Pourquoi pas le seul code de sortie

Le runner console de xUnit sort avec **0** quand un filtre ne correspond à aucun test. Un outil qui
se fierait au code de sortie rapporterait un tel mutant comme `Survived`. Le XML de résultats porte
`total`, `passed`, `failed`, `errors` et `skipped` : le verdict est donc lu depuis les compteurs, et
un run n'ayant exécuté aucun test est reconnu comme une erreur plutôt que comme une survie.
