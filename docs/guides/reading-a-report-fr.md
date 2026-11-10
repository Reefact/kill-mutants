# Lire un rapport

🌍 **Langues :**  
🇬🇧 [English](./reading-a-report-en.md) | 🇫🇷 Français (ce fichier)

Une exécution écrit deux choses : une progression, pour qui la regarde, et un rapport, pour qui doit
agir dessus. Cette page passe en revue les deux, puis le rapport JSON qu'un script lit, les fichiers
qu'une exécution laisse à côté de lui, et les codes de sortie.

## Deux flux, à dessein

La progression va sur l'**erreur standard** et le rapport sur la **sortie standard**, de sorte que
`killmutants > report.txt` capture le rapport sans qu'une ligne de progression s'y glisse. Ce que
montre l'erreur standard se choisit avec `--progress` ; rien n'est détecté à propos du terminal
([DEC0023](../decisions/0023-the-console-progress-is-chosen-by-an-option-and-defaults-to-a-log-that-reads-anywhere-fr.md)) :

| Mode | Pour | Ce qu'il montre |
| --- | --- | --- |
| `log` — le défaut | un journal de CI, un fichier, un tube, et quiconque n'a rien demandé | des lignes datées : chaque phase une fois, puis son décompte et le mutant en cours au plus une fois par minute |
| `live` | une personne devant un terminal | une seule ligne réécrite sur place : la phase, le décompte, une barre, le temps restant et les verdicts obtenus jusque-là |
| `none` | un outil qui lit le rapport JSON | rien, sauf les parties mises de côté |

Deux événements sont écrits à l'instant où ils se produisent, dans tous les modes sauf `none` : une
partie de l'exécution mise de côté, avec sa raison, et un mutant qui tourne encore après `--slow`
(30 secondes par défaut), une seule fois, avec le budget qui lui a été accordé. Ces lignes sont
destinées aux personnes et peuvent changer d'une version à l'autre ; un outil lit `--report-json`.

## Le rapport console, section par section

```text
KillMutants

Survived (2)

  Shipping.cs
     7:16  orderTotal >= 100m    -> orderTotal > 100m     [Comparison]
    12:38  weightInGrams <= 2000 -> weightInGrams < 2000  [Comparison]

No coverage (5)

  Labels.cs
    7:52  "Free shipping"      -> ""   [StringLiteral]
    7:70  "Standard shipping"  -> ""   [StringLiteral]
    ...

Mutants: 13
Killed: 6
Survived: 2
No coverage: 5

By mutator

  Comparison       5 mutants, 2 detected  (40%)
  Conditional      4 mutants, 2 detected  (50%)
  StringLiteral    2 mutants, 0 detected  (0%)
  LogicalOperator  1 mutant, 1 detected  (100%)
  NegatedDecision  1 mutant, 1 detected  (100%)

Mutation score: 46.15%
Elapsed: 11.1 s
Ran on: .NET 10.0.14 on Ubuntu 24.04.5 LTS, 2 of 4 processors, xUnit 4.0.0, timeout budget 31.2 s (0.4 s baseline alone × 3 + 30 s), no kills re-verified
```

### Les réserves viennent en premier

Tout ce qui change la façon dont le reste doit être lu est imprimé **avant** lui, pour qu'un lecteur
qui s'arrête tôt ne puisse pas emporter un chiffre sans la phrase qui le nuance. Une exécution
propre n'en a aucune. Les quatre qui existent :

* **Stopped before the end** — l'exécution a atteint `--stop-after` ou a été interrompue. Les
  verdicts ci-dessous tiennent ; les mutants qui n'en ont pas n'ont jamais été interrogés.
* **Set aside (n)** — une partie de l'exécution n'a pas pu être mesurée : un projet que MSBuild n'a
  pas pu lire, un projet de test qui n'a pas compilé, une référence (« baseline ») rouge. Chacune
  est nommée avec sa raison. Aucune barrière de qualité (gate) ne passe sur une telle exécution, et
  elle sort avec le code `2`.
* **Series '…'** — ce rapport additionne plusieurs exécutions d'une
  [série](./continuous-integration-fr.md#terminer-un-grand-projet-en-plusieurs-exécutions), et dit
  combien de verdicts viennent d'exécutions antérieures.
* **Comparison incomplete** — une [exécution partielle](./pull-requests-fr.md) n'a pas pu lire
  entièrement l'état antérieur, et retient son verdict.

### Constats

**Survived** et **No coverage** listent les mutants non détectés, regroupés par fichier et ordonnés
par position. Chaque ligne donne `ligne:colonne`, le code d'origine, le code muté et la famille. Les
textes longs sont coupés à soixante caractères avec des points de suspension ; le texte complet est
dans le rapport JSON.

### Décomptes

`Mutants`, `Killed`, `Survived` toujours ; `Timed out`, `Compile errors`, `No coverage` et `Ignored`
seulement quand ils se sont produits, pour qu'une exécution propre reste courte.

### Par mutateur

Ce que chaque famille a produit, ce que vos tests ont attrapé, et le ratio — affiché quand plus
d'une famille a été exécutée. C'est le seul chiffre sur lequel agir directement : une famille qui
produit un tiers des mutants et en détecte un dixième est soit le constat le plus précieux du
rapport, soit le moins précieux, selon le projet.
[Les mutateurs](./mutators-fr.md#choisir-un-catalogue) dit comment agir dessus.

### Ignorés par les règles du projet

Présente seulement quand `--ignore-calls` est en service : chaque règle avec les mutants qu'elle a
fait taire, et une règle qui n'a rien fait taire dite à voix haute — celle-là est une méthode
renommée ou un motif qui n'a jamais correspondu.

### Avertissements

Des lignes préfixées par `!`, placées juste avant le score pour qu'on ne puisse pas les manquer : un
verdict qui ne s'est pas répété sous `--verify-kills`, et ce genre de choses. Elles sont aussi dans
le rapport JSON, pour qu'un job de CI puisse agir dessus.

### Les trois dernières lignes

**Mutation score** — les détectés sur les détectés plus les non détectés, avec le nombre d'ignorés
à côté quand il y en a un. Une exécution partielle ou interrompue imprime ici une phrase à la place,
qui dit pourquoi il n'y a pas de score.

**Elapsed** — le temps écoulé, montre en main, pour toute l'exécution.

**Ran on** — le runtime, le système d'exploitation, les workers utilisés sur les processeurs
disponibles, la version de xUnit, le budget de délai sous la forme de la formule qu'il est, et le
nombre de mises à mort revérifiées. Deux rapports sans cette ligne ne sont pas comparables, et
personne ne s'en aperçoit.

## Revérifier les mises à mort

Un test instable ou dépendant de l'ordre produit une mise à mort que la mutation n'a pas causée.
`--verify-kills <n>` teste une seconde fois `n` des mutants rapportés tués, chacun seul, et rapporte
tout verdict qui ne survit pas à sa propre répétition — sous forme d'avertissement, et sur le mutant
lui-même dans le rapport JSON. Cela coûte une exécution des tests par mutant échantillonné, donc
c'est désactivé sauf demande.

## Le rapport JSON

`--report-json <path>` écrit le même rapport pour tout ce qui n'est pas une personne : un résumé de
CI, un tableau de bord, une différence entre deux exécutions. Il est délibérément plat, et il nomme
chaque mutant par une identité stable et sa position exacte plutôt que de résumer.

```json
{
  "tool": "KillMutants",
  "score": 0.4615,
  "scoreDisplay": "46.15%",
  "durationSeconds": 11.15,
  "scope": { "mode": "full", "comparedFrom": null, "comparedTo": null, "comparedToIsExact": true, "changedFiles": 0 },
  "environment": { "runtime": ".NET 10.0.14", "operatingSystem": "Ubuntu 24.04.5 LTS", "processorCount": 4, "workerCount": 2, "testFramework": "xUnit 4.0.0", "timeoutBudgetSeconds": [31.21], "baselineSecondsAlone": [0.4], "killsReVerifiedAlone": 0, "timeoutBaselineFactor": [3], "timeoutMarginSeconds": [30] },
  "totals": { "mutants": 13, "killed": 6, "survived": 2, "timedOut": 0, "compileErrors": 0, "noCoverage": 5, "detected": 6, "undetected": 7, "untestable": 0, "ignored": 0 },
  "ignoredCalls": [],
  "hasUndetected": true,
  "inconclusive": false,
  "complete": true,
  "mutantsSelected": 13,
  "setAside": [],
  "series": null,
  "coverageLost": [],
  "widened": [],
  "undetectedFromWidening": null,
  "comparisonIsIncomplete": false,
  "unreadComponents": [],
  "warnings": [],
  "byMutator": [ { "mutator": "Comparison", "mutants": 5, "detected": 2, "undetected": 3, "untestable": 0, "ignored": 0, "score": 0.4 }, "..." ],
  "mutants": [ "..." ]
}
```

| Champ | Signification |
| --- | --- |
| `score`, `scoreDisplay` | le score sous forme de ratio et tel qu'imprimé. Les deux valent **`null`** sur une exécution partielle ou interrompue. Quand une exécution complète n'a rien pu tester, `score` vaut `null` et `scoreDisplay` est la chaîne `"n/a"` — jamais un zéro qui n'a pas été mesuré, ni un pourcentage |
| `scope` | quelle population a été inspectée : `full`, ou `partial` avec les deux états comparés et le nombre de fichiers changés |
| `environment` | tout ce qui se cache derrière `Ran on`, sous forme de données |
| `totals` | les décomptes par statut, plus les trois qui définissent le score : `detected`, `undetected`, `untestable` ; et `ignored`, en dehors des trois |
| `hasUndetected`, `inconclusive` | la réponse binaire que donne une exécution partielle ; toutes deux ont aussi un sens sur une exécution complète |
| `complete`, `mutantsSelected` | si l'exécution a jugé tout ce qu'elle a sélectionné ; `complete` vaut `false` sur une exécution interrompue et sur une qui a mis quelque chose de côté |
| `setAside` | ce qui n'a pas pu être mesuré, avec son étape et sa raison |
| `series` | `null`, ou les exécutions que ce rapport additionne |
| `coverageLost`, `widened`, `undetectedFromWidening`, `comparisonIsIncomplete`, `unreadComponents` | ce qu'une [exécution partielle](./pull-requests-fr.md) a trouvé à propos du changement lui-même |
| `warnings` | les lignes `!`, sous forme de texte |
| `byMutator` | une entrée par famille exécutée ; `score` vaut `null` là où celui de l'exécution l'est |
| `mutants` | une entrée par mutant, ci-dessous |

Chaque entrée de `mutants` porte ce qu'il faut pour contester un verdict :

```json
{
  "key": "4a420df55ffb8fef",
  "id": "M8",
  "mutator": "Comparison",
  "status": "Survived",
  "outcome": "Undetected",
  "path": "Shop/Shipping.cs",
  "file": "/home/you/shop/Shop/Shipping.cs",
  "line": 7,
  "character": 16,
  "original": "orderTotal >= 100m",
  "mutated": "orderTotal > 100m",
  "ignoredBy": [],
  "killedBy": [],
  "disagreement": null,
  "overturned": null,
  "detail": null
}
```

* **`key`** est dérivée de ce que le mutant *est* — fichier, position, famille, les deux textes —
  de sorte que deux rapports du même commit peuvent être joints dessus même si l'`id` de la forme
  `M8` change d'une exécution à l'autre.
* **`killedBy`** nomme les tests qui ont échoué, dans la forme que le runner accepte, pour qu'une
  mise à mort mise en doute puisse être tranchée à la main : mettez la mutation dans le fichier à
  cette position, lancez ces tests, regardez-les échouer.
* **`disagreement`** n'est renseigné que sur un mutant que `--verify-kills` a retesté et dont la mise
  à mort ne s'est pas répétée. Le verdict d'origine est conservé, signalé comme non fiable.
* **`overturned`** est renseigné sur un mutant qui a dépassé son délai sous charge puis s'est terminé
  quand on l'a relancé seul, que `--verify-kills` ait été demandé ou non. Le second verdict remplace
  le délai dépassé, et ce champ le dit.

## Les fichiers à côté du rapport

Une exécution qui a demandé `--report-json artifacts/mutation.json` écrit aussi deux fichiers à
côté. Tous deux existent pour l'exécution qui n'arrive jamais à écrire son rapport : un job annulé
par un push plus récent, un runner perdu.

**`artifacts/mutation.jsonl` — le journal des verdicts.** Une ligne par verdict, exactement dans la
forme des entrées `mutants` du rapport, ajoutée à l'instant où le verdict est définitif. Un rapport
qu'une exécution antérieure a laissé à ce chemin est effacé au démarrage de l'exécution, donc un
journal sans rapport à côté est toujours une exécution qui n'a pas terminé
([DEC0014](../decisions/0014-each-verdict-is-appended-to-a-log-beside-the-report-fr.md)).

Au sein d'une exécution, cela fait une ligne par mutant. Sur une [série d'exécutions](./continuous-integration-fr.md#terminer-un-grand-projet-en-plusieurs-exécutions),
un dépassement de délai et une mise à mort qu'aucun test ne revendique sont jugés à nouveau par
l'exécution suivante, et le nouveau verdict est ajouté sous la même `key` : un mutant peut alors
apparaître sur plusieurs lignes. **La dernière ligne d'une `key` est le verdict de ce mutant** ; les
précédentes sont des verdicts qu'il a remplacés. Un lecteur du journal doit le lire ainsi — compter
les lignes compte les répétitions. Le rapport d'une série qui se termine compte chaque mutant une
fois, à partir de sa dernière ligne. Une exécution qui s'arrête avant d'avoir jugé à nouveau un tel
mutant ne rapporte que ce dont elle a hérité ou ce qu'elle a jugé elle-même : ce mutant est dans le
journal, sous son verdict antérieur, et pas dans le rapport de cette exécution
([DEC0015](../decisions/0015-a-resumed-run-inherits-the-verdicts-its-series-reached-on-the-same-code-fr.md)).

**`artifacts/mutation.plan.json` — le plan.** Quel code et quels réglages l'exécution a mesurés, et
chaque mutant qu'elle a sélectionné, de sorte que *plan moins journal* est ce qui reste après une
exécution tuée, lisible sans l'outil. Il est écrit avant que quoi que ce soit soit découvert, avec
la liste des mutants encore à `null`, et réécrit en entier une fois les mutants sélectionnés
([DEC0016](../decisions/0016-the-run-writes-its-plan-beside-the-report-fr.md)).

Un chemin de rapport se terminant par `.jsonl` ou `.plan.json` est refusé, parce que ces noms sont
pris.

## Codes de sortie

Les codes de sortie sont un contrat public : des scripts les encodent, donc un code n'est jamais
renuméroté, et une nouvelle cause d'un résultat qu'un code nomme déjà le rejoint plutôt que de
prendre un code à elle
([DEC0009](../decisions/0009-exit-codes-are-a-public-contract-fr.md)).

| Code | Signification |
| --- | --- |
| `0` | A tourné, et la barrière de qualité demandée est passée — ou vous n'en avez demandé aucune |
| `1` | A tourné, mais la barrière n'est pas passée : le score est sous `--break-at`, aucun mutant n'a pu être testé du tout, `--since` a trouvé un mutant non détecté dans le changement, ou l'exécution a été interrompue avant que la barrière ait pu être montrée comme passée |
| `2` | N'a pas pu tourner, ou a tourné avec une partie de sa portée mise de côté ; le rapport ou l'erreur standard dit pourquoi |
| `64` | La ligne de commande n'a pas été comprise |

La distinction qui compte est celle entre `1` et `2` : *vos tests sont plus faibles que ce que vous
avez demandé* et *l'outil n'a pas pu faire son travail* appellent des réactions différentes, et un
job qui ne sait pas les distinguer finira par traiter un environnement cassé comme une régression de
qualité — ou l'inverse. L'erreur standard dit toujours de quel cas il s'agissait :

```text
Mutation score 46.15% is below the 80% threshold.
```

## Pour aller plus loin

| Si vous voulez… | Lisez |
| --- | --- |
| comprendre ce que signifient les statuts et le score | [Concepts fondamentaux](./core-concepts-fr.md) |
| agir sur la répartition par famille | [Les mutateurs](./mutators-fr.md#choisir-un-catalogue) |
| lire les sections supplémentaires qu'imprime une exécution partielle | [Juger une pull request](./pull-requests-fr.md) |
| utiliser le journal et le plan à travers plusieurs jobs de CI | [Intégration continue](./continuous-integration-fr.md) |

---

[← Index de la documentation](../README-fr.md)
