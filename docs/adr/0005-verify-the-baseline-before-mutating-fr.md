# ADR-0005 — Vérifier le baseline par le chemin de mutation avant de muter

**Statut :** accepté · **Date :** 2026-08-31

## Contexte

Le mode de défaillance le plus dangereux d'un outil de mutation testing est le **faux positif** :
l'assembly muté échoue aux tests pour une raison sans rapport avec la mutation. Une référence de
métadonnées manquante, un fichier source généré oublié, un symbole de préprocesseur erroné ou une
version d'assembly modifiée produisent tous des échecs de test qui ressemblent exactement à un mutant
tué.

Ce n'est pas hypothétique. Le cas a été reproduit pendant la phase de recherche de ce projet :
retirer l'`AssemblyInfo.cs` généré de la compilation reconstruite a mis la version de l'assembly à
`0.0.0.0`, l'hôte de test a alors échoué à le charger, et la `FileNotFoundException` résultante s'est
manifestée comme un échec de test ordinaire — rapporté comme un mutant tué.

Un outil dans cet état affiche un score de mutation élevé et ne vaut silencieusement rien. Pire, il
est *rassurant* : rien n'a l'air anormal.

## Décision

Avant qu'aucun mutant ne soit envisagé, KillMutants **émet la compilation non mutée par exactement le
même chemin qu'emprunte un mutant** — même analyse de la ligne de commande, même
`CSharpCompilation`, même émission, même injection dans le répertoire de sortie du projet de test —
puis exécute les tests.

Ce run doit être vert. Sinon, l'exécution s'interrompt avec un diagnostic indiquant l'échec du
baseline, et aucun résultat de mutation n'est rapporté.

## Conséquences

- Toutes les classes d'infidélité de compilation sont détectées d'un coup, par construction, pour le
  prix d'une exécution de tests (~0,6 s).
- La vérification n'a de sens que parce qu'elle emprunte le chemin de mutation plutôt que la sortie
  du build d'origine. Vérifier l'assembly d'origine ne prouverait rien sur notre propre émission.
- Elle établit également la durée de référence dont seront dérivés les délais par mutant (M2).
- Un utilisateur dont la suite de tests est déjà rouge en est informé immédiatement, au lieu de
  recevoir un score de mutation calculé sur des fondations cassées.
- Une exécution de tests supplémentaire par projet. Négligeable, et c'est la vérification au meilleur
  rapport valeur/coût de tout l'outil.
