# DEC0016 | L'exécution écrit son plan à côté du rapport, dans un fichier à part

## Statut

| Date | Statut | Note | Compte rendu lié |
|---|---|---|---|
| 2026-10-11 | Proposé | Première rédaction | |
| 2026-10-11 | Accepté | | |

## Contexte

Le mainteneur a demandé une exécution qui écrit son plan au démarrage, afin qu'à tout moment ultérieur
ce qui a été fait et ce qui reste puissent l'un et l'autre être lus — par une exécution ultérieure qui
continue la série
([DEC0015](0015-a-resumed-run-inherits-the-verdicts-its-series-reached-on-the-same-code-fr.md)), et
par quiconque lit ce qu'une exécution tuée a laissé derrière elle.

[DEC0014](0014-each-verdict-is-appended-to-a-log-beside-the-report-fr.md) ajoute une ligne par verdict
définitif à un journal JSON Lines nommé d'après le rapport, et compte parmi ses conséquences positives
que *« le verdict d'un mutant a un seul format, dans le rapport comme dans le journal »* : chaque ligne
est une entrée de `mutants` du rapport, et un consommateur lit les deux avec le même code. Les propres
tests du dépôt lisent le journal ainsi — `key` sur chaque ligne, et autant de lignes que de verdicts
annoncés. DEC0014 consigne aussi que le journal *« n'indique ni combien de mutants l'exécution avait
sélectionnés, ni si elle s'est terminée, ni ce qu'a trouvé `--verify-kills` : c'est le rôle du
rapport »*, et que le mainteneur a préféré le chemin du journal dérivé de `--report-json` à une option
à part. Un chemin de rapport se terminant par `.jsonl` est refusé, parce que ce nom est celui du
journal.

La population entière d'une exécution est connue à un moment précis : une fois que la compilation de
chaque projet est en mémoire et que chaque mutant a été généré, avant qu'aucun ne soit testé, quand
`VerdictLedger.Select` est appelé. Une exécution arrêtée se sert déjà de ce moment pour dire combien
de mutants elle avait sélectionnés. Ce qu'une exécution sait grandit ensuite : quelle exécution a
rendu quels verdicts, et les désaccords que trouve `--verify-kills`, que `VerdictLedger.Amend`
consigne sans en avertir aucun observateur, si bien qu'ils vivent dans le rapport et jamais dans le
journal.

Un fichier lu d'un bloc peut être réécrit atomiquement, en écrivant une nouvelle copie puis en la
déplaçant par-dessus l'ancienne. Un fichier append-only ne peut pas être réécrit ainsi, et une ligne
qui n'y serait pas un verdict devrait être sautée par chaque lecteur.

GitHub Actions tue l'arbre de processus d'un job quelques secondes après avoir envoyé un signal au
shell de l'étape, si bien qu'une exécution peut se terminer avec son journal écrit et aucun rapport ;
une étape de synthèse lancée ensuite ne peut lire que ce qui est sur le disque. Aujourd'hui, elle peut
lire les verdicts rendus et rien de ce dont ils faisaient partie.

## Décision

Dans ce contexte, chaque exécution qui écrit un rapport JSON écrit aussi, à côté de lui et nommé
d'après lui avec l'extension `.plan.json`, un plan qui identifie l'exécution et énumère chaque mutant
qu'elle a sélectionné, afin que le journal continue de ne contenir que des verdicts et rien d'autre.

## Justification

Le plan est le document que le mainteneur a demandé : la liste de ce qu'une exécution s'est donné à
juger, avec ce qui identifie le code et les réglages sous lesquels elle s'est donné de le juger, dans
les propres noms de champs du rapport afin qu'une ligne du journal se joigne à une entrée du plan sur
la clé. Le plan moins le journal est le reste, lisible par une exécution ultérieure comme par une étape
de synthèse, avec l'outil ou sans lui.

Un fichier à part plutôt qu'une première ligne dans le journal, parce que le format unique de DEC0014
est une propriété qui vaut d'être gardée : chaque lecteur du journal, les tests de ce dépôt compris,
prend chaque ligne pour un verdict, et un en-tête obligerait chacun d'eux à sauter une ligne. Le plan
est aussi réécrit à mesure que l'exécution apprend — l'exécution qui a rendu un verdict, un désaccord
trouvé plus tard —, et un document se réécrit d'un bloc et atomiquement, ce qu'un journal append-only
ne peut pas être.

Nommé d'après le rapport, comme l'est le journal, parce que le mainteneur a préféré la dérivation à une
option et qu'un lecteur qui trouve l'un des trois fichiers sait où sont les deux autres ; et
`.plan.json` est refusé comme nom de rapport pour la raison qui fait que `.jsonl` l'est déjà.

Écrit à chaque exécution qui écrit un rapport, pas seulement à celle qui nomme une série, parce que
le reste d'une exécution tuée vaut d'être lu même quand personne ne le continue, et que seul un plan
dit de quoi les lignes du journal faisaient partie.

## Alternatives envisagées

### Alternative 1 — Une ligne d'en-tête dans le journal des verdicts

* **Description :** écrire le plan comme première ligne du `.jsonl`, dans un format qui lui soit
  propre.
* **Pourquoi écartée :** chaque ligne du journal ne serait plus un verdict, ce qui casse le format
  unique de DEC0014 et chaque lecteur écrit pour lui, et un en-tête ne peut pas être réécrit à mesure
  que l'exécution apprend.

### Alternative 2 — Pas de plan : le régénérer

* **Description :** une exécution ultérieure régénère la population, qui est déterministe, et la
  compare au journal ; rien n'est écrit au démarrage.
* **Pourquoi écartée :** une étape de synthèse après une exécution tuée n'a rien d'où lire ce qui
  reste, et l'identité dont une exécution reprise a besoin n'aurait nulle part où vivre sinon dans les
  lignes du journal.

### Alternative 3 — N'écrire le plan que quand `--resume` est passé

* **Description :** une exécution ordinaire écrit rapport et journal comme aujourd'hui ; seule une
  exécution qui nomme une série écrit un plan.
* **Pourquoi écartée :** une exécution qui ne nomme aucune série peut être tuée elle aussi, et son
  reste est illisible sans plan.

### Alternative 4 — Une option nommant le chemin du plan

* **Description :** `--plan <chemin>`, avec une clé correspondante dans `killmutants.json`.
* **Pourquoi écartée :** le mainteneur a préféré le chemin du journal dérivé de celui du rapport, et
  la même raison vaut ici : un job qui nomme déjà son rapport a son plan sans rien demander.

## Conséquences

### Positives

* Ce qu'une exécution s'est donné à juger, et sous quelle identité, est sur le disque avant que le
  premier mutant soit testé, et survit à un arrêt forcé.
* Le reste d'une exécution tuée est lisible à partir du plan et du journal seuls.
* Le journal continue de ne contenir que des verdicts et rien d'autre.

### Négatives

* Chaque exécution qui écrit un rapport laisse un troisième fichier à côté.
* Un chemin de rapport se terminant par `.plan.json` est refusé.
* Le plan est réécrit plusieurs fois par exécution, et une réécriture qui échoue pendant un arrêt est
  mise sur le compte de l'arrêt.

### Risques

* Un lecteur peut prendre la liste du plan pour le résultat de l'exécution plutôt que pour ce qu'elle
  s'est donné à juger.

### Actions de suivi

* Faire lire le plan et le journal par l'étape de synthèse de `Reefact/slugger` quand le rapport
  manque, et lui faire dire combien de mutants restent.
