# DEC0023 | La progression de la console se choisit par une option, sans détection, et sa valeur par défaut est un journal lisible partout

## Statut

| Date | Statut | Note | Compte rendu lié |
|---|---|---|---|
| 2026-11-01 | Proposé | Première rédaction | |
| 2026-11-01 | Accepté | À la demande du mainteneur | |

## Contexte

Une exécution dure de quelques minutes à plusieurs heures et écrit sur l'erreur standard ce qu'elle
fait. Aujourd'hui, la forme de cette sortie est choisie par l'outil, selon que l'erreur standard est
redirigée ou non : sur un terminal, une seule ligne est réécrite sur place ; sinon, chaque phase est
annoncée une fois et son compteur répété au plus une fois par minute. Les commentaires du rapporteur
consignent pourquoi les deux formes existent : des milliers de retours chariot dans un journal ne
servent à personne, et une exécution dont le test des mutants a duré cinquante-sept minutes a un jour
affiché une ligne, puis plus rien jusqu'à l'annulation du job.

Suivre une exécution de 75 minutes sur `Reefact/first-class-errors` par cette sortie (#65) a montré ce
que ni l'une ni l'autre forme ne dit. Elle ne nomme aucun mutant, si bien qu'un mutant lent ne se
distingue pas d'une exécution bloquée ; une exécution tuée avec son conteneur avait le même aspect
qu'une exécution simplement lente ; aucune ligne ne porte d'heure ; et une partie mise de côté selon
[DEC0022](0022-a-run-carries-on-past-what-fails-and-reports-what-it-measured-and-what-it-could-not-fr.md)
n'est nommée que dans le rapport final.

La redirection ne dit pas qui lit la sortie. Un terminal peut se trouver dans un conteneur dont la
sortie est capturée, et un journal de CI peut être lu par une personne ; la supposition de l'outil se
trompe dans les deux sens et ne laisse rien à changer à celui qui est surpris. Le mainteneur a décidé
de ne rien détecter, et de proposer une option que l'utilisateur fixe.

Les options de la ligne de commande sont courtes et disent sur quoi elles agissent : `--exclude`,
`--since`, `--stop-after`, `--no-coverage`, `--report-json`. Aucune ne concerne ce qui s'affiche sur la
console. L'outil n'a pas encore de version publiée : aucun tag n'existe, la version est `0.1.0-alpha`
et le journal des changements ne contient que des changements non publiés, si bien qu'aucun pipeline ne
peut dépendre aujourd'hui du nom, des valeurs ou de la valeur par défaut d'une option.
[DEC0009](0009-exit-codes-are-a-public-contract-fr.md) traite les codes de sortie comme un contrat
public parce que des outils en dépendent ; le rapport JSON est le canal que l'outil offre aux outils.

Le mainteneur a aussi décidé que les lignes de la progression de la console ne sont pas un contrat, que
l'aide indique que le rapport JSON est le canal des outils, et qu'aucune dépendance n'est ajoutée pour
l'affichage.

## Décision

Dans ce contexte, nous décidons que la progression de la console se choisit par une option explicite,
`--progress`, qui prend `log`, `live` ou `none`, que `log` en est la valeur par défaut, et que l'outil
ne détecte rien du terminal pour la choisir.

## Justification

Une option dit ce que l'utilisateur veut, là où une supposition tirée de la redirection ne le peut pas :
elle supprime le cas où un terminal dans un conteneur, ou un journal lu par une personne, reçoit
l'autre forme que celle voulue, et elle donne à l'utilisateur quelque chose à changer quand la sortie le
surprend.

La valeur par défaut doit convenir à tous ceux qui ne la fixent pas, y compris l'utilisateur qui n'a
pas lu l'aide. Une ligne datée par transition se lit de la même façon sur un terminal, dans un fichier
et dans un journal de CI, alors qu'une ligne réécrite sur place est ce que les commentaires du
rapporteur décrivent comme rendant un journal capturé illisible. Prendre par défaut la forme qui ne
peut pas nuire, et la forme agréable sur demande, échange une petite perte pour l'utilisateur sur un
terminal contre un échec pour celui dont la sortie est capturée.

Décider le nom, les valeurs et la valeur par défaut avant la première version publiée est ce qui garde
la décision peu coûteuse : rien n'en dépend encore, et après une publication un pipeline en dépendra,
comme le montrent les codes de sortie avec DEC0009. Laisser la forme des lignes hors de l'engagement,
avec le rapport JSON pour canal des outils, garde l'affichage libre de s'améliorer sans un compte
rendu à chaque fois.

Le nom `--progress` dit sur quoi l'option agit, comme les autres options, et ne se lit pas comme
désignant l'endroit où le rapport est écrit, ce que `--report-json` fait déjà.

## Alternatives envisagées

### Alternative 1 — Continuer de choisir selon que l'erreur standard est redirigée

* **Description :** le statu quo : l'outil décide, et rien ne permet de le fixer.
* **Pourquoi rejetée :** elle se trompe dans les deux sens pour les cas du contexte, et elle ne laisse
  rien à changer à l'utilisateur. Le mainteneur a décidé de ne rien détecter.

### Alternative 2 — Mettre `live` par défaut

* **Description :** la ligne réécrite sur place par défaut, `log` sur demande.
* **Pourquoi rejetée :** la valeur par défaut s'applique à tous ceux qui ne fixent rien, et dans un
  journal capturé cette forme est celle que les commentaires du rapporteur décrivent comme illisible.
  Sans détection, il n'y a aucun moyen de donner `live` à un terminal et `log` à un fichier en même
  temps.

### Alternative 3 — Un autre nom pour l'option, tel que `--output`

* **Description :** un nom qui couvre la sortie de la console en général.
* **Pourquoi rejetée :** à côté de `--report-json`, il se lit comme désignant l'endroit où le rapport
  est écrit, ce qui n'est pas ce que l'option contrôle.

## Conséquences

### Positives

* La forme de la sortie ne dépend plus d'une supposition, et un utilisateur surpris par sa sortie peut la
  changer.
* La sortie par défaut est lisible dans un terminal, dans un fichier et dans un journal de CI.
* L'affichage peut changer d'une version à l'autre : le nom de l'option, ses trois valeurs et sa valeur
  par défaut sont tenus comme un contrat, la forme des lignes ne l'est pas, et l'aide indique que le
  rapport JSON est ce que lisent les outils.

### Négatives

* Une personne sur un terminal qui ne fixe rien reçoit des lignes datées et non une ligne actualisée
  sur place, tant qu'elle ne demande pas `--progress live`.
* La ligne de commande a une option de plus à apprendre.
* Le nom de l'option, ses trois valeurs et sa valeur par défaut devront être tenus une fois l'outil
  publié : un script qui passe `--progress none` dépend de la valeur autant que du nom.

### Risques

* Un pipeline peut finir par dépendre des lignes malgré l'aide, et les traiter alors comme une
  interface.
* La valeur par défaut peut s'avérer convenir moins bien que prévu à l'utilisateur habituel, ce dont
  ce compte rendu n'a aucune mesure.

### Actions de suivi

* Implémenter l'option, les trois modes et les deux événements écrits aussitôt, comme décrit dans #65.
* Indiquer dans l'aide que le rapport JSON est le canal des outils et que les lignes de progression ne
  sont pas une interface.
* Réexaminer la valeur par défaut, par un nouveau compte rendu, si l'usage habituel de l'outil s'avère
  interactif.