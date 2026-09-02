# DEC0007 | Mesurer la couverture avec une sonde qui préserve le type, un test à la fois

## Statut

| Date | Statut | Note | Compte rendu lié |
|---|---|---|---|
| 2026-08-31 | Accepté | | |

## Contexte

Exécuter tous les tests pour chaque mutant est le coût dominant d'un run. N'exécuter que les tests
qui atteignent un mutant demande une carte de couverture, et le DEC0002 ne nous laisse rien qui
observe l'atteignabilité : aucun commutateur de mutation n'est injecté, donc aucun code n'enregistre
avoir été exécuté.

Trois façons de l'obtenir :

1. **Réutiliser le commutateur de mutation comme sonde**, comme Stryker — son
   `MutantControl.IsActive(id)` sert aussi d'enregistreur de couverture.
2. **Un outil de couverture externe.**
3. **Un build de couverture dédié, avec sa propre sonde.**

Et deux façons d'attribuer un passage à un test : piloter la barrière `-automated sync` de xUnit, en
retenant l'hôte entre les tests et en lisant la sortie de la sonde pendant qu'il est bloqué ; ou
simplement exécuter un test à la fois.

Mesuré sur les fixtures : chaque lancement d'hôte de test coûte environ 0,5 s contre 0,12 s de test.
Sur une fixture dont les tests font un vrai travail, 60 mutants sont passés de 29,3 s à 22,6 s avec
la sélection ; sur une fixture dont les tests sont instantanés, la sélection n'a rien fait gagner de
mesurable. Un run compte normalement un ordre de grandeur plus de mutants que de tests.

La plainte de correction la plus ancienne chez Stryker concerne la réutilisation à chaud des hôtes de
test, où de l'état global de processus fuit entre mutants et gonfle les scores.

Certains sites de mutation n'ont aucun type qui puisse traverser un enveloppeur générique : une
valeur qui est un ref struct, un pointeur, `void`, ou qui n'a aucun type naturel (RB-017). Une mesure
peut aussi expirer, planter ou être interrompue.

## Décision

Dans ce contexte, nous mesurons la couverture depuis un build dédié où chaque site de mutation est
enveloppé dans un enregistreur préservant le type, en exécutant une méthode de test à la fois
sélectionnée par son nom, l'enregistreur écrivant dans un fichier propre à cette exécution.

## Justification

L'enregistreur *préserve le type* :

```csharp
public static T Hit<T>(int id, T value) { record(id); return value; }
```

Envelopper une expression ne peut changer ni ce qu'elle vaut ni quand elle est évaluée —
court-circuit, ordre et types survivent tous. Cette seule propriété supprime tout ce qui rend la
commutation de mutation difficile : il n'y a pas de branche à placer, donc aucun contexte où le
placement est illégal, donc aucune boucle de compilation/rollback. Et elle n'est jamais présente en
même temps qu'une mutation : le build de couverture est émis une fois, utilisé, puis jeté. Ce n'est
donc pas un retour aux schemata, et le DEC0002 tient.

Le cas a été vérifié sur un site de mutation à l'intérieur d'un `Expression<Func<int, bool>>`, le plus
susceptible de casser un schéma de réécriture : cela compile, l'enregistreur se déclenche quand
l'arbre est compilé puis invoqué, et les deux mutants qui s'y trouvaient ont été tués.

Une exécution par test plutôt que la barrière est le bon compromis, même si la barrière est le
mécanisme le plus astucieux. Une exécution par test n'exige aucune communication inter-processus,
aucun protocole, et aucun raisonnement sur ce qui tourne par ailleurs ; l'attribution est exacte
parce que rien d'autre ne s'exécute. Elle réutilise telles quelles la sélection par nom de le DEC0006
et les bacs à sable du milestone 6, et se parallélise gratuitement entre les workers. Son coût est un
lancement de processus par test, payé une fois, face à un nombre de mutants normalement d'un ordre de
grandeur supérieur.

## Alternatives envisagées

### Alternative 1 — Réutiliser le commutateur de mutation comme sonde, comme Stryker

* **Description :** laisser l'appel injecté `MutantControl.IsActive(id)` servir aussi d'enregistreur
  de couverture.
* **Pourquoi écartée :** elle nous est indisponible — le DEC0002 n'injecte aucun commutateur de
  mutation — et en réintroduire un annulerait cette décision.

### Alternative 2 — Un outil de couverture externe

* **Description :** obtenir la couverture d'un outil existant plutôt que de nous instrumenter
  nous-mêmes.
* **Pourquoi écartée :** il donne une couverture de lignes, pas l'attribution par test, qui est
  justement ce qui compte pour sélectionner les tests atteignant un mutant.

### Alternative 3 — Attribuer les passages par une barrière de synchronisation

* **Description :** piloter la barrière `-automated sync` de xUnit, en retenant l'hôte de test entre
  les tests et en lisant la sortie de la sonde pendant qu'il est bloqué.
* **Pourquoi écartée :** c'est le mécanisme le plus astucieux et le mauvais compromis. Il exige une
  communication inter-processus, un protocole et un raisonnement sur ce qui tourne par ailleurs, pour
  économiser des lancements de processus payés une fois face à un nombre de mutants d'un ordre de
  grandeur supérieur.

### Alternative 4 — Réutiliser un hôte de test à chaud entre les exécutions

* **Description :** le levier évident suivant une fois le démarrage de processus devenu le plancher :
  garder l'hôte de test en vie entre les exécutions au lieu d'en lancer un par test.
* **Pourquoi écartée :** c'est la source de la plainte de correction la plus ancienne chez Stryker, où
  de l'état global de processus fuit entre mutants et gonfle les scores.

## Conséquences

### Positives

* **Les mutants non couverts ne sont jamais exécutés**, et sont rapportés `NoCoverage` plutôt que
  `Survived`. C'est le gain non ambigu, et il relève autant de l'honnêteté que de la vitesse :
  « aucun test n'atteint ce code » est un constat différent, et souvent plus urgent, que « un mutant
  a survécu ».
* Un build instrumenté qui échoue interrompt le run avec un diagnostic, plutôt que de dégrader
  silencieusement vers « tout exécuter ». Le build instrumenté doit également passer la suite avant
  que quoi que ce soit n'en soit mesuré : l'enveloppement ne peut pas changer ce qu'une expression
  vaut, mais une carte de couverture bâtie sur un programme qui ne se comporte plus comme avant
  aurait l'air parfaitement valide.
* `--no-coverage` est la porte de sortie, et c'est aussi ce qui rend les deux chemins comparables
  dans un test.

### Négatives

* La sélection ne paie qu'en proportion de la durée de la suite : de 29,3 s à 22,6 s pour 60 mutants
  sur une fixture dont les tests font un vrai travail, et rien de mesurable sur une fixture dont les
  tests sont instantanés.
* Le démarrage de processus est désormais le plancher — environ 0,5 s de lancement d'un hôte de test
  contre 0,12 s de test — et le levier évident contre cela est refusé (alternative 4).
* Tous les sites ne peuvent pas porter d'enregistreur, et c'est une troisième réponse, pas une
  réponse manquante. Un site dont la valeur est un ref struct, un pointeur, `void`, ou n'a aucun type
  naturel, ne peut pas être argument de `Hit<T>` (RB-017). Une mesure qui a expiré, planté ou été
  interrompue ne peut pas non plus se lire « ce test n'atteint rien ». Les deux cas se résolvent en
  *exécuter tous les tests* : plus lent, jamais faux ; seul un site mesuré et trouvé non atteint est
  signalé `NoCoverage`.

### Risques

*Non consigné au moment de la décision.*

### Actions de suivi

* Reconsidérer l'attribution par barrière si le démarrage de processus cesse un jour de dominer — une
  suite bien plus lente, ou beaucoup plus de tests que de mutants. Rien ici ne la ferme : elle
  remplacerait `CoverageCollector` en laissant intacts la carte, la sélection et les bacs à sable.
