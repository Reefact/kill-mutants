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

Le score vaut `détectés / valides`. Ce qui rend deux exécutions complètes comparables, ce n'est pas
qu'elles jugent les mêmes mutants — elles ne le font pas, puisque le code lui-même change d'un commit
à l'autre — mais qu'elles appliquent la même *règle de portée* : tout site mutable du code
sélectionné. La population bouge ; la question à laquelle le nombre répond, elle, ne bouge pas, si
bien qu'un mouvement du nombre est une affirmation sur la suite de tests.

Une exécution partielle n'a pas de règle de ce genre. Son dénominateur est *les mutants de ce diff*,
et un diff n'est pas une portée : c'est l'accident de ce que quelqu'un a touché. Six mutants mardi,
quatre-vingt-dix mercredi, et rien de commun entre les deux. Une exécution qui affiche 72 % puis 40 %
n'a pas mesuré une dégradation : elle a répondu à deux questions différentes et donné aux deux
réponses le même nom, la même formule et la même place dans le rapport.

Et le nombre serait mal fabriqué autant que mal nommé. **Un petit dénominateur revendique une
précision qu'il n'a pas** : un diff de trois mutants dont un survit s'affiche « 66,7 % », trois
chiffres significatifs sur une mesure qui n'en porte aucun.

Un piège que nous n'avons *pas*, et qu'il vaut la peine d'écrire parce que la version évidente de cet
argument s'y trompe. Un diff vide ne produit pas ici de titre rassurant :
`MutationScore.IsUndefined` est vrai quand rien n'a été jugé, `ToString` rend `n/a`, et l'ADR-0009
fait déjà échouer un seuil sur un score indéfini. Invoquer « l'exécution vide affiche 100 % » aurait
été emprunter la défaillance d'un autre outil sans vérifier la nôtre — précisément l'erreur dont ce
document entier parle.

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

Elle affiche le décompte de chaque statut, pour que rien ne soit caché ; les nouveaux constats,
nommés, avec de quoi reproduire chacun d'eux ; et un verdict binaire, parce que la question à
laquelle répond une exécution partielle est binaire. Une exécution complète demande *cette suite de
tests vaut-elle quelque chose ?* Une exécution partielle demande *ce changement introduit-il du
comportement non testé ?* Seule la première a un pourcentage pour réponse.

**Le verdict échoue sur tout nouveau mutant *non détecté*, pas seulement sur un survivant.** Un
mutant qu'aucun test n'atteint est `NoCoverage`, pas `Survived` — et un changement qui ajoute du code
que rien ne teste produit exactement ceux-là. Un portillon ne lisant que les survivants laisserait
passer le cas le plus flagrant de comportement non testé nouvellement introduit, c'est-à-dire la
seule chose que cette exécution existe pour attraper. `MutationScore` compte déjà les deux comme non
détectés, et l'ADR-0007 tient l'absence de couverture pour le plus urgent des deux constats. Les deux
sont nommés dans la sortie, et les deux font échouer le verdict.

`CompileError` en reste dehors, pour la raison qui l'exclut déjà du score : la suite n'a jamais été
interrogée sur un mutant que l'outil n'a pas su construire.

**Aucun statut ne signifie « hors du diff ».** Les mutants qu'une exécution partielle n'a pas
considérés ne sont pas engendrés, pas comptés, et pas rapportés avec un état à eux. Ajouter un statut
qui quitte silencieusement le dénominateur, c'est exactement la couture décrite plus haut, et nous
l'importerions délibérément.

**Le rapport consigne la portée de l'exécution en métadonnée.** Un rapport partiel dont les mutants
hors diff sont simplement absents est indiscernable d'une exécution complète qui aurait eu ce
nombre-là de mutants — un tableau de bord, ou un lecteur six mois plus tard, ne peut donc ni savoir
quelle population a été inspectée ni reproduire la sélection. `--report-json` porte donc le mode
d'exécution et les révisions de base et de tête *résolues*, au niveau du rapport, à côté de
l'environnement et des budgets de temps qui y figurent déjà. C'est la même règle qu'eux : un rapport
qu'on ne peut pas interpréter n'est pas un rapport. C'est une métadonnée, pas un statut de mutant :
le paragraphe précédent tient.

**Un seuil est refusé avec `--since`, d'où qu'il vienne.** Un seuil suppose un score, et il n'y en a
pas ici. Le refus nomme l'option — et quand la valeur vient de `killmutants.json`, il nomme le
fichier et la clé, comme tous les autres refus qui lisent ce fichier. Ce dernier point n'est pas un
détail : `RunSettings` résout `options.Threshold ?? file?.BreakAt`, donc un projet qui suit le README
et range `breakAt` dans sa configuration verrait sinon toutes ses exécutions partielles refusées,
sans autre issue que d'éditer un fichier versionné. L'issue est `--break-at none`, qui l'efface pour
cette exécution — même forme que `--without none`, et pour la même raison.

**Un score comparable issu d'une exécution rapide est une autre fonctionnalité, pour plus tard.**
Garder le dénominateur du dépôt entier et réutiliser les verdicts des mutants inchangés — un *calcul*
incrémental plutôt qu'une *population* incrémentale — produit un nombre réellement comparable à celui
d'une exécution complète. Cette fonctionnalité-là mérite un pourcentage. `--since` non, et les deux
ne doivent pas être confondues.

## Conséquences

- `--since` ne peut pas servir de barrière en pourcentage. C'est le but, pas une limitation à
  contourner plus tard : la barrière qu'il offre à la place — aucun nouveau mutant non détecté — est
  une affirmation plus forte sur un changement que n'importe quel seuil sur six mutants.
- **Cela élargit le code de sortie `1`, et le dit.**
  L'[ADR-0009](0009-exit-codes-are-a-public-contract-fr.md) définissait `1` comme *le score de
  mutation est inférieur à `--break-at`*, or une exécution partielle n'a pas de score. Plutôt que de
  laisser le tableau et le comportement se contredire, `1` signifie désormais ce que le raisonnement
  de cet ADR disait déjà — *ce que vous m'avez demandé de vérifier n'est pas assez bon* — avec pour
  deux cas le score sous un seuil et le nouveau mutant non détecté. L'ADR-0009 est amendé dans le
  même changement ; une automatisation qui lit `1` apprend toujours « des constats », ce sur quoi
  elle agit. `2` est inchangé.
- Deux rapports ne peuvent plus être comparés en lisant un nombre de chacun, puisque nous n'affichons
  plus le nombre qui y invite. Les décomptes par statut sont comparables et disent ce qu'ils disent.
- Un rapport partiel se distingue d'un rapport complet, et sa sélection se reproduit, parce que le
  mode d'exécution et les révisions résolues y figurent.
- Si la fonctionnalité de base de référence est construite plus tard, cet ADR est l'endroit où son
  dénominateur est déjà argumenté.
