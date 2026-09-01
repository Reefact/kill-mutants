# ADR-0009 — Les codes de sortie sont un contrat public

**Statut :** accepté · **Date :** 2026-08-31

## Contexte

Un job de CI doit agir selon le résultat d'un run, et la seule chose qu'il voit de façon fiable est
le code de sortie. Des scripts figeront ce que nous choisissons : les renuméroter plus tard les
casserait silencieusement. Cela se décide donc une fois.

Les résultats qui méritent d'être distingués ne vont pas de soi. Un run peut :

- se terminer, avec un score qui satisfait l'utilisateur ;
- se terminer, avec un score inférieur à ce qu'il avait demandé ;
- ne pas pouvoir s'exécuter du tout — aucun projet trouvé, baseline rouge, environnement cassé ;
- ne jamais démarrer, parce que la ligne de commande n'avait pas de sens.

Tout réduire à « 0 ou non-zéro » est la simplification tentante. Elle échoue dès qu'un job veut
réagir différemment à *vos tests se sont affaiblis* et à *cet outil est cassé*. Un job incapable de
les distinguer finira par traiter un environnement cassé comme une régression de qualité — ou, bien
pire, par traiter un environnement réellement défaillant comme un build qui passe, parce que le run
n'a jamais eu lieu et que rien ne l'a dit.

## Décision

| code | signification |
|---|---|
| **0** | A tourné, et a atteint le seuil s'il y en avait un |
| **1** | A tourné, mais le score de mutation est inférieur à `--break-at` |
| **2** | N'a pas pu tourner ; la raison est sur la sortie d'erreur |
| **64** | La ligne de commande n'a pas été comprise |

`--break-at` est **optionnel**. Sans seuil, un score faible est rapporté et le run sort tout de même
en 0. Un seuil par défaut ferait de l'adoption de KillMutants une rupture pour tout build qui
l'ajouterait.

Un **score indéfini échoue face à un seuil**. Si rien n'a pu être testé, le run n'a rien démontré, et
rapporter un succès laisserait un job mal configuré rester vert indéfiniment.

`64` suit la convention `EX_USAGE`, ancienne et bien établie, ce qui la garde à l'écart de tout code
que le run lui-même pourrait vouloir signifier.

## Nous divergeons ici de Stryker.NET, délibérément

Stryker utilise `1` pour une erreur générale et `2` pour un seuil non atteint. Nous utilisons la
correspondance inverse, celle des linters et des formateurs : `1` signifie *ce que vous m'avez
demandé de vérifier n'est pas assez bon*, `2` signifie *je n'ai pas pu le vérifier*.

Les deux se défendent. La nôtre correspond à la façon dont l'outil est réellement invoqué — comme une
barrière de qualité aux côtés d'autres vérificateurs, où l'habitude d'un job de faire `|| exit 1`
devrait vouloir dire « constats », pas « plantage ». Quiconque scripte les deux outils devra lire ce
tableau : il est donc énoncé plutôt que laissé à découvrir.

## Conséquences

- Un job de CI peut être précis : échouer le build sur `1`, alerter quelqu'un sur `2`.
- La correspondance est désormais testable, et elle est testée en exécutant le vrai exécutable et en
  vérifiant le code qu'il renvoie, plutôt qu'en vérifiant la logique derrière. Tester autre chose
  reviendrait à tester notre intention au lieu du contrat.
- Ajouter un résultat plus tard signifie ajouter un code, jamais renuméroter un code existant.
