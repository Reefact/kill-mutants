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

**Nos tests.** Chaque famille du catalogue porte un test
`Every_replacement_carries_the_kind_it_prints` — `ComparisonOperatorMutatorTests`,
`LogicalOperatorMutatorTests`, `BooleanLiteralMutatorTests` — et le garde-fou comportemental se
trouve dans
`ProjectCompilationTests.A_mutant_emits_an_assembly_that_actually_differs_from_the_baseline`. La
règle est énoncée dans le contrat `IMutator` pour que chaque nouveau mutateur en hérite, et imposée
structurellement par `BinaryOperatorMutator`, qui construit les remplacements par kind pour toute la
famille. **Tout mutateur ajouté au catalogue doit être couvert par une assertion équivalente.**

---

## RB-002 — Les générateurs de source produisent du code absent de la liste des sources · OUVERT

**Ce que fait Stryker.** Exécute le driver de génération, et le réexécute après chaque
`ReplaceSyntaxTree` (`Compiling/CsharpCompilingProcess.cs:360-364`). Il détecte aussi les projets
épinglant un compilateur plus récent que le sien et le signale nommément (`ReferencesNewerCompiler`,
`Buildalyzer/IAnalyzerResultExtensions.cs:115-120`).

**Pourquoi cela existe.** `CscCommandLineArgs` liste les générateurs sous `/analyzer:` mais **pas**
leur sortie parmi les sources — le compilateur la produit pendant le build.

**Est-ce toujours d'actualité ?** Oui, et plus qu'avant : `[GeneratedRegex]`, `[JsonSerializable]`,
`[LibraryImport]`, Mapperly, Refit et les minimal APIs ASP.NET Core reposent tous sur des
générateurs. Reproduit : un projet avec une propriété partielle `[GeneratedRegex]` échoue à l'émission
avec `CS9248` si les générateurs ne sont pas exécutés. La ligne de commande de notre propre fixture
porte déjà huit assemblys `/analyzer:`.

**Impact.** L'affirmation de l'ADR-0003 selon laquelle les sources générées « suivent
automatiquement » n'est vraie que pour les fichiers émis par le SDK. L'ADR-0005 fait échouer cela
*proprement* — le baseline passe au rouge au lieu de produire des faux positifs — mais KillMutants ne
fonctionne tout simplement pas sur ces projets aujourd'hui.

**Ce qu'il faut faire.** Exécuter `CSharpGeneratorDriver` sur la compilation. Deux subtilités à
trancher délibérément : la sortie d'un générateur peut dépendre de l'arbre muté, donc en toute
rigueur le driver doit être réexécuté par mutant (l'exécuter une fois sur la compilation d'origine
est une approximation défendable pour des mutateurs au niveau opérateur, et doit être documentée
comme telle) ; et les générateurs se chargent dans notre propre processus, si bien qu'un projet
épinglant un Roslyn plus récent que le nôtre ne contribue silencieusement rien — ce cas doit être
signalé nommément, pas présenté comme « KillMutants n'a pas pu compiler votre projet ».

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

## RB-006 — L'injection ne résiste pas à un arrêt brutal · OUVERT

Si KillMutants est tué anormalement (annulation CI, SIGKILL) alors qu'un mutant est injecté,
`AssemblyInjection.Dispose` ne s'exécute jamais et le développeur se retrouve avec un assembly muté
et un fichier `.killmutants-original` dans `bin`. Stryker souffre de la même plaie et se contente de
la journaliser (`ProjectComponents/TestProjects/TestProjectsInfo.cs:51-58`).

**Ce qu'il faudrait faire.** Détecter une sauvegarde résiduelle au démarrage et la restaurer avant
toute autre chose. Peu coûteux, et cela transforme un échec déroutant en non-événement.

---

## RB-007 — Un projet multi-cible produit une ligne de commande par framework · OUVERT

`MsBuildQuery` demande `CscCommandLineArgs` sans épingler de framework cible. Sur un projet qui en
vise plusieurs, le résultat est ambigu, et des mutants pourraient être émis contre un framework que
le projet de test n'utilise pas. À résoudre avant M3.

---

## RB-008 — L'exclusion des fichiers générés repose uniquement sur le chemin · OUVERT

`MutantGenerator` ignore `.g.cs`, `.g.i.cs` et tout ce qui se trouve sous `obj/`. Les fichiers
produits par T4, les concepteurs visuels, protobuf ou un `BaseIntermediateOutputPath` personnalisé
sont toujours mutés, produisant des constats sur du code que personne n'a écrit. Lire les en-têtes
`<auto-generated>` serait plus honnête.

---

## RB-009 — Conserver les artefacts de chaque mutant ne passera pas à l'échelle · OUVERT

Les arbres syntaxiques mutés, les tableaux d'octets émis et les diagnostics par mutant sont tous
conservés pendant tout le run. De vraies solutions atteignent des dizaines de milliers de mutants.
Sans conséquence à l'échelle actuelle ; cela en devient une à M3.

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
