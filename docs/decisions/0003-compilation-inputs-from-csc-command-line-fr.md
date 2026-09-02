# DEC0003 | Prendre les entrées de compilation dans la ligne de commande `csc` de MSBuild

## Statut

| Date | Statut | Note | Compte rendu lié |
|---|---|---|---|
| 2026-08-31 | Accepté | | |

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

MSBuild peut être interrogé sur la ligne de commande `csc` qu'il allait exécuter, et Roslyn sait
analyser cette ligne lui-même. Interrogé sur la fixture, il renvoie 205 arguments, donnant 4 fichiers
sources (dont les `GlobalUsings.g.cs` et `AssemblyInfo.cs` générés), 167 références de métadonnées,
`LanguageVersion.CSharp14` et `NullableContextOptions.Enable`, avec **zéro** erreur d'analyse et rien
de reconstruit à la main.

`-getItem:` et `ProvideCommandLineArgs` sont des fonctionnalités MSBuild plutôt qu'un contrat d'API
publique documenté. La même information est également récupérable depuis un journal binaire.

Si MSBuild considère le projet à jour, il saute `CoreCompile` et renvoie une liste d'arguments
**vide** ; `CSharpCommandLineParser` produit alors une compilation par défaut, sans source ni
référence.

La ligne de commande nomme les générateurs de source sous `/analyzer:` mais ne liste pas le code
qu'ils produisent, puisque le compilateur le génère pendant le build.

## Décision

Dans ce contexte, nous demandons à MSBuild la ligne de commande `csc` réelle et laissons Roslyn
l'analyser, plutôt que de reconstruire nous-mêmes les entrées de compilation.

## Justification

Rien n'est deviné. Chaque réglage est celui qui allait effectivement être passé à `csc`, y compris
ceux auxquels personne ne pense à reconstruire avant qu'un utilisateur ne remonte un bug — et c'est
le mode d'échec qui compte ici, puisqu'une entrée erronée se manifeste en faux positif et non en
erreur.

Le mécanisme se réduit à une invocation de build et à une analyse :

```
dotnet build <projet> -t:Build \
  -p:ProvideCommandLineArgs=true -p:SkipCompilerExecution=true \
  -getItem:CscCommandLineArgs
```

puis `CSharpCommandLineParser.Default.Parse(args, projectDirectory, sdkDirectory: null)`. Les 205
arguments de la fixture s'analysent sans une seule erreur, ce qui rend la reconstruction à la main —
la plus grande source de complexité accidentelle relevée dans Stryker.NET — inutile plutôt que
simplement indésirable.

Les sources générées par le SDK suivent automatiquement. Les omettre est une cause connue de faux
positifs : sans l'`AssemblyInfo.cs` généré, la version de l'assembly devient `0.0.0.0`, l'hôte de
test échoue à le charger, et cela se manifeste comme un échec de test ordinaire.

Dépendre de fonctionnalités MSBuild plutôt que d'un contrat documenté est acceptable parce que la
panne est récupérable et ne peut pas être silencieuse : la même information se lit dans un journal
binaire, et la vérification du baseline de le DEC0005 détecte la rupture immédiatement.

## Alternatives envisagées

### Alternative 1 — Buildalyzer, comme l'utilise Stryker.NET

* **Description :** lancer un *design-time build* via Buildalyzer puis reconstruire à la main les
  `CSharpCompilationOptions` et `CSharpParseOptions` de Roslyn à partir de chaînes de propriétés
  MSBuild brutes.
* **Pourquoi écartée :** notre étude de Stryker.NET y a identifié la plus grande source de complexité
  accidentelle de ce code, entraînant avec elle Mono.Cecil pour la récupération des ressources, un
  fournisseur d'options `analyzerconfig` écrit à la main et un chargeur d'assemblys d'analyseurs sur
  mesure — le tout pour reconstruire ce que la ligne de commande énonce déjà.

### Alternative 2 — Charger le projet par le modèle objet MSBuild

* **Description :** utiliser `MSBuildWorkspace`, ou les API MSBuild avec `MSBuildLocator`, pour
  obtenir la compilation.
* **Pourquoi écartée :** aucune raison n'est consignée au-delà du résultat que la décision
  revendique — la voie retenue ne prend aucune dépendance tierce ni aucune dépendance aux API
  MSBuild.

## Conséquences

### Positives

* Pas de Buildalyzer, pas de `MSBuildWorkspace`, pas de `MSBuildLocator`, aucune dépendance tierce.
* Rien n'est deviné. Chaque réglage est celui qui allait effectivement être passé à `csc`.
* Les sources générées par le SDK suivent automatiquement, ce qui supprime une cause connue de faux
  positifs.

### Négatives

* Nous dépendons de `-getItem:` et de `ProvideCommandLineArgs`, qui sont des fonctionnalités MSBuild
  plutôt qu'un contrat d'API publique documenté. C'est assumé.
* Les générateurs de source font exception, et cette décision l'affirmait initialement de façon trop
  large. La ligne de commande nomme les générateurs sous `/analyzer:` mais ne liste pas le code
  qu'ils produisent. Les exécuter est une étape distincte, décrite en RB-002 du backlog de
  robustesse. La ligne de commande fournit malgré tout tout ce dont cette étape a besoin — les
  assemblys de générateurs, les fichiers de configuration d'analyseurs et les fichiers additionnels —
  donc la décision tient ; elle ne fait simplement pas tout le travail à elle seule.

### Risques

* Si MSBuild considère le projet à jour, il saute `CoreCompile` et renvoie une liste d'arguments
  vide. `CSharpCommandLineParser` produit alors joyeusement une compilation par défaut, sans source
  ni référence — un état qui ressemble à une analyse réussie.

### Actions de suivi

* Vérifier le résultat analysé plutôt que le présumer : il doit être non vide et contenir `/out:` et
  `/target:` avant d'être utilisé.
