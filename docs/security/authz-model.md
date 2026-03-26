# Authentication And Authorization Model

## Purpose

BeeTracker uses a single authentication and authorization model for the admin plane and keeps tracker runtime rights isolated from the announce and scrape hot path.

## Boundaries

### Identity

Identity answers who the user is and how the user signs in.

- ASP.NET Identity stores admin users and credentials.
- OpenIddict issues admin tokens.
- Cookie sessions and OpenIddict tokens are built from the same principal builder.
- Identity claims contain subject and profile data such as `sub`, `name`, and `email`.

### Admin RBAC

Admin RBAC answers what an authenticated admin may do inside the admin UI and admin API.

- Canonical permissions live in `BeeTracker.Contracts.Identity.AdminPermissionCatalog`.
- Roles, permission groups, direct grants, and effective permission resolution live in `Identity.SelfService`.
- Backend policies are permission-based, not role-based.
- Frontend guards, menu visibility, and action visibility use the same permission keys returned by the admin session contract.

### Tracker Operational Rights

Tracker protocol rights are a separate domain model.

- They control tracker-facing behavior such as passkey access, bans, or tracker policy outcomes.
- They must not depend on admin UI session claims or EF-based RBAC resolution inside the announce hot path.
- Runtime announce and scrape execution remains node-local and allocation-aware.
- The canonical tracker access contract is `TrackerAccessRightsDto` in `BeeTracker.Contracts.Configuration`.
- Ban scope names for tracker enforcement are centralized in `TrackerBanScopes` instead of scattered string literals.

## Effective Permission Resolution

The effective admin permission set for a user is the union of:

1. direct permissions assigned to the user, when supported
2. direct permissions assigned to the user's roles
3. permissions inherited through permission groups assigned to those roles

The resolved permission set is emitted as `permission` claims together with `admin_permission_snapshot_version`.

Tracker operational rights are resolved separately from configuration snapshots and are not part of admin RBAC claim issuance.

## Enforcement Rules

- HTTP endpoints use generated permission policies from the catalog.
- Application handlers enforce sensitive operations with RBAC guards in addition to HTTP policies.
- Frontend routes, pages, menu items, and privileged actions all consume the same effective permission list from `/api/admin/session`.
- System roles such as `SuperAdmin` are protected against destructive or self-lockout workflows.

## Cache And Session Invalidation

- RBAC mutations increment the global permission snapshot version.
- Admin sessions carrying an outdated snapshot are forced to reauthenticate.
- Bootstrap users are synchronized into the RBAC profile/account-state store during startup so identity data and RBAC data stay consistent.

## Migration Rule

- No new legacy coarse-grained claims may be introduced.
- Bootstrap configuration and tests must use `AdminPermissionCatalog` / `AdminPermissions` keys only.
- Compatibility bridges should be removed once all callers are migrated to fine-grained permissions.

## Tracker Access Deprecation Plan

BeeTracker is in the final migration stage from legacy `UserPermission*` contracts and `/permissions` endpoints to canonical `TrackerAccess*` contracts and `/tracker-access` endpoints.

### Phase 1

- Introduce canonical `TrackerAccess*` DTOs, requests, use-cases, and routes.
- Keep legacy contracts and routes as functional aliases.
- Update frontend and admin orchestration to prefer canonical names.

### Phase 2

- Mark legacy `UserPermission*` contracts as obsolete in code.
- Mark legacy `/permissions` routes as deprecated and emit `Deprecation`, `Sunset`, and `Link` headers pointing at canonical routes.
- Keep response compatibility fields such as `permissionItems`, but prefer canonical `trackerAccessItems`.

### Phase 3

- Update all remaining internal or external callers to canonical `TrackerAccess*` contracts only.
- Remove legacy `/permissions` aliases from admin and configuration APIs.
- Remove `UserPermission*` compatibility DTOs and conversion helpers.

## Rollout Recommendation

For production rollout, prefer this order:

1. Deploy the deprecation release and monitor logs for continued `/permissions` traffic.
2. Update external clients and scripts to `/tracker-access`.
3. Add a release note with the sunset date and canonical replacement paths.
4. Remove compatibility aliases only after traffic has dropped to zero for at least one release cycle.
