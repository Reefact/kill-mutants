# Enregistrements de décisions

*🇬🇧 [English version](README.md)*

> Documentation mainteneur. Cette base est la mémoire du **pourquoi** du dépôt ; elle ne fait partie
> d'aucune documentation décrivant **ce que** l'outil fait aujourd'hui.

Les enregistrements sont numérotés `DECNNNN` et vivent dans ce dossier. Ils s'appelaient `ADR-NNNN`
dans `docs/adr` jusqu'au 2026-09-02, date à laquelle la base est passée à l'identifiant de la méthode
de rédaction ; les numéros, eux, n'ont pas changé, si bien que `ADR-0003` et `DEC0003` désignent le
même enregistrement. Les identifiants sont cités depuis `README.md`, `docs/architecture-*`,
`docs/robustness-backlog-*`, `docs/study/*` et depuis des commentaires de code : un numéro, une fois
attribué, est donc un identifiant permanent.

## Ce qu'est un enregistrement

Un enregistrement capture une décision importante ou structurante et permet, plusieurs mois ou
plusieurs années plus tard, de comprendre pourquoi cette décision était la bonne **au moment où elle
a été prise**.

C'est une **mémoire historique, pas une documentation vivante.** Il énonce ce qui était vrai, connu,
anticipé ou décidé ce jour-là. Tout cela peut cesser d'être vrai plus tard sans qu'un mot n'y change.

## Les règles qui mordent vraiment

* **Une décision, un enregistrement.** Quand une discussion s'avère porter plusieurs décisions
  indépendantes, on le dit et on propose la séparation. Les garder ensemble reste l'arbitrage du
  mainteneur.
* **Un enregistrement accepté ne se réécrit jamais.** Une décision qui évolue donne un *nouvel*
  enregistrement ; l'ancien reçoit une ligne de statut indiquant qu'il est remplacé. Son corps est de
  l'histoire.
* **La Décision tient en exactement une phrase** — présent, voix active, autonome. Aucun contexte,
  aucune justification, aucune conséquence, aucune comparaison.
* **Le Contexte contient des faits, la Justification des arguments tirés de ces faits.** Un argument
  qui a besoin d'un fait absent du Contexte n'a pas le droit de l'y glisser : le fait est vérifié
  avec le mainteneur, ajouté au Contexte, et seulement ensuite utilisé.
* **On n'invente rien.** Une hypothèse, une alternative, une conséquence ou un risque soulevé pendant
  la réflexion ne devient un élément de l'enregistrement qu'une fois validé explicitement par le
  mainteneur — dates, statuts et liens compris. Là où un enregistrement historique ne dit rien, la
  section le dit plutôt que de combler le vide.
* **Un enregistrement n'est pas une spécification.** Aucune configuration, aucun inventaire, aucune
  procédure ni aucun état courant qu'il faudrait maintenir. Le filtre pour chaque phrase : *cette
  information aide-t-elle à comprendre pourquoi la décision a été prise à ce moment-là ?* Sinon, elle
  relève d'un autre document.

**Une exception, bornée ici plutôt que laissée implicite.** Le 2026-09-02, les dix enregistrements
existants — d'`ADR-0001` à `ADR-0010`, tous acceptés — ont été migrés vers ce format et renumérotés
d'`ADR-NNNN` en `DECNNNN`. L'exception, c'est cette migration entière, et rien en dehors d'elle.

L'essentiel relevait de la présentation : le texte a été déplacé d'une section à l'autre, les sections
sans matière consignée le disent plutôt que d'être comblées, et aucune ligne de statut n'a été ajoutée
puisque aucune décision n'a changé d'état. Trois enregistrements se lisent différemment, et chacun de
ces changements est la migration elle-même. Le DEC0001 nomme le dossier et l'identifiant qui ont été
renommés. Le DEC0009 a été réécrit dans la phrase unique qu'impose le format, son tableau de codes de
sortie étant une documentation utilisateur qui vit déjà dans le `README.md` du dépôt. Et l'`ADR-0010`
a été **découpé en deux enregistrements** — le DEC0010 pour ce qu'une exécution partielle affiche, le
DEC0011 pour ce qu'elle sélectionne — parce qu'il portait deux décisions indépendamment réversibles,
dont les alternatives appartiennent à l'une ou à l'autre, jamais aux deux.

Ce découpage est structurel et non cosmétique, et c'est la raison pour laquelle cette exception est
écrite comme une frontière et non comme un principe. Il ne tient pas à l'âge du record : il n'existe
aucune période de grâce pendant laquelle un enregistrement accepté resterait réécrivable, et en
déduire une de cette migration serait la lire à l'envers. **À partir du merge de cette migration, un
enregistrement accepté n'est plus jamais réécrit** — une décision qui évolue donne un nouvel
enregistrement, et l'ancien reçoit une ligne de statut.

## D'où vient le format

La méthode — les deux modes de collaboration, les boucles de construction, le format obligatoire et
le contrôle de cohérence final — est
[`Reefact/guidelines` → `important-decision-record-guideline.md`](https://github.com/Reefact/guidelines/blob/801615b78569eba80bf577a801d02a954819cbdc/important-decision-record-guideline.md),
au commit `801615b` (2026-09-01).

**Ce dépôt est privé** : un contributeur ou un agent travaillant dans celui-ci ne peut pas l'ouvrir,
et un pointeur vers un document que son lecteur ne peut pas lire n'est pas une instruction. Le
guideline est donc restitué à l'intérieur de ce dépôt, sous forme de la skill `decision-record` dans
[`.claude/skills/decision-record/`](../../.claude/skills/decision-record/SKILL.md). Cette restitution
est un mécanisme de livraison, jamais une seconde source de vérité : en cas de désaccord sur la
méthode, c'est le guideline qui a raison et la skill qui est en défaut. Le guideline dit comment une
décision se raisonne et s'écrit ; quelles décisions méritent ici un enregistrement relève du DEC0001,
et les deux ne se concurrencent pas.

## Conventions de fichiers

* Une décision par fichier, nommé `NNNN-resume-en-kebab-case-en.md`, avec un jumeau français
  `NNNN-resume-en-kebab-case-fr.md`. **Le fichier anglais est canonique** ; le français est une
  traduction qui change avec lui, portant le même numéro, le même historique de statut et le même
  contenu.
* Le titre exprime la **décision**, jamais la question ni le problème.
* **Aucune section n'est ajoutée au format, et aucune n'en est retirée.** Chaque enregistrement porte
  exactement Statut, Contexte, Décision, Justification, Alternatives envisagées et Conséquences, et
  les Conséquences portent exactement Positives, Négatives, Risques et Actions de suivi.
* **Le Statut est un historique append-only** — une ligne par état réellement atteint par la
  décision, et aucune ligne existante n'est jamais modifiée ni supprimée. Statuts en usage :
  *Proposé*, *Accepté*, *Rejeté*, *Déprécié*, *Remplacé par DECNNNN*.
* Un remplacement s'écrit des deux côtés : l'enregistrement remplacé reçoit une ligne de statut
  nommant son successeur et rien d'autre n'y change ; le successeur nomme ce qu'il remplace dans son
  propre Contexte.
* Les enregistrements sont cités par leur identifiant depuis le reste de la documentation et depuis
  des commentaires de code. En renuméroter un casse ces citations silencieusement : les numéros sont
  donc des identifiants permanents.

## Qui propose, qui accepte

Un agent — ou quiconque prépare un enregistrement — **rédige et propose**. Il n'accepte jamais, ne
rejette jamais, ne déprécie ni ne remplace un enregistrement, et n'ajoute jamais une ligne de statut
de sa propre autorité. C'est l'arbitrage du mainteneur, et c'est délibérément la même frontière que
celle qui empêche un agent de merger une pull request.

## Index

| DEC | Titre | Statut |
|---|---|---|
| [DEC0001](0001-record-architecture-decisions-fr.md) | Consigner les décisions d'architecture | Accepté |
| [DEC0002](0002-one-compilation-per-mutant-fr.md) | Une compilation par mutant | Accepté |
| [DEC0003](0003-compilation-inputs-from-csc-command-line-fr.md) | Prendre les entrées de compilation dans la ligne de commande `csc` de MSBuild | Accepté |
| [DEC0004](0004-run-tests-by-launching-the-test-executable-fr.md) | Exécuter les tests en lançant l'exécutable du projet de test | Accepté |
| [DEC0005](0005-verify-the-baseline-before-mutating-fr.md) | Vérifier le baseline par le chemin de mutation avant de muter | Accepté |
| [DEC0006](0006-identify-tests-by-name-not-by-unique-id-fr.md) | Identifier les tests par leur nom, pas par leur identifiant unique | Accepté |
| [DEC0007](0007-measure-coverage-with-a-type-preserving-probe-fr.md) | Mesurer la couverture avec une sonde qui préserve le type, un test à la fois | Accepté |
| [DEC0008](0008-never-reuse-a-test-host-between-mutants-fr.md) | Ne jamais réutiliser un hôte de test d'un mutant à l'autre | Accepté |
| [DEC0009](0009-exit-codes-are-a-public-contract-fr.md) | Les codes de sortie sont un contrat public | Accepté |
| [DEC0010](0010-a-partial-run-reports-findings-not-a-score-fr.md) | Une exécution partielle rapporte des constats, pas un score | Accepté |
| [DEC0011](0011-widen-a-partial-run-selection-when-a-test-file-changes-fr.md) | Élargir la sélection d'une exécution partielle quand un fichier de test change | Accepté |
