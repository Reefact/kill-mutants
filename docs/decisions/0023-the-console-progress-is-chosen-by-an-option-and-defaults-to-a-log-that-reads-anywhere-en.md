# DEC0023 | The console progress mode is chosen explicitly, with `log` as the default and no detection of the terminal

## Status

| Date | Status | Note | Related minutes |
|---|---|---|---|
| 2026-11-01 | Proposed | First draft | |
| 2026-11-01 | Accepted | At the maintainer's request | |

## Context

A run takes minutes to hours and writes what it is doing on standard error. Today the form of that
output is chosen by the tool, from whether standard error is redirected: on a terminal one line is
rewritten in place, and otherwise each phase is announced once and its count repeated at most once a
minute. The reporter's own comments record why the two forms exist: thousands of carriage returns in a
log help nobody, and a run whose mutant testing took fifty-seven minutes once printed one line and
then nothing until the job was cancelled.

Following a 75-minute run on `Reefact/first-class-errors` through that output (#65) showed what neither
form says. It names no mutant, so a slow mutant cannot be told from a stuck run; a run killed with its
container looked the same as a run that was merely slow; no line carries a time; and a part set aside
under [DEC0022](0022-a-run-carries-on-past-what-fails-and-reports-what-it-measured-and-what-it-could-not-en.md)
is named in the final report only.

Redirection does not tell who reads the output. A terminal can sit inside a container whose output is
captured, and a CI log can be read by a person; the tool's guess is wrong in both directions and gives
whoever is surprised nothing to change. The maintainer decided against detecting anything, and for an
option the user sets.

The options of the command line are short and say what they act on: `--exclude`, `--since`,
`--stop-after`, `--no-coverage`, `--report-json`. None concerns what is shown on the console. The tool
has no release yet: no tag exists, the version is `0.1.0-alpha`, and the changelog holds only
unreleased changes, so no pipeline can depend on an option's name, values or default today.
[DEC0009](0009-exit-codes-are-a-public-contract-en.md) treats the exit codes as a public contract
because tools depend on them; the JSON report is the channel the tool offers to tools.

The maintainer also decided that the lines of the console progress are not a contract, that the help
says the JSON report is the channel for tools, and that no dependency is added for the display.

## Decision

In this context, we decide that the console progress is chosen by an explicit option, `--progress`,
taking `log`, `live` or `none`, that `log` is its default, and that the tool detects nothing about the
terminal to choose it.

## Rationale

An option states what the user wants, where a guess from redirection cannot: it removes the case where
a terminal in a container, or a log read by a person, gets the other form than the one wanted, and it
gives the user something to change when the output surprises them.

The default has to suit everyone who does not set it, including the user who has not read the help. A
dated line per transition reads the same on a terminal, in a file and in a CI log, whereas a line
rewritten in place is what the reporter's comments say makes a captured log unreadable. Choosing the
form that cannot do harm as the default, and the pleasant one on request, trades a small loss for the
user at a terminal against a failure for the user whose output is captured.

Deciding the name, the values and the default before the first release is what keeps the decision
cheap: nothing depends on them yet, and after a release a pipeline will, as the exit codes show with
DEC0009. Leaving the form of the lines out of the commitment, with the JSON report as the machine
channel, keeps the display free to improve without a record each time.

The name `--progress` says what the option acts on, as the other options do, and does not read as
naming where the report goes, which `--report-json` already does.

## Alternatives considered

### Alternative 1 — Keep choosing from whether standard error is redirected

* **Description:** The status quo: the tool decides, and nothing sets it.
* **Why rejected:** It is wrong in both directions for the cases in the context, and it leaves the user
  with nothing to change. The maintainer decided against detecting anything.

### Alternative 2 — Default to `live`

* **Description:** The line rewritten in place by default, `log` on request.
* **Why rejected:** The default applies to everyone who sets nothing, and in a captured log that form
  is the one the reporter's comments describe as unreadable. Without detection there is no way to
  give a terminal `live` and a file `log` at once.

### Alternative 3 — Another name for the option, such as `--output`

* **Description:** A name that covers the console output in general.
* **Why rejected:** Next to `--report-json` it reads as naming where the report is written, which is
  not what the option controls.

## Consequences

### Positive

* The form of the output no longer depends on a guess, and a user whose output surprises them can
  change it.
* The default is readable in a terminal, a file and a CI log alike.
* The display can change between versions: the option's name, its three values and its default are
  held as a contract, the form of the lines is not, and the help says the JSON report is what tools
  read.

### Negative

* A person at a terminal who sets nothing gets dated lines, not a live line, until they ask for
  `--progress live`.
* The command line has one more option to learn.
* The option's name, its three values and its default will have to be kept once the tool is released:
  a script that passes `--progress none` depends on the value as much as on the name.

### Risks

* A pipeline may come to depend on the lines despite the help, and then treat them as an interface.
* The default may turn out to suit the usual user less than assumed, which this record has no
  measurement of.

### Follow-up actions

* Implement the option, the three modes and the two events written at once, as described in #65.
* State in the help that the JSON report is the channel for tools and that the progress lines are not
  an interface.
* Revisit the default, by a new record, if the tool's usual use turns out to be interactive.
