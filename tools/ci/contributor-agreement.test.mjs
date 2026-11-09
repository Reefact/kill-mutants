// Tests the script of `.github/workflows/contributor-agreement.yml` (DEC0029, DEC0030).
//
// That workflow runs on `pull_request_target`, so GitHub executes it as `main` holds it: the pull
// request that changes it never runs the changed version, and actionlint checks its syntax, not what
// it decides. This file is what a change to that logic is tested by. It reads the workflow, extracts
// the `github-script` body verbatim and runs it with a mocked `github`, `context` and `core`, so the
// code under test is the code that will run, not a copy of it. It is run by the `ci` gate, in
// `Lint the workflows`, with `node --test tools/ci/contributor-agreement.test.mjs`.
//
// The rendered descriptions below stand in for what GitHub's pulls API returns as `body_html`. The
// ticked one is copied from GitHub's rendering of pull request 121. The hostile ones model what GitHub
// renders for Markdown that shows no ticked box - a fence GitHub does not close, a raw `<pre>`, the
// clause as code or as text: the check relies on GitHub's renderer emitting a
// `task-list-item-checkbox` only for a real task list item, and that is not something a test here
// can observe.

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const workflow = readFileSync(new URL('../../.github/workflows/contributor-agreement.yml', import.meta.url), 'utf8');
const template = readFileSync(new URL('../../.github/pull_request_template.md', import.meta.url), 'utf8');

function extractScript(yaml) {
  const lines = yaml.split('\n');
  const start = lines.findIndex(line => /^\s+script: \|\s*$/.test(line));
  assert.notEqual(start, -1, 'the workflow has no `script: |` block');
  const keyIndent = lines[start].search(/\S/);
  const body = [];
  for (const line of lines.slice(start + 1)) {
    if (line.trim() !== '' && line.search(/\S/) <= keyIndent) {
      break;
    }
    body.push(line);
  }
  const indent = Math.min(...body.filter(line => line.trim() !== '').map(line => line.search(/\S/)));
  return body.map(line => line.slice(indent)).join('\n');
}

const script = extractScript(workflow);
const AsyncFunction = Object.getPrototypeOf(async function () {}).constructor;
const validate = new AsyncFunction('github', 'context', 'core', script);

const agreementUrl = 'https://github.com/Reefact/kill-mutants/blob/main/CONTRIBUTOR_AGREEMENT.md';
const clauseMarkdown = `* [x] I have read the [Contributor Agreement](${agreementUrl}) and, if I am not the Project Owner (@Reefact), I accept it for this contribution.`;

// GitHub's rendering of the ticked section of pull request 121.
const tickedHtml = `<h2 dir="auto">Contributor agreement</h2>
<ul class="contains-task-list">
<li class="task-list-item"><input type="checkbox" id="" disabled="" class="task-list-item-checkbox" aria-label="Completed task" checked=""> I have read the <a href="${agreementUrl}">Contributor Agreement</a> and, if I am not the Project Owner (<a class="user-mention notranslate" data-hovercard-type="user" data-hovercard-url="/users/Reefact/hovercard" data-octo-click="hovercard-link-click" data-octo-dimensions="link_type:self" href="https://github.com/Reefact">@Reefact</a>), I accept it for this contribution.</li>
</ul>`;
const untickedHtml = tickedHtml.replace(' aria-label="Completed task" checked=""', ' aria-label="Incomplete task"');
const tickedWith = (from, to) => tickedHtml.replace(from, to);

const signedByGitHub = { author: 'dependabot[bot]', committer: 'web-flow', verified: true, reason: 'valid' };

async function run({ login = 'someone', ref = 'patch', body = template, html = tickedHtml, head = signedByGitHub } = {}) {
  const outcome = { failed: null, updatedBody: null };
  const github = {
    rest: {
      pulls: {
        update: async ({ body: updated }) => { outcome.updatedBody = updated; },
        get: async ({ mediaType }) => {
          assert.equal(mediaType?.format, 'html', 'the description must be read as GitHub renders it');
          if (html instanceof Error) {
            throw html;
          }
          return { data: { body_html: html } };
        },
      },
      repos: {
        getCommit: async () => {
          if (head instanceof Error) {
            throw head;
          }
          return {
            data: {
              author: { login: head.author },
              committer: { login: head.committer },
              commit: { verification: { verified: head.verified, reason: head.reason } },
            },
          };
        },
      },
    },
  };
  const core = { info() {}, notice() {}, setFailed(message) { outcome.failed = message; } };
  const context = {
    payload: { pull_request: { number: 1, user: { login }, head: { ref, sha: 'abc' }, body } },
    repo: { owner: 'Reefact', repo: 'kill-mutants' },
  };
  await validate(github, context, core);
  return outcome;
}

const passes = async options => assert.equal((await run(options)).failed, null);
const fails = async options => assert.notEqual((await run(options)).failed, null);

test('a pull request the Project Owner opens directly needs nothing, and loses the section', async () => {
  const outcome = await run({ login: 'Reefact', ref: 'feature' });
  assert.equal(outcome.failed, null);
  assert.ok(!outcome.updatedBody.includes('contributor-agreement:start'));
});

test('a pull request the Project Owner opens directly, without the section, is left alone', async () => {
  const outcome = await run({ login: 'Reefact', ref: 'feature', body: 'A change.' });
  assert.equal(outcome.failed, null);
  assert.equal(outcome.updatedBody, null);
});

test('a claude/* branch, though opened by the Project Owner, needs the ticked box', async () => {
  await passes({ login: 'Reefact', ref: 'claude/some-branch' });
  await fails({ login: 'Reefact', ref: 'claude/some-branch', html: untickedHtml });
});

test('an outside pull request passes with the box ticked, and only then', async () => {
  await passes({});
  await fails({ html: untickedHtml });
  await fails({ html: '' });
});

test('a description that cannot be read as rendered counts as not ticked', async () => {
  await fails({ html: new Error('unavailable') });
});

test('the clause GitHub renders as code, in a fence it does not close, counts for nothing', async () => {
  // ~~~text / ~~~still-code / the clause / ~~~ : GFM takes no closing fence with an info string.
  await fails({ html: `<div class="highlight highlight-text"><pre><code>~~~still-code\n${clauseMarkdown}\n</code></pre></div>` });
});

test('the clause in a raw <pre>, or as plain text, counts for nothing', async () => {
  await fails({ html: `<pre>\n${clauseMarkdown}\n</pre>` });
  await fails({ html: `<p dir="auto">${clauseMarkdown}</p>` });
});

test('a ticked box saying something else, or more, or linking elsewhere, counts for nothing', async () => {
  await fails({ html: tickedWith('I accept it', 'I do not accept it') });
  await fails({ html: tickedWith('for this contribution.', 'for this contribution, except section 3.') });
  await fails({ html: tickedWith(`href="${agreementUrl}"`, 'href="https://example.com/agreement"') });
});

test('a checked input that is not a task list checkbox counts for nothing', async () => {
  await fails({ html: tickedWith('class="task-list-item-checkbox" ', '') });
});

test("a Dependabot pull request at its own GitHub-signed head needs no box", async () => {
  await passes({ login: 'dependabot[bot]', ref: 'dependabot/nuget/x', body: 'Bumps a package.', html: '<p>Bumps a package.</p>' });
});

test('a Dependabot pull request at any other head is refused, ticked box or not', async () => {
  const dependabot = { login: 'dependabot[bot]', ref: 'dependabot/nuget/x', body: 'Bumps a package.' };
  for (const head of [
    { ...signedByGitHub, verified: false, reason: 'unsigned' },
    { ...signedByGitHub, author: 'mallory', committer: 'mallory' },
    // GitHub verifies against the committer: Dependabot's address as author, someone else's key.
    { ...signedByGitHub, committer: 'mallory' },
    { ...signedByGitHub, reason: 'unknown_key' },
    new Error('unreadable'),
  ]) {
    await fails({ ...dependabot, head, html: '<p>Bumps a package.</p>' });
    // The description is the bot's: a box ticked in it binds nobody (DEC0030).
    await fails({ ...dependabot, head, html: tickedHtml });
  }
});
