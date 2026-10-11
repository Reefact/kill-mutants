# DEC0014 | Chaque verdict est ajouté à un journal à côté du rapport dès qu'il est définitif

## Statut

| Date | Statut | Note | Compte rendu lié |
|---|---|---|---|
| 2026-10-11 | Proposé | Première rédaction | |
| 2026-10-11 | Accepté | | |

## Contexte

[DEC0013](0013-a-stopped-run-reports-what-it-judged-and-passes-no-gate-fr.md) permet à une exécution
arrêtée d'écrire son rapport — mais seulement pour un arrêt que l'exécution se fixe elle-même ou
qu'elle entend. Certaines fins ne lui parviennent sous aucune de ces formes. Sur GitHub Actions, un
job annulé par une personne, ou par `concurrency: cancel-in-progress` quand un push plus récent
arrive — ce qu'utilise la CI de ce dépôt —, se termine comme un job hors délai : un signal au shell de
l'étape, puis l'arbre de processus tué quelques secondes plus tard. Un runner perdu met fin à tout
sans aucun signal. Et une exécution qui atteint sa propre échéance a encore des mutants en cours et
son rapport à écrire : une échéance fixée trop près du budget de la CI peut être rattrapée par la
mise à mort.

Ce qui survit à un processus tué, c'est ce qu'il avait déjà remis au système d'exploitation. Avant
cette décision, ce n'était rien : chaque verdict restait en mémoire jusqu'à l'écriture du rapport, à
la fin.

Le rapport JSON est lu par des étapes de CI après l'exécution.
[L'issue #24](https://github.com/Reefact/kill-mutants/issues/24) cite l'étape de synthèse de
`Reefact/slugger`, gardée par `hashFiles('killmutants-report.json') != ''` pour être sautée quand
l'exécution n'a écrit aucun rapport. Le champ `complete` du rapport est nouveau avec DEC0013 : aucun
consommateur écrit avant lui ne le lit. Son champ `hasUndetected` est ce que lit un consommateur qui
fait barrière sur le rapport pour décider si une exécution partielle a réussi.
[DEC0009](0009-exit-codes-are-a-public-contract-fr.md) désigne comme sens dangereux, pour un résultat
mal lu, le sens silencieux : celui où une exécution est prise pour un succès qu'elle n'a jamais été.

Le statut d'un mutant ne peut changer après avoir été vu une première fois que de deux façons. Un
timeout est réexécuté seul, une fois les workers terminés, et peut revenir sous une autre forme. Un
mutant tué tiré au sort par `--verify-kills` est testé à nouveau, ce qui ne change jamais son statut
mais peut y adjoindre un désaccord. Tout autre statut est définitif dès que le mutant a été testé.

Le mainteneur a demandé que ce qui est écrit soit nommé d'après le chemin de `--report-json` plutôt
que configuré par une option à part.

## Décision

Quand une exécution écrit un rapport JSON, elle ajoute aussi chaque verdict, dès qu'il est définitif,
sous la forme d'une ligne JSON à un journal placé à côté du rapport, qui prend le nom du rapport avec
l'extension `.jsonl`.

## Justification

Seul ce qui est sur le disque survit à une mise à mort : un verdict doit donc atteindre le disque au
moment où il est atteint, et non quand l'exécution se termine. C'est tout l'argument en faveur d'un
journal, et il vaut pour chaque fin que DEC0013 ne peut pas voir.

Un fichier à part plutôt que le rapport réécrit au fil de l'exécution. Chaque version intermédiaire
d'un rapport réécrit occuperait la place où l'on attend un rapport fini, lue par des consommateurs qui
ignorent l'existence de `complete` — et sur une exécution partielle, `hasUndetected: false` dans un
rapport à moitié écrit se lit comme un succès. C'est le sens silencieux dont traite DEC0009. Avec un
journal séparé, le fichier du rapport ne contient jamais qu'une exécution finie, ou une exécution qui
dit qu'elle s'est arrêtée.

Seulement des verdicts définitifs, pour qu'une ligne, une fois écrite, ne soit jamais contredite par
une suivante. Un journal qui consignerait chaque statut au moment où il est vu devrait être lu selon
la règle « la dernière ligne de chaque mutant l'emporte », et un consommateur qui le lirait autrement
compterait un timeout non confirmé comme une détection. Retenir un timeout jusqu'à sa confirmation
coûte quelques lignes à la fin de chaque projet ; un désaccord trouvé par `--verify-kills` reste dans
le rapport, à côté du verdict qu'il nuance.

Une ligne de la forme d'une entrée de `mutants` du rapport, pour qu'un consommateur qui lit déjà le
rapport lise le journal avec le même code, et que les deux ne nomment jamais un mutant différemment.

Nommé d'après le rapport, comme le mainteneur l'a demandé : un job qui nomme déjà son rapport a son
journal sans rien demander, et qui trouve l'un des deux fichiers sait où chercher l'autre.

## Alternatives envisagées

### Alternative 1 — Pas de journal

* **Description :** s'en remettre au seul arrêt de DEC0013, et n'écrire les verdicts que dans le
  rapport.
* **Pourquoi écartée :** cela ne couvre qu'une fin que l'exécution se fixe ou entend. Un job annulé,
  un runner perdu et une échéance rattrapée par la mise à mort ne laisseraient toujours rien.

### Alternative 2 — Réécrire le rapport JSON au fil de l'exécution

* **Description :** écrire le rapport toutes les quelques secondes, de façon atomique, marqué
  `complete: false` jusqu'à la dernière écriture.
* **Pourquoi écartée :** chaque fichier intermédiaire occupe la place où l'on attend un rapport fini,
  et un consommateur qui ne lit pas `complete` prend pour un succès une exécution partielle qui n'a
  encore rien trouvé.

### Alternative 3 — Consigner chaque statut au moment où il est vu

* **Description :** écrire une ligne dès que le statut d'un mutant est connu, puis une autre quand un
  timeout est confirmé ou qu'un mutant tué est revérifié, la ligne ultérieure remplaçant la
  précédente.
* **Pourquoi écartée :** chaque consommateur devrait appliquer « la dernière ligne l'emporte », et
  celui qui ne le ferait pas compterait les timeouts non confirmés comme des détections.

### Alternative 4 — Une option dédiée au chemin du journal

* **Description :** une option `--verdicts <chemin>` et une clé correspondante dans
  `killmutants.json`.
* **Pourquoi écartée :** le mainteneur a préféré le chemin dérivé de `--report-json`, qui ne demande
  aucun nouveau réglage.

## Conséquences

### Positives

* Une exécution tuée à n'importe quel moment laisse derrière elle chaque verdict qu'elle avait
  atteint.
* Le journal peut être suivi pendant l'exécution, et lu par une étape qui s'exécute après une étape
  annulée.
* Le verdict d'un mutant a une seule forme, dans le rapport comme dans le journal.

### Négatives

* Chaque exécution qui écrit un rapport JSON laisse un second fichier à côté, vidé au début de
  l'exécution suivante.
* Un chemin de rapport qui se termine par `.jsonl` est refusé, puisque ce serait celui du journal.
* Le journal ne dit ni combien de mutants l'exécution avait sélectionnés, ni si elle s'est terminée,
  ni ce qu'a trouvé `--verify-kills` : cela revient au rapport. Un journal sans rapport à côté est une
  exécution qui ne s'est pas terminée, et c'est tout ce qu'il peut dire de lui-même.
* Les lignes sont dans l'ordre où les verdicts sont arrivés, qui dépend du worker qui a fini le
  premier.

### Risques

* Un lecteur peut prendre le journal pour le résultat de l'exécution et en tirer des conclusions que
  seul un rapport fini permet.

### Actions de suivi

* L'étape de synthèse de `Reefact/slugger` peut lire le journal quand le rapport manque, au lieu
  d'être sautée.
