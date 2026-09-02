# DEC0011 | Élargir la sélection d'une exécution partielle quand un fichier de test change

## Statut

| Date | Statut | Note | Compte rendu lié |
|---|---|---|---|
| 2026-09-02 | Accepté | | |

## Contexte

`--since` n'exécutera que les mutants qu'un changement touche. C'est la fonctionnalité qu'on réclame
en premier, parce qu'un balayage complet de ce dépôt prend des minutes et qu'un diff prend des
secondes — c'est elle qui rend le test de mutation utilisable sur une pull request plutôt qu'une fois
par nuit. Ce qu'une telle exécution a le droit d'afficher relève du
[DEC0010](0010-a-partial-run-reports-findings-not-a-score-fr.md) ; ce qu'elle sélectionne se décide
ici.

**« Toucher » doit inclure les tests.** Un changement qui se contente de supprimer une assertion ne
met aucun code de production dans le diff : une sélection qui ne lit que les fichiers de production
n'y trouve rien à exécuter et rapporte une exécution vide et verte — pendant que les mutants que
cette assertion tuait survivent désormais. C'est exactement le comportement non testé que cette
fonctionnalité existe pour attraper, arrivant par la porte que personne ne surveille.

Lire les fichiers de test modifiés ne suffit pas à soi seul, parce que cela ne fonctionne que tant
que le test est encore là pour être interrogé. Supprimez ou renommez un test — ou un utilitaire sur
lequel les tests s'appuient — et la relation de couverture qui nommait les mutants qu'il tuait
disparaît de HEAD avec lui : plus rien ne les sélectionne, et l'exécution redevient verte pour la
raison exacte qui a motivé la règle. Et l'élargissement évident ne suffit pas non plus, ce qu'il vaut
la peine d'énoncer parce qu'il en a l'air : « tout mutant que ce projet de test couvre » se calcule
lui aussi depuis la couverture HEAD, et si `T` était le *seul* test couvrant `M`, alors `M` a quitté
cet ensemble à l'instant où `T` l'a quitté. Élargir le long de l'axe qui a déjà perdu l'information
ne change rien.

Le déclencheur n'est pas non plus « la modification n'est pas attribuable », qui est plus étroit que
le problème. Ce qui disparaît est une *arête* de couverture, pas une *identité* de test : laissez `T`
en place et modifiez un utilitaire, un fichier d'entrée ou un fixture sur lequel il s'appuie de sorte
qu'il n'atteigne plus `M`, et `T` reste parfaitement attribuable pendant que `T -> M` a disparu. HEAD
ne peut pas nous dire que cette arête a jamais existé — prouver une disparition exige l'exécution
d'avant, précisément ce dont nous ne disposons pas.

Le même trou reparaît une couche plus bas si le graphe de projets est lu au seul HEAD. Supprimez la
`ProjectReference` de `Tests` vers `ProjectA` dans le changement même qu'on juge, et le graphe HEAD ne
dit plus que `Tests` exerce `ProjectA` : le repli pose une question dont le changement a déjà effacé
la réponse. La relation qui s'évanouissait était `T -> M` ; ici c'est `Tests -> ProjectA`.

L'histoire de la couverture exige une *exécution* précédente ; l'histoire structurelle n'exige que les
deux *révisions*, et git les a toutes les deux.

Un test *ajouté* par le changement ne peut pas retirer une arête qui lui préexiste. Modifier un
fichier existant n'est pas le même cas : rien de peu coûteux ne distingue une modification qui ajoute
un test d'une qui supprime une assertion.

Le support de test vit souvent dans une bibliothèque ordinaire à côté des tests — constructeurs,
doublures, horloges, entrées engendrées — et `ProjectDiscovery` ne classe les projets que par
`IsTestProject` : une telle bibliothèque est donc une *cible mutable*, exactement comme le code sous
test.

Stryker.NET sélectionne sur les deux mêmes fondements — leur documentation de configuration, texto :
*« For changes on test project files all mutants covered by tests in that file will be seen as
changed. »* Que deux outils aboutissent à la même règle de départ ne prouve pas grand-chose à soi
seul, mais cela dit au moins que la seconde moitié n'est pas une inquiétude théorique. Cela s'arrête
aussi là : « all mutants covered by tests in that file » se lit depuis l'exécution courante, donc
hérite de la même faille d'arête perdue.

## Décision

Dans ce contexte, nous sélectionnons les mutants d'une exécution partielle parmi tout site mutable du
code de production modifié et tout mutant couvert par un test d'un fichier de test modifié, et — dès
qu'un changement touche un test, un fixture, un utilitaire ou un fichier de configuration existant
d'un projet de test — nous élargissons la sélection à tout mutant des projets de production que ce
projet de test exerce, lu dans le graphe de projets aux deux révisions de base et de tête.

## Justification

Une sélection qui ne lit que les fichiers de production est mise en échec par le changement qu'elle a
le plus besoin d'attraper : la suppression d'une assertion. Lire les fichiers de test modifiés comble
cela, et rien d'autre ne le fait.

Les lire ne suffit pas à soi seul, parce que l'information dont la règle dépend peut être précisément
ce que le changement a retiré. Élargir le long de l'axe de la couverture n'y change rien, puisque cet
ensemble se calcule depuis la même couverture HEAD qui a déjà perdu l'arête ; l'élargissement doit
donc courir le long d'une relation que le changement ne peut pas effacer en silence.
`MutationTestTarget` est cette relation : elle vient des références de projet, pas de la couverture
observée.

Le graphe est lu aux deux révisions plutôt qu'au seul HEAD, sans quoi le repli hérite de la
défaillance qu'il existe pour prévenir, une couche plus haut. Et c'est abordable ici là où la réponse
précise ne l'est pas : l'histoire de la couverture exigerait une exécution précédente — la
fonctionnalité de base de référence — quand l'histoire structurelle n'exige que les deux révisions,
que git possède. Il n'y a pas d'excuse équivalente : le graphe côté base est donc résolu et non
supposé, et un graphe qui ne peut pas l'être rend l'exécution non concluante plutôt que confiée au
seul HEAD.

Un test ajouté par le changement est une exception de principe et non une concession : un test neuf ne
peut pas retirer une arête qui lui préexiste, donc l'attribution depuis HEAD y est saine et la règle
précise s'applique toujours.

Le prix est une exécution plus lente, parfois beaucoup — le même arbitrage que l'exécution fait déjà
quand la couverture est inconnue, et quand un filtre est trop long pour une ligne de commande. Prudent
et lent vaut mieux que précis et faux quand le mode de défaillance est une exécution verte qui aurait
dû être rouge.

## Alternatives envisagées

### Alternative 1 — Sélectionner sur les seuls fichiers de production modifiés

* **Description :** lire le diff pour y trouver des sites mutables et s'en tenir là, en laissant les
  fichiers de test hors de la sélection.
* **Pourquoi écartée :** un changement qui se contente de supprimer une assertion ne met aucun code de
  production dans le diff : l'exécution est vide et verte pendant que les mutants que cette assertion
  tuait survivent — le cas exact que la fonctionnalité existe pour attraper.

### Alternative 2 — Élargir à tout mutant que le projet de test modifié couvre

* **Description :** quand un fichier de test change, sélectionner tout ce que ce projet de test est
  connu pour couvrir, plutôt que de suivre le graphe de projets.
* **Pourquoi écartée :** cet ensemble se calcule depuis la couverture HEAD, et si `T` était le seul
  test couvrant `M`, `M` l'a quitté à l'instant où `T` l'a quitté. Élargir le long de l'axe qui a déjà
  perdu l'information ne change rien.

### Alternative 3 — Déclencher sur une modification non attribuable à un test

* **Description :** n'élargir que lorsque l'outil ne peut pas dire à quel test une modification se
  rattache.
* **Pourquoi écartée :** c'est plus étroit que le problème. Ce qui disparaît est une arête de
  couverture, pas une identité de test : `T` peut rester parfaitement attribuable pendant que `T -> M`
  disparaît.

### Alternative 4 — Lire le graphe de projets au seul HEAD

* **Description :** calculer l'élargissement depuis la révision de tête, comme l'est la carte de
  couverture.
* **Pourquoi écartée :** supprimer une `ProjectReference` dans le changement qu'on juge efface la
  réponse que le repli s'apprête à demander. Le trou que l'élargissement existe pour combler reparaît
  une couche plus haut.

### Alternative 5 — Élargir sur tout projet modifié joignable depuis un projet de test

* **Description :** réexécuter tout ce qu'un projet de test peut atteindre transitivement dès que l'un
  de ces projets change, ce qui couvrirait aussi la faille des bibliothèques de support.
* **Pourquoi écartée :** chaque changement de production réexécuterait tout, c'est-à-dire
  supprimerait `--since` plutôt que de le nuancer, et aucun fait structurel ne sépare une bibliothèque
  de support d'un sujet.

### Alternative 6 — Lire la couverture de la révision de base

* **Description :** la réponse précise plutôt que prudente — demander à l'exécution précédente quels
  tests atteignaient quels mutants.
* **Pourquoi écartée :** elle exige les résultats stockés d'une exécution précédente. C'est la
  fonctionnalité de base de référence, et ce n'est délibérément pas celle-ci.

## Conséquences

### Positives

* Le changement que cette fonctionnalité a le plus besoin d'attraper — une assertion supprimée sans
  aucun code de production dans le diff — est sélectionné plutôt que laissé passer en silence.
* L'élargissement court le long d'une relation que le changement ne peut pas effacer sans que l'outil
  s'en aperçoive, et il est résolu aux deux révisions plutôt que supposé depuis une seule.
* Un test *ajouté* par un changement n'élargit rien, puisque le cas imprécis ne peut pas s'y produire :
  un test qui n'existait pas à la révision de base ne peut pas avoir retiré une arête de couverture.

### Négatives

* Les exécutions sont plus lentes, parfois beaucoup : toucher un seul fichier de test peut réexécuter
  tous les mutants des projets de production que ce projet de test exerce. Mesuré à la construction de
  `--since`, sur ce dépôt contre `main` : 33 fichiers modifiés, 364 mutants sélectionnés,
  7,0 minutes — contre 384 mutants en 6,8 minutes pour une exécution complète du même projet.
  L'exécution partielle a inspecté 95 % de la population pour le même temps, parce que le changement
  touchait des fichiers de `KillMutants.Core.Tests`. Rien n'a mal fonctionné ; la règle a fait ce
  qu'elle dit.
* La moitié *précise* de la règle n'est pas implémentée pour un fichier de test ajouté, qui ne
  sélectionne donc rien plutôt que les mutants que ses nouveaux tests couvrent. « Couvert par un test
  de ce fichier » exige une correspondance entre une méthode de test et le fichier source où elle est
  écrite : la découverte de xUnit répond par des noms seuls, et la compilation qui la résoudrait est
  celle du projet de test, que cet outil ne construit jamais. La restriction ne peut cacher aucun
  constat — voir la conséquence positive ci-dessus — si bien que ce qui est perdu est informatif et
  non protecteur : après un commit qui n'ajoute que des tests, l'exécution rapporte qu'il n'y avait
  rien à juger.
* L'élargissement est prudent et non précis, et le reste tant que la couverture d'une exécution
  précédente n'est pas consultable.

### Risques

* Un fichier de build partagé est attribué aux projets situés sous lui, ce qui manque celui qu'un
  projet importe explicitement depuis un répertoire voisin plutôt que depuis un parent.
  `MSBuildAllProjects` aurait été la réponse exacte et revient vide sur un projet SDK — mesuré — donc
  il n'existe pas de moyen économique de demander quels fichiers de build un projet lit vraiment.
* Un fichier C# ajouté dans un projet de test n'élargit toujours rien, et seul un test peut être
  supposé ajouter de la couverture plutôt que la changer. Un fichier portant une mise en place
  partagée, ou un initialiseur de module, n'est pas un test et rien de peu coûteux ne l'en distingue.
  Toute autre entrée ajoutée — une fixture, une liste de cas, un fichier de réglages — élargit
  désormais, car l'argument « un test neuf ne peut pas retirer une arête préexistante » n'a jamais
  porté sur elles.
* Une modification de `killmutants.json` fait refuser l'exécution partielle plutôt que d'être
  sélectionnée. L'`exclude` qui s'y trouve agit dans la découverte, avant qu'aucune sélection
  n'existe : un changement qui en ajoute un retire un projet des cibles, et aucun élargissement
  ultérieur ne peut le rattraper. Comparer les réglages des deux révisions serait la réponse précise ;
  refuser de juger une modification de la configuration de l'exécution elle-même est la réponse
  honnête.
* Un fichier qu'un changement **supprime** et qu'un projet de test atteignait depuis l'extérieur de
  son propre répertoire n'est attribué à rien. L'appartenance se lit dans l'évaluation de HEAD, où un
  fichier supprimé n'apparaît plus, et la règle du répertoire qui couvre les suppressions ordinaires
  ne l'atteint pas davantage. Les deux règles sont nécessaires et aucune ne couvre l'angle mort de
  l'autre ici.
* La garantie s'arrête au bord d'un projet de test. Le support de test rangé dans une bibliothèque
  ordinaire est une cible mutable et non un projet de test : la modifier peut faire cesser `T`
  d'atteindre `M` sans que ni le projet de test ni celui de `M` n'apparaisse dans le diff, et
  l'exécution passe sans avoir interrogé `M`.

### Actions de suivi

* Combler RB-025 par la couverture de référence, ou par un moyen explicite de déclarer un projet comme
  support de test.
