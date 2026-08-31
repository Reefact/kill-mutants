# ADR-0003 — Prendre les entrées de compilation dans la ligne de commande `csc` de MSBuild

**Statut :** accepté · **Date :** 2026-08-31

## Contexte

Pour émettre un assembly muté fidèle, KillMutants a besoin des entrées exactes du compilateur pour le
projet : fichiers sources (y compris ceux générés par le SDK), références de métadonnées, symboles de
préprocesseur, version du langage, contexte nullable, options `unsafe` et de dépassement, type de
sortie, nom d'assembly et ressources embarquées.

Se tromper sur l'un de ces éléments ne produit pas une erreur. Cela produit un **faux positif** : les
tests échouent pour des raisons étrangères à la mutation, et tous les mutants sont rapportés comme
`Killed`.

Les options envisagées étaient `MSBuildWorkspace`, les API MSBuild avec `MSBuildLocator`, Buildalyzer
(ce qu'utilise Stryker.NET), et interroger MSBuild directement.

Stryker.NET lance un *design-time build* via Buildalyzer puis reconstruit à la main les
`CSharpCompilationOptions` et `CSharpParseOptions` de Roslyn à partir de chaînes de propriétés
MSBuild brutes. Notre étude a identifié là la plus grande source de complexité accidentelle de ce
code, entraînant avec elle Mono.Cecil pour la récupération des ressources, un fournisseur d'options
`analyzerconfig` écrit à la main, et un chargeur d'assemblys d'analyseurs sur mesure.

## Décision

Demander à MSBuild la **ligne de commande `csc` réelle**, et laisser Roslyn l'analyser :

```
dotnet build <projet> -t:Build \
  -p:ProvideCommandLineArgs=true -p:SkipCompilerExecution=true \
  -getItem:CscCommandLineArgs
```

puis `CSharpCommandLineParser.Default.Parse(args, projectDirectory, sdkDirectory: null)`.

Vérifié sur la fixture : 205 arguments, donnant 4 fichiers sources (dont les `GlobalUsings.g.cs` et
`AssemblyInfo.cs` générés), 167 références de métadonnées, `LanguageVersion.CSharp14`,
`NullableContextOptions.Enable` — avec **zéro** erreur d'analyse et rien de reconstruit à la main.

## Conséquences

- Pas de Buildalyzer, pas de `MSBuildWorkspace`, pas de `MSBuildLocator`, aucune dépendance tierce.
- Rien n'est deviné. Chaque réglage est celui qui allait effectivement être passé à `csc`, y compris
  ceux auxquels personne ne pense à reconstruire avant qu'un utilisateur ne remonte un bug.
- Les sources générées par le SDK suivent automatiquement. Les omettre est une cause connue de faux
  positifs : sans l'`AssemblyInfo.cs` généré, la version de l'assembly devient `0.0.0.0`, l'hôte de
  test échoue à le charger, et cela se manifeste comme un échec de test ordinaire.
- **Les générateurs de source font exception, et cet ADR l'affirmait initialement de façon trop
  large.** La ligne de commande nomme les générateurs sous `/analyzer:` mais ne liste *pas* le code
  qu'ils produisent, puisque le compilateur le génère pendant le build. Les exécuter est une étape
  distincte, décrite en RB-002 du backlog de robustesse. La ligne de commande fournit malgré tout
  tout ce dont cette étape a besoin — les assemblys de générateurs, les fichiers de configuration
  d'analyseurs et les fichiers additionnels — donc la décision tient ; elle ne fait simplement pas
  tout le travail à elle seule.
- Nous dépendons de `-getItem:` et de `ProvideCommandLineArgs`, qui sont des fonctionnalités MSBuild
  plutôt qu'un contrat d'API publique documenté. C'est assumé : le repli, si cela cassait un jour,
  consiste à lire la même information depuis un journal binaire, et le risque est immédiatement
  détecté par la vérification du baseline de l'ADR-0005.

## Le piège connu

Si MSBuild considère le projet à jour, il peut sauter `CoreCompile` et renvoyer une liste d'arguments
**vide**. `CSharpCommandLineParser` produit alors joyeusement une compilation par défaut, sans source
ni référence. Le résultat analysé doit donc être vérifié plutôt que présumé : non vide, et contenant
`/out:` et `/target:`.
