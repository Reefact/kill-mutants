# DEC0005 | Vérifier le baseline par le chemin de mutation avant de muter

## Statut

| Date | Statut | Note | Compte rendu lié |
|---|---|---|---|
| 2026-08-31 | Accepté | | |

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

Une exécution des tests de la fixture coûte environ 0,6 s. Les délais par mutant, prévus en M2, ont
besoin d'une durée de référence dont les dériver.

## Décision

Dans ce contexte, nous émettons la compilation non mutée par exactement le même chemin qu'emprunte un
mutant, exécutons les tests dessus avant qu'aucun mutant ne soit envisagé, et interrompons le run
avec un diagnostic — sans rapporter aucun résultat de mutation — lorsque cette exécution n'est pas
verte.

## Justification

Toutes les classes d'infidélité de compilation sont détectées d'un coup, par construction, pour le
prix d'une seule exécution de tests. Le cas reproduit de l'`AssemblyInfo.cs` n'est qu'un membre d'une
famille — référence manquante, source générée oubliée, symbole de préprocesseur erroné, version
d'assembly modifiée — et la vérification n'a pas besoin de savoir auquel elle fait face.

La vérification n'a de sens que parce qu'elle emprunte le chemin de mutation plutôt que la sortie du
build d'origine : même analyse de la ligne de commande, même `CSharpCompilation`, même émission, même
injection dans le répertoire de sortie du projet de test. Vérifier l'assembly d'origine ne prouverait
rien sur notre propre émission.

Interrompre plutôt que rapporter est la seule réponse honnête à un baseline rouge, parce que le mode
de défaillance dont on se protège est précisément celui où les chiffres ont l'air bons. Un score
calculé sur des fondations cassées est pire que pas de score du tout.

## Alternatives envisagées

### Alternative 1 — Vérifier la sortie du build d'origine

* **Description :** exécuter les tests contre l'assembly produit par le build propre du projet, avant
  de muter.
* **Pourquoi écartée :** cela ne prouverait rien sur notre propre émission. L'infidélité dont on se
  protège est introduite par le chemin qu'emprunte KillMutants, que le build d'origine n'exerce
  jamais.

## Conséquences

### Positives

* Toutes les classes d'infidélité de compilation sont détectées d'un coup, par construction, pour le
  prix d'une exécution de tests (~0,6 s).
* Elle établit la durée de référence dont seront dérivés les délais par mutant (M2).
* Un utilisateur dont la suite de tests est déjà rouge en est informé immédiatement, au lieu de
  recevoir un score de mutation calculé sur des fondations cassées.

### Négatives

* Une exécution de tests supplémentaire par projet. Négligeable, et c'est la vérification au meilleur
  rapport valeur/coût de tout l'outil.

### Risques

*Non consigné au moment de la décision.*

### Actions de suivi

*Non consigné au moment de la décision.*
