# Les mutateurs

🌍 **Langues :**  
🇬🇧 [English](./mutators-en.md) | 🇫🇷 Français (ce fichier)

Un **mutateur** est une règle qui réécrit une forme de code en une erreur précise. Les mutateurs
sont regroupés en **familles**, et chaque famille pose une question à vos tests. Cette page liste
les seize familles, ce que chacune demande, comment choisir celles qui s'exécutent, et ce que le
catalogue laisse volontairement tranquille.

## Ce qu'une famille demande

Chaque mutant coûte une exécution des tests, donc une famille mérite sa place par la question
qu'elle pose. Trois exemples montrent le principe.

**`Comparison` demande si une borne est testée.** `age >= 18` devient `age > 18` — la borne
décalée d'un cran — et `age < 18` — la comparaison inversée. Un test avec 17 et 42 tue le second et
laisse vivre le premier ; seul un test à exactement 18 tue les deux.

**`LogicalOperator` demande si chaque condition compte pour elle-même.** `age >= 18 && hasConsent`
devient `age >= 18 || hasConsent`. Une suite qui ne passe jamais qu'un adulte *avec* consentement
ne peut pas distinguer les deux ; un seul test où le consentement est refusé le peut.

**`Statement` demande si l'effet d'un appel est observé.** `register.Add(age);` devient `{ }`. Un
test qui appelle `Register` et n'affirme rien sur le registre passe dans les deux cas.

La section `By mutator` du rapport montre, par famille, combien de mutants elle a produits et
combien vos tests en ont attrapés — le nombre à regarder avant de décider ce que l'on garde.

## Le catalogue

Quinze familles s'exécutent par défaut. `NumericLiteral` ne s'exécute que sur demande.

| Famille | Réécrit | En |
| --- | --- | --- |
| `Comparison` | `>=` `>` `<=` `<` | la borne décalée d'un cran, et la comparaison inversée |
| `Comparison` | `==` `!=` | l'un en l'autre — il n'y a pas de borne à décaler |
| `LogicalOperator` | `&&` `\|\|` | l'un en l'autre |
| `Arithmetic` | `+` `-` `*` `/` `%` | son pendant |
| `Bitwise` | `&` `\|` `^` `<<` `>>` | son pendant |
| `Assignment` | `+=` `-=` `*=` `/=` `%=` `&=` `\|=` `<<=` `>>=` | son pendant |
| `Increment` | `++` `--` | l'un en l'autre, préfixé comme postfixé |
| `Conditional` | `c ? a : b` | `(c \|\| true) ? a : b` et `(c && false) ? a : b` — chaque branche forcée, la condition toujours évaluée |
| `NullCoalescing` | `a ?? b` | `a` ; et `b ?? a` quand `a` ne fait que lire une variable, un champ ou une propriété automatique, sinon `(a is null ? b : b)` — le repli forcé, `a` toujours évalué |
| `BooleanLiteral` | `true` `false` | l'un en l'autre |
| `Negation` | `!x` | `x` |
| `NegatedDecision` | la condition d'un `if`, d'une boucle ou d'un `bool` retourné | la décision inversée — sauf si une autre famille produit déjà ce mutant-là |
| `Pattern` | `x is P`, `x is T`, et les patterns à l'intérieur d'un `is` | le test inversé ; la borne d'un pattern relationnel décalée et inversée ; chaque branche d'un `or` ou d'un `and` supprimée |
| `StringLiteral` | `"text"` | `""`, et `""` en une chaîne non vide |
| `Statement` | une instruction qui ne fait qu'appeler quelque chose — `f();`, `await f();`, `x?.f();`, `new X();` — et un `yield return` | `{ }` |
| `Linq` | un appel à l'une de onze paires — `Any`/`All`, `Min`/`Max`, `MinBy`/`MaxBy`, `OrderBy`/`OrderByDescending`, `ThenBy`/`ThenByDescending`, `Order`/`OrderDescending`, `Skip`/`Take`, `SkipWhile`/`TakeWhile`, `SkipLast`/`TakeLast`, `Union`/`Intersect`, `Concat`/`Except` | son pendant, quand le pendant est un jumeau : même type conteneur, même type de retour |
| `NumericLiteral` *(activée à la demande, opt-in)* | un littéral entier qui initialise une constante, un champ ou une propriété | `n + 1` et `n - 1` |

Trois propriétés valent pour toutes, et ce sont elles qui rendent un verdict fiable :

* **Un mutant est un nouveau nœud, jamais un jeton échangé.** Roslyn émet d'après le type du nœud,
  donc ne changer que le texte de l'opérateur afficherait `age > 18` tout en compilant `age >= 18` —
  un mutant impossible à tuer, rapporté comme un survivant contre lequel vos tests n'ont jamais eu
  la moindre chance.
* **Un mutant qui ne compilerait pas n'est jamais généré.** Chaque famille demande au compilateur
  si son remplacement se lie avant de le proposer.
* **Un mutant connu pour être équivalent n'est jamais généré.** `NullCoalescing` ne supprime jamais
  en silence un opérande gauche qui fait quelque chose ; `Conditional` saute un ternaire dont les
  deux branches sont la même expression ; `NegatedDecision` n'écrit jamais `!!x`.

### Trois familles qui méritent un regard plus attentif

**`Conditional` force chaque branche plutôt que de les échanger.** `c ? a : b` devient
`(c || true) ? a : b` et `(c && false) ? a : b`. La condition est toujours évaluée, donc un effet
de bord dans `c` se produit encore, et chaque mutant interroge une seule branche : *y a-t-il un
test qui a besoin de `b` ?* puis *y a-t-il un test qui a besoin de `a` ?* Échanger les branches
poserait les deux questions à la fois et serait tué par l'une ou l'autre
([DEC0017](../decisions/0017-a-ternary-is-forced-each-way-rather-than-swapped-fr.md)).

**`NumericLiteral` est activée à la demande parce que les littéraux sont partout et que la plupart
ne sont pas des seuils.** `MinimumNouns = 100` devient `101`, puis `99`, ce qui demande si un test
vérifie un seuil *à* sa borne. Elle n'atteint une `const` que lorsque le projet lui-même la lit : un
assembly de test conserve la valeur qu'une constante avait au moment de sa compilation, donc une
constante que seuls les tests lisent survivrait à tout ce qu'ils affirment
([DEC0019](../decisions/0019-numeric-literals-are-an-opt-in-family-that-reaches-the-constants-its-project-reads-fr.md)).

```bash
killmutants --with NumericLiteral       # the default families plus this one
killmutants --mutators NumericLiteral   # this one alone
```

**`Linq` ne nomme aucune classe.** Un appel n'est échangé que lorsque le pendant se lie à un jumeau
de la méthode appelée — déclaré sur le même type, retournant le même type. Ainsi le `Any`
d'`ImmutableArray` est atteint comme l'est celui d'`Enumerable`, et une méthode de votre cru qui ne
fait que partager le nom est laissée tranquille
([DEC0024](../decisions/0024-an-opt-in-linq-family-swaps-a-method-for-its-twin-on-the-same-type-fr.md),
[DEC0027](../decisions/0027-linq-runs-by-default-and-without-leaves-it-out-fr.md)).

## Choisir un catalogue

Toutes les familles ne méritent pas leur place sur tous les projets, et le rapport dit lesquelles
la méritent. Mesurées sur le code source de KillMutants lui-même, les familles d'opérateurs —
`Comparison`, `LogicalOperator`, `Arithmetic` — détectent 45 % à 55 % de ce qu'elles produisent,
tandis que `StringLiteral` et `BooleanLiteral` font ensemble la moitié de tous les mutants et en
détectent 10 % à 15 % : des messages d'erreur et des drapeaux sur lesquels rien n'affirme quoi que
ce soit.

Ce sont de vrais constats. Sur un projet qui affirme sur ses messages, ce sont des constats utiles.
L'outil rapporte donc la répartition et vous laisse le choix :

```bash
killmutants --without StringLiteral,BooleanLiteral
killmutants --mutators Comparison,LogicalOperator,Arithmetic
```

| Option | Effet |
| --- | --- |
| `--mutators <list>` | n'exécuter **que** ces familles |
| `--with <list>` | ajouter celles-ci aux familles par défaut — c'est ainsi qu'une famille activée à la demande est mise en route |
| `--without <list>` | laisser celles-ci de côté, appliqué après les deux précédentes ; `--without none` rétablit toutes les familles qu'un `killmutants.json` exclut |

**Un score n'est comparable qu'avec un autre score issu du même catalogue.** Abandonner ces deux
familles sur ce dépôt a fait passer une exécution de 413 mutants en 7,1 minutes à 207 en 4,1, et le
score de 28,81 % à 43 % — non parce que les tests se sont améliorés, mais parce qu'une question
différente a été posée. Choisissez un catalogue pour un job de CI, écrivez-le dans
[`killmutants.json`](./configuration-fr.md) pour qu'il soit versionné avec le code, et gardez-le.
Le rapport JSON liste sous `byMutator` les familles qui ont réellement été exécutées, de sorte qu'un
consommateur peut savoir à quelle question il a été répondu.

## Faire taire les appels dont vous ne voulez pas entendre parler

Une famille est un instrument trop grossier pour un choix plus fin. Mesurée sur un dépôt,
`Statement` a laissé 144 survivants, dont 116 étaient des gardes d'arguments —
`ArgumentNullException.ThrowIfNull(x)` et consorts, couverts par des tests qui ne passent jamais
`null` — enterrant 9 vraies lacunes.

`--ignore-calls` nomme les appels dont un projet ne veut pas de constats, par le nom pleinement
qualifié de la méthode à laquelle l'appel **se lie** :

```bash
killmutants --ignore-calls 'System.ArgumentNullException.ThrowIfNull' \
            --ignore-calls 'System.ArgumentException.ThrowIf*' \
            --ignore-calls '*.ConfigureAwait'
```

* `*` et `?` fonctionnent comme dans `--exclude` ; la casse compte, comme en C#.
* Un constructeur s'écrit `System.ArgumentException.ctor`.
* Un simple `ThrowIfNull` ne reconnaît rien — une méthode de votre cru qui partage ce nom n'est pas
  attrapée — et le rapport dit qu'une règle qui n'a rien fait taire n'a rien fait taire.
* Un appel reconnu fait taire son instruction et ses arguments — le littéral de
  `ConfigureAwait(false)`, le message d'une exception — et rien au-delà : ni ce sur quoi l'appel
  est fait, ni le corps d'une lambda ou d'une fonction locale qu'on lui passe, qui est du code à
  part entière.

Un mutant réduit au silence est toujours généré, jamais testé, et rapporté comme **ignoré** : hors
du score, compté par famille et par règle, avec le total imprimé à côté du score lui-même. Le choix
appartient au projet, il est versionné dans `killmutants.json` sous `ignoreCalls`, et le rapport le
montre plutôt que de le cacher
([DEC0026](../decisions/0026-a-project-names-the-calls-it-silences-and-the-report-counts-what-it-chose-not-to-hear-fr.md)).

## Laisser du code tranquille

Trois mécanismes tiennent du code hors d'une exécution, et ils répondent à trois questions
différentes.

**`--exclude <pattern>` : ce n'est pas du code que je veux mesurer.** Fixtures, exemples, fichiers
générés. Le motif est comparé au chemin relatif au répertoire parcouru ; `*` correspond aussi à
`/`, donc `tests/*` couvre tout ce qui se trouve sous `tests`. Un *projet* exclu est entièrement
laissé hors de l'exécution ; un *fichier* exclu est toujours compilé mais jamais muté.

```bash
killmutants --exclude "tests/fixtures/*" --exclude "*.Generated.cs"
```

**`[ExcludeFromCodeCoverage]` : ce code ne fait pas partie de ce que les tests sont censés
couvrir.** L'attribut le dit déjà, et KillMutants le prend au mot : rien de ce qu'il couvre n'est
muté.

**`<KillMutantsTestSupport>true</KillMutantsTestSupport>` : ce projet est de l'échafaudage.**
Builders, fakes, horloges, assistants d'assertion — une bibliothèque de support de test n'est pas
le sujet, et la muter rapporte des constats que personne n'a cherché à mesurer. Rien de structurel
ne distingue une telle bibliothèque du code testé, donc elle le dit elle-même, dans son `.csproj` :

```xml
<PropertyGroup>
  <KillMutantsTestSupport>true</KillMutantsTestSupport>
</PropertyGroup>
```

Le projet est alors laissé de côté sans masquer ce qu'il référence : le code testé derrière lui est
toujours trouvé et toujours muté, et un assistant qui se trouve être une application de test
exécutable n'est pas non plus lancé comme une suite.

## Ce qui n'est délibérément pas muté

Le catalogue est sélectif plutôt qu'exhaustif. Chaque entrée ci-dessous a été mesurée en exécutant
le catalogue sur la forme concernée, et non lue dans la spécification du langage.

| Non muté | Pourquoi |
| --- | --- |
| `+x` | retirer un plus unaire ne change rien : le mutant est équivalent par construction |
| `~x` | n'importe quel test qui touche l'expression le tue ; presque gratuit à écrire, presque sans valeur |
| `?.`, `as` | tous deux mutent en des formes qui lèvent une exception plutôt que de calculer, donc ils mesurent si un test touche la ligne, pas s'il en vérifie le résultat |
| les patterns d'un `switch` | changer une branche peut rendre une branche ultérieure inatteignable, ce que le compilateur refuse pour tout le `switch` ([DEC0018](../decisions/0018-patterns-are-mutated-by-a-family-of-their-own-fr.md)) |
| les branches d'un `switch` | réordonner ou retirer des branches est une mutation structurelle, qui exige de raisonner sur l'exhaustivité |
| les autres échanges LINQ — `First` ↔ `FirstOrDefault`, `First` ↔ `Last`, `Sum`, `Count`, `Reverse` | quasi équivalents là où cela a été mesuré, ou un changement de type ([DEC0024](../decisions/0024-an-opt-in-linq-family-swaps-a-method-for-its-twin-on-the-same-type-fr.md)) |
| `>>>`, `^=`, `-x`, les littéraux numériques hors d'un initialiseur, les affectations et le flot de contrôle en tant qu'instructions | **candidats futurs**, chacun avec une raison consignée dans l'[architecture](../architecture-fr.md) |

Une expression qui *déclare* une variable — une variable de pattern, un argument `out` — n'est pas
mutée non plus : changer le moment où ses parties sont évaluées casse l'affectation définitive, et
le projet ne compile plus. L'appel qui l'entoure est toujours muté.

## Pour aller plus loin

| Si vous voulez… | Lisez |
| --- | --- |
| garder le catalogue dans un fichier à côté du code | [Configuration](./configuration-fr.md) |
| lire la répartition par famille dans le rapport | [Lire un rapport](./reading-a-report-fr.md) |
| savoir pourquoi le catalogue est sélectif plutôt que complet | [Principes de conception](./design-principles-fr.md) |

---

[← Index de la documentation](../README-fr.md)
