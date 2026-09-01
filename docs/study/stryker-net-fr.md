# Étude — Stryker.NET, et ce que KillMutants en retient

Stryker.NET (Apache-2.0, <https://github.com/stryker-mutator/stryker-net>) est l'implémentation de
référence du mutation testing pour .NET. Il a été étudié pour comprendre le *problème*, non pour être
copié. Aucun code source de Stryker n'a été réutilisé dans KillMutants ; ce qui suit est une
description dans nos propres termes, avec des citations `fichier:ligne` permettant de vérifier chaque
affirmation dans l'original.

Étudié le 2026-08-31, sur la branche `master`.

## 1. Forme du code

598 fichiers `.cs` (390 de production, 208 de test unitaire), ~27 000 lignes de production,
11 projets de production. La partie qui effectue réellement le mutation testing est petite : environ
sept fichiers et ~1 200 lignes, en une chaîne courte et linéaire.

    StrykerRunner -> ProjectOrchestrator -> InitialisationProcess -> ProjectMutator
                  -> MutationTestProcess -> MutationTestExecutor -> ITestRunner

Tout le reste est de l'étendue, et cette étendue est presque entièrement de la compatibilité :

| Domaine | Taille | Raison d'être |
|---|---|---|
| Options / configuration | ~21 % des fichiers (49 types « une classe par option » + 50 tests) | Dix ans d'options accumulées |
| VSTest + DataCollector | ~2 200 lignes | Plateforme de test antérieure à MTP, hôtes .NET Framework |
| Découverte de projet via Buildalyzer | ~2 500 lignes | .NET Framework, multi-TFM, `packages.config`, projets non-SDK |
| Rapporteurs, baseline, dashboard | ~2 900 lignes | Rapports HTML/JSON/dashboard, baselines S3/Azure |

La leçon que nous retenons est structurelle, pas mécanique : la liste de phases de haut niveau de
Stryker (`StrykerRunner.cs:49-164`) est restée courte, linéaire et lisible à travers dix ans
d'ajouts. Cette forme mérite d'être imitée. La leçon que nous évitons : `Stryker.Core` référence les
deux runners concrets et choisit entre eux par un `switch`
(`Initialisation/ProjectOrchestrator.cs:72-78`), si bien que l'abstraction `ITestRunner` n'apporte
rien au point de composition.

## 2. Le moteur de mutation

Les mutateurs sont de petites classes implémentant une interface commune, chacune déclarant le kind
de nœud syntaxique qu'elle traite et renvoyant zéro ou plusieurs mutations. Les opérateurs de
comparaison binaires — précisément ce dont le milestone 1 a besoin — sont traités dans
`Mutators/BinaryExpressionMutator.cs:33`, qui associe `GreaterThanOrEqualExpression` à la fois à `<`
et à `>`.

L'orchestration parcourt l'arbre syntaxique avec des orchestrateurs spécifiques au kind de nœud
plutôt qu'avec un unique `CSharpSyntaxRewriter`, parce que Stryker doit aussi suivre *où une mutation
peut légalement être placée* (voir §3). Un outil étroit qui mute un nœud à la fois n'a pas besoin de
cette machinerie : un simple `CSharpSyntaxWalker` pour trouver les candidats, et
`SyntaxNode.ReplaceNode` pour en appliquer un, suffisent.

## 3. Instrumentation — les *mutant schemata*, et pourquoi nous n'en avons pas besoin

C'est la décision de conception la plus lourde de conséquences chez Stryker, et celle que
KillMutants ne suit délibérément pas.

Stryker compile **tous les mutants d'un projet dans un seul assembly**. Chaque mutation est émise
comme une branche supplémentaire à côté du code d'origine, gardée par un appel injecté :

- les instructions deviennent `if (MutantControl.IsActive(n)) { muté } else { original }` ;
- les expressions deviennent `(MutantControl.IsActive(n) ? muté : original)`.

À l'exécution, un mutant est sélectionné par un canal auxiliaire — une variable d'environnement pour
la voie VSTest, un fichier mappé en mémoire pour la voie MTP
(`MicrosoftTestingPlatformRunner.cs:129-180`).

Le placement est purement syntaxique ; le modèle sémantique n'est jamais consulté au moment de
l'injection. Une pile de « niveaux de contrôle de mutation » (MemberAccess < Expression < Statement <
Block < Member) permet à une mutation qui ne peut être hébergée à son propre niveau de remonter. Les
mutations qui atteignent le sommet sont abandonnées.

Parce que seul le compilateur peut décider si un mutant injecté est légal, Stryker a besoin d'une
**boucle de rollback** : un processus de 394 lignes qui recompile jusqu'à 50 fois, en retirant les
mutants dont les branches injectées ont cassé le build. S'y ajoutent huit « moteurs
d'instrumentation » réversibles et une comptabilité à base de `SyntaxAnnotation`.

Ce que cela achète, c'est de la compilation. Nous avons mesuré ce coût sur la plateforme cible et il
ne vaut pas la peine d'être payé — voir [ADR-0002](../adr/0002-one-compilation-per-mutant-fr.md).
Compiler un mutant par assembly rend chacun de ces mécanismes inutile : les schemata, `MutantControl`,
le namespace d'aide aléatoire, la pile de niveaux de contrôle, la comptabilité d'annotations, la
boucle de rollback et le canal d'activation à l'exécution disparaissent tous. Un échec d'émission
devient un fait sans ambiguïté sur un mutant, au lieu d'un problème de recherche.

## 4. Compilation

Stryker ne réutilise pas la sortie du compilateur produite par MSBuild. Il lance un *design-time
build* via Buildalyzer puis reconstruit à la main les `CSharpCompilationOptions` et
`CSharpParseOptions` à partir de chaînes de propriétés MSBuild brutes
(`IAnalyzerResultCSharpExtensions.cs:16-108`) : type de sortie, `AllowUnsafeBlocks`,
`CheckForOverflowUnderflow`, contexte nullable, fusion de `NoWarn`/`WarningsAsErrors`, niveau
d'avertissement, analyse de `LangVersion`, et manipulation de chaînes sur la propriété `Features`.

Cette reconstruction est la plus grande source de complexité accidentelle du code. Autour d'elle
gravitent un sous-système de récupération de ressources embarquées à base de Mono.Cecil, un
fournisseur d'options `analyzerconfig` écrit à la main, un chargeur d'assemblys d'analyseurs sur
mesure, et un contournement pour un bug Roslyn corrigé de longue date.

Sur .NET 10, rien de tout cela n'est nécessaire, parce que MSBuild fournit simplement la ligne de
commande `csc` exacte et que Roslyn sait l'analyser. C'est l'objet de
[ADR-0003](../adr/0003-compilation-inputs-from-csc-command-line-fr.md).

La seule chose que Stryker fait exactement bien ici, et que nous reprenons comme *décision* et non
comme code, est l'endroit où va le mutant : les octets mutés sont écrits par-dessus l'assembly du
projet source **à l'intérieur du répertoire de sortie du projet de test**
(`ProjectComponents/TestProjects/TestProjectsInfo.cs:87`), l'original ayant d'abord été mis de côté.
Rien dans les références du projet de test n'est réécrit, parce que le runtime charge simplement
l'assembly qui se trouve à côté de l'assembly de test.

## 5. Exécution des tests

Stryker supporte deux runners derrière une même abstraction : VSTest
(`Stryker.TestRunner.VsTest`, 11 fichiers, ~1 872 lignes, plus un collecteur de données
`netstandard2.0` distinct) et Microsoft Testing Platform
(`Stryker.TestRunner.MicrosoftTestPlatform`, avec un dossier `RPC/` implémentant un client JSON-RPC
contre le mode serveur de MTP). KillMutants ne supporte que les projets fondés sur MTP : tout le bras
VSTest, le collecteur de données et l'abstraction qui existe pour les faire coexister sortent donc du
périmètre.

Le runner MTP de Stryker démarre chaque assembly de test comme un processus persistant
`--server --client-port N`, Stryker étant l'écouteur TCP et l'application de test rappelant en
retour, puis dialogue en JSON-RPC encadré par `Content-Length` : `initialize`,
`testing/discoverTests`, `testing/runTests`, `exit`, avec des notifications `testing/testUpdates/tests`
en flux terminées par une sentinelle `changes: null`.

Deux constats ont justifié de ne pas copier cela. D'abord, en mode serveur l'hôte **sort toujours
avec 0** quels que soient les échecs de test : un client en mode serveur doit donc interpréter les
nœuds diffusés plutôt que le code de sortie. Ensuite, et plus utilement, MTP 2 a acquis deux
capacités que la conception en mode serveur de Stryker précède et n'utilise pas : `--list-tests json`
pour une découverte exploitable par machine, et `--filter-uid` au niveau de la plateforme pour
exécuter exactement un ensemble donné d'UID de tests. Ensemble, elles fournissent la découverte et la
sélection par test — les deux besoins de M4 et M5 — **sans aucun code RPC**, ce qui explique que
[ADR-0004](../adr/0004-run-tests-by-launching-the-test-executable-fr.md) ne considère pas le mode
serveur comme inévitable.

## 6. Couverture et association tests ↔ mutants

Stryker n'utilise aucun outil de couverture : il réemploie l'instrumentation de commutation des
mutants comme sonde de couverture. Chaque site de mutation est déjà gardé par
`MutantControl.IsActive(id)` ; en mode capture, cet appel enregistre l'identifiant et renvoie `false`,
de sorte qu'une exécution supplémentaire révèle quels mutants sont atteignables.

Attribuer *quel test* a atteint un mutant relève ensuite de la plomberie, et c'est là que le coût se
loge. Sur VSTest, un collecteur de données in-process capture et réinitialise la liste à
`TestCaseStart` / `TestCaseEnd`. MTP n'a pas d'équivalent au collecteur : Stryker se rabat donc sur
des variables d'environnement, des fichiers mappés en mémoire et une poignée de main par sondage
(« epoch relay »), en exécutant littéralement un test par requête RPC. Les constructeurs et
initialiseurs statiques sont toute la source de la complexité restante, puisqu'ils s'exécutent une
fois par processus et ne peuvent être attribués à un test unique.

La découverte utile pour M5 est que xUnit 4 offre une primitive bien plus simple : `-automated sync`
est une barrière par test, stricte et sans course — l'hôte se bloque jusqu'à lire un saut de ligne —
ce qui réduit collecteur, fichier mappé en mémoire et poignée de main à un échange « lire le message,
agir pendant que l'hôte est bloqué, écrire un saut de ligne ». KillMutants n'en a nul besoin pour M1,
mais sait désormais sur quoi M5 devra être bâti.

À noter également : notre conception n'exige pas que la sonde de couverture soit *injectée* ; avec un
assembly par mutant, l'atteignabilité peut être établie depuis l'exécution du baseline plutôt que
depuis une instrumentation devant survivre jusque dans le code de production.

## 7. Performance

Le coût naïf est `N mutants × compilation × suite complète`. Stryker attaque le premier facteur
(schemata : une seule compilation pour tous les mutants), le deuxième (sélection des tests par la
couverture, arrêt au premier test en échec) et le troisième (hôtes de test parallèles, et un délai
maximal dérivé de l'exécution de référence pour qu'une mutation introduisant une boucle infinie ne
bloque pas le run).

Nos propres mesures indiquent que le premier facteur est le mauvais à attaquer sur .NET moderne —
voir ADR-0002. L'exécution des tests domine de deux ordres de grandeur : c'est donc la *sélection*
des tests et la parallélisation qui rapportent, et toutes deux s'ajoutent à notre conception sans
exiger qu'elle change.

Trois points de cette partie de l'étude méritent d'être retenus :

- **L'arrêt au premier test en échec n'est implémenté que pour le runner VSTest historique de Stryker
  et est explicitement absent de son runner MTP** (issue ouverte #3655). KillMutants l'obtient
  gratuitement via `-stopOnFail` du runner console xUnit, et l'utilise déjà pour les mutants.
- **La réutilisation à chaud des hôtes de test exige des points de réinitialisation explicites.**
  Stryker remet ses runners dans un pool après chaque travail (`VsTestRunnerPool.cs:95-111`) et doit
  ramener de force ces processus de longue durée à un état propre aux frontières de phase
  (`MicrosoftTestPlatformRunnerPool.cs:96,140`). Cette discipline est un argument direct en faveur de
  notre modèle processus-par-mutant, qui n'en a aucun besoin — voir ADR-0008.
- **Le `--timeout` propre à MTP n'arrête pas de façon fiable un test qui tourne en boucle.** Le délai
  maximal doit être détenu par l'outil, avec `Process.Kill(entireProcessTree: true)` à l'expiration.
  C'est exactement ce que fait KillMutants.

## 8. Ce que nous en avons conclu

**Intrinsèque à tout outil de mutation testing** — obtenir les entrées de compilation du projet à
muter ; savoir quel projet de test l'exerce et où se trouve son répertoire de sortie ; compiler une
fois ; établir un baseline vert et une durée de référence ; analyser, muter et émettre ; placer
l'assembly muté là où l'hôte de test le charge, et restaurer l'original ensuite ; classer le
résultat ; imposer un délai maximal ; rapporter et sortir avec un code significatif.

**Présent uniquement pour des raisons historiques** — VSTest et son collecteur de données ; le canal
d'activation par variable d'environnement ; `packages.config` et les replis de restauration NuGet ;
la découverte de msbuild.exe via vswhere ; les gardes .NET Framework ; la désambiguïsation multi-TFM ;
la correspondance `Configuration|Platform` au niveau solution ; l'énumération `Language` et la forme
générique de l'orchestrateur, vestiges d'un support VB/F# envisagé ; Mono.Cecil, utilisé uniquement
pour lire deux attributs d'assembly destinés au rapporteur dashboard.

**Radicalement simplifiable compte tenu de nos contraintes** — l'analyse de projet (de Buildalyzer à
un seul appel MSBuild), la reconstruction des options (à un seul appel Roslyn), les schemata et le
rollback (à rien du tout), le cadre d'options (de 49 classes à un record), le reporting (de
11 rapporteurs à une écriture console) et le filtrage des mutants (de 13 fichiers à aucun, pour
l'instant).

**Les risques majeurs hérités de cette étude** sont consignés dans
[architecture-fr.md](../architecture-fr.md#6-risques), le plus grave étant les *faux positifs*
causés par une infidélité de la compilation reconstruite plutôt que par la mutation elle-même.
