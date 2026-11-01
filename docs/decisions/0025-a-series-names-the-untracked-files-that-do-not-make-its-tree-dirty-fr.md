# DEC0025 | Une série nomme les fichiers non suivis qui ne rendent pas son arbre sale, et un motif qui pourrait couvrir du code est refusé

## Statut

| Date | Statut | Note | Compte rendu lié |
|---|---|---|---|
| 2026-11-01 | Proposé | Première rédaction | |
| 2026-11-01 | Accepté | À la demande du mainteneur | |

## Contexte

[DEC0015](0015-a-resumed-run-inherits-the-verdicts-its-series-reached-on-the-same-code-fr.md) permet
à une exécution de continuer une série et d'hériter de ses verdicts sur le même code seulement, et
nomme ce code par un commit dont l'arbre est propre. Ses conséquences le disent en une ligne : *« Un
arbre modifié ne reprend jamais, même à contenu identique. »* L'arbre est lu avec `git status`, qui
liste les fichiers non suivis, et les fichiers que KillMutants écrit lui-même à côté du rapport sont
exclus de cette lecture, parce que, comptés comme des modifications, ils empêcheraient toute série de
continuer. DEC0015 liste aussi, comme action de suivi, *« une identité par le contenu pour un arbre
modifié »* : elle concerne un arbre déjà modifié au début de la série et retrouvé identique plus
tard, pas un arbre qui était propre et a gagné des fichiers.

Sur `Reefact/first-class-errors` à `8f84258` ([#66](https://github.com/Reefact/kill-mutants/issues/66)),
une exécution a été lancée avec `--resume 8f84258` sur un arbre propre et tuée après 1 342 verdicts,
au redémarrage de son conteneur ; le plan et le journal étaient intacts. La même commande, relancée
dans le même répertoire, a refusé : l'arbre avait des modifications non commitées. C'étaient seize
fichiers non suivis, quinze `*.received.html` et un `*.received.json`, dans
`FirstClassErrors.GenDoc.UnitTests/`. Les tests de snapshot écrivent un tel fichier à côté de leur
source, par `[CallerFilePath]`, quand un mutant change leur sortie
([#51](https://github.com/Reefact/kill-mutants/issues/51)). L'arbre était propre au début de
l'exécution, c'est donc l'exécution qui les a écrits. Une exécution qui se termine nomme les fichiers
non suivis apparus pendant qu'elle tournait ([#59](https://github.com/Reefact/kill-mutants/issues/59)) ;
une exécution tuée, ou arrêtée par `--stop-after`, n'en a pas l'occasion, et c'est celle qu'un
développeur veut reprendre.

En CI, une série reprend depuis un checkout neuf et laisse derrière elle les fichiers non suivis de
l'exécution précédente. Sur un poste, une série reprend dans le répertoire où elle a été interrompue,
et #66 ne se produit que là. Personne ne reclone pour reprendre sur un poste.

Un fichier non suivi n'est pas toujours inoffensif. Le SDK .NET compile par défaut tout fichier `.cs`
sous le répertoire d'un projet, donc un `.cs` non suivi est du code, et les fichiers de projet et de
build (`*.csproj`, `*.props`, `*.targets`, `global.json`, `NuGet.config`, `.editorconfig`,
`*.globalconfig`) changent ce qui est construit. Sous Windows, le système de fichiers ignore la casse,
donc `*.CS` est compilé aussi. Un test peut enfin lire un fichier à l'exécution, ce que rien hors du
test ne peut voir.

Les entrées du compilateur sont connues exactement, par la ligne de commande `csc` de MSBuild
([DEC0003](0003-compilation-inputs-from-csc-command-line-fr.md)), mais seulement pour les projets sous
mutation, et seulement après le build. La vérification qui refuse une série a lieu avant le build. Une
exécution peut déjà être refusée après la génération de ses mutants, et les fichiers de la série sont
alors gardés tels qu'ils étaient.

`--exclude` prend des motifs comparés au chemin relatif au répertoire désigné à l'exécution, écrit avec
`/`, où `*` couvre aussi `/`. Toute option qui exprime une habitude a une clé dans `killmutants.json`,
et une liste donnée sur la ligne de commande remplace celle du fichier. L'outil n'a encore aucune
release.

L'issue #66 proposait trois issues : nommer les fichiers au moment du refus, ne pas compter les
fichiers apparus après le début de la série, ou empêcher la bibliothèque de snapshot de les écrire.
Verify, la bibliothèque de #66, offre peut-être une variable d'environnement pour la dernière ;
laquelle n'a pas été vérifié.

Le mainteneur a tranché les points suivants pendant la discussion :

* le motif est donné par l'utilisateur, avec `--ignore-untracked` ou la clé `ignoreUntracked`, dans la
  syntaxe de `--exclude` ; sans motif, le refus demeure et son message nomme les fichiers non suivis
  apparus depuis le début de la série ;
* seuls les fichiers non suivis peuvent être ignorés ; un fichier suivi et modifié rend toujours
  l'arbre sale ;
* le motif s'applique chaque fois qu'une série lit l'état de son code, à son démarrage comme à sa
  reprise, et pas à `--since` ;
* `--ignore-untracked` sur la ligne de commande sans `--resume` est une erreur ; la clé du fichier
  reste alors sans effet ;
* un motif qui peut accepter le même chemin qu'un motif d'une liste interne de fichiers sensibles est
  refusé avant que l'exécution démarre, sans tenir compte de la casse ; un motif fait seulement de
  jokers et de `/` est refusé avec un message à part, qui dit qu'il ignorerait tout fichier non suivi ;
* la liste retenue ce jour-là est `*.cs`, `*.csproj`, `*.props`, `*.targets`, `*.globalconfig`,
  `.editorconfig`, `global.json`, `NuGet.config` et `*.resx` ;
* une exécution qui trouve un fichier ignoré parmi les entrées du compilateur est refusée elle aussi ;
* les deux vérifications arrêtent les erreurs évidentes, pas un motif écrit pour contourner la règle ;
* les motifs sont enregistrés dans le plan, et peuvent changer d'une exécution de la série à l'autre ;
* chaque fichier ignoré est nommé dans un avertissement sur l'erreur standard et consigné dans le
  rapport JSON.

## Décision

Dans ce contexte, nous décidons qu'une série peut nommer, avec `--ignore-untracked`, les fichiers non
suivis qui ne rendent pas son arbre sale, que chaque fichier ainsi ignoré est signalé, et qu'un motif
qui pourrait couvrir un fichier source ou de build, ou qui accepte un fichier que lit le compilateur,
est refusé.

## Justification

Les fichiers de #66 ont été écrits par l'exécution elle-même, dans un arbre propre au début de la
série, et le refus qui a suivi est ce que DEC0015 prescrit. La règle a raison sur le code et tort sur
ces fichiers : ils ne changent rien de ce qui est compilé. Exclure de la même lecture les fichiers de
l'outil est ce que DEC0015 fait déjà, pour la même raison ; un motif étend cette exception aux fichiers
qu'écrivent les tests du projet, que seul le projet connaît.

Un motif donné par l'utilisateur place cette connaissance là où elle se trouve. L'outil ne peut pas
distinguer un snapshot laissé par un test d'un fichier source ajouté à la main : les deux sont non
suivis et apparus depuis le début de la série. Le projet le peut, et il écrit le motif une fois, dans
le fichier qu'il versionne avec son code.

Comme un `.cs` ou un fichier de build non suivi change ce qui est compilé, un motif qui pourrait en
couvrir un ferait de cette échappatoire un moyen d'hériter de verdicts obtenus sur un autre code.
Confronter un motif à une liste de motifs sensibles, avant que rien ne tourne, arrête les cas évidents
sans coût et avec un message qui dit pourquoi. Ignorer la casse découle de ce que Windows compile
`*.CS`. Confronter les fichiers ignorés aux entrées du compilateur, une fois celles-ci connues, attrape
un motif que la liste ne pouvait pas juger, comme un répertoire de sources générées. Aucune des deux
vérifications ne couvre un fichier lu par un test, et aucune ne peut arrêter un motif écrit pour les
contourner ; le record le dit plutôt que de prétendre davantage.

Signaler chaque fichier ignoré garde l'exécution honnête sur ce qu'elle a accepté, ce que la rigueur de
DEC0015 protégeait : le lecteur du rapport voit que l'arbre n'était pas exactement le commit, et quels
fichiers faisaient la différence.

Le cas se produit quand une série reprend dans le répertoire où elle a été interrompue, c'est-à-dire
sur un poste. Rendre le motif explicite, plutôt qu'actif par défaut, laisse la CI, où le problème ne
se pose pas, exactement telle que DEC0015 l'a laissée.

Décider le nom et la liste avant la première release les garde peu coûteux à changer : rien n'en
dépend encore.

## Alternatives envisagées

### Alternative 1 — Le statu quo, avec un meilleur refus

* **Description :** Garder la règle de DEC0015 inchangée, et faire nommer par le refus les fichiers non
  suivis apparus depuis le début de la série, comme probablement l'œuvre de la série elle-même.
* **Raison du rejet :** Le refus reste correct, mais les fichiers doivent être supprimés à la main avant
  chaque reprise, et rien n'automatise ce geste. Il est conservé comme ce qui se passe quand aucun
  motif n'est donné.

### Alternative 2 — Ignorer tout fichier non suivi apparu depuis le début de la série

* **Description :** Enregistrer les fichiers non suivis au démarrage de la série, et ignorer tout
  fichier qui n'en faisait pas partie.
* **Raison du rejet :** Un fichier `.cs` ajouté à la main entre deux exécutions est compilé, et la série
  hériterait alors de verdicts obtenus sur un autre code.

### Alternative 3 — Ignorer les fichiers que les exécutions de la série ont vus apparaître

* **Description :** Chaque exécution enregistre les fichiers non suivis qui apparaissent pendant
  qu'elle tourne, et une reprise ignore ceux-là.
* **Raison du rejet :** Une exécution tuée n'enregistre rien, sauf à écrire à chaque verdict. Et elle
  fait confiance à tout ce qui apparaît pendant une longue exécution, y compris une modification faite
  à la main sur le même poste.

### Alternative 4 — Un motif intégré à l'outil, tel que `*.received.*`

* **Description :** KillMutants ignore les fichiers des bibliothèques de snapshot connues sans qu'on le
  lui dise.
* **Raison du rejet :** Il fait entrer dans l'outil la connaissance d'une bibliothèque, alors que
  d'autres bibliothèques de snapshot écrivent ailleurs et autrement.

### Alternative 5 — Empêcher la bibliothèque de snapshot d'écrire

* **Description :** Positionner, sur le test host, la variable d'environnement qui empêche Verify
  d'écrire ses fichiers `.received`.
* **Raison du rejet :** C'est propre à une bibliothèque, la variable n'a pas été vérifiée, et cela
  change l'environnement dans lequel les tests tournent sous mutation par rapport au baseline.

### Alternative 6 — Reprendre depuis un clone neuf, et le dire dans le refus

* **Description :** L'alternative 1, avec une issue de plus dans le message : continuer la série
  depuis un autre checkout du même commit, ce que DEC0015 permet déjà.
* **Raison du rejet :** Reprendre depuis un autre répertoire n'est pas naturel là où le problème se
  pose. Sur un poste, cela veut dire recloner, reconstruire et déplacer le plan et le journal à la
  main, pour contourner des fichiers que l'outil sait nommer.

## Conséquences

### Positives

* Une série interrompue sur un poste reprend dans le même répertoire, une fois que son projet a nommé
  les fichiers qu'écrivent ses tests.
* La règle de DEC0015 est conservée pour tout fichier suivi et pour tout fichier non suivi qu'aucun
  motif ne nomme.
* Le rapport dit quels fichiers ont été ignorés, si bien qu'un arbre qui n'était pas exactement le
  commit n'est jamais présenté comme s'il l'était.
* Un motif qui pourrait couvrir du code est refusé avec un message qui dit pourquoi et quoi écrire à la
  place.

### Négatives

* Les motifs doivent être précis : `*.received.*` est refusé parce qu'il accepte `x.received.cs`, et ce
  sont les extensions qu'on nomme à la place.
* La vérification contre les entrées du compilateur vient après le build, si bien qu'un motif qu'elle
  refuse coûte le temps de la découverte, du build et de l'analyse.
* La ligne de commande et le fichier de configuration portent une option de plus, dont le nom et la clé
  deviennent un contrat une fois l'outil publié.
* Ajouter un motif à la liste sensible après la release peut refuser un motif qui passait.

### Risques

* Un fichier lu par un test à l'exécution, et qu'un motif ignore, peut changer des verdicts sans
  qu'aucune vérification ne le voie.
* Un fichier source non suivi dans un projet de test, couvert par un motif que la liste ne pouvait pas
  juger, échappe aux deux vérifications, puisque les entrées du compilateur ne sont lues que pour les
  projets sous mutation.
* Un motif écrit pour contourner les vérifications fait hériter à une série de verdicts obtenus sur un
  autre code.

### Actions de suivi

* Implémenter le message de refus qui nomme les fichiers apparus depuis le début de la série, utilisé
  quand aucun motif n'est donné.
* Implémenter `--ignore-untracked`, la clé `ignoreUntracked`, les deux vérifications, l'avertissement
  et le champ du rapport JSON.
* Documenter l'option dans le `README.md`, avec la liste sensible telle qu'elle est.
* Réexaminer la liste sensible avant la première release.
