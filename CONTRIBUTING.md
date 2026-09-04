# Contributing

Changes reach `main` through a short-lived branch and a pull request.

## Workflow

1. Update your local `main` branch:

   ```powershell
   git switch main
   git pull --ff-only
   ```

2. Create a branch with a clear purpose:

   ```powershell
   git switch -c feature/short-description
   ```

   Use `feature/` for new behavior, `fix/` for bug fixes, `chore/` for maintenance, and `docs/` for documentation.

3. Make and validate the change:

   ```powershell
   dotnet build
   git status
   git diff
   ```

4. Commit a focused unit of work:

   ```powershell
   git add <files>
   git commit -m "Describe the change"
   ```

5. Push the branch and open a pull request into `main`:

   ```powershell
   git push -u origin HEAD
   ```

6. Wait for CI to pass, address review comments, and merge the pull request. Delete the remote branch after merging.

7. Refresh your local repository:

   ```powershell
   git switch main
   git pull --ff-only
   git branch -d <branch-name>
   ```

## Pull requests

Keep each pull request focused on one change. Explain the behavior, list the validation commands, mention schema or configuration changes, and include screenshots for UI changes. Do not merge when required checks are failing.

Prefer **Squash and merge** so each pull request becomes one clear commit on `main`.

## Automated review

Every pull request must pass the `Build` check. When the pull request author has an eligible Copilot plan, including Copilot Student, GitHub Copilot also reviews new pull requests and new pushes using the repository guidance in `.github/copilot-instructions.md` and `AGENTS.md`.

CI and AI review have different roles: CI gives a repeatable pass or fail result, while AI review suggests possible problems that still require developer judgment. Resolve or answer each useful review comment before merging.
