# DEC0031 | Une ligne du journal des verdicts porte la durée de son jugement, que le rapport JSON ne porte pas

## Statut

| Date | Statut | Note | Compte rendu lié |
|---|---|---|---|
| 2026-11-10 | Proposé | Première rédaction | |
| 2026-11-11 | Accepté | À la demande du mainteneur | |

## Contexte

DEC0014 ajoute chaque verdict définitif à un journal à côté du rapport, sous la forme d'une ligne de
JSON « ayant la forme d'une entrée des `mutants` du rapport », pour qu'un consommateur qui lit le
rapport lise le journal avec le même code ; ses conséquences disent qu'il n'y a qu'une forme pour le
verdict d'un mutant, dans le rapport et dans le journal. La ligne est écrite dès que le verdict est
définitif, si bien qu'une exécution arrêtée ou tuée garde chaque ligne qu'elle a écrite.

Rien ne disait combien de temps avait pris le jugement d'un mutant. En faisant tourner KillMutants
sur `Reefact/just-dummies`, environ 5 600 mutants pour une exécution de bien plus d'une heure, le
seul chiffre disponible était une moyenne déduite du rythme des verdicts. Elle cache la dispersion :
un mutant tué s'arrête au premier test qui échoue, un survivant exécute tous les tests de sa
sélection, et un mutant sans couverture ne coûte presque rien. Une durée par mutant montre quels
mutants et quels fichiers sont lents, permet de régler le délai à partir de mesures plutôt que d'un
multiple de la baseline, éclaire le choix du nombre de workers, et estime ce qu'il reste à une
exécution (#75).

Le rapport JSON est déterministe : deux exécutions sur le même code écrivent le même rapport. Une
durée change à chaque exécution.

Un mutant peut être jugé plus d'une fois. Un délai dépassé n'est cru qu'une fois qu'une seconde
exécution, seule, le reproduit (RB-013), et `--verify-kills` reteste un échantillon de mises à mort
sans changer leur verdict. Les workers jugent côte à côte, donc les durées se chevauchent : leur
somme dépasse le temps écoulé, montre en main, de l'exécution.

Le mainteneur a arrêté la forme dans #75 et sa revue : la durée va dans le journal des verdicts
seulement, pas dans le rapport ; ni nouvelle option ni fichier séparé ; une ligne dans le résumé
final de la console, avec la médiane et le mutant le plus lent et sans total ; un verdict hérité
d'une exécution antérieure garde sa durée ou n'en a pas ; la reprise et l'identité de l'exécution
ignorent le champ. Le 2026-11-10, le mainteneur a choisi que la durée enregistrée soit celle du
jugement qui a produit le verdict final.

## Décision

Dans ce contexte, nous décidons que chaque ligne du journal des verdicts porte, à côté de l'entrée
du rapport qu'elle contient déjà, la durée du jugement qui a produit son verdict, et que le rapport
JSON n'en porte aucune.

## Justification

Le journal est le seul endroit où une durée peut être à la fois conservée et sans effet. Il est
écrit à mesure que chaque verdict devient définitif, si bien qu'une exécution arrêtée ou tuée garde
ses durées, et c'est précisément l'exécution dont on veut comprendre le temps. Le rapport est ce sur
quoi deux exécutions sur le même code s'accordent ; un champ qui change à chaque exécution rendrait
bruyants chaque diff et chaque rapport versionné, pour un chiffre que personne ne compare d'une
exécution à l'autre.

La ligne garde l'entrée du rapport entière et y ajoute, si bien que l'objectif de DEC0014 tient :
un consommateur lit le mutant avec le même code dans l'un ou l'autre fichier, et une ligne sans
durée est encore exactement une entrée. Ce qui change, c'est la lettre de « une seule forme » : la
forme du journal est l'entrée plus ce que seul le journal porte.

Enregistrer le jugement qui a produit le verdict final fait mesurer à la médiane et au plus lent ce
que coûte un jugement. La durée d'un délai dépassé confirmé est celle de sa confirmation, exécutée
seule, plutôt que celle-ci plus un premier passage fait sous charge ; une mise à mort revérifiée
garde la durée du jugement qui l'a établie, puisque le nouveau test ne change aucun verdict. Les
durées se chevauchent entre workers, donc la console donne une médiane et un plus lent, et jamais
une somme, qui se lirait comme le temps de l'exécution.

## Alternatives envisagées

### Alternative 1 — Aucune durée par mutant

* **Description :** garder le seul chiffre disponible aujourd'hui, une moyenne déduite du rythme des
  verdicts.
* **Pourquoi rejetée :** une moyenne cache la dispersion entre mises à mort, survivants et mutants
  non couverts, qui est ce qui dit où passe le temps.

### Alternative 2 — La durée aussi dans le rapport JSON

* **Description :** ajouter le même champ à chaque entrée des `mutants` du rapport.
* **Pourquoi rejetée :** le rapport ne serait plus le même pour deux exécutions sur le même code.
  Si on la veut un jour là, c'est une option explicite et une décision à part entière.

### Alternative 3 — Un fichier de durées séparé, ou une option qui en écrit un

* **Description :** écrire les durées dans un fichier à part, toujours ou derrière une option.
* **Pourquoi rejetée :** un fichier et une option de plus sont un contrat de plus à tenir, pour peu :
  le journal est déjà lu par script.

### Alternative 4 — La somme de tous les jugements d'un mutant

* **Description :** enregistrer, pour un mutant jugé plus d'une fois, le total de ses jugements.
* **Pourquoi rejetée :** le mainteneur a choisi le jugement qui a produit le verdict final ; une
  somme compterait deux fois le budget d'un délai dépassé confirmé et ferait du mutant le plus lent
  une mesure de la règle de confirmation plutôt que du mutant.

## Conséquences

### Positives

* Le coût de chaque mutant jugé est sur le disque dès que son verdict l'est, y compris pour une
  exécution qui n'écrit jamais son rapport.
* Le rapport JSON reste le même pour deux exécutions sur le même code.
* Le résumé de la console dit, en une ligne, où passe le temps d'une longue exécution.

### Négatives

* Une ligne du journal n'est plus exactement son entrée du rapport : un consommateur qui compare
  les deux doit laisser `durationMs` de côté.
* Deux exécutions sur le même code écrivent des lignes de journal différentes.
* Un verdict hérité d'une ligne écrite avant ce changement n'a pas de durée, et le résumé porte
  alors sur une partie des verdicts.

### Risques

* Un lecteur additionne les durées et prend la somme pour le temps de l'exécution.
* Une durée mesurée sur une machine chargée est lue comme le coût propre du mutant.

### Actions de suivi

Rien n'a été consigné au moment de la décision.
