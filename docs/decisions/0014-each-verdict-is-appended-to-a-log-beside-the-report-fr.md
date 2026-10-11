# DEC0014 | Chaque verdict est ajouté à un journal à côté du rapport dès qu'il est définitif

## Statut

| Date | Statut | Note | Compte rendu lié |
|---|---|---|---|
| 2026-10-11 | Proposé | Première rédaction | |
| 2026-10-11 | Accepté | | |

## Contexte

[DEC0013](0013-a-stopped-run-reports-what-it-judged-and-passes-no-gate-fr.md) permet à une exécution
arrêtée d'écrire son rapport — mais seulement lorsque l'arrêt vient d'une échéance qu'elle s'est
fixée ou d'un signal qu'elle reçoit. Certaines interruptions ne lui parviennent sous aucune de ces
deux formes. Sur GitHub Actions, un job annulé par une personne, ou par
`concurrency: cancel-in-progress` quand un push plus récent arrive — ce qu'utilise la CI de ce
dépôt —, se termine de la même façon qu'un job qui a dépassé son délai : un signal envoyé au shell de
l'étape, puis l'arbre de processus tué quelques secondes plus tard. Un runner perdu met fin à tout
sans le moindre signal. Et une exécution qui atteint sa propre échéance a encore des mutants en cours
et son rapport à écrire : une échéance fixée trop près du budget de la CI peut être devancée par
l'arrêt forcé du processus.

Quand un processus est tué, seul survit ce qu'il avait déjà transmis au système d'exploitation. Avant
cette décision, ce n'était rien : chaque verdict restait en mémoire jusqu'à l'écriture du rapport, à
la fin.

Le rapport JSON est lu par des étapes de CI après l'exécution.
[L'issue #24](https://github.com/Reefact/kill-mutants/issues/24) cite l'étape de synthèse de
`Reefact/slugger`, conditionnée par `hashFiles('killmutants-report.json') != ''` afin d'être ignorée
quand l'exécution n'a écrit aucun rapport. Le champ `complete` du rapport apparaît avec DEC0013 :
aucun consommateur écrit avant lui ne le lit. Son champ `hasUndetected` est ce que lit un
consommateur qui applique une barrière à partir du rapport pour décider si une exécution partielle a
réussi. [DEC0009](0009-exit-codes-are-a-public-contract-fr.md) considère que, pour un résultat mal
interprété, le cas dangereux est le cas silencieux : celui où une exécution est prise pour un succès
qu'elle n'a jamais été.

Une fois connu, le statut d'un mutant ne peut changer que de deux façons. Un dépassement de délai est
réexécuté seul, une fois les workers terminés, et peut aboutir à un autre statut. Un mutant tué tiré
au sort par `--verify-kills` est testé à nouveau, ce qui ne change jamais son statut mais peut lui
adjoindre un désaccord. Tout autre statut est définitif dès que le mutant a été testé.

Le mainteneur a demandé que ce qui est écrit soit nommé d'après le chemin de `--report-json` plutôt
que configuré par une option à part.

## Décision

Quand une exécution écrit un rapport JSON, elle ajoute aussi chaque verdict, dès qu'il devient
définitif, sous la forme d'une ligne JSON dans un journal placé à côté du rapport, qui porte le nom du
rapport avec l'extension `.jsonl`.

## Justification

Seul ce qui est sur le disque survit à l'arrêt forcé d'un processus : un verdict doit donc être
persisté dès qu'il devient définitif, et non à la fin de l'exécution. C'est tout l'argument en faveur
d'un journal, et il vaut pour chacune des interruptions que DEC0013 ne peut pas voir.

Un fichier à part, plutôt que le rapport réécrit au fil de l'exécution. Chaque version intermédiaire
d'un rapport réécrit occuperait l'emplacement où l'on attend un rapport terminé, et serait lue par des
consommateurs qui ignorent l'existence de `complete` — or, sur une exécution partielle,
`hasUndetected: false` dans un rapport à moitié écrit se lit comme un succès. C'est le cas silencieux
dont traite DEC0009. Avec un journal séparé, le fichier du rapport ne contient jamais qu'une exécution
terminée, ou une exécution qui indique qu'elle s'est arrêtée.

Uniquement des verdicts définitifs, pour qu'une ligne, une fois écrite, ne soit jamais contredite par
une ligne suivante. Un journal qui consignerait chaque statut dès qu'il est connu devrait être lu
selon la règle « la dernière ligne de chaque mutant l'emporte », et un consommateur qui le lirait
autrement compterait un dépassement de délai non confirmé comme une détection. Différer l'écriture
d'un dépassement de délai jusqu'à sa confirmation ne coûte que quelques lignes en fin de projet ; un
désaccord trouvé par `--verify-kills` reste dans le rapport, à côté du verdict qu'il nuance.

Une ligne au même format qu'une entrée de `mutants` du rapport, pour qu'un consommateur qui lit déjà
le rapport puisse lire le journal avec le même code, et que les deux ne désignent jamais un mutant de
deux façons différentes.

Nommé d'après le rapport, comme le mainteneur l'a demandé : un job qui nomme déjà son rapport obtient
son journal sans rien configurer, et qui trouve l'un des deux fichiers sait où chercher l'autre.

## Alternatives envisagées

### Alternative 1 — Pas de journal

* **Description :** s'en remettre au seul arrêt prévu par DEC0013, et n'écrire les verdicts que dans
  le rapport.
* **Pourquoi écartée :** cela ne couvre que les interruptions que l'exécution se fixe ou dont elle est
  prévenue. Un job annulé, un runner perdu ou une échéance devancée par l'arrêt forcé ne laisseraient
  toujours rien.

### Alternative 2 — Réécrire le rapport JSON au fil de l'exécution

* **Description :** réécrire le rapport toutes les quelques secondes, de façon atomique, en le
  marquant `complete: false` jusqu'à la dernière écriture.
* **Pourquoi écartée :** chaque fichier intermédiaire occupe l'emplacement où l'on attend un rapport
  terminé, et un consommateur qui ne lit pas `complete` prend pour un succès une exécution partielle
  qui n'a encore rien trouvé.

### Alternative 3 — Consigner chaque statut dès qu'il est connu

* **Description :** écrire une ligne dès que le statut d'un mutant est connu, puis une autre quand un
  dépassement de délai est confirmé ou qu'un mutant tué est revérifié, la ligne la plus récente
  remplaçant la précédente.
* **Pourquoi écartée :** chaque consommateur devrait appliquer la règle « la dernière ligne
  l'emporte », et celui qui ne le ferait pas compterait les dépassements de délai non confirmés comme
  des détections.

### Alternative 4 — Une option dédiée au chemin du journal

* **Description :** une option `--verdicts <chemin>` et une clé correspondante dans
  `killmutants.json`.
* **Pourquoi écartée :** le mainteneur a préféré le chemin dérivé de `--report-json`, qui ne demande
  aucun nouveau réglage.

## Conséquences

### Positives

* Une exécution tuée à n'importe quel moment laisse derrière elle tous les verdicts qu'elle avait
  déjà rendus.
* Le journal peut être suivi pendant l'exécution, et lu par une étape lancée après une étape annulée.
* Le verdict d'un mutant a un seul format, dans le rapport comme dans le journal.

### Négatives

* Chaque exécution qui écrit un rapport JSON laisse un second fichier à côté, vidé au début de
  l'exécution suivante.
* Un chemin de rapport se terminant par `.jsonl` est refusé, puisque ce serait celui du journal.
* Le journal n'indique ni combien de mutants l'exécution avait sélectionnés, ni si elle s'est
  terminée, ni ce qu'a trouvé `--verify-kills` : c'est le rôle du rapport. Un journal sans rapport à
  côté correspond à une exécution qui ne s'est pas terminée, et c'est tout ce qu'il peut dire de
  lui-même.
* Les lignes apparaissent dans l'ordre d'arrivée des verdicts, qui dépend du worker qui a terminé le
  premier.

### Risques

* Un lecteur peut prendre le journal pour le résultat de l'exécution et en tirer des conclusions que
  seul un rapport terminé autorise.

### Actions de suivi

* L'étape de synthèse de `Reefact/slugger` peut lire le journal quand le rapport manque, au lieu
  d'être ignorée.
