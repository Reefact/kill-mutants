# DEC0018 | Les motifs d'un `is` sont mutés par une famille dédiée, comme les opérateurs du catalogue

## Statut

| Date | Statut | Note | Compte rendu lié |
|---|---|---|---|
| 2026-10-29 | Proposé | Première rédaction | |
| 2026-10-29 | Accepté | | |

## Contexte

L'issue #19, ouverte à la suite de la comparaison du catalogue avec Stryker.NET 5.0.0 sur
`Reefact/slugger` (#15), recense 36 sites de comparaison que Stryker mutait et que KillMutants
ignorait. Tous étaient des motifs, et non des opérateurs. `docs/architecture-fr.md` classait les motifs
relationnels parmi les candidats futurs et `is T` parmi les formes non prises en charge pour
l'instant, à traiter « comme une seule famille consciente des motifs plutôt que deux règles ».

Au commit `b826642` de Stryker.NET, trois mutateurs traitent les motifs. `IsPatternExpressionMutator`
inverse le motif racine de chaque expression `is`, quel que soit son emplacement.
`RelationalPatternMutator` remplace `>` par `<` et par `>=`, et procède de même pour les autres
opérateurs relationnels. `BinaryPatternMutator` remplace `or` par `and` et inversement. Aucun ne
refuse un motif qui déclare une variable, et l'exécution de Stryker sur slugger a produit 181 mutants
qui ne compilaient pas.

Dans KillMutants, `NegatedDecision` (#18) inverse une expression `is` utilisée comme décision - la
condition d'un branchement ou d'une boucle, ou un `bool` renvoyé - sous la forme `!(x is P)`, et rien
n'inverse une expression `is` utilisée ailleurs. `Comparison` remplace `>` par `>=`, qui décale la
borne, et par `<=`, qui inverse la comparaison. Aucune famille ne mute un motif relationnel ni les
branches d'un motif `or` ou `and`. Aucune famille ne mute une expression qui déclare une variable
(RB-016), et la sonde ne peut rien envelopper à l'intérieur d'un motif (RB-019). C# analyse `x is T`
comme un opérateur, et non comme un motif.

Sur slugger, au commit `c9cf4a2`, un décompte approximatif relève environ 157 expressions `is` dans le
code : environ 71 déclarent une variable, comme `is not { } terms` ; environ 68 testent `null` ;
environ 15 contiennent un motif relationnel ou un motif `or` ou `and`, comme `is < 0 or > 100` et
`is "." or ".."`. L'analyseur qui contenait la majorité des 36 sites de #19 a été réécrit depuis.

`not` est prioritaire sur `or` et `and`. Remplacer `or` par `and` entre deux constantes produit un
motif qui ne correspond jamais à rien. `x is not null and { Length: > 0 }` correspond exactement aux
mêmes valeurs que `x is { Length: > 0 }`. Muter un bras d'un `switch` peut rendre un bras suivant
inatteignable, et le compilateur refuse alors le `switch` entier (CS8510).

Le 2026-10-29, le mainteneur a demandé de retenir la meilleure option sur chaque point, et de ne lui
poser une question qu'en cas d'hésitation.

## Décision

Une famille `Pattern` inverse chaque test `is` en `x is not P`, décale et inverse chaque motif
relationnel d'une expression `is` comme `Comparison` le fait pour ses opérateurs, et retire tour à tour
chaque branche d'un motif `or` ou `and`, tandis que `NegatedDecision` lui laisse toutes les
expressions `is`.

## Justification

Une seule famille prend en charge l'opérateur `is`, comme `Comparison` prend en charge `==`. Confier
l'inversion à la famille qui comprend les motifs permet également de muter les expressions `is`
utilisées hors des conditions de branchement, écrit le mutant sous la forme même du code d'origine, et
évite que `NegatedDecision` évalue une seconde fois le même mutant.

Le motif relationnel vérifie les mêmes cas limites que les mutations de `Comparison` : il reçoit donc
les deux mêmes mutants, et le rapport donne au lecteur les mêmes réponses. Quand le motif relationnel
constitue à lui seul le test, son inversion est omise, car `x is <= 1` vérifie la même chose que
l'inversion de `x is > 1`.

Retirer une branche répond à la question que #19 posait pour un motif `or` : les deux branches
sont-elles testées, ou une seule ? Remplacer l'opérateur, au contraire, produit le plus souvent un
motif qui ne correspond jamais à rien. Un motif `and` conserve sa branche `not null`, parce que
l'autre branche rejette déjà `null` et que le mutant se comporterait exactement comme l'original.

Les refus déjà présents dans le catalogue s'appliquent sans changement : un motif qui déclare une
variable n'est pas muté, et un motif ne porte pas d'enregistreur, si bien que ses mutants sont testés
contre la suite complète. Le `switch` est exclu parce que l'erreur qu'il provoque concerne le
`switch` entier, et non le nœud muté.

## Alternatives envisagées

### Alternative 1 — Reprendre les trois mutateurs de Stryker tels quels

* **Description :** inverser le motif racine, remplacer `>` par `<` et `>=`, échanger `or` et `and`.
* **Pourquoi rejetée :** l'échange produit le plus souvent un motif qui ne correspond jamais à rien,
  et `<` s'écarte de la paire relationnelle du catalogue sans rien vérifier de plus.

### Alternative 2 — Laisser l'inversion à `NegatedDecision` et ajouter le reste

* **Description :** conserver `!(x is P)` dans `NegatedDecision`, et ne confier à la nouvelle famille
  que les motifs relationnels et les branches.
* **Pourquoi rejetée :** une expression `is` utilisée hors d'une condition resterait non mutée, et
  les deux familles produiraient le même mutant pour toute expression `is` utilisée comme condition.

### Alternative 3 — Muter aussi les motifs d'un `switch`

* **Description :** muter également les motifs relationnels et les branches des instructions et
  expressions `switch`.
* **Pourquoi rejetée :** un mutant peut y rendre un bras suivant inatteignable, ce que le compilateur
  refuse pour tout le `switch`, et qu'aucune vérification limitée à un nœud ne peut détecter.

## Conséquences

### Positives

* Une expression `is` est mutée où qu'elle soit, et plus seulement lorsqu'elle sert de condition.
* Les motifs relationnels et les branches des motifs `or` et `and` sont mutés pour la première fois.
* Aucun motif qui déclare une variable n'est transformé en un motif qui ne compile pas.

### Négatives

* `NegatedDecision` n'inverse plus les expressions `is`, si bien que ses mutants sur le même code
  changent d'identité.
* Les mutants situés à l'intérieur d'un motif ne portent pas d'enregistreur et sont testés contre la
  suite complète, ce qui est plus lent.
* Les motifs d'un `switch` ne sont toujours pas mutés.

### Risques

* Retirer une branche peut encore produire une expression `is` qui correspond toujours, ou jamais,
  quand les branches se recouvrent, ce qui donnerait un mutant impossible à tuer.

### Actions de suivi

* Mettre à jour la table du catalogue et l'inventaire de ce qui n'est pas muté dans
  `architecture-en.md` et `architecture-fr.md`.
* Mesurer la famille sur slugger.
* Réexaminer les motifs d'un `switch` si une vérification portant sur le `switch` entier devient
  possible.
