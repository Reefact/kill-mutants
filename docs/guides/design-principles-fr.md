# Principes de conception

🌍 **Langues :**  
🇬🇧 [English](./design-principles-en.md) | 🇫🇷 Français (ce fichier)

Chaque outil refuse quelque chose. La plupart le font par accident et s'en excusent dans le
gestionnaire de tickets. KillMutants le fait à dessein et écrit la frontière noir sur blanc. Cette
page explique où passe cette frontière, pour que vous puissiez décider si c'est le bon outil pour
vous — et pour que ses refus cessent de ressembler à des manques.

Chaque principe renvoie à l'enregistrement de décision qui l'argumente en entier. Un enregistrement
est la mémoire du *pourquoi* du dépôt ; cette page est ce que ces décisions signifient pour vous.

## Étroit à dessein

**Supporté :** xUnit 4, .NET moderne, C#, projets au format SDK.

**Non supporté, et aucune abstraction n'existe en prévision :** xUnit 2 et antérieurs, NUnit,
MSTest, TUnit, VSTest, .NET Framework, formats de projet non-SDK, `packages.config`, F#, Visual
Basic.

L'outil de référence pour .NET porte dix ans de compatibilité : un cinquième de ses fichiers sont
des options, des milliers de lignes servent des plateformes de test et des formats de projet plus
anciens. La partie qui effectue réellement le test par mutation tient en quelques fichiers.
KillMutants garde cette partie et décline le reste, et ce sont les contraintes qui permettent à la
conception de rester assez petite pour qu'on lui fasse confiance
([architecture](../architecture-fr.md)).

KillMutants ne prend aucune dépendance envers un paquet xUnit. Il lance l'exécutable du projet de
test comme processus enfant ; le couplage est la connaissance d'un contrat de ligne de commande, pas
une référence.

## Un nombre qu'on ne peut pas expliquer n'est pas une mesure

Plusieurs choix découlent d'une seule règle : **chaque nombre d'un rapport est un nombre qu'un
lecteur peut vérifier**.

* Le budget de délai est affiché sous la forme de la formule qu'il est —
  `0.4 s baseline alone × 3 + 30 s` — parce qu'un outil qui met la moitié de ses mutants en délai
  dépassé sous un budget qu'il n'a jamais écrit a produit un titre, pas un constat.
* Chaque mise à mort nomme les tests qui ont échoué, si bien qu'un verdict mis en doute peut être
  reproduit à la main.
* Chaque mutant porte une clé dérivée de ce qu'il est, si bien que deux rapports d'un même commit
  peuvent être joints.
* `--verify-kills` relance isolément un échantillon des mises à mort et signale celles qui ne se
  répètent pas, parce qu'un test instable produit une mise à mort que la mutation n'a pas causée.
* La ligne `Ran on` enregistre le runtime, la machine et la version de xUnit, parce que deux
  rapports sans elle ne sont pas comparables et que personne ne s'en aperçoit.

## Le score est honnête, ou il n'est rien

Un mutant que rien ne couvre compte comme **non détecté**. La suite n'aurait pas remarqué le
changement ; sauter son exécution de tests est une optimisation, pas un pardon. L'exclure
signifierait qu'un projet pourrait faire monter son score en ajoutant du code qu'aucun test ne
touche.

Et là où aucun pourcentage honnête n'existe, aucun n'est affiché :

* **Une exécution partielle n'affiche aucun score**
  ([DEC0010](../decisions/0010-a-partial-run-reports-findings-not-a-score-fr.md)). Sa population
  est le diff, choisi contre une base qui diffère à chaque exécution, si bien qu'un pourcentage sur
  elle ne répond à rien qui s'étende sur deux exécutions. Elle affiche des constats et un verdict à
  la place.
* **Une exécution interrompue n'affiche aucun score et ne passe aucune barrière de qualité**
  ([DEC0013](../decisions/0013-a-stopped-run-reports-what-it-judged-and-passes-no-gate-fr.md)). Les
  mutants qu'elle a jugés sont ceux qu'elle a atteints en premier, pas un échantillon.
* **Un score indéfini fait échouer la barrière.** Une exécution qui n'a rien pu tester ne peut pas
  être présentée comme ayant atteint un seuil, et annoncer un succès laisserait un job mal configuré
  au vert pour toujours.
* **Une exécution partielle retient son verdict** quand elle n'a pas pu lire l'état antérieur en
  entier
  ([DEC0012](../decisions/0012-a-partial-run-abstains-when-the-earlier-state-is-incomplete-fr.md)),
  plutôt que de refuser de s'exécuter ou de passer avec un avertissement en prime.

## Les verdicts sont isolés, quoi qu'il en coûte

Chaque mutant a sa propre compilation
([DEC0002](../decisions/0002-one-compilation-per-mutant-fr.md)) et son propre processus de test
neuf
([DEC0008](../decisions/0008-never-reuse-a-test-host-between-mutants-fr.md)). Réutiliser un hôte
chaud d'un mutant à l'autre est l'optimisation la plus tentante qui existe, et celle que l'outil
refuse : c'est la source de la plus ancienne plainte de justesse contre les outils qui le font, où
l'état global au processus fuit entre les mutants et gonfle les scores.

Le coût est un lancement de processus par mutant, et une copie privée de la sortie de test par
worker. Ce que cela achète, c'est la garantie qu'aucun état en mémoire ne peut passer d'un mutant à
un autre — et que KillMutants n'écrit jamais dans votre sortie de build. La garantie porte sur le
processus : un worker garde sa copie de la sortie pour chaque mutant qu'il teste et ne réécrit que
l'assembly testé entre deux mutants, si bien qu'un fichier écrit par un test à côté de son exécutable
peut survivre au mutant qui l'a écrit.

## La référence est vérifiée avant que quoi que ce soit ne soit muté

Le code non muté est émis, injecté et testé par exactement le chemin qu'un mutant emprunte, et la
suite doit être verte. Une compilation qui diffère de la vraie construction de quelque façon — un
fichier généré manquant, un mauvais symbole de préprocesseur — fait échouer les tests pour des
raisons sans rapport avec la mutation, et chaque mutant est alors rapporté tué. Un outil qui dit
toujours *tué* est pire que pas d'outil du tout, parce qu'il rassure en silence. Un projet dont la
référence (baseline) est rouge est mis de côté avec sa raison, et le reste de l'exécution continue
([DEC0022](../decisions/0022-a-run-carries-on-past-what-fails-and-reports-what-it-measured-and-what-it-could-not-fr.md)).

## Une exécution continue au-delà de ce qui échoue, et le dit

Un projet que MSBuild ne peut pas lire, un projet de test qui ne se construit pas, une référence
rouge, une passe de couverture en échec : chacun arrêtait autrefois l'exécution. Désormais chacun
est **mis de côté** avec sa raison, nommé en tête du rapport, et le reste est mesuré comme
d'habitude. Une telle exécution ne passe aucune barrière et sort avec le code `2` — pas `1`, parce
que ce qui a échoué se trouve dans l'environnement ou dans la suite, pas dans les tests, et qu'un
job qui ne sait pas distinguer les deux finira par traiter un environnement cassé comme une
régression de qualité.

Les codes de sortie sont un contrat public : un code n'est jamais renuméroté, et une nouvelle cause
d'un résultat qu'un code nomme déjà le rejoint
([DEC0009](../decisions/0009-exit-codes-are-a-public-contract-fr.md)).

## Le catalogue est sélectif, et le choix vous appartient

Chaque mutant coûte une exécution de tests, un opérateur gagne donc sa place par le signal qu'il
porte. `+x` n'est jamais retiré, parce que le mutant est équivalent par construction ; `?.` et `as`
sont laissés tranquilles, parce que leurs mutants mesurent si un test touche la ligne plutôt que
s'il en vérifie le résultat. [Les mutateurs](./mutators-fr.md#ce-qui-nest-délibérément-pas-muté)
liste chaque omission avec sa raison.

À l'intérieur du catalogue, l'outil mesure et rapporte plutôt que de décider à votre place.
`StringLiteral` produit beaucoup de mutants et en détecte peu sur la plupart des projets ; ce sont
de vrais constats, précieux là où les messages font l'objet d'assertions, si bien que le rapport
montre la répartition par famille et que `--without` agit dessus. `--ignore-calls` va plus fin, et
ce qu'il fait taire est compté à côté du score qu'il a déplacé
([DEC0026](../decisions/0026-a-project-names-the-calls-it-silences-and-the-report-counts-what-it-chose-not-to-hear-fr.md)).
Rien n'est caché au lecteur ; le projet choisit ce qu'il entend, dans un fichier versionné avec son
code.

## Rien n'est détecté à propos du terminal

Ce que la sortie d'erreur standard affiche se choisit avec `--progress`, et `log` — des lignes
datées qui se lisent partout — est le défaut
([DEC0023](../decisions/0023-the-console-progress-is-chosen-by-an-option-and-defaults-to-a-log-that-reads-anywhere-fr.md)).
Un outil qui devine à partir du flux se trompe exactement aux endroits qui comptent : un journal de
CI plein de retours chariot, un terminal qui n'affiche rien parce que la sortie est passée par un
tube. Un choix explicite, c'est une surprise de moins.

## Pour aller plus loin

| Si vous voulez… | Lisez |
| --- | --- |
| lire les décisions elles-mêmes | [Enregistrements de décisions](../decisions/README-fr.md) |
| voir les mesures sur lesquelles les principes reposent | [Architecture](../architecture-fr.md) |
| voir ce que le catalogue laisse tranquille, et pourquoi | [Les mutateurs](./mutators-fr.md#ce-qui-nest-délibérément-pas-muté) |

---

[← Index de la documentation](../README-fr.md)
