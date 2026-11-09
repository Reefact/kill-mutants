# DEC0030 | Une pull request de Dependabot n'a pas besoin du Contributor Agreement tant que sa tête est le propre commit signé de Dependabot

## Statut

| Date | Statut | Note | Compte rendu lié |
|---|---|---|---|
| 2026-11-09 | Proposé | Première rédaction | |

## Contexte

DEC0029 n'accepte une pull request venant de toute autre personne que le propriétaire du projet
qu'une fois que son auteur a accepté le Contributor Agreement, en cochant une case dans la
description que porte le modèle de pull request. Un workflow, `contributor-agreement.yml`, refuse la
pull request tant que la case n'est pas cochée.

L'accord définit une Contribution comme un contenu que le contributeur **soumet intentionnellement**.
Il demande à ce contributeur de déclarer qu'il est juridiquement en droit de la soumettre et qu'il
détient les droits nécessaires pour conclure l'accord, et il prend une cession des droits
patrimoniaux cessibles sur ce qu'il a soumis.

Dependabot est une application GitHub, pas une personne juridique. `.github/dependabot.yml` lui dit
de surveiller chaque semaine les paquets NuGet et les GitHub Actions de ce dépôt, et ce qu'il soumet
est un numéro de version répondant à une publication en amont. Au 2026-11-09, deux de ses pull
requests étaient ouvertes, #111 et #112. Il ne remplit pas le modèle de pull request, si bien que la
case n'est jamais dans la description qu'il écrit : le contrôle échouerait sur chacune de ses pull
requests.

Quiconque a un accès en écriture peut ajouter des commits à une branche de Dependabot ; ouvrir une
pull request ne fige pas ce qu'elle porte ensuite. Le nom d'auteur d'un commit est un réglage local
de `git config`. Le commit de tête d'une pull request que Dependabot ouvre est signé par GitHub,
comme l'étaient les têtes de #111 et #112 le 2026-11-09, avec pour committer l'identité propre de
GitHub, `web-flow`. GitHub vérifie une signature par rapport au committer d'un commit, non à son
auteur : un commit dont l'auteur est l'adresse de Dependabot, signé par quelqu'un d'autre avec sa
propre clé, apparaît lui aussi comme vérifié, avec Dependabot pour auteur et cette personne pour
committer. `[` et `]` ne peuvent pas figurer dans un nom d'utilisateur GitHub, si bien que personne
d'autre ne peut enregistrer `dependabot[bot]`.

Une pull request de Dependabot reste celle de Dependabot, quoi que sa branche porte ensuite : son
auteur sur GitHub est `dependabot[bot]` et sa description est celle que Dependabot a écrite. Une case
cochée dans cette description est cochée dans la pull request du robot, et le contrôle ne peut
l'attribuer qu'à `dependabot[bot]`. Commenter `@dependabot recreate` sur la pull request fait
reconstruire sa branche par Dependabot, ce qui retire ce qui y avait été ajouté.

just-dummies, sous le même propriétaire du projet et le même accord, a réglé ce point dans son
ADR-0090, accepté le 2026-08-31, par l'exemption que ce record propose ; le mainteneur a choisi
d'aligner KillMutants sur just-dummies. Ce record a pesé le fait de cocher la case à la main sur
chaque pull request de Dependabot, d'exempter tout auteur robot, d'exempter Dependabot sur son seul
auteur, et de rendre le contrôle consultatif pour les robots. À toute autre tête, l'ADR-0090 demande
la case comme pour n'importe quelle autre pull request.

En relisant, le 2026-11-09, les premières versions de ce record et du contrôle, le mainteneur a
demandé qu'une pull request de Dependabot à toute autre tête soit refusée plutôt que jugée sur cette
case, puisque la cocher n'enregistre aucune acceptation de la personne qui a ajouté le commit, ou
bien que l'acceptation soit rendue attribuable à cette personne.

## Décision

Dans ce contexte, nous décidons qu'une pull request ouverte par Dependabot n'a besoin d'aucune
acceptation du Contributor Agreement tant que sa tête est le propre commit de Dependabot signé par
GitHub, et est refusée à toute autre tête, les commits qui y ont été ajoutés entrant par une pull
request que leur auteur ouvre.

## Justification

Le contrôle recueille une cession et un ensemble de déclarations, et Dependabot ne peut donner ni
l'une ni les autres : ce n'est pas une personne juridique, et le numéro de version qu'il écrit à la
demande de ce dépôt ne porte aucun droit d'un tiers qu'il aurait à céder. Exiger la case sur ses pull
requests, c'est demander un consentement que personne n'est en mesure de donner, et ne l'obtenir que
parce qu'un humain la coche à la place du robot.

Lever l'acceptation est le sens dans lequel une erreur coûte quelque chose, aussi la levée repose sur
ce que la branche porte plutôt que sur qui l'a ouverte. Quiconque a un accès en écriture peut ajouter
des commits à une branche de Dependabot, et ce qu'il ajoute est une Contribution au sens même de
l'accord ; un auteur se falsifie librement, une signature vérifiée faite avec une autre clé que celle
de GitHub aussi, mais la signature de GitHub, elle, ne se falsifie pas. Lier l'exemption à une
tête qui est encore le propre commit signé de Dependabot la retire dès que la branche porte autre
chose.

Ce dont la pull request a alors besoin, c'est de l'acceptation de la personne qui a ajouté le commit,
et sa propre description ne peut pas la contenir : une case cochée là l'est dans la pull request du
robot et ne peut se lire que comme celle du robot, ce qui est le consentement donné à la place d'un
autre pour lequel la première alternative ci-dessous est rejetée. Une pull request que son auteur
ouvre est l'endroit où cette acceptation est la sienne à donner ; les commits vont donc là, et la
pull request de Dependabot est refusée jusqu'à ce qu'elle soit revenue au seul travail de Dependabot.

L'exemption est celle de just-dummies, sous le même propriétaire et le même accord. À toute autre
tête, ce record s'écarte de l'ADR-0090, pour la raison ci-dessus.

## Alternatives envisagées

### Alternative 1 — Cocher la case à la main sur chaque pull request de Dependabot

* **Description :** laisser le contrôle tel quel, et faire modifier par le propriétaire du projet la
  description de chaque pull request de Dependabot pour cocher la case.
* **Pourquoi rejetée :** elle fait d'un contournement manuel d'un contrôle de gouvernance la voie
  ordinaire par laquelle arrivent les mises à jour de dépendances, et coche, à la place d'un robot,
  un consentement que personne n'a donné.

### Alternative 2 — Exempter tout auteur robot

* **Description :** lire le type du compte auteur et lever l'acceptation pour toute application
  GitHub.
* **Pourquoi rejetée :** elle donne la levée à toute application installée sur le dépôt, aujourd'hui
  ou plus tard, sur la foi d'un indicateur de type, ce qui est bien plus large que le cas qui l'a
  motivée.

### Alternative 3 — Exempter Dependabot sur son seul auteur, sans la tête signée

* **Description :** lever l'acceptation chaque fois que Dependabot a ouvert la pull request.
* **Pourquoi rejetée :** elle la lève pour tout ce que la branche porte plutôt que pour ce que
  Dependabot a écrit, et quiconque a un accès en écriture peut ajouter des commits à la branche.

### Alternative 4 — Rendre le contrôle consultatif sur les pull requests de robots

* **Description :** signaler l'acceptation manquante sur la pull request d'un robot sans la faire
  échouer.
* **Pourquoi rejetée :** le contrôle recueille un consentement, et un consentement consultatif ne
  recueille rien : un contrôle qui signale « non accepté » et laisse quand même fusionner a l'air
  d'une protection sans en être une.

### Alternative 5 — À toute autre tête, accepter la case cochée dans la pull request de Dependabot

* **Description :** juger une pull request de Dependabot qui porte le commit de quelqu'un d'autre
  comme n'importe quelle autre, sur la case de sa description, comme le fait l'ADR-0090.
* **Pourquoi rejetée :** la description est celle du robot, si bien que la case n'engage personne et
  est cochée, en pratique, à la place du robot ; elle n'enregistre aucune acceptation de la personne
  qui a ajouté le commit.

### Alternative 6 — À toute autre tête, recueillir l'acceptation de l'auteur du commit

* **Description :** garder la pull request de Dependabot, et exiger une acceptation faite par la
  personne qui a ajouté le commit, qui lui soit attribuable.
* **Pourquoi rejetée :** elle ajoute une seconde façon d'accepter l'accord, liée à qui a poussé quoi,
  pour un cas qu'une pull request à part traite déjà avec la seule façon qui existe.

## Conséquences

### Positives

* Une pull request de Dependabot n'est pas retenue par un consentement qu'elle ne peut pas donner.
* Aucune contribution ajoutée ne passe sous le nom de Dependabot : l'exemption prend fin avec la tête
  signée.

### Négatives

* Une lecture d'API de plus à chaque événement d'une pull request de Dependabot.
* Une pull request de Dependabot à laquelle quelqu'un a ajouté des commits est refusée, même quand
  l'ajout est trivial ou vient du propriétaire du projet lui-même : la modification va dans une pull
  request à part.

### Risques

* Si GitHub cessait de signer les commits de Dependabot, chaque pull request de Dependabot serait
  refusée : du bruit, et jamais dans le sens qui laisse passer une contribution sans consentement.

### Actions de suivi

Rien n'a été consigné au moment de la décision.
