# Security policy

## Supported versions

Each package this repository publishes is versioned on its own release train (see
[`RELEASING.md`](RELEASING.md)), so support is defined per package: only the most recent version of
**each package** published on nuget.org receives security fixes, pre-releases included. A fix ships
as a new version of the package concerned; an earlier version is not patched.

A mutation score moves with the tool's version, so projects pin KillMutants per repository rather
than taking updates silently. Moving the pin to the fixed version is the upgrade path, and the one
change a security fix asks of you.

## Reporting a vulnerability

**Do not open a public issue.** Report it privately, through GitHub's private vulnerability
reporting: [Report a vulnerability](https://github.com/Reefact/kill-mutants/security/advisories/new),
under the repository's **Security** tab.

Say what you found, the version or commit, how to reproduce it, and what an attacker gains. A
minimal repository that shows it is the most useful thing you can send.

## What to expect

- **Acknowledgement within 7 days** of the report.
- **A first assessment within 30 days**: whether it is accepted as a vulnerability, how severe it is
  judged, and what happens next.
- You are kept informed in the advisory until it is closed. Once a fixed version is published, the
  advisory is made public, crediting you unless you ask otherwise.

KillMutants is maintained by one person; these are the delays that can be kept, rather than shorter
ones that could not.

## What is and is not a vulnerability here

KillMutants **builds and runs the code it is pointed at**: it compiles the projects under test,
launches their test executables, and runs their tests against each mutant. Running it on a
repository therefore runs that repository's code, with the permissions of whoever started it —
exactly as `dotnet test` would. Running KillMutants on a repository you do not trust is running
untrusted code, and that is not a vulnerability in KillMutants.

What counts is what **KillMutants itself** does, as distinct from what the code under test does
while KillMutants runs it. A snapshot test that writes a `.received` file beside its source when a
mutant changes its output is that test's own behaviour: KillMutants reports such files at the end of
a run, as the README describes, but they are not a vulnerability.

In scope, for example:

- KillMutants itself writing, deleting or reading outside the directories a run is documented to
  use;
- KillMutants itself modifying an original source file, or placing a mutated assembly outside the
  sandbox it builds for testing;
- a report or log that leaks something the run had no reason to record;
- a package that fails the integrity check of the channel it came from: a `.nupkg` attached to a
  GitHub Release that fails its provenance attestation (`gh attestation verify`), or a package
  downloaded from nuget.org that fails `dotnet nuget verify`. The nuget.org copy is expected **not**
  to match the attested checksum, since nuget.org repository-signs every upload; see
  [`RELEASING.md`](RELEASING.md), "Verifying a published package".
