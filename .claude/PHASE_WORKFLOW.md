# Phase Development Workflow

Every phase of ValheimServerGuide development follows this cycle in order.
Do not skip a step or move to the next phase until the current step is complete.

---

## Step 1 — Build Phase

Implement everything in the phase criteria file.

- Read the relevant `CRIT-XX` or `hearthbound/phase-XX` file before writing any code.
- Make all code changes across the files listed in the criteria's **Files Changed** table.
- Run `dotnet build src/ValheimServerGuide.csproj -c Release --nologo -v q` and fix every error and warning before proceeding.
- Do not add features, comments, or abstractions beyond what the criteria specifies.

---

## Step 2 — Show Result

After a clean build, post a concise summary:

- Which files changed and what each change does (one line per file).
- Any non-obvious implementation decisions made.
- The build output line (`Build succeeded. 0 Warning(s) 0 Error(s)`).

---

## Step 3 — Test Instructions + Write Test guidance.yaml

Write the test YAML **directly** to the r2modman test profile config file:

```
C:\Users\yesu0725\AppData\Roaming\r2modmanPlus-local\Valheim\profiles\Mod Test Profile\BepInEx\config\ValheimServerGuide\guidance.yaml
```

- Read the existing file first, then overwrite it with the full merged content (preserve any existing entries that are still relevant; replace or append the new test entries).
- The YAML must be complete and runnable — valid IDs, real trigger types, real item/creature names from Valheim.
- Cover every criterion checkbox in the phase with at least one entry or action.

After writing the file, provide:

- A `## Test Instructions` section with numbered in-game steps.
- A mapping of which step covers which criterion checkbox.

---

## Step 4 — Debug and Fix

After the user tests in-game and reports issues:

- Reproduce the issue from the reported steps.
- Fix the root cause — do not add workarounds or guards for scenarios that can't happen.
- Rebuild and confirm `0 Warning(s) 0 Error(s)`.
- Repeat Steps 2–4 until all criteria checkboxes pass.

---

## Step 5 — Update MD Files

Once all criteria pass:

1. **Phase criteria file** — change `**Status:** \`pending\`` to `**Status:** \`done\`` and tick every `- [ ]` to `- [x]`.
2. **CLAUDE.md** — no change needed unless the phase introduced a new key invariant or source file.
3. **Memory** — if anything non-obvious was learned (a Unity quirk, a Valheim API gotcha, a design decision), write a `feedback` or `project` memory entry.

---

## Step 6 — Next Phase Build

State clearly: *"Phase XX complete. Ready for phase YY — start YYX."*
Wait for the user to confirm before beginning the next phase.
