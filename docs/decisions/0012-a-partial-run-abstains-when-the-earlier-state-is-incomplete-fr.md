# DEC0012 | Une exécution partielle retient son verdict sur un état antérieur incomplet

## Statut

| Date | Statut | Note | Compte rendu lié |
|---|---|---|---|
| 2026-09-06 | Proposé | Première rédaction | |
| 2026-09-06 | Accepté | | |

## Contexte

Une exécution partielle compare l'arbre de travail à un état antérieur, qu'elle reconstitue dans un
worktree détaché. Les composants suivis à part — des sous-modules, pour git — y sont déposés depuis le
magasin d'objets que le clone détient déjà. Un composant dont les objets ne sont pas présents
localement ne peut pas être déposé du tout : un clone frais, avant `submodule update --init`, a le
gitlink et rien derrière, et aucun travail local ne produit ces objets. L'instantané nomme un tel
composant au lieu de laisser un répertoire vide, parce qu'un répertoire vide se lit comme une réponse :
pas de projet, donc pas d'arête, donc rien de perdu.

Jusqu'à cette décision, cette liste n'atteignait qu'un seul endroit. La traversée refusait de
continuer lorsqu'on lui demandait un projet situé *à l'intérieur* d'un composant manquant. Toutes les
autres exécutions allaient au verdict comme si la comparaison avait été complète.

Mesuré sur un projet SDK : un projet situé hors d'un composant manquant peut importer un fichier de
build qui s'y trouve, et MSBuild saute en silence un import dont le `Exists()` est faux. Sur le même
projet, `-getItem:ProjectReference` répond avec la référence quand le composant est là et avec `[]`
quand il ne l'est pas — succès, aucun avertissement, code de sortie 0 dans les deux cas. Les
références que ce fichier aurait ajoutées ont disparu du graphe reconstitué, et rien dans l'évaluation
ne consigne qu'on les attendait. Le projet importateur n'est pas sous le chemin manquant, donc une
vérification fondée sur l'inclusion ne peut pas l'atteindre non plus.

Le verdict avait déjà deux raisons de retenir un succès : un projet que le changement laisse sans
aucun test qui l'atteint, et une sélection dont tous les mutants étaient intestables. Les deux
reposent sur la même règle, énoncée par DEC0009 et appliquée de nouveau par DEC0010 : une exécution
qui n'a rien établi ne doit pas rapporter un succès.

Le principe directeur du projet est qu'un outil de mesure a le droit de sous-détecter, jamais de
mentir. Le verdict d'une exécution partielle est ce sur quoi agit une barrière d'intégration continue.

## Décision

Quand l'instantané d'une exécution partielle signale un composant qu'il n'a pas pu lire, l'exécution
va à son terme et rapporte ce qu'elle a trouvé, et son verdict retient le succès.

## Justification

La pertinence d'un composant manquant ne peut pas être établie à partir de ce qui reste. La mesure
montre que la panne est silencieuse à l'endroit même où il faudrait la détecter : MSBuild rend un
succès et une liste plus courte, il n'y a donc aucun signal sur lequel resserrer. Toute règle qui
tenterait de ne refuser que lorsque le composant manquant comptait devinerait, et deviner faux
signifie passer.

Retenir le verdict est la seule réponse qui ne coûte que le succès. Les constats produits l'ont été en
exécutant réellement des mutants contre du code réellement construit, et ils méritent d'être lus quoi
que la comparaison n'ait pas pu établir ; arrêter l'exécution les jetterait. Signaler le manque tout
en passant quand même laisserait un vert sur lequel une barrière agit, ce qui est précisément l'échec
que nomme le principe directeur.

Cela ne demande par ailleurs aucune configuration, et n'offre donc rien qu'on puisse désactiver plus
tard et laisser désactivé.

Le verdict retient déjà le succès pour deux conditions de même forme : c'est une troisième raison dans
un mécanisme existant plutôt qu'une nouvelle sorte de résultat.

## Alternatives envisagées

### Alternative 1 — Refuser l'exécution partielle d'emblée

* **Description :** arrêter l'exécution dès que l'instantané signale quelque chose de manquant, avec
  un message nommant les composants et la commande qui les récupère.
* **Pourquoi écartée :** exactement aussi sûre et strictement moins utile. L'exécution a déjà produit
  un travail qui mérite d'être rapporté, et refuser le jette sans protection supplémentaire — le
  succès est retenu dans les deux cas.

### Alternative 2 — Signaler le manque et laisser le verdict tenir

* **Description :** nommer les composants non lus dans le rapport et décider le verdict à partir des
  mutants comme d'habitude.
* **Pourquoi écartée :** un vert avec une note de bas de page reste un vert, et une barrière agit sur
  le code de sortie, pas sur la prose. Cela demanderait aussi au lecteur de juger si le composant
  manquant comptait, qui est la seule question à laquelle l'exécution n'a pas pu répondre.

### Alternative 3 — Ne retenir le verdict que si le composant manquant est pertinent

* **Description :** établir si quelque chose atteignait le composant manquant et ne s'abstenir
  qu'alors.
* **Pourquoi écartée :** indécidable. La mesure ci-dessus montre qu'un import sauté ne laisse aucune
  trace dans l'évaluation ; il faudrait donc retrouver la pertinence en lisant le texte des fichiers
  de build — ce que ce projet a déjà refusé pour le graphe de projets, parce qu'un import peut être
  calculé, hérité d'un SDK ou imbriqué. Un balayage qui en rate un rend vert, ce qui est l'échec que
  l'on supprime.

### Alternative 4 — Laisser le développeur choisir le risque

* **Description :** un drapeau autorisant une exécution à passer malgré une comparaison incomplète,
  soit globalement, soit en nommant le composant dont on se porte garant.
* **Pourquoi écartée :** le choix ne serait pas éclairé. Décider qu'un composant manquant est
  inoffensif suppose de savoir si quelque chose importe depuis lui, ce qui est précisément invisible
  tant qu'il manque : le développeur affirmerait une conviction plutôt que d'agir sur une preuve. La
  forme nommée est la meilleure des deux, puisqu'une affirmation portant sur un composant est
  relisible et qu'un nouveau composant n'hérite pas d'une ancienne permission, et elle reste
  disponible si l'usage la réclame un jour.

## Conséquences

### Positives

* Une exécution partielle ne peut plus rapporter un succès sur une comparaison qu'elle n'a pas
  établie.
* Le rapport survit : les mutants exécutés restent listés, donc un développeur dont le clone est
  incomplet apprend quelque chose au lieu d'être seulement refusé.
* Il n'y a rien à configurer, donc rien à désactiver pendant un incident et à laisser désactivé.
* Le refus qui se déclenchait quand la traversée entrait dans un composant manquant devient inutile,
  et avec lui une incohérence : le même dépôt dans le même état finissait en exception ou en rapport
  complet selon ce que le diff touchait.

### Négatives

* Un dépôt dont l'intégration continue n'initialise pas ses composants ne peut pas utiliser `--since`
  comme barrière tant que ce workflow n'a pas changé, puisque le succès est inatteignable.
* Le refus par inclusion ayant disparu, un projet signalé comme ayant perdu sa couverture peut être
  l'artefact d'une arête qui n'a pas pu être lue. Le rapport doit donc dire quelles de ses parties
  l'incomplétude atteint au lieu de les présenter toutes comme établies.

### Risques

* « Pas de verdict » peut se lire comme un défaut de l'outil plutôt que comme un constat sur le clone,
  et se traiter en abandonnant `--since` au lieu d'aller chercher les composants.
* Un dépôt qui laisse délibérément un gros composant non initialisé la plupart du temps n'obtiendrait
  jamais de succès de `--since`, et cette décision ne lui offre aucune issue.

### Actions de suivi

* Le `README.md` racine documente les deux conditions sous lesquelles une exécution partielle échoue
  et ne mentionne pas encore cette troisième. Elle y est ajoutée dans le même changement que ce
  record.
