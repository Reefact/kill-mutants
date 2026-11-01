# DEC0027 | `Linq` s'exécute par défaut, et `--without` l'écarte

## Statut

| Date | Statut | Note | Compte rendu lié |
|---|---|---|---|
| 2026-11-01 | Proposé | Première rédaction | |
| 2026-11-01 | Accepté | À la demande du mainteneur | |

## Contexte

DEC0024 a ajouté la famille `Linq` sur demande « d'abord », son statut par défaut étant « décidé
après qu'elle a été mesurée » par un nouveau record. Ses actions de suivi nommaient deux mesures, sur
`Reefact/slugger` et sur `Reefact/first-class-errors` à `8f84258`. Aucune des deux n'a été faite, et
le mainteneur note que ces deux dépôts utilisent très peu LINQ, de sorte qu'aucun ne mesurerait la
famille là où elle a des mutants à produire. L'issue #82 pose la question que le record avait
différée.

Une mesure existe, sur un troisième dépôt : `Reefact/enum-member-name-binding` à `bc6eda9`, exécuté
avec KillMutants `13b0229` et `--with Linq,NumericLiteral`, à côté de Stryker.NET 5.0.0 sur le même
commit et la même machine, avec deux workers chacun. Le catalogue par défaut y a produit 308 mutants,
dont 26 ont survécu. `Linq` en a produit 5 : quatre échanges `Any` ↔ `All`, tous tués, et un
`Take(2)` → `Skip(2)` qui a survécu sur une ligne construisant le texte d'exemple d'une description
OpenAPI. Aucun mutant `Linq` n'a échoué à compiler, et aucun ne se lisait comme quasi équivalent. Un
sixième site, `OrderByDescending` → `OrderBy` sur une clé de tri, n'a eu aucun mutant parce que
RB-016 refusait l'appel à cause d'une variable `out` dans sa lambda ; #78 lève ce refus, et le
mutant, appliqué à la main, est tué avec trois échecs.

Stryker.NET exécute son mutateur Linq par défaut, à son niveau `Standard`. Sur le même commit il a
produit 22 mutants Linq : 7 tués, 2 survivants, 1 non couvert, 14 qui n'ont pas compilé
(`Count` → `Sum` dix fois, `Append` → `Prepend` trois, `FirstOrDefault` → `First` une). Son survivant
`OrderBy` est tué par la suite complète quand on l'applique à la main ; son autre survivant est le
même `Take` → `Skip`. Sur ses 22, les cinq qui sont dans les paires de KillMutants sont les quatre
`Any` ↔ `All` et l'`OrderBy`.

Des échantillons antérieurs de la famille de Stryker, sur `first-class-errors` (#53, #63), ont donné
deux trous plausibles `Any` → `All` et six `FirstOrDefault` → `First` quasi équivalents ; les onze
paires de DEC0024 ont été choisies pour garder les premiers et laisser les seconds.

Une famille du catalogue par défaut change le score de tous les projets sur lesquels l'outil tourne.
DEC0020 a mis `Statement` dans le catalogue par défaut sur une mesure ; DEC0019 a mis `NumericLiteral`
sur demande parce qu'un littéral est partout et qu'un projet qui n'a rien demandé ne devrait pas payer
une exécution par littéral. Le décompte par nom de DEC0024 suggérait une trentaine de mutants `Linq`
sur un dépôt de trois mille, « ce qui est le profil de `Statement` plutôt que celui de
`NumericLiteral` ».

`--without <liste>` retire déjà une famille du catalogue de départ, quel qu'il soit, et
`--with <liste>` en ajoute une au catalogue par défaut ; les deux ont une clé dans `killmutants.json`.
Une série reprise est refusée quand ses réglages diffèrent de ceux du plan, et le catalogue en fait
partie (DEC0015). Les fixtures de bout en bout ne contiennent aucun appel parmi les onze paires.

## Décision

Dans ce contexte, nous décidons que `Linq` entre dans le catalogue par défaut, de sorte qu'elle
s'exécute à moins que `--without` ne l'écarte.

## Justification

La barre fixée par DEC0024 était une mesure par KillMutants, et celle qui existe est propre : cinq
mutants sur trois cents, tous compilés, quatre sur cinq tués, et le seul survivant une trouvaille de
faible valeur plutôt qu'un quasi-équivalent. Le profil est celui de `Statement` — quelques mutants,
une vraie question chacun — et `Statement` est entré dans le catalogue par défaut sur exactement ce
constat. Rien dans la mesure ne pointe dans l'autre sens.

Stryker.NET, qui exécute la famille par défaut, a produit quatorze erreurs de compilation sur
vingt-deux sur le même code ; la règle des jumelles est ce qui les tient hors des cinq de KillMutants.
La famille mesurée est celle que DEC0024 a resserrée, et le resserrement a fait ce pour quoi il était
là.

Promouvoir une famille a été décrit par DEC0024 comme peu coûteux, et la retirer comme ne l'étant
pas. `--without Linq` est la porte de sortie pour un projet qui n'en veut pas, sans option nouvelle,
et une série ne change pas silencieusement sous le changement, puisque son plan refuse un catalogue
différent.

Les deux mesures demandées par DEC0024 n'ont pas été faites, et ce record ne prétend pas le
contraire. Elles n'auraient pas mesuré grand-chose : les deux dépôts utilisent très peu LINQ. La
décision repose sur un dépôt et sur les échantillons de Stryker de deux autres ; une mesure sur du
code riche en LINQ devient une action de suivi plutôt qu'une condition préalable, parce que la mesure
existante ne donne aucune raison d'attendre qu'elle la contredise, et parce que le retour en arrière
tient en une option.

## Alternatives envisagées

### Alternative 1 — Statu quo : `Linq` reste sur demande

* **Description :** garder la famille derrière `--with Linq` jusqu'à ce que les deux mesures nommées
  par DEC0024 aient été faites.
* **Pourquoi écartée :** une mesure par KillMutants existe et elle est propre ; la famille coûte cinq
  exécutions de tests pour trois cents sur ce dépôt ; et la plupart des utilisateurs ne voient jamais
  une famille sur demande — la raison de DEC0020 pour ne pas en faire une de `Statement`.

### Alternative 2 — `Any` ↔ `All` par défaut, les autres paires sur demande

* **Description :** promouvoir la seule paire aux vrais trous confirmés sur deux dépôts, et garder
  les dix autres derrière `--with`.
* **Pourquoi écartée :** cela scinde une famille en deux sans mécanisme pour le faire — `--with` et
  `--without` agissent sur des familles — et les dix autres paires ont produit un survivant de
  faible valeur et aucun quasi-équivalent là où elles ont été mesurées. Les quasi-équivalents que
  DEC0024 redoutait étaient dans les échanges qu'elle a laissés de côté, pas dans ceux-ci.

### Alternative 3 — Mesurer d'abord, sur des dépôts riches en LINQ

* **Description :** trouver deux dépôts publics de taille modeste qui utilisent largement LINQ,
  exécuter `--with Linq` dessus, puis décider sur trois mesures. Les deux que DEC0024 nommait ne
  conviendraient pas : ils utilisent très peu LINQ.
* **Pourquoi écartée :** la mesure existante ne donne aucune raison d'attendre une autre réponse, le
  retour en arrière est `--without Linq`, et les exécutions peuvent se faire après coup aussi bien
  qu'avant ; elles sont gardées comme action de suivi.

## Conséquences

### Positives

* Un quantificateur, un ordre ou une tranche est interrogé sur tous les projets, sans option à
  connaître.
* Les mesures de la famille viendront désormais de chaque exécution plutôt que des quelques-unes qui
  l'avaient demandée.
* `--with Linq` et `--mutators Linq` continuent de fonctionner ; seul le défaut change.

### Négatives

* Le score de chaque projet bouge, d'environ cinq mutants pour trois cents là où c'est mesuré.
* Une série commencée avant ce changement ne se poursuit pas à travers lui : son plan nomme un autre
  catalogue, et l'exécution est refusée en nommant la différence (DEC0015).
* Le survivant `Take` → `Skip` sur un texte d'exemple est une trouvaille qu'un développeur lit et met
  de côté.

### Risques

* `Skip` ↔ `Take` sur une séquence d'un élément, et les autres quasi-équivalents que DEC0024 nommait
  comme possibles dans un programme donné, atteignent désormais tous les projets plutôt que ceux qui
  l'avaient demandé.
* Un dépôt riche en LINQ peut montrer un profil différent de celui mesuré.

### Actions de suivi

* Déplacer `Linq` de la liste sur demande vers le catalogue par défaut ; mettre à jour l'aide, le
  README, la table du catalogue dans `architecture-en.md` et `architecture-fr.md`, et le CHANGELOG.
* Mesurer la famille sur deux dépôts publics de taille modeste qui utilisent largement LINQ, à la
  place des deux que DEC0024 nommait, et consigner les chiffres sur #82.
* Le statut de DEC0024 gagne une ligne disant que ce record remplace son statut sur demande, sur
  décision du mainteneur.
