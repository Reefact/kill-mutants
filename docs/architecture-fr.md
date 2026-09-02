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
| 60 mutants, suite lente — suite entière vs sélection par couverture | 29,3 s vs 22,6 s |
| Lancement de l'hôte de test vs le test lui-même | ~0,5 s vs ~0,12 s |
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
   [DEC0005](decisions/0005-verify-the-baseline-before-mutating-fr.md).
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
| Filtrage du périmètre | `KillMutants.Filtering` | Ce qu'un run laisse tranquille (`--exclude`) |
| Génération des mutations | `KillMutants.Mutations` | Parcourt les arbres, produit les candidats |
| Catalogue des mutateurs | `KillMutants.Mutations.Mutators` | `IMutator` et ses implémentations |
| Représentation d'un mutant | `KillMutants.Mutations` | `Mutant`, `MutantId`, `MutantStatus`, `SourceLocation` |
| Instrumentation | *(absorbée)* | Voir ci-dessous |
| Compilation | `KillMutants.Compilation` | Émet un assembly muté |
| Découverte des tests | `KillMutants.Testing` | `-list tests /json`, par nom (DEC0006) |
| Exécution des tests | `KillMutants.Testing` | `ITestRunner`, `TestRunOutcome` |
| Spécificités xUnit 4 / MTP 2 | `KillMutants.Testing.XUnit` | Le seul endroit qui connaît la CLI du runner |
| Association tests ↔ mutants | `KillMutants.Coverage` | Sonde préservant le type, une exécution par test (DEC0007) |
| Orchestration | `KillMutants.Execution` | La liste de phases, courte et linéaire |
| Résultats | `KillMutants.Reporting` | `MutationTestReport`, écritures console et JSON, progression |
| CLI | `KillMutants.Cli` | `dotnet killmutants`, seuils et codes de sortie (DEC0009) |

**Le catalogue, à M9.** Onze familles, chacune un `IMutator` distinct, chacune avec ses propres
tests et chacune exercée de bout en bout contre un vrai projet de fixture.

| Famille | Réécrit | En |
|---|---|---|
| `Comparison` | `>=` `>` `<=` `<` | le décalage de borne et la négation |
| `Comparison` | `==` `!=` | la négation seule — il n'y a pas de borne à décaler |
| `LogicalOperator` | `&&` `\|\|` | l'un l'autre |
| `Arithmetic` | `+` `-` `*` `/` `%` | son homologue |
| `Bitwise` | `&` `\|` `^` `<<` `>>` | son homologue |
| `Assignment` | `+=` `-=` `*=` `/=` `%=` `&=` `\|=` `<<=` `>>=` | son homologue |
| `Increment` | `++` `--` | l'un l'autre, en préfixe comme en suffixe |
| `Conditional` | `c ? a : b` | `c ? b : a` |
| `NullCoalescing` | `a ?? b` | `a` |
| `BooleanLiteral` | `true` `false` | l'un l'autre |
| `Negation` | `!x` | `x` |
| `StringLiteral` | `"texte"` | `""`, et `""` en une chaîne non vide |

**Ce qu'il ne mute délibérément pas.** Le catalogue est sélectif et non exhaustif : chaque mutant
coûte une exécution de tests, donc un opérateur gagne sa place au signal qu'il porte. L'inventaire
ci-dessous a été mesuré en passant le catalogue sur chaque forme, pas lu dans la spécification.

| Non muté | Décision |
|---|---|
| `>>>`, `>>>=` | **Candidat futur.** Fort signal : décalage signé et non signé ne diffèrent que pour les valeurs négatives, exactement le cas qu'une suite de tests oublie. |
| `^=` | **Candidat futur.** `^` est muté mais pas sa forme composée : c'est une incohérence, pas une décision. |
| Motifs relationnels (`is > 3`) | **Candidat futur**, et grandissant : c'est le jumeau de la famille `Comparison` pour du code écrit en motifs. |
| Littéraux numériques | **Candidat futur.** Classique et à fort signal, mais assez bruyant pour mériter une activation explicite plutôt qu'une place par défaut. |
| `-x` | **Candidat futur**, en dessous des autres : la plupart des erreurs de signe sont déjà atteignables par la famille arithmétique. |
| `+x` | **Non supporté.** Retirer un plus unaire ne change rien : le mutant est équivalent par construction et ne peut jamais être tué. |
| `~x` | **Non supporté pour l'instant.** Le retirer change la valeur si radicalement que tout test touchant l'expression le tue ; le mutant est presque gratuit à écrire et presque sans valeur. |
| `?.`, `as` | **Non supportés.** Les deux mutent vers des formes qui lèvent plutôt qu'elles ne calculent : elles mesurent si un test touche la ligne, pas s'il en vérifie le résultat — et `?.` ne compile souvent même plus une fois le chemin null retiré. |
| `is T` | **Non supporté pour l'instant.** À revoir avec les motifs relationnels ci-dessus, comme une seule famille consciente des motifs plutôt que deux règles. |
| Bras de `switch` | **Non supportés.** Réordonner ou supprimer un bras est une mutation structurelle, pas une mutation d'opérateur, et demande un autre raisonnement sur l'exhaustivité. |

Trois propriétés valent pour toutes. Chaque remplacement est un **nouveau nœud du bon type**, pas un
jeton échangé ([RB-001](robustness-backlog-fr.md)). Chacune demande au compilateur si le remplacement
se lierait avant de le proposer : un mutant qui ne compile pas n'est jamais engendré
([RB-011](robustness-backlog-fr.md)). Et chaque famille qui pourrait produire un mutant au
comportement identique à l'original s'en abstient : `NullCoalescing` ne garde que l'opérande gauche,
jamais le droit, de sorte qu'aucun effet de bord n'est silencieusement supprimé, et `Conditional`
laisse tranquille un ternaire dont les deux branches sont la même expression.

**L'instrumentation n'a aucun code propre, et c'est voulu.** Parce que chaque mutant reçoit sa propre
compilation ([DEC0002](decisions/0002-one-compilation-per-mutant-fr.md)), « instrumenter » un mutant se
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
- `MutantStatus` — ce qu'est devenu un mutant : `Killed`, `Survived`, `Timeout`, `NoCoverage`,
  `CompileError`.
- `MutantOutcome` — ce que cela *vaut* : `Detected` (`Killed`, `Timeout`), `Undetected` (`Survived`,
  `NoCoverage`), `Untestable` (`CompileError`). Séparé du statut à dessein, pour qu'aucun rapporteur
  ni aucun seuil ne décide pour lui-même de ce que signifie un timeout ou un mutant non couvert.
- `MutationScore` — un type valeur qui sait se calculer et se rendre, de sorte qu'aucun appelant ne
  divise deux entiers et ne formate un pourcentage à la main. Il vaut
  `Detected / (Detected + Undetected)`. Seuls les mutants non testables sont exclus, et seulement
  parce que la suite n'a jamais été interrogée à leur sujet : un mutant que rien ne couvre *est* non
  détecté, et l'exclure signifierait qu'un projet peut augmenter son score en ajoutant du code
  qu'aucun test ne touche.

## 6. Risques

Classés par dommage attendu, d'après l'étude et nos propres sondes.

**Critique — faux positifs dus à une infidélité de compilation.** Si la compilation reconstruite
diffère du build réel d'une quelconque manière (un `AssemblyInfo.cs` généré manquant qui change la
version de l'assembly, une référence absente, un symbole de préprocesseur erroné), les tests échouent
pour des raisons étrangères à la mutation et tous les mutants sont rapportés `Killed`. Un outil de
mutation testing qui répond toujours « Killed » est pire que pas d'outil du tout, parce qu'il est
silencieusement rassurant. *Atténué par le DEC0005 : le baseline est émis par le même chemin et doit
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

**Élevé — mutations et instrumentation qui cassent l'affectation définie.** Une variable de motif ou
`out` n'est définie que *conditionnellement*. Muter l'expression qui la déclare change le moment où
ses parties sont évaluées, et envelopper cette expression dans la sonde de couverture fait passer cet
état par un appel de méthode ; dans les deux cas le projet cesse de compiler. Découvert en quelques
secondes en exécutant l'outil sur son propre code source, où les clauses de garde de cette forme sont
partout. *Atténué en ne mutant pas une expression qui déclare une variable, ce qui la retire du même
coup des sites d'instrumentation — [RB-016](robustness-backlog-fr.md).*

**Moyen — un site dont la sonde ne peut pas accepter la valeur.** `Hit<T>` ne peut pas prendre un ref
struct, un pointeur ni `void` comme argument de type, et une expression conditionnelle sur deux
`Span<T>` a exactement ce type. *Atténué en n'instrumentant pas ces sites et en testant leurs mutants
contre la suite complète — [RB-017](robustness-backlog-fr.md).*

**Moyen — mutations introduisant une boucle infinie.** Hors d'atteinte pour l'unique mutateur du
milestone 1, mais le délai maximal doit exister avant que le catalogue ne grossisse. *Reporté en M2,
la durée du baseline étant déjà enregistrée pour en dériver le budget.*

## 7. Position dans la roadmap

Le milestone 1 comprenait un couple de projets, un mutateur (`>=` devient `>`), un mutant, exécuté
pour de vrai. M2 a étoffé le catalogue à six familles, M9 à onze. M3 traite les structures de
solution réelles : plusieurs projets de test, plusieurs projets à muter, les références de projet
suivies transitivement, et un framework épinglé par projet. M6 teste les mutants en parallèle, chaque worker dans une copie privée du
répertoire de sortie des tests. M4 et M5 découvrent les tests et mesurent lesquels atteignent quels
mutants : les mutants non couverts ne sont jamais exécutés, et les autres n'exécutent que ce qui peut
les tuer. M7 rapporte : progression en direct, constats groupés par fichier, et un rapport JSON pour
tout ce qui n'est pas un humain. M8 en fait une barrière de qualité utilisable : un seuil optionnel
`--break-at` et des codes de sortie qui séparent une suite de tests faible d'un run cassé. M9
porte le catalogue d'opérateurs à onze familles — sélectif et non complet, les omissions
ci-dessus étant décidées et non oubliées. M10 en fait un outil et non plus un moteur : empaqueté en
`dotnet killmutants`, doté du `--exclude` qu'exige un vrai dépôt, et — c'est le cœur du milestone —
exécuté sur le code source de KillMutants lui-même, ce qui a fait apparaître RB-016 en quelques
secondes. M11 rend sa sortie exploitable : ce que chaque famille de mutateurs a coûté et attrapé, les
`--mutators` et `--without` qui agissent dessus, et `[ExcludeFromCodeCoverage]` respecté. M12 permet à
un projet de garder ces choix dans `killmutants.json` plutôt que dans une commande shell : le
catalogue derrière un score est ainsi versionné avec le code qu'il a noté.

**Ce que mesure l'exécution sur lui-même.** 384 mutants sur `KillMutants.Core`, 6,8 minutes sur
quatre cœurs : 106 tués, 111 survivants, un tué par timeout, 166 non couverts, aucun en échec de
compilation — un score de mutation de 27,86 %. Deux chiffres méritent une réserve plutôt qu'un titre.
Les mutants non couverts sont en grande partie un artefact de la configuration du run : il exclut la
suite de bout en bout, qui est précisément ce qui exerce l'essentiel du code de découverte, d'analyse
et d'exécution ; ces mutants sont donc non couverts *par la suite exécutée*, pas non testés. Et parmi
les survivants, une part importante sont des mutants `StringLiteral` sur des messages d'erreur que
rien n'asserte — des constats vrais, mais les moins utiles par unité de temps d'exécution, et la
première indication de là où le catalogue gagne réellement sa place sur du vrai code. Le score est
rapporté ici comme une mesure, pas comme un verdict sur la suite de tests.

**Où passe réellement le temps d'un run.** Mesuré sur `KillMutants.Core`, quatre cœurs, 384 mutants,
72 méthodes de test, 6,8 minutes de bout en bout :

| Phase | Temps | Part |
|---|---|---|
| Découverte de cinq projets | 2,2 s | 0,5 % |
| Build des projets de test | 3,1 s | 0,8 % |
| Lecture des lignes de commande du compilateur et construction des compilations | 5,2 s | 1,3 % |
| Vérification du baseline | 22,1 s | 5,5 % |
| Mesure de la couverture, une exécution par méthode de test | 73,9 s | 18,2 % |
| Test des mutants | 299,0 s | 73,7 % |

Deux conclusions en découlent, et toutes deux sont des raisons de *ne rien* changer pour l'instant. La
mesure de couverture n'est pas le goulot d'étranglement à cette échelle : elle coûte un lancement de
processus par test, environ 1,0 s ici, et permet d'écarter d'emblée 166 mutants non couverts. Et elle
croît avec le nombre de *tests* quand la phase des mutants croît avec le nombre de mutants : la
stratégie cesse donc d'être rentable seulement lorsque les tests dépassent largement les mutants — une
suite de mille tests passerait dix-sept minutes à mesurer. C'est le chiffre à surveiller, et tant
qu'un vrai projet ne l'atteint pas, l'attribution exacte qu'achète une exécution par test vaut plus
que le temps qu'un schéma plus malin ferait gagner. Voir
[DEC0007](decisions/0007-measure-coverage-with-a-type-preserving-probe-fr.md).

**Quelles familles valent leur temps, et qui en décide.** Les onze familles ne portent pas un signal
équivalent, et exécuter l'outil sur lui-même a mesuré l'écart : `Comparison`, `LogicalOperator` et
`Arithmetic` détectent 45 % à 55 % des mutants qu'elles produisent, tandis que `StringLiteral` et
`BooleanLiteral` représentent à elles deux la moitié des mutants engendrés et en détectent 10 % à
15 % — messages d'erreur et drapeaux que rien n'asserte. La moitié du coût du run pour un tiers de
ses survivants.

Ce n'est pas une raison de les supprimer : un mutant `StringLiteral` survivant est un constat vrai,
et utile sur un projet qui asserte sur ses messages. C'est une raison de *rapporter la répartition*
et de laisser l'utilisateur agir dessus : c'est à cela que servent `--mutators` et `--without`. Le
rapport indique ce que chaque famille a coûté et attrapé, de sorte que le choix se fait sur les
chiffres du projet et non sur ceux de celui-ci. Les retirer ici fait passer le run de 413 mutants en
7,1 minutes à 207 en 4,1, et les survivants à lire de 129 à 62.

**Ce qui mérite d'être dit franchement :** un score n'est comparable qu'à un score issu du même
catalogue. Ce même changement fait passer le chiffre de 28,81 % à 43 %, et les tests ne se sont pas
améliorés — une autre question a été posée. Un job de CI devrait choisir un catalogue et s'y tenir ;
le rapport JSON liste les familles réellement exécutées, de sorte qu'un consommateur peut savoir à
quelle question il a été répondu.

M11 cesse également de muter tout ce qui porte `[ExcludeFromCodeCoverage]`. L'attribut est une
déclaration d'intention — ce code ne fait pas partie de ce que les tests sont censés couvrir — et
comme les mutants non couverts comme survivants pèsent tous deux sur le score, l'ignorer n'encombrait
pas seulement le rapport : cela déplaçait le chiffre.

**Où vivent les habitudes d'un projet.** M12 lit `killmutants.json` dans le répertoire visé par le
run. Chaque réglage a son équivalent en option de ligne de commande, et tout ce qui est donné en ligne
de commande l'emporte : le fichier énonce l'habitude, la ligne de commande énonce l'exception. Sa
raison d'être est le paragraphe ci-dessus : un score ne signifie quelque chose que rapporté aux
familles qui l'ont produit, donc un job qui choisit son catalogue dans une commande shell a inscrit
dans ses logs un chiffre que personne ne peut reproduire. Le garder à côté du code versionne la
question en même temps que la réponse.

Deux règles gagnent leur place. Une clé mal orthographiée arrête le run au lieu d'être ignorée — le
même refus que la ligne de commande oppose à une famille de mutateurs mal orthographiée, et pour la
même raison. Et chaque option de ligne de commande est analysée comme *nullable* :
`--configuration Release` et ne rien dire doivent être distingués, sinon les valeurs par défaut
l'emporteraient silencieusement sur un fichier qu'elles n'ont jamais mentionné.

**Deux flux de sortie, délibérément.** La progression part sur la sortie d'erreur et le rapport sur la
sortie standard : `killmutants > rapport.txt` capture donc le rapport sans y mêler la ligne de
progression. Sur un terminal cette ligne est réécrite sur place ; quand le flux est redirigé, chaque
phase est annoncée une fois, parce que des milliers de retours chariot dans un journal de CI
n'aident personne.

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
  renforce le DEC0002 au lieu de le fragiliser : le terme qu'optimiseraient les schemata rétrécit à
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
