# DECNNNN | {La décision, pas la question}

<!-- Copier ce fichier vers NNNN-resume-en-kebab-case-fr.md et son jumeau vers
     NNNN-resume-en-kebab-case-en.md, puis supprimer chaque commentaire au fur et à mesure du
     remplissage.

     AUCUNE SECTION N'EST AJOUTÉE À CE FORMAT, ET AUCUNE N'EN EST RETIRÉE.

     Le titre exprime la DÉCISION — « Adoption de PostgreSQL pour la facturation », jamais « Quelle
     base de données choisir ? ».

     Rien ici n'est inventé : chaque date, statut, fait, alternative, conséquence et lien a été validé
     explicitement par le mainteneur. Là où il n'y a réellement rien à consigner, le dire plutôt que
     de combler le vide.

     Voir README-fr.md dans ce dossier, et la skill `decision-record` pour la méthode. -->

## Statut

<!-- Historique append-only : une ligne par état réellement atteint par la décision. Ne jamais
     modifier ni supprimer une ligne existante. Statuts : Proposé, Accepté, Rejeté, Déprécié,
     Remplacé par DECNNNN. Laisser « Compte rendu lié » vide quand il n'y en a pas — ne jamais en
     inventer un. -->

| Date | Statut | Note | Compte rendu lié |
|---|---|---|---|
| AAAA-MM-JJ | Proposé | Première rédaction | |

## Contexte

<!-- EXCLUSIVEMENT DES FAITS, de toute nature pertinente — technique, humaine, sociale, politique,
     organisationnelle, stratégique, produit, économique, réglementaire, contractuelle, fournisseur,
     sécurité, gouvernance. Ils constituent l'espace de décision et ne défendent PAS encore la
     solution retenue.

     Une contrainte imposée de l'extérieur est un fait. Le fait qu'une équipe apprécie ou rejette
     fortement une pratique en est un aussi, dès lors qu'il a réellement influencé la décision.

     Cette section peut décrire une situation qui ne sera plus vraie plus tard : elle représente la
     réalité pertinente AU MOMENT DE LA DÉCISION. Elle ne doit jamais devenir une spécification ni une
     photographie technique à maintenir à jour.

     C'est généralement la section la plus développée. Tout ce dont la Justification argumente doit
     d'abord être énoncé ici. -->

## Décision

<!-- EXACTEMENT UNE PHRASE. Présent, voix active, autonome, immédiatement compréhensible. Aucun
     contexte, aucune justification, aucune conséquence, aucune comparaison.

     Forme possible : « Dans ce contexte, nous décidons de <…>. » -->

## Justification

<!-- EXCLUSIVEMENT DES ARGUMENTS — pourquoi la décision est adaptée au contexte. Chaque argument
     important se rattache à un ou plusieurs faits énoncés dans le Contexte.

     Elle n'introduit jamais subrepticement de nouvelle information factuelle. Si la rédaction d'un
     argument révèle un fait manquant, vérifier ce fait avec le mainteneur, l'ajouter au Contexte, et
     seulement ensuite l'utiliser ici. -->

## Alternatives envisagées

<!-- Options réellement envisagées et validées, crédibles dans ce contexte — pas une énumération
     exhaustive de tout ce qui est théoriquement possible. Lorsqu'une situation existe déjà, le statu
     quo est envisagé dès qu'il constitue une vraie option. Chaque alternative porte une raison de
     rejet explicite. -->

### Alternative 1 — {nom}

* **Description :** <ce qu'aurait impliqué cette option>
* **Pourquoi écartée :** <pourquoi elle était moins adaptée au contexte>

### Alternative 2 — {nom}

* **Description :**
* **Pourquoi écartée :**

## Conséquences

### Positives

<!-- Ce que la décision améliore, simplifie, rend possible ou débloque. -->

### Négatives

<!-- Coûts, contraintes et inconvénients suffisamment certains : le prix que l'on sait devoir
     assumer. -->

### Risques

<!-- Événements ou situations défavorables possibles mais incertains — jamais un coût certain. Courts,
     simples, assez précis pour être compris ; aucun formalisme probabilité/impact. -->

### Actions de suivi

<!-- Actions rendues nécessaires ou souhaitables par la décision : migration, communication,
     formation, documentation, expérimentation, mesure, revue ultérieure, accompagnement. -->
