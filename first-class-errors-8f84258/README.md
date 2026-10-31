# KillMutants on first-class-errors at 8f84258 — the reference verdicts

This branch holds data, not code. It is orphaned from `main` on purpose and is never meant to be
merged: it keeps the verdicts of the run described in #44 so that the run after the fixes (#45–#52)
can be compared with it mutant by mutant, once the container that produced it is gone.

## The run

| | |
| --- | --- |
| Subject | `Reefact/first-class-errors` at `8f84258319881be6a502de40d91244ef9302cc93` |
| KillMutants | `98aa95ed79e34182a70fdf5079f108dd6d4b62ed`, Release, default catalogue |
| Command | `killmutants <repo> --exclude 'tools/*' --report-json km.json --resume 8f84258`, with `TMPDIR` set to `<repo>/artifacts/km-tmp` (#45, #47) |
| Shape | a series of two runs (DEC0015): 35.3 min stopped by the baseline of #45, then 39.4 min resumed |
| Machine | Ubuntu 24.04.5, 4 vCPU Xeon 2.1 GHz, SDK 10.0.114, runtime 10.0.14, 2 workers |
| Result | 3049 mutants: 2079 killed, 569 survived, 3 timed out, 398 without coverage — 68.28 % |

## The files

- `km-fce-8f84258-km-98aa95e.json.gz` — the report `--report-json` wrote, unchanged, gzipped
  (`gunzip -k` to read it). SHA-256 of the uncompressed file:
  `a918a7a3cb314083344d5b4d87e373c90d9da514a15100f61124aa276c8e3d7c`.
- `km-verdicts-fce-8f84258.tsv` — the same 3049 mutants reduced to what a comparison needs, sorted by
  key: `key`, `status` (`K` killed, `S` survived, `N` no coverage, `T` timed out), `mutator`,
  `path:line:col`.

## Comparing the next run

The key is `MutantKey`, which does not depend on the run, so a mutant both runs generate has the same
key in both. Mutants the fixes stop generating (#48, #49, #50) appear only on this side; all 67 of them
survived here, which is the expected difference. Any other key whose status changed is worth reading.

```bash
jq -r '.mutants[] | [.key, .status[0:1]] | @tsv' new.json | sort > new.tsv
cut -f1,2 km-verdicts-fce-8f84258.tsv | tail -n +2 | sort > old.tsv
join -t $'\t' -a1 -a2 -e '-' -o 0,1.2,2.2 old.tsv new.tsv | awk -F'\t' '$2 != $3'
```

The first letter of each status is the code this file uses, so `.status[0:1]` makes the two sides
comparable. A `-` means the key exists on one side only.
