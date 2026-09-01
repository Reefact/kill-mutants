# ADR-0002 — Une compilation par mutant

**Statut :** accepté · **Date :** 2026-08-31

## Contexte

Un outil de mutation testing doit produire, pour chaque mutant, un assembly dans lequel cette
mutation-là est active. Trois approches existent.

1. **Une compilation par mutant.** Remplacer l'arbre syntaxique, émettre un nouvel assembly.
2. **Les *mutant schemata*** (l'approche de Stryker.NET). Compiler *tous* les mutants dans un seul
   assembly, chacun gardé par un appel injecté `MutantControl.IsActive(id)`, et sélectionner le
   mutant actif à l'exécution via une variable d'environnement ou un fichier mappé en mémoire.
3. **Réécriture du source puis build réel.** Muter le fichier source dans une copie du projet et
   lancer `dotnet build`.

Les schemata existent pour éviter de recompiler. La question est donc : que coûte réellement une
recompilation ?

## Mesures

Mesuré sur cette plateforme (SDK 10.0.111), sur la fixture du milestone 1 :

| Opération | Coût |
|---|---|
| Émission Roslyn, en réutilisant la `CSharpCompilation` | **6 ms / mutant** |
| `dotnet build` du même projet | ~1400 ms |
| Une exécution de l'application de test | ~600 ms |

Le rapport compilation/tests est d'environ **1 : 100**. Une étude indépendante de Stryker.NET menée
pour ce projet a mesuré la même chose et abouti à la même conclusion.

Pour 1 000 mutants, cela représente environ 6 secondes de compilation contre 10 minutes d'exécution
de tests.

## Décision

**Une compilation et une émission Roslyn par mutant.** Pas de schemata, pas d'aide injectée, pas de
canal d'activation à l'exécution, pas d'analyse des niveaux de placement, pas de boucle de
compilation/rollback.

## Conséquences

Positives, et c'est la raison de la décision :

- Toute la charge de correction des schemata disparaît. La question de savoir si une expression
  conditionnelle peut légalement être injectée dans un initialiseur `const`, un argument d'attribut,
  un arbre d'expression, un constructeur statique ou un motif de pattern matching ne se pose plus —
  puisque rien n'est injecté.
- Un échec d'émission est un fait sans ambiguïté à propos d'un seul mutant (`CompileError`), constaté
  directement au lieu d'être découvert en recompilant jusqu'à 50 fois et en bissectant les
  diagnostics.
- Chaque mutant est un assembly indépendant et un processus indépendant, ce qui fait de la
  parallélisation prévue en M6 une addition plutôt qu'un problème de synchronisation.
- L'assembly d'un mutant est exactement ce qu'il prétend être, ce qui rend le débogage d'un résultat
  surprenant abordable.

Négatives, et assumées :

- Nous payons ~6 ms de compilation par mutant que les schemata éviteraient. À un rapport de 1:100,
  cela représente environ 1 % de la durée d'un run, en échange de la suppression de la plus grosse
  source de complexité et de risque de correction de la conception.
- La `CSharpCompilation` doit être maintenue en vie et réutilisée d'un mutant à l'autre. Émettre à
  froid coûte ~1,6 s, presque entièrement consacrées au chargement de 167 références de métadonnées —
  un coût unique par projet, pas par mutant. La reconstruire à chaque mutant invaliderait
  l'arithmétique de cet ADR.

## L'avis dissident, et pourquoi il n'a pas prévalu

L'une des études commandées pour ce projet soutenait l'inverse : adopter les schemata dès le premier
jour, au motif que « réécrire le source et recompiler par mutant est une impasse qui ne peut pas être
optimisée ensuite, seulement remplacée », en s'appuyant sur les notes de recherche de Stryker.

Cet avis a été écarté pour trois raisons.

1. **Il raisonne à partir de l'histoire plutôt que de la mesure.** Le pari de Stryker a été fait
   quand les coûts environnants étaient différents. La mesure ci-dessus date d'aujourd'hui, sur cette
   plateforme : les schemata suppriment environ 1 % du run.
2. **L'affirmation selon laquelle ce ne serait pas optimisable ensuite ne résiste pas.** Le terme
   coûteux est `N × tests`, et les deux leviers qui l'attaquent réellement — sélection des tests par
   la couverture, et parallélisation — sont indifférents à la manière dont le mutant est arrivé dans
   l'assembly. Ils sont même plus simples ici, puisque chaque mutant est un assembly isolé dans un
   processus isolé.
3. **La même étude fournit un argument contre sa propre recommandation.** La réutilisation à chaud
   des hôtes de test — que les schemata rendent attrayante — exige des points explicites où ces
   processus de longue durée sont réinitialisés (`MicrosoftTestPlatformRunnerPool.cs:96,140`). Un
   modèle processus-par-mutant ne peut tout simplement pas produire cette classe de bug — voir
   ADR-0008.

Une autre étude, qui a réellement mesuré les coûts de compilation et de test sur cette machine au
lieu de raisonner par précédent, est parvenue indépendamment à la même conclusion que cet ADR.

## À réexaminer si

Cette décision devra être reconsidérée si un profilage montre un jour que la compilation dépasse
~20 % du temps total d'un run — par exemple sur de très gros projets où le coût par émission croît
avec la taille du projet plutôt qu'avec celle du changement.
