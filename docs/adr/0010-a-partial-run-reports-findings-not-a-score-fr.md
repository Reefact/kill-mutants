# ADR-0010 — Une exécution partielle rapporte des constats, pas un score

**Statut :** accepté · **Date :** 2026-09-02

## Contexte

`--since` n'exécutera que les mutants qu'un changement touche. C'est la fonctionnalité qu'on réclame
en premier, parce qu'un balayage complet de ce dépôt prend des minutes et qu'un diff prend des
secondes — c'est elle qui rend le test de mutation utilisable sur une pull request plutôt qu'une fois
par nuit.

**« Toucher » doit inclure les tests.** Un changement qui se contente de supprimer une assertion ne
met aucun code de production dans le diff : une sélection qui ne lit que les fichiers de production
n'y trouve rien à exécuter et rapporte une exécution vide et verte — pendant que les mutants que
cette assertion tuait survivent désormais. C'est exactement le comportement non testé que cette
fonctionnalité existe pour attraper, arrivant par la porte que personne ne surveille. La sélection
est donc : tout site mutable du code de production modifié, **plus tout mutant couvert par un test
d'un fichier de test modifié**.

Cette seconde moitié ne fonctionne que tant que le test est encore là pour être interrogé. Supprimez
ou renommez un test — ou un utilitaire sur lequel les tests s'appuient — et la relation de
couverture qui nommait les mutants qu'il tuait disparaît de HEAD avec lui : plus rien ne les
sélectionne, et l'exécution redevient verte pour la raison exacte qui a motivé la règle. Et
l'élargissement évident ne suffit pas, ce qu'il vaut la peine d'énoncer parce qu'il en a l'air :
« tout mutant que ce projet de test couvre » se calcule lui aussi depuis la couverture HEAD, et si
`T` était le *seul* test couvrant `M`, alors `M` a quitté cet ensemble à l'instant où `T` l'a
quitté.
Élargir le long de l'axe qui a déjà perdu l'information ne change rien.

Et le déclencheur n'est pas non plus « la modification n'est pas attribuable », qui est plus étroit
que le problème. Ce qui disparaît est une *arête* de couverture, pas une *identité* de test : laissez
`T` en place et modifiez un utilitaire, un fichier d'entrée ou un fixture sur lequel il s'appuie de
sorte qu'il n'atteigne plus `M`, et `T` reste parfaitement attribuable pendant que `T -> M` a disparu.
HEAD ne peut pas nous dire que cette arête a jamais existé — prouver une disparition exige
l'exécution d'avant, précisément ce dont nous ne disposons pas.

**Donc toute modification qui touche un test, un fixture, un utilitaire ou un fichier de
configuration existant d'un projet de test élargit la sélection à tout mutant des projets de
production que ce projet de test exerce** — la relation que porte `MutationTestTarget`, issue des
références de projet et non de la couverture observée.

Et cette relation doit être lue aux **deux** révisions, `targets(base) ∪ targets(head)`, sans quoi le
même trou reparaît une couche plus bas. Supprimez la `ProjectReference` de `Tests` vers `ProjectA`
dans le changement même qu'on juge, et le graphe HEAD ne dit plus que `Tests` exerce `ProjectA` : le
repli pose une question dont le changement a déjà effacé la réponse. La relation qui s'évanouissait
était `T -> M` ; ici c'est `Tests -> ProjectA`.

Ce qui distingue ce cas de la couverture est ce qui le rend réparable maintenant plutôt que reporté,
et il vaut la peine de le dire franchement. L'histoire de la couverture exige une *exécution*
précédente — c'est la fonctionnalité de base de référence, et son absence est la raison pour laquelle
l'élargissement ci-dessus est prudent et non précis. L'histoire structurelle n'exige que les deux
*révisions*, et git les a toutes les deux. Il n'y a pas d'excuse équivalente : le graphe côté base est
donc résolu, pas supposé. S'il ne peut pas l'être, l'exécution est non concluante — et non confiée au
seul graphe HEAD.

Un test *ajouté* par le changement fait exception, et par principe plutôt que par concession : un
test neuf ne peut pas retirer une arête qui lui préexiste, donc l'attribution depuis HEAD y est saine
et la règle précise s'applique toujours. Modifier un fichier existant n'est pas le même cas : rien de
peu coûteux ne distingue une modification qui ajoute un test d'une qui supprime une assertion.

Plus lent, parfois beaucoup, et jamais un faux vert — le même arbitrage que l'exécution fait déjà
quand la couverture est inconnue, et quand un filtre est trop long pour une ligne de commande. Lire
la couverture de la révision de base serait la réponse précise plutôt que prudente, et elle exige les
résultats stockés d'une exécution précédente : c'est la fonctionnalité de base de référence, et ce
n'est délibérément pas celle-ci.

Stryker.NET sélectionne sur les deux mêmes fondements — leur
documentation de configuration, texto : *« For changes on test project files all mutants covered by
tests in that file will be seen as changed. »* Que deux outils aboutissent à la même règle de départ
ne prouve pas grand-chose à soi seul, mais cela dit au moins que la seconde moitié n'est pas une
inquiétude théorique. Cela s'arrête aussi là : « all mutants covered by tests in that file » se lit
depuis l'exécution courante, donc hérite de la même faille d'arête perdue, et l'élargissement
ci-dessus est le nôtre et non le leur.

La question tranchée ici est : qu'a le droit d'afficher une telle exécution ? Toutes les autres se
terminent par un score de mutation. L'évidence serait d'en afficher un ici aussi.

### Ce que voudrait dire un score partiel

Le score vaut `détectés / valides`. Ce qui rend deux exécutions complètes comparables, ce n'est pas
qu'elles jugent les mêmes mutants — elles ne le font pas, puisque le code lui-même change d'un commit
à l'autre — mais qu'elles appliquent la même *règle de portée* : tout site mutable du code
sélectionné. La population bouge ; la question à laquelle le nombre répond, elle, ne bouge pas, si
bien qu'un mouvement du nombre est une affirmation sur la suite de tests.

Une exécution partielle a une règle elle aussi — les sites que son changement touche — et il serait
trop commode de prétendre le contraire. La différence tient à ce sur quoi la règle est ancrée. La
portée d'une exécution complète, c'est le dépôt, qui est le même objet d'une fois sur l'autre ; celle
d'une exécution partielle est définie contre une révision de base choisie à chaque exécution, si bien
que sa population n'est pas seulement différente à chaque fois : elle l'est *par construction*, sans
aucune relation entre celle d'une exécution et celle de la suivante.

Un pourcentage là-dessus est donc une réponse parfaitement sensée à *dans quelle mesure la suite
s'est-elle bien comportée sur ce changement ?* — et aucune réponse à une question qui enjambe deux
exécutions. Affichez 72 % mardi et 40 % mercredi sous un seul nom, une seule formule et une seule
place dans le rapport, et le lecteur tirera une tendance de deux nombres qui ne partagent que leur
unité.

Le nombre serait aussi trop grossier pour servir de barrière, ce qui est un reproche différent de
l'imprécision et plus difficile à contester. Deux détectés et un non détecté s'affichent « 66,67 % »
— épinglé par `MutationScoreTests` — et c'est un rapport exact, pas une mesure bruitée : parler de
fausse précision serait faux. **Le problème est la granularité.** Sur trois mutants, un verdict
déplace le score de 33,3 points : tous les seuils entre 34 % et 66 % veulent donc dire la même chose,
et aucun ne peut exprimer « légèrement moins bien ». Un pourcentage exige une population assez grande
pour bouger par pas plus petits que la décision qu'il éclaire, et un diff, couramment, n'en est pas
une.

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

**Mais une exécution qui n'a rien pu tester n'a pas réussi** — et elle sort en `1`, comme sa jumelle
complète. Exclure les mutants intestables un par
un est juste ; laisser un changement dont *tous* les mutants étaient intestables rapporter un succès
ne l'est pas, et les deux sont à une ligne l'un de l'autre. L'ADR-0009 a déjà tranché la version
complète de ce problème — un score indéfini fait échouer un seuil, parce qu'une exécution qui n'a
rien démontré ne doit pas laisser un job mal configuré rester vert — et l'exécution partielle en
hérite : une sélection qui a produit des mutants dont aucun n'a pu être testé est rapportée comme non
concluante et ne passe pas. Un changement sans aucun mutant est autre chose, et passe : il n'a rien à
répondre.

Le code n'est pas un choix neuf. `Program.Verdict` renvoie déjà `1` pour la version complète de ce
cas — un score indéfini face à un seuil, avec *« No mutant could be tested, so the N% threshold
cannot be shown to be met »* sur la sortie d'erreur — si bien que `1` porte deux causes depuis bien
avant qu'on pense à `--since`, et voici la troisième. `2` reste ce qu'il a toujours été : l'outil n'a
pas pu s'exécuter. Une exécution partielle entièrement intestable, elle, **s'est** exécutée ; elle
n'a simplement rien établi, et le dire avec le code qui signifie *la barrière n'est pas passée* est
ce qui empêche un job mal configuré de passer au vert.

Cela fait de `ScoreBelowThreshold` un mauvais nom pour la constante, et il l'était déjà avant cet ADR
— le chemin du score indéfini le renvoie aussi. Il est renommé `GateNotPassed` dans ce changement,
pour que le vocabulaire de l'implémentation et l'ADR-0009 disent la même chose.

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

- `--since` ne peut pas servir de barrière en pourcentage. La barrière qu'il offre à la place —
  aucun nouveau mutant non détecté — n'est *pas* plus forte que tout seuil, et l'affirmer serait la
  même surenchère que ce document ne cesse de corriger : sur une population non vide, elle échoue
  exactement dans les mêmes conditions qu'un seuil à 100 %. Ce qu'elle a de plus qu'un pourcentage,
  c'est de rester sensée quand le dénominateur vaut six, là où un seuil n'est plus que de
  l'arithmétique sur rien.
- **Cela élargit le code de sortie `1`, et le dit.**
  L'[ADR-0009](0009-exit-codes-are-a-public-contract-fr.md) définissait `1` comme *le score de
  mutation est inférieur à `--break-at`*, or une exécution partielle n'a pas de score. Plutôt que de
  laisser le tableau et le comportement se contredire, `1` signifie désormais ce que le raisonnement
  de cet ADR disait déjà — *ce que vous m'avez demandé de vérifier n'est pas assez bon* — avec pour
  trois cas le score sous un seuil, le score indéfini et le nouveau mutant non détecté — les trois
  mêmes que l'ADR-0009 énumère. L'ADR-0009 est amendé dans le
  même changement ; une automatisation qui lit `1` apprend toujours « des constats », ce sur quoi
  elle agit. `2` est inchangé.
- Deux rapports ne peuvent plus être comparés en lisant un nombre de chacun, puisque nous n'affichons
  plus le nombre qui y invite. Les décomptes par statut restent explicites et interprétables
  localement, et ne sont pas davantage offerts comme métrique de qualité d'une exécution à l'autre :
  « Killed 5 / Survived 1 » à côté de « Killed 80 / Survived 2 » n'est pas plus une tendance que ne
  l'auraient été les pourcentages.
- Un rapport partiel se distingue d'un rapport complet, et sa sélection se reproduit, parce que le
  mode d'exécution et les révisions résolues y figurent.
- Si la fonctionnalité de base de référence est construite plus tard, cet ADR est l'endroit où son
  dénominateur est déjà argumenté.
