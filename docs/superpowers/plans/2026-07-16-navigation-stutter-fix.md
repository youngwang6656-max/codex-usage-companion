# Codex Navigation Stutter Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop rolling rate-limit notifications from launching redundant account and rate-limit RPC reads during Codex page navigation.

**Architecture:** Preserve the long-lived official app-server session, but forward the `account/rateLimits/updated` payload through the session boundary. `CodexUsageClient` parses and merges the sparse notification into its last snapshot under the existing refresh lock; full reads remain limited to startup, the 60-second timer, genuine restore/refocus, and manual refresh.

**Tech Stack:** .NET 8, C# 12, WPF, xUnit v3, Codex app-server JSON-RPC.

## Global Constraints

- Preserve the current UI, collapsed-button placement, full-screen integration, restore behavior, and 60 FPS move-follow path.
- Preserve manual refresh, 60-second polling, and immediate refresh after a genuine restore or foreground return.
- Sparse notifications must not clear existing plan, reset time, or available reset count when those values are absent.
- Do not add dependencies, screenshot capture, UI Automation, or new settings.
- Use test-first red/green verification and keep Release builds warning-free.

---

### Task 1: Reproduce Notification Refetching

**Files:**
- Modify: `tests/CodexUsageCompanion.Tests/CodexAppServerProtocolSessionTests.cs`
- Modify: `tests/CodexUsageCompanion.Tests/CodexUsageClientTests.cs`

**Interfaces:**
- Consumes: `ICodexAppServerSession.RateLimitsUpdated`, `JsonRpcNotification.Params`.
- Produces: regression coverage proving notification payload preservation and zero refetches.

- [ ] **Step 1: Add failing protocol and client tests**

Change the fake session event to `EventHandler<string>?`. Assert that a protocol notification forwards its raw parameter JSON. Replace `RateLimitNotification_RefetchesCompleteSnapshot` with a test that raises a sparse rolling notification, waits for `RemainingPercent == 40`, and asserts both `AccountReads` and `RateLimitReads` remain at one while the old plan and reset count are preserved.

```csharp
Assert.Equal(1, session.AccountReads);
Assert.Equal(1, session.RateLimitReads);
Assert.Equal("Plus", result.PlanLabel);
Assert.Equal(40, result.RemainingPercent);
Assert.Equal(1, result.AvailableResetCount);
```

- [ ] **Step 2: Run focused tests and verify RED**

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" test .\tests\CodexUsageCompanion.Tests\CodexUsageCompanion.Tests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~CodexUsageClientTests|FullyQualifiedName~CodexAppServerProtocolSessionTests" --nologo
```

Expected: compilation or assertion failure because the current event discards notification parameters and refetches the complete snapshot.

### Task 2: Forward and Merge Sparse Notifications

**Files:**
- Modify: `src/CodexUsageCompanion.Core/Protocol/ICodexAppServerSession.cs`
- Modify: `src/CodexUsageCompanion.Core/Protocol/CodexAppServerProtocolSession.cs`
- Modify: `src/CodexUsageCompanion.Core/Protocol/StdioCodexAppServerSession.cs`
- Modify: `src/CodexUsageCompanion.Core/Usage/CodexUsageClient.cs`
- Test: `tests/CodexUsageCompanion.Tests/CodexUsageClientTests.cs`
- Test: `tests/CodexUsageCompanion.Tests/CodexAppServerProtocolSessionTests.cs`

**Interfaces:**
- Produces: `event EventHandler<string>? RateLimitsUpdated` containing cloned raw `params` JSON.
- Consumes: `CodexProtocolParser.ParseRateLimits(string)` and the current `UsageSnapshot`.

- [ ] **Step 1: Propagate notification JSON through protocol and stdio sessions**

For `account/rateLimits/updated`, require non-null parameters and invoke the event with `notification.Params.Value.GetRawText()`. Forward the same string through `StdioCodexAppServerSession`.

- [ ] **Step 2: Merge under the refresh lock without RPC calls**

Parse the payload, project its weekly window, and merge each nullable field with `CurrentSnapshot`. Preserve existing values when the sparse update omits them. Catch `ProtocolException` and ignore malformed notifications; do not mark a healthy cached snapshot offline.

```csharp
UsageSnapshot projected = UsageProjection.Create(
    limits.PlanType ?? _notifiedPlanType,
    limits.Windows,
    limits.AvailableResetCount,
    _clock.Now);
UsageSnapshot merged = projected with
{
    PlanLabel = projected.PlanLabel ?? CurrentSnapshot.PlanLabel,
    RemainingPercent = projected.RemainingPercent ?? CurrentSnapshot.RemainingPercent,
    ResetsAt = projected.ResetsAt ?? CurrentSnapshot.ResetsAt,
    AvailableResetCount = projected.AvailableResetCount ?? CurrentSnapshot.AvailableResetCount,
};
```

- [ ] **Step 3: Run focused tests and verify GREEN**

Run the Task 1 command. Expected: all selected tests pass.

### Task 3: Release and Live Verification

**Files:**
- Generated and ignored: `publish/win-x64/**`
- Update: `docs/superpowers/specs/2026-07-16-navigation-stutter-design.md`
- Update: `docs/superpowers/plans/2026-07-16-navigation-stutter-fix.md`

**Interfaces:**
- Consumes: `installer/Build.ps1`, `installer/Install.ps1`, Git remote `origin`.
- Produces: updated current-user installation and reviewed GitHub change.

- [ ] **Step 1: Run full Release verification**

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" test .\CodexUsageCompanion.sln --configuration Release --no-restore --nologo
& "$env:USERPROFILE\.dotnet\dotnet.exe" build .\CodexUsageCompanion.sln --configuration Release --no-restore --nologo
```

- [ ] **Step 2: Build, install, and verify hashes**

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\installer\Build.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\installer\Install.ps1
```

- [ ] **Step 3: Repeat the live page-navigation I/O test**

Expected: rolling notifications update the visible snapshot without the additional `account/read` and `account/rateLimits/read` request/response burst; subjective page switching matches the component-off baseline more closely.

- [ ] **Step 4: Review, commit, push, and open a GitHub pull request**

```powershell
git diff --check
git status -sb
git add src tests docs/superpowers
git commit -m "Fix rate-limit notification refresh stutter"
git push -u origin codex/fix-navigation-stutter
```

Open a pull request to `main` describing the discarded-notification-payload root cause, sparse merge behavior, tests, Release build, and live verification.
