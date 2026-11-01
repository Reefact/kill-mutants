# KillMutants on first-class-errors at 8f84258 — the run after the fixes

This is the run that [the reference](../first-class-errors-8f84258/README.md) was kept for. It is
data, not code, and this branch is never meant to be merged.

## The run

| | |
| --- | --- |
| Subject | `Reefact/first-class-errors` at `8f84258319881be6a502de40d91244ef9302cc93`, a clean clone with `dotnet restore` done |
| KillMutants | `48ee3149be5a388159791752879d722f4b4b83a7` (`main` after #64), Release, default catalogue |
| Command | `killmutants <clone> --report-json km.json --resume 8f84258` — **no `--exclude`, no `TMPDIR`** |
| Machine | Ubuntu 24.04.5, 4 vCPU, runtime 10.0.14, 2 workers |
| Exit code | **2**: a part was set aside (DEC0022) |
| Result | 3048 mutants: 2079 killed, 568 survived, 3 timed out, 398 without coverage |
| Score | **none printed**: a project set aside at discovery leaves the population unknown (DEC0022) |

Over the reference's own population this is 2082 detected out of 3048, **68.31 %** against the
reference's 68.28 %. That figure is computed here from the counts, it is not one the tool printed.

## What the fixes changed

| | Reference (`98aa95e`) | This run (`48ee314`) |
| --- | --- | --- |
| Mutants | 3049 | 3048 |
| Killed / timed out | 2079 / 3 | 2079 / 3 |
| Survived | 569 | 568 |
| Without coverage | 398 | 398 |

- **No verdict changed** on the 3048 mutants both runs have, and the run generated none the
  reference did not.
- **One mutant is gone:** `9a087560fbb53a61`, a `Pattern` mutant at
  `FirstClassErrors.RequestBinder/PropertyGetters.cs:116:58` that survived in the reference. It is the
  effect of #54 (a pattern that already matches everything), and the only difference between the two.
- **The 66 other survivors the fixes were expected to touch are still there** (ConfigureAwait,
  EnableConcurrentExecution, an expression's name): review of #54 found them observable, so they belong
  to #42 and not to the catalogue.

## What DEC0022 did on the real case

- `tools/floor-check/FloorCheck.csproj` was **set aside at discovery**: its own `global.json` asks for
  SDK 8.0.100, which is not installed. The report names it with the reason and the `--exclude` to write.
- The rest of the repository was measured to the end, including the target whose baseline was red in
  the reference (#58).
- The run exited with 2 and printed no score. Standard error named the two `.received` files the
  tests had left in the clone (#59).

## What this run is not

- **Not one stretch.** It ran as four runs of one series (DEC0015): the container this was run in
  restarted three times and took the process with it, so `--resume` carried it. The last run's report
  says it inherited 2017 verdicts and reached 1031. **The durations are not comparable** with the
  reference's, and none is given here.
- **Not on the original checkout.** The checkout was left dirty by the interrupted run's tests (16
  `*.received.*` files), and `--resume` refuses a dirty tree (#66). It was left as it was and the run
  went on in a clone of the same commit. Mutant keys are relative, so the two compare.
- **Once.** The variability between two runs was not measured.

## The files

- `km-fce-8f84258-km-48ee314.json.gz` — the report `--report-json` wrote, gzipped. SHA-256 of the
  uncompressed file: `989b6d045bc099d8d8907a736806a11522aba47670ca9a36e3fd9c9699e27218`.
- `km-verdicts-fce-8f84258-km-48ee314.tsv` — the 3048 mutants in the same shape as the reference's:
  `key`, `status` (`K`, `S`, `N`, `T`), `mutator`, `path:line:col`, sorted by key.
- `console-report-fce-8f84258-km-48ee314.txt.gz` — what the run printed on standard output, with the
  "Set aside" section at the top.

## Comparing

```bash
cut -f1,2 ../first-class-errors-8f84258/km-verdicts-fce-8f84258.tsv | tail -n +2 | sort > old.tsv
cut -f1,2 km-verdicts-fce-8f84258-km-48ee314.tsv | tail -n +2 | sort > new.tsv
join -t $'\t' -a1 -a2 -e '-' -o 0,1.2,2.2 old.tsv new.tsv | awk -F'\t' '$2 != $3'
```

This prints one line, `9a087560fbb53a61  S  -`.
