# DEC0004 | Exécuter les tests en lançant l'exécutable du projet de test

## Statut

| Date | Statut | Note | Compte rendu lié |
|---|---|---|---|
| 2026-08-31 | Accepté | | |

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
uniquement », et a tranché : ce sont les *projets testés* qui doivent être des projets xUnit 4 ;
KillMutants lui-même n'a pas à parler le protocole MTP. Cette contrainte a depuis été reformulée plus
précisément. Le socle est **xUnit 4 et .NET moderne uniquement**, sans aucune compatibilité VSTest ni
runners historiques. MTP 2 fait partie de l'écosystème visé — un projet xUnit 4 peut s'appuyer
dessus, et nous le traitons — mais ce n'est pas une contrainte architecturale. La règle est de
prendre le chemin d'exécution xUnit 4 le plus simple, le plus fiable et le plus performant pour le
besoin, et d'introduire un couplage direct à MTP seulement lorsqu'un besoin concret le justifie et
que xUnit 4 n'y répond pas déjà.

`dotnet test` comme `dotnet build` recopient l'assembly d'origine par-dessus la sortie du projet de
test.

Le runner console de xUnit sort avec **0** quand un filtre ne correspond à aucun test. Son XML de
résultats porte `total`, `passed`, `failed`, `errors` et `skipped`.

Le point d'entrée généré a deux formes, inversées par la propriété MSBuild
`UseMicrosoftTestingPlatformRunner`. Cela a été découvert après la rédaction initiale de cet
enregistrement, par une relecture adverse de l'implémentation :

```csharp
// par défaut                                // UseMicrosoftTestingPlatformRunner=true
if (--server || --internal-msbuild-node)     if (-automated || @@)
    hôte MTP;                                    runner console xUnit;
else                                         else
    runner console xUnit;                        hôte MTP;
```

Sur un projet utilisant la seconde forme, nos arguments parvenaient à l'hôte Microsoft Testing
Platform, qui les rejetait (`Unknown option '--noLogo'`), **sortait avec 5 et n'écrivait aucun
fichier de résultat** — interrompant tout le run. KillMutants y était purement et simplement
inutilisable. `-automated` sélectionne le runner console xUnit sous les *deux* formes.

## Décision

Dans ce contexte, nous exécutons les tests d'un projet en lançant son exécutable de test comme
processus enfant et en lisant le verdict dans le fichier de résultats structuré (`-result-xml`), sans
recourir à `dotnet test` ni à un client JSON-RPC MTP.

## Justification

C'est l'option la plus rapide, de 2,5× devant `dotnet test`, sur l'opération qui domine la durée
totale d'un run.

Elle ne prend aucune dépendance envers un paquet xUnit ou MTP : le couplage est un contrat de ligne
de commande, confiné à `KillMutants.Testing.XUnit`. Sous le socle reformulé — le chemin xUnit 4 le
plus simple, le plus fiable et le plus rapide, avec un couplage MTP seulement là où un besoin concret
l'exige — lancer l'exécutable n'est pas un compromis pragmatique face à une contrainte affichée :
c'est simplement le bon chemin.

La stabilité de l'injection découle du même choix. `dotnet test` et `dotnet build` recopient tous deux
l'assembly d'origine par-dessus le mutant : aucun des deux ne peut donc s'exécuter une fois le mutant
en place, et lancer l'exécutable directement est ce qui fait tenir l'injection.

Le verdict doit être lu dans les compteurs de résultats plutôt que dans le code de sortie, parce que
le runner console sort avec 0 quand un filtre ne correspond à aucun test. Un outil se fiant au code
de sortie rapporterait un tel mutant comme `Survived` ; lire `total`, `passed`, `failed`, `errors` et
`skipped` fait d'un run n'ayant exécuté aucun test une erreur plutôt qu'une survie.

## Alternatives envisagées

### Alternative 1 — `dotnet test --no-build`

* **Description :** atteindre le projet de test par MSBuild, comme le fait le flux .NET ordinaire.
* **Pourquoi écartée :** ~1,5 s contre ~0,6 s par run, sur l'opération qui domine la durée totale — et
  elle recopie l'assembly d'origine par-dessus le mutant injecté, ce qui la rend inutilisable ici
  quel que soit son coût.

### Alternative 2 — Le mode serveur MTP en JSON-RPC

* **Description :** parler directement le protocole Microsoft Testing Platform
  (`--server --client-port N`), seule autre voie d'accès à l'hôte MTP.
* **Pourquoi écartée :** le propriétaire du projet a tranché que KillMutants n'a pas à parler le
  protocole MTP, et le coût n'a même pas été mesuré puisque le chemin xUnit 4 le plus simple et le
  plus fiable fait déjà le travail. `ITestRunner` reste le point de couture où une implémentation en
  mode serveur viendrait se greffer si un besoin concret apparaissait.

## Conséquences

### Positives

* L'option la plus rapide, de 2,5× devant `dotnet test`, sur l'opération qui domine la durée totale.
* Aucune dépendance envers un paquet xUnit ou MTP ; le couplage est un contrat de ligne de commande
  confiné à `KillMutants.Testing.XUnit`.
* Les options dont nous aurons besoin plus tard existent déjà : `-stopOnFail` (un mutant est tué par
  son premier test en échec), `-list tests /json` (découverte des tests, M4), `-id <uid>` (exécuter
  un cas de test précis, pour l'association test↔mutant en M5).

### Négatives

* `dotnet test` et `dotnet build` ne doivent jamais s'exécuter après l'injection d'un mutant.
* Nous renonçons au flux d'événements par test, plus riche, de MTP. Si M5 montre que l'association
  test↔mutant en a réellement besoin, `ITestRunner` est le point où une implémentation en mode
  serveur viendrait se greffer — une couture volontairement mince, pas un système de plugins.

### Risques

* Ce qu'une application de test fait d'une ligne de commande est une propriété du projet qui l'a
  produite, pas de la version du framework. Une forme de projet que nous n'avons pas rencontrée peut
  rejeter nos arguments comme l'a fait la seconde forme du point d'entrée : sortie 5, aucun fichier
  de résultat, et tout le run interrompu.

### Actions de suivi

* Passer `-automated` à chaque exécution et le garder en tête de la liste d'arguments : c'est la
  seule option qui sélectionne le runner console xUnit sous les deux formes du point d'entrée.
* Conserver le test de non-régression end-to-end qui construit une fixture avec
  `UseMicrosoftTestingPlatformRunner=true` et échoue avec `Unknown option '--noLogo'` si l'option est
  retirée.
