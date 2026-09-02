# DEC0001 | Consigner les décisions d'architecture

## Statut

| Date | Statut | Note | Compte rendu lié |
|---|---|---|---|
| 2026-08-31 | Accepté | | |

## Contexte

KillMutants repose sur un petit nombre de décisions coûteuses à inverser : la façon dont un mutant
est appliqué, la façon dont les entrées de compilation sont obtenues, la façon dont les tests sont
exécutés.

Dans six mois, le raisonnement qui les sous-tend sera invisible dans le code, et la tentation de
« corriger » un choix délibéré sera bien réelle.

Tous les choix du projet n'ont pas cette propriété. Certains découlent naturellement des contraintes
affichées et n'admettaient pas de seconde réponse raisonnable.

## Décision

Dans ce contexte, nous consignons toute décision structurante difficile à inverser et admettant plus
d'une réponse raisonnable sous forme d'enregistrement court dans `docs/decisions`, numéroté
séquentiellement et jamais réécrit.

## Justification

Une décision coûteuse à inverser est précisément celle dont le raisonnement doit survivre au code qui
l'implémente : le code sera lu bien avant que quiconque reconstitue pourquoi il a cette forme.

À ce moment-là le raisonnement est invisible dans le code, et l'enregistrement est le seul endroit où
il puisse vivre. Sans lui, un choix délibéré est indiscernable d'un accident — ce qui rend la
tentation de le « corriger » bien réelle.

Borner ce qui mérite un enregistrement aux deux conditions ci-dessus est ce qui garde la base à peu
d'enregistrements, chacun méritant d'être lu.

## Alternatives envisagées

*Non consigné au moment de la décision.*

## Conséquences

### Positives

Il y a peu d'enregistrements, et chacun mérite d'être lu.

### Négatives

*Non consigné au moment de la décision.*

### Risques

*Non consigné au moment de la décision.*

### Actions de suivi

*Non consigné au moment de la décision.*
