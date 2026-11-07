# DEC0028 | L'outil est publié sous `KillMutants`, et sa commande est `killmutants`

## Statut

| Date | Statut | Note | Compte rendu lié |
|---|---|---|---|
| 2026-11-07 | Proposé | Première rédaction | |
| 2026-11-07 | Accepté | À la demande du mainteneur | |

## Contexte

Rien n'a encore été publié. Le jalon `1.0.0-rc.1` et l'issue #95 demandent une première pré-version
de l'outil sur nuget.org. L'identifiant d'un paquet nuget.org ne peut plus changer une fois qu'une
version a été publiée sous lui : renommer ensuite, c'est un second paquet et un premier retiré de la
liste, avec les consommateurs de l'ancien identifiant laissés en arrière. `RELEASING.md` désigne la
première publication comme la date limite pour arrêter l'identifiant.

Deux documents divergeaient sur le nom. `src/KillMutants.Cli/KillMutants.Cli.csproj` déclare
l'identifiant de paquet `KillMutants` et la commande `killmutants`, et la section d'installation du
README documente ce couple. La section « Package naming » de `RELEASING.md` proposait l'identifiant
`KillMutants.Cli` et la commande `dotnet-killmutants`.

Les autres dépôts Reefact donnent le nom nu à la bibliothèque et le suffixe `.Cli` à l'outil :
`DiagnosticCatalog` / `DiagnosticCatalog.Cli` (`dcat`), `JustDummies` / `JustDummies.Cli` (`dum`),
`FirstClassErrors` / `FirstClassErrors.Cli` (`fce`). Ici le moteur porte déjà un suffixe qui lui est
propre, `KillMutants.Core`, sur le train `lib`. Le mainteneur indique que KillMutants s'utilise par
sa ligne de commande, et aucun consommateur du moteur en tant que bibliothèque n'a été identifié. La
politique de publication de confiance de nuget.org que prévoit `RELEASING.md`, et qui n'est pas
encore configurée, limitera ce dépôt aux identifiants correspondant à `KillMutants*`. Le domaine
est `killmutants.io` ; le trait d'union n'existe que dans le nom GitHub `kill-mutants`. Un identifiant de paquet nuget.org ne peut pas contenir d'espace.

Le pilote `dotnet` exécute `dotnet <foo>` en cherchant une commande `dotnet-<foo>`, pour un outil
épinglé dans le manifeste d'un dépôt comme pour un outil dans le PATH. `RELEASING.md` a mesuré les
quatre combinaisons : une commande `dotnet-killmutants` se tape `dotnet killmutants` dans les deux
modes, alors qu'une commande nommée sans le préfixe se tape `dotnet <nom>` quand elle est épinglée et
`<nom>` seul quand elle est installée globalement. Un outil épinglé dans un manifeste n'est jamais
dans le PATH. Stryker.NET publie son outil sous `dotnet-stryker` et se tape `dotnet stryker`.

Un score de mutation varie avec la version de l'outil, et c'est pourquoi cette organisation épingle
`dotnet-stryker` par dépôt avec `rollForward: false`, afin que la CI et la machine d'un mainteneur
mesurent la même chose. Le mainteneur veut taper `killmutants` seul, sans `dotnet` devant. Une
commande de deux lettres, `km`, a été évoquée et jugée trop courte par le mainteneur.

## Décision

Dans ce contexte, nous décidons que l'outil est publié sur nuget.org sous l'identifiant de paquet
`KillMutants` et installe la commande `killmutants`.

## Justification

Le produit, c'est l'outil. KillMutants s'utilise par sa ligne de commande et personne ne consomme le
moteur comme bibliothèque, donc le nom nu, celui qu'un lecteur cherche sur nuget.org, va à ce qui est
réellement installé. La convention des dépôts voisins donne le nom nu à une bibliothèque parce que
ces dépôts ont une bibliothèque que des gens utilisent ; celui-ci n'en a pas, et son moteur est déjà
`KillMutants.Core`.

`killmutants` est le nom tapé dans tous les modes : seul quand l'outil est installé globalement,
comme le veut le mainteneur, et après `dotnet` quand il est épinglé. Le `dotnet` devant un outil
épinglé n'est pas une propriété du nom - aucun choix de nom ne le retire - donc le préfixe
n'achèterait que l'alignement de la forme globale sur la forme épinglée, au prix de l'invocation
nue que le mainteneur a demandée. L'épinglage reste la forme recommandée pour la CI, pour la
reproductibilité sur laquelle l'organisation s'appuie déjà avec Stryker.

Une seule orthographe, `KillMutants`, s'accorde avec le domaine, le motif des paquets et le code ; un
`Kill Mutants` en deux mots ne peut pas être un identifiant et ne ferait qu'ajouter une seconde
orthographe à chercher. Une commande de deux lettres économise peu une fois précédée de `dotnet` en
CI, et se lit mal dans un script.

Les deux noms sont ceux que disent déjà le fichier projet et le README, donc la décision ne coûte
aucun renommage, seulement la réécriture de la section de `RELEASING.md` qui défendait l'inverse,
avant que rien ne soit publié.

## Alternatives envisagées

### Alternative 1 — `KillMutants.Cli`, tapé `dotnet killmutants` (la proposition de `RELEASING.md`)

* **Description :** suivre la convention des dépôts voisins pour l'identifiant, et donner le préfixe
  `dotnet-` à la commande, pour qu'une seule invocation, `dotnet killmutants`, tienne que l'outil
  soit épinglé ou global.
* **Pourquoi rejetée :** elle garde le nom nu pour un moteur que personne ne consomme comme
  bibliothèque, et elle retire le `killmutants` seul que le mainteneur veut taper.

### Alternative 2 — `KillMutants`, tapé `dotnet killmutants`

* **Description :** l'identifiant nu, avec la commande `dotnet-killmutants` pour une invocation
  unique.
* **Pourquoi rejetée :** même raison pour la commande : une invocation unique partout se paie du
  `killmutants` seul, qui est la forme demandée par le mainteneur.

### Alternative 3 — Une commande courte, `km`

* **Description :** installer une commande de deux lettres, à la manière de `dcat`, `dum` et `fce`.
* **Pourquoi rejetée :** jugée trop courte par le mainteneur ; un nom court ne rapporte que pour un
  outil tapé seul toute la journée, et un outil de mutation épinglé se tape après `dotnet` en CI, où
  `dotnet km` se lit mal.

### Alternative 4 — `Kill Mutants`, en deux mots

* **Description :** nommer le produit en deux mots, comme nom d'affichage à côté de l'identifiant.
* **Pourquoi rejetée :** un identifiant de paquet ne peut pas contenir d'espace, donc l'identifiant
  resterait `KillMutants` et le produit porterait deux orthographes à chercher, contre le domaine et
  le code qui s'accordent déjà sur une seule.

## Conséquences

### Positives

* Le nom cherché sur nuget.org est le nom installé, et la commande porte le nom du produit.
* Une installation globale se tape `killmutants`, sans `dotnet`.
* Le fichier projet et le README le disent déjà : rien n'est renommé avant la première publication.

### Négatives

* Le moteur ne pourra jamais prendre l'identifiant nu `KillMutants` ; une bibliothèque publiée plus
  tard porte un suffixe.
* Le dépôt s'écarte de la convention que partagent ses voisins.
* Il existe deux invocations, `killmutants` et `dotnet killmutants`, et la documentation montre les
  deux.

### Risques

* Un lecteur suit la forme globale dans un script de CI où l'outil est épinglé, ou l'inverse, et la
  commande est introuvable.
* Une bibliothèque que des gens consommeraient peut apparaître plus tard et trouver le nom nu pris
  par l'outil.

### Actions de suivi

* Réécrire la section « Package naming » de `RELEASING.md` pour décrire cette décision.
* Réserver le préfixe `KillMutants.` sur nuget.org avant la première publication.
