# DEC0022 | Une exécution passe outre ce qui échoue, le met de côté avec sa raison, et rapporte ce qu'elle a mesuré et ce qu'elle n'a pas pu mesurer

## Statut

| Date | Statut | Note | Compte rendu lié |
|---|---|---|---|
| 2026-10-31 | Proposé | Première rédaction | |
| 2026-10-31 | Accepté | À la demande du mainteneur | |

## Contexte

[DEC0005](0005-verify-the-baseline-before-mutating-fr.md) vérifie la baseline par le chemin des
mutants et interrompt l'exécution, sans rapporter aucun résultat de mutation, lorsque cette baseline
n'est pas verte. Elle a été écrite face à une fixture d'un seul projet dont l'exécution des tests
prenait environ 0,6 s.

Une exécution mesure désormais plusieurs cibles l'une après l'autre - un projet sous test avec les
projets de test qui l'atteignent - et chaque cible voit sa baseline vérifiée avant que ses propres
mutants soient testés. Une baseline rouge sur une cible met toujours fin à toute l'exécution.

Les échecs sous le niveau de la cible sont déjà contenus. Un hôte de test qui plante coûte un mutant,
pas l'exécution (RB-003), et un mutant qui ne compile pas est mis de côté sans rien arrêter (RB-011).
Ce sont deux issues d'un mutant à l'intérieur du périmètre mesuré : le rapport compte un mutant qui ne
peut pas être testé comme non testable, et selon DEC0009 aucune des deux ne change le code de sortie.

La découverte lit chaque projet sous le répertoire pour savoir lesquels sont des projets de test. Un
projet qu'elle ne peut pas lire peut donc être un projet de test, et rien ne dit alors quel code de
production lui seul atteint : ces mutants peuvent se lire comme non couverts, ou leur cible peut ne pas
être sélectionnée du tout.

Sur `Reefact/first-class-errors` à `8f84258` (#44), deux arrêts de ce type sont survenus avant
qu'aucun résultat ne soit rapporté :

- La découverte s'est arrêtée au bout de 15 secondes sur `tools/floor-check/FloorCheck.csproj`, un
  projet hors de la solution qu'aucun projet de test ne référence, placé derrière un `global.json`
  imbriqué qui épingle un SDK absent de la machine (#47).
- La baseline de `FirstClassErrors.RequestBinder.UnitTests` était rouge au bout de 35 minutes
  d'exécution, à cause de l'endroit où le bac à sable plaçait les tests (#45), alors que `Analyzers`,
  `Cli`, l'essentiel de `GenDoc` et `RequestBinder.Usage` avaient été jugés. L'exécution n'a écrit
  aucun rapport. Les verdicts étaient dans le journal à côté du rapport
  ([DEC0014](0014-each-verdict-is-appended-to-a-log-beside-the-report-fr.md)), et `--resume`
  ([DEC0015](0015-a-resumed-run-inherits-the-verdicts-its-series-reached-on-the-same-code-fr.md)) en
  a récupéré 2186, pour qui savait où regarder.

Sur ce dépôt, les cinq cibles dont l'exécution reprise a vérifié la baseline ont pris chacune entre
1,5 s et 9,6 s pour cette seule baseline.

[DEC0013](0013-a-stopped-run-reports-what-it-judged-and-passes-no-gate-fr.md) définit déjà comment
rapporte une exécution qui n'a pas tout jugé : les verdicts rendus, sans score, sans passer aucun
seuil. [DEC0009](0009-exit-codes-are-a-public-contract-fr.md) fixe `2` comme code de sortie d'une
exécution qui n'a pas pu avoir lieu, et cite une baseline rouge parmi ses causes ; `1` est un seuil
non atteint.

L'attente du mainteneur, formulée pour cette décision : une exécution avance sur tout ce qu'elle peut,
à chaque niveau - projets, mutateurs, mutants -, ne s'arrête que lorsque plus rien ne peut avancer, et
se termine par un rapport de ce qu'elle a pu faire, de ce qu'elle n'a pas pu faire et pourquoi, avec
le bilan que la partie mesurée permet de dresser. Le mainteneur ne voit pas pourquoi un utilisateur
voudrait plutôt que l'exécution s'arrête au premier échec.

## Décision

Dans ce contexte, une exécution met de côté toute partie de son périmètre qu'elle ne peut pas mesurer
- un projet qu'elle ne peut pas lire, une cible qu'elle ne peut pas construire ou dont la baseline
n'est pas verte - avec sa raison, poursuit sur tout le reste, ne s'arrête plus tôt que lorsque plus
rien ne peut avancer, et se termine par un rapport de ce qu'elle a mesuré et de ce qu'elle n'a pas pu
mesurer, un score seulement lorsque la population qu'il couvre peut être établie et étiqueté par cette
partie, aucun seuil passé, et le code de sortie `2`, tandis qu'un mutant qui ne peut pas être jugé
reste une issue à l'intérieur du périmètre mesuré.

## Justification

La baseline de chaque cible est vérifiée avant ses propres mutants, donc la garantie pour laquelle
DEC0005 existe tient cible par cible : une baseline rouge sur une cible ne dit rien de faux sur une
cible dont la baseline était verte, et les verdicts de la seconde restent aussi fiables que dans une
exécution où la première aurait été absente.

Poursuivre coûte peu, même quand la cause est globale. Si la reconstruction ou l'environnement est
cassé pour toutes les cibles, toutes les baselines suivantes échouent aussi, et chaque échec coûte une
exécution de baseline - quelques secondes sur le dépôt mesuré - avant que la cible soit mise de côté.
Les heures que l'interruption de DEC0005 semblerait épargner ne sont pas dépensées, puisqu'aucun
mutant n'est testé sur une cible dont la baseline a échoué.

C'est l'arrêt qui a fait perdre le travail. Les 35 minutes de verdicts sur `first-class-errors`
étaient mesurées correctement et restaient invisibles faute de rapport ; les récupérer supposait de
savoir que le journal et `--resume` existaient. Une exécution qui poursuit les rapporte par
construction.

La même règle vaut déjà sous la cible, où un hôte qui plante ou un mutant qui ne compile pas coûte ce
mutant et rien de plus (RB-003, RB-011). L'étendre aux projets et aux cibles donne une seule règle
pour tous les niveaux, ce qui est l'attente formulée par le mainteneur. Un mutant qui ne peut pas être
jugé ne laisse aucune partie du périmètre non mesurée : c'est une issue du périmètre, comptée comme
non testable, et la traiter comme un échec de l'exécution laisserait un mutant ordinaire faire passer
au rouge une exécution complète. Seul ce qui laisse une partie du périmètre prévu non mesurée est mis
de côté.

Un score sur la partie mesurée est utile, et ne trompe que s'il se lit comme le tout ; étiqueté par la
partie qu'il couvre et imprimé à côté de ce qui a été mis de côté, il dit ce qu'il est. Cela ne vaut
que lorsque la partie couverte est connue. Une cible mise de côté après la découverte laisse les
populations des autres cibles telles qu'elles étaient ; un projet mis de côté à la découverte a pu
être la seule suite à atteindre du code, ce qui laisse alors la population elle-même inconnue, et un
pourcentage sur une population inconnue est la lecture erronée que refusent DEC0010 et DEC0013. Un
score n'est donc imprimé que lorsque rien n'a été mis de côté à la découverte. Aucun seuil n'est
passé, parce qu'un seuil est une affirmation sur tous les mutants de son périmètre - la règle de
DEC0013 pour une exécution qui n'a pas tout jugé.

Le code de sortie `2`, parce que DEC0009 le donne à une exécution qui n'a pas pu avoir lieu et cite
une baseline rouge parmi ses causes ; une partie de cette exécution n'a pas pu avoir lieu, pour une
raison que l'utilisateur doit corriger dans l'environnement ou dans la suite plutôt que dans les
tests. `1` dirait à un job de CI que les tests ont faibli, ce qui n'est pas ce qui s'est passé. C'est
la différence avec DEC0013, où l'exécution aurait pu tout juger et a été arrêtée par une échéance
qu'on lui avait donnée.

Une cible mise de côté laisse ses mutants sans verdict, ce qui est exactement ce autour de quoi un
`--resume` ultérieur hérite : une fois la cause corrigée, une exécution reprise ne juge que ce qui a
été mis de côté et complète la série avec un score sur l'ensemble (DEC0015).

## Alternatives envisagées

### Alternative 1 — Garder l'interruption de DEC0005 (statu quo)

* **Description :** une baseline rouge, ou un projet que la découverte ne peut pas lire, met fin à
  l'exécution avec un diagnostic et sans résultat ; le journal et `--resume` récupèrent ce qui a été
  jugé.
* **Pourquoi rejetée :** elle perd un travail correctement mesuré derrière un mécanisme que seul un
  utilisateur averti sait utiliser, et la baseline vérifiée cible par cible rend l'interruption inutile
  à la confiance dans les verdicts des autres cibles.

### Alternative 2 — S'arrêter au premier échec, mais rapporter ce qui a été jugé

* **Description :** mettre fin à l'exécution sur une baseline rouge comme aujourd'hui, en écrivant le
  rapport des cibles déjà jugées, sans score, comme le fait une exécution arrêtée (DEC0013).
* **Pourquoi rejetée :** elle garde le travail déjà fait mais renonce aux cibles qui suivent l'échec,
  aussi mesurables que celles qui le précèdent ; l'utilisateur doit alors relancer une exécution pour
  des résultats que la première aurait pu produire.

### Alternative 3 — Poursuivre sans rapporter aucun score

* **Description :** comme décidé, mais seulement avec les comptes de ce qui a été jugé, jamais un
  pourcentage.
* **Pourquoi rejetée :** elle retient le bilan que la partie mesurée permet, que le mainteneur a
  demandé ; lorsque la partie couverte est connue, une étiquette qui la nomme supprime la raison de le
  retenir.

### Alternative 4 — Une option pour s'arrêter au premier échec

* **Description :** poursuivre par défaut, avec une option qui rétablit l'arrêt immédiat.
* **Pourquoi rejetée :** aucun besoin utilisateur n'a été identifié. Son seul argument est d'économiser
  du calcul quand tout est cassé, et une cible cassée coûte une exécution de baseline avant d'être mise
  de côté.

## Conséquences

### Positives

* Aucun verdict correctement mesuré n'est perdu à cause d'un échec ailleurs dans l'exécution.
* Une seule règle pour tous les niveaux au-dessus du mutant : ce qui ne peut pas être mesuré est mis
  de côté avec sa raison, comme l'est déjà un mutant qui ne peut pas être jugé au sein de sa cible.
* Le rapport dit ce qui n'a pas pu être mesuré et pourquoi, là où l'utilisateur le lit, au lieu d'une
  erreur unique qui cache tout le reste.
* Un projet que la découverte ne peut pas lire, comme `FloorCheck.csproj`, n'empêche plus de mesurer
  le reste du dépôt, et le rapport le nomme avec la raison pour laquelle il n'a pas pu être lu.
* Une exécution reprise complète une série dont l'exécution précédente a mis quelque chose de côté.

### Négatives

* Une exécution aux résultats utiles sort désormais avec `2`, qu'un job de CI qui ne lit que le code
  de sortie traite comme une panne de l'outil ; il faut lire le rapport pour voir ce qui a été mesuré.
* Un seul projet que la découverte ne peut pas lire, n'importe où sous le répertoire, retient le score
  de toute l'exécution jusqu'à ce qu'il soit exclu ou corrigé.
* L'interruption de DEC0005 et le sens d'une baseline rouge selon DEC0009 changent, si bien que le
  README, le document d'architecture et les messages qui les décrivent doivent changer aussi.
* Le rapport gagne une section pour ce qui a été mis de côté, et le score une mention de ce qu'il
  couvre.

### Risques

* Un score sur une partie du code peut malgré tout être cité comme le score du projet, quoi que dise
  son étiquette.
* Une cause commune à toutes les cibles est rapportée une fois par cible, ce qui peut noyer le seul
  message qui compte.

### Actions de suivi

* Décider si DEC0005 est remplacée par ce document, et l'inscrire dans le statut de DEC0005.
* Mettre en œuvre la règle à la découverte, au build, à la baseline et à la couverture, avec la
  section du rapport et le code de sortie ; revoir #57 (#47), qui arrête encore l'exécution, et
  réutiliser le diagnostic de #56 (#46) comme raison d'une cible mise de côté.
* Mettre à jour le README et le document d'architecture là où ils décrivent une baseline rouge ou un
  projet illisible.
