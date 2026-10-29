# DEC0018 | Les motifs d'un `is` sont mutés par une famille à part, comme les opérateurs du catalogue

## Statut

| Date | Statut | Note | Compte rendu lié |
|---|---|---|---|
| 2026-10-29 | Proposé | Première rédaction | |
| 2026-10-29 | Accepté | | |

## Contexte

L'issue #19, issue de la comparaison du catalogue avec Stryker.NET 5.0.0 sur `Reefact/slugger` (#15),
a mesuré 36 sites de comparaison que Stryker mutait et KillMutants non, tous des motifs plutôt que des
opérateurs. `docs/architecture-fr.md` rangeait les motifs relationnels parmi les candidats futurs et
`is T` parmi ce qui n'est pas supporté pour l'instant, « comme une seule famille consciente des
motifs plutôt que deux règles ».

Lus au commit `b826642` de Stryker.NET, trois mutateurs couvrent les motifs.
`IsPatternExpressionMutator` nie le motif racine de chaque `is`, où qu'il se trouve.
`RelationalPatternMutator` réécrit `>` en `<` et `>=`, et de même pour chaque autre opérateur
relationnel. `BinaryPatternMutator` échange `or` et `and`. Aucun ne refuse un motif qui déclare une
variable, et l'exécution de Stryker sur slugger a compté 181 mutants qui ne compilaient pas.

Dans KillMutants, `NegatedDecision` (#18) nie un `is` écrit comme une décision - la condition d'un
branchement ou d'une boucle, ou un `bool` renvoyé - sous la forme `!(x is P)`, et rien ne nie un `is`
ailleurs. `Comparison` réécrit `>` en `>=`, son décalage de borne, et en `<=`, sa négation. Aucune
famille ne mute un motif relationnel ni les bras d'un `or` ou d'un `and`. Aucune famille ne mute une
expression qui déclare une variable (RB-016), et la sonde ne peut rien envelopper à l'intérieur d'un
motif (RB-019). C# analyse `x is T` comme un opérateur, non comme un motif.

Sur slugger à `c9cf4a2`, un décompte approximatif des expressions `is` de son code en a trouvé environ
157 : environ 71 déclarent une variable, comme `is not { } terms` ; environ 68 testent `null` ; environ
15 contiennent un motif relationnel ou un `or` ou un `and`, comme `is < 0 or > 100` et
`is "." or ".."`. L'analyseur qui portait l'essentiel des 36 sites de #19 a depuis été réécrit.

`not` lie plus fort que `or` et `and`. Échanger `or` pour `and` entre deux constantes donne un motif
qui ne correspond jamais. `x is not null and { Length: > 0 }` correspond exactement à ce à quoi
correspond `x is { Length: > 0 }`. Muter un bras d'un `switch` peut rendre un bras suivant
inatteignable, et le compilateur refuse alors tout le `switch` (CS8510).

Le mainteneur a demandé, le 2026-10-29, que la meilleure option soit retenue sur chaque point, et
qu'une question ne lui soit posée qu'en cas d'hésitation.

## Décision

Une famille `Pattern` nie chaque test `is` en `x is not P`, décale et nie chaque motif relationnel
d'un `is` comme `Comparison` le fait de ses opérateurs, et retire tour à tour chaque bras d'un `or` ou
d'un `and`, tandis que `NegatedDecision` lui laisse tous les `is`.

## Justification

Une seule famille possède le `is`, comme `Comparison` possède `==` : le nier dans la famille qui
comprend les motifs atteint les `is` qu'aucune décision ne porte, écrit le mutant dans la forme où le
code a été écrit, et évite que `NegatedDecision` juge une seconde fois le même mutant.

Le motif relationnel pose la question que `Comparison` pose au même opérateur : il reçoit donc les
mêmes deux mutants, et le lecteur du rapport les mêmes réponses. Quand il est le motif entier, sa
négation est omise, car `x is <= 1` demande ce que nier `x is > 1` demande déjà.

Retirer un bras pose la question que #19 posait d'un `or` - les deux bras sont-ils testés, ou un
seul ? - là où échanger l'opérateur construit le plus souvent un motif qui ne correspond jamais. Un
`and` garde son `not null`, parce que le bras voisin rejette déjà `null` et que le mutant ne
différerait jamais de l'original.

Les refus déjà présents dans le catalogue s'appliquent tels quels : un motif qui déclare une variable
n'est pas muté, et un motif ne porte pas d'enregistreur, si bien que ses mutants sont testés contre la
suite complète. Le `switch` est laissé de côté parce que l'échec qu'il provoque est celui du `switch`,
pas celui du nœud.

## Alternatives envisagées

### Alternative 1 — Les trois mutateurs de Stryker tels quels

* **Description :** nier le motif racine, réécrire `>` en `<` et `>=`, échanger `or` et `and`.
* **Pourquoi rejetée :** l'échange construit le plus souvent un motif qui ne correspond jamais, et `<`
  s'écarte de la paire relationnelle du catalogue sans rien ajouter à ce qui est demandé.

### Alternative 2 — Garder la négation dans `NegatedDecision` et ajouter le reste

* **Description :** laisser `!(x is P)` où il est, et ne confier à la nouvelle famille que les motifs
  relationnels et les bras.
* **Pourquoi rejetée :** un `is` qui n'est pas une décision resterait non muté, et les deux familles
  se rencontreraient sur tout `is` écrit comme une décision.

### Alternative 3 — Les motifs d'un `switch` aussi

* **Description :** muter aussi les motifs relationnels et les bras des instructions et expressions
  `switch`.
* **Pourquoi rejetée :** un mutant peut y rendre un bras suivant inatteignable, ce que le compilateur
  refuse pour tout le `switch` et qu'aucune vérification d'un nœud isolé ne peut voir.

## Conséquences

### Positives

* Un `is` est muté où qu'il se trouve, et pas seulement là où il est une décision.
* Les motifs relationnels et les bras de `or` et `and` sont mutés pour la première fois.
* Aucun motif qui déclare une variable n'est muté en un motif qui ne compile pas.

### Négatives

* `NegatedDecision` ne nie plus un `is` : ses mutants sur le même code changent d'identité.
* Les mutants à l'intérieur d'un motif ne portent pas d'enregistreur et sont testés contre la suite
  complète, ce qui est plus lent.
* Les motifs d'un `switch` restent non mutés.

### Risques

* Retirer un bras peut encore produire un `is` qui correspond toujours ou jamais quand les bras se
  recouvrent, ce qui donnerait un mutant impossible à tuer.

### Actions de suivi

* Mettre à jour la table du catalogue et l'inventaire de ce qui n'est pas muté dans
  `architecture-en.md` et `architecture-fr.md`.
* Mesurer la famille sur slugger.
* Revoir les motifs d'un `switch` si une vérification du `switch` entier devient disponible.
