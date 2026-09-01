# ADR-0007 — Mesurer la couverture avec une sonde qui préserve le type, un test à la fois

**Statut :** accepté · **Date :** 2026-08-31

## Contexte

Exécuter tous les tests pour chaque mutant est le coût dominant d'un run. N'exécuter que les tests
qui atteignent un mutant demande une carte de couverture, et l'ADR-0002 ne nous laisse rien qui
observe l'atteignabilité : aucun commutateur de mutation n'est injecté, donc aucun code n'enregistre
avoir été exécuté.

Trois façons de l'obtenir :

1. **Réutiliser le commutateur de mutation comme sonde**, comme Stryker — son
   `MutantControl.IsActive(id)` sert aussi d'enregistreur de couverture. Indisponible pour nous, et
   le réintroduire annulerait l'ADR-0002.
2. **Un outil de couverture externe.** Donne une couverture de lignes, pas l'attribution par test,
   qui est justement ce qui compte.
3. **Un build de couverture dédié, avec sa propre sonde.**

Et deux façons d'attribuer un passage à un test : piloter la barrière `-automated sync` de xUnit, en
retenant l'hôte entre les tests et en lisant la sortie de la sonde pendant qu'il est bloqué ; ou
simplement exécuter un test à la fois.

## Décision

Un **build dédié à la couverture** où chaque site de mutation est enveloppé dans un enregistreur qui
renvoie son argument :

```csharp
public static T Hit<T>(int id, T value) { record(id); return value; }
```

et **une exécution par méthode de test**, sélectionnée par son nom, l'enregistreur écrivant dans un
fichier propre à cette exécution.

## Pourquoi ce n'est pas un retour aux schemata

L'enregistreur *préserve le type*. Envelopper une expression ne peut changer ni ce qu'elle vaut ni
quand elle est évaluée — court-circuit, ordre et types survivent tous. Cette seule propriété
supprime tout ce qui rend la commutation de mutation difficile : il n'y a pas de branche à placer,
donc aucun contexte où le placement est illégal, donc aucune boucle de compilation/rollback. Et elle
n'est jamais présente en même temps qu'une mutation : le build de couverture est émis une fois,
utilisé, puis jeté.

Vérifié sur un site de mutation à l'intérieur d'un `Expression<Func<int, bool>>`, le cas le plus
susceptible de casser un schéma de réécriture : cela compile, l'enregistreur se déclenche quand
l'arbre est compilé puis invoqué, et les deux mutants qui s'y trouvaient ont été tués.

## Pourquoi une exécution par test plutôt qu'une barrière de synchronisation

La barrière est le mécanisme le plus astucieux et le mauvais compromis. Une exécution par test
n'exige aucune communication inter-processus, aucun protocole, et aucun raisonnement sur ce qui
tourne par ailleurs ; l'attribution est exacte parce que rien d'autre ne s'exécute. Elle réutilise
telles quelles la sélection par nom de l'ADR-0006 et les bacs à sable du milestone 6, et se
parallélise gratuitement entre les workers.

Le coût est un lancement de processus par test, payé une fois, face à un nombre de mutants
normalement d'un ordre de grandeur supérieur.

## Conséquences, mesurées

- **Les mutants non couverts ne sont jamais exécutés**, et sont rapportés `NoCoverage` plutôt que
  `Survived`. C'est le gain non ambigu, et il relève autant de l'honnêteté que de la vitesse :
  « aucun test n'atteint ce code » est un constat différent, et souvent plus urgent, que « un mutant
  a survécu ».
- **La sélection paie proportionnellement à la durée de la suite.** Sur une fixture dont les tests
  font un vrai travail, 60 mutants sont passés de 29,3 s à 22,6 s. Sur une fixture dont les tests
  sont instantanés, elle n'a rien fait gagner de mesurable.
- **Le démarrage de processus est désormais le plancher.** Chaque exécution coûte environ 0,5 s de
  lancement d'un hôte de test contre 0,12 s de test. Le levier évident suivant serait de réutiliser
  un hôte à chaud, et nous le refusons : c'est la source de la plainte de correction la plus ancienne
  chez Stryker, où de l'état global de processus fuit entre mutants et gonfle les scores.
- **Un build instrumenté qui échoue interrompt le run** avec un diagnostic, plutôt que de dégrader
  silencieusement vers « tout exécuter ». `--no-coverage` est la porte de sortie, et c'est aussi ce
  qui rend les deux chemins comparables dans un test.

## À réexaminer si

Si le démarrage de processus cesse un jour de dominer — une suite bien plus lente, ou beaucoup plus
de tests que de mutants — l'attribution par barrière vaudra sa complexité. Rien ici ne la ferme :
elle remplacerait `CoverageCollector` en laissant intacts la carte, la sélection et les bacs à sable.
