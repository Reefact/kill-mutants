# DEC0013 | Une exécution arrêtée rapporte ce qu'elle a jugé et ne passe aucune barrière

## Statut

| Date | Statut | Note | Compte rendu lié |
|---|---|---|---|
| 2026-10-11 | Proposé | Première rédaction | |
| 2026-10-11 | Accepté | | |

## Contexte

Une exécution garde ses verdicts en mémoire et écrit ses rapports une seule fois, quand elle se
termine. Jusqu'à cette décision, rien ne permettait d'arrêter une exécution avant la fin tout en en
obtenant un rapport : la session acceptait un jeton d'annulation, qui abandonnait l'exécution sur une
exception, et aucun gestionnaire n'existait pour SIGINT ou SIGTERM, si bien qu'un signal mettait fin
au processus sans que rien soit écrit.

[L'issue #24](https://github.com/Reefact/kill-mutants/issues/24) a mesuré ce que cela coûte sur
`Reefact/slugger`, dont le workflow accorde à son job `timeout-minutes: 90`. L'exécution complète
nocturne sur `main` a été annulée à 90 minutes les 6, 8 et 9 octobre 2026, et a pris 86 minutes le 7.
Une exécution de pull request a passé cinquante-sept minutes à tester les mutants d'un projet et a été
annulée sans qu'il en reste rien. Exécuté localement jusqu'au bout sur quatre cœurs avec `-p 4`, le
même périmètre a pris 16,1 minutes pour un projet (262 mutants) et 88,1 minutes pour l'autre
(816 mutants) ; la CI tourne avec la valeur par défaut, la moitié des processeurs.

GitHub Actions documente la façon dont il annule une étape : le runner envoie SIGINT au processus
d'entrée de l'étape — le shell, pour une étape `run:` —, puis un SIGTERM 7,5 secondes plus tard, puis
tue l'arbre de processus 2,5 secondes après. Un processus lancé par le shell ne reçoit en général
aucun signal avant d'être tué. GNU `timeout`, le moyen habituel d'envoyer un signal à l'outil
lui-même, sort avec 124 quand son temps est écoulé, sauf si on lui passe `--preserve-status`, quoi
qu'ait renvoyé l'outil qu'il a lancé.

Les mutants sont testés projet par projet, dans l'ordre où la découverte a trouvé les projets, et au
sein d'un projet les workers les prennent dans l'ordre où ils ont été générés. Un mutant en timeout
est réexécuté seul, une fois les workers terminés, avant que le timeout soit cru ; un timeout compte
comme une détection. Un hôte de test qui se termine anormalement est
rapporté comme un plantage, et un plantage compte comme un mutant tué.

Les codes de sortie sont un contrat public ([DEC0009](0009-exit-codes-are-a-public-contract-fr.md))
: `0` pour une exécution dont la barrière est passée ou qui n'en avait reçu aucune, `1` pour une
barrière non passée, `2` pour une exécution qui n'a pas pu avoir lieu, `64` pour une ligne de
commande incomprise ; la lecture qu'en fait cet enregistrement est qu'un job fait échouer le build
sur `1` et alerte quelqu'un sur `2`. Une nouvelle cause d'un résultat qu'un code nomme déjà rejoint
ce code. Un score indéfini n'atteint pas un seuil. Une exécution partielle est toujours une barrière
([DEC0010](0010-a-partial-run-reports-findings-not-a-score-fr.md)), n'affiche aucun score, et sort
avec `1` quand tous les mutants qu'elle a sélectionnés étaient intestables, au motif qu'une telle
exécution a bien eu lieu et n'a simplement rien établi. Une exécution partielle dont la comparaison
est incomplète retient son succès et sort avec `1`
([DEC0012](0012-a-partial-run-abstains-when-the-earlier-state-is-incomplete-fr.md)).

## Décision

Une exécution arrêtée avant que chaque mutant qu'elle a sélectionné ait un verdict — par une échéance
fixée avec `--stop-after`, ou par le premier SIGINT ou SIGTERM qu'elle reçoit — rapporte les verdicts
qu'elle a atteints sans score ni verdict et ne passe aucune barrière, en sortant avec `1` quand une
barrière était demandée et `0` quand il n'y en avait aucune.

## Justification

Une échéance que l'outil se fixe lui-même est le mécanisme qui fonctionne là où les exécutions ont été
perdues. Sur GitHub Actions, le signal va au shell et l'arbre est tué quelques secondes plus tard :
un outil qui attend d'être prévenu ne l'est jamais. Le faire entendre demanderait un `exec` ou un
`timeout` dans chaque workflow, et `timeout` remplacerait alors le code de sortie par 124. Une
échéance ne dépend d'aucune livraison. Les signaux restent gérés, pour la personne devant un terminal
et pour ce qui en délivre un, et seul le premier est pris en charge : un second met toujours fin au
processus immédiatement.

Pas de score, parce que les mutants jugés ne sont pas un échantillon. Ce sont des projets entiers dans
l'ordre de la découverte, puis le début du suivant : un pourcentage calculé sur eux décrit les
fichiers qui se trouvaient en tête. C'est l'objection de DEC0010 — un pourcentage sur une population
qui n'est pas la base de code — atteinte par un autre chemin.

Aucune barrière ne passe, parce qu'une barrière est une affirmation sur chaque mutant de son
périmètre et qu'une exécution arrêtée n'en a jugé qu'une partie. C'est la règle que suit déjà un score
indéfini face à un seuil, et une comparaison incomplète sur une exécution partielle : une exécution
qui n'a pas établi la réponse ne rapporte pas celle qui la fait passer. L'arrêt est une nouvelle cause
d'un résultat que `1` nomme déjà ; il rejoint donc `1` au lieu de prendre un code à lui.

`1` plutôt que `2`, parce que l'exécution a eu lieu. DEC0010 a tracé la même limite pour une exécution
partielle dont tous les mutants étaient intestables. `2` signifie que l'outil n'a pas pu fonctionner,
et une exécution nocturne qui atteint chaque nuit l'échéance qu'on lui a donnée n'a rien cassé — lue
comme une panne de l'outil chaque nuit, elle déclencherait une alerte ou serait désactivée. Sans
barrière demandée, `0`, comme toute exécution qui ne fait que rapporter ; la sortie d'erreur dit
qu'elle s'est arrêtée.

Un verdict atteint pendant que l'exécution s'arrête n'est pas conservé. L'arrêt met fin aux hôtes de
test que l'exécution attend, et un signal qui arrête l'outil peut avoir atteint un hôte aussi ; dans
les deux cas l'hôte se lit comme un plantage, et un plantage compte comme un mutant tué. Le conserver
attribuerait à la suite une détection causée par l'arrêt, c'est-à-dire le sens d'erreur que la
confirmation des timeouts existe déjà pour empêcher. Pour la même raison, un timeout qui n'a pas été
réexécuté seul n'a pas encore de verdict.

## Alternatives envisagées

### Alternative 1 — Garder le statu quo

* **Description :** une exécution n'est rapportée que lorsqu'elle se termine ; un signal ou un délai
  de CI met fin au processus et rien n'est écrit.
* **Pourquoi écartée :** une heure de mesure devient une absence totale d'information, ce qui s'est
  produit trois nuits sur quatre sur `Reefact/slugger`.

### Alternative 2 — Ne gérer que les signaux

* **Description :** écrire le rapport sur SIGINT et SIGTERM, et laisser au workflow le soin de faire
  en sorte que l'outil en reçoive un, comme l'issue le proposait d'abord.
* **Pourquoi écartée :** sur GitHub Actions l'outil ne reçoit rien à moins que chaque workflow le
  lance avec `exec` ou l'enveloppe dans `timeout`, et `timeout` remplace le code de sortie par 124.

### Alternative 3 — Ne gérer que l'échéance

* **Description :** proposer `--stop-after` et laisser aux signaux leur comportement par défaut.
* **Pourquoi écartée :** un Ctrl-C dans un terminal jetterait encore toute l'exécution.

### Alternative 4 — Sortir avec `2` pour une exécution arrêtée

* **Description :** traiter une exécution qui n'est pas allée au bout comme une exécution qui n'a pas
  pu se terminer.
* **Pourquoi écartée :** `2` signifie que l'outil n'a pas fonctionné. Une exécution nocturne qui
  atteint son échéance serait rapportée comme cassée chaque nuit.

### Alternative 5 — Sortir avec `130` ou `143` quand un signal a arrêté l'exécution

* **Description :** suivre la convention du shell, 128 plus le numéro du signal.
* **Pourquoi écartée :** c'est un nouveau code pour une cause qu'un code existant nomme déjà, et une
  échéance n'a pas de numéro de signal : un même résultat porterait deux codes selon ce qui l'a
  provoqué.

### Alternative 6 — Afficher un score sur les mutants jugés

* **Description :** rapporter le pourcentage habituel, calculé sur ce que l'exécution a atteint.
* **Pourquoi écartée :** ces mutants sont le début de l'exécution, pas un échantillon, et le nombre
  décrirait l'ordre dans lequel les projets ont été trouvés.

## Conséquences

### Positives

* Une exécution qui dépasse son budget de CI rapporte ce qu'elle a jugé au lieu de rien.
* Un Ctrl-C conserve le travail déjà fait, et un second Ctrl-C met toujours fin au processus
  immédiatement.
* Une exécution à qui l'on a demandé une barrière ne rapporte jamais un succès qu'elle n'a pas établi,
  quelle que soit la façon dont elle s'est terminée.

### Négatives

* Une exécution arrêtée sans barrière sort avec `0` : une exécution nocturne qui atteint son échéance
  chaque nuit reste verte, et les seuls signes en sont une ligne sur la sortie d'erreur et
  `complete: false` dans le rapport.
* L'échéance doit être fixée à la main sous le budget de la CI, avec une marge pour les mutants en
  cours et l'écriture du rapport. L'outil ne peut pas connaître le budget sous lequel il tourne.
* Comme l'ordre est fixe, une exécution qui s'arrête toujours juge toujours le même début, et les
  projets trouvés en dernier ne sont jamais atteints, quel que soit le nombre d'exécutions.

### Risques

* Un consommateur du rapport JSON qui lit `hasUndetected` sans lire `complete` peut prendre pour un
  succès une exécution arrêtée qui n'a encore rien trouvé.
* Un hôte tué par le même signal que l'outil pourrait, en principe, être vu se terminer avant que
  l'arrêt soit enregistré. L'arrêt est demandé dès que le signal arrive, alors que la fin d'un hôte
  n'est remarquée qu'une fois que l'hôte l'a lui-même traité ; son verdict devrait donc être écarté,
  mais cela repose sur un ordre des événements plutôt que sur une garantie.

### Actions de suivi

* Le workflow de `Reefact/slugger` peut passer `--stop-after` sous son `timeout-minutes`, ce qui est
  le cas à l'origine de cet enregistrement.
* Ce qui survit à un arrêt brutal dont l'outil n'entend jamais parler n'est pas réglé ici ; voir
  [DEC0014](0014-each-verdict-is-appended-to-a-log-beside-the-report-fr.md).
