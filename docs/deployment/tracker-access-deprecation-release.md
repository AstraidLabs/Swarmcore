# Tracker Access Deprecation Release

## Purpose

This release deprecates the legacy tracker access aliases based on `UserPermission*` contracts and `/permissions` routes.

Canonical replacements:

- `/api/admin/tracker-access`
- `/api/admin/users/{userId}/tracker-access`
- `/api/admin/users/bulk/tracker-access`
- `/api/configuration/users/{userId}/tracker-access`

Legacy aliases remain functional in this release, but they are now deprecated.

## Runtime Signals

Legacy alias usage now emits:

- HTTP headers:
  - `Deprecation: true`
  - `Sunset: Wed, 30 Sep 2026 00:00:00 GMT`
  - `Link: <canonical-route>; rel="successor-version"`
- warning logs from:
  - `BeeTracker.Admin.DeprecatedApiAlias`
  - `BeeTracker.Configuration.DeprecatedApiAlias`
- metrics:
  - `tracker.admin.legacy_tracker_access_alias.hit`
  - `tracker.configuration.legacy_tracker_access_alias.hit`
  - `tracker.compatibility.warning_issued`

## Rollout Checklist

1. Deploy this release.
2. Monitor legacy alias metrics and logs for at least one full release cycle.
3. Update every external client, script, or integration to `/tracker-access`.
4. Treat any remaining `/permissions` traffic as migration debt.
5. In the next release, mark legacy contracts as removal candidates in release notes.
6. Remove aliases only after legacy traffic reaches zero and stays there for one stable release.

## Operator Query Hints

Recommended checks depend on your telemetry backend, but the core questions are:

1. Are there any hits on `tracker.admin.legacy_tracker_access_alias.hit`?
2. Are there any hits on `tracker.configuration.legacy_tracker_access_alias.hit`?
3. Which legacy routes are still used most frequently?
4. Which callers are still triggering `BeeTracker.*.DeprecatedApiAlias` warnings?

## Removal Gate

Do not remove legacy aliases until all of the following are true:

- no repo-local callers remain
- no external clients are known to depend on `/permissions`
- metrics show zero legacy hits for one stable release
- the removal is called out in release notes ahead of time
