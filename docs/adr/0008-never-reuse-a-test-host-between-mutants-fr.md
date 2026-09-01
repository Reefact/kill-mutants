# ADR-0008 — Ne jamais réutiliser un hôte de test d'un mutant à l'autre

**Statut :** accepté · **Date :** 2026-08-31

## Contexte

Avec la sélection par couverture en place (ADR-0007), l'exécution d'un mutant coûte environ **0,5 s
pour lancer un hôte de test contre 0,12 s de test réel**. Le démarrage est le plancher, et c'est
désormais le plus gros coût unitaire d'un run. La façon évidente de le supprimer serait de garder des
hôtes vivants et de confier chaque mutant à un processus déjà démarré.

Stryker.NET fait exactement cela. Ses deux pools de runners conservent un sac de runners vivants, en
prennent un pour un travail puis le remettent (`VsTestRunnerPool.cs:95-111`), et il lui faut des
points explicites où ces processus de longue durée sont ramenés de force à un état propre — après le
run initial, et après la passe de couverture, où le commentaire dit franchement la seconde raison :
*« Reset test processes to trigger coverage file flush (process exit writes coverage) »*
(`MicrosoftTestPlatformRunnerPool.cs:96,140`).

## Ce qui tranche

La réutilisation n'est pas une optimisation indépendante. Elle est **couplée aux mutant schemata**.

Stryker peut réutiliser un hôte parce que, sous les schemata, l'assembly sur disque ne change jamais :
tous les mutants y sont déjà compilés et un commutateur d'exécution choisit lequel est actif. Confier
un autre mutant à un processus chaud revient à positionner une variable.

KillMutants change l'assembly lui-même à chaque mutant (ADR-0002). Un processus .NET ne relit pas un
assembly qu'il a déjà chargé : un hôte chaud continuerait donc de tester le mutant avec lequel il a
démarré et rapporterait tous les suivants comme survivants — une corruption silencieuse et totale du
résultat.

Le vrai choix n'est donc pas « rapide ou prudent ». C'est :

1. **Un processus neuf par mutant.** On paie le coût de lancement.
2. **Adopter les schemata et réutiliser les hôtes.** Cela inverse l'ADR-0002 et reprend tout ce qu'il
   supprimait : placement conditionnel, contextes illégaux, boucle de compilation/rollback — plus la
   fuite d'état qui rend les points de réinitialisation nécessaires.
3. **Un seul hôte, un `AssemblyLoadContext` collectable par mutant.** On garde un assembly par
   mutant, mais l'isolation passe du système d'exploitation au CLR : l'état statique hors du contexte
   collectable persiste, un contexte qui échoue à se décharger fuit, et tout test touchant un type du
   contexte par défaut la met en échec.

## Décision

**Un processus hôte neuf par exécution de mutant. Les hôtes de test ne sont jamais mis en pool,
réutilisés, ni gardés chauds.**

## Conséquences

- Nous conservons un plancher d'environ 0,5 s par exécution de mutant que Stryker ne paie pas.
- En échange, l'isolation est garantie par le système d'exploitation plutôt que par le fait de penser
  à réinitialiser. Aucun champ statique, cache, singleton, assembly chargé, réglage de culture ou
  descripteur ouvert ne peut passer d'un mutant au suivant, parce qu'il n'y a pas de « suivant » : il
  n'y a qu'un nouveau processus.
- C'est ce qui rend sûre la parallélisation du milestone 6 : répertoires de sortie isolés plus
  processus par mutant signifient que deux mutants concurrents n'ont aucune surface commune.
- Cela compose avec l'ADR-0002 au lieu de le contrarier. Un mutant, un assembly, un processus, un
  verdict, de bout en bout.

## Pourquoi l'arbitrage n'est pas serré, pour cet outil

Toute la production de KillMutants tient en une affirmation : *ces mutants ont été attrapés, ceux-là
non*. Un mécanisme qui laisse l'état d'un mutant colorer le verdict d'un autre ne rend pas l'outil
légèrement moins précis — il rend le nombre qu'il affiche indigne de confiance, et dans le sens
flatteur. Rien d'autre de ce que fait l'outil ne compte si le score peut être gonflé en silence, et la
défaillance serait invisible : un mutant faussement rapporté tué ressemble exactement à un mutant
réellement tué.

La vitesse est une fonctionnalité. Le score est le produit.

## À réexaminer si

L'option 3 est le seul chemin futur qui n'inverse pas l'ADR-0002, et elle ne vaudra d'être mesurée que
si le démarrage de processus cesse d'être le plancher pour une autre raison — une suite bien plus
lente, ou beaucoup plus de tests que de mutants. L'adopter exigerait de démontrer qu'un contexte
collectable isole réellement une vraie suite de tests, pas seulement une fixture.

## Note sur les sources

Une étude antérieure menée pour ce projet attribuait à l'issue #3742 un bug de fuite d'état gonflant
le score chez Stryker. Cette session ne peut pas atteindre l'API GitHub de ce dépôt : cette référence
est donc **non vérifiée** et cet ADR ne s'appuie pas dessus. Tout ce qui précède repose sur le code
cité, lu directement, et sur le mécanisme, qui n'a besoin d'aucune citation : un processus qu'on ne
redémarre pas conserve ce que le dernier test y a laissé.
