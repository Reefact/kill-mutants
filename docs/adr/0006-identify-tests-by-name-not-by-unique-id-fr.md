# ADR-0006 — Identifier les tests par leur nom, pas par leur identifiant unique

**Statut :** accepté · **Date :** 2026-08-31

## Contexte

Le milestone 5 associera chaque mutant aux tests qui l'atteignent, afin de n'exécuter que ceux-là.
Le milestone 6 exécutera les mutants en parallèle, ce qui exige que chaque mutant concurrent dispose
de sa propre copie du répertoire de sortie des tests — sinon deux mutants écrasent mutuellement leur
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

Une carte de couverture indexée sur les identifiants uniques perd donc tout sens dès que les mutants
s'exécutent dans des bacs à sable : les identifiants enregistrés pendant la passe de couverture
désigneraient des tests qui, dans un bac à sable, en portent d'autres. Choisir les identifiants,
c'était choisir entre la sélection de tests et la parallélisation.

## Décision

La carte de couverture est indexée sur l'**identité stable du test** — le nom qualifié
`Namespace.Classe.Méthode` — jamais sur l'identifiant unique.

La sélection utilise les filtres par nom du runner, vérifiés fonctionnels et composables :

| invocation | tests exécutés |
|---|---|
| sans filtre | 11 |
| `-method Sample.Library.Tests.AgesTests.Adult_age_is_adult` | 2 (ses deux cas de théorie) |
| le même, deux fois, pour deux méthodes | 3 (l'union) |
| `-class Sample.Library.Tests.AgesTests` | 11 |

## Conséquences

- La sélection de tests et la parallélisation deviennent indépendantes. Aucune ne ferme la porte à
  l'autre, et M6 peut être construit maintenant sans attendre la conception de la couverture.
- La sélection se fait par *méthode*, pas par cas de théorie : filtrer une `[Theory]` par son nom
  exécute tous ses cas. Plus grossier qu'une sélection par identifiant, et c'est de toute façon la
  bonne granularité — un mutant est tué par le premier cas en échec, donc découper une théorie
  ajouterait de la complexité de filtrage sans rien apporter.
- La carte reste lisible. Une entrée de couverture nommant
  `Sample.Library.Tests.AgesTests.Adult_age_is_adult` se comprend, se compare et se rapporte ;
  `a3afdc575d78bd06…` non.
- Un test renommé sort de la carte. C'est correct : c'est un autre test, il doit être remesuré
  plutôt qu'hériter silencieusement de la couverture de l'ancien.
- Nous renonçons à `--filter-uid`. Rien n'est perdu sur quoi nous aurions de toute façon pu compter.
