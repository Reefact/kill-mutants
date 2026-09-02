# DEC0006 | Identifier les tests par leur nom, pas par leur identifiant unique

## Statut

| Date | Statut | Note | Compte rendu lié |
|---|---|---|---|
| 2026-08-31 | Accepté | | |

## Contexte

Le milestone 5 associera chaque mutant aux tests qui l'atteignent, afin de n'exécuter que ceux-là. Le
milestone 6 exécutera les mutants en parallèle, ce qui exige que chaque mutant concurrent dispose de
sa propre copie du répertoire de sortie des tests — sinon deux mutants écrasent mutuellement leur
assembly.

Ces deux plans entrent en collision, et la collision se situe dans le modèle de données, pas dans le
code.

Un test xUnit porte un identifiant unique, et `--filter-uid` sélectionne par celui-ci. Mais cet
identifiant dérive du **chemin** de l'assembly de test. Mesuré sur notre propre fixture, en comparant
deux copies identiques octet pour octet du même répertoire de sortie :

| | stable d'une copie à l'autre |
|---|---|
| `ID` (ex. `a3afdc575d78bd06…`) | **non** — tous différaient |
| `DisplayName` (ex. `Sample.Library.Tests.AgesTests.Adult_age_is_adult`) | **oui** — tous correspondaient |

Le runner offre par ailleurs des filtres par nom, vérifiés fonctionnels et composables :

| invocation | tests exécutés |
|---|---|
| sans filtre | 11 |
| `-method Sample.Library.Tests.AgesTests.Adult_age_is_adult` | 2 (ses deux cas de théorie) |
| le même, deux fois, pour deux méthodes | 3 (l'union) |
| `-class Sample.Library.Tests.AgesTests` | 11 |

Ces filtres sélectionnent par *méthode* : filtrer une `[Theory]` par son nom exécute tous ses cas. Un
mutant est tué par son premier cas en échec.

## Décision

Dans ce contexte, nous indexons la carte de couverture sur l'identité stable du test — le nom
qualifié `Namespace.Classe.Méthode` — et jamais sur l'identifiant unique.

## Justification

Une carte de couverture indexée sur les identifiants uniques perd tout sens dès que les mutants
s'exécutent dans des bacs à sable : les identifiants enregistrés pendant la passe de couverture
désigneraient des tests qui, dans un bac à sable, en portent d'autres. Puisque ces identifiants ont
été mesurés différents entre deux copies identiques octet pour octet d'un même répertoire de sortie,
les choisir reviendrait à choisir entre la sélection de tests et la parallélisation — les deux
milestones qui motivent la carte.

Le nom, lui, a été mesuré stable entre ces mêmes copies : l'indexer dessus rend les deux plans
indépendants, et aucun ne ferme la porte à l'autre.

Rien d'utilisable n'est abandonné avec `--filter-uid`, puisque les filtres par nom ont été vérifiés
sélectifs et composables. Leur granularité par méthode est plus grossière qu'une sélection par
identifiant, et c'est de toute façon la bonne : un mutant est tué par le premier cas en échec, donc
découper une théorie ajouterait de la complexité de filtrage sans rien apporter.

## Alternatives envisagées

### Alternative 1 — Indexer la carte sur l'identifiant unique et sélectionner avec `--filter-uid`

* **Description :** utiliser l'identifiant que le runner attribue déjà à chaque cas de test, et le
  filtre prévu pour lui.
* **Pourquoi écartée :** l'identifiant dérive du chemin de l'assembly de test, et a été mesuré
  différent entre deux copies identiques octet pour octet du même répertoire de sortie. Une carte
  indexée dessus ne survit qu'aussi longtemps que les mutants ne s'exécutent pas dans des bacs à
  sable, ce qui ferme la porte à la parallélisation de M6.

## Conséquences

### Positives

* La sélection de tests et la parallélisation deviennent indépendantes. Aucune ne ferme la porte à
  l'autre, et M6 peut être construit maintenant sans attendre la conception de la couverture.
* La carte reste lisible. Une entrée de couverture nommant
  `Sample.Library.Tests.AgesTests.Adult_age_is_adult` se comprend, se compare et se rapporte ;
  `a3afdc575d78bd06…` non.

### Négatives

* La sélection se fait par *méthode*, pas par cas de théorie : filtrer une `[Theory]` par son nom
  exécute tous ses cas.
* Un test renommé sort de la carte. C'est correct — c'est un autre test, il doit être remesuré plutôt
  qu'hériter silencieusement de la couverture de l'ancien — mais la couverture est perdue et doit
  être repayée.
* Nous renonçons à `--filter-uid`.

### Risques

*Non consigné au moment de la décision.*

### Actions de suivi

*Non consigné au moment de la décision.*
