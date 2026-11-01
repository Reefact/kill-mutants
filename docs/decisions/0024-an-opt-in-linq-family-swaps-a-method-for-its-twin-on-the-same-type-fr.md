# DEC0024 | Une famille `Linq` optionnelle échange une méthode avec sa contrepartie, quand celle-ci est une jumelle sur le même type

## Statut

| Date | Statut | Note | Compte rendu lié |
|---|---|---|---|
| 2026-11-01 | Proposé | Première rédaction | |
| 2026-11-01 | Accepté | À la demande du mainteneur | |

## Contexte

Le catalogue n'a aucune famille qui touche un appel LINQ. Stryker.NET en a une, que sa documentation
présente comme 34 paires qu'il échange - `Any` ↔ `All`, `First` ↔ `FirstOrDefault`, `Min` ↔ `Max`,
`Skip` ↔ `Take`, `OrderBy` ↔ `OrderByDescending`, `Sum` → `Max`, `Count` → `Sum`, `Average` →
`Min`, `Reverse` ↔ `AsEnumerable` entre autres. La documentation ne dit pas s'il reconnaît un appel
par son nom ou par la méthode à laquelle il se lie. Mesurée sur `Reefact/slugger` (#15), elle a créé
57 mutants Linq et en a jugé 16 ; #15 a classé la famille « à avoir, priorité basse ».

Sur `Reefact/first-class-errors` à `8f84258` (#44, #53, #63), les mutations Linq de Stryker ont donné
8 verdicts lisibles dans `FirstClassErrors.Analyzers`. Deux survivants `Any()` → `All()` ont été
confirmés à la main sur un clone propre : chaque échange passe les 132 tests des analyseurs, parce
qu'aucun test n'a un candidat qui satisfait le prédicat et un autre qui ne le satisfait pas. Les six
survivants `FirstOrDefault()` → `First()` sont quasi équivalents : cinq sont sur
`method.Locations.FirstOrDefault() ?? Location.None`, où un symbole issu du source a toujours un
emplacement, et le sixième, à `ErrorContextKeyFacts.cs:43`, garde un `null` qui ne peut pas se
produire parce que les deux surcharges de `ErrorContextKey.Create<T>` prennent `name` en premier
paramètre.

Les deux trous confirmés se lient différemment. À `DocumentedByInvalidSignatureAnalyzer.cs:47`, le
receveur est un `ImmutableArray<IMethodSymbol>`, donc `candidates.Any(...)` se lie à
`System.Linq.ImmutableArrayExtensions.Any`, et non à `System.Linq.Enumerable.Any` ; à
`ExampleDoesNotCallDocumentedFactoryAnalyzer.cs:76`, l'appel suit `.OfType<T>()` et se lie à
`Enumerable.Any`. L'API de Roslyn renvoie des `ImmutableArray` partout. Dans les assemblages de
référence de .NET 10.0.14, `System.Linq.ImmutableArrayExtensions` déclare `Any` et `All`, tandis que
`System.MemoryExtensions` ne déclare ni l'un ni l'autre sur `Span` ou `ReadOnlySpan`, ni `Min` ni
`Max`. Le même dépôt déclare une méthode à lui nommée `ErrorCodeFactory.Any()`, sans `All` à côté.

Une méthode peut retourner par référence - `ref bool Any()` - ce qu'`IMethodSymbol` signale à part
du type de retour, par `ReturnsByRef` et `ReturnsByRefReadonly`. Une variable locale `ref`
initialisée depuis un tel appel, `ref bool r = ref x.Any();`, ne compile pas depuis un appel qui
retourne par valeur.

Comptés par leur nom dans les projets de production de ce dépôt, et non par liaison : 13 `.Any(`,
dont 11 avec un prédicat, 9 `.OrderBy(`, 6 `.ThenBy(`, 2 `.Skip(`, 2 `.Except(`, 1 `.Concat(`, et
aucun `.All(`, `.Min(` ni `.Max(` ; contre 15 `.FirstOrDefault(` et 8 `.First(`. L'exécution
complète sur ce dépôt compte 3048 mutants.

Le catalogue a déjà ce qu'une famille de ce genre demande. Quatre mutateurs refusent un remplacement
qui ne se lierait pas, par `GetSpeculativeTypeInfo` (RB-011). `NumericLiteral` est optionnelle,
exécutée quand `--mutators` la nomme ou que `--with` l'ajoute, parce qu'« un littéral est partout »
et qu'un projet qui n'a rien demandé ne doit pas payer une exécution par littéral (DEC0019) ;
`Statement` est entrée dans le catalogue par défaut parce que son signal est élevé et que ses mutants
compilent (DEC0020). Un appel LINQ est une expression, et le `Hit<T>` de DEC0007 enregistre la
couverture d'une expression telle quelle.

Le 2026-11-01, le mainteneur a arbitré trois points : un appel n'est échangé que si la contrepartie
se lie à une méthode du même type conteneur avec le même type de retour ; les paires sont `Any` ↔
`All`, `Min` ↔ `Max`, `MinBy` ↔ `MaxBy`, `OrderBy` ↔ `OrderByDescending`, `ThenBy` ↔
`ThenByDescending`, `Order` ↔ `OrderDescending`, `Skip` ↔ `Take`, `SkipWhile` ↔ `TakeWhile`,
`SkipLast` ↔ `TakeLast`, `Union` ↔ `Intersect` et `Concat` ↔ `Except` ; la famille est d'abord
optionnelle, et son statut par défaut est décidé après qu'elle a été mesurée. À la relecture du compte
rendu, le mainteneur a demandé que la façon dont la valeur est retournée, par valeur ou par
référence, fasse partie de la règle.

## Décision

Dans ce contexte, nous décidons d'ajouter une famille `Linq` optionnelle qui échange le nom de la
méthode d'un appel avec sa contrepartie parmi les onze paires ci-dessus, quand la contrepartie se lie
à une méthode du même type conteneur que l'original, avec le même type de retour, retourné de la
même façon.

## Justification

Lier à une jumelle, plutôt qu'à `System.Linq.Enumerable` et `Queryable`, est ce qui atteint les deux
trous confirmés : l'un d'eux se lie à `ImmutableArrayExtensions`, et une règle qui nomme des classes
le raterait, comme elle raterait toute autre bibliothèque qui déclare la paire sur un type à elle. La
règle de la jumelle ne nomme rien et garde pourtant la garantie que l'issue demandait : `ErrorCodeFactory.Any()` n'a pas de `All` sur son
type et est laissé tranquille, et un type utilisateur qui déclare bien les deux noms avec la même
forme est échangé comme une paire que l'utilisateur a rendue comparable. Exiger le même type de
retour, retourné de la même façon, est ce qui fait compiler le mutant par construction, comme RB-011
le fait pour les opérateurs : la valeur de l'appel garde son type partout où elle est utilisée, et un
retour `ref` n'est jamais échangé contre un retour par valeur, ce qu'une variable locale `ref`
initialisée depuis l'appel refuserait.

Les onze paires sont celles dont les deux membres ont la même forme et dont l'échange change ce que
l'appel calcule - un quantificateur, un extrême, un ordre, un côté d'une séquence, une opération
ensembliste. Les paires écartées changent autre chose : `First` ↔ `FirstOrDefault` et ses semblables
échangent une exception contre un `null`, ce que l'échantillon montre quasi équivalent six fois sur
six ; `First` ↔ `Last` est équivalent sur une séquence d'un élément ; les échanges de `Sum`, `Count`
et `Average` changent le type ; `Reverse` ↔ `AsEnumerable` rencontre la surcharge
`MemoryExtensions.Reverse` sur les tableaux. Sur l'échantillon, garder les onze et écarter le reste
garde les deux trous et écarte les six quasi-équivalents.

Optionnelle d'abord, parce que la famille n'a jamais été mesurée par KillMutants : les 57 de #15 et
les 8 de l'échantillon sont ceux de Stryker, créés par une règle plus large. Le compte par nom
suggère une trentaine de mutants sur un dépôt qui en compte trois mille, ce qui est le profil de
`Statement` plutôt que celui de `NumericLiteral`, mais une famille du catalogue par défaut change le
score de chaque projet, et DEC0020 y a mis `Statement` sur une mesure. Promouvoir une famille coûte
peu ; en retirer une, non.

Une famille plutôt qu'un ajout à une famille existante, parce qu'aucune famille n'échange une méthode
contre une autre, et que `--with` et `--without` agissent sur des familles.

## Alternatives envisagées

### Alternative 1 — Aucune famille (statu quo)

* **Description :** laisser les appels LINQ non mutés, comme #15 avait classé la famille en priorité
  basse.
* **Pourquoi rejetée :** deux trous confirmés sur l'échantillon, dans des analyseurs, qu'aucune
  famille du catalogue ne peut trouver : la valeur de l'appel est une décision qu'aucune famille
  d'opérateurs ne touche.

### Alternative 2 — Le périmètre de Stryker, reconnu par le nom

* **Description :** les 34 paires, échangées partout où le nom apparaît.
* **Pourquoi rejetée :** sur slugger, cette règle a créé 57 mutants et en a jugé 16, et sur
  l'échantillon six de ses huit verdicts étaient des quasi-équivalents ; reconnue par le nom, elle
  échangerait aussi une méthode écrite par l'utilisateur. Le catalogue génère ce qui compile et pose
  une question, par construction.

### Alternative 3 — Lier à `System.Linq.Enumerable` et `Queryable` seulement

* **Description :** la proposition de l'issue : n'échanger qu'un appel lié à l'une de ces deux
  classes.
* **Pourquoi rejetée :** elle rate `ImmutableArrayExtensions.Any`, qui est l'un des deux trous qui
  motivent la famille, et avec lui toute bibliothèque qui déclare la paire sur un type à elle.

### Alternative 4 — Par défaut dès la première version

* **Description :** entrer dans le catalogue par défaut comme `Statement` l'a fait, sur la foi du
  compte par nom et de mutants qui compilent.
* **Pourquoi rejetée :** la famille n'a été mesurée par KillMutants sur aucun dépôt, et une famille
  par défaut change le score de chaque projet ; DEC0020 est entrée dans le catalogue par défaut sur
  une mesure.

## Conséquences

### Positives

* Un trou de la forme trouvée deux fois sur l'échantillon - un quantificateur qu'un test n'exerce que
  d'un côté - est rapporté, ce qu'aucune famille ne pouvait faire.
* Chaque mutant que la famille propose se lie, avec le type de l'appel et sa façon de le retourner
  inchangés, donc aucun n'échoue à compiler.
* La règle atteint tout type sur lequel une bibliothèque déclare la paire, `ImmutableArray` compris,
  et une méthode propre à l'utilisateur n'est échangée que si son type déclare la contrepartie avec
  la même forme.
* Le catalogue par défaut, et tout score pris avec lui, restent inchangés.

### Négatives

* Un projet ne voit la famille qu'en la demandant, par `--with Linq` ou `--mutators Linq`.
* `First` ↔ `FirstOrDefault` et les autres paires écartées ne sont pas posées, donc un trou de cette
  forme n'est pas trouvé.
* Une mesure sur deux dépôts, et une seconde décision, séparent cette version du catalogue par défaut.

### Risques

* Une paire gardée peut rester quasi équivalente dans un programme donné : `Skip` ↔ `Take` sur une
  séquence d'un élément, `Min` ↔ `Max` sur un singleton, `OrderBy` ↔ `OrderByDescending` sous un test
  qui ne vérifie que l'appartenance.
* Un type utilisateur qui déclare les deux noms d'une paire avec des sens sans rapport donne un
  mutant qui compile et ne pose aucune question.

### Actions de suivi

* Implémenter la famille dans la liste optionnelle, avec la liaison spéculative et la règle de la
  jumelle, et sa couverture par le `Hit<T>` de DEC0007 comme pour toute expression.
* Mettre à jour la table du catalogue et l'inventaire de ce qui n'est pas muté dans
  `architecture-en.md` et `architecture-fr.md`, le `README.md` et le CHANGELOG.
* Mesurer la famille sur `Reefact/slugger` et sur `Reefact/first-class-errors` à `8f84258`.
* Décider son statut par défaut par un nouveau compte rendu, sur cette mesure.
