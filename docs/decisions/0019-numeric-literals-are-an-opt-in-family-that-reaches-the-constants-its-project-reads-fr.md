# DEC0019 | Les littéraux numériques forment une famille sur demande, décalée d'un cran dans chaque sens, qui atteint les constantes que lit leur projet

## Statut

| Date | Statut | Note | Compte rendu lié |
|---|---|---|---|
| 2026-10-29 | Proposé | Première rédaction | |
| 2026-10-29 | Accepté | | |

## Contexte

L'issue #20, ouverte à la suite de la comparaison du catalogue avec Stryker.NET sur `Reefact/slugger`
(#15), demande une famille sur demande pour les littéraux numériques. Ce n'est pas un retard sur
Stryker : au commit `b826642` de Stryker.NET, aucun de ses mutateurs ne réécrit un littéral numérique.
`docs/architecture-fr.md` classait les littéraux numériques parmi les candidats futurs, comme une
famille « classique et à fort signal, mais assez bruyante pour mériter sa propre activation plutôt
qu'une place dans le jeu par défaut ».

Stryker.NET rend un mutateur facultatif au moyen d'un niveau. Chaque mutateur déclare `Basic`,
`Standard`, `Advanced` ou `Complete`, une exécution applique les mutateurs jusqu'à son niveau, et le
niveau par défaut est `Standard` ; `Math`, `StringMethod`, `StringMethodToConstant`, `Regex` et
`CollectionExpression` sont `Advanced`, si bien qu'élever le niveau les active tous à la fois.
Stryker.NET ne mute jamais un champ `const` ni une variable locale `const` : son orchestrateur refuse
les deux, parce qu'un mutant placé là ne peut pas être activé à l'exécution.

slugger contient environ 175 littéraux numériques dans `src`, selon un décompte grossier. Ceux que
désigne #20 sont des seuils métier portés par des constantes, comme
`public const int MinimumNouns = 100;`, qu'une suite de tests peut vérifier autour plutôt qu'à la
borne. #20 décrit ce qui gardait cette famille utile dans les outils que son auteur a vus : les
valeurs des constantes et des initialiseurs avant les compteurs de boucle et les indices, `n + 1` et
`n - 1` plutôt que `n` vers `0`, et `0` et `1` laissés tranquilles sauf quand ils sont la valeur d'une
constante. Elle affirme que `n ± 1` sur les constantes et les initialiseurs de champ suffirait déjà à
répondre à sa question.

Dans KillMutants, toutes les familles appartiennent au catalogue par défaut. `--mutators` désigne les
seules familles à exécuter et `--without` en retire ; rien n'ajoute une famille au catalogue par
défaut. Chaque mutant est une compilation à part (DEC0002) : le projet testé est donc recompilé avec
chaque mutant, alors que l'assembly de test ne l'est pas. `MutationSite.IsObservable` tient toutes les
familles à l'écart d'une `const`, parce que C# recopie la valeur d'une constante dans chaque assembly
qui la lit, et qu'il a été vérifié qu'un mutant laissait sur la valeur d'origine une assembly de test
qui la lisait. La sonde de couverture est un appel, et l'initialiseur d'une constante doit être une
constante (CS0133).

Le type d'un littéral entier sans suffixe découle de sa taille : `2147483647 + 1` est donc un `uint`.
Une constante ne se convertit implicitement vers un entier plus étroit que si elle y tient :
`byte b = 256` échoue avec CS0031. Une énumération accepte le littéral `0` et aucun autre.

Le 2026-10-29, le mainteneur a demandé des décisions simples mais efficaces, et a tranché quatre
points : la famille n'atteint une `const` que là où le projet testé la lit ; on l'active en la
nommant dans `--mutators` ou en l'ajoutant avec une nouvelle option `--with` ; elle n'atteint que les
initialiseurs des constantes, des champs et des propriétés, `0` et `1` compris ; elle décale un
entier d'un cran dans chaque sens, et laisse les autres nombres tranquilles.

## Décision

Une famille `NumericLiteral` sur demande, exécutée quand `--mutators` la nomme ou que `--with`
l'ajoute, décale d'un cran vers le haut et d'un cran vers le bas chaque littéral entier de
l'initialiseur d'une constante, d'un champ ou d'une propriété, et n'atteint une `const` que là où le
projet testé la lit.

## Justification

L'activation sur demande est ce que le catalogue disait déjà de cette famille : un littéral est
partout, et un projet qui ne l'a pas demandée ne doit pas payer une exécution par littéral. Avec une
seule famille facultative, la nommer suffit ; les niveaux de Stryker servent à activer un groupe, et
ici un niveau désignerait une liste d'un seul élément. `--with` est la moitié manquante de
`--without` : elle active la famille sans retaper le catalogue par défaut, tandis que
`--mutators NumericLiteral` l'exécute toujours seule.

Un cran dans chaque sens interroge la borne, ce qui est la question que soulève #20 : une suite qui
vérifie « 99 noms sont refusés, 100 sont acceptés » tue les deux mutants, et une suite qui ne vérifie
que loin du seuil n'en tue aucun. Les deux mutants sont les deux côtés de la borne, comme le décalage
de `Comparison` l'est pour un opérateur. Les entiers seulement, parce qu'un cran sur une valeur
décimale ou flottante n'est pas un cran à l'échelle où cette valeur est écrite.

Les initialiseurs seulement, parce qu'une valeur qui porte un nom est une valeur que quelqu'un a
décidée, alors qu'un `0` ou un `1` dans une boucle ou un indice ne l'est généralement pas ; c'est la
première version que propose #20 elle-même. Dans un initialiseur, `0` et `1` sont eux aussi une
décision, et sont donc conservés.

Une `const` était la cible principale de la famille, et le seul endroit où le catalogue la refusait.
Le refus vaut pour une constante que seuls les tests lisent, dont le mutant survit quoi qu'ils
vérifient ; il ne vaut pas pour une constante que le projet lit lui-même, puisque le projet est
recompilé avec le mutant. Atteindre la seconde et non la première garantit que chaque mutant peut être
tué par un test. Les autres familles gardent le refus tel quel, si bien que le catalogue par défaut
génère exactement ce qu'il générait.

Un voisin qui changerait le type du littéral, ou ne tiendrait plus dans le type où il est utilisé,
n'est pas proposé, parce qu'un tel mutant soit ne compile pas, soit interroge autre chose que la
borne.

## Alternatives envisagées

### Alternative 1 — Laisser les constantes de côté, comme toutes les autres familles

* **Description :** garder le refus de `IsObservable` pour la nouvelle famille aussi.
* **Raison du rejet :** la famille manquerait la plupart des seuils dont parle #20, qui sont des
  constantes.

### Alternative 2 — Muter toutes les constantes

* **Description :** atteindre une `const` quel que soit son lecteur.
* **Raison du rejet :** une constante que seuls les tests lisent donne un mutant qui survit quoi
  qu'ils vérifient, signalé comme une lacune qui n'existe pas.

### Alternative 3 — `--mutators` seulement, sans `--with`

* **Description :** activer la famille en la nommant parmi les seules familles à exécuter.
* **Raison du rejet :** l'exécuter à côté du catalogue par défaut obligerait à nommer toutes les
  familles.

### Alternative 4 — Un niveau, comme celui de Stryker.NET

* **Description :** une option `--mutation-level` dont la valeur la plus haute active les familles
  facultatives.
* **Raison du rejet :** avec une seule famille facultative, un niveau n'est qu'un second nom pour
  cette famille.

### Alternative 5 — Tous les littéraux sauf `0` et `1`, ou tous les littéraux

* **Description :** atteindre aussi les littéraux du corps des méthodes, dont `random.Next(100)` et
  `< 100`.
* **Raison du rejet :** davantage de bruit, compteurs de boucle et indices compris, et `Comparison`
  décale déjà la borne de `< 100`.

### Alternative 6 — `n + 1` seulement, ou tous les types numériques

* **Description :** un seul mutant par littéral, ou le même cran sur les valeurs décimales et
  flottantes.
* **Raison du rejet :** un seul mutant ne teste qu'un côté de la borne ; un cran sur `0.05m` n'est
  pas un cran.

## Conséquences

### Positives

* Un seuil vérifié autour plutôt qu'à la borne est signalé, ce qu'aucune famille ne permettait
  jusque-là.
* Le catalogue par défaut ne change pas, si bien qu'un projet qui ne demande rien ne paie rien.
* `--with` activera de la même façon toute future famille sur demande.

### Négatives

* Un mutant placé dans une constante ne porte pas d'enregistreur, puisque la sonde n'est pas une
  constante, et il est testé contre toute la suite.
* Une constante lue seulement à travers une autre constante, un attribut ou la valeur par défaut d'un
  paramètre est laissée tranquille, même si le projet finit par la lire.
* La génération indexe une fois tous les identifiants du projet, dès qu'une constante contient un
  littéral entier, afin de savoir si le projet lit cette constante.

### Risques

* Un littéral dont les deux voisins ne changent rien d'observable - une capacité, une taille de
  tampon - produit des mutants impossibles à tuer.

### Actions de suivi

* Mettre à jour le tableau du catalogue et l'inventaire de ce qui n'est pas muté dans
  `architecture-en.md` et `architecture-fr.md`, ainsi que le `README.md`.
* Mesurer la famille sur slugger.
* Réexaminer les littéraux du corps des méthodes une fois cette mesure disponible.
