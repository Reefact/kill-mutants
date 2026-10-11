# DEC0013 | Une exécution arrêtée rapporte ce qu'elle a jugé et ne passe aucune barrière

## Statut

| Date | Statut | Note | Compte rendu lié |
|---|---|---|---|
| 2026-10-11 | Proposé | Première rédaction | |
| 2026-10-11 | Accepté | | |

## Contexte

Une exécution garde ses verdicts en mémoire et n'écrit ses rapports qu'une fois, à la fin. Jusqu'à
cette décision, rien ne permettait d'arrêter une exécution avant son terme tout en obtenant un
rapport : la session acceptait un jeton d'annulation, qui interrompait l'exécution en levant une
exception, et aucun gestionnaire de SIGINT ou de SIGTERM n'existait, si bien qu'un signal mettait fin
au processus sans que rien ne soit écrit.

[L'issue #24](https://github.com/Reefact/kill-mutants/issues/24) a mesuré ce coût sur
`Reefact/slugger`, dont le workflow accorde 90 minutes à son job (`timeout-minutes: 90`). L'exécution
nocturne complète sur `main` a été annulée au bout de 90 minutes les 6, 8 et 9 octobre 2026, et a
duré 86 minutes le 7. Une exécution de pull request a passé cinquante-sept minutes à tester les
mutants d'un projet avant d'être annulée, sans que rien n'en subsiste. Menée localement jusqu'au bout
sur quatre cœurs avec `-p 4`, la même exécution a duré 16,1 minutes pour un projet (262 mutants) et
88,1 minutes pour l'autre (816 mutants) ; la CI utilise la valeur par défaut, soit la moitié des
processeurs.

GitHub Actions documente la manière dont il annule une étape : le runner envoie SIGINT au processus
d'entrée de l'étape — le shell, pour une étape `run:` —, puis SIGTERM 7,5 secondes plus tard, et tue
l'arbre de processus 2,5 secondes après. Un processus lancé par ce shell ne reçoit généralement aucun
signal avant d'être tué. GNU `timeout`, le moyen habituel d'envoyer un signal à l'outil lui-même, se
termine avec le code 124 quand son délai expire, quel que soit le code renvoyé par la commande qu'il
a lancée, sauf si on lui passe `--preserve-status`.

Les mutants sont testés projet par projet, dans l'ordre où la découverte a trouvé les projets ; au
sein d'un projet, les workers les traitent dans l'ordre de leur génération. Un mutant qui dépasse son
délai est réexécuté seul, une fois les workers terminés, avant que le dépassement de délai soit
considéré comme confirmé ; un dépassement de délai compte comme une détection. Un hôte de test qui se
termine anormalement est signalé comme un plantage, et un plantage compte comme un mutant tué.

Les codes de sortie sont un contrat public ([DEC0009](0009-exit-codes-are-a-public-contract-fr.md)) :
`0` pour une exécution dont la barrière est passée ou qui n'en avait aucune, `1` pour une barrière
qui n'est pas passée, `2` pour une exécution qui n'a pas pu avoir lieu, `64` pour une ligne de
commande incomprise ; selon cet enregistrement, un job fait échouer le build sur `1` et alerte
quelqu'un sur `2`. Une nouvelle cause d'un résultat qu'un code désigne déjà rejoint ce code. Un score
indéfini ne satisfait pas un seuil. Une exécution partielle est toujours une barrière
([DEC0010](0010-a-partial-run-reports-findings-not-a-score-fr.md)), n'affiche aucun score, et sort
avec `1` quand aucun des mutants qu'elle a sélectionnés n'a pu être testé, au motif qu'une telle
exécution a bien eu lieu mais n'a simplement rien établi. Une exécution partielle dont la comparaison
est incomplète ne conclut pas au succès et sort avec `1`
([DEC0012](0012-a-partial-run-abstains-when-the-earlier-state-is-incomplete-fr.md)).

## Décision

Une exécution arrêtée avant que chacun des mutants qu'elle a sélectionnés ait un verdict — par une
échéance fixée avec `--stop-after`, ou par le premier SIGINT ou SIGTERM qu'elle reçoit — rapporte les
verdicts qu'elle a rendus sans score ni verdict et ne passe aucune barrière, en sortant avec `1` quand
une barrière était demandée et avec `0` sinon.

## Justification

Une échéance que l'outil se fixe lui-même est le mécanisme qui fonctionne là où les exécutions ont été
perdues. Sur GitHub Actions, le signal est envoyé au shell et l'arbre de processus est tué quelques
secondes plus tard : un outil qui attend d'être prévenu ne l'est jamais. Pour que l'outil reçoive ce
signal, il faudrait un `exec` ou un `timeout` dans chaque workflow, et `timeout` remplacerait alors le
code de sortie par 124. Une échéance ne dépend pas de la réception d'un signal. Les signaux restent
gérés, pour la personne devant son terminal et pour tout ce qui en envoie un ; seul le premier est
intercepté, si bien qu'un second met toujours fin au processus immédiatement.

Pas de score, parce que les mutants jugés ne forment pas un échantillon. Ce sont des projets entiers,
dans l'ordre de la découverte, puis le début du suivant : un pourcentage calculé sur eux décrirait
les fichiers qui se trouvaient en tête. C'est l'objection de DEC0010 — un pourcentage sur une
population qui n'est pas la base de code — à laquelle on aboutit par un autre chemin.

Aucune barrière ne passe, parce qu'une barrière porte sur chacun des mutants de son périmètre et
qu'une exécution arrêtée n'en a jugé qu'une partie. C'est la règle qu'appliquent déjà un score
indéfini face à un seuil et une comparaison incomplète sur une exécution partielle : une exécution
qui n'a pas établi la réponse ne rapporte pas celle qui la ferait réussir. L'arrêt est une nouvelle
cause d'un résultat que `1` désigne déjà ; il rejoint donc `1` au lieu de recevoir son propre code.

`1` plutôt que `2`, parce que l'exécution a eu lieu. DEC0010 a tracé la même frontière pour une
exécution partielle dont aucun mutant n'avait pu être testé. `2` signifie que l'outil n'a pas pu
fonctionner, or une exécution nocturne qui atteint chaque nuit l'échéance qu'on lui a fixée n'a rien
cassé : interprétée chaque nuit comme une panne de l'outil, elle déclencherait une alerte ou finirait
désactivée. Sans barrière demandée, `0`, comme pour toute exécution qui se contente de rapporter ; la
sortie d'erreur indique qu'elle s'est arrêtée.

Un verdict rendu pendant l'arrêt de l'exécution n'est pas conservé. L'arrêt met fin aux hôtes de test
que l'exécution attend, et un signal qui arrête l'outil peut aussi avoir atteint un hôte ; dans les
deux cas, l'hôte est interprété comme un plantage, et un plantage compte comme un mutant tué.
Conserver ce verdict reviendrait à attribuer à la suite une détection due à l'arrêt, c'est-à-dire
précisément le type d'erreur que la confirmation des dépassements de délai sert déjà à empêcher. Pour
la même raison, un dépassement de délai qui n'a pas été réexécuté seul n'a pas encore de verdict.

## Alternatives envisagées

### Alternative 1 — Garder le statu quo

* **Description :** une exécution ne produit de rapport que lorsqu'elle se termine ; un signal ou le
  délai de la CI met fin au processus sans que rien ne soit écrit.
* **Pourquoi écartée :** une heure de mesure se solde par une absence totale d'information, ce qui
  s'est produit trois nuits sur quatre sur `Reefact/slugger`.

### Alternative 2 — Ne gérer que les signaux

* **Description :** écrire le rapport à la réception de SIGINT ou de SIGTERM, et laisser au workflow
  le soin de garantir que l'outil en reçoive un, comme l'issue le proposait au départ.
* **Pourquoi écartée :** sur GitHub Actions, l'outil ne reçoit rien à moins que chaque workflow ne le
  lance avec `exec` ou ne l'enveloppe dans `timeout`, et `timeout` remplace le code de sortie par 124.

### Alternative 3 — Ne gérer que l'échéance

* **Description :** proposer `--stop-after` et laisser aux signaux leur comportement par défaut.
* **Pourquoi écartée :** un Ctrl-C dans un terminal ferait encore perdre toute l'exécution.

### Alternative 4 — Sortir avec `2` pour une exécution arrêtée

* **Description :** traiter une exécution qui n'est pas allée à son terme comme une exécution qui n'a
  pas pu aboutir.
* **Pourquoi écartée :** `2` signifie que l'outil n'a pas fonctionné. Une exécution nocturne qui
  atteint son échéance serait signalée comme en panne chaque nuit.

### Alternative 5 — Sortir avec `130` ou `143` quand un signal a arrêté l'exécution

* **Description :** suivre la convention du shell : 128 plus le numéro du signal.
* **Pourquoi écartée :** ce serait un nouveau code pour une cause qu'un code existant désigne déjà, et
  une échéance n'a pas de numéro de signal : un même résultat aurait deux codes selon ce qui l'a
  provoqué.

### Alternative 6 — Afficher un score sur les mutants jugés

* **Description :** rapporter le pourcentage habituel, calculé sur les mutants que l'exécution a
  atteints.
* **Pourquoi écartée :** ces mutants sont le début de l'exécution, pas un échantillon, et le chiffre
  décrirait l'ordre dans lequel les projets ont été trouvés.

## Conséquences

### Positives

* Une exécution qui dépasse son budget de CI rapporte ce qu'elle a jugé, au lieu de ne rien laisser.
* Un Ctrl-C conserve le travail déjà accompli, et un second Ctrl-C met toujours fin au processus
  immédiatement.
* Une exécution soumise à une barrière ne conclut jamais à un succès qu'elle n'a pas établi, quelle
  que soit la façon dont elle s'est terminée.

### Négatives

* Une exécution arrêtée sans barrière sort avec `0` : une exécution nocturne qui atteint son échéance
  chaque nuit reste au vert, et les seuls indices en sont une ligne sur la sortie d'erreur et
  `complete: false` dans le rapport.
* L'échéance doit être fixée à la main sous le budget de la CI, en laissant une marge pour les
  mutants en cours et pour l'écriture du rapport. L'outil ne peut pas connaître le budget dont il
  dispose.
* Comme l'ordre est fixe, une exécution qui s'arrête à chaque fois juge toujours les mêmes premiers
  mutants, et les projets trouvés en dernier ne sont jamais atteints, quel que soit le nombre
  d'exécutions.

### Risques

* Un consommateur du rapport JSON qui lit `hasUndetected` sans lire `complete` peut prendre pour un
  succès une exécution arrêtée qui n'a encore rien trouvé.
* Un hôte tué par le même signal que l'outil pourrait, en principe, être vu se terminer avant que
  l'arrêt soit enregistré. L'arrêt est demandé dès que le signal arrive, alors que la fin d'un hôte
  n'est constatée qu'une fois que l'hôte a lui-même traité ce signal ; son verdict devrait donc être
  écarté, mais cela repose sur l'ordre des événements plutôt que sur une garantie.

### Actions de suivi

* Le workflow de `Reefact/slugger` peut passer à `--stop-after` une valeur inférieure à son
  `timeout-minutes` : c'est le cas qui a motivé cet enregistrement.
* Ce qui survit à un arrêt brutal dont l'outil n'est jamais prévenu n'est pas traité ici ; voir
  [DEC0014](0014-each-verdict-is-appended-to-a-log-beside-the-report-fr.md).
