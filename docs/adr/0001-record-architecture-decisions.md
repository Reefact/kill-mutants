# ADR-0001 — Record architecture decisions

**Status:** accepted · **Date:** 2026-08-31

## Context

KillMutants makes a small number of decisions that are expensive to reverse: how a mutant is
applied, how compilation inputs are obtained, how tests are executed. Six months from now the
reasoning behind them will be invisible in the code, and the temptation to "fix" a deliberate
choice will be real.

## Decision

Structuring decisions are recorded as short ADRs in `docs/adr`, numbered sequentially and never
rewritten — a decision that turns out wrong gets a new ADR that supersedes it.

An ADR is written only when a decision is **hard to reverse** and had **more than one reasonable
answer**. Choices that follow obviously from the project's stated constraints are not ADRs.

## Consequences

There are few ADRs, and each is worth reading.
