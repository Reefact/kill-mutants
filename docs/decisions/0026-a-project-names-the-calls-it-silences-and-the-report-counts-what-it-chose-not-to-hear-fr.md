# DEC0026 | Un projet nomme les appels qu'il fait taire, et le rapport compte ce qu'il a choisi de ne pas entendre

## Statut

| Date | Statut | Note | Compte rendu lié |
|---|---|---|---|
| 2026-11-01 | Proposé | Première rédaction | |
| 2026-11-01 | Accepté | À la demande du mainteneur | |

## Contexte

Un projet peut laisser trois choses hors d'une exécution : un fichier ou un projet, avec
`--exclude` ; une famille de mutateurs, avec `--without`, `--with` et `--mutators` ; un membre, avec
`[ExcludeFromCodeCoverage]`. Les trois fonctionnent de la même façon : aucun mutant n'est généré, et
rien dans le rapport ne dit que quelque chose a été laissé de côté. Un mutant qui ne compile pas est
la seule chose que le rapport compte à part : son statut est `CompileError`, son issue `Untestable`,
et il est hors du score, qui vaut `détectés / (détectés + non détectés)`. Le rapport JSON publie ces
trois comptes pour qu'un outil n'ait jamais à redériver ce que vaut chaque statut, et le rapport
JSON est le canal que l'outil offre aux outils
([DEC0023](0023-the-console-progress-is-chosen-by-an-option-and-defaults-to-a-log-that-reads-anywhere-fr.md)).

Chaque fois que l'outil reconnaît quelque chose dans le code, il le fait par le symbole auquel le
code se lie, jamais par un nom. `Statement` laisse tranquille un appel `[Conditional]` en demandant
au compilateur si la méthode porte l'attribut
([DEC0020](0020-a-call-statement-is-removed-by-a-default-family-measured-by-a-statement-probe-fr.md)) ;
`[ExcludeFromCodeCoverage]` est résolu par le modèle sémantique pour qu'un attribut homonyme de
l'utilisateur ne fasse pas taire une classe par accident ; la famille Linq n'échange un appel que
contre un jumeau lié sur le même type
([DEC0024](0024-an-opt-in-linq-family-swaps-a-method-for-its-twin-on-the-same-type-fr.md)).

`Statement` ([DEC0020](0020-a-call-statement-is-removed-by-a-default-family-measured-by-a-statement-probe-fr.md))
a été mesuré seul sur `Reefact/slugger` à `c9cf4a2` ([#42](https://github.com/Reefact/kill-mutants/issues/42)) :
317 mutants, 148 tués, 144 survivants, 25 sans couverture, 32 minutes. Les survivants se rangent en
trois sortes : 116 gardes d'arguments - `ArgumentNullException.ThrowIfNull(x)`,
`ArgumentException.ThrowIfNullOrWhiteSpace(x)`, `ArgumentOutOfRangeException.ThrowIf…(x, …)` -
couvertes par les tests, qui exécutent la ligne sans jamais passer `null` ni une valeur hors
bornes ; 19 appels à `report.Append(...)` écrivant une prose de rapport sur laquelle rien n'affirme ;
et 9 autres, dont des sections entières de rapport et des avertissements dont aucun test ne vérifie la
présence. La décision prise pour cette famille a été de ne rien faire taire dans l'outil, comme la
position du README sur `StringLiteral` le fait déjà : rapporter la répartition plutôt que décider
pour l'utilisateur. Elle laisse à un projet deux choix, grossiers l'un et l'autre : garder les 116
gardes survivantes qui enterrent les 9 trous, ou `--without Statement` et perdre les 9 avec.

Sur `Reefact/first-class-errors` à `8f84258` ([#44](https://github.com/Reefact/kill-mutants/issues/44)),
trois formes ont représenté 66 des 569 survivants : 42 mutants de `ConfigureAwait(false)` en
`ConfigureAwait(true)`, un `BooleanLiteral` dans l'argument de l'appel, 7 % de tous les survivants
([#48](https://github.com/Reefact/kill-mutants/issues/48)) ; 22 suppressions de
`context.EnableConcurrentExecution()`, l'instruction d'appel elle-même, une par analyseur
([#49](https://github.com/Reefact/kill-mutants/issues/49)) ; 2 noms d'`Expression.Parameter` et
d'`Expression.Variable` vidés, un `StringLiteral` dans l'argument de l'appel
([#50](https://github.com/Reefact/kill-mutants/issues/50)). Chacune a été signalée comme un mutant
équivalent que l'outil ne devrait jamais générer. La revue de
[#54](https://github.com/Reefact/kill-mutants/pull/54) a établi que les trois sont observables - un
test peut installer un `SynchronizationContext`, fournir son propre `AnalysisContext`, ou lire la
propriété publique `Name` de l'expression - et qu'il existe trois sortes de survivants : prouvé
équivalent, que l'outil cesse de générer, comme il l'a fait pour un filtre `catch` nommant un type et
son sous-type ; exclu par choix, qu'un projet décide de ne pas entendre ; observable et rapporté, qui
reste un survivant. Les trois formes sont de la troisième sorte, et les trois issues ont été fermées
au profit de cette décision. Deux des trois mutent un argument de l'appel, pas l'instruction.

[DEC0010](0010-a-partial-run-reports-findings-not-a-score-fr.md) a refusé un statut signifiant
« hors du diff », parce que l'état `ignored` de Stryker.NET est une primitive dont le sens est *ceci
ne compte pas*, et qu'une exécution partielle bâtie dessus ampute le dénominateur pendant que
l'étiquette, la formule et la place dans le rapport restent les mêmes. Stryker.NET offre aussi, lu
dans son code et sa documentation, `ignore-methods` - les appels aux méthodes correspondant à un
motif textuel ne sont pas mutés, avec jokers, qualifiés par le type si on veut et `.ctor` pour les
constructeurs, ce qui, note sa documentation, ignore aussi les mutations *à l'intérieur* de l'appel -
et des commentaires `// Stryker disable|restore [once] [familles]` dans la source ; les mutants ignorés
de l'une ou l'autre façon apparaissent dans son rapport comme `Ignored`.

Une série ([DEC0015](0015-a-resumed-run-inherits-the-verdicts-its-series-reached-on-the-same-code-fr.md))
compare `Exclude`, `Mutators`, `Without` et `With` entre deux exécutions comme des ensembles, et
refuse de continuer sur une différence. Toute option qui exprime une habitude a une clé dans
`killmutants.json` ; une liste donnée sur la ligne de commande remplace celle du fichier ; `none`
vide une liste que le fichier remplit. Dans `--exclude`, `*` couvre aussi `/`. L'outil n'a aucune
release : la version est `0.1.0-alpha` et aucun tag n'existe.

Le mainteneur a énoncé la base sur laquelle ceci a été décidé : l'outil ne ment pas, il est
pertinent, et ce n'est pas une usine à gaz. Le mainteneur a décidé de scinder l'issue en deux, ce
record portant la liste d'appels et un record ultérieur les commentaires dans la source, une fois
celui-ci mesuré ; et a tranché les points suivants : un appel reconnu fait taire son instruction et
les expressions qui lui fournissent ses valeurs, pas le corps d'une lambda ou d'une fonction locale
qu'on lui confie ; la reconnaissance passe par le symbole auquel l'appel se lie, contre le nom
pleinement qualifié de la méthode ou du constructeur, avec `*` comme dans `--exclude`, les
constructeurs écrits comme Stryker les écrit ; un mutant tu est généré, pas testé, rapporté comme
`Ignored` hors du score, et compté par famille et par règle, une règle qui n'a rien fait taire étant
nommée aussi ; la ligne du score porte elle-même le compte des mutants ignorés ; une série refuse de
continuer sous une autre liste ; la liste a une option et une clé.

## Décision

Dans ce contexte, nous décidons qu'un projet nomme, avec `--ignore-calls` ou la clé `ignoreCalls`,
les appels dont l'instruction et les arguments ne sont pas mutés - reconnus par le nom pleinement
qualifié de la méthode ou du constructeur auquel l'appel se lie, avec `*` comme dans `--exclude`,
et sans atteindre le corps d'une lambda ou d'une fonction locale confiée à l'appel - que chaque
mutant ainsi tu est généré mais pas testé et rapporté comme `Ignored`, hors du score, compté par
famille et par règle et à côté du score lui-même, une règle qui n'a rien fait taire étant nommée, et
qu'une série refuse de continuer sous une autre liste.

## Justification

Les deux choix grossiers que slugger laissait à un projet sont ce que ceci existe pour remplacer : un
projet peut désormais garder `Statement` et entendre les 9 trous sans que les 116 gardes les
enterrent, et les trois formes de first-class-errors reçoivent la même réponse. Le projet dit ce
qu'il ne veut pas entendre, dans un fichier qu'il versionne avec son code, et l'outil ne décide rien
à sa place - ce que la revue de #54 a tranché en refusant que l'outil cesse de générer des mutants
qu'un test pourrait observer.

Reconnaître l'appel par le symbole auquel il se lie est ce que l'outil fait partout ailleurs, et
pour la même raison : une méthode de l'utilisateur qui partage seulement le nom de `ThrowIfNull`
n'est pas la garde que le projet visait. Écrire le motif contre le nom pleinement qualifié, avec le
`*` que le projet connaît déjà de `--exclude`, garde un seul dialecte de motifs ; un nom nu ne
reconnaît rien, et est nommé dans le rapport comme règle sans effet, si bien qu'un filet large doit
être écrit comme tel. Les constructeurs sont l'appel derrière `throw new ArgumentException("…")`,
dont le message est le bruit `StringLiteral` le plus ordinaire, et la forme que Stryker leur donne
est celle qu'un projet venant de Stryker connaît déjà.

Faire taire les arguments autant que l'instruction est ce que les trois formes de first-class-errors
exigent, deux d'entre elles étant des mutants d'un argument. Un argument ne compte que par ce que
l'appel en fait, si bien qu'un mutant dans l'un n'est observable qu'à travers un appel que le projet
a dit ne pas entendre. Le corps d'une lambda ou d'une fonction locale est autre chose : c'est du code
qu'on confie à l'appel et qui s'exécute plus tard, et ses effets s'échappent de l'appel ; le faire
taire ferait dire à la règle plus que ce que le projet a écrit.

Rapporter un mutant tu comme `Ignored`, compté par famille et par règle, est l'inverse de ce que
DEC0010 a refusé dans l'état de Stryker : là, le dénominateur rétrécissait sans le choix de
l'utilisateur et sans trace ; ici, le projet a choisi, le choix est versionné, chaque compte en est
publié, et une règle qui ne reconnaît plus rien est nommée plutôt que suivie en silence. Mettre le
compte à côté du score lui-même est ce qui empêche le pourcentage de mentir à un lecteur qui ne lit
rien d'autre. Générer le mutant sans le tester est ce qui garde le choix visible sans coût : la
génération est déjà payée par fichier, et une exécution de tests par garde tue est exactement ce que
le projet a demandé à ne pas payer.

Une série qui mélangerait deux listes ne saurait pas dire ce qu'est un mutant tué sous l'une et
ignoré sous l'autre ; comparer la liste comme les exclusions sont comparées, et refuser une
différence, est la règle que DEC0015 applique déjà. Une clé et une option existent toutes deux parce
que toute autre habitude a les deux, la ligne de commande remplaçant la liste du fichier : une règle
sans exception est moins à apprendre qu'une raison pour laquelle cette liste serait différente, et la
question d'un soir - que vaudrait le score sans les gardes - est une question pertinente.

## Alternatives envisagées

### Alternative 1 — Le statu quo : une famille, ou un fichier

* **Description :** Laisser au projet `--without Statement` et `--exclude`, comme aujourd'hui.
* **Raison du rejet :** Les deux sont grossiers. Sur slugger, l'un garde 116 gardes survivantes qui
  enterrent 9 vrais trous, l'autre perd les 9 avec elles.

### Alternative 2 — Cesser de générer ces mutants dans l'outil

* **Description :** Ce que #48, #49 et #50 demandaient : une liste d'API que le catalogue ne mute
  jamais.
* **Raison du rejet :** Les trois mutants sont observables. Un outil qui cesse de générer un mutant
  parce que les tests habituels ne l'attrapent pas décide pour chaque projet ce qu'il aurait dû
  tester.

### Alternative 3 — `[ExcludeFromCodeCoverage]` sur la méthode

* **Description :** Marquer les méthodes qui portent les gardes avec l'attribut que l'outil honore
  déjà.
* **Raison du rejet :** Il fait taire le membre entier, les 9 trous avec les gardes, et rien dans le
  rapport ne dit qu'un membre a été laissé de côté.

### Alternative 4 — Ne pas générer du tout un mutant tu

* **Description :** Traiter un appel reconnu comme `--exclude` traite un fichier : aucun mutant, rien
  de rapporté.
* **Raison du rejet :** Le README rapporte la répartition de `StringLiteral` plutôt que de décider ;
  un mutant tu que personne ne peut compter est un choix que le rapport ne peut pas montrer, et une
  règle périmée continuerait d'être suivie sans qu'on le voie.

### Alternative 5 — Tester quand même les mutants tus, et les rapporter à part

* **Description :** Les générer et les tester, et montrer leurs verdicts sous un titre à eux.
* **Raison du rejet :** Cela paie les 116 exécutions de tests que le projet a demandé à ne pas payer,
  ce qui sur slugger est l'essentiel d'une exécution de 32 minutes.

### Alternative 6 — Reconnaître les appels par un motif textuel sur leur nom

* **Description :** L'`ignore-methods` de Stryker : un motif comparé au nom de la méthode tel qu'il
  est écrit.
* **Raison du rejet :** Une méthode de l'utilisateur qui partage le nom est prise par accident, ce qui
  est la raison pour laquelle l'outil résout tout attribut et tout jumeau par le modèle sémantique.

## Conséquences

### Positives

* Un projet dit, dans un fichier qu'il versionne, ce qu'il choisit de ne pas entendre, plus finement
  qu'un fichier ou une famille, et les vrais trous ne sont plus enterrés sous les gardes.
* Le choix reste visible : `Ignored` par famille et par règle, le compte à côté du score, et une
  règle qui n'a rien fait taire nommée.
* Aucune exécution de tests n'est payée pour un mutant tu.
* #48, #49 et #50 reçoivent une réponse sans que l'outil décide pour le projet.
* Le code confié à un appel - une lambda, une fonction locale - n'est jamais tu par une règle sur
  l'appel.

### Négatives

* Le score monte par le propre choix du projet ; le rapport le dit, et rien d'autre ne le peut.
* Un appel reconnu fait taire l'arithmétique et les littéraux de ses arguments.
* Le rapport JSON gagne `ignored` parmi ses totaux et une liste par règle, qui deviennent un contrat
  une fois l'outil publié ; la ligne de commande et le fichier portent une option et une clé de plus.
* Une série a une cause de refus de plus : une liste qui a changé.

### Risques

* Une équipe fait taire de vrais trous et lit le rapport comme meilleur qu'il n'est.
* Le nom pleinement qualifié d'un symbole, tel que Roslyn l'affiche, peut changer d'une version à
  l'autre - types génériques, types imbriqués - et un motif cesser de reconnaître sans un mot ; la
  règle nommée comme sans effet est ce qui le rattrape.

### Actions de suivi

* Mesurer sur `Reefact/slugger` avec les gardes listées, et confirmer que les 116 passent en
  `Ignored` et que les 9 restent.
* Documenter l'option, la clé et la forme d'un motif, constructeurs compris, dans le `README.md`.
* Décider les commentaires dans la source dans un record à eux, une fois celui-ci mesuré.
* Dire, au moment de le bâtir, comment un mutant `Ignored` s'inscrit dans le plan et le journal d'une
  série.
