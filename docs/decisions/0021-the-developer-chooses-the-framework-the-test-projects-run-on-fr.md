# DEC0021 | Le développeur choisit le framework sur lequel tournent les projets de test, et une exécution ambiguë est refusée sans ce choix

## Statut

| Date | Statut | Note | Compte rendu lié |
|---|---|---|---|
| 2026-10-31 | Proposé | Première rédaction | |
| 2026-10-31 | Accepté | À la demande du mainteneur | |

## Contexte

KillMutants refuse un projet de test qui vise plusieurs frameworks, avec un message disant que chacun
demanderait sa propre exécution et son propre score, et il refuse pour la même raison un projet
atteint par des suites de tests sur des frameworks différents. Un projet de test construit pour
`net8.0;net10.0` est du C# ordinaire, et aujourd'hui il n'a aucun moyen d'être exécuté.

Le framework pour lequel une bibliothèque est analysée était, jusqu'au changement qui accompagne cet
enregistrement, celui du projet de test lui-même, ce qui arrêtait toute exécution sur une bibliothèque
construite pour un autre framework. C'est désormais celui que NuGet négocie à partir du résultat de
restauration du projet de test (RB-007) : une bibliothèque suit le projet de test qui la charge. Rien
ne permet encore au développeur de dire sur quel framework tourne un projet de test.

Le mainteneur a demandé, le 2026-10-31, si le développeur devrait pouvoir cibler un framework
particulier, et si d'autres options étaient souhaitables. L'étude de Stryker.NET de ce dépôt range la
désambiguïsation multi-TFM parmi les choses que cet outil ne porte que pour l'histoire.

Les codes de sortie sont un contrat public (DEC0009) : `64` est réservé à une ligne de commande non
comprise et `2` à une exécution qui n'a pas pu aller à son terme. Toute option qui énonce une habitude
a une clé dans `killmutants.json`. Le plan d'une exécution consigne sa configuration, sa couverture,
ses mutateurs, ses exclusions, son périmètre et son empreinte (DEC0016), et une exécution reprise
hérite des verdicts que sa série a atteints sur le même code (DEC0015) ; le plan ne consigne aucun
framework.

Un score de mutation décrit un seul build du code : chaque framework produit son propre assembly, sa
propre sortie et ses propres verdicts (RB-007). La barrière (`--break-at`) juge l'unique score d'une
exécution.

## Décision

Dans ce contexte, nous laissons le développeur choisir, par une seule option `--target-framework` et
une clé `targetFramework` de `killmutants.json`, l'unique framework sur lequel tournent les projets de
test, et nous refusons, en nommant cette option, une exécution dont les frameworks sont ambigus sans
elle.

## Justification

Le cas ordinaire refusé aujourd'hui, un projet de test sur plusieurs frameworks, n'a d'autre issue que
ce choix, et le refus ne peut être levé que par la personne qui sait quel framework compte.

L'option sélectionne le framework des projets de test et non celui des bibliothèques, parce que les
bibliothèques découlent déjà du projet de test qui les charge (RB-007) : un second contrôle sur le même
fait serait une seconde façon d'être en désaccord avec le premier. Choisir le framework des suites est
aussi ce qui débloque le cas refusé.

Une exécution ambiguë reste refusée sans l'option, plutôt que de prendre un défaut, parce qu'un score
décrit un seul build : un défaut produirait un score pour un framework que personne n'a choisi, ce qui
est la raison d'être du refus. Le message nomme désormais l'option et les valeurs offertes, de sorte que
le refus devient une issue.

Un seul framework par exécution garde un score par rapport, de sorte que la barrière a toujours un seul
score à juger et qu'une série reprise a un seul build à poursuivre.

Une valeur qu'aucun projet n'offre est une ligne de commande bien formée, et ce sont les fichiers de
projet qui l'écartent, ce qu'on ne sait qu'après la découverte ; `64` reste réservé à la syntaxe de la
ligne de commande (DEC0009), donc l'exécution se termine par `2`. Pour la même raison de ne jamais
perdre du code en silence, un projet de test qui n'offre pas le framework choisi arrête l'exécution et
est nommé : l'ignorer laisserait hors du rapport, sans un mot, le code que lui seul testait, et
`--exclude` existe déjà pour mettre un projet de côté exprès.

Une clé de `killmutants.json` découle du fait que les habitudes ont des clés. Le framework doit entrer
dans le plan de l'exécution, faute de quoi une série reprise pourrait hériter de verdicts atteints sur
un autre build.

## Alternatives envisagées

### Alternative 1 — Continuer à refuser, sans moyen de choisir

* **Description :** le statu quo : un projet de test sur plusieurs frameworks reste inexécutable.
* **Pourquoi écartée :** un projet ordinaire ne peut pas du tout être muté, et le refus ne donne rien à
  faire à son auteur.

### Alternative 2 — Forcer directement le framework de la bibliothèque

* **Description :** l'option sélectionne le build analysé pour chaque bibliothèque et contourne la
  négociation.
* **Pourquoi écartée :** elle ne débloque pas un projet de test sur plusieurs frameworks, qui est le cas
  refusé, et elle double un fait que la négociation règle déjà.

### Alternative 3 — Deux options, une pour les projets de test et une pour les bibliothèques

* **Description :** les deux contrôles sont exposés.
* **Pourquoi écartée :** davantage de surface publique à garder pour toujours, et un second contrôle qui
  peut contredire le premier, pour un cas que la négociation couvre déjà.

### Alternative 4 — Prendre un défaut quand le choix est ambigu

* **Description :** prendre le premier framework offert quand le développeur ne dit rien.
* **Pourquoi écartée :** le score décrirait un framework que personne n'a choisi.

### Alternative 5 — Accepter une liste de frameworks et rapporter chacun

* **Description :** `--target-framework net8.0,net10.0` exécute une fois par framework et écrit un
  rapport pour chacun.
* **Pourquoi écartée :** elle demande de revoir ce qui identifie un mutant et un rapport, ainsi que la
  barrière, pour une commodité que le développeur a déjà en lançant deux fois.

### Alternative 6 — Ignorer les projets de test qui n'offrent pas le framework

* **Description :** les laisser de côté avec un avertissement.
* **Pourquoi écartée :** le code que seuls ils testaient sortirait du rapport sans que le score le
  montre.

### Alternative 7 — Sortir avec `64` pour une valeur qu'aucun projet n'offre

* **Description :** traiter la valeur indisponible comme une erreur d'usage.
* **Pourquoi écartée :** elle élargit `64` au-delà de la syntaxe de la ligne de commande, à laquelle
  DEC0009 le réserve.

## Conséquences

### Positives

* Un projet de test sur plusieurs frameworks peut être exécuté, et le refus qui reste dit quoi saisir.
* Un score et un rapport continuent de décrire un seul framework.
* Les bibliothèques continuent de suivre le projet de test qui les charge, sans second contrôle pour
  les contredire.

### Négatives

* Une option publique et une clé de configuration à garder pour toujours.
* Couvrir plusieurs frameworks demande une exécution par framework.
* Une solution mêlant des projets de test sur des frameworks différents demande `--exclude` pour ceux
  qui n'offrent pas celui qui est choisi.

### Risques

* Un script qui fige un framework échoue avec `2` le jour où un projet change de cible.
* Un développeur habitué à un défaut peut lire le refus comme une régression.

### Actions de suivi

* Mettre le framework choisi dans l'identité de l'exécution, à laquelle une exécution reprise est
  comparée, pour qu'elle n'hérite jamais de verdicts atteints sur un autre framework (DEC0015,
  DEC0016).
* Documenter l'option et la clé dans le README et dans `--help`.
* Mettre à jour RB-007 et donner aux deux refus un message qui nomme l'option et les valeurs offertes.
