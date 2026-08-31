# ADR-0001 — Consigner les décisions d'architecture

**Statut :** accepté · **Date :** 2026-08-31

## Contexte

KillMutants repose sur un petit nombre de décisions coûteuses à inverser : la façon dont un mutant
est appliqué, la façon dont les entrées de compilation sont obtenues, la façon dont les tests sont
exécutés. Dans six mois, le raisonnement qui les sous-tend sera invisible dans le code, et la
tentation de « corriger » un choix délibéré sera bien réelle.

## Décision

Les décisions structurantes sont consignées sous forme d'ADR courts dans `docs/adr`, numérotés
séquentiellement et jamais réécrits — une décision qui se révèle mauvaise donne lieu à un nouvel ADR
qui remplace l'ancien.

Un ADR n'est rédigé que lorsqu'une décision est **difficile à inverser** et admettait **plus d'une
réponse raisonnable**. Les choix qui découlent naturellement des contraintes affichées du projet ne
font pas l'objet d'un ADR.

## Conséquences

Il y a peu d'ADR, et chacun mérite d'être lu.
