# DEC0008 | Ne jamais réutiliser un hôte de test d'un mutant à l'autre

## Statut

| Date | Statut | Note | Compte rendu lié |
|---|---|---|---|
| 2026-08-31 | Accepté | | |

## Contexte

Avec la sélection par couverture en place (DEC0007), l'exécution d'un mutant coûte environ **0,5 s
pour lancer un hôte de test contre 0,12 s de test réel**. Le démarrage est le plancher, et c'est
désormais le plus gros coût unitaire d'un run. La façon évidente de le supprimer serait de garder des
hôtes vivants et de confier chaque mutant à un processus déjà démarré.

Stryker.NET fait exactement cela. Ses deux pools de runners conservent un sac de runners vivants, en
prennent un pour un travail puis le remettent (`VsTestRunnerPool.cs:95-111`), et il lui faut des
points explicites où ces processus de longue durée sont ramenés de force à un état propre — après le
run initial, et après la passe de couverture, où le commentaire dit franchement la seconde raison :
*« Reset test processes to trigger coverage file flush (process exit writes coverage) »*
(`MicrosoftTestPlatformRunnerPool.cs:96,140`).

La réutilisation n'y est pas une optimisation indépendante. Stryker peut réutiliser un hôte parce
que, sous les mutant schemata, l'assembly sur disque ne change jamais : tous les mutants y sont déjà
compilés et un commutateur d'exécution choisit lequel est actif. Confier un autre mutant à un
processus chaud revient à positionner une variable.

KillMutants change l'assembly lui-même à chaque mutant (DEC0002), et un processus .NET ne relit pas
un assembly qu'il a déjà chargé.

Trois options en découlent :

1. **Un processus neuf par mutant.** On paie le coût de lancement.
2. **Adopter les schemata et réutiliser les hôtes.**
3. **Un seul hôte, un `AssemblyLoadContext` collectable par mutant.** On garde un assembly par
   mutant, mais l'isolation passe du système d'exploitation au CLR : l'état statique hors du contexte
   collectable persiste, un contexte qui échoue à se décharger fuit, et tout test touchant un type du
   contexte par défaut la met en échec.

Une étude antérieure menée pour ce projet attribuait à l'issue #3742 un bug de fuite d'état gonflant
le score chez Stryker. Cette référence n'a pas pu être atteinte et est donc **non vérifiée** ; rien
ici ne s'appuie dessus. Tout ce qui précède repose sur le code cité, lu directement, et sur le
mécanisme, qui n'a besoin d'aucune citation : un processus qu'on ne redémarre pas conserve ce que le
dernier test y a laissé.

## Décision

Dans ce contexte, nous lançons un processus hôte de test neuf pour chaque exécution de mutant, et ne
mettons jamais un hôte en pool, ne le réutilisons ni ne le gardons chaud.

## Justification

Un hôte chaud ne peut pas être correct ici. Parce qu'un processus .NET ne relit pas un assembly qu'il
a déjà chargé, et parce que chaque mutant est un assembly différent sur disque, un hôte réutilisé
continuerait de tester le mutant avec lequel il a démarré et rapporterait tous les suivants comme
survivants — une corruption silencieuse et totale du résultat.

Le vrai choix n'est donc pas « rapide ou prudent ». Toute façon de garder l'hôte chaud inverse
le DEC0002 ou déplace l'isolation dans le CLR, où elle dépend de ce qu'aucun élément de la suite
testée ne touche le contexte par défaut.

L'arbitrage n'est pas serré, parce que toute la production de KillMutants tient en une affirmation :
*ces mutants ont été attrapés, ceux-là non*. Un mécanisme qui laisse l'état d'un mutant colorer le
verdict d'un autre ne rend pas l'outil légèrement moins précis — il rend le nombre qu'il affiche
indigne de confiance, et dans le sens flatteur. La défaillance serait de plus invisible : un mutant
faussement rapporté tué ressemble exactement à un mutant réellement tué. La vitesse est une
fonctionnalité ; le score est le produit.

Payer le coût de lancement achète une isolation garantie par le système d'exploitation plutôt que par
le fait de penser à réinitialiser — précisément ce que les points de réinitialisation explicites de
Stryker existent pour compenser.

## Alternatives envisagées

### Alternative 1 — Adopter les schemata et réutiliser les hôtes

* **Description :** compiler tous les mutants dans un seul assembly derrière un commutateur
  d'exécution, comme Stryker, ce qui réduit le fait de confier un autre mutant à un processus chaud à
  positionner une variable.
* **Pourquoi écartée :** cela inverse le DEC0002 et reprend tout ce que cette décision supprimait —
  placement conditionnel, contextes illégaux, boucle de compilation/rollback — plus la fuite d'état
  qui rend les points de réinitialisation explicites nécessaires en premier lieu.

### Alternative 2 — Un seul hôte, un `AssemblyLoadContext` collectable par mutant

* **Description :** garder un unique processus et charger l'assembly de chaque mutant dans son propre
  contexte collectable, déchargé entre deux mutants.
* **Pourquoi écartée :** cela déplace l'isolation du système d'exploitation vers le CLR. L'état
  statique hors du contexte collectable persiste, un contexte qui échoue à se décharger fuit, et tout
  test touchant un type du contexte par défaut la met en échec.

## Conséquences

### Positives

* L'isolation est garantie par le système d'exploitation plutôt que par le fait de penser à
  réinitialiser. Aucun champ statique, cache, singleton, assembly chargé, réglage de culture ou
  descripteur ouvert ne peut passer d'un mutant au suivant, parce qu'il n'y a pas de « suivant » : il
  n'y a qu'un nouveau processus.
* C'est ce qui rend sûre la parallélisation du milestone 6 : répertoires de sortie isolés plus
  processus par mutant signifient que deux mutants concurrents n'ont aucune surface commune.
* Cela compose avec le DEC0002 au lieu de le contrarier. Un mutant, un assembly, un processus, un
  verdict, de bout en bout.

### Négatives

* Nous conservons un plancher d'environ 0,5 s par exécution de mutant que Stryker ne paie pas.

### Risques

*Non consigné au moment de la décision.*

### Actions de suivi

* Réexaminer l'option de l'`AssemblyLoadContext` collectable — le seul chemin futur qui n'inverse pas
  le DEC0002 — si le démarrage de processus cesse d'être le plancher pour une autre raison, par
  exemple une suite bien plus lente ou beaucoup plus de tests que de mutants. L'adopter exigerait de
  démontrer qu'un contexte collectable isole réellement une vraie suite de tests, pas seulement une
  fixture.
