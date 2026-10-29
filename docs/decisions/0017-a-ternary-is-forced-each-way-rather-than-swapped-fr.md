# DEC0017 | La condition d'un ternaire est forcée dans chaque sens, toujours évaluée, au lieu d'échanger ses branches

## Statut

| Date | Statut | Note | Compte rendu lié |
|---|---|---|---|
| 2026-10-29 | Proposé | Première rédaction | |

## Contexte

La famille `Conditional` réécrit un ternaire `c ? a : b` en `c ? b : a` : un mutant par ternaire.
Elle ignore un ternaire dont les deux branches sont la même expression, et aucune famille ne mute une
expression qui déclare une variable (RB-016) ou qui en affecte une dans une partie que C# peut sauter
(RB-030).

L'issue #16, issue de la comparaison du catalogue avec Stryker.NET 5.0.0 sur `Reefact/slugger` (#15),
demandait de forcer une condition à une constante, « comme le fait la famille Conditional de
Stryker », sur les conditions de `if`, `while`, `do`, `for` et du ternaire. Lu au commit `b826642` de
Stryker.NET, `ConditionalExpressionMutator` ne s'applique qu'au ternaire. Il produit `(true ? a : b)`
et `(false ? a : b)`, si bien que la condition n'est plus évaluée, et ne refuse qu'une condition qui
déclare une variable de motif. Sa seule mutation d'une condition de `if` ou de `while` est une
négation (`NegateConditionMutator`), que `NegatedDecision` fait déjà dans KillMutants (#18).

Les trois mutants sont liés exactement. Sur un test où `c` est faux, l'échange et la condition forcée
à vrai donnent tous deux `a` là où l'original donne `b` ; sur un test où `c` est vrai, l'échange et la
condition forcée à faux donnent tous deux `b` là où l'original donne `a`. Un test qui observe le
ternaire tue donc l'échange si et seulement s'il tue l'un des deux mutants forcés. Un mutant forcé dit
en outre quelle branche aucun test ne vérifie, ce que l'échange ne peut pas dire.

Le catalogue ne supprime jamais un effet de bord en silence : `NullCoalescing` ne garde que son
opérande gauche, et n'échange ses opérandes que si l'opérande gauche se limite à lire une variable, un
champ ou une propriété automatique (RB-015). KillMutants n'a aucune analyse qui décide si une condition
quelconque est sans effet de bord. `c || true` et `c && false` évaluent `c` et sont des `bool` partout
où `c` en est un ; aucune n'est une constante de compilation, donc aucune ne change ce que le
compilateur conclut de l'atteignabilité (RB-029).

Une boucle forcée à `true` ne se termine pas, et chacun de ces mutants consommerait tout le budget de
temps (RB-010). Sur `Reefact/slugger`, `NegatedDecision` a trouvé 103 conditions de `if` et de
`while` ; le dernier run complet (run 38422204935) a jugé 63 mutants `Conditional`, dont 57 tués,
5 survivants et 1 non couvert. Chaque mutant coûte une exécution des tests.

Le mainteneur a choisi, le 2026-10-29, le ternaire seul, une forme qui garde la condition évaluée, et
les mutants forcés à la place de l'échange.

## Décision

La famille `Conditional` transforme chaque ternaire `c ? a : b` en `(c || true) ? a : b` et
`(c && false) ? a : b`, à la place de l'échange de branches `c ? b : a`.

## Justification

Les deux mutants forcés portent tout ce que porte l'échange, puisque celui-ci meurt exactement quand
l'un d'eux meurt, et ils disent en plus quelle branche n'est pas vérifiée. Garder l'échange à côté
d'eux coûterait une exécution des tests par ternaire, sans aucun verdict à ajouter.

Écrire la valeur forcée à côté de `c` plutôt qu'à sa place conserve les effets de bord de `c`, ce qui
est la règle du catalogue, sans l'analyse de pureté que KillMutants n'a pas. Cette forme se lie
partout où `c` se lie et ne tranche rien à la compilation : elle ne ramène ni mutant qui ne compile
pas, ni l'échec d'instrumentation de RB-029.

Le ternaire est l'endroit où Stryker force une condition, et celui où se trouvait l'échange. Un `if`
ou une boucle est déjà interrogé par `NegatedDecision`, forcer une boucle à `true` coûte un timeout par
mutant, et sur slugger cela ajouterait quelque deux cents mutants en plus de ceux des ternaires.

## Alternatives envisagées

### Alternative 1 — Garder l'échange seul (statu quo)

* **Description :** laisser `Conditional` produire `c ? b : a` et fermer #16.
* **Pourquoi rejetée :** l'échange ne peut pas dire quelle branche n'est pas vérifiée, un mutant forcé
  le peut.

### Alternative 2 — La forme de Stryker, `true ? a : b` et `false ? a : b`

* **Description :** remplacer la condition par le littéral, comme le fait Stryker.NET.
* **Pourquoi rejetée :** elle supprime l'évaluation de `c` et tout effet de bord qu'elle contient, ce
  que le catalogue refuse ; la limiter aux conditions sans effet de bord demanderait une analyse que
  KillMutants n'a pas.

### Alternative 3 — Les mutants forcés à côté de l'échange

* **Description :** trois mutants par ternaire.
* **Pourquoi rejetée :** le verdict de l'échange découle des deux autres ; il coûterait une exécution
  des tests par ternaire sans rien ajouter.

### Alternative 4 — Forcer aussi les conditions des `if` et des boucles

* **Description :** deux mutants forcés sur chaque condition de branchement et de boucle, comme #16
  le demandait d'abord.
* **Pourquoi rejetée :** Stryker ne le fait pas, `NegatedDecision` interroge déjà ces décisions, une
  boucle forcée à `true` coûte un timeout par mutant, et le compte sur slugger augmenterait de quelque
  deux cents.

## Conséquences

### Positives

* Un mutant `Conditional` survivant désigne la branche qu'aucun test ne vérifie.
* Aucun effet de bord de la condition n'est supprimé, contrairement à la forme de Stryker.
* Sur le ternaire, le catalogue couvre ce que couvre `Conditional` chez Stryker.

### Négatives

* Deux mutants par ternaire au lieu d'un : sur slugger, quelque 63 exécutions des tests de plus.
* Les mutants de la famille changent d'identité : les verdicts `Conditional` obtenus avant le
  changement ne se comparent pas à ceux obtenus après, et une série reprise à travers lui les juge à
  nouveau.
* Le rapport affiche `(c || true) ? a : b`, qui se lit moins directement qu'un libellé disant que la
  condition a été forcée à vrai.

### Risques

* Un ternaire dont la condition n'est pas un `bool` mais un type doté de son propre `operator true`
  peut ne pas lier `c || true` ; la famille doit interroger le compilateur, comme l'exige RB-011.

### Actions de suivi

* Réécrire la description de l'issue #16 au périmètre du ternaire que fixe cet enregistrement.
* Mettre à jour la table du catalogue dans `architecture-en.md` et `architecture-fr.md`.
* Mesurer la famille sur slugger avant et après.
