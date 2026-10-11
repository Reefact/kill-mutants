# DEC0015 | Une exécution reprise hérite des verdicts que sa série a rendus sur le même code

## Statut

| Date | Statut | Note | Compte rendu lié |
|---|---|---|---|
| 2026-10-11 | Proposé | Première rédaction | |

## Contexte

[DEC0013](0013-a-stopped-run-reports-what-it-judged-and-passes-no-gate-fr.md) permet à une exécution
de s'arrêter à une échéance ou sur un signal et de rapporter les verdicts qu'elle a rendus, sans score
et sans qu'aucune barrière soit passée.
[DEC0014](0014-each-verdict-is-appended-to-a-log-beside-the-report-fr.md) ajoute chaque verdict
définitif, dès qu'il tombe, à un journal JSON Lines placé à côté du rapport JSON, si bien qu'une
exécution tuée net laisse tout de même ce qu'elle a jugé. L'une et l'autre s'arrêtent à la fin d'une
seule exécution. DEC0013 consigne la conséquence en toutes lettres : *« Comme l'ordre est fixe, une
exécution qui s'arrête à chaque fois juge toujours les mêmes premiers mutants, et les projets trouvés
en dernier ne sont jamais atteints, quel que soit le nombre d'exécutions. »* Sur `Reefact/slugger`
([issue #24](https://github.com/Reefact/kill-mutants/issues/24)), le moteur à lui seul compte
816 mutants et prend 88 minutes sur quatre cœurs avec quatre workers, pour un budget de CI de
90 minutes pour l'ensemble du job. Une exécution arrêtée à 80 minutes chaque nuit juge chaque nuit les
mêmes 80 minutes de ce moteur.

Le mainteneur a demandé une exécution qui écrit son plan au démarrage, consigne chaque verdict dès
qu'il tombe de sorte que ce qui a été fait et ce qui reste puissent l'un et l'autre être lus, s'arrête
à son échéance, et laisse une exécution ultérieure reprendre le reste, avec à la fin un seul rapport
consolidé et complet. Le mainteneur a aussi demandé que la série soit nommée par un identifiant passé
par l'appelant — typiquement le hash du commit — plutôt que déduit par l'outil.

Ce que le code offre pour bâtir cela, et ce qu'il n'offre pas :

* Un mutant porte deux identités. `MutantId` est un compteur, sans valeur d'une exécution à l'autre.
  `MutantKey` est un hash du chemin du fichier relatif au répertoire sur lequel l'exécution a été
  pointée, de la ligne, du caractère, de la famille de mutateur et du texte avant et après la
  mutation ; elle est la même à chaque exécution du même code, et elle est publiée dans le rapport et
  dans le journal précisément à cette fin. Elle ne dit rien de tout le reste : ni du reste du fichier,
  ni des autres fichiers, ni des tests, ni de la configuration de build.
* La génération est déterministe pour un checkout donné. Les projets sont découverts dans l'ordre
  ordinal de leur chemin, et les fichiers sont parcourus dans l'ordre où la ligne de commande du
  compilateur de MSBuild les énumère. Rien ne fige cet ordre d'une machine à l'autre, si bien que deux
  exécutions du même code peuvent numéroter différemment les mêmes mutants tout en leur donnant les
  mêmes clés.
* Un verdict dépend de choses qu'aucune clé ne voit. Un commit qui ne touche que des tests change
  quels mutants sont tués. Un changement hors de tout site de mutation — un fichier projet, une
  référence de package, une ligne après la dernière expression mutable — change ce qui est construit.
  `--no-coverage` transforme `NoCoverage` en `Survived`. Un motif `--exclude` retire un projet du côté
  des tests autant que du côté mutable, et une liste donnée sur la ligne de commande remplace celle du
  fichier. La version de l'outil lui-même change la façon dont un verdict est rendu.
* `KillMutants.Git` résout déjà `HEAD` en un commit et détecte un arbre de travail modifié, avec
  `git status --porcelain`, pour l'exécution partielle ; le rapport consigne l'un et l'autre dans
  `comparedTo` et `comparedToIsExact`. `git status` énumère les fichiers non suivis, si bien qu'un
  fichier que l'outil écrit lui-même à côté du rapport compte comme une modification à moins que la
  lecture ne l'exclue. Le cœur n'appelle jamais git : il reçoit des noms et des répertoires par
  `IChangeSource`, et la dépendance va dans un seul sens, à dessein.
* Un dépassement de délai ne devient définitif qu'une fois réexécuté seul, sous un budget dérivé d'un
  baseline mesuré sur cette machine. Le rapport ne publie un budget que pour les projets dont
  l'exécution a elle-même mesuré le baseline, et `TimeBudget` et `RunEnvironment` soutiennent qu'un
  dépassement de délai que personne ne peut expliquer à partir du rapport est précisément la
  défaillance que ces champs existent pour empêcher.
* Un hôte de test qui meurt sans écrire de fichier de résultat est lu comme un plantage, et un
  plantage compte comme un mutant tué, parce que le baseline a prouvé que l'hôte s'exécute sans
  encombre lorsqu'il n'est pas muté. Un verdict rendu pendant que l'exécution s'arrête est écarté,
  mais DEC0013 consigne que cela repose sur l'ordre des événements plutôt que sur une garantie ; et
  quand un runner de CI tue l'arbre de processus, rien n'en parvient jamais jusqu'à l'outil.
* `--verify-kills` reteste un échantillon des mutants tués de la liste qu'on lui remet. Il ne change
  jamais un statut ; un désaccord est consigné par `VerdictLedger.Amend`, qui n'en avertit aucun
  observateur, si bien qu'il existe dans le rapport et jamais dans le journal. Ce dépôt revérifie dix
  mutants tués à chaque exécution, et `RunEnvironment` note qu'une exécution qui n'a vérifié aucun de
  ses mutants tués et une qui les a tous vérifiés se lisent l'une comme l'autre « aucun désaccord ».
* Chaque verdict devient définitif en un seul endroit, `VerdictLedger`, qui consigne ce que l'exécution
  a sélectionné et ce qu'elle a rendu ; un rapport est construit à partir du seul registre.
* `RunEnvironment` consigne déjà la version de xUnit lue dans les assemblies de test construites, et
  la découverte apparie chaque projet sous test aux projets de test qui l'atteignent, directement ou
  à travers un autre projet. Rejuger un mutant coûte un lancement de tests, ou un budget quand il
  dépasse de nouveau son délai. Sur l'exécution de ce dépôt sur lui-même du 7 octobre 2026, un mutant
  sur 797 a dépassé son délai.
* Les codes de sortie sont un contrat public ([DEC0009](0009-exit-codes-are-a-public-contract-fr.md)) :
  une nouvelle cause d'un résultat qu'un code désigne déjà rejoint ce code. Une exécution partielle n'a
  pas de score ([DEC0010](0010-a-partial-run-reports-findings-not-a-score-fr.md)) ; DEC0010 nomme
  *calcul incrémental* le fait d'hériter de résultats d'un commit à l'autre pour des mutants et des
  tests inchangés, une fonctionnalité distincte. Une exécution arrêtée n'a pas de score parce que les
  mutants qu'elle a jugés sont le début de l'exécution, pas un échantillon.
* Sur GitHub Actions, chaque job part d'un espace de travail vierge, si bien que tout ce qui doit
  survivre à un job voyage sous forme d'artefact ou d'entrée de cache. Une exécution planifiée extrait
  la tête de sa branche, qui bouge d'une nuit à l'autre. Une exécution de pull request extrait un
  commit de merge synthétique dont le hash change chaque fois que la branche de base bouge, même sans
  nouveau commit sur la pull request. Les images des runners hébergés sont mises à jour entre deux
  exécutions planifiées.

Trois conceptions ont été confrontées avant la rédaction de cet enregistrement — le plan-et-reprise
décrit par le mainteneur, un découpage sans état de la population en shards fusionnés ensuite, et une
reprise pilotée par le seul journal —, chacune soumise à une revue contradictoire. Les shards
n'écrivent aucun plan et répètent le défaut de l'ordre fixe à l'intérieur de chaque shard ; le journal
seul n'a nulle part où écrire le plan que le mainteneur a demandé, et une ligne d'en-tête y
enfreindrait la règle de DEC0014 selon laquelle chaque ligne du journal est un verdict. Les revues ont
aussi établi qu'une reprise conditionnée à un arbre propre ne peut jamais se déclencher si les propres
fichiers de l'outil comptent comme des modifications, et qu'un mutant tué que l'arrêt lui-même a
provoqué peut atteindre le journal.

Le mainteneur a arbitré : un dépassement de délai n'est jamais hérité ; les désaccords trouvés par les
exécutions précédentes sont transportés ; la reprise se demande explicitement sur la ligne de commande,
sans clé dans `killmutants.json` ni série `--since` dans la première version ; et cette décision et le
fichier de plan font deux enregistrements.

## Décision

Dans ce contexte, une exécution à laquelle on passe `--resume <id>` continue la série de ce nom — en
héritant, du plan et du journal placés à côté de son rapport, de chaque verdict définitif qu'une
exécution antérieure de la série a rendu sur le même commit, le même arbre propre, les mêmes réglages
et le même outil, à l'exception des dépassements de délai et des mutants tués pour lesquels aucun test
n'est nommé —, ne juge que les mutants qui n'en ont encore aucun, et ne rapporte un score qu'une fois
que chaque mutant sélectionné a un verdict.

## Justification

Une série est nommée par l'appelant parce que l'appelant est celui qui sait ce qu'elle est. Un
workflow qui passe le hash du commit fait une série par commit sans aucune condition nulle part ; une
exécution qui ne trouve aucun plan de ce nom commence la série, et une qui le trouve la continue.
Déduire la série d'un plan qui se trouverait là, à côté du rapport, ferait qu'une exécution nocturne
sur une branche calme cesserait de remesurer sans que personne l'ait demandé, et transformerait
l'affirmation du rapport — « la somme de plusieurs exécutions » — en quelque chose qu'un job obtient
sans le savoir.

Le nom n'est pas une preuve. Un mutant tué par des tests que cette exécution ne contient pas est la
pire chose que cet outil puisse afficher, et une clé ne peut voir ni les tests, ni la configuration,
ni les exclusions, ni le reste du code. Le nom ouvre donc la série et l'état du code décide si elle
peut continuer : le même commit avec un arbre propre est l'identité que le rapport garantit déjà pour
une exécution partielle, et les réglages, la version de l'outil, la population effectivement générée,
le graphe de projets et la version de xUnit lue dans les assemblies construits sont autant de choses
que l'exécution calcule de toute façon, si bien que les comparer ne coûte rien. Une différence sous le
même nom est refusée en nommant la différence, plutôt que de repartir de zéro en silence — le journal
peut contenir des heures de verdicts, et le vider pour une cause que l'utilisateur n'a pas créée n'est
pas à l'outil d'en décider. Un autre nom, ou l'absence de plan, est une nouvelle série, ce qui permet
à un workflow de toujours passer la même option.

L'arbre est lu comme propre en laissant de côté les trois fichiers de l'outil lui-même, parce qu'ils
sont écrits à côté du rapport avant l'exécution et qu'un job de CI les restaure à cet endroit ;
comptés comme des modifications, ils rendraient la série incapable de continuer dans le seul
déploiement pour lequel elle existe. Le runtime, le système d'exploitation et le nombre de processeurs
sont consignés pour chaque exécution et comparés par personne : une image de runner avance entre deux
nuits d'une série épinglée, et bloquer là-dessus laisserait la série coincée sous un nom que personne
ne peut changer par un commit.

Un dépassement de délai est jugé de nouveau plutôt qu'hérité parce qu'il n'est définitif que sous le
budget de l'exécution qui l'a confirmé, et que le rapport ne peut publier que les budgets que cette
exécution a mesurés. Rejuger coûte un budget par dépassement de délai et garde chaque dépassement de
délai d'un rapport consolidé explicable à partir de ce rapport. Un mutant tué sans test nommé est jugé
de nouveau parce qu'un plantage est le seul verdict que l'arrêt lui-même peut fabriquer, que la mise à
l'écart qui en protège repose sur l'ordre des événements, et qu'un arbre de processus tué par un
runner ne se fait jamais entendre. Le rejuger coûte une réexécution par mutant de ce genre.

Le score est légitime sur une série terminée là où DEC0013 l'a refusé : la population est chaque
mutant sélectionné de la base de code à un état nommé, jugé en plusieurs exécutions, ce qui est la
base de code et non le début de quoi que ce soit. Jusque-là, une exécution intermédiaire est
exactement une exécution arrêtée, et les codes de DEC0013 s'appliquent sans changement — `1` quand une
barrière était demandée, `0` quand aucune ne l'était — parce qu'une barrière dont on n'a pas encore
montré qu'elle passe est la lecture véridique, et qu'une exécution soumise à une barrière qui sort
avec `0` sans avoir établi la réponse est la direction silencieuse que DEC0009 interdit.

Les désaccords voyagent dans le plan plutôt que dans le journal parce que le journal contient des
verdicts et rien d'autre, et qu'`Amend` consigne déjà sans annoncer. L'exécution qui termine une
série répartit son échantillon `--verify-kills` sur les mutants tués hérités des projets dont elle
mesure encore le baseline, parce qu'un rapport qui affiche un score à côté de « aucun mutant tué
revérifié » est l'état que `RunEnvironment` qualifie de dénué de sens.

Les séries `--since` attendent un enregistrement qui leur soit propre : leur identité serait les
révisions comparées, et sur une pull request le commit de merge extrait bouge chaque fois que la base
bouge, si bien que le cas demande ses propres faits plutôt qu'une note en bas de page ici.

## Alternatives envisagées

### Alternative 1 — Garder DEC0013 et DEC0014 en l'état

* **Description :** une exécution arrêtée rapporte ce qu'elle a jugé et laisse son journal ;
  l'exécution suivante repart de zéro.
* **Pourquoi écartée :** un projet plus grand qu'un budget n'est jamais terminé, et chaque nuit juge
  le même début — la conséquence que DEC0013 a consignée et que cet enregistrement existe pour
  supprimer.

### Alternative 2 — Reprendre implicitement dès qu'un plan correspondant se trouve à côté du rapport

* **Description :** aucune option ; une exécution qui trouve un plan pour le même état du code le
  continue.
* **Pourquoi écartée :** une exécution nocturne sur une branche inchangée cesserait en silence de
  remesurer, ce qui défait ce que `selfcheck.yml` dit être sa raison d'être — un mutant tué qui cesse
  de se reproduire —, et un rapport qui est la somme de plusieurs exécutions est une affirmation
  qu'un job doit faire en connaissance de cause.

### Alternative 3 — Hériter entre commits des verdicts dont le mutant et ses tests sont inchangés

* **Description :** comparer le site muté et les tests qui l'atteignent entre deux commits, et garder
  le verdict quand les deux sont identiques.
* **Pourquoi écartée :** c'est le calcul incrémental que DEC0010 a mis de côté, avec un problème
  d'identité qui lui est propre — ce que « inchangé » veut dire pour un test qui appelle du code
  changé —, et le construire sous le nom d'une reprise mélangerait deux populations dans un même
  rapport.

### Alternative 4 — Découper la population en shards et fusionner leurs rapports

* **Description :** `--shard i/n` juge une part déterministe des mutants dans chacun de n jobs
  parallèles, et une étape de fusion consolide les n rapports.
* **Pourquoi écartée :** elle n'écrit aucun plan et ne garde aucun reste, si bien qu'un shard arrêté à
  son échéance chaque nuit répète le défaut de l'ordre fixe à l'intérieur de sa part ; et sa fusion
  calcule le score sur une empreinte des sites de mutation, qui ne peut voir ni un changement de test,
  ni une configuration, ni une exclusion. Elle répond à la question d'aller plus vite, pas à celle de
  finir.

### Alternative 5 — Hériter des dépassements de délai et porter dans le plan les budgets de chaque exécution

* **Description :** garder un dépassement de délai confirmé par une exécution antérieure, et consigner
  à côté de lui le budget sous lequel il a été jugé.
* **Pourquoi écartée :** les budgets du rapport consolidé ne décriraient alors que l'exécution qui l'a
  écrit, et un lecteur ne trouverait le budget derrière un dépassement de délai hérité qu'au travers
  d'une seconde structure. Un mutant sur 797 a dépassé son délai sur l'exécution de ce dépôt sur
  lui-même, et en rejuger un coûte un budget.

### Alternative 6 — Faire tourner l'ordre pour que chaque projet soit atteint au fil de plusieurs nuits

* **Description :** commencer chaque exécution là où la précédente s'est arrêtée, sans rien hériter.
* **Pourquoi écartée :** un projet plus grand qu'un budget n'est toujours jamais terminé, et aucune
  exécution n'a jamais une population qu'un score puisse décrire.

## Conséquences

### Positives

* Un projet qui ne tient pas dans un budget de CI est terminé au fil de plusieurs exécutions, et les
  projets trouvés en dernier sont atteints.
* Une série terminée a un score et répond à une barrière, sur chaque mutant sélectionné de la base de
  code à un état nommé.
* Une exécution tuée net laisse son plan et son journal, si bien que ce qui reste peut être lu sans
  l'outil.

### Négatives

* Une série vit sur un seul commit avec un arbre propre. Une exécution nocturne qui suit une branche
  en mouvement n'en termine jamais une à moins que son workflow n'épingle le checkout sur le commit de
  la série jusqu'à ce que la série soit terminée. Un arbre modifié ne reprend jamais, même à contenu
  identique.
* Une série avec `--break-at` sort avec `1` à chaque exécution intermédiaire, par la règle de DEC0013.
* Une exécution reprise repaie la découverte, le build, l'analyse, et le baseline et la couverture de
  chaque projet qui a encore un reste. Un projet dont les dépassements de délai ne sont jamais
  confirmés avant l'échéance les juge de nouveau à chaque exécution.
* Une exécution qui termine une série peut elle-même exécuter peu de tests, puisqu'un projet dont il ne
  reste rien n'est ni soumis au baseline ni mesuré ; le rapport dit combien de verdicts elle a hérités
  et de combien d'exécutions.
* `--resume` a besoin de `--report-json`, parce que la série vit à côté du rapport, et d'un dépôt git,
  parce que l'état du code y est lu.
* Une exécution au même chemin de rapport qui ne nomme aucune série, ou en nomme une autre, remplace
  le plan et vide le journal : une série ne partage son chemin de rapport avec rien d'autre.
* Deux mutants qui partagent une clé sont tous deux rejugés, puisqu'une ligne du journal ne peut pas
  dire duquel elle parle.

### Risques

* Un mutant tué par un test instable, dépendant de l'ordre ou sensible à la machine lors d'une
  exécution est hérité par le reste de la série ; seuls l'échantillon `--verify-kills` de cette
  exécution-là et l'échantillon de l'exécution qui termine la série sur les mutants tués hérités
  peuvent l'attraper.
* Le commit identifie les sources, pas les dépendances restaurées : une version de package flottante
  ou un SDK qui a avancé peut changer les tests entre deux exécutions d'un même commit. La version de
  xUnit lue dans les assemblies construits est comparée ; rien d'autre du graphe restauré ne l'est.
* Deux builds de l'outil à partir de codes différents peuvent porter la même chaîne de version.
* Le plan est réécrit pendant l'exécution, et une écriture qui échoue pendant qu'un arrêt est en cours
  est mise sur le compte de l'arrêt, si bien qu'un désaccord trouvé par cette exécution ne peut
  survivre que dans son propre rapport arrêté.

### Actions de suivi

* Documenter la forme du workflow pour `Reefact/slugger` : un checkout épinglé sur le commit de la
  série jusqu'à ce que le plan n'ait plus de reste, le plan et le journal transportés d'une nuit à
  l'autre, et `--stop-after` sous le budget du job.
* Envisager une identité par le contenu pour un arbre modifié, afin qu'une exécution interrompue sur
  un portable puisse continuer le lendemain matin.
* Consigner sur quoi une série `--since` serait indexée, dans un enregistrement qui lui soit propre.
* Envisager de transporter la carte de couverture dans le plan, afin qu'une exécution reprise ne paie
  pas de nouveau la passe de couverture pour un projet qui a un reste.
