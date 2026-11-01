# The `Linq` family measured on first-class-errors and slugger

This is the measurement DEC0024 asks for before the family's default status is decided. It is data,
not code, and this branch is never meant to be merged. The family is `Linq`, opt-in, implemented in
Reefact/kill-mutants#72 (merged), and decided by DEC0024.

## The runs

| | `first-class-errors` | `slugger` |
| --- | --- | --- |
| Subject | `Reefact/first-class-errors` at `8f84258` | `Reefact/slugger` at `c9cf4a2` |
| Command | `killmutants <clone> --mutators Linq --exclude 'tools/*' --report-json km.json --resume linq63` | `killmutants <clone> --mutators Linq --report-json km.json --resume linq63s` |
| KillMutants | the branch of #72 at `c0ae81e`, on `main` `b1b2fa9` | the same build |
| Machine | Ubuntu 24.04.5, 4 vCPU, runtime 10.0.14, 2 workers | the same |
| Result | 31 mutants: 18 killed, 11 survived, 2 without coverage | 43 mutants: 23 killed, 19 survived, 1 without coverage |
| Compile errors | **0** | **0** |
| Duration | 12.1 min | 9.6 min |

Both ran on a clean clone of the commit, after `dotnet restore`, in one stretch each.

## What these files are not

- **Not the merged code.** They were produced by `c0ae81e`. The pull request was then reviewed: its
  second commit added the rule that a call returning by reference carries no coverage recorder, and
  both commits were rebased onto a newer `main` before the merge (`7a05f72`, `0d7b5bb`). Neither
  codebase has a pair returned by reference, so the rule changes nothing in these two runs, but they
  were not re-run on the merged commit.
- **Not repeated.** Each run was made once; the variability between two runs was not measured.
- **Not a default decision.** DEC0024 leaves the family opt-in until it is measured, and this is that
  measurement, not the decision.

## Reading them

By swap, first-class-errors: `Any`↔`All` 9 killed / 2 survived / 1 uncovered and one `All`→`Any`
survived; `OrderBy`↔`OrderByDescending` 5 / 4; `ThenBy`↔`ThenByDescending` 2 / 4; `Skip`↔`Take` 2 / 0;
`Concat`↔`Except` one uncovered. The two gaps that motivated the family survive:
`DocumentedByInvalidSignatureAnalyzer.cs:47` (bound to `ImmutableArrayExtensions.Any`) and
`ExampleDoesNotCallDocumentedFactoryAnalyzer.cs:74`; `Currency.cs:36` is of the same kind.

By swap, slugger: `Order`↔`OrderDescending` 4 killed / 7 survived; `Min`/`Max` and `MinBy`/`MaxBy` 10 / 6;
`Take`↔`Skip` 6 / 2; `Any`/`All` 0 / 3 / 1 uncovered; `Concat`/`Except`/`Intersect`/`Union` 3 / 1. One
swap is not a LINQ call: `Math.Min` → `Math.Max` in `KeySpelling.cs:45`, because `System.Math` declares
both names with the same shape.

## The files

For each repository:

- `km-<repo>-linq-c0ae81e.json.gz` - the report `--report-json` wrote, gzipped.
- `km-verdicts-<repo>-linq-c0ae81e.tsv` - the mutants, sorted by key: `key`, `status` (`K` killed,
  `S` survived, `N` no coverage, `T` timed out), `mutator`, `path:line:col`, then the expression as
  written and as mutated, cut at 100 characters.
- `console-<repo>-linq-c0ae81e.txt.gz` - what the run printed on standard output.

SHA-256 of the uncompressed reports: first-class-errors `f204f176704c46f4eb3399b61ace6001d460ef9e945b3a8f0b6c519929dc04dd`; slugger `72b6d2845d837c609a26d5e905b18badbf1e802656413e83576d40bfa1f1ca9d`.
