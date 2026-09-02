# DEC0010 | Une exécution partielle rapporte des constats, pas un score

## Statut

| Date | Statut | Note | Compte rendu lié |
|---|---|---|---|
| 2026-09-02 | Accepté | | |

## Contexte

`--since` n'exécutera que les mutants qu'un changement touche. C'est la fonctionnalité qu'on réclame
en premier, parce qu'un balayage complet de ce dépôt prend des minutes et qu'un diff prend des
secondes — c'est elle qui rend le test de mutation utilisable sur une pull request plutôt qu'une fois
par nuit. Ce qu'une telle exécution sélectionne relève du
[DEC0011](0011-widen-a-partial-run-selection-when-a-test-file-changes-fr.md) ; ce qu'elle a le droit
d'afficher se décide ici.

Toutes les autres exécutions de cet outil se terminent par un score de mutation.

Le score vaut `détectés / valides`. Ce qui rend deux exécutions complètes comparables, ce n'est pas
qu'elles jugent les mêmes mutants — elles ne le font pas, puisque le code lui-même change d'un commit
à l'autre — mais qu'elles appliquent la même *règle de portée* : tout site mutable du code
sélectionné. La population bouge ; la question à laquelle le nombre répond, elle, ne bouge pas, si
bien qu'un mouvement du nombre est une affirmation sur la suite de tests. Une exécution partielle a
une règle elle aussi — les sites que son changement touche — et il serait trop commode de prétendre le
contraire. La différence tient à ce sur quoi la règle est ancrée. La portée d'une exécution complète,
c'est le dépôt, qui est le même objet d'une fois sur l'autre ; celle d'une exécution partielle est
définie contre une révision de base choisie à chaque exécution, si bien que sa population n'est pas
seulement différente à chaque fois : elle l'est *par construction*, sans aucune relation entre celle
d'une exécution et celle de la suivante.

Le nombre serait aussi trop grossier pour servir de barrière. Deux détectés et un non détecté
s'affichent « 66,67 % » — épinglé par `MutationScoreTests` — et c'est un rapport exact, pas une mesure
bruitée : parler de fausse précision serait faux. **Le problème est la granularité.** Sur trois
mutants, un verdict déplace le score de 33,3 points : tous les seuils entre 34 % et 66 % veulent donc
dire la même chose, et aucun ne peut exprimer « légèrement moins bien ».

Un piège que nous n'avons *pas*, et qu'il vaut la peine d'écrire parce que la version évidente de cet
argument s'y trompe. Un diff vide ne produit pas ici de titre rassurant : `MutationScore.IsUndefined`
est vrai quand rien n'a été jugé, `ToString` rend `n/a`, et le
[DEC0009](0009-exit-codes-are-a-public-contract-fr.md) fait déjà échouer un seuil sur un score
indéfini.

Le comportement de Stryker.NET est lu dans leur documentation, pas dans leur code. Leur score vaut
`detected / valid * 100`, où `valid` = killed + timeout + survived + no coverage. Les états `ignored`,
`compile error` et `runtime error` sont hors dénominateur, et de `ignored` la documentation dit :
« This will not count against your mutation score but will show up in reports. » L'état lui-même est
défini comme « The mutant wasn't tested because it is ignored. Either by user action, **or for another
reason**. » `since` passe par cette dernière clause ; leur documentation de configuration dit
« Stryker will only report on mutants within the changed code. All other mutants will not have a
result. » Une exécution partielle est donc bâtie sur une primitive dont le sens est déjà *ceci ne
compte pas*, et le dénominateur devient discrètement le diff pendant que l'étiquette, la formule et la
position dans le rapport restent en place. Ils ont ensuite livré une **seconde** fonctionnalité,
`with-baseline`, pour défaire cela — « provid[ing] you with a full report after a partial mutation
testrun » — et les deux sont mutuellement exclusives.

Une note d'honnêteté : que les mutants hors diff atterrissent précisément dans l'état `ignored` vient
d'une discussion avec un mainteneur, pas de la documentation de référence, qui dit seulement « will
not have a result ». Les deux lectures amputent le dénominateur, donc le raisonnement tient dans les
deux cas — mais c'est à l'affirmation la plus faible que nous avons droit.

`Program.Verdict` renvoie déjà `1` pour la version complète d'un résultat non concluant — un score
indéfini face à un seuil, avec *« No mutant could be tested, so the N% threshold cannot be shown to be
met »* sur la sortie d'erreur. `RunSettings` résout `options.Threshold ?? file?.BreakAt` : un seuil
peut donc arriver de `killmutants.json` autant que de la ligne de commande.

## Décision

Dans ce contexte, nous n'affichons aucun score de mutation pour une exécution partielle — seulement le
décompte de chaque statut, les nouveaux mutants non détectés nommés avec de quoi reproduire chacun
d'eux, et un verdict binaire qui échoue sur l'un quelconque d'entre eux comme sur une sélection dont
aucun mutant n'a pu être testé —, nous n'engendrons aucun statut signifiant « hors du diff », nous
consignons dans le rapport le mode d'exécution et les révisions de base et de tête résolues, et nous
refusons un seuil d'où qu'il vienne.

## Justification

La population d'une exécution partielle est différente *par construction* d'une fois sur l'autre, sans
aucune relation entre elles. Affichez 72 % mardi et 40 % mercredi sous un seul nom, une seule formule
et une seule place dans le rapport, et le lecteur tirera une tendance de deux nombres qui ne partagent
que leur unité. Un pourcentage là-dessus est une réponse parfaitement sensée à *dans quelle mesure la
suite s'est-elle bien comportée sur ce changement ?* — et aucune réponse à une question qui enjambe
deux exécutions.

La granularité l'écarte comme barrière indépendamment de cela. Un pourcentage exige une population
assez grande pour bouger par pas plus petits que la décision qu'il éclaire, et un diff, couramment,
n'en est pas une.

La question à laquelle répond une exécution partielle est binaire, et c'est pourquoi le verdict l'est.
Une exécution complète demande *cette suite de tests vaut-elle quelque chose ?* ; une exécution
partielle demande *la portée sélectionnée a-t-elle produit un mutant non détecté ?* — la portée étant
celle du DEC0011, et non, malgré la tentation de le dire, toute manière dont un changement pourrait
introduire du comportement non testé. La question la plus étroite est celle à laquelle l'exécution sait réellement
répondre, et un document sur le fait de ne pas surpromettre doit la poser dans les termes qu'il peut
défendre. Aucune des deux n'a un pourcentage pour réponse, mais seule la première y aurait droit.

Le verdict échoue sur tout nouveau mutant *non détecté* plutôt que sur les seuls survivants. Un mutant
qu'aucun test n'atteint est `NoCoverage`, pas `Survived`, et un changement qui ajoute du code que rien
ne teste produit exactement ceux-là ; un portillon ne lisant que les survivants laisserait passer le
cas le plus flagrant de comportement non testé nouvellement introduit, c'est-à-dire la seule chose que
cette exécution existe pour attraper. `MutationScore` compte déjà les deux comme non détectés, et le
DEC0007 tient l'absence de couverture pour le plus urgent des deux constats. `CompileError` en reste
dehors, pour la raison qui l'exclut déjà du score : la suite n'a jamais été interrogée sur un mutant
que l'outil n'a pas su construire.

Une exécution qui n'a rien pu tester n'a pas réussi. Exclure les mutants intestables un par un est
juste ; laisser un changement dont *tous* les mutants étaient intestables rapporter un succès ne l'est
pas, et les deux sont à une ligne l'un de l'autre. Le DEC0009 a déjà tranché la version complète — un
score indéfini fait échouer un seuil, parce qu'une exécution qui n'a rien démontré ne doit pas laisser
un job mal configuré rester vert — et l'exécution partielle en hérite. Un changement sans aucun mutant
est autre chose, et passe : il n'a rien à répondre.

Le code de sortie n'est pas un choix neuf : `1` porte deux causes depuis bien avant qu'on pense à
`--since`, et voici la troisième. `2` reste ce qu'il a toujours été — l'outil n'a pas pu s'exécuter.
Une exécution partielle entièrement intestable, elle, **s'est** exécutée ; elle n'a simplement rien
établi, et le dire avec le code qui signifie *la barrière n'est pas passée* est ce qui empêche un job
mal configuré de passer au vert.

Un statut signifiant « hors du diff » réintroduirait la couture par une autre voie : c'est un état qui
quitte silencieusement le dénominateur, ce qui est précisément ce qui fait changer de sens le nombre
de Stryker sans l'annoncer. L'importer en connaissance de cause serait pire que d'en hériter.

Le rapport consigne la portée de l'exécution parce qu'un rapport partiel dont les mutants hors diff
sont simplement absents est indiscernable d'une exécution complète qui aurait eu ce nombre-là de
mutants — un tableau de bord, ou un lecteur six mois plus tard, ne peut donc ni savoir quelle
population a été inspectée ni reproduire la sélection. C'est une métadonnée, pas un statut de mutant :
le paragraphe ci-dessus tient.

Un seuil suppose un score, et il n'y en a pas ici : il est donc refusé d'où qu'il vienne. Le refus
nomme l'option, et quand la valeur vient de `killmutants.json`, il nomme le fichier et la clé, comme
tous les autres refus qui lisent ce fichier. Ce n'est pas un détail : un projet qui suit le README et
range `breakAt` dans sa configuration verrait sinon toutes ses exécutions partielles refusées, sans
autre issue que d'éditer un fichier versionné. L'issue est `--break-at none`, même forme que
`--without none`.

La conception même de Stryker est la preuve externe la plus forte : quand un outil a besoin d'une
deuxième fonctionnalité pour rendre la première lisible, le problème est dans la conception, pas dans
la documentation.

## Alternatives envisagées

### Alternative 1 — Afficher un score de mutation pour l'exécution partielle

* **Description :** terminer une exécution `--since` par le même pourcentage `détectés / valides` que
  toute autre exécution, calculé sur les mutants du diff. C'est ce que fait Stryker.NET.
* **Pourquoi écartée :** la population est différente par construction à chaque exécution, si bien que
  deux de ces nombres ne répondent à aucune question commune alors même que l'étiquette, la formule et
  leur place dans le rapport disent le contraire ; et sur une population de la taille d'un diff, le
  nombre est trop grossier pour servir de barrière.

### Alternative 2 — Ajouter un statut signifiant « hors du diff »

* **Description :** engendrer tous les mutants et marquer d'un état à eux ceux que l'exécution
  partielle n'a pas considérés, comme le fait l'`ignored` de Stryker.
* **Pourquoi écartée :** un tel état quitte silencieusement le dénominateur, ce qui est la couture qui
  fait changer de sens un score partiel sans l'annoncer. L'adopter serait importer cette couture
  délibérément.

### Alternative 3 — Accepter un seuil avec `--since`

* **Description :** garder l'option de seuil fonctionnelle en exécution partielle, soit ignorée, soit
  réinterprétée contre le diff.
* **Pourquoi écartée :** un seuil suppose un score, et il n'y en a pas ici. Les deux variantes sont la
  réinterprétation tacite que cette décision existe pour refuser.

### Alternative 4 — Faire échouer le verdict sur les seuls survivants

* **Description :** traiter un nouveau mutant `Survived` comme la condition d'échec et laisser
  `NoCoverage` hors de la barrière.
* **Pourquoi écartée :** un changement qui ajoute du code que rien ne teste produit des `NoCoverage`,
  pas des survivants. Une telle barrière laisserait passer le cas le plus flagrant de comportement non
  testé nouvellement introduit — la seule chose que cette exécution existe pour attraper.

### Alternative 5 — Garder le dénominateur du dépôt entier en réutilisant les verdicts inchangés

* **Description :** un *calcul* incrémental plutôt qu'une *population* incrémentale — réutiliser les
  verdicts des mutants qu'un changement ne touche pas, pour que le dénominateur reste le dépôt et que
  le nombre reste comparable à celui d'une exécution complète.
* **Pourquoi écartée :** c'est une autre fonctionnalité, pas une façon de scorer `--since`. Elle mérite
  un pourcentage précisément parce qu'elle garde la population complète, que `--since` n'a pas, et
  confondre les deux est ce que cette décision refuse.

## Conséquences

### Positives

* Un rapport partiel se distingue d'un rapport complet, et sa sélection se reproduit, parce que le mode
  d'exécution et les révisions résolues y figurent.
* Les décomptes par statut restent explicites et interprétables localement.
* Si la fonctionnalité de base de référence est construite plus tard, cet enregistrement est l'endroit
  où son dénominateur est déjà argumenté.

### Négatives

* `--since` ne peut pas servir de barrière en pourcentage. La barrière qu'il offre à la place — aucun
  nouveau mutant non détecté — n'est *pas* plus forte que tout seuil, et l'affirmer serait la même
  surenchère que cet enregistrement ne cesse de corriger : sur une population non vide, elle échoue
  exactement dans les mêmes conditions qu'un seuil à 100 %. Ce qu'elle a de plus qu'un pourcentage,
  c'est de rester sensée quand le dénominateur vaut six, là où un seuil n'est plus que de
  l'arithmétique sur rien.
* Deux rapports ne peuvent plus être comparés en lisant un nombre de chacun. Les décomptes par statut
  ne sont pas davantage offerts comme métrique de qualité d'une exécution à l'autre : « Killed 5 /
  Survived 1 » à côté de « Killed 80 / Survived 2 » n'est pas plus une tendance que ne l'auraient été
  les pourcentages.
* Cela élargit le code de sortie `1`. Le [DEC0009](0009-exit-codes-are-a-public-contract-fr.md) le
  définissait comme *le score de mutation est inférieur à `--break-at`*, or une exécution partielle n'a
  pas de score ; plutôt que de laisser le contrat et le comportement se contredire, `1` signifie
  désormais ce que le raisonnement de cet enregistrement disait déjà. Une automatisation qui lit `1`
  apprend toujours « des constats », ce sur quoi elle agit. `2` est inchangé.

### Risques

* Une exécution partielle qui passe dit que la portée sélectionnée n'a produit aucun mutant non
  détecté, ce qui est une affirmation plus étroite que « ce changement n'a introduit aucun
  comportement non testé ». Un lecteur qui retient la lecture large en tire plus d'assurance que
  l'exécution n'en donne, et le DEC0011 nomme une forme que la portée ne voit pas.

### Actions de suivi

* Le DEC0009 est amendé dans le même changement, pour que le contrat et le comportement ne se
  contredisent pas, et la constante est renommée `GateNotPassed` — `ScoreBelowThreshold` était déjà
  faux pour le chemin du score indéfini avant que cet enregistrement n'existe.
