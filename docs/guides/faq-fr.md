# FAQ

🌍 **Langues :**  
🇬🇧 [English](./faq-en.md) | 🇫🇷 Français (ce fichier)

Des réponses courtes aux questions qui reviennent le plus. Chacune renvoie à la page qui traite le
sujet comme il faut.

## Choisir l'outil

### En quoi est-ce différent de Stryker.NET ?

Stryker.NET est l'implémentation de référence pour .NET, et il a été étudié de près avant qu'une
ligne de KillMutants ne soit écrite ([l'étude](../study/stryker-net-fr.md)). Les différences sont
des choix, pas des manques :

* **Périmètre.** xUnit 4 et .NET moderne seulement ; pas de VSTest, pas de .NET Framework, pas de
  format de projet ancien.
* **Une compilation par mutant**, plutôt qu'une seule construction instrumentée où chaque mutant
  est activé à l'exécution — et un processus de test neuf par mutant, jamais un hôte réutilisé.
* **Un score honnête** : les mutants non couverts comptent comme non détectés, et une exécution
  partielle ou interrompue n'affiche aucun score du tout.
* **Un mode pull request** qui répond par un verdict en quelques secondes, et s'élargit à ce qu'un
  test modifié a exercé.

[Principes de conception](./design-principles-fr.md) argumente chacun d'eux.

### Supporte-t-il NUnit, MSTest ou TUnit ? .NET Framework ?

Non, et aucune abstraction n'existe en prévision. Le couplage à xUnit 4 est la connaissance de la
ligne de commande de son runner, et c'est ce qui garde l'outil petit. Voir
[Principes de conception](./design-principles-fr.md#étroit-à-dessein).

### A-t-il besoin de quelque chose dans mes projets de test ?

Aucun paquet, aucun attribut, aucune classe de base. Un projet de test est reconnu par sa référence
à la famille de paquets `xunit.v3` en version 4 — `xunit.v3`, `xunit.v3.core`, `xunit.v3.mtp-v2` ou
`xunit.v3.core.mtp-v2`.

## Lire les résultats

### Mon code est couvert à 100 %. Pourquoi le score est-il de 46 % ?

Parce que la couverture dit qu'une ligne a été *exécutée* et qu'un score de mutation dit qu'elle a
été *vérifiée*. Un test peut exécuter `orderTotal >= 100m` mille fois sans jamais essayer 100. Voir
[Démarrer](./getting-started-fr.md#quest-ce-quun-mutant-).

### Pourquoi les mutants non couverts font-ils baisser le score ?

Parce que la suite ne les aurait pas remarqués non plus. Sauter leur exécution de tests est une
optimisation, pas un pardon, et les exclure permettrait à un projet de faire monter son score en
ajoutant du code qu'aucun test ne touche. Voir
[Concepts fondamentaux](./core-concepts-fr.md#le-score-et-pourquoi-le-code-non-couvert-compte).

### Pourquoi n'y a-t-il pas de score sur une exécution `--since` ?

La population est le diff, choisi contre une base qui diffère à chaque exécution, si bien qu'un
pourcentage sur elle ne peut être comparé à aucune autre exécution. Une exécution partielle affiche
des constats et un verdict à la place. Voir
[Juger une pull request](./pull-requests-fr.md#ce-quelle-affiche).

### Un mutant a survécu et je n'arrive pas à écrire un test qui le tue. Que faire ?

Il est peut-être **équivalent** : un changement qui ne modifie pas le comportement du programme, et
qu'aucun test ne peut donc détecter. Les mutateurs évitent d'en produire partout où cela peut se
savoir statiquement, mais le cas général est indécidable. Lisez-le, et s'il est équivalent,
laissez-le — c'est un coût connu de la méthode, pas un défaut de vos tests. Si c'est une clause de
garde que les tests ne déclenchent légitimement jamais,
[`--ignore-calls`](./mutators-fr.md#faire-taire-les-appels-dont-vous-ne-voulez-pas-entendre-parler)
fait taire cet appel et compte ce qu'il a fait taire.

### Un mutant a été tué et je n'y crois pas.

Chaque mise à mort nomme les tests qui ont échoué, sous `killedBy` dans le rapport JSON. Mettez la
mutation dans le fichier à la position indiquée, lancez ces tests, regardez-les échouer. Si vous
soupçonnez un test instable, `--verify-kills` relance isolément un échantillon des mises à mort et
signale celles qui ne se répètent pas. Voir
[Lire un rapport](./reading-a-report-fr.md#revérifier-les-mises-à-mort).

### Pourquoi le score change-t-il quand je laisse une famille de côté ?

Parce qu'une autre question a été posée. Un score n'est comparable qu'à un autre score issu du même
catalogue, alors choisissez-en un pour un job de CI et gardez-le dans `killmutants.json`. Voir
[Les mutateurs](./mutators-fr.md#choisir-un-catalogue).

### Qu'est-ce que le budget de délai sur la dernière ligne ?

La durée pendant laquelle les tests d'un mutant peuvent s'exécuter avant qu'il ne soit déclaré en
délai dépassé (timeout) : la durée de la suite non mutée, fois trois, plus trente secondes. Il est
affiché sous forme de formule pour qu'un lecteur puisse le vérifier. Voir
[Concepts fondamentaux](./core-concepts-fr.md#le-budget-de-délai-est-écrit-noir-sur-blanc).

## L'exécuter

### Combien de temps prend une exécution ?

Mesuré sur ce dépôt : 384 mutants sur l'assembly principal, 6,8 minutes sur quatre cœurs, avec
trois quarts du temps passés à tester les mutants et un cinquième à mesurer la couverture. Une
exécution `--since` sur une pull request prend quelques secondes. L'[architecture](../architecture-fr.md)
en donne le détail.

### Mon job de CI a une limite de temps. Que se passe-t-il quand l'exécution l'atteint ?

Donnez à l'exécution une échéance qui lui est propre avec `--stop-after`, sous la limite du job.
Elle rapporte alors les verdicts qu'elle a atteints, n'affiche aucun score, ne passe aucune
barrière de qualité, et laisse un journal et un plan à côté du rapport pour ce qu'elle n'a pas
atteint. `--resume` termine le reste dans un job ultérieur. Voir
[Intégration continue](./continuous-integration-fr.md#une-exécution-qui-dépasse-son-budget).

### L'exécution dit qu'un projet a été « mis de côté ». Mon score est-il faux ?

Le score couvre la partie qui a été mesurée, et le rapport le dit. Un projet que MSBuild n'a pas pu
lire, un projet de test qui ne s'est pas construit, ou une suite qui échoue sur du code non muté
est nommé en tête avec sa raison ; réparez cela, et la prochaine exécution le mesure. L'exécution
sort avec le code `2`, ce qui dit à un job que c'est l'environnement qui est en cause, pas les
tests. Voir
[Intégration continue](./continuous-integration-fr.md#quand-une-partie-de-lexécution-ne-peut-pas-être-mesurée).

### L'exécution dit que mon projet de test cible plusieurs frameworks et s'arrête.

Un score décrit une seule construction du code, alors passez `--target-framework net10.0` (ou
`targetFramework` dans `killmutants.json`). Relancez-la pour un autre framework. Voir
[Intégration continue](./continuous-integration-fr.md#pointer-vers-le-bon-code).

### Puis-je l'exécuter sur mes builders et mes helpers de test ?

Vous le pouvez, mais vous ne voulez probablement pas des constats. Une bibliothèque de builders, de
fakes et de helpers d'assertion déclare `<KillMutantsTestSupport>true</KillMutantsTestSupport>` et
est laissée de côté sans cacher le code qui se trouve derrière. Voir
[Les mutateurs](./mutators-fr.md#laisser-du-code-tranquille).

### Pourquoi refuse-t-il `*.received.*` dans `--ignore-untracked` ?

Parce que ce motif correspond aussi à `x.received.cs`, un fichier qui change ce qui est construit.
Nommez plutôt les extensions : `*.received.html`, `*.received.json`. Voir
[Intégration continue](./continuous-integration-fr.md#les-fichiers-que-les-tests-écrivent-eux-mêmes).

### Où vont les assemblies mutés ?

Dans une copie privée de la sortie du projet de test, une par worker, jamais dans votre `bin`. Rien
de l'outil n'est écrit dans votre sortie de build.

---

[← Index de la documentation](../README-fr.md)
