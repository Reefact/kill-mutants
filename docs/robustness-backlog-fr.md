# Backlog de robustesse — cas limites hérités de Stryker.NET

Stryker.NET est sur le terrain depuis des années, et ses cicatrices sont de la connaissance. Ce
fichier est la manière dont nous héritons de cette connaissance **par les spécifications et les
tests** plutôt que par l'architecture.

La méthode, pour chaque complexité de Stryker qui paraît étrange, excessivement défensive ou
historique :

1. chercher les tests qui couvrent le comportement ;
2. chercher les issues GitHub associées ;
3. chercher, lorsque cela apporte de l'information, la PR ou le commit qui l'a introduit ou corrigé ;
4. comprendre le bug, le cas limite ou la contrainte qui l'a motivé ;
5. seulement ensuite, déterminer si cette contrainte existe encore pour C#, .NET moderne et xUnit 4.

Une complexité n'est pas écartée parce qu'elle paraît excessive. Elle n'est écartée qu'une fois que
l'on sait nommer ce contre quoi elle protégeait et démontrer que la menace a disparu. Quand la menace
subsiste, nous la reproduisons sous forme de test de non-régression qui nous est propre, même si
notre implémentation du mécanisme est entièrement différente.

**Vocabulaire des statuts.** `COUVERT` — un test KillMutants échoue si le comportement régresse.
`OUVERT` — compris, reproduit, pas encore traité. `ASSUMÉ` — compris et délibérément non traité, avec
la raison consignée.

---

## RB-001 — Une mutation doit changer le kind du nœud, pas seulement le token · COUVERT

**Ce que fait Stryker.** `BinaryExpressionMutator` construit toujours un nœud neuf via
`SyntaxFactory.BinaryExpression(kind, …)` et ne rattache qu'ensuite la trivia du token d'origine
(`Mutators/BinaryExpressionMutator.cs:60-62`). Leur note de conception en donne la raison sans
détour : *« Changing the token changes the text representation, but the compiled version will retain
the original operator! »* (`docs/technical-reference/Mutation Orchestration Design.md:33`).

**Pourquoi cela existe.** Roslyn lie et émet à partir du kind du nœud. Ne remplacer que le token
produit un arbre qui *s'affiche* `age > 18` mais émet l'IL de `age >= 18`.

**Est-ce toujours d'actualité ?** Entièrement. C'est une propriété de Roslyn, pas une contrainte
héritée. Reproduit indépendamment lors de la conception de ce projet, au niveau IL : la variante par
token émettait `clt`, identique à l'original, là où le remplacement de nœud émettait `cgt`.

**Pourquoi c'est le pire mode de défaillance connu.** Le mutant compile, le rapport paraît juste, les
tests passent, et il est consigné **Survived** — une lacune inventée dans la suite de tests de
l'utilisateur. La vérification du baseline (ADR-0005) ne peut pas l'attraper, puisqu'elle protège
contre les faux *positifs*.

**Ce que la garantie dit réellement.** Non pas « la syntaxe mutée diffère » — c'est précisément ce
qu'une réécriture du seul jeton réussit — mais *le programme émis diffère*. La vérification compare
l'assembly émis dans son entier plutôt que les seuls corps de méthode : changer un littéral de chaîne
peut laisser l'IL identique octet pour octet (`ldstr` garde son index de tas) alors que le programme
diffère manifestement ; comparer le fichier couvre aussi les tas de métadonnées.

**Ce dont cette comparaison dépend, et comment elle a un temps échoué.**
`CSharpCompilationOptions` met `Deterministic` à `false` par défaut, et le corpus construisait ses
extraits sans le régler. Mesuré sur Roslyn 5.9 : deux émissions d'un programme *identique* diffèrent
alors, car l'identifiant de version de module et l'horodatage de l'en-tête sont générés à neuf à
chaque fois. La comparaison signalait donc tout mutant comme différent — y compris un qui ne changeait
rien — et la garantie passait sans rien démontrer. Le vrai pipeline n'a jamais été concerné : la ligne
de commande du compilateur que rapporte MSBuild porte `/deterministic+`. Avec le déterminisme, le
fichier est fonction du seul programme ; mesuré : un même programme émet des assemblies identiques
octet pour octet à travers un reformatage, un commentaire ajouté et un chemin de fichier différent, et
aucun flux de débogage n'est émis, donc rien ne transporte de positions source.

**Nos tests.** Chaque famille du catalogue porte un test
`Every_replacement_carries_the_kind_it_prints` — `ComparisonOperatorMutatorTests`,
`LogicalOperatorMutatorTests`, `BooleanLiteralMutatorTests` — et le garde-fou comportemental se
trouve dans
`ProjectCompilationTests.A_mutant_emits_an_assembly_that_actually_differs_from_the_baseline` et dans
`CatalogueCorpusTests.Every_proposed_mutant_changes_the_emitted_program` sur tout le corpus. La règle
est énoncée dans le contrat `IMutator` pour que chaque nouveau mutateur en hérite, et imposée
structurellement par `BinaryOperatorMutator`, qui construit les remplacements par kind pour toute la
famille. **Tout mutateur ajouté au catalogue doit être couvert par une assertion équivalente.**

**Et le garde-fou du garde-fou.** Une vérification qui ne voit jamais passer que de vrais mutants
n'est pas une preuve : deux tests la soutiennent donc.
`The_same_compilation_emits_the_same_bytes_twice` vérifie la précondition au lieu de la supposer.
`A_token_only_rewrite_is_rejected_although_the_syntax_shows_a_mutation` construit délibérément
l'erreur exacte de cette entrée — un jeton d'opérateur échangé sur un nœud qui garde son kind
d'origine —, vérifie que la syntaxe affiche bien `>` là où il y avait `>=`, et exige de la
vérification qu'elle réponde que rien n'a changé. Retirer `WithDeterministic(true)` fait échouer ces
deux tests, et seulement eux : la garantie du corpus, elle, passe toujours — c'est exactement ainsi
que le trou est resté invisible.

---

## RB-002 — Les générateurs de source produisent du code absent de la liste des sources · COUVERT

**Ce que fait Stryker.** Exécute le driver de génération, et le réexécute après chaque
`ReplaceSyntaxTree` (`Compiling/CsharpCompilingProcess.cs:360-364`). Il détecte aussi les projets
épinglant un compilateur plus récent que le sien et le signale nommément (`ReferencesNewerCompiler`,
`Buildalyzer/IAnalyzerResultExtensions.cs:115-120`).

**Pourquoi cela existe.** `CscCommandLineArgs` liste les générateurs sous `/analyzer:` mais **pas**
leur sortie parmi les sources — le compilateur la produit pendant le build.

**Est-ce toujours d'actualité ?** Oui, et plus qu'avant : `[GeneratedRegex]`, `[JsonSerializable]`,
`[LibraryImport]`, Mapperly, Refit et les minimal APIs ASP.NET Core reposent tous sur des
générateurs. Reproduit : un projet avec une propriété partielle `[GeneratedRegex]` échouait au
baseline sur `CS9248` alors qu'il compilait parfaitement sous `dotnet build`. Même la ligne de
commande de notre fixture triviale porte huit assemblys `/analyzer:` ; le projet à générateur en
porte sept réels.

**Notre comportement.** `SourceGenerators` charge chaque générateur nommé sur la ligne de commande et
l'exécute via `CSharpGeneratorDriver`, avec les vrais fichiers `.editorconfig` / `.globalconfig`
fournis au compilateur et les `AdditionalFiles` du projet, afin qu'un générateur lisant des
propriétés MSBuild voie les mêmes valeurs que lors d'un vrai build.

**Régénéré par mutant, et mesuré plutôt que supposé.** La sortie d'un générateur peut dépendre du
code muté : le driver est donc réexécuté pour chaque mutant au lieu de réutiliser sa sortie. Mesuré
sur le projet à sept générateurs : la première exécution coûte environ une seconde, chaque suivante
**1,4 ms**, contre 60 ms pour l'émission et environ 600 ms pour exécuter les tests. La correction est
ici quasiment gratuite, donc aucune approximation n'était nécessaire. On aboutit au même comportement
que Stryker, mais pour une raison vérifiée sur cette plateforme plutôt qu'héritée.

**Le code généré est compilé, jamais muté.** Les arbres générés doivent être dans la compilation que
lisent les mutateurs — un modèle sémantique qui ne voit pas les types générés répond faux à la
question de liaison — mais ils sont exclus de la mutation par leur chemin. Verrouillé par un test
vérifiant que chaque mutant provient du fichier écrit à la main, puisque le moteur de regex généré
regorge de comparaisons et d'arithmétique que personne n'a écrites et que personne ne peut corriger.

**Un analyseur impossible à charger est nommé.** Généralement un projet épinglant un Roslyn plus
récent que celui de KillMutants, qui ne contribue alors silencieusement rien. Ces assemblys sont
consignés et signalés avec notre version de Roslyn, plutôt que de ressortir en « KillMutants n'a pas
pu compiler votre projet ».

**Nos tests.**
`MutationTestingEndToEndTests.A_project_that_depends_on_a_source_generator_is_mutated_and_tested`,
qui échoue sur `CS9248` si le driver cesse de s'exécuter.

---

## RB-003 — Un hôte de test qui plante doit coûter un mutant, pas le run · COUVERT

**Ce que fait Stryker.** Maintient une fixture dédiée à cela
(`integrationtest/…/StrykerFeatures/StackOverflow.cs`), une branche spécifique dans
`Mutants/Mutant.cs:45-51`, et un indicateur `IsAlive` pour « initialisé mais le processus a disparu »
(`AssemblyTestServer.cs:44-51`).

**Pourquoi cela existe.** Une mutation peut retirer le cas de base d'une récursion. La
`StackOverflowException` qui en résulte ne peut pas être interceptée et tue le processus avant
qu'aucun résultat ne soit écrit.

**Est-ce toujours d'actualité ?** Oui. C'est une propriété du CLR, pas de VSTest.

**Notre comportement.** Le runner renvoie `TestRunOutcome.FromCrash` au lieu de lever une exception.
La session décide ensuite selon le contexte : pendant la vérification du baseline, un plantage
interrompt le run avec un message clair, car plus rien en aval ne serait fiable ; pour un mutant, il
est consigné `Killed`, puisque le baseline a déjà prouvé que l'hôte tourne proprement sans mutation —
le plantage est donc imputable à la mutation.

**Nos tests.** `XUnitTestRunnerTests.A_host_that_writes_no_result_file_is_reported_rather_than_thrown`,
`TestRunOutcomeTests.A_crashed_run_is_neither_a_pass_nor_an_empty_run`.

---

## RB-004 — Les avertissements-en-erreurs doivent être entièrement neutralisés · COUVERT

**Pourquoi cela compte.** Une mutation rend fréquemment du code inatteignable (CS0162) ou une
variable inutilisée (CS0219). Si cela fait toujours échouer la compilation, le mutant est consigné
`CompileError` et retiré du dénominateur, ce qui sous-évalue silencieusement le score pour une raison
étrangère aux tests.

**Le piège.** `WithGeneralDiagnosticOption(Default)` efface `/warnaserror+` mais laisse
`SpecificDiagnosticOptions` intact. Vérifié sur Roslyn 5.9 : après cet appel,
`/warnaserror+:CS0162,CS0219` associe toujours les deux à `Error`.
`<WarningsAsErrors>nullable</WarningsAsErrors>` est courant dans les vrais projets, et la ligne de
commande de notre propre fixture porte `/warnaserror+:NU1605,SYSLIB0011`.

**Notre comportement.** Toute entrée valant `Error` est rétrogradée en `Warn`. Les suppressions
issues de `/nowarn:` sont préservées — l'utilisateur les a silencées délibérément, et le respecter ne
peut pas nous coûter un mutant.

**Nos tests.** `WarningsAsErrorsTests`.

---

## RB-005 — Les constantes de compilation ne peuvent pas être mutées de façon observable · COUVERT

**Ce que fait Stryker.** Maintient une liste de constructions à ne pas muter couvrant `const`, les
arguments d'attribut et les membres d'énumération.

**Pourquoi cela existe.** C# copie ces valeurs dans chaque *site d'appel* au moment où le
consommateur est compilé.

**Est-ce toujours d'actualité ?** Oui — c'est une règle du langage. Vérifié : muter
`const Limit = 18` en `99` puis remplacer l'assembly laisse un consommateur déjà compilé lire toujours
`18`. Une nuance mérite d'être consignée : le code *de la bibliothèque elle-même* observe bien la
nouvelle valeur ; c'est spécifiquement le consommateur — donc le projet de test — qui ne la voit pas.

**Impact.** Un tel mutant survivra à coup sûr, quelle que soit la qualité des tests. Le générer
fabriquerait une lacune sur laquelle l'utilisateur ne peut pas agir et abaisserait le score sans
raison.

**Notre comportement.** `MutationSite.IsObservable` exclut les champs et variables locales `const`,
les valeurs de paramètres par défaut, les arguments d'attribut et les membres d'énumération. Ils sont
ignorés, pas rapportés.

**Nos tests.** `MutationSiteTests`.

---

## RB-006 — L'injection ne résiste pas à un arrêt brutal · COUVERT

**Pourquoi cela existe.** Un run tué par SIGKILL ou par un job CI annulé ne peut pas faire son
ménage : il laisse un assembly muté dans le répertoire de sortie du développeur. Stryker souffre de
la même plaie et se contente de la journaliser
(`ProjectComponents/TestProjects/TestProjectsInfo.cs:51-58`).

**Comment nous l'avions d'abord corrigé, et pourquoi cela a changé.** Prendre en charge un assembly
restaurait d'abord toute sauvegarde abandonnée. Cela fonctionnait, mais c'était une règle à retenir.

La parallélisation l'a rendue inutile. Chaque worker travaille désormais depuis une copie privée du
répertoire de sortie des tests : **KillMutants n'écrit plus du tout dans la sortie de build du
développeur**. Un run qui meurt en cours de route ne laisse qu'un répertoire temporaire. Le mode de
défaillance disparaît par construction plutôt que par nettoyage, ce qui est la meilleure sorte de
correction : il n'y a plus de règle à oublier.

---

## RB-007 — Un projet multi-cible produit une ligne de commande par framework · COUVERT

**Pourquoi cela existe.** Interroger MSBuild sur un projet visant plusieurs frameworks sans préciser
lequel donne une réponse pour un framework indéterminé. Des mutants pourraient alors être émis contre
un framework que le projet de test ne charge jamais.

**Notre comportement.** Un projet à muter est toujours résolu contre le framework du projet de test
qui l'atteint, épinglé explicitement sur les deux requêtes MSBuild. Un *projet de test* qui en vise
plusieurs est refusé avec un message les nommant, plutôt que d'en choisir un en silence et de
rapporter un score pour un framework que l'utilisateur n'a pas choisi — chacun demanderait son propre
run, sa propre sortie et son propre verdict.

**Cette entrée a été fausse pendant un temps, et cela mérite d'être consigné.** « Épinglé
explicitement sur la requête MSBuild » était vrai de la requête sur les *faits* d'un projet et faux
de la requête sur sa *ligne de commande de compilation*, qui ne nommait aucun framework. Une revue
automatique de la pull request d'ouverture l'a trouvé. La conséquence n'est pas celle que cette
entrée prévoyait : une compilation externe ne répond pas pour un framework indéterminé, elle répond
une liste vide et sort en code zéro — mesuré contre le SDK .NET 10 — si bien que
`CscCommandLine.Parse` la refusait et que l'exécution s'arrêtait en accusant une compilation qui
avait réussi. Une bibliothèque visant deux frameworks était tout simplement inmutable.

La leçon porte sur ce document plutôt que sur MSBuild. COUVERT avait été écrit d'après la conception
et non d'après un test, et rien ici n'exerçait un projet multi-cible avant que
`tests/fixtures/multitarget` n'existe. Une entrée qui affirme qu'un défaut est traité doit nommer ce
qui le retient.

**Nos tests.** `MultiTargetedProjectTests.A_library_built_for_several_frameworks_is_mutated_for_the_one_its_tests_load`,
contre une bibliothèque construite pour `netstandard2.0;net10.0` et un projet de test qui en charge
un seul.

---

## RB-008 — L'exclusion des fichiers générés repose uniquement sur le chemin · COUVERT

**Pourquoi cela compte.** Muter du code généré produit des constats sur lesquels le développeur ne
peut pas agir : la réponse à « ce mutant a survécu » est de modifier un gabarit, un schéma ou le
générateur de quelqu'un d'autre. Cela enfouit aussi les vrais constats sous des centaines d'autres.

**Ce qui manquait.** La règle était `.g.cs`, `.g.i.cs` et tout ce qui se trouve sous `obj/` — des noms
et des répertoires, qui n'attrapent que ce à quoi nous avons pensé. Un projet avec un
`BaseIntermediateOutputPath` personnalisé place ses intermédiaires ailleurs ; T4 écrit sa sortie à
côté du gabarit ; protobuf et les concepteurs resx ont leurs propres conventions ; et le prochain
générateur nommera sa sortie comme bon lui semble.

**Ce que fait Stryker.NET.** `GeneratedCodeFilterExtension` (`MutantFilters/`) reconnaît
`*.designer.cs` par le nom et, surtout, un marqueur `<auto-generated` ou `<autogenerated` dans le
commentaire de tête du fichier — une règle que son en-tête attribue à StyleCopAnalyzers. Ni leur règle
ni la nôtre n'était un sur-ensemble de l'autre : ils avaient l'en-tête et `.designer.cs`, nous avions
`.g.cs` et `obj/`.

**La règle maintenant.** L'union, implémentée à partir de la convention et non de leur code.
L'en-tête est ce qui clôt l'entrée, car il voyage *avec* le fichier au lieu de décrire où il se
trouve — c'est précisément la raison d'être de la convention : le compilateur C# la lit pour taire les
avertissements d'analyseurs, et T4, protobuf, les concepteurs, XSD et EF l'émettent tous. Elle n'est
honorée qu'en tout début de fichier, avant tout code, comme la convention le spécifie ; un
commentaire plus bas est un commentaire.

**Nos tests.** La suite `SourceFileTests`, en particulier
`A_file_that_declares_itself_generated_is_recognised_wherever_it_lives` — un nom ordinaire, hors
`obj`, reconnu sur son seul en-tête — et `The_header_only_counts_at_the_top_of_the_file`.

---

## RB-009 — Conserver les artefacts de tous les mutants ne passera pas à l'échelle · ASSUMÉ

Consigné comme une inquiétude, puis mesuré. L'inquiétude se trompait sur ce qui est réellement
conservé, et la mesure le dit sans détour.

**Ce qui était supposé.** « Les arbres syntaxiques mutés, les tableaux d'octets émis et les
diagnostics par mutant sont tous conservés pendant tout le run. » Les assemblys émis ne le sont pas :
`EmitWith` les retourne dans une variable locale, le sandbox les écrit sur disque, et rien n'en garde
la référence. Ce qu'un mutant terminé conserve réellement, ce sont ses deux nœuds syntaxiques — dont
les nœuds verts sont partagés avec l'arbre que la compilation détient déjà —, un statut, et une chaîne
de diagnostics uniquement s'il n'a pas compilé.

**La mesure.** Empreinte mémoire résidente échantillonnée toutes les deux secondes sur un run complet
de `KillMutants.Core`, 384 mutants sur quatre cœurs :

| Moment du run | RSS |
|---|---|
| Démarrage | 39 Mo |
| Après le build des projets de test | 41 Mo |
| Après lecture des lignes de commande du compilateur | 47 Mo |
| Après construction de la compilation Roslyn et exécution des générateurs | 210 Mo |
| Début de la phase des mutants | 321 Mo |
| Fin de la phase des mutants | 399 Mo (pic 402 Mo) |

C'est la forme de la courbe qui tranche. Dans la phase des mutants, la RSS atteint 399 Mo au bout
d'une centaine de secondes puis reste entre 396 et 402 Mo pendant les 255 secondes restantes — environ
trois cents mutants de plus sans coût additionnel. Une rétention linéaire en nombre de mutants aurait
grimpé tout du long ; ce n'est pas le cas. La hausse qui se produit est le tas atteignant sa taille de
travail face aux tampons d'émission transitoires, dont l'essentiel atterrit sur le tas des grands
objets et est ensuite réutilisé.

**Pourquoi c'est assumé plutôt que corrigé.** Le coût fixe, c'est Roslyn : 280 Mo sur les 402 sont la
compilation, ses modèles sémantiques et sept générateurs de source, et ils sont là avant le premier
mutant. Réduire l'état par mutant ne déplacerait pas un chiffre déjà plat. Si une solution bien plus
grande montre un jour un profil croissant plutôt que plat, la mesure à refaire est celle-ci, et la
première chose à regarder est la compilation, pas les mutants.

---

## RB-010 — Une mutation peut transformer une boucle qui termine en boucle infinie · COUVERT

**Pourquoi cela existe.** Stryker dérive un délai maximal de l'exécution de référence et tue l'hôte
de test à l'expiration, pour une seule raison : une mutation peut empêcher une boucle de finir. Aucun
mutateur dédié n'est nécessaire pour l'atteindre. La famille arithmétique le fait déjà — réécrire
`value = value + 1` en `value - 1` rend une condition `while (value <= limit)` définitivement vraie.

**Notre comportement.** `TimeoutPolicy` dérive le budget en `baseline × facteur + marge`, par défaut
trois fois le baseline plus trente secondes. Ce défaut est délibérément généreux : un mutant
faussement rapporté en dépassement masque une vraie lacune des tests, ce qui est pire qu'attendre.
`ProcessRunner` tue tout l'arbre de processus à l'expiration, et le mutant est consigné `Timeout` —
compté comme une détection dans le score, puisqu'une mutation qui bloque la suite a bien changé le
comportement observable.

**Le piège, rencontré de plein fouet en écrivant le test.** La première fixture utilisait des
compteurs `int` et le mutant était rapporté *tué*, pas en dépassement. Le compteur décrémenté atteint
`int.MinValue`, repasse à `int.MaxValue`, et la condition devient fausse — la boucle finit donc après
environ deux milliards d'itérations, en une quinzaine de secondes. Élargir les compteurs en `long`
repousse le débordement à neuf trillions d'itérations. La leçon dépasse la fixture : **beaucoup de
mutants qui ressemblent à des boucles infinies ne sont que des boucles très lentes**, ce qui plaide
pour un budget conçu comme une échéance plutôt que comme une détection de non-terminaison.

**Nos tests.** `MutationTestingEndToEndTests.A_mutation_that_never_terminates_is_recorded_as_timed_out`
exécute le vrai outil sur un vrai projet et vérifie que le mutant arithmétique dépasse le délai
pendant que les trois autres sont tués.
`ProcessRunnerTests.A_process_that_never_finishes_is_killed_and_reported_as_timed_out` verrouille la
mise à mort elle-même, et `TimeoutPolicyTests` l'arithmétique du budget.

---

## RB-011 — Un mutant qui ne compile pas est un coût sans signal · COUVERT

**Pourquoi cela compte.** `"a" + "b"` est une concaténation ; `"a" - "b"` n'existe pas. Un mutateur
arithmétique qui la réécrirait produirait un mutant qui échoue à l'émission — résultat correct, mais
inutile, qui coûte de l'analyse et encombre le rapport.

**La réponse générale, plutôt qu'une liste.** Chaque mutateur binaire demande au compilateur si le
remplacement se lierait, via `GetSpeculativeTypeInfo`. Une seule règle rejette la concaténation de
chaînes, les types définissant un seul opérateur d'une paire, et tous les cas auxquels personne n'a
pensé — tout en laissant passer les délégués, où `+` et `-` existent tous deux.

**Le piège dans le piège.** Le test doit porter sur le **type** résultant, pas sur le symbole.
Vérifié sur Roslyn 5.9 : `a && b` réécrit en `a || b` se lie à un symbole *nul* — les opérateurs
conditionnels sur `bool` n'ont pas de méthode d'opérateur — tout en donnant le type `bool`. Une
vérification par symbole compile, passe une relecture rapide, et écarte silencieusement tous les
mutants logiques. Ce sont nos propres tests de famille qui l'ont attrapé.

**Nos tests.** `ArithmeticOperatorMutatorTests.String_concatenation_is_not_mutated`,
`A_type_that_declares_only_one_operator_of_the_pair_is_not_mutated`,
`A_type_that_declares_both_operators_is_mutated`, et la suite `LogicalOperatorMutatorTests`, qui est
ce qui échoue si la vérification régresse vers le symbole.

---

## RB-012 — Lire la ligne de commande du compilateur peut détruire la sortie de build · COUVERT

Découvert en faisant fonctionner les solutions multi-projets, et c'est le point le plus surprenant de
ce fichier. Deux options qui ressemblent à une isolation raisonnable sont en réalité nuisibles, et
aucune des deux n'échoue visiblement sur une solution mono-projet.

**`IntermediateOutputPath` est une propriété globale.** La rediriger pour garder les artefacts
générés hors du `obj` de l'utilisateur la propage à chaque projet référencé : la ligne de commande
pointe alors vers des assemblys de référence dans un répertoire où le compilateur n'a jamais eu le
droit de tourner. Tout projet ayant une référence de projet échoue en `FileNotFoundException` avant
même de pouvoir être muté.

**`CopyBuildOutputToOutputDirectory=false` supprime l'assembly construit.** La copie étant supprimée
et le compilateur sauté, le nettoyage incrémental de MSBuild voit un assembly qu'il n'a pas écrit et
le retire de `bin`. Vérifié : interroger `Core` supprimait
`Core/bin/Release/net10.0/Core.dll`, et la requête du projet suivant échouait en tentant de copier la
référence qui venait de disparaître.

**Notre comportement.** Aucune des deux options n'est utilisée. `CoreCompile` est forcé à se
réexécuter en supprimant le fichier de cache que lit sa vérification d'incrémentalité — un fichier que
MSBuild régénère — et la requête tourne *après* le vrai build, si bien que l'assembly intermédiaire
existe encore, que la copie réussit et que rien n'est nettoyé.

**Où vit cette connaissance, et pourquoi en hériter ne suffisait pas.** Stryker ne contient aucune de
ces propriétés ; il délègue tout le problème à Buildalyzer, dont `MsBuildProperties.DesignTime` est un
jeu d'une quinzaine de propriétés globales conçues pour aller ensemble — dont
`SkipCopyBuildProduct`, `BuildProjectReferences` et `UseCommonOutputDirectory` aux côtés du
`CopyBuildOutputToOutputDirectory` que nous avions pris seul. Extraire une propriété d'un ensemble
coordonné, voilà ce qui a produit le bug.

Mais appliquer ce jeu tel quel ne le corrige **pas**, ce qui a été mesuré et non supposé : avec les
propriétés design-time canoniques au complet, un des projets de la fixture renvoyait toujours une
ligne de commande vide et les assemblys construits étaient toujours supprimés de `bin`. La contrainte
de Buildalyzer n'est pas la nôtre : il analyse des projets et n'a jamais besoin que leur sortie de
build survive, là où KillMutants analyse un projet puis exécute les tests contre ces artefacts mêmes.
La leçon est donc plus tranchante que « lire la dépendance qu'on remplace » : en abandonnant une
dépendance on hérite de son espace de problèmes mais pas de ses cicatrices, et ses cicatrices peuvent
ne même pas convenir à notre problème.

**L'ordre est désormais une règle, pas un hasard.** Construire chaque projet de test, puis lire chaque
ligne de commande, puis injecter. MSBuild ne doit pas tourner avant le build, car la requête dépend de
sa sortie ; ni après l'injection, car `dotnet build` et `dotnet test` recopient tous deux l'assembly
d'origine par-dessus le mutant.

**Nos tests.** `MutationTestingEndToEndTests.Several_projects_and_several_test_suites_are_all_covered`,
qui part d'une arborescence propre et échoue si l'une des deux options revient.

---

## RB-013 — Le budget de timeout est mesuré à vide mais dépensé sous charge · COUVERT

Le budget par mutant est dérivé d'une exécution de baseline qui se fait sans rien d'autre en cours,
alors que les mutants sont ensuite testés avec jusqu'à `--parallel` frères se disputant la machine. Un
mutant sain mais lent pouvait dépasser son budget pour cette seule raison et être enregistré
`Timeout` — compté comme une **détection**, donc l'effet est de *gonfler* le score et non de le
déprimer. C'est la pire direction pour une erreur : la suite est créditée d'avoir attrapé quelque
chose qu'elle n'a jamais remarqué.

**Ce que coûte réellement la contention.** Mesuré sur une machine à quatre cœurs : quatre exécutions
concurrentes de la suite de la fixture, dominée par le démarrage, ont pris 0,444–0,514 s contre
0,431–0,444 s seule — 18 % de plus en queue, ce que le budget par défaut (trois fois la baseline plus
trente secondes) absorbe largement. Une suite limitée par le CPU n'a en revanche aucune borne de ce
genre, car l'hôte de test parallélise lui aussi en interne : la demande est le nombre de workers
multiplié par les threads de l'hôte, face aux cœurs disponibles.

**Les options, et pourquoi celle retenue n'est pas un plus gros nombre.** Faire dépendre le facteur du
parallélisme, mesurer la baseline sous charge, ou simplement élargir la marge rendent toutes un faux
timeout moins probable sans le rendre impossible, et chacune l'achète en rendant plus lente la
détection de toute vraie boucle infinie. Ré-exécuter les timeouts une fois les workers terminés
supprime la cause : à ce moment plus rien de notre fait ne tourne, donc un mutant qui dépasse encore
son budget est lent par lui-même. Le coût est une exécution supplémentaire par timeout, et les
timeouts sont rares — sur une suite où ils ne le sont pas, ce sont déjà les mutants qui dominent le
run.

**Nos tests.** `TimeoutConfirmationTests.A_timeout_that_does_not_reproduce_alone_is_not_believed`, qui
injecte un timeout dans le premier mutant exactement comme le ferait la contention et exige que le run
atteigne malgré tout le vrai verdict de ce mutant ; et
`A_timeout_that_does_reproduce_is_still_recorded`, pour que la confirmation ne transforme pas
silencieusement une vraie boucle infinie en survivant.

---

## RB-014 — Le démarrage de processus est désormais le plancher · ASSUMÉ

Avec la sélection par couverture en place, l'exécution d'un mutant coûte environ 0,5 s de lancement
d'un hôte de test contre 0,12 s de test réel. Sélectionner moins de tests ne peut plus beaucoup
aider ; c'est le lancement qui domine.

Le levier évident suivant serait un hôte de test réutilisé à chaud, et il est **délibérément
refusé**. Stryker, qui les réutilise, a besoin de points explicites où ils sont
réinitialisés (`MicrosoftTestPlatformRunnerPool.cs:96,140`) ; il nous faudrait la même discipline, et
un assembly déjà chargé par un processus chaud n'est de toute façon pas relu depuis le disque. Un outil dont le seul
propos est de dire la vérité sur une suite de tests ne peut pas acheter de la vitesse avec un
mécanisme qui rapporte silencieusement des mutants comme tués alors qu'ils ne l'étaient pas.

Consigné comme assumé plutôt qu'ouvert : le coût est compris, l'alternative est comprise, et
l'arbitrage a été fait exprès.

---

## RB-015 — Un mutateur par suppression peut changer le type, pas seulement la valeur · COUVERT

Découvert en ajoutant la famille `NullCoalescing` au M9, et la raison pour laquelle cette famille
n'est pas la réécriture d'une ligne qu'elle paraît être.

**Pourquoi cela compte.** `a ?? b` est très souvent là pour *supprimer* la nullabilité plutôt que
pour fournir une valeur de repli. Retirer le repli laisse alors une expression d'un autre type :
`int total = count ?? 0` muté en `int total = count` est une erreur dure (CS0266), pas un mutant.
Pire, le cas des types référence n'est pas symétrique : `string s = name ?? ""` muté en
`string s = name` compile, parce que la plainte de nullabilité est un avertissement, et que les
avertissements sont déjà neutralisés pour les compilations de mutants par RB-004. Une règle naïve
produit donc silencieusement des mutants utiles dans une moitié des cas et des erreurs de compilation
dans l'autre.

**La règle.** Classifier la conversion que le compilateur devrait faire depuis le seul opérande
gauche vers ce qu'attendait le code environnant :
`ClassifyConversion(coalesce.Left, GetTypeInfo(coalesce).ConvertedType)`, et ne proposer la mutation
que si cette conversion existe et est implicite. Les cas d'élargissement restent mutables
(`object o = text ?? fallback`) et exactement les cas de suppression de nullabilité sont rejetés.

**Pourquoi pas la mutation miroir.** Réécrire `a ?? b` en `b` est tentant par symétrie, et n'est
délibérément pas fait : cela supprime l'opérande gauche et tout effet de bord qu'il porte, ce qui
transforme un signal de couverture manquante en un changement de comportement sans rapport. Le mutant
survivant serait vrai mais sans enseignement.

**Le cas voisin, même milestone.** `Conditional` échange les branches de `c ? a : b`, et un ternaire
dont les deux branches sont la même expression donnerait un mutant au comportement identique à
l'original : survie garantie, pour une raison qui ne dit rien des tests. Ceux-là sont écartés. La
vérification de liaison de cette famille a dû être *plus faible* que celle des binaires : un
conditionnel n'a pas nécessairement de type naturel — `flag ? 1 : null` n'en acquiert un que de sa
cible — donc un type nul ne doit pas être lu comme un échec, contrairement à ce que fait
`BinaryOperatorMutator`.

**Nos tests.** `NullCoalescingMutatorTests.A_fallback_that_removes_nullability_is_not_dropped`,
`A_left_operand_that_widens_to_the_expected_type_is_dropped`,
`ConditionalExpressionMutatorTests.Identical_branches_are_not_swapped`,
`A_target_typed_conditional_is_mutated`, et le test de bout en bout
`Every_mutator_family_is_exercised_against_the_fixture`, qui échoue si une famille cesse de produire
des mutants contre un vrai projet.

---

## RB-016 — Une mutation ne doit pas laisser une déclaration orpheline · COUVERT

Découvert à la toute première exécution de KillMutants sur son propre code source, ce qui est
précisément la raison pour laquelle M10 l'a fait. Le problème est apparu deux fois, sous deux
déguisements différents, avant même qu'un seul mutant ait été testé.

**Le fait sous-jacent.** Une variable de motif ou une variable `out` n'est définie que
**conditionnellement** — « affectée quand cette expression est fausse » — et toutes les mutations que
fait cet outil sur une telle expression changent le moment où ses parties sont évaluées. Ceci, du C#
ordinaire et omniprésent dans ce dépôt :

```csharp
if (node is not BinaryExpressionSyntax binary ||
    !Replacements.TryGetValue(binary.Kind(), out IReadOnlyList<SyntaxKind>? replacements))
{
    yield break;
}
```

muté de `||` en `&&` laisse `binary` et `replacements` non affectées à chaque usage ultérieur :
`CS0165`. Même chose pour un ternaire : échanger les branches de
`d.TryGetValue(k, out var v) ? v : 0` déplace `v` dans la branche où elle n'a jamais été affectée.
Seize mutants du premier run de dogfooding étaient des erreurs de compilation, tous de cette forme.

**Le second déguisement, celui qui a arrêté le run.** La sonde de couverture efface le même état pour
une autre raison : `Hit(id, value)` retourne son argument, donc elle ne peut pas changer ce qu'une
expression *vaut*, mais l'affectation définie conditionnelle ne survit pas au passage par un appel de
méthode. Envelopper ce même `||` a produit dix `CS0165` répartis sur sept fichiers, et le build
instrumenté a échoué net : pas de couverture, pas de run, pas de rapport.

**Pourquoi c'était invisible jusqu'ici.** Le code des fixtures — comparaisons, arithmétique, un
ternaire, un `??` — ne contient aucune variable de motif. Toute cette famille était hors de portée des
fixtures. Seul du vrai code a des clauses de garde.

**Une règle, deux symptômes.** Une expression qui déclare une variable, où que ce soit en dessous,
n'est pas mutée. C'est vérifié une seule fois, à la génération, dans
`MutationSite.DeclaresAVariable` — et comme un site est par définition un nœud que remplace un
mutant, un nœud jamais muté n'est jamais instrumenté non plus. La défaillance d'instrumentation
disparaît par conséquence, sans règle propre.

La règle est délibérément grossière : toute déclaration sous le nœud, même une dont la portée ne
pourrait pas s'en échapper. Ce qu'elle coûte, c'est la rare mutation d'une déclaration que personne ne
relit ensuite ; ce qu'elle achète, c'est qu'aucune des deux défaillances ne peut se reproduire.

**Nos tests.** `MutationSiteTests.An_expression_that_declares_a_variable_is_not_mutated`, qui vérifie
aussi que les expressions ordinaires voisines de la garde sont toujours mutées — une règle qui
avalerait le fichier passerait un test plus faible.

---

## RB-017 — La sonde ne peut pas accepter tous les types qu'un site peut avoir · COUVERT

Le frère de RB-016, et la raison pour laquelle son argument « un site jamais muté n'est jamais
instrumenté » ne clôt pas entièrement le sujet.

**Pourquoi cela compte.** L'enregistreur est `T Hit<T>(int id, T value)`, et C# n'autorise pas
n'importe quel type comme `T`. Vérifié sur le SDK .NET 10 :

```
error CS9244: The type 'Span<int>' may not be a ref struct or a type parameter allowing ref
              structs in order to use it as parameter 'T'
```

Une expression conditionnelle est un site de mutation, et `flag ? a : b` sur deux spans a exactement
ce type. Le cas est donc atteignable dans du code ordinaire et, contrairement à RB-016, la mutation
elle-même est parfaitement valide : c'est seulement la *mesure* qui ne peut pas s'exprimer.

**Pourquoi la réparation évidente est refusée.** `where T : allows ref struct` règle le problème en un
mot, et exige C# 13. La sonde est compilée dans le projet de l'**utilisateur**, dont nous ne
contrôlons pas la version de langage ;
[ADR-0007](adr/0007-measure-coverage-with-a-type-preserving-probe-fr.md) garde ce source délibérément
conservateur pour exactement cette raison. Acheter la couverture des spans au prix d'un refus de
tourner sur une version de langage plus ancienne est un mauvais échange.

**La règle.** Un site dont la valeur est un ref struct, un pointeur ou `void` ne porte pas
d'enregistreur. Ses mutants sont alors testés contre la suite complète : plus lent, jamais faux.
C'est ce qui fait que `CoverageMap.TestsReaching` répond trois choses au lieu de deux : une liste de
tests, une liste vide (mesuré, rien ne l'atteint — `NoCoverage`), et `null` (non mesuré — tout
exécuter). Confondre les deux dernières reviendrait à reporter `NoCoverage` sur du code que les tests
exercent réellement.

**Nos tests.** `MutationSitesTests.A_site_whose_value_is_a_ref_struct_carries_no_recorder`,
`An_ordinary_expression_still_carries_one`,
`Every_mutant_keeps_a_representative_and_every_site_lands_in_one_bucket`.

---

## RB-018 — Un générateur est rarement un seul fichier, et n'est pas le code du développeur · COUVERT

Découvert en construisant la fixture sur laquelle repose désormais cette entrée : un générateur de
source ayant son propre assembly d'appoint, référencé comme l'est un générateur empaqueté. Deux
défauts indépendants, dont chacun suffisait à rendre inutilisable un projet parfaitement ordinaire.

**La dépendance du générateur ne se chargeait pas.** `AnalyzerLoader.AddDependencyLocation` ne faisait
rien, au motif déclaré que « les dépendances se résolvent depuis le répertoire de l'analyseur, que le
contexte par défaut sonde déjà ». Mesuré sur le SDK .NET 10 : il ne le fait pas.
`AssemblyLoadContext.Default` résout les dépendances d'un assembly chargé via les chemins de sondage
de l'*hôte*, pas via le répertoire du fichier chargé ; le générateur levait donc
`FileNotFoundException` à l'initialisation. Roslyn le signale en `CS8784` — un **avertissement** — si
bien que le générateur ne contribuait silencieusement rien et que le projet échouait ensuite à
compiler faute du code qu'il aurait dû produire, avec une erreur accusant KillMutants d'une
reconstruction pourtant correcte. Mapperly, Refit et protobuf embarquent tous des assemblys
d'appoint : c'est la forme courante, pas un cas exotique.

Le correctif mémorise le répertoire de tout ce que Roslyn enregistre et sert les échecs depuis là,
via `AssemblyLoadContext.Default.Resolving`. Brancher le repli plutôt que charger avidement est ce
qui le rend sûr : l'événement ne se déclenche qu'après l'échec de la recherche normale, donc un
répertoire d'analyseur ne peut jamais l'emporter sur le `Microsoft.CodeAnalysis` de l'hôte, et
l'identité des types au travers de la frontière est préservée.

**Le générateur était muté.** Un générateur est référencé avec `OutputItemType="Analyzer"` et
`ReferenceOutputAssembly="false"` : il tourne dans le compilateur au moment du build, et son assembly
n'atteint jamais le répertoire de sortie du projet de test. La découverte suivait quand même la
référence, et le run mutait donc le code source du générateur. Chacun de ces mutants est
non-couvrable par construction : les tests n'exécutent pas ce code, et il n'y a aucun assembly à
remplacer dans le répertoire de sortie. Mesuré sur la fixture : dix mutants sur douze venaient du
générateur et, maintenant que les mutants non couverts comptent dans le score, ils faisaient tomber
un projet aux tests parfaitement bons de 100 % à 16,67 %. Les références de projet ne sont désormais
suivies que lorsqu'elles apportent un assembly que les tests chargeront.

**Ce que cela établit du support des générateurs, et ce que cela n'établit pas.** Un générateur dont
les assemblys d'appoint se trouvent à côté de lui sur la liste d'analyseurs du compilateur fonctionne
désormais. Un générateur compilé contre un Roslyn plus récent que celui sur lequel tourne KillMutants
reste inspectable — c'est enregistré dans `SourceGenerators.Unloadable` et signalé par son nom plutôt
que de ressortir en erreur de compilation inexpliquée. Un générateur ayant besoin d'une *version
différente* d'un assembly déjà chargé par l'hôte obtiendra celui de l'hôte : le repli `Resolving` ne
se déclenche jamais pour un assembly qui s'est résolu. Ce dernier point est **assumé** plutôt que
corrigé. Charger les analyseurs dans leur propre contexte le règlerait et exigerait de partager à la
main les assemblys Roslyn au travers de la frontière : beaucoup d'appareillage pour un cas que nous
n'avons pas encore rencontré.

**Nos tests.** `MutationTestingEndToEndTests.A_source_generator_with_a_dependency_of_its_own_is_run_and_not_mutated`,
contre `tests/fixtures/generator`, qui échoue sur chacun des deux défauts pris isolément.

---

## RB-019 — Un motif est fait de constantes, et un enregistreur n'en est pas une · COUVERT

La seule entrée de ce fichier issue de la lecture de Stryker.NET plutôt que de l'exécution de notre
propre outil — ce à quoi sert précisément la méthode annoncée en tête de document.

**Comment elle a été trouvée.** Leur liste d'orchestrateurs comporte un
`ConstantPatternSyntaxOrchestrator` dont tout le corps est « bloquer l'injection ici, la rétablir
ensuite ». Rien n'y dit pourquoi ; la question était donc de savoir si la contrainte qu'il défend
existe encore pour nous — notre instrumentation est un appel enveloppant et non un interrupteur
injecté, si bien que la plupart de leurs règles de placement ne s'appliquent pas. Celle-ci si.
Mesuré sur le SDK .NET 10 : instrumenter le littéral de `s is "abc"` donne
`CS9135 - a constant value of type 'string' is expected`, et de même pour un bras d'expression
`switch`. Le build instrumenté échoue, donc le run s'arrête avant d'avoir testé un seul mutant.

**La règle.** Aucun site ayant un ancêtre `PatternSyntax` ou `SwitchLabelSyntax` ne porte
d'enregistreur. Une clause `when` en est délibérément exclue : elle est sœur du motif et non partie
de lui, et ses expressions sont du code ordinaire.

**Ce qui demeure.** La *mutation* n'est pas affectée, et ne doit pas l'être : `s is "abc"` réécrit en
`s is ""` est une constante, compile, et change ce qui correspond. C'est une règle sur les
enregistreurs, exactement comme RB-017 — les mutants de ces sites sont testés contre la suite
complète au lieu d'un sous-ensemble mesuré.

**Nos tests.** `MutationSitesTests.A_site_inside_a_pattern_carries_no_recorder`,
`A_site_in_a_when_clause_still_carries_one`, et les entrées de corpus « a literal in a constant
pattern » et « a literal in a switch expression arm », qui vérifient les deux moitiés à la fois : les
mutants compilent et diffèrent, et instrumenter le fichier le laisse compilable.


---

## RB-020 — Une deuxième exécution dans le même processus réutilise les générateurs de la première · OUVERT

**Comment elle a été trouvée.** En écrivant le test de RB-021, par accident. Deux tests de bout en
bout dans le même processus s'exécutent sur le même projet de test à générateur : le premier y ajoute
un générateur qui lève, le second utilise le projet tel quel. Le second a échoué, de manière
déterministe, trois fois sur trois — et il a échoué parce qu'il exécutait les générateurs du
*premier*.

**Le mécanisme.** `SourceGenerators.AnalyzerLoader` charge les assemblages de générateurs d'un projet
avec `AssemblyLoadContext.Default.LoadFromAssemblyPath`. Ce contexte met en cache par *identité*
d'assemblage et non par chemin : le deuxième appel pour un `Sample.Generator.dll` situé ailleurs
renvoie l'assemblage déjà chargé depuis le premier chemin — quel que soit son contenu, et même si ce
chemin n'existe plus. La liste `Directories` que parcourt le repli `Resolving` est `static` elle
aussi, si bien que les répertoires d'analyseurs d'une exécution restent sur la liste de toutes les
suivantes.

Cela se manifeste sous deux formes, toutes deux observées. La deuxième exécution récupère les
générateurs de la première — le cas ci-dessus — ou bien elle n'en récupère aucun : sur la suite
complète, une exécution du projet de test à générateur a annoncé huit générateurs là où elle aurait
dû en avoir neuf, celui du projet n'ayant discrètement rien apporté, et la compilation a ensuite
échoué sur le code qu'il aurait dû produire. Rien n'a été consigné comme non chargeable : l'exécution
ignorait qu'il manquait quoi que ce soit.

**Ce que cela coûte.** Rien via la ligne de commande, qui exécute une session par processus puis
s'arrête. Via l'API — `MutationTesting.RunAsync` appelé deux fois, ce que font nos propres tests et
ce que ferait un mode surveillance ou une intégration dans un IDE — une deuxième exécution génère
silencieusement avec les générateurs de la première. La compilation n'est alors plus celle du projet,
et chaque verdict mesuré contre elle décrit autre chose. C'est exactement la défaillance que cet
outil existe pour ne pas produire, atteinte par un chemin que l'outil livré n'emprunte pas
aujourd'hui.

**Pourquoi elle est ouverte plutôt que corrigée.** Le correctif est celui que RB-018 a déjà écarté :
donner aux analyseurs leur propre `AssemblyLoadContext` et partager les assemblages Roslyn à la main
par-dessus la frontière. C'est une vraie mécanique, et elle relève d'un changement à elle seule
plutôt que de la fin d'un autre. En attendant, notre propre projet de test à générateur renomme son
assemblage lorsqu'il porte un générateur délibérément cassé, pour que la collision ne puisse pas
atteindre un autre test.

**Nos tests.** Aucun pour l'instant. C'est ce que veut dire OUVERT ici.

---

## RB-021 — Un générateur qui échoue produit un avertissement, et un avertissement n'arrête rien · COUVERT

**Comment elle a été trouvée.** Une revue automatique de la pull request d'ouverture de ce dépôt a
pointé `SourceGenerators.Run`, qui jetait les diagnostics renvoyés par
`RunGeneratorsAndUpdateCompilation`. Elle avait raison, et ce qui rend cela important, c'est la
sévérité.

**Ce qui a été mesuré.** Contre Roslyn 5.9, un générateur qui lève depuis son initialiseur est
signalé par `CS8784`, de sévérité **Avertissement** ; un générateur qui lève pendant la génération
donne `CS8785`, avertissement lui aussi. Dans les deux cas le générateur ne contribue rien et la
compilation émet quand même. C'est le bon comportement pour un compilateur — les erreurs qui suivent
désignent le vrai problème — et c'est le mauvais comportement à hériter en silence : RB-004 relâche
déjà les avertissements-en-erreurs, si bien que rien en aval ne l'aurait remarqué.

**Pourquoi ce n'est pas simplement une erreur de compilation.** Quand le code manquant est
nécessaire, l'émission échoue et l'exécution s'arrête bruyamment ; ce cas-là n'a jamais fait de
doute. Le cas dangereux est celui d'un générateur dont la sortie n'est pas ce que les tests
sélectionnés exercent : l'assemblage émet, la ligne de base passe, les mutants sont tués, et le score
décrit un assemblage que le projet ne construit pas.

**La règle.** Une exécution de générateurs emporte ses échecs avec elle. À la reconstruction de la
ligne de base, un échec est fatal — tout ce que l'exécution mesure est comparé à cette compilation.
À l'émission d'un mutant, il ne l'est pas : une mutation peut réellement casser ce qu'un générateur
lit, donc le mutant est rapporté comme n'ayant pas pu être construit, ce que le score laisse de côté
et que l'exécution rapporte comme intestable. Une erreur émise par un générateur compte aussi comme
un échec : le projet compilait avant que KillMutants n'y touche.

**Nos tests.** `SourceGeneratorFailureTests`, qui épingle les deux identifiants de diagnostic et leur
sévérité et prouve que les avertissements propres à un générateur ne sont pas traités comme des
échecs, et
`FailedGeneratorTests.A_run_stops_rather_than_reporting_on_a_compilation_a_generator_did_not_finish`,
qui ajoute un générateur qui lève et dont la sortie n'est nécessaire à personne — la compilation
fonctionne donc toujours, et seule la nouvelle règle arrête l'exécution.

---

## RB-022 — Chaque émission construit un nouveau pilote de générateurs · ACCEPTÉ

**Comment elle a été trouvée.** La même revue automatique que RB-021 a relevé que
`SourceGenerators.Run` jette le pilote renvoyé par `RunGeneratorsAndUpdateCompilation` — celui qui
porte l'état incrémental de Roslyn — si bien qu'un projet à plusieurs centaines de mutants exécute
ses générateurs à froid autant de fois, au lieu de bénéficier des exécutions ultérieures mises en
cache que la conception annonce.

**Ce qui a été mesuré.** L'observation est juste sur le mécanisme et fausse sur ce qu'il coûte.
Contre le SDK .NET 10 sur `tests/fixtures/single`, qui embarque huit générateurs sans en demander
aucun — `Microsoft.Interop.LibraryImportGenerator` et ses semblables sont livrés avec le
framework :

| | Coût |
| --- | --- |
| Première exécution des générateurs dans le processus | 1 139 ms |
| Chacune des suivantes | 4,5 ms |
| L'émission entière qui l'entoure | 9 ms |

La première exécution, c'est le chargement des assemblages et la compilation JIT. Elle est payée une
fois par processus quelle que soit la façon dont le pilote est conservé : ce n'est donc pas ce que la
réutilisation ferait gagner ; les 4,5 ms le sont. Rapporté à notre dernière exécution sur nous-mêmes
— 499 mutants en 7,4 minutes — conserver l'état du pilote récupérerait environ deux secondes, moins
d'un pour cent de l'exécution, et la phase des mutants est dominée par le lancement des hôtes de test
bien plus que par quoi que ce soit que fasse Roslyn.

**Pourquoi c'est accepté plutôt que corrigé.** Un pilote est un état, et les mutants sont testés sur
plusieurs travailleurs à la fois. Acheter moins d'un pour cent d'une exécution au prix d'un état
partagé entre ces travailleurs est un mauvais marché pour un outil dont la pire défaillance est un
verdict discrètement faux — et RB-020, sur la même page, montre ce que le partage d'état de
chargement entre exécutions nous a déjà coûté. C'est consigné plutôt que fait, pour que la prochaine
personne qui remarquera le pilote jeté trouve la mesure au lieu de la refaire.
