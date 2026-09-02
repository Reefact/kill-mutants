# DEC0002 | Une compilation par mutant

## Statut

| Date | Statut | Note | Compte rendu lié |
|---|---|---|---|
| 2026-08-31 | Accepté | | |

## Contexte

Un outil de mutation testing doit produire, pour chaque mutant, un assembly dans lequel cette
mutation-là est active. Trois approches existent.

1. **Une compilation par mutant.** Remplacer l'arbre syntaxique, émettre un nouvel assembly.
2. **Les *mutant schemata*** (l'approche de Stryker.NET). Compiler *tous* les mutants dans un seul
   assembly, chacun gardé par un appel injecté `MutantControl.IsActive(id)`, et sélectionner le
   mutant actif à l'exécution via une variable d'environnement ou un fichier mappé en mémoire.
3. **Réécriture du source puis build réel.** Muter le fichier source dans une copie du projet et
   lancer `dotnet build`.

Les schemata existent pour éviter de recompiler. Ce que coûte réellement une recompilation a été
mesuré sur cette plateforme (SDK 10.0.111), sur la fixture du milestone 1 :

| Opération | Coût |
|---|---|
| Émission Roslyn, en réutilisant la `CSharpCompilation` | **6 ms / mutant** |
| `dotnet build` du même projet | ~1400 ms |
| Une exécution de l'application de test | ~600 ms |

Le rapport compilation/tests est d'environ **1 : 100**. Une étude indépendante de Stryker.NET menée
pour ce projet a mesuré la même chose et abouti à la même conclusion. Pour 1 000 mutants, cela
représente environ 6 secondes de compilation contre 10 minutes d'exécution de tests.

Émettre à froid coûte ~1,6 s, presque entièrement consacrées au chargement de 167 références de
métadonnées — un coût unique par projet, pas par mutant.

L'une des études commandées pour ce projet soutenait l'inverse : adopter les schemata dès le premier
jour, au motif que « réécrire le source et recompiler par mutant est une impasse qui ne peut pas être
optimisée ensuite, seulement remplacée », en s'appuyant sur les notes de recherche de Stryker. Elle
rapporte aussi que la réutilisation à chaud des hôtes de test — que les schemata rendent attrayante —
exige des points explicites où ces processus de longue durée sont réinitialisés
(`MicrosoftTestPlatformRunnerPool.cs:96,140`).

## Décision

Dans ce contexte, nous produisons chaque mutant par sa propre compilation et émission Roslyn, sans
schemata, sans aide injectée, sans canal d'activation à l'exécution, sans analyse des niveaux de
placement et sans boucle de compilation/rollback.

## Justification

À un rapport compilation/tests de 1:100, les schemata suppriment environ 1 % d'un run. Ce 1 % est le
bénéfice entier, et il s'achète au prix de la plus grosse source de complexité et de risque de
correction de la conception — un échange que la mesure rend facile à refuser.

La charge de correction est ce que coûtent réellement les schemata. Savoir si une expression
conditionnelle peut légalement être injectée dans un initialiseur `const`, un argument d'attribut,
un arbre d'expression, un constructeur statique ou un motif de pattern matching est une question qui
n'existe que parce que quelque chose est injecté ; une compilation par mutant ne la pose jamais.

L'affirmation selon laquelle cette voie ne serait pas optimisable ensuite ne résiste pas aux mesures.
Le terme coûteux est `N × tests`, et les deux leviers qui l'attaquent réellement — sélection des
tests par la couverture, et parallélisation — sont indifférents à la manière dont le mutant est
arrivé dans l'assembly. Ils sont même plus simples ici, puisque chaque mutant est un assembly isolé
dans un processus isolé.

Raisonner à partir du précédent de Stryker plutôt que de la mesure est ce qu'a fait l'étude
dissidente. Le pari de Stryker a été fait quand les coûts environnants étaient différents ; les
chiffres ci-dessus datent d'aujourd'hui, sur cette plateforme. Une autre étude, qui a mesuré les
coûts de compilation et de test sur cette machine au lieu de raisonner par précédent, est parvenue
indépendamment à la conclusion de cet enregistrement.

## Alternatives envisagées

### Alternative 1 — Les mutant schemata

* **Description :** compiler tous les mutants dans un seul assembly, chacun gardé par un appel
  injecté `MutantControl.IsActive(id)`, et sélectionner le mutant actif à l'exécution via une
  variable d'environnement ou un fichier mappé en mémoire. C'est l'approche de Stryker.NET, et elle
  a été défendue par l'une des études commandées pour ce projet.
* **Pourquoi écartée :** elle supprime environ 1 % d'un run, mesuré, en échange du plus grand risque
  de correction de la conception. Sa prétention à être la seule voie optimisable ne survit pas à la
  mesure, et la réutilisation à chaud des hôtes de test qu'elle rend attrayante apporte une classe de
  bug de réinitialisation qu'un modèle processus-par-mutant ne peut pas produire (DEC0008).

### Alternative 2 — Réécriture du source puis build réel

* **Description :** muter le fichier source dans une copie du projet et lancer `dotnet build` pour
  chaque mutant.
* **Pourquoi écartée :** `dotnet build` coûte ~1400 ms contre 6 ms pour une émission Roslyn — plus de
  200 fois le coût de la voie retenue, sur l'opération même que cette alternative existe pour
  réaliser.

## Conséquences

### Positives

* Toute la charge de correction des schemata disparaît. La question de savoir si une expression
  conditionnelle peut légalement être injectée dans un initialiseur `const`, un argument d'attribut,
  un arbre d'expression, un constructeur statique ou un motif de pattern matching ne se pose plus —
  puisque rien n'est injecté.
* Un échec d'émission est un fait sans ambiguïté à propos d'un seul mutant (`CompileError`), constaté
  directement au lieu d'être découvert en recompilant jusqu'à 50 fois et en bissectant les
  diagnostics.
* Chaque mutant est un assembly indépendant et un processus indépendant, ce qui fait de la
  parallélisation prévue en M6 une addition plutôt qu'un problème de synchronisation.
* L'assembly d'un mutant est exactement ce qu'il prétend être, ce qui rend le débogage d'un résultat
  surprenant abordable.

### Négatives

* Nous payons ~6 ms de compilation par mutant que les schemata éviteraient — environ 1 % d'un run à
  un rapport de 1:100.
* La `CSharpCompilation` doit être maintenue en vie et réutilisée d'un mutant à l'autre. La
  reconstruire à chaque mutant invaliderait l'arithmétique de cet enregistrement.

### Risques

* Sur de très gros projets, le coût par émission peut croître avec la taille du projet plutôt qu'avec
  celle du changement, portant la compilation au-delà de la part du temps d'exécution que cette
  décision lui suppose.

### Actions de suivi

* Reconsidérer cette décision si un profilage montre un jour que la compilation dépasse ~20 % du
  temps total d'un run.
