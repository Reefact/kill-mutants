# KillMutants — architecture

> Un outil de mutation testing moderne et opinionated pour .NET, conçu pour xUnit 4.

## 1. Contraintes

Ce sont des engagements, pas des valeurs par défaut. Ce sont eux qui permettent à la conception de
rester petite.

**Supporté :** xUnit 4, .NET moderne (net10.0), C#, projets au format SDK.

**Non supporté, et aucune abstraction n'existe en prévision :** xUnit 2 et antérieurs, NUnit, MSTest,
TUnit, VSTest, .NET Framework, formats de projet non-SDK, `packages.config`, F#, Visual Basic.

« xUnit 4 » désigne la famille de paquets `xunit.v3` en version `4.0.0` (publiée le 2026-08-15). Il
n'existe pas d'identifiant de paquet `xunit.v4`. La déclinaison Microsoft Testing Platform 2 est
`xunit.v3.mtp-v2`.

**Où se situe Microsoft Testing Platform.** MTP 2 fait partie de l'écosystème que nous visons, ce
n'est pas une contrainte autour de laquelle nous concevons. Les projets xUnit 4 peuvent ou non
s'appuyer dessus, et KillMutants traite les deux cas. Ce dont l'outil a besoin, c'est du chemin
d'exécution xUnit 4 le plus simple, le plus fiable et le plus performant pour le besoin considéré ;
aujourd'hui c'est le runner propre à xUnit, qui fournit en prime `-stopOnFail`, `-list tests /json`
et `-id <uid>`. Un couplage direct à MTP — un client JSON-RPC, par exemple — ne sera introduit que
lorsqu'un besoin concret de KillMutants le justifiera *et* que xUnit 4 ne saura pas y répondre.

KillMutants ne prend **aucune dépendance envers un paquet xUnit ou MTP**. Il lance l'exécutable du
projet de test comme processus enfant. Le couplage est la connaissance d'un contrat de ligne de
commande, pas une référence — ce qui constitue la forme la plus forte de « dépendance localisée »
disponible ici.

## 2. Faits vérifiés

Chaque chiffre ci-dessous a été mesuré sur cette plateforme (SDK 10.0.111, runtime 10.0.11), et non
tiré de connaissances antérieures.

| Fait | Valeur |
|---|---|
| Émission Roslyn, en réutilisant la compilation | **6 ms / mutant** |
| `dotnet build` du même projet | ~1400 ms |
| Exécution de l'application de test (2 tests) | ~600 ms |
| Phase mutants, 60 mutants, 4 cœurs — 1 / 2 / 4 workers | 1,0× / ~2,1× / ~3,2× |
| `dotnet test --no-build` | ~1500 ms |
| Code de sortie de l'exécutable de test — succès / échec | 0 / **1** |
| Code de sortie de `dotnet test` — succès / échec / aucun test | 0 / 2 / 8 |

Le rapport décisif est **compilation : tests ≈ 1 : 100**. La compilation n'est pas le goulot
d'étranglement.

## 3. Le pipeline

```
  découvrir -> analyser -> générer -> [ par mutant : appliquer -> compiler -> injecter -> exécuter -> classer ] -> rapporter
```

Concrètement, une exécution se déroule ainsi :

1. **Découvrir** le projet à muter et le projet de test qui l'exerce.
2. **Analyser** : demander à MSBuild la ligne de commande `csc` exacte, la transformer en
   `CSharpCompilation`.
3. **Vérifier le baseline** : émettre la compilation *non mutée*, l'injecter, exécuter les tests,
   exiger le vert. Ce n'est pas optionnel — voir
   [ADR-0005](adr/0005-verify-the-baseline-before-mutating-fr.md).
4. **Générer** les mutants en parcourant les arbres syntaxiques avec le catalogue de mutateurs.
5. Pour chaque mutant : remplacer l'arbre syntaxique, émettre, écrire l'assembly dans le répertoire
   de sortie du projet de test, exécuter l'application de test, classer le résultat, restaurer
   l'original.
6. **Rapporter** les compteurs et le score de mutation.

## 4. Correspondance entre les concepts et le code

Les préoccupations ci-dessous sont maintenues distinctes en tant que namespaces et types. Elles ne
sont délibérément **pas** maintenues distinctes en tant qu'assemblys : pour le milestone 1, cela
ferait treize projets pour quelques centaines de lignes, c'est-à-dire exactement la structure
prématurée que ce projet s'est fixé d'éviter. Découper plus tard coûte peu ; les frontières de
namespace sont déjà placées là où passeraient les frontières d'assembly.

| Préoccupation | Namespace | Remarques |
|---|---|---|
| Découverte du projet | `KillMutants.Projects` | Localise le projet à muter et son projet de test |
| Analyse du code | `KillMutants.Analysis` | Ligne de commande `csc` -> `CSharpCompilation` |
| Génération des mutations | `KillMutants.Mutations` | Parcourt les arbres, produit les candidats |
| Catalogue des mutateurs | `KillMutants.Mutations.Mutators` | `IMutator` et ses implémentations |
| Représentation d'un mutant | `KillMutants.Mutations` | `Mutant`, `MutantId`, `MutantStatus`, `SourceLocation` |
| Instrumentation | *(absorbée)* | Voir ci-dessous |
| Compilation | `KillMutants.Compilation` | Émet un assembly muté |
| Découverte des tests | *(reportée en M4)* | Point d'accroche identifié : `-list tests /json` |
| Exécution des tests | `KillMutants.Testing` | `ITestRunner`, `TestRunOutcome` |
| Spécificités xUnit 4 / MTP 2 | `KillMutants.Testing.XUnit` | Le seul endroit qui connaît la CLI du runner |
| Association tests ↔ mutants | *(reportée en M5)* | Point d'accroche identifié : `-id <uid>` |
| Orchestration | `KillMutants.Execution` | La liste de phases, courte et linéaire |
| Résultats | `KillMutants.Reporting` | `MutationTestReport`, écriture console |
| CLI | `KillMutants.Cli` | `dotnet killmutants` |

**L'instrumentation n'a aucun code propre, et c'est voulu.** Parce que chaque mutant reçoit sa propre
compilation ([ADR-0002](adr/0002-one-compilation-per-mutant-fr.md)), « instrumenter » un mutant se
réduit à un appel à `SyntaxNode.ReplaceNode` suivi de `Compilation.ReplaceSyntaxTree`. Tout
l'appareillage qu'exige un outil fondé sur les schemata — aides de contrôle injectées, canal
d'activation à l'exécution, niveaux de placement, boucle de compilation/rollback — n'existe pas ici.
C'est la plus grande simplification de la conception, et la raison pour laquelle le reste tient en
peu de choses.

## 5. Modèle de domaine

Les mutants sont modélisés explicitement. Un mutant n'est jamais un assemblage de chaînes et
d'entiers.

- `MutantId` — une identité, pas un `int`.
- `MutatorName` — nomme la règle ayant produit la mutation.
- `SourceLocation` — fichier, ligne et colonne, pour le rapport.
- `Mutant` — identifiant, nom du mutateur, nœuds syntaxiques d'origine et de remplacement, position.
- `MutantStatus` — `Killed`, `Survived`, `CompileError`, `Timeout`, `NoCoverage`, `Pending`. Le
  milestone 1 ne produit que `Killed` et `Survived`, mais le vocabulaire est figé dès maintenant afin
  que les milestones suivants ajoutent du comportement au lieu de remodeler le modèle.
- `MutationScore` — un type valeur qui sait se calculer et se rendre, de sorte qu'aucun appelant ne
  divise deux entiers et ne formate un pourcentage à la main.

## 6. Risques

Classés par dommage attendu, d'après l'étude et nos propres sondes.

**Critique — faux positifs dus à une infidélité de compilation.** Si la compilation reconstruite
diffère du build réel d'une quelconque manière (un `AssemblyInfo.cs` généré manquant qui change la
version de l'assembly, une référence absente, un symbole de préprocesseur erroné), les tests échouent
pour des raisons étrangères à la mutation et tous les mutants sont rapportés `Killed`. Un outil de
mutation testing qui répond toujours « Killed » est pire que pas d'outil du tout, parce qu'il est
silencieusement rassurant. *Atténué par l'ADR-0005 : le baseline est émis par le même chemin et doit
être vert avant qu'aucun mutant ne soit envisagé.*

**Critique — mutants silencieusement équivalents dus à la réécriture de l'arbre.** Constaté de
première main : remplacer le *token* opérateur d'un `>=` par `>` laisse le nœud parent avec le kind
`GreaterThanOrEqualExpression`. Roslyn émet à partir du kind du nœud, si bien que `ToFullString()`
affiche `age > 18` alors que l'IL est inchangé. Le mutant est silencieusement équivalent et donc
toujours rapporté `Survived`. *Atténué en remplaçant les nœuds entiers, et par un test de
non-régression vérifiant que l'IL émis change effectivement.*

**Élevé — `CscCommandLineArgs` vide.** Si MSBuild considère le projet à jour, il saute `CoreCompile`
et ne renvoie aucun argument ; `CSharpCommandLineParser` produit alors une compilation par défaut
sans source ni référence. *Atténué en forçant la cible à s'exécuter et en vérifiant que la liste
d'arguments est non vide et contient `/out:` et `/target:`.*

**Élevé — une cible MSBuild restaurant l'assembly d'origine.** `dotnet build` et `dotnet test`
recopient tous deux la sortie du projet par-dessus un mutant injecté. *Atténué en n'invoquant jamais
ni l'un ni l'autre après injection : l'exécutable de test est lancé directement.*

**Moyen — un filtre ne correspondant à aucun test passe pour un succès.** Le runner console xUnit
sort avec `0` quand un filtre ne correspond à rien, ce qui classerait un mutant comme `Survived`.
*Atténué en lisant le fichier de résultats structuré et en exigeant un nombre de tests exécutés
strictement positif, plutôt qu'en se fiant au code de sortie.*

**Moyen — mutations introduisant une boucle infinie.** Hors d'atteinte pour l'unique mutateur du
milestone 1, mais le délai maximal doit exister avant que le catalogue ne grossisse. *Reporté en M2,
la durée du baseline étant déjà enregistrée pour en dériver le budget.*

## 7. Position dans la roadmap

Le milestone 1 comprenait un couple de projets, un mutateur (`>=` devient `>`), un mutant, exécuté
pour de vrai. M2 a étoffé le catalogue à six familles. M3 traite les structures de solution réelles :
plusieurs projets de test, plusieurs projets à muter, les références de projet suivies
transitivement, et un framework épinglé par projet. M6 teste les mutants en parallèle, chaque worker dans une copie privée du
répertoire de sortie des tests. Restent devant : M4 la découverte des tests ; M5 la couverture et
l'association tests ↔ mutants ; M7 le reporting ; M8 la CI ; M9 les mutations avancées.

**Pourquoi des bacs à sable plutôt qu'un hôte de test réutilisé à chaud.** Réutiliser un hôte d'un
mutant à l'autre est l'optimisation la plus tentante et celle que nous refusons : c'est la source de
la plainte de correction la plus ancienne chez Stryker, où de l'état global de processus fuit entre
mutants et gonfle les scores. Un répertoire de sortie privé par worker coûte une copie de répertoire
et un peu de disque, et achète la garantie qu'aucun mutant ne peut en observer un autre. Cela
signifie aussi que KillMutants n'écrit plus du tout dans la sortie de build du développeur.

**La règle d'ordre établie par M3.** Construire chaque projet de test, puis lire chaque ligne de
commande du compilateur, puis injecter. MSBuild ne doit pas tourner avant le build, car la lecture
d'une ligne de commande dépend de sa sortie ; ni après l'injection, car `dotnet build` et
`dotnet test` recopient tous deux l'assembly d'origine par-dessus un mutant. Voir RB-012 du backlog
de robustesse.

Rien dans M1 ne bloque ces étapes. La sélection des tests (M5) restreint ce qu'on demande à
`ITestRunner` d'exécuter. La parallélisation (M6) est accessible parce que chaque mutant est un
assembly indépendant dans un processus indépendant — la propriété que nous offre gratuitement le
choix d'une compilation par mutant.

### Ce que les milestones suivants devront traiter, déjà vérifié

Une relecture adverse du M1 livré a établi les points suivants sur cette machine. Ils sont consignés
maintenant parce que chacun est peu coûteux à anticiper et cher à découvrir tard.

- **M2 a besoin d'une liste de constructions à ne pas muter, et c'est une exigence de correction, pas
  un raffinement.** C# fige les valeurs `const` et les valeurs de paramètres par défaut dans le *site
  d'appel* à la compilation. Muter `const Limit = 18` en `99` dans la bibliothèque et remplacer
  l'assembly laisse un projet de test déjà compilé lire toujours `18`. Un tel mutant ne peut jamais
  être tué : muter ces constructions fabriquerait des survies factices garanties et abaisserait
  silencieusement le score.
- **Le point bloquant de M6 est l'injection, pas la compilation.** `AssemblyInjection` détient un
  seul chemin et `MutationTestSession` hisse un unique `using` au-dessus de la boucle : N mutants
  concurrents exigent N répertoires de sortie isolés. Mesuré avec quatre bacs à sable : 639 ms contre
  2 235 ms en séquentiel, soit un gain de 3,5×, avec des verdicts indépendants corrects. L'émission
  elle-même se parallélise bien — 3,76 ms par émission sur un thread, 0,85 ms sur quatre — ce qui
  renforce l'ADR-0002 au lieu de le fragiliser : le terme qu'optimiseraient les schemata rétrécit à
  mesure que l'on parallélise.
- **M5 et M6 entrent en collision, et la collision est dans le modèle de données.** Les identifiants
  uniques de tests xUnit dérivent du *chemin* de l'assembly, pas de son contenu : des copies de bac à
  sable identiques octet pour octet ont produit des UID différents. L'isolation par mutant et la
  sélection de tests par UID sont donc mutuellement exclusives en l'état. La question « sur quoi la
  carte de couverture est-elle indexée ? » doit être tranchée avant de construire l'une ou l'autre.
- **La couverture réclame un mécanisme que cette conception ne nomme pas encore.** Puisque rien n'est
  injecté, rien n'observe qu'un test a atteint un site de mutation donné. `-automated` fournit des
  événements par *test*, ce qui est un problème différent de l'atteignabilité par *site de mutation*.
  M5 devra choisir sa source délibérément — une passe instrumentée distincte, ou des données de
  couverture externes projetées sur les spans de mutation — plutôt que de supposer que le runner la
  fournit déjà.
- **La numérotation des mutants court sur toute la session.** Un seul générateur sert tous les
  projets, si bien que les identifiants ne se répètent jamais ; un générateur par projet
  redémarrerait à `M1` pour chacun et rendrait le rapport ambigu. Fait, et verrouillé par un test.
