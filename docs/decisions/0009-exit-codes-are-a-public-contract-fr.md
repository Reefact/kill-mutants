# DEC0009 | Les codes de sortie sont un contrat public

## Statut

| Date | Statut | Note | Compte rendu lié |
|---|---|---|---|
| 2026-08-31 | Accepté | | |

## Contexte

Un job de CI doit agir selon le résultat d'un run, et la seule chose qu'il voit de façon fiable est le
code de sortie. Des scripts figeront ce que nous choisissons : les renuméroter plus tard les
casserait silencieusement. Cela se décide donc une fois.

Les résultats qui méritent d'être distingués ne vont pas de soi. Un run peut :

- se terminer, avec un score qui satisfait l'utilisateur ;
- se terminer, avec un score inférieur à ce qu'il avait demandé ;
- ne pas pouvoir s'exécuter du tout — aucun projet trouvé, baseline rouge, environnement cassé ;
- ne jamais démarrer, parce que la ligne de commande n'avait pas de sens.

Tout réduire à « 0 ou non-zéro » est la simplification tentante. Elle échoue dès qu'un job veut réagir
différemment à *vos tests se sont affaiblis* et à *cet outil est cassé*. Un job incapable de les
distinguer finira par traiter un environnement cassé comme une régression de qualité — ou, bien pire,
par traiter un environnement réellement défaillant comme un build qui passe, parce que le run n'a
jamais eu lieu et que rien ne l'a dit.

« La barrière n'est pas passée » avait déjà plus d'une cause avant qu'on pense à `--since`. Un score
inférieur à `--break-at` en est une ; un score **indéfini** parce que rien n'a pu être testé en est une
autre, et `Program.Verdict` échoue face à un seuil dans ce cas depuis avant le
[DEC0010](0010-a-partial-run-reports-findings-not-a-score-fr.md).

Stryker.NET utilise `1` pour une erreur générale et `2` pour un seuil non atteint. Les linters et les
formateurs utilisent la correspondance inverse : `1` signifie *ce que vous m'avez demandé de vérifier
n'est pas assez bon*, `2` signifie *je n'ai pas pu le vérifier*. `64` est la convention `EX_USAGE`,
ancienne et bien établie.

## Décision

Dans ce contexte, nous figeons les codes de sortie de KillMutants comme un contrat public — `0` pour
une exécution qui a atteint le seuil s'il y en avait un, `1` pour une exécution dont la barrière n'est
pas passée, `2` pour une exécution qui n'a pas pu avoir lieu, et `64` pour une ligne de commande non
comprise — avec `--break-at` optionnel et un score indéfini qui ne vaut jamais un seuil atteint.

## Justification

Quatre codes plutôt que deux, parce qu'un job incapable de distinguer *vos tests se sont affaiblis* de
*cet outil est cassé* finira par agir sur le mauvais — et la direction dangereuse est la silencieuse,
celle où un run qui n'a jamais eu lieu se lit comme un succès.

`1` est nommé d'après la barrière plutôt que d'après l'une de ses causes. Il couvre un score inférieur
à `--break-at`, un score indéfini parce que rien n'a pu être testé, et une exécution partielle qui a
trouvé ce sur quoi l'appelant lui a demandé d'échouer ou n'a rien pu établir du tout. Les trois disent
*ce que vous m'avez demandé de vérifier n'est pas passé*, ce sur quoi un script de build branche ; la
sortie d'erreur dit lequel. C'est pourquoi la constante s'appelle `GateNotPassed` et non
`ScoreBelowThreshold` — l'ancien nom était déjà faux pour le score indéfini.

`--break-at` est optionnel parce qu'un seuil par défaut ferait de l'adoption de KillMutants une
rupture pour tout build qui l'ajouterait.

Un score indéfini échoue face à un seuil parce que, si rien n'a pu être testé, le run n'a rien
démontré, et rapporter un succès laisserait un job mal configuré rester vert indéfiniment.

`64` suit `EX_USAGE`, ce qui la garde à l'écart de tout code que le run lui-même pourrait vouloir
signifier.

La correspondance est celle des linters plutôt que celle de Stryker.NET parce qu'elle correspond à la
façon dont l'outil est réellement invoqué — comme une barrière de qualité aux côtés d'autres
vérificateurs, où l'habitude d'un job de faire `|| exit 1` devrait vouloir dire « constats », pas
« plantage ».

## Alternatives envisagées

### Alternative 1 — Tout réduire à « 0 ou non-zéro »

* **Description :** rapporter un succès ou un échec et laisser l'opérateur lire les journaux pour en
  connaître la raison.
* **Pourquoi écartée :** elle échoue dès qu'un job veut réagir différemment à une suite de tests
  affaiblie et à un outil cassé, et son pire cas est silencieux : un environnement réellement
  défaillant lu comme un build qui passe, parce que le run n'a jamais eu lieu et que rien ne l'a dit.

### Alternative 2 — Suivre la correspondance de Stryker.NET

* **Description :** `1` pour une erreur générale et `2` pour un seuil non atteint, comme l'autre outil
  de mutation testing .NET.
* **Pourquoi écartée :** les deux correspondances se défendent, mais la nôtre correspond à la façon
  dont l'outil est invoqué — comme une barrière de qualité aux côtés des linters et des formateurs,
  dont elle partage la convention.

### Alternative 3 — Livrer un seuil par défaut

* **Description :** appliquer une valeur de `--break-at` d'origine, pour qu'un score faible fasse
  échouer un build sans configuration.
* **Pourquoi écartée :** cela ferait de l'adoption de KillMutants une rupture pour tout build qui
  l'ajouterait.

## Conséquences

### Positives

* Un job de CI peut être précis : échouer le build sur `1`, alerter quelqu'un sur `2`.
* La correspondance est testable, et elle est testée en exécutant le vrai exécutable et en vérifiant
  le code qu'il renvoie, plutôt qu'en vérifiant la logique derrière. Tester autre chose reviendrait à
  tester notre intention au lieu du contrat.

### Négatives

* Nous divergeons de Stryker.NET : quiconque scripte les deux outils doit tenir compte de la
  correspondance différente. La correspondance publique est documentée dans le `README.md` plutôt que
  laissée à découvrir.

### Risques

*Non consigné au moment de la décision.*

### Actions de suivi

* Ne jamais renuméroter un code. Un nouveau *genre* de résultat reçoit un nouveau code ; une nouvelle
  *cause* d'un résultat qu'un code nomme déjà le rejoint — c'est ce que le
  [DEC0010](0010-a-partial-run-reports-findings-not-a-score-fr.md) a fait à `1`. La distinction est ce
  sur quoi un script de build peut agir : il branche sur « des constats » contre « je n'ai pas pu
  vérifier », pas sur lequel des constats.
