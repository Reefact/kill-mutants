# DEC0029 | Une contribution extérieure n'est acceptée qu'avec le Contributor Agreement, qui en cède les droits patrimoniaux au propriétaire du projet

## Statut

| Date | Statut | Note | Compte rendu lié |
|---|---|---|---|
| 2026-11-09 | Proposé | Première rédaction | |

## Contexte

KillMutants appartient à Sylvain Aurat, agissant à titre personnel (le propriétaire du projet). Ses
utilisateurs le reçoivent sous la PolyForm Internal Use License 1.0.0. Par ailleurs, le propriétaire
du projet accorde à REEFACT, une SARL française, la REEFACT Exploitation License : REEFACT peut
utiliser, modifier et exploiter commercialement le projet, et le licencier ou le sous-licencier à des
tiers sous des modèles libres, source-available, propriétaires, commerciaux ou autres, approuvés par
le propriétaire du projet. Cette licence accorde ce que détient le propriétaire du projet.

Le code qu'une autre personne écrit lui appartient, sauf accord contraire. La licence PolyForm
accorde des droits aux utilisateurs du projet ; ce n'est pas une licence d'un contributeur au
propriétaire du projet, et elle n'accorde à personne le droit de re-licencier.

Au 2026-11-09, `CONTRIBUTING.md` et la section Contributing du README disaient que les issues et les
pull requests sont les bienvenues, et ne disaient rien des droits sur ce qu'une pull request apporte.
Aucun accord d'aucune sorte n'était demandé à un contributeur. L'historique de `main` contenait alors
des commits d'agents travaillant pour le propriétaire du projet et de Dependabot. L'issue #102 avait
retenu `CONTRIBUTING.md` et un code de conduite précisément sur cette question : inviter à contribuer
avant que les droits soient réglés, c'est inviter du code que le projet pourrait ne pas pouvoir
exploiter. Le formulaire OpenSSF Best Practices (#92) demande un guide de contribution.

just-dummies, un autre dépôt du même propriétaire, sous la même licence utilisateur et la même
licence d'exploitation accordée à REEFACT, a déjà réglé la question. Son Contributor Agreement cède
au propriétaire du projet, sans contrepartie, les droits patrimoniaux cessibles sur une contribution,
énumérés un par un, pour le monde entier et pour toute leur durée légale, avec une licence large en
repli là où la cession n'est pas juridiquement possible, une licence de brevets et un consentement
sur le droit moral ; il relève du droit français. Un contributeur l'accepte une fois par pull
request, en cochant une case que porte le modèle de pull request, et un workflow refuse la pull
request tant qu'elle n'est pas cochée. Une pull request que le propriétaire du projet ouvre
directement n'a besoin d'aucun accord ; une pull request venant d'une branche `claude/*`, ouverte
sous le compte du propriétaire du projet mais écrite par un agent, garde la case et la coche.

Un Developer Certificate of Origin certifie que le contributeur peut soumettre le code sous la
licence du projet. Ici, cette licence est la licence PolyForm Internal Use.

Un contributeur qui garde son droit d'auteur peut à la place accorder une licence large et
sous-licenciable, qui lui laisse davantage et lui demande moins.

Le mainteneur a choisi, le 2026-11-09, d'aligner KillMutants sur just-dummies.

## Décision

Dans ce contexte, nous décidons qu'une pull request venant de toute autre personne que le
propriétaire du projet n'est acceptée qu'une fois que son auteur a accepté, pour cette contribution,
le Contributor Agreement de KillMutants, qui cède au propriétaire du projet les droits patrimoniaux
sur la contribution.

## Justification

La licence d'exploitation permet à REEFACT de re-licencier le projet sous n'importe quel modèle, et
elle ne peut accorder que ce que détient le propriétaire du projet. Accepter du code sans accord
laisserait des parties du projet hors de portée de cette licence. L'accord ferme cette brèche là où
elle s'ouvre, sur la pull request qui apporte le code.

Un Developer Certificate of Origin ne la fermerait pas : il certifie un droit de soumettre sous la
licence PolyForm, qui ne porte aucun droit de re-licencier. Refuser les contributions fermerait la
brèche en fermant la porte, ce que le README et `CONTRIBUTING.md` disent que le projet ne veut pas.

Entre une cession et une licence, le même propriétaire du projet a déjà un modèle, avec les deux
mêmes licences de part et d'autre, dans just-dummies. L'utiliser ici, c'est un seul accord à lire,
un seul jeu de conditions pour un contributeur qui travaille sur les deux, et un seul modèle à
défendre plutôt que deux qui diffèrent sans raison tenant aux projets eux-mêmes.

## Alternatives envisagées

### Alternative 1 — Statu quo : pull requests bienvenues, sans accord

* **Description :** laisser `CONTRIBUTING.md` et le README tels qu'ils étaient, et fusionner les
  contributions extérieures sur la seule foi de la licence du projet.
* **Pourquoi rejetée :** le code fusionné ainsi reste à son auteur, hors de portée de la licence
  d'exploitation, ce qui est le problème pour lequel #102 a été ouverte.

### Alternative 2 — Des issues seulement, pas de code extérieur

* **Description :** accepter les issues, les signalements et les rapports de vulnérabilité, et
  décliner les pull requests extérieures jusqu'à ce que la question soit réglée autrement.
* **Pourquoi rejetée :** elle ferme la porte à des contributions que le projet dit accueillir, pour
  éviter une question à laquelle l'accord répond.

### Alternative 3 — Un Developer Certificate of Origin seul

* **Description :** demander à chaque commit une ligne `Signed-off-by:` certifiant le droit du
  contributeur de le soumettre.
* **Pourquoi rejetée :** elle certifie un droit de soumettre sous la licence PolyForm, qui n'accorde
  aucun droit de re-licencier, si bien que la brèche reste ouverte.

### Alternative 4 — Une licence du contributeur, qui garde son droit d'auteur

* **Description :** un contributor licence agreement accordant au propriétaire du projet une licence
  perpétuelle, irrévocable et sous-licenciable, le droit d'auteur restant au contributeur.
* **Pourquoi rejetée :** elle donnerait à KillMutants un modèle propre, à côté de celui que
  just-dummies a déjà pour le même propriétaire et les deux mêmes licences, et le mainteneur a choisi
  d'aligner les deux.

## Conséquences

### Positives

* Une contribution fusionnée peut être re-licenciée, sous-licenciée à REEFACT et vendue comme le
  code du propriétaire du projet lui-même.
* `CONTRIBUTING.md` dit à quelles conditions une contribution est acceptée, ce que demandait #102.
* KillMutants et just-dummies demandent la même chose à un contributeur.

### Négatives

* Un contributeur renonce, sans contrepartie, aux droits patrimoniaux sur sa contribution ; certains
  ne contribueront pas à ces conditions.
* Chaque pull request venant d'une branche `claude/*` porte la case et la coche.
* Le contrôle tourne tel que `main` le contient, si bien qu'une modification de ce contrôle n'est pas
  exercée par la pull request qui la fait.

### Risques

* L'accord est le texte même de just-dummies, au nom du projet près : un défaut dans l'un est un
  défaut dans les deux projets.
* Un contributeur conteste une acceptation donnée en cochant une case.
* Le contrôle n'est pas rendu obligatoire dans le ruleset `main-protection`, et une pull request est
  fusionnée sans l'accord.

### Actions de suivi

* Rendre obligatoire le contrôle `Contributor agreement` dans le ruleset `main-protection`.
* Trancher la question du code de conduite, que #102 demande aussi et que cette décision ne règle
  pas.
