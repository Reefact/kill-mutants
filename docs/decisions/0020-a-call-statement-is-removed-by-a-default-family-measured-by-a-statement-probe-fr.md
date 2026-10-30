# DEC0020 | Une instruction d'appel est supprimée par une famille par défaut, et sa couverture est mesurée par une sonde placée avant elle

## Statut

| Date | Statut | Note | Compte rendu lié |
|---|---|---|---|
| 2026-10-30 | Proposé | Première rédaction | |
| 2026-10-30 | Accepté | | |

## Contexte

L'issue #21, ouverte à la suite de la comparaison du catalogue avec Stryker.NET 5.0.0 sur
`Reefact/slugger` (#15), y mesure le `Statement mutation` de Stryker à 147 mutants créés et 139
jugés, contre 199 créés et 29 jugés pour son `Block removal mutation`. Elle demande si la suppression
d'instructions a sa place dans le catalogue, dont l'inventaire refusait les bras de `switch` comme
« une mutation structurelle, pas une mutation d'opérateur », qui demande « un autre raisonnement sur
l'exhaustivité ».

Au commit `b826642` de Stryker.NET, son `StatementMutator` est actif au niveau par défaut et remplace
une instruction par `;`. Il supprime les instructions-expressions, un `return` sans valeur, `break`,
`continue`, `goto`, `throw` et `yield`. Il refuse une instruction-expression qui contient une
affectation ou un argument `out`, un `return` avec valeur, et le flux de contrôle placé dans une
section de `switch`, et laisse le reste à la compilation et au retour arrière de ses mutant schemata.

Chaque famille du catalogue mute une valeur ou une décision. Supprimer
`Directory.CreateDirectory(path);` ou `Config.Save(merged);` ne change ni l'une ni l'autre, et #21
rapporte que sur slugger, deux de ses trois exemples n'étaient vérifiés par aucun test jusqu'à ce que
des tests soient écrits pour eux. Supprimer une affectation à une variable locale peut la laisser non
assignée là où elle est lue (CS0165). Supprimer un `return`, un `throw` ou un `break` change ce que
le compilateur conclut de l'atteignabilité, comme l'a montré l'histoire de la sonde avec les
conditions constantes (RB-029). Une instruction vide comme corps d'un `if` provoque CS0642 ; un bloc
vide, non. Un appel à une méthode marquée `[Conditional]`, comme `Debug.Assert`, est retiré par le
compilateur quand son symbole n'est pas défini, ce qui est le cas dans le build Release qu'une
exécution fait par défaut.

DEC0007 mesure la couverture avec `Hit<T>(id, valeur)`, qui enveloppe une expression et renvoie sa
valeur. Une instruction n'a pas de valeur, et l'appel d'une méthode `void` ne peut pas être un
argument de type : `Hit` ne peut enregistrer ni l'une ni l'autre. Un site sans enregistreur fait
tester ses mutants contre toute la suite, qui prend environ 27 secondes par exécution sur slugger.

Les familles #16 à #19 sont entrées dans le catalogue par défaut ; `NumericLiteral` est sur demande
(DEC0019).

Le 2026-10-30, le mainteneur a tranché quatre points : la suppression d'instructions entre dans le
catalogue ; la première version ne supprime que les instructions d'appel ; leur couverture est
mesurée par une sonde au niveau de l'instruction ; la famille appartient au catalogue par défaut.

## Décision

Une famille `Statement` par défaut remplace par un bloc vide chaque instruction qui ne fait
qu'appeler quelque chose, et la passe de couverture enregistre une telle instruction par un appel
placé avant elle, dans un bloc qui leur est propre.

## Justification

La limite tracée aux bras de `switch` l'a été contre le raisonnement qu'ils demandent,
l'exhaustivité, et non contre la structure en tant que telle. Supprimer un appel n'en demande aucun :
la seule question est de savoir si le résultat compile, et le catalogue la pose déjà à chaque
famille. La suppression d'instructions respecte donc la règle du catalogue, et elle pose une question
qu'aucune autre famille ne peut poser : l'effet a-t-il seulement eu lieu.

Les appels seulement, parce qu'un appel est la seule instruction dont la suppression compile sans
analyse de flux. Les affectations et le flux de contrôle échouent sur l'affectation définie et
l'atteignabilité, ce qui fait précisément perdre au block removal de Stryker la plupart de ses
mutants ; les laisser de côté garantit que chaque mutant compile par construction, comme pour #16 à
#19. C'est aussi là que se trouvent les mutants d'instruction jugés par Stryker, qui refuse lui aussi
les affectations. Un appel qui écrit un argument `out` est une affectation en ce sens et est refusé
avec elles ; un appel `[Conditional]` est refusé parce que sa suppression est invisible dans le build
qu'une exécution fait, et que son mutant ne pourrait jamais être tué.

Un bloc vide, parce qu'il tient partout où l'appel se trouvait, sans avertissement propre.

Une sonde avant l'instruction, parce que l'alternative est toute la suite pour chacun de ces mutants,
et que la famille doit s'exécuter par défaut. Un appel placé avant une instruction, dans un bloc qui
ne déclare rien, ne change ni ce que fait l'instruction ni aucune portée, ce qui garde la propriété de
DEC0007 - l'instrumentation ne peut pas changer le comportement du programme - pour un site qui n'a
pas de valeur à renvoyer.

Par défaut, comme #16 à #19, parce que la famille porte un signal fort et que ses mutants compilent ;
l'activation sur demande est pour une famille dont le bruit coûterait à chaque projet qui ne l'a pas
demandée, ce que DEC0019 a réservé aux littéraux.

## Alternatives envisagées

### Alternative 1 — Garder la limite et refuser la suppression d'instructions

* **Description :** consigner que les mutations structurelles restent dehors, en citant la mesure de
  #21.
* **Raison du rejet :** la raison de la limite est le raisonnement sur l'exhaustivité, dont un appel
  supprimé n'a pas besoin, et la question qu'elle pose n'est posée par aucune autre famille.

### Alternative 2 — La portée de Stryker : le flux de contrôle en plus des appels

* **Description :** supprimer aussi `return;`, `break`, `continue` et `throw`.
* **Raison du rejet :** ils changent l'atteignabilité et l'affectation définie, si bien que certains
  mutants ne compileraient pas sans une analyse de flux que la famille ne fait pas.

### Alternative 3 — Les appels et les affectations à des champs

* **Description :** supprimer aussi `this.name = name;`, qui compile là où une affectation à une
  variable locale peut ne pas compiler.
* **Raison du rejet :** il faudrait distinguer champs et variables locales dans chaque affectation,
  pour une première version dont le but est de compiler par construction.

### Alternative 4 — Pas de sonde, toute la suite

* **Description :** laisser les sites d'instruction sans enregistreur.
* **Raison du rejet :** environ 139 × 27 s de plus sur slugger, pour une famille qui s'exécute par
  défaut.

### Alternative 5 — Sur demande, par `--with`

* **Description :** ne toucher ni au catalogue par défaut ni à ses scores.
* **Raison du rejet :** la plupart des utilisateurs ne verraient jamais une famille dont le signal est
  fort et dont les mutants compilent.

## Conséquences

### Positives

* Un appel dont aucun test ne vérifie l'effet est signalé, ce qu'aucune famille ne permettait
  jusque-là.
* Chaque mutant que la famille propose compile, et chacun ne s'exécute que contre les tests qui
  l'atteignent.

### Négatives

* Le catalogue par défaut de chaque projet grandit, si bien que son score n'est plus comparable à un
  score pris avant.
* Les appels dont l'effet est la journalisation ou le diagnostic sont supprimés aussi, et leurs
  survivants sont de vraies trouvailles de peu de valeur, comme la prose d'erreur de `StringLiteral`.
* Les affectations et le flux de contrôle restent non mutés.

### Risques

* Un appel dont la suppression ne change rien d'observable - un rafraîchissement idempotent, un
  préchauffage de cache - produit un mutant impossible à tuer.

### Actions de suivi

* Mettre à jour le tableau du catalogue et l'inventaire de ce qui n'est pas muté dans
  `architecture-en.md` et `architecture-fr.md`, ainsi que le `README.md`.
* Mesurer la famille sur slugger.
* Réexaminer les affectations à des champs une fois cette mesure disponible.
