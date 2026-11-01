# DEC0025 | A series names the untracked files that do not make its tree dirty, and a pattern that could cover code is refused

## Status

| Date | Status | Note | Related minutes |
|---|---|---|---|
| 2026-11-01 | Proposed | First draft | |
| 2026-11-01 | Accepted | At the maintainer's request | |

## Context

[DEC0015](0015-a-resumed-run-inherits-the-verdicts-its-series-reached-on-the-same-code-en.md) lets a
run continue a series and inherit its verdicts only on the same code, and names that code by a commit
with a clean tree. Its consequences say so in one line: *"A dirty tree never resumes, even with
identical content."* The tree is read with `git status`, which lists untracked files, and the files
KillMutants itself writes beside the report are left out of that read, because counted as edits they
would make every series unable to continue. DEC0015 also lists, as a follow-up, *"a content identity
for a dirty tree"*: that is about a tree already dirty when the series began and found identical
later, not about a tree that was clean and has gained files.

On `Reefact/first-class-errors` at `8f84258` ([#66](https://github.com/Reefact/kill-mutants/issues/66)),
a run was started with `--resume 8f84258` on a clean tree and killed after 1,342 verdicts, when its
container restarted; the plan and the log were intact. The same command, run again in the same
directory, refused: the tree had uncommitted changes. They were sixteen untracked files, fifteen
`*.received.html` and one `*.received.json`, in `FirstClassErrors.GenDoc.UnitTests/`. Snapshot tests
write such a file beside their source, through `[CallerFilePath]`, when a mutant changes their output
([#51](https://github.com/Reefact/kill-mutants/issues/51)). The tree was clean when the run started, so
the run wrote them. A run that ends names the untracked files that appeared while it ran
([#59](https://github.com/Reefact/kill-mutants/issues/59)); a run that is killed, or stopped by
`--stop-after`, does not get to, and it is the run a developer wants to resume.

In CI a series resumes from a fresh checkout and leaves the untracked files of the earlier run behind.
On a workstation a series resumes in the directory it was interrupted in, and #66 arises only there.
Nobody reclones to resume on a workstation.

An untracked file is not always harmless. The .NET SDK compiles every `.cs` file under a project's
directory by default, so an untracked `.cs` is code, and the project and build files (`*.csproj`,
`*.props`, `*.targets`, `global.json`, `NuGet.config`, `.editorconfig`, `*.globalconfig`) change what
is built. On Windows the file system ignores case, so `*.CS` is compiled too. A test can also read a
file at run time, which nothing outside the test can see.

The compiler's inputs are known exactly, from MSBuild's `csc` command line
([DEC0003](0003-compilation-inputs-from-csc-command-line-en.md)), but only for the projects under
mutation, and only after the build. The check that refuses a series happens before the build. A run
may already be refused after its mutants are generated, and the series' files are then kept as they
were.

`--exclude` takes patterns matched against the path relative to the directory the run was pointed at,
written with `/`, in which `*` also matches `/`. Every option that states a habit has a key in
`killmutants.json`, and a list given on the command line replaces the file's. The tool has no release
yet.

Issue #66 proposed three ways out: name the files at the refusal, make files that appeared after the
series began not count, or stop the snapshot library from writing them. Verify, the library in #66,
may offer an environment variable for the last; which one has not been checked.

The maintainer settled the following during the discussion:

* the pattern is given by the user, with `--ignore-untracked` or the `ignoreUntracked` key, in the
  syntax of `--exclude`; with no pattern, the refusal stays and its message names the untracked files
  that appeared since the series began;
* only untracked files can be ignored; a tracked file that was edited always makes the tree dirty;
* the pattern applies every time a series reads the state of its code, when it starts as when it
  resumes, and not to `--since`;
* `--ignore-untracked` on the command line without `--resume` is an error; the key in the file then
  has no effect;
* a pattern that can match the same path as a pattern of an internal list of sensitive files is
  refused before the run starts, ignoring case; a pattern made only of wildcards and `/` is refused
  with a message of its own, saying that it would ignore every untracked file;
* the list retained on the day is `*.cs`, `*.csproj`, `*.props`, `*.targets`, `*.globalconfig`,
  `.editorconfig`, `global.json`, `NuGet.config` and `*.resx`;
* a run that finds an ignored file among the compiler's inputs is refused as well;
* the two checks stop obvious mistakes, not a pattern written to get around the rule;
* the patterns are recorded in the plan, and may differ from one run of the series to the next;
* each ignored file is named in a warning on standard error and recorded in the JSON report.

## Decision

In this context, we decide that a series may name, with `--ignore-untracked`, the untracked files that
do not make its tree dirty, that each file so ignored is reported, and that a pattern which could cover
a source or build file, or which matches a file the compiler reads, is refused.

## Rationale

The files in #66 were written by the run itself, in a tree that was clean when the series began, and
the refusal that followed is what DEC0015 specifies. The rule is right about the code and wrong about
these files: they change nothing that is compiled. Leaving out the tool's own files from the same read
is what DEC0015 already does, for the same reason; a pattern extends that exception to the files the
project's tests write, which only the project knows.

A pattern given by the user puts that knowledge where it is. The tool cannot tell a snapshot left by a
test from a source file added by hand: both are untracked and appeared since the series began. The
project can, and it writes the pattern once, in the file it versions with its code.

Because an untracked `.cs` or build file changes what is compiled, a pattern that could cover one would
turn the escape hatch into a way to inherit verdicts reached on other code. Testing a pattern against
a list of sensitive patterns, before anything runs, stops the obvious cases at no cost and with a
message that says why. Ignoring case follows from Windows compiling `*.CS`. Testing the ignored files
against the compiler's inputs, once they are known, catches a pattern that the list could not judge,
such as a directory of generated sources. Neither check covers a file a test reads, and neither can
stop a pattern written to get around them; the record says so rather than claiming more.

Reporting every ignored file keeps the run honest about what it accepted, which DEC0015's strictness
was protecting: a reader of the report sees that the tree was not exactly the commit, and which files
made the difference.

The case arises where a series resumes in the directory it was interrupted in, which is a workstation.
Making the pattern explicit, rather than active by default, leaves CI, where the problem does not
occur, exactly as DEC0015 left it.

Deciding the name and the list before the first release keeps them cheap to change: nothing depends on
them yet.

## Alternatives considered

### Alternative 1 — The status quo, with a better refusal

* **Description:** Keep DEC0015's rule unchanged, and make the refusal name the untracked files that
  appeared since the series began, as the likely work of the series itself.
* **Why rejected:** The refusal stays correct, but the files have to be deleted by hand before every
  resume, and nothing automates that step. It is kept as what happens when no pattern is given.

### Alternative 2 — Ignore every untracked file that appeared since the series began

* **Description:** Record the untracked files when the series starts, and ignore any file that was
  not among them.
* **Why rejected:** A `.cs` file added by hand between two runs is compiled, and the series would then
  inherit verdicts reached on other code.

### Alternative 3 — Ignore the files the series' own runs saw appear

* **Description:** Each run records the untracked files that appear while it runs, and a resume
  ignores those.
* **Why rejected:** A killed run records nothing unless it writes at every verdict. And it trusts
  whatever appears during a long run, including a change made by hand on the same workstation.

### Alternative 4 — A pattern built into the tool, such as `*.received.*`

* **Description:** KillMutants ignores the files of known snapshot libraries without being told.
* **Why rejected:** It brings the knowledge of one library into the tool, while other snapshot
  libraries write elsewhere and otherwise.

### Alternative 5 — Stop the snapshot library from writing

* **Description:** Set, on the test host, the environment variable that keeps Verify from writing its
  `.received` files.
* **Why rejected:** It is specific to one library, the variable has not been checked, and it changes
  the environment the tests run in under mutation compared with the baseline.

### Alternative 6 — Resume from a fresh clone, and say so in the refusal

* **Description:** Alternative 1, with one more way out in the message: continue the series from
  another checkout of the same commit, which DEC0015 already supports.
* **Why rejected:** Resuming from another directory is not natural where the problem arises. On a
  workstation it means recloning, rebuilding and moving the plan and the log by hand, to get around
  files the tool can name.

## Consequences

### Positive

* A series interrupted on a workstation resumes in the same directory once its project has named the
  files its tests write.
* DEC0015's rule is kept for every tracked file and for every untracked file no pattern names.
* The report says which files were ignored, so a tree that was not exactly the commit is never
  presented as if it were.
* A pattern that could cover code is refused with a message that says why and what to write instead.

### Negative

* Patterns have to be precise: `*.received.*` is refused because it accepts `x.received.cs`, and the
  extensions are named instead.
* The check against the compiler's inputs comes after the build, so a pattern it refuses costs the
  time of discovery, build and analysis.
* The command line and the configuration file carry one more option, and its name and key become a
  contract once the tool is released.
* Adding a pattern to the sensitive list after the release can refuse a pattern that used to pass.

### Risks

* A file a test reads at run time, and that a pattern ignores, can change verdicts without either
  check seeing it.
* An untracked source file in a test project, covered by a pattern the list could not judge, escapes
  both checks, since the compiler's inputs are read only for the projects under mutation.
* A pattern written to get around the checks makes a series inherit verdicts reached on other code.

### Follow-up actions

* Implement the refusal message that names the files appeared since the series began, used when no
  pattern is given.
* Implement `--ignore-untracked`, the `ignoreUntracked` key, the two checks, the warning and the field
  of the JSON report.
* Document the option in the `README.md`, with the sensitive list as it stands.
* Review the sensitive list before the first release.
