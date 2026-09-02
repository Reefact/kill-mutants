# ADR-0010 — Une exécution partielle rapporte des constats, pas un score

**Statut :** accepté · **Date :** 2026-09-02

## Contexte

`--since` n'exécutera que les mutants qu'un changement touche. C'est la fonctionnalité qu'on réclame
en premier, parce qu'un balayage complet de ce dépôt prend des minutes et qu'un diff prend des
secondes — c'est elle qui rend le test de mutation utilisable sur une pull request plutôt qu'une fois
par nuit.

La question tranchée ici est : qu'a le droit d'afficher une telle exécution ? Toutes les autres se
terminent par un score de mutation. L'évidence serait d'en afficher un ici aussi.

### Ce que voudrait dire un score partiel

Le score vaut `détectés / valides`. Dans une exécution complète, le dénominateur est *ce dépôt*, et
c'est ce qui rend deux exécutions comparables : la population est la même, donc un mouvement du
nombre est un mouvement de la suite de tests.

Dans une exécution partielle, le dénominateur est *les mutants de ce diff*. Cette population change à
chaque fois. Six mutants mardi, quatre-vingt-dix mercredi. Une exécution qui affiche 72 % puis 40 %
n'a pas mesuré une dégradation : elle a mesuré deux choses sans rapport et leur a donné le même nom,
la même formule et la même place dans le rapport.

Deux conséquences plus petites le rendent nuisible plutôt que seulement imprécis :

- **Un petit dénominateur revendique une précision qu'il n'a pas.** Un diff de trois mutants dont un
  survit donne « 66,7 % » — trois chiffres significatifs sur une mesure qui n'en porte aucun.
- **Le diff vide est le cas le plus bruyant.** Un changement sans mutant, ou dont tous les mutants
  sont hors du code testé, produit le nombre le plus rassurant disponible. C'est exactement la forme
  de mensonge que ce projet existe pour refuser : le titre est au mieux là où la preuve est au pire.

### Ce que fait Stryker.NET, et ce que ça leur a coûté

Lu dans leur documentation, pas dans leur code.

Leur score vaut `detected / valid * 100`, où `valid` = killed + timeout + survived + no coverage. Les
états `ignored`, `compile error` et `runtime error` sont hors dénominateur, et de `ignored` la
documentation dit : « This will not count against your mutation score but will show up in reports. »
L'état lui-même est défini comme « The mutant wasn't tested because it is ignored. Either by user
action, **or for another reason**. »

`since` passe par cette dernière clause. Leur documentation de configuration : « Stryker will only
report on mutants within the changed code. All other mutants will not have a result. »

Une exécution partielle est donc bâtie sur une primitive dont le sens est déjà *ceci ne compte pas*.
C'est une implémentation économe, et c'est précisément pourquoi le nombre change de sens sans
l'annoncer : le dénominateur devient discrètement le diff pendant que l'étiquette, la formule et la
position dans le rapport restent en place.

La preuve la plus forte est dans leur propre conception. Ils ont livré une **seconde** fonctionnalité,
`with-baseline`, pour défaire cela — « provid[ing] you with a full report after a partial mutation
testrun » — et les deux sont mutuellement exclusives. Quand un outil a besoin d'une deuxième
fonctionnalité pour rendre la première lisible, le problème est dans la conception, pas dans la
documentation.

Une note d'honnêteté : que les mutants hors diff atterrissent précisément dans l'état `ignored` vient
d'une discussion avec un mainteneur, pas de la documentation de référence, qui dit seulement « will
not have a result ». Les deux lectures amputent le dénominateur, donc le raisonnement tient dans les
deux cas — mais c'est à l'affirmation la plus faible que nous avons droit.

## Décision

**Une exécution partielle n'affiche aucun score de mutation.**

Elle affiche le décompte de chaque statut, pour que rien ne soit caché ; les nouveaux survivants,
nommés, avec de quoi reproduire chacun d'eux ; et un verdict binaire, parce que la question à
laquelle répond une exécution partielle est binaire. Une exécution complète demande *cette suite de
tests vaut-elle quelque chose ?* Une exécution partielle demande *ce changement introduit-il du
comportement non testé ?* Seule la première a un pourcentage pour réponse.

**Aucun statut ne signifie « hors du diff ».** Les mutants qu'une exécution partielle n'a pas
considérés ne sont pas engendrés, pas comptés, et pas rapportés avec un état à eux. Ajouter un statut
qui quitte silencieusement le dénominateur, c'est exactement la couture décrite plus haut, et nous
l'importerions délibérément.

**`--break-at` est refusé avec `--since`**, par son nom, en disant pourquoi — ni accepté puis ignoré
en silence, ni réinterprété tacitement contre le diff. Un seuil suppose un score, et il n'y en a pas
ici. Le refus nomme l'option et le drapeau, à la manière de tous les autres refus de cet outil.

**Un score comparable issu d'une exécution rapide est une autre fonctionnalité, pour plus tard.**
Garder le dénominateur du dépôt entier et réutiliser les verdicts des mutants inchangés — un *calcul*
incrémental plutôt qu'une *population* incrémentale — produit un nombre réellement comparable à celui
d'une exécution complète. Cette fonctionnalité-là mérite un pourcentage. `--since` non, et les deux
ne doivent pas être confondues.

## Conséquences

- `--since` ne peut pas servir de barrière en pourcentage. C'est le but, pas une limitation à
  contourner plus tard : la barrière qu'il offre à la place — aucun nouveau survivant — est une
  affirmation plus forte sur un changement que n'importe quel seuil sur six mutants.
- Le contrat de codes de sortie de [ADR-0009](0009-exit-codes-are-a-public-contract-fr.md) reste
  valable : `1` quand l'exécution a trouvé ce sur quoi l'utilisateur a demandé d'échouer, `2` quand
  elle n'a pas pu s'exécuter.
- Deux rapports ne peuvent plus être comparés en lisant un nombre de chacun, puisque nous n'affichons
  plus le nombre qui y invite. Les décomptes par statut sont comparables et disent ce qu'ils disent.
- Si la fonctionnalité de base de référence est construite plus tard, cet ADR est l'endroit où son
  dénominateur est déjà argumenté.
