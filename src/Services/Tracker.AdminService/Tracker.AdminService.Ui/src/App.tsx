import { type ReactNode, useEffect, useMemo, useRef, useState } from "react";
import {
  Link,
  NavLink,
  Navigate,
  Route,
  Routes,
  useLocation,
  useNavigate,
  useParams
} from "react-router-dom";
import { User, UserManager, WebStorageStateStore } from "oidc-client-ts";
import { type I18nDictionary, supportedLocales, useI18n } from "./i18n";
import {
  AdminUserEditorPage,
  AdminUsersPage,
  PermissionGate,
  PermissionGroupEditorPage,
  PermissionGroupsPage,
  ProfilePage,
  RoleEditorPage,
  RolesPage,
  hasPermission,
  permissionKeys
} from "./rbac";
import { buildGridQueryString, type CatalogQueryState, type PageResult } from "./catalog";
import { CatalogTableRow, CatalogToolbar, ConfirmActionModal, CopyValueButton, Modal, ModalDismissButton, PaginationFooter, PreviewDrawer, RowActionsMenu, SortHeaderButton, TableStateRow, useCatalogViewState } from "./catalog.tsx";

type AdminUiConfig = {
  authority: string;
  clientId: string;
  redirectUri: string;
  postLogoutRedirectUri: string;
  scope: string;
  responseType: string;
};

type AdminSessionResponse = {
  isAuthenticated: boolean;
  userName: string;
  role: string;
  permissions: string[];
  capabilities: CapabilityDto[];
};

type ClusterOverviewDto = {
  observedAtUtc: string;
  activeNodeCount: number;
  nodes: Array<{ nodeId: string; region: string; ready: boolean; observedAtUtc: string }>;
};

type TorrentAdminDto = {
  infoHash: string;
  isPrivate: boolean;
  isEnabled: boolean;
  announceIntervalSeconds: number;
  minAnnounceIntervalSeconds: number;
  defaultNumWant: number;
  maxNumWant: number;
  allowScrape: boolean;
  version: number;
};

type PasskeyAdminDto = {
  passkeyMask: string;
  userId: string;
  isRevoked: boolean;
  expiresAtUtc?: string | null;
  version: number;
};

type BanRuleAdminDto = {
  scope: string;
  subject: string;
  reason: string;
  expiresAtUtc?: string | null;
  version: number;
};

type TrackerAccessAdminDto = {
  userId: string;
  canLeech: boolean;
  canSeed: boolean;
  canScrape: boolean;
  canUsePrivateTracker: boolean;
  version: number;
};

type AuditRecordDto = {
  id: string;
  occurredAtUtc: string;
  actorId: string;
  actorRole: string;
  action: string;
  severity: string;
  entityType: string;
  entityId: string;
  correlationId: string;
  result: string;
  ipAddress?: string | null;
};

type MaintenanceRunDto = {
  id: string;
  operation: string;
  requestedBy: string;
  requestedAtUtc: string;
  status: string;
  correlationId: string;
};

type CapabilityDto = {
  action: string;
  permission: string;
  displayName: string;
  category: string;
  confirmationSeverity: string;
  routePattern: string;
  granted: boolean;
};

type BulkDryRunResultDto = {
  totalCount: number;
  applicableCount: number;
  rejectedCount: number;
  torrentPolicyItems: TorrentPolicyDryRunItemDto[];
};

type TorrentPolicyDryRunItemDto = {
  infoHash: string;
  canApply: boolean;
  errorCode?: string | null;
  errorMessage?: string | null;
  currentSnapshot?: TorrentAdminDto | null;
  proposedSnapshot: TorrentAdminDto;
  warnings: string[];
};

type BulkOperationResultDto = {
  totalCount: number;
  succeededCount: number;
  failedCount: number;
  passkeyItems: BulkPasskeyOperationItemDto[];
  permissionItems: BulkTrackerAccessOperationItemDto[];
  trackerAccessItems?: BulkTrackerAccessOperationItemDto[] | null;
  torrentItems: BulkTorrentOperationItemDto[];
  banItems: BulkBanOperationItemDto[];
};

type BulkPasskeyOperationItemDto = {
  passkeyMask: string;
  succeeded: boolean;
  errorCode?: string | null;
  errorMessage?: string | null;
  snapshot?: PasskeyAdminDto | null;
  newPasskey?: string | null;
  newPasskeyMask?: string | null;
};

type BulkBanOperationItemDto = {
  scope: string;
  subject: string;
  succeeded: boolean;
  errorCode?: string | null;
  errorMessage?: string | null;
  snapshot?: BanRuleAdminDto | null;
};

type BulkTrackerAccessOperationItemDto = {
  userId: string;
  succeeded: boolean;
  errorCode?: string | null;
  errorMessage?: string | null;
  snapshot?: TrackerAccessAdminDto | null;
};

type BulkTorrentOperationItemDto = {
  infoHash: string;
  succeeded: boolean;
  errorCode?: string | null;
  errorMessage?: string | null;
  snapshot?: TorrentAdminDto | null;
};

type AdminApiError = {
  code?: string;
  message?: string;
};

type TorrentPolicyFormState = {
  isPrivate: boolean;
  isEnabled: boolean;
  announceIntervalSeconds: number;
  minAnnounceIntervalSeconds: number;
  defaultNumWant: number;
  maxNumWant: number;
  allowScrape: boolean;
  expectedVersion: number;
};

type BanFormState = {
  scope: string;
  subject: string;
  reason: string;
  expiresAtLocal: string;
  expectedVersion?: number;
};

type TrackerAccessFormState = {
  userId: string;
  canLeech: boolean;
  canSeed: boolean;
  canScrape: boolean;
  canUsePrivateTracker: boolean;
  expectedVersion?: number;
};

type BulkTorrentPolicySelectionItem = {
  infoHash: string;
  version: number;
  isPrivate: boolean;
  isEnabled: boolean;
  announceIntervalSeconds: number;
  minAnnounceIntervalSeconds: number;
  defaultNumWant: number;
  maxNumWant: number;
  allowScrape: boolean;
};

type NavigationBannerState = {
  message: string;
  tone: "good" | "warn";
};

type NavigationLink = {
  to: string;
  label: string;
  description: string;
  icon: "overview" | "users" | "roles" | "groups" | "torrents" | "passkeys" | "trackerAccess" | "bans" | "audit" | "maintenance";
};

type NavigationSection = {
  id: string;
  label: string;
  to: string;
  icon: NavigationLink["icon"];
  links: NavigationLink[];
};

const bulkTorrentPolicySelectionStorageKey = "beetracker.admin.bulkPolicySelection";
const adminSignedOutStorageKey = "beetracker.admin.signedout";

function toTitleCase(value: string): string {
  return value
    .replaceAll("_", " ")
    .replaceAll(".", " ")
    .replace(/\b\w/g, (match) => match.toUpperCase());
}

function formatText(template: string, replacements: Record<string, string | number>): string {
  return Object.entries(replacements).reduce(
    (value, [key, replacement]) => value.replaceAll(`{${key}}`, String(replacement)),
    template
  );
}

function formatMode(isPrivate: boolean, dictionary: I18nDictionary): string {
  return isPrivate ? dictionary.common.privateMode : dictionary.common.publicMode;
}

function formatState(enabled: boolean, dictionary: I18nDictionary): string {
  return enabled ? dictionary.common.enabled : dictionary.common.disabled;
}

function formatScrape(allowed: boolean, dictionary: I18nDictionary): string {
  return allowed ? dictionary.common.allowed : dictionary.common.disabled;
}

function formatBool(value: boolean, dictionary: I18nDictionary): string {
  return value ? dictionary.common.yes : dictionary.common.no;
}

function toBanRecordId(scope: string, subject: string) {
  return `${encodeURIComponent(scope)}::${encodeURIComponent(subject)}`;
}

function tryParseBanRecordId(value: string | null) {
  if (!value) {
    return null;
  }

  const separatorIndex = value.indexOf("::");
  if (separatorIndex <= 0 || separatorIndex >= value.length - 2) {
    return null;
  }

  try {
    return {
      scope: decodeURIComponent(value.slice(0, separatorIndex)),
      subject: decodeURIComponent(value.slice(separatorIndex + 2))
    };
  } catch {
    return null;
  }
}

function NavigationItemIcon({ icon }: { icon: NavigationLink["icon"] }) {
  switch (icon) {
    case "overview":
      return (
        <svg viewBox="0 0 24 24" className="h-4 w-4 fill-none stroke-current" strokeWidth="1.8">
          <path d="M3 11.5 12 4l9 7.5" />
          <path d="M5.5 10.5V20h13V10.5" />
        </svg>
      );
    case "users":
      return (
        <svg viewBox="0 0 24 24" className="h-4 w-4 fill-none stroke-current" strokeWidth="1.8">
          <path d="M16 19v-1a4 4 0 0 0-4-4H7a4 4 0 0 0-4 4v1" />
          <circle cx="9.5" cy="7.5" r="3.5" />
          <path d="M17 10.5a3 3 0 1 0 0-6" />
          <path d="M21 19v-1a4 4 0 0 0-3-3.87" />
        </svg>
      );
    case "roles":
      return (
        <svg viewBox="0 0 24 24" className="h-4 w-4 fill-none stroke-current" strokeWidth="1.8">
          <path d="m12 3 7 4v5c0 4.5-3 7.5-7 9-4-1.5-7-4.5-7-9V7l7-4Z" />
          <path d="m9.5 12 1.7 1.7 3.3-3.7" />
        </svg>
      );
    case "groups":
      return (
        <svg viewBox="0 0 24 24" className="h-4 w-4 fill-none stroke-current" strokeWidth="1.8">
          <rect x="3" y="5" width="7" height="6" rx="1.5" />
          <rect x="14" y="5" width="7" height="6" rx="1.5" />
          <rect x="8.5" y="14" width="7" height="6" rx="1.5" />
        </svg>
      );
    case "torrents":
      return (
        <svg viewBox="0 0 24 24" className="h-4 w-4 fill-none stroke-current" strokeWidth="1.8">
          <path d="M12 4v10" />
          <path d="m8 10 4 4 4-4" />
          <rect x="4" y="16" width="16" height="4" rx="2" />
        </svg>
      );
    case "passkeys":
      return (
        <svg viewBox="0 0 24 24" className="h-4 w-4 fill-none stroke-current" strokeWidth="1.8">
          <circle cx="8.5" cy="11.5" r="3.5" />
          <path d="M12 11.5h8" />
          <path d="M17 11.5v3" />
          <path d="M20 11.5v2" />
        </svg>
      );
    case "trackerAccess":
      return (
        <svg viewBox="0 0 24 24" className="h-4 w-4 fill-none stroke-current" strokeWidth="1.8">
          <path d="M4 12h16" />
          <path d="M12 4v16" />
          <circle cx="12" cy="12" r="8" />
        </svg>
      );
    case "bans":
      return (
        <svg viewBox="0 0 24 24" className="h-4 w-4 fill-none stroke-current" strokeWidth="1.8">
          <circle cx="12" cy="12" r="8" />
          <path d="m8.5 15.5 7-7" />
        </svg>
      );
    case "audit":
      return (
        <svg viewBox="0 0 24 24" className="h-4 w-4 fill-none stroke-current" strokeWidth="1.8">
          <path d="M8 6h11" />
          <path d="M8 12h11" />
          <path d="M8 18h11" />
          <path d="M4 6h.01" />
          <path d="M4 12h.01" />
          <path d="M4 18h.01" />
        </svg>
      );
    case "maintenance":
      return (
        <svg viewBox="0 0 24 24" className="h-4 w-4 fill-none stroke-current" strokeWidth="1.8">
          <circle cx="8" cy="16" r="2.5" />
          <circle cx="16" cy="8" r="2.5" />
          <path d="m9.8 14.2 4.4-4.4" />
          <path d="M6.5 10.5 4 8l4-4 2.5 2.5" />
          <path d="M13.5 19.5 16 22l4-4-2.5-2.5" />
        </svg>
      );
  }
}

async function loadUiConfig(): Promise<AdminUiConfig> {
  const response = await fetch("/admin-ui/config", { credentials: "include" });
  if (!response.ok) {
    throw new Error(`Unable to load admin UI configuration (${response.status}).`);
  }

  return response.json() as Promise<AdminUiConfig>;
}

function createUserManager(config: AdminUiConfig): UserManager {
  return new UserManager({
    authority: config.authority,
    client_id: config.clientId,
    redirect_uri: config.redirectUri,
    post_logout_redirect_uri: config.postLogoutRedirectUri,
    response_type: config.responseType,
    scope: config.scope,
    automaticSilentRenew: false,
    loadUserInfo: false,
    userStore: new WebStorageStateStore({ store: window.localStorage })
  });
}

function useAdminOidc() {
  const [manager, setManager] = useState<UserManager | null>(null);
  const [user, setUser] = useState<User | null>(null);
  const [isBootstrapping, setIsBootstrapping] = useState(true);
  const [bootError, setBootError] = useState<string | null>(null);
  const location = useLocation();

  useEffect(() => {
    let isMounted = true;

    const bootstrap = async () => {
      try {
        const loadedConfig = await loadUiConfig();
        const loadedManager = createUserManager(loadedConfig);
        const existingUser = await loadedManager.getUser();

        if (!isMounted) {
          return;
        }

        setManager(loadedManager);
        setUser(existingUser && !existingUser.expired ? existingUser : null);
        setIsBootstrapping(false);
      } catch (error) {
        if (!isMounted) {
          return;
        }

        setBootError(error instanceof Error ? error.message : "Admin UI bootstrap failed.");
        setIsBootstrapping(false);
      }
    };

    void bootstrap();

    return () => {
      isMounted = false;
    };
  }, []);

  const signin = async (forceFreshLogin = false) => {
    if (!manager) {
      return;
    }

    if (typeof window !== "undefined") {
      window.sessionStorage.removeItem(adminSignedOutStorageKey);
    }

    await manager.signinRedirect({
      state: {
        returnTo: `${location.pathname}${location.search}${location.hash}`
      },
      extraQueryParams: forceFreshLogin ? { prompt: "login" } : undefined
    });
  };

  const signout = async () => {
    if (manager) {
      await manager.removeUser();
    }

    if (typeof window !== "undefined") {
      window.sessionStorage.setItem(adminSignedOutStorageKey, "1");
    }

    await fetch("/account/logout", {
      method: "POST",
      credentials: "include",
      headers: {
        "Content-Type": "application/x-www-form-urlencoded"
      },
      body: new URLSearchParams({ returnUrl: "/" })
    });

    setUser(null);
    window.location.assign("/");
  };

  return { manager, user, setUser, isBootstrapping, bootError, signin, signout };
}

async function readAdminError(response: Response): Promise<AdminApiError> {
  return (await response.json().catch(() => ({}))) as AdminApiError;
}

async function apiRequest<T>(path: string, accessToken: string, onReauthenticate: (fresh: boolean) => Promise<void>): Promise<T> {
  const response = await fetch(path, {
    headers: {
      Authorization: `Bearer ${accessToken}`
    }
  });

  if (response.status === 401) {
    await onReauthenticate(false);
    throw new Error("Admin authentication is required.");
  }

  if (response.status === 403) {
    const errorPayload = await readAdminError(response);
    if (errorPayload.code === "admin_reauthentication_required") {
      await onReauthenticate(true);
      throw new Error("Recent authentication is required.");
    }

    throw new Error(errorPayload.message ?? "Admin access is forbidden.");
  }

  if (!response.ok) {
    const errorPayload = await readAdminError(response);
    throw new Error(errorPayload.message ?? `Admin request failed (${response.status}).`);
  }

  return response.json() as Promise<T>;
}

async function apiMutation<TResponse, TRequest>(
  path: string,
  method: "POST" | "PUT",
  accessToken: string,
  payload: TRequest,
  onReauthenticate: (fresh: boolean) => Promise<void>
): Promise<TResponse> {
  const response = await fetch(path, {
    method,
    headers: {
      Authorization: `Bearer ${accessToken}`,
      "Content-Type": "application/json"
    },
    body: JSON.stringify(payload)
  });

  if (response.status === 401) {
    await onReauthenticate(false);
    throw new Error("Admin authentication is required.");
  }

  if (response.status === 403) {
    const errorPayload = await readAdminError(response);
    if (errorPayload.code === "admin_reauthentication_required") {
      await onReauthenticate(true);
      throw new Error("Recent authentication is required.");
    }

    throw new Error(errorPayload.message ?? "Admin mutation is forbidden.");
  }

  if (!response.ok) {
    const errorPayload = await readAdminError(response);
    throw new Error(errorPayload.message ?? `Admin mutation failed (${response.status}).`);
  }

  return response.json() as Promise<TResponse>;
}

function useAdminSession(accessToken: string, onReauthenticate: (fresh: boolean) => Promise<void>) {
  const [session, setSession] = useState<AdminSessionResponse | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let isMounted = true;

    apiRequest<AdminSessionResponse>("/api/admin/session", accessToken, onReauthenticate)
      .then((value) => {
        if (isMounted) {
          setSession(value);
          setError(null);
        }
      })
      .catch((requestError) => {
        if (isMounted) {
          setError(requestError instanceof Error ? requestError.message : "Unable to load admin session.");
        }
      });

    return () => {
      isMounted = false;
    };
  }, [accessToken, onReauthenticate]);

  return { session, error };
}

function hasGrantedCapability(capabilities: CapabilityDto[], action: string): boolean {
  return capabilities.some((capability) => capability.action === action && capability.granted);
}

function toPolicyForm(snapshot: TorrentAdminDto): TorrentPolicyFormState {
  return {
    isPrivate: snapshot.isPrivate,
    isEnabled: snapshot.isEnabled,
    announceIntervalSeconds: snapshot.announceIntervalSeconds,
    minAnnounceIntervalSeconds: snapshot.minAnnounceIntervalSeconds,
    defaultNumWant: snapshot.defaultNumWant,
    maxNumWant: snapshot.maxNumWant,
    allowScrape: snapshot.allowScrape,
    expectedVersion: snapshot.version
  };
}

function toTrackerAccessForm(snapshot: TrackerAccessAdminDto): TrackerAccessFormState {
  return {
    userId: snapshot.userId,
    canLeech: snapshot.canLeech,
    canSeed: snapshot.canSeed,
    canScrape: snapshot.canScrape,
    canUsePrivateTracker: snapshot.canUsePrivateTracker,
    expectedVersion: snapshot.version
  };
}

function parseLineSeparatedValues(raw: string): string[] {
  return raw
    .split(/\r?\n/)
    .map((value) => value.trim())
    .filter((value, index, values) => value.length > 0 && values.indexOf(value) === index);
}

function readBulkTorrentPolicySelection(): BulkTorrentPolicySelectionItem[] {
  if (typeof window === "undefined") {
    return [];
  }

  const raw = window.sessionStorage.getItem(bulkTorrentPolicySelectionStorageKey);
  if (!raw) {
    return [];
  }

  try {
    const parsed = JSON.parse(raw) as BulkTorrentPolicySelectionItem[];
    return Array.isArray(parsed) ? parsed : [];
  } catch {
    return [];
  }
}

function persistBulkTorrentPolicySelection(items: BulkTorrentPolicySelectionItem[]) {
  if (typeof window === "undefined") {
    return;
  }

  if (items.length === 0) {
    window.sessionStorage.removeItem(bulkTorrentPolicySelectionStorageKey);
    return;
  }

  window.sessionStorage.setItem(bulkTorrentPolicySelectionStorageKey, JSON.stringify(items));
}

function clearBulkTorrentPolicySelection() {
  if (typeof window === "undefined") {
    return;
  }

  window.sessionStorage.removeItem(bulkTorrentPolicySelectionStorageKey);
}

function toBulkTorrentPolicySelectionItem(snapshot: TorrentAdminDto): BulkTorrentPolicySelectionItem {
  return {
    infoHash: snapshot.infoHash,
    version: snapshot.version,
    isPrivate: snapshot.isPrivate,
    isEnabled: snapshot.isEnabled,
    announceIntervalSeconds: snapshot.announceIntervalSeconds,
    minAnnounceIntervalSeconds: snapshot.minAnnounceIntervalSeconds,
    defaultNumWant: snapshot.defaultNumWant,
    maxNumWant: snapshot.maxNumWant,
    allowScrape: snapshot.allowScrape
  };
}

function buildPolicyComparisonRows(currentSnapshot: TorrentAdminDto | null | undefined, proposedSnapshot: TorrentAdminDto, dictionary: I18nDictionary) {
  return [
    {
      label: dictionary.common.mode,
      current: currentSnapshot ? formatMode(currentSnapshot.isPrivate, dictionary) : "n/a",
      proposed: formatMode(proposedSnapshot.isPrivate, dictionary)
    },
    {
      label: dictionary.common.state,
      current: currentSnapshot ? formatState(currentSnapshot.isEnabled, dictionary) : "n/a",
      proposed: formatState(proposedSnapshot.isEnabled, dictionary)
    },
    {
      label: dictionary.policyEditor.announceInterval,
      current: currentSnapshot ? `${currentSnapshot.announceIntervalSeconds}s` : "n/a",
      proposed: `${proposedSnapshot.announceIntervalSeconds}s`
    },
    {
      label: dictionary.policyEditor.minAnnounceInterval,
      current: currentSnapshot ? `${currentSnapshot.minAnnounceIntervalSeconds}s` : "n/a",
      proposed: `${proposedSnapshot.minAnnounceIntervalSeconds}s`
    },
    {
      label: dictionary.policyEditor.defaultNumwant,
      current: currentSnapshot ? String(currentSnapshot.defaultNumWant) : "n/a",
      proposed: String(proposedSnapshot.defaultNumWant)
    },
    {
      label: dictionary.policyEditor.maxNumwant,
      current: currentSnapshot ? String(currentSnapshot.maxNumWant) : "n/a",
      proposed: String(proposedSnapshot.maxNumWant)
    },
    {
      label: dictionary.common.scrape,
      current: currentSnapshot ? formatScrape(currentSnapshot.allowScrape, dictionary) : "n/a",
      proposed: formatScrape(proposedSnapshot.allowScrape, dictionary)
    },
    {
      label: dictionary.common.version,
      current: currentSnapshot ? String(currentSnapshot.version) : "n/a",
      proposed: String(proposedSnapshot.version)
    }
  ];
}

function toLocalDateTimeInput(value?: string | null): string {
  if (!value) {
    return "";
  }

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return "";
  }

  return new Date(date.getTime() - date.getTimezoneOffset() * 60_000).toISOString().slice(0, 16);
}

function fromLocalDateTimeInput(value: string): string | null {
  if (!value.trim()) {
    return null;
  }

  return new Date(value).toISOString();
}

function StatusPill({ tone, children }: { tone: "neutral" | "good" | "warn"; children: string }) {
  const classes =
    tone === "good"
      ? "border border-moss/15 bg-moss/10 text-moss"
      : tone === "warn"
        ? "border border-rose-200 bg-rose-50 text-rose-700"
        : "border border-slate-200 bg-white text-steel";

  return <span className={`inline-flex items-center rounded-full px-3 py-1.5 text-[11px] font-semibold uppercase tracking-[0.18em] ${classes}`}>{children}</span>;
}

function Card({ title, eyebrow, children }: { title: string; eyebrow?: string; children: React.ReactNode }) {
  return (
    <section className="app-card">
      <div className="app-card-header">
        {eyebrow ? <p className="app-kicker">{eyebrow}</p> : null}
        <h2 className="mt-2 text-xl font-semibold text-ink">{title}</h2>
      </div>
      <div className="app-card-body">{children}</div>
    </section>
  );
}

function getRouteMeta(pathname: string, dictionary: I18nDictionary): { eyebrow: string; title: string; description: string } {
  const routes = dictionary.routes;
  if (pathname.startsWith("/profile")) {
    return { eyebrow: "RBAC", title: "Admin profile", description: "Update your admin profile and inspect effective permissions issued into the current session." };
  }
  if (pathname.startsWith("/admin-users")) {
    return { eyebrow: "RBAC", title: "Admin users", description: "Provision admin accounts, assign roles, and enforce account state transitions with auditability." };
  }
  if (pathname.startsWith("/roles")) {
    return { eyebrow: "RBAC", title: "Roles", description: "Manage system and custom roles, attach permission groups, and review effective permissions." };
  }
  if (pathname.startsWith("/permission-groups")) {
    return { eyebrow: "RBAC", title: "Permission groups", description: "Bundle permissions into reusable groups and attach them to roles." };
  }
  if (pathname.startsWith("/torrents/bulk-policy")) {
    return {
      eyebrow: routes.bulkPolicyEyebrow,
      title: routes.bulkPolicyTitle,
      description: routes.bulkPolicyDescription
    };
  }

  if (pathname.startsWith("/torrents/")) {
    return {
      eyebrow: routes.torrentPolicyEyebrow,
      title: routes.torrentPolicyTitle,
      description: routes.torrentPolicyDescription
    };
  }

  if (pathname.startsWith("/torrents")) {
    return {
      eyebrow: routes.torrentsEyebrow,
      title: routes.torrentsTitle,
      description: routes.torrentsDescription
    };
  }

  if (pathname.startsWith("/passkeys")) {
    return {
      eyebrow: routes.passkeysEyebrow,
      title: routes.passkeysTitle,
      description: routes.passkeysDescription
    };
  }

  if (pathname.startsWith("/permissions")) {
    return {
      eyebrow: routes.permissionsEyebrow,
      title: routes.permissionsTitle,
      description: routes.permissionsDescription
    };
  }

  if (pathname.startsWith("/bans")) {
    return {
      eyebrow: routes.bansEyebrow,
      title: routes.bansTitle,
      description: routes.bansDescription
    };
  }

  if (pathname.startsWith("/audit")) {
    return {
      eyebrow: routes.auditEyebrow,
      title: routes.auditTitle,
      description: routes.auditDescription
    };
  }
  if (pathname.startsWith("/maintenance")) {
    return {
      eyebrow: routes.maintenanceEyebrow,
      title: routes.maintenanceTitle,
      description: routes.maintenanceDescription
    };
  }

  return {
    eyebrow: routes.overviewEyebrow,
    title: routes.overviewTitle,
    description: routes.overviewDescription
  };
}

function CapabilityGate({
  capabilities,
  action,
  children
}: {
  capabilities: CapabilityDto[];
  action: string;
  children: React.ReactNode;
}) {
  const { dictionary } = useI18n();
  if (!hasGrantedCapability(capabilities, action)) {
    return (
      <Card title={dictionary.common.accessDenied} eyebrow={dictionary.common.authorization}>
        <p className="text-sm text-ember">{dictionary.common.accessDeniedBody}</p>
      </Card>
    );
  }

  return <>{children}</>;
}

function DashboardPage({
  accessToken,
  onReauthenticate,
  capabilities
}: {
  accessToken: string;
  onReauthenticate: (fresh: boolean) => Promise<void>;
  capabilities: CapabilityDto[];
}) {
  const { dictionary } = useI18n();
  const dashboard = dictionary.dashboard;
  const [overview, setOverview] = useState<ClusterOverviewDto | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let isMounted = true;

    apiRequest<ClusterOverviewDto>("/api/admin/cluster-overview", accessToken, onReauthenticate)
      .then((clusterOverview) => {
        if (isMounted) {
          setOverview(clusterOverview);
        }
      })
      .catch((requestError) => {
        if (isMounted) {
          setError(requestError instanceof Error ? requestError.message : dashboard.loadError);
        }
      });

    return () => {
      isMounted = false;
    };
  }, [accessToken, onReauthenticate]);

  if (error) {
    return <Card title={dictionary.routes.overviewTitle}><p className="text-sm text-ember">{error}</p></Card>;
  }

  if (!overview) {
    return <Card title={dictionary.routes.overviewTitle}><p className="text-sm text-ink/60">{dashboard.loading}</p></Card>;
  }

  const readyNodes = overview.nodes.filter((node) => node.ready).length;
  const degradedNodes = overview.nodes.length - readyNodes;
  const grantedCapabilities = capabilities.filter((capability) => capability.granted);
  const readinessPercent = overview.activeNodeCount > 0
    ? Math.round((readyNodes / overview.activeNodeCount) * 100)
    : 0;
  const capabilityCategories = Array.from(new Set(grantedCapabilities.map((capability) => capability.category)));

  return (
    <div className="space-y-6">
      <section className="grid gap-4 xl:grid-cols-3">
        <div className="app-stat-card">
          <p className="text-[11px] font-semibold uppercase tracking-[0.18em] text-steel/70">{dashboard.readinessTitle}</p>
          <div className="mt-4 flex items-end justify-between">
            <p className="app-stat-value">{readinessPercent}%</p>
            <span className="text-sm text-steel">{readyNodes}/{overview.activeNodeCount}</span>
          </div>
          <p className="mt-3 text-sm leading-6 text-steel">
            {degradedNodes > 0
              ? `${degradedNodes} ${dashboard.readinessNeedsAttention}`
              : dashboard.readinessReady}
          </p>
          <div className="app-stat-bar">
            <div className="app-stat-bar-fill bg-moss" style={{ width: `${readinessPercent}%` }} />
          </div>
        </div>
        <div className="app-stat-card">
          <p className="text-[11px] font-semibold uppercase tracking-[0.18em] text-steel/70">{dashboard.postureTitle}</p>
          <div className="mt-4 flex items-end justify-between">
            <p className="app-stat-value">{grantedCapabilities.length}</p>
            <span className="text-sm text-steel">{capabilityCategories.length} {dashboard.postureDomains}</span>
          </div>
          <p className="mt-3 text-sm leading-6 text-steel">
            Access posture is derived from the current effective permission model.
          </p>
          <div className="app-stat-bar">
            <div className="app-stat-bar-fill bg-amber-500" style={{ width: `${capabilities.length ? (grantedCapabilities.length / capabilities.length) * 100 : 0}%` }} />
          </div>
        </div>
        <div className="app-stat-card">
          <p className="text-[11px] font-semibold uppercase tracking-[0.18em] text-steel/70">{dashboard.observedSnapshot}</p>
          <div className="mt-4 flex items-end justify-between">
            <p className="text-2xl font-bold tracking-tight text-ink">{new Date(overview.observedAtUtc).toLocaleTimeString()}</p>
            <StatusPill tone={degradedNodes > 0 ? "warn" : "good"}>
              {degradedNodes > 0 ? dictionary.common.degraded : dictionary.common.ready}
            </StatusPill>
          </div>
          <p className="mt-3 text-sm leading-6 text-steel">
            {new Date(overview.observedAtUtc).toLocaleDateString()} · {overview.nodes.length} nodes in the current cluster snapshot.
          </p>
          <div className="mt-4 flex flex-wrap gap-2">
            {capabilityCategories.slice(0, 3).map((category) => (
              <span key={category} className="app-chip">{toTitleCase(category)}</span>
            ))}
          </div>
        </div>
      </section>
      <Card title={dashboard.readinessMapTitle} eyebrow={dashboard.operationsEyebrow}>
        <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
          {overview.nodes.map((node) => (
            <div key={node.nodeId} className="rounded-3xl border border-slate-200 bg-slate-50/80 px-5 py-4">
              <div className="flex items-start justify-between gap-4">
                <div>
                  <p className="text-sm font-semibold text-ink">{node.nodeId}</p>
                  <p className="mt-1 text-xs uppercase tracking-[0.18em] text-steel/70">{node.region}</p>
                </div>
                <StatusPill tone={node.ready ? "good" : "warn"}>{node.ready ? dictionary.common.ready : dictionary.common.degraded}</StatusPill>
              </div>
              <div className="mt-6 h-28 rounded-2xl bg-white px-4 py-4">
                <div className="flex h-full items-end gap-2">
                  {[30, 58, 42, 72, 38, 61, node.ready ? 68 : 24].map((value, index) => (
                    <div key={`${node.nodeId}-${index}`} className="flex-1 rounded-full bg-slate-200/90">
                      <div
                        className={`rounded-full ${index === 6 ? (node.ready ? "bg-brand" : "bg-ember") : "bg-slate-300"}`}
                        style={{ height: `${value}%` }}
                      />
                    </div>
                  ))}
                </div>
              </div>
              <p className="mt-4 text-sm text-steel">{dashboard.lastHeartbeat} {new Date(node.observedAtUtc).toLocaleTimeString()}</p>
            </div>
          ))}
        </div>
      </Card>
    </div>
  );
}

function TorrentsPage({
  accessToken,
  onReauthenticate,
  capabilities
}: {
  accessToken: string;
  onReauthenticate: (fresh: boolean) => Promise<void>;
  capabilities: CapabilityDto[];
}) {
  const { dictionary } = useI18n();
  const labels = dictionary.torrents;
  const navigate = useNavigate();
  const location = useLocation();
  const [view, setView] = useCatalogViewState({
    search: "",
    filter: "all",
    sort: "infohash:asc",
    page: 1,
    pageSize: 25
  });
  const { query, preview } = view;
  const [items, setItems] = useState<TorrentAdminDto[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [selectedInfoHashes, setSelectedInfoHashes] = useState<string[]>([]);
  const [status, setStatus] = useState<string | null>(null);
  const [result, setResult] = useState<BulkOperationResultDto | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [isLoading, setIsLoading] = useState(true);
  const canEditPolicy = hasGrantedCapability(capabilities, "admin.write.torrent_policy");
  const canActivate = hasGrantedCapability(capabilities, "admin.activate.torrent");
  const canDeactivate = hasGrantedCapability(capabilities, "admin.deactivate.torrent");
  const canBulkEditPolicy = hasGrantedCapability(capabilities, "admin.bulk_upsert.torrent_policy");
  const previewItem = items.find((item) => item.infoHash === preview) ?? null;
  const [activeSortField, activeSortDirection = "asc"] = query.sort.split(":");
  const pageCount = Math.max(1, Math.ceil(totalCount / query.pageSize));

  useEffect(() => {
    const banner = location.state as NavigationBannerState | null;
    if (!banner?.message) {
      return;
    }

    setStatus(banner.message);
    navigate(location.pathname, { replace: true, state: null });
  }, [location.pathname, location.state, navigate]);

  const reload = async () => {
    const page = await apiRequest<PageResult<TorrentAdminDto>>(
      `/api/admin/torrents?${buildGridQueryString(query)}`,
      accessToken,
      onReauthenticate
    );
    setItems(page.items);
    setTotalCount(page.totalCount);
    setSelectedInfoHashes((current) => current.filter((infoHash) => page.items.some((item) => item.infoHash === infoHash)));
  };

  useEffect(() => {
    let isMounted = true;

    setIsLoading(true);
    reload()
      .then(() => {
        if (isMounted) {
          setError(null);
        }
      })
      .catch((requestError) => {
        if (isMounted) {
          setError(requestError instanceof Error ? requestError.message : labels.loadError);
          setItems([]);
          setTotalCount(0);
        }
      })
      .finally(() => {
        if (isMounted) {
          setIsLoading(false);
        }
      });

    return () => {
      isMounted = false;
    };
  }, [accessToken, onReauthenticate, query]);

  useEffect(() => {
    if (preview && !items.some((item) => item.infoHash === preview)) {
      setView((current) => ({ ...current, preview: null }));
    }
  }, [items, preview, setView]);

  const toggleSelection = (infoHash: string) => {
    setSelectedInfoHashes((current) => {
      const nextSelection =
        current.includes(infoHash) ? current.filter((value) => value !== infoHash) : [...current, infoHash];
      persistBulkTorrentPolicySelection(
        items
          .filter((item) => nextSelection.includes(item.infoHash))
          .map(toBulkTorrentPolicySelectionItem)
      );
      return nextSelection;
    });
  };

  const runLifecycle = async (mode: "activate" | "deactivate") => {
      try {
        setIsSubmitting(true);
        setError(null);
        setStatus(null);
        setResult(null);
        const selectedItems = items
        .filter((item) => selectedInfoHashes.includes(item.infoHash))
        .map((item) => ({
          infoHash: item.infoHash,
          expectedVersion: item.version
        }));

      const result = await apiMutation<
        BulkOperationResultDto,
        { items: Array<{ infoHash: string; expectedVersion: number }> }
      >(
        `/api/admin/torrents/bulk/${mode}`,
        "POST",
        accessToken,
        { items: selectedItems },
        onReauthenticate
      );

      setResult(result);
      setStatus(formatText(labels.lifecycleStatus, { succeeded: result.succeededCount, total: result.totalCount }));
      await reload();
      setSelectedInfoHashes([]);
      clearBulkTorrentPolicySelection();
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : mode === "activate" ? labels.lifecycleErrorActivate : labels.lifecycleErrorDeactivate);
    } finally {
      setIsSubmitting(false);
    }
  };

  const openBulkPolicyEditor = () => {
    const selection = items
      .filter((item) => selectedInfoHashes.includes(item.infoHash))
      .map(toBulkTorrentPolicySelectionItem);
    persistBulkTorrentPolicySelection(selection);
    navigate("/torrents/bulk-policy");
  };

  return (
    <div className="space-y-6">
      <CatalogToolbar
        title={labels.cardTitle}
        description="Search torrents, review lifecycle state and launch policy workflows."
        totalCount={totalCount}
        search={query.search}
        onSearchChange={(value) => setView((current) => ({ ...current, query: { ...current.query, search: value, page: 1 } }))}
        searchPlaceholder="Search torrents"
        filter={query.filter}
        onFilterChange={(value) => setView((current) => ({ ...current, query: { ...current.query, filter: value, page: 1 } }))}
        filterOptions={[
          { value: "all", label: "All torrents" },
          { value: "enabled", label: "Enabled" },
          { value: "disabled", label: "Disabled" },
          { value: "private", label: "Private" },
          { value: "public", label: "Public" }
        ]}
        sortValue={query.sort}
        onSortChange={(value) => setView((current) => ({ ...current, query: { ...current.query, sort: value, page: 1 } }))}
        sortOptions={[
          { value: "infohash:asc", label: "Info hash A-Z" },
          { value: "infohash:desc", label: "Info hash Z-A" },
          { value: "enabled:desc", label: "Enabled first" },
          { value: "private:desc", label: "Private first" },
          { value: "interval:desc", label: "Interval high-low" }
        ]}
        pageSize={query.pageSize}
        onPageSizeChange={(value) => setView((current) => ({ ...current, query: { ...current.query, pageSize: value, page: 1 } }))}
      />
      {status ? <div className="app-notice-success">{status}</div> : null}
      {error ? <div className="app-notice-warn">{error}</div> : null}
      {result && result.torrentItems.length > 0 ? (
        <div className="app-selection-summary">
          <div className="space-y-1">
            <div className="text-sm font-semibold text-ink">Last lifecycle operation</div>
            <div className="text-sm text-steel">{result.succeededCount} succeeded, {result.failedCount} failed, {result.totalCount} processed</div>
          </div>
          <button type="button" className="app-button-secondary py-2.5" onClick={() => setResult(null)}>Clear result</button>
        </div>
      ) : null}
      {selectedInfoHashes.length > 0 ? (
        <div className="app-selection-summary">
          <div className="space-y-1">
            <div className="text-sm font-semibold text-ink">{selectedInfoHashes.length} torrent(s) selected</div>
            <div className="text-sm text-steel">Run lifecycle actions or open the bulk policy workflow for the current selection.</div>
          </div>
          <div className="flex flex-wrap gap-3">
            <button type="button" className="app-button-secondary py-2.5" onClick={() => { setSelectedInfoHashes([]); clearBulkTorrentPolicySelection(); }}>Clear selection</button>
            <button type="button" className="app-button-secondary py-2.5" disabled={!canActivate || isSubmitting || selectedInfoHashes.length === 0} onClick={() => void runLifecycle("activate")}>{labels.activateSelection}</button>
            <button type="button" className="app-button-danger py-2.5" disabled={!canDeactivate || isSubmitting || selectedInfoHashes.length === 0} onClick={() => void runLifecycle("deactivate")}>{labels.deactivateSelection}</button>
            <button type="button" className="app-button-primary py-2.5" disabled={!canBulkEditPolicy || isSubmitting || selectedInfoHashes.length === 0} onClick={openBulkPolicyEditor}>{labels.openBulkPolicy}</button>
          </div>
        </div>
      ) : null}
      <div className="app-data-table">
        <div className="overflow-x-auto">
          <table className="min-w-full text-sm">
            <thead className="app-table-head">
              <tr>
                <th className="w-14 px-5 py-4">{labels.tableSelect}</th>
                <th className="px-5 py-4"><SortHeaderButton label={labels.tableInfoHash} active={activeSortField === "infohash"} direction={activeSortDirection as "asc" | "desc"} onClick={() => setView((current) => ({ ...current, query: { ...current.query, sort: activeSortField === "infohash" && activeSortDirection === "asc" ? "infohash:desc" : "infohash:asc", page: 1 } }))} /></th>
                <th className="px-5 py-4"><SortHeaderButton label={labels.tableMode} active={activeSortField === "private"} direction={activeSortDirection as "asc" | "desc"} onClick={() => setView((current) => ({ ...current, query: { ...current.query, sort: activeSortField === "private" && activeSortDirection === "asc" ? "private:desc" : "private:asc", page: 1 } }))} /></th>
                <th className="px-5 py-4"><SortHeaderButton label={labels.tableState} active={activeSortField === "enabled"} direction={activeSortDirection as "asc" | "desc"} onClick={() => setView((current) => ({ ...current, query: { ...current.query, sort: activeSortField === "enabled" && activeSortDirection === "asc" ? "enabled:desc" : "enabled:asc", page: 1 } }))} /></th>
                <th className="px-5 py-4"><SortHeaderButton label={labels.tableInterval} active={activeSortField === "interval"} direction={activeSortDirection as "asc" | "desc"} onClick={() => setView((current) => ({ ...current, query: { ...current.query, sort: activeSortField === "interval" && activeSortDirection === "asc" ? "interval:desc" : "interval:asc", page: 1 } }))} /></th>
                <th className="px-5 py-4">{labels.tableNumwant}</th>
                <th className="px-5 py-4 text-right">{labels.tableAction}</th>
              </tr>
            </thead>
            <tbody>
              {isLoading ? (
                <TableStateRow colSpan={7} title="Loading torrents" message="Refreshing tracker policy records from the backend." />
              ) : error && totalCount === 0 ? (
                <TableStateRow colSpan={7} title="Unable to load torrents" message={error} />
              ) : items.length === 0 ? (
                <TableStateRow colSpan={7} title="No torrents match this view" message="Try a broader search or switch the current filter." />
              ) : items.map((item) => (
                <CatalogTableRow key={item.infoHash} onOpen={() => setView((current) => ({ ...current, preview: item.infoHash }))}>
                  <td className="px-5 py-4"><input type="checkbox" checked={selectedInfoHashes.includes(item.infoHash)} onChange={() => toggleSelection(item.infoHash)} /></td>
                  <td className="px-5 py-4">
                    <div className="font-mono text-xs text-ink">{item.infoHash}</div>
                    <div className="app-inline-id">
                      <CopyValueButton value={item.infoHash} label={`Copy info hash ${item.infoHash}`} />
                    </div>
                  </td>
                  <td className="px-5 py-4 text-steel">{formatMode(item.isPrivate, dictionary)}</td>
                  <td className="px-5 py-4 text-steel">{formatState(item.isEnabled, dictionary)}</td>
                  <td className="px-5 py-4 text-steel">{item.announceIntervalSeconds}s</td>
                  <td className="px-5 py-4 text-steel">{item.defaultNumWant} / {item.maxNumWant}</td>
                  <td className="px-5 py-4 text-right">
                    <div className="flex justify-end items-center gap-2">
                      {canEditPolicy ? (
                        <Link className="app-button-secondary py-2.5 no-underline" to={`/torrents/${item.infoHash}`}>
                          {labels.openPolicy}
                        </Link>
                      ) : (
                        <span className="app-chip-muted">Read only</span>
                      )}
                      <RowActionsMenu items={[{ label: "Preview", onClick: () => setView((current) => ({ ...current, preview: item.infoHash })) }]} />
                    </div>
                  </td>
                </CatalogTableRow>
              ))}
            </tbody>
          </table>
        </div>
      </div>
      <PaginationFooter page={query.page} pageCount={pageCount} totalCount={totalCount} pageSize={query.pageSize} onPageChange={(page) => setView((current) => ({ ...current, query: { ...current.query, page } }))} />
      <PreviewDrawer open={previewItem != null} title={previewItem?.infoHash ?? ""} subtitle="Current torrent policy snapshot" onClose={() => setView((current) => ({ ...current, preview: null }))}>
        {previewItem ? (
          <div className="space-y-4">
            <div className="app-detail-grid">
              <div className="app-subtle-panel space-y-2"><div className="app-kicker">{dictionary.common.mode}</div><div className="font-semibold text-ink">{formatMode(previewItem.isPrivate, dictionary)}</div></div>
              <div className="app-subtle-panel space-y-2"><div className="app-kicker">{dictionary.common.state}</div><div className="font-semibold text-ink">{formatState(previewItem.isEnabled, dictionary)}</div></div>
              <div className="app-subtle-panel space-y-2"><div className="app-kicker">{dictionary.common.interval}</div><div className="font-semibold text-ink">{previewItem.announceIntervalSeconds}s / min {previewItem.minAnnounceIntervalSeconds}s</div></div>
              <div className="app-subtle-panel space-y-2"><div className="app-kicker">{dictionary.common.scrape}</div><div className="font-semibold text-ink">{formatScrape(previewItem.allowScrape, dictionary)}</div></div>
            </div>
            <div className="app-detail-grid">
              <div className="app-subtle-panel space-y-2"><div className="app-kicker">Numwant</div><div className="font-semibold text-ink">{previewItem.defaultNumWant} / {previewItem.maxNumWant}</div></div>
              <div className="app-subtle-panel space-y-2"><div className="app-kicker">{dictionary.common.version}</div><div className="font-semibold text-ink">{previewItem.version}</div></div>
            </div>
            {result?.torrentItems?.some((item) => item.infoHash === previewItem.infoHash) ? (
              <div className="space-y-3">
                <div className="app-kicker">Latest operation</div>
                {result.torrentItems.filter((item) => item.infoHash === previewItem.infoHash).map((item) => (
                  <div key={item.infoHash} className="app-subtle-panel space-y-2">
                    <StatusPill tone={item.succeeded ? "good" : "warn"}>{item.succeeded ? labels.applied : labels.failed}</StatusPill>
                    {item.errorMessage ? <div className="text-sm text-ember">{item.errorMessage}</div> : null}
                  </div>
                ))}
              </div>
            ) : null}
          </div>
        ) : null}
      </PreviewDrawer>
    </div>
  );
}

function TorrentPolicyEditorPage({
  accessToken,
  onReauthenticate
}: {
  accessToken: string;
  onReauthenticate: (fresh: boolean) => Promise<void>;
}) {
  const { dictionary } = useI18n();
  const labels = dictionary.policyEditor;
  const params = useParams();
  const infoHash = params.infoHash ?? "";
  const [current, setCurrent] = useState<TorrentAdminDto | null>(null);
  const [form, setForm] = useState<TorrentPolicyFormState | null>(null);
  const [dryRun, setDryRun] = useState<TorrentPolicyDryRunItemDto | null>(null);
  const [status, setStatus] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  useEffect(() => {
    let isMounted = true;

    apiRequest<TorrentAdminDto>(`/api/admin/torrents/${infoHash}`, accessToken, onReauthenticate)
      .then((snapshot) => {
        if (isMounted) {
          setCurrent(snapshot);
          setForm(toPolicyForm(snapshot));
          setDryRun(null);
          setError(null);
        }
      })
      .catch((requestError) => {
        if (isMounted) {
          setError(requestError instanceof Error ? requestError.message : labels.loadError);
        }
      });

    return () => {
      isMounted = false;
    };
  }, [accessToken, infoHash, onReauthenticate]);

  const updateField = <K extends keyof TorrentPolicyFormState>(key: K, value: TorrentPolicyFormState[K]) => {
    setForm((currentForm) => (currentForm ? { ...currentForm, [key]: value } : currentForm));
  };

  const buildPayload = () => {
    if (!form) {
      throw new Error("Torrent policy form is not loaded.");
    }

    return {
      isPrivate: form.isPrivate,
      isEnabled: form.isEnabled,
      announceIntervalSeconds: form.announceIntervalSeconds,
      minAnnounceIntervalSeconds: form.minAnnounceIntervalSeconds,
      defaultNumWant: form.defaultNumWant,
      maxNumWant: form.maxNumWant,
      allowScrape: form.allowScrape,
      expectedVersion: form.expectedVersion
    };
  };

  const handleDryRun = async () => {
    try {
      setIsSubmitting(true);
      setStatus(null);
      setError(null);
      const result = await apiMutation<
        BulkDryRunResultDto,
        { items: Array<{ infoHash: string } & ReturnType<typeof buildPayload>> }
      >(
        "/api/admin/torrents/bulk/policy/dry-run",
        "POST",
        accessToken,
        {
          items: [
            {
              infoHash,
              ...buildPayload()
            }
          ]
        },
        onReauthenticate
      );

      setDryRun(result.torrentPolicyItems[0] ?? null);
      setStatus(labels.previewGenerated);
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : labels.dryRunFailed);
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleSave = async () => {
    try {
      setIsSubmitting(true);
      setStatus(null);
      setError(null);
      const snapshot = await apiMutation<TorrentAdminDto, ReturnType<typeof buildPayload>>(
        `/api/admin/torrents/${infoHash}/policy`,
        "PUT",
        accessToken,
        buildPayload(),
        onReauthenticate
      );

      setCurrent(snapshot);
      setForm(toPolicyForm(snapshot));
      setDryRun(null);
      setStatus(labels.updated);
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : labels.saveFailed);
    } finally {
      setIsSubmitting(false);
    }
  };

  if (error && !form) {
    return (
      <Card title={dictionary.routes.torrentPolicyTitle} eyebrow={dictionary.routes.torrentPolicyEyebrow}>
        <p className="text-sm text-ember">{error}</p>
      </Card>
    );
  }

  if (!form || !current) {
    return (
      <Card title={dictionary.routes.torrentPolicyTitle} eyebrow={dictionary.routes.torrentPolicyEyebrow}>
        <p className="text-sm text-ink/60">{labels.loading}</p>
      </Card>
    );
  }

  return (
    <div className="grid gap-6 xl:grid-cols-[1.1fr,0.9fr]">
      <Card title={labels.editTitle} eyebrow={labels.editEyebrow}>
        <p className="text-xs uppercase tracking-[0.18em] text-ink/40">{labels.infoHash}</p>
        <p className="mt-2 break-all font-mono text-sm text-ink">{infoHash}</p>
        <div className="mt-6 grid gap-5 md:grid-cols-2">
          <label className="space-y-2">
            <span className="text-sm font-medium text-ink">{labels.announceInterval}</span>
            <input className="w-full rounded-2xl border border-ink/15 px-4 py-3" type="number" value={form.announceIntervalSeconds} onChange={(event) => updateField("announceIntervalSeconds", Number(event.target.value))} />
          </label>
          <label className="space-y-2">
            <span className="text-sm font-medium text-ink">{labels.minAnnounceInterval}</span>
            <input className="w-full rounded-2xl border border-ink/15 px-4 py-3" type="number" value={form.minAnnounceIntervalSeconds} onChange={(event) => updateField("minAnnounceIntervalSeconds", Number(event.target.value))} />
          </label>
          <label className="space-y-2">
            <span className="text-sm font-medium text-ink">{labels.defaultNumwant}</span>
            <input className="w-full rounded-2xl border border-ink/15 px-4 py-3" type="number" value={form.defaultNumWant} onChange={(event) => updateField("defaultNumWant", Number(event.target.value))} />
          </label>
          <label className="space-y-2">
            <span className="text-sm font-medium text-ink">{labels.maxNumwant}</span>
            <input className="w-full rounded-2xl border border-ink/15 px-4 py-3" type="number" value={form.maxNumWant} onChange={(event) => updateField("maxNumWant", Number(event.target.value))} />
          </label>
        </div>
        <div className="mt-5 grid gap-3 md:grid-cols-3">
          <label className="flex items-center gap-3 rounded-2xl border border-ink/10 bg-slate-50 px-4 py-3 text-sm text-ink">
            <input type="checkbox" checked={form.isPrivate} onChange={(event) => updateField("isPrivate", event.target.checked)} />
            {labels.privateTracker}
          </label>
          <label className="flex items-center gap-3 rounded-2xl border border-ink/10 bg-slate-50 px-4 py-3 text-sm text-ink">
            <input type="checkbox" checked={form.isEnabled} onChange={(event) => updateField("isEnabled", event.target.checked)} />
            {labels.enabled}
          </label>
          <label className="flex items-center gap-3 rounded-2xl border border-ink/10 bg-slate-50 px-4 py-3 text-sm text-ink">
            <input type="checkbox" checked={form.allowScrape} onChange={(event) => updateField("allowScrape", event.target.checked)} />
            {labels.allowScrape}
          </label>
        </div>
        <div className="mt-6 flex flex-wrap gap-3">
          <button
            type="button"
            disabled={isSubmitting}
            onClick={() => void handleDryRun()}
            className="rounded-2xl border border-ink/20 px-5 py-3 text-sm font-semibold text-ink transition hover:bg-ink/5 disabled:cursor-not-allowed disabled:opacity-60"
          >
            {labels.previewChanges}
          </button>
          <button
            type="button"
            disabled={isSubmitting}
            onClick={() => void handleSave()}
            className="rounded-2xl bg-ink px-5 py-3 text-sm font-semibold text-white transition hover:bg-slate-900 disabled:cursor-not-allowed disabled:opacity-60"
          >
            {labels.applyPolicy}
          </button>
        </div>
        {status ? <p className="mt-4 text-sm text-moss">{status}</p> : null}
        {error ? <p className="mt-2 text-sm text-ember">{error}</p> : null}
      </Card>
      <Card title={labels.previewTitle} eyebrow={labels.previewEyebrow}>
        <div className="space-y-4">
          <div className="rounded-2xl border border-ink/10 bg-white px-4 py-4">
            <p className="text-xs uppercase tracking-[0.18em] text-ink/45">{labels.currentSnapshot}</p>
            <div className="mt-3 grid gap-2 text-sm text-ink/70">
              <p>{dictionary.common.mode}: {formatMode(current.isPrivate, dictionary)}</p>
              <p>{dictionary.common.state}: {formatState(current.isEnabled, dictionary)}</p>
              <p>{dictionary.common.interval}: {current.announceIntervalSeconds}s / min {current.minAnnounceIntervalSeconds}s</p>
              <p>Numwant: {current.defaultNumWant} / {current.maxNumWant}</p>
              <p>{dictionary.common.scrape}: {formatScrape(current.allowScrape, dictionary)}</p>
              <p>{dictionary.common.version}: {current.version}</p>
            </div>
          </div>
          {dryRun ? (
            <div className="rounded-2xl border border-ink/10 bg-slate-50 px-4 py-4">
              <div className="flex items-center justify-between gap-3">
                <p className="text-xs uppercase tracking-[0.18em] text-ink/45">{labels.dryRunResult}</p>
                <StatusPill tone={dryRun.canApply ? "good" : "warn"}>{dryRun.canApply ? labels.canApply : labels.rejected}</StatusPill>
              </div>
              {dryRun.errorMessage ? <p className="mt-3 text-sm text-ember">{dryRun.errorMessage}</p> : null}
              <div className="mt-3 grid gap-2 text-sm text-ink/70">
                <p>{dictionary.common.mode}: {formatMode(dryRun.proposedSnapshot.isPrivate, dictionary)}</p>
                <p>{dictionary.common.state}: {formatState(dryRun.proposedSnapshot.isEnabled, dictionary)}</p>
                <p>{dictionary.common.interval}: {dryRun.proposedSnapshot.announceIntervalSeconds}s / min {dryRun.proposedSnapshot.minAnnounceIntervalSeconds}s</p>
                <p>Numwant: {dryRun.proposedSnapshot.defaultNumWant} / {dryRun.proposedSnapshot.maxNumWant}</p>
                <p>{dictionary.common.scrape}: {formatScrape(dryRun.proposedSnapshot.allowScrape, dictionary)}</p>
                <p>{labels.versionTarget}: {dryRun.proposedSnapshot.version}</p>
              </div>
              {dryRun.warnings.length > 0 ? (
                <div className="mt-4 rounded-2xl bg-ember/10 px-4 py-3">
                  <p className="text-xs uppercase tracking-[0.18em] text-ember">{dictionary.common.warnings}</p>
                  <ul className="mt-2 space-y-2 text-sm text-ember">
                    {dryRun.warnings.map((warning) => (
                      <li key={warning}>{warning}</li>
                    ))}
                  </ul>
                </div>
              ) : null}
            </div>
          ) : (
            <p className="text-sm text-ink/55">{labels.previewHint}</p>
          )}
        </div>
      </Card>
    </div>
  );
}

function BulkTorrentPolicyEditorPage({
  accessToken,
  onReauthenticate
}: {
  accessToken: string;
  onReauthenticate: (fresh: boolean) => Promise<void>;
}) {
  const { dictionary } = useI18n();
  const labels = dictionary.bulkPolicy;
  const navigate = useNavigate();
  const [selection, setSelection] = useState<BulkTorrentPolicySelectionItem[]>([]);
  const [form, setForm] = useState<TorrentPolicyFormState | null>(null);
  const [dryRunResult, setDryRunResult] = useState<BulkDryRunResultDto | null>(null);
  const [applyResult, setApplyResult] = useState<BulkOperationResultDto | null>(null);
  const [status, setStatus] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  useEffect(() => {
    const storedSelection = readBulkTorrentPolicySelection();
    if (storedSelection.length === 0) {
      navigate("/torrents", {
        replace: true,
        state: {
          message: labels.requiresSelection,
          tone: "warn"
        } satisfies NavigationBannerState
      });
      return;
    }

    setSelection(storedSelection);
    const firstItem = storedSelection[0];
    setForm({
      isPrivate: firstItem.isPrivate,
      isEnabled: firstItem.isEnabled,
      announceIntervalSeconds: firstItem.announceIntervalSeconds,
      minAnnounceIntervalSeconds: firstItem.minAnnounceIntervalSeconds,
      defaultNumWant: firstItem.defaultNumWant,
      maxNumWant: firstItem.maxNumWant,
      allowScrape: firstItem.allowScrape,
      expectedVersion: firstItem.version
    });
  }, [navigate]);

  const updateField = <K extends keyof TorrentPolicyFormState>(key: K, value: TorrentPolicyFormState[K]) => {
    setForm((currentForm) => {
      if (!currentForm) {
        return currentForm;
      }

      return { ...currentForm, [key]: value };
    });
    setDryRunResult(null);
    setApplyResult(null);
    setStatus(null);
    setError(null);
  };

  const buildItems = () => {
    if (!form) {
      throw new Error(labels.formNotReady);
    }

    return selection.map((item) => ({
      infoHash: item.infoHash,
      isPrivate: form.isPrivate,
      isEnabled: form.isEnabled,
      announceIntervalSeconds: form.announceIntervalSeconds,
      minAnnounceIntervalSeconds: form.minAnnounceIntervalSeconds,
      defaultNumWant: form.defaultNumWant,
      maxNumWant: form.maxNumWant,
      allowScrape: form.allowScrape,
      expectedVersion: item.version
    }));
  };

  const runDryRun = async () => {
    try {
      setIsSubmitting(true);
      setError(null);
      setStatus(null);
      setApplyResult(null);
      const result = await apiMutation<BulkDryRunResultDto, { items: ReturnType<typeof buildItems> }>(
        "/api/admin/torrents/bulk/policy/dry-run",
        "POST",
        accessToken,
        { items: buildItems() },
        onReauthenticate
      );

      setDryRunResult(result);
      setStatus(formatText(labels.dryRunStatus, { applicable: result.applicableCount, total: result.totalCount }));
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : labels.dryRunError);
    } finally {
      setIsSubmitting(false);
    }
  };

  const applyBulkUpdate = async () => {
    try {
      setIsSubmitting(true);
      setError(null);
      setStatus(null);
      const result = await apiMutation<BulkOperationResultDto, { items: ReturnType<typeof buildItems> }>(
        "/api/admin/torrents/bulk/policy",
        "PUT",
        accessToken,
        { items: buildItems() },
        onReauthenticate
      );

      setApplyResult(result);
      setStatus(formatText(labels.applyStatus, { succeeded: result.succeededCount, total: result.totalCount }));

      if (result.failedCount === 0) {
        clearBulkTorrentPolicySelection();
        navigate("/torrents", {
          replace: true,
          state: {
            message: formatText(labels.applySuccessRedirect, { count: result.succeededCount }),
            tone: "good"
          } satisfies NavigationBannerState
        });
      }
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : labels.applyError);
    } finally {
      setIsSubmitting(false);
    }
  };

  if (!form) {
    return (
      <Card title={labels.title} eyebrow={labels.eyebrow}>
        <p className="text-sm text-ink/60">{labels.loading}</p>
      </Card>
    );
  }

  return (
    <div className="grid gap-6 xl:grid-cols-[1.05fr,0.95fr]">
      <Card title={labels.title} eyebrow={labels.eyebrow}>
        <p className="text-sm text-ink/60">
          {labels.selectedInRollout}: <span className="font-semibold text-ink">{selection.length}</span>
        </p>
        <div className="mt-4 max-h-40 overflow-y-auto rounded-2xl border border-ink/10 bg-slate-50 px-4 py-3">
          <div className="space-y-2">
            {selection.map((item) => (
              <p key={item.infoHash} className="break-all font-mono text-xs text-ink">{item.infoHash}</p>
            ))}
          </div>
        </div>
        <div className="mt-6 grid gap-5 md:grid-cols-2">
          <label className="space-y-2">
            <span className="text-sm font-medium text-ink">{dictionary.policyEditor.announceInterval}</span>
            <input className="w-full rounded-2xl border border-ink/15 px-4 py-3" type="number" value={form.announceIntervalSeconds} onChange={(event) => updateField("announceIntervalSeconds", Number(event.target.value))} />
          </label>
          <label className="space-y-2">
            <span className="text-sm font-medium text-ink">{dictionary.policyEditor.minAnnounceInterval}</span>
            <input className="w-full rounded-2xl border border-ink/15 px-4 py-3" type="number" value={form.minAnnounceIntervalSeconds} onChange={(event) => updateField("minAnnounceIntervalSeconds", Number(event.target.value))} />
          </label>
          <label className="space-y-2">
            <span className="text-sm font-medium text-ink">{dictionary.policyEditor.defaultNumwant}</span>
            <input className="w-full rounded-2xl border border-ink/15 px-4 py-3" type="number" value={form.defaultNumWant} onChange={(event) => updateField("defaultNumWant", Number(event.target.value))} />
          </label>
          <label className="space-y-2">
            <span className="text-sm font-medium text-ink">{dictionary.policyEditor.maxNumwant}</span>
            <input className="w-full rounded-2xl border border-ink/15 px-4 py-3" type="number" value={form.maxNumWant} onChange={(event) => updateField("maxNumWant", Number(event.target.value))} />
          </label>
        </div>
        <div className="mt-5 grid gap-3 md:grid-cols-3">
          <label className="flex items-center gap-3 rounded-2xl border border-ink/10 bg-slate-50 px-4 py-3 text-sm text-ink">
            <input type="checkbox" checked={form.isPrivate} onChange={(event) => updateField("isPrivate", event.target.checked)} />
            {dictionary.policyEditor.privateTracker}
          </label>
          <label className="flex items-center gap-3 rounded-2xl border border-ink/10 bg-slate-50 px-4 py-3 text-sm text-ink">
            <input type="checkbox" checked={form.isEnabled} onChange={(event) => updateField("isEnabled", event.target.checked)} />
            {dictionary.policyEditor.enabled}
          </label>
          <label className="flex items-center gap-3 rounded-2xl border border-ink/10 bg-slate-50 px-4 py-3 text-sm text-ink">
            <input type="checkbox" checked={form.allowScrape} onChange={(event) => updateField("allowScrape", event.target.checked)} />
            {dictionary.policyEditor.allowScrape}
          </label>
        </div>
        <div className="mt-6 flex flex-wrap gap-3">
          <button
            type="button"
            disabled={isSubmitting || selection.length === 0}
            onClick={() => void runDryRun()}
            className="rounded-2xl border border-ink/20 px-5 py-3 text-sm font-semibold text-ink transition hover:bg-ink/5 disabled:cursor-not-allowed disabled:opacity-60"
          >
            {labels.previewRollout}
          </button>
          <button
            type="button"
            disabled={isSubmitting || selection.length === 0}
            onClick={() => void applyBulkUpdate()}
            className="rounded-2xl bg-ink px-5 py-3 text-sm font-semibold text-white transition hover:bg-slate-900 disabled:cursor-not-allowed disabled:opacity-60"
          >
            {labels.applyRollout}
          </button>
          <button
            type="button"
            disabled={isSubmitting}
            onClick={() => {
              clearBulkTorrentPolicySelection();
              navigate("/torrents", { replace: true });
            }}
            className="rounded-2xl border border-ember/30 px-5 py-3 text-sm font-semibold text-ember transition hover:bg-ember/5 disabled:cursor-not-allowed disabled:opacity-60"
          >
            {labels.backToCatalog}
          </button>
        </div>
        {status ? <p className="mt-4 text-sm text-moss">{status}</p> : null}
        {error ? <p className="mt-2 text-sm text-ember">{error}</p> : null}
        {applyResult && applyResult.torrentItems.length > 0 ? (
          <div className="mt-5 space-y-2">
            {applyResult.torrentItems.map((item) => (
              <div key={item.infoHash} className="rounded-2xl border border-ink/10 bg-slate-50 px-4 py-3">
                <div className="flex items-center justify-between gap-3">
                  <p className="font-mono text-xs text-ink">{item.infoHash}</p>
                  <StatusPill tone={item.succeeded ? "good" : "warn"}>{item.succeeded ? dictionary.torrents.applied : dictionary.torrents.failed}</StatusPill>
                </div>
                {item.errorMessage ? <p className="mt-2 text-sm text-ember">{item.errorMessage}</p> : null}
              </div>
            ))}
          </div>
        ) : null}
      </Card>
      <Card title={labels.previewTitle} eyebrow={labels.previewEyebrow}>
        {!dryRunResult ? (
          <p className="text-sm text-ink/55">{labels.previewHint}</p>
        ) : (
          <div className="space-y-4">
            <div className="grid gap-3 md:grid-cols-3">
              <div className="rounded-2xl bg-white px-4 py-3 ring-1 ring-ink/10">
                <p className="text-xs uppercase tracking-[0.18em] text-ink/45">{dictionary.common.total}</p>
                <p className="mt-2 text-2xl font-semibold text-ink">{dryRunResult.totalCount}</p>
              </div>
              <div className="rounded-2xl bg-white px-4 py-3 ring-1 ring-ink/10">
                <p className="text-xs uppercase tracking-[0.18em] text-ink/45">{dictionary.common.applicable}</p>
                <p className="mt-2 text-2xl font-semibold text-moss">{dryRunResult.applicableCount}</p>
              </div>
              <div className="rounded-2xl bg-white px-4 py-3 ring-1 ring-ink/10">
                <p className="text-xs uppercase tracking-[0.18em] text-ink/45">{dictionary.common.rejected}</p>
                <p className="mt-2 text-2xl font-semibold text-ember">{dryRunResult.rejectedCount}</p>
              </div>
            </div>
            {dryRunResult.torrentPolicyItems.map((item) => (
              <div key={item.infoHash} className="rounded-3xl border border-ink/10 bg-white px-4 py-4">
                <div className="flex items-center justify-between gap-3">
                  <p className="break-all font-mono text-xs text-ink">{item.infoHash}</p>
                  <StatusPill tone={item.canApply ? "good" : "warn"}>{item.canApply ? dictionary.policyEditor.canApply : dictionary.common.rejected}</StatusPill>
                </div>
                {item.errorMessage ? <p className="mt-3 text-sm text-ember">{item.errorMessage}</p> : null}
                {item.warnings.length > 0 ? (
                  <div className="mt-3 rounded-2xl bg-ember/10 px-4 py-3">
                    <p className="text-xs uppercase tracking-[0.18em] text-ember">{dictionary.common.warnings}</p>
                    <ul className="mt-2 space-y-2 text-sm text-ember">
                      {item.warnings.map((warning) => (
                        <li key={warning}>{warning}</li>
                      ))}
                    </ul>
                  </div>
                ) : null}
                <div className="mt-4 overflow-hidden rounded-2xl border border-ink/10">
                  <table className="min-w-full divide-y divide-ink/10 text-sm">
                    <thead className="bg-slate-50 text-left text-xs uppercase tracking-[0.18em] text-ink/45">
                      <tr>
                        <th className="px-4 py-3">{dictionary.common.field}</th>
                        <th className="px-4 py-3">{dictionary.common.current}</th>
                        <th className="px-4 py-3">{dictionary.common.proposed}</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-ink/10 bg-white">
                      {buildPolicyComparisonRows(item.currentSnapshot, item.proposedSnapshot, dictionary).map((row) => (
                        <tr key={`${item.infoHash}-${row.label}`}>
                          <td className="px-4 py-3 font-medium text-ink">{row.label}</td>
                          <td className="px-4 py-3 text-ink/65">{row.current}</td>
                          <td className="px-4 py-3 text-ink">{row.proposed}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </div>
            ))}
          </div>
        )}
      </Card>
    </div>
  );
}

function PasskeysPage({
  accessToken,
  onReauthenticate,
  capabilities
}: {
  accessToken: string;
  onReauthenticate: (fresh: boolean) => Promise<void>;
  capabilities: CapabilityDto[];
}) {
  const { dictionary } = useI18n();
  const labels = dictionary.passkeys;
  const [view, setView] = useCatalogViewState({
    search: "",
    filter: "all",
    sort: "userid:asc",
    page: 1,
    pageSize: 25
  });
  const { query, preview, modal } = view;
  const [items, setItems] = useState<PasskeyAdminDto[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [passkeyInput, setPasskeyInput] = useState("");
  const [rotateExpiryInput, setRotateExpiryInput] = useState("");
  const [result, setResult] = useState<BulkOperationResultDto | null>(null);
  const [status, setStatus] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [isLoading, setIsLoading] = useState(true);
  const canRevoke = hasGrantedCapability(capabilities, "admin.revoke.passkey");
  const canRotate = hasGrantedCapability(capabilities, "admin.rotate.passkey");
  const previewItem = items.find((item) => item.passkeyMask === preview) ?? null;
  const [activeSortField, activeSortDirection = "asc"] = query.sort.split(":");
  const pageCount = Math.max(1, Math.ceil(totalCount / query.pageSize));

  const reload = async () => {
    const page = await apiRequest<PageResult<PasskeyAdminDto>>(
      `/api/admin/passkeys?${buildGridQueryString(query)}`,
      accessToken,
      onReauthenticate
    );
    setItems(page.items);
    setTotalCount(page.totalCount);
  };

  useEffect(() => {
    let isMounted = true;

    setIsLoading(true);
    reload()
      .then(() => {
        if (isMounted) {
          setError(null);
        }
      })
      .catch((requestError) => {
        if (isMounted) {
          setError(requestError instanceof Error ? requestError.message : labels.loadError);
          setItems([]);
          setTotalCount(0);
        }
      })
      .finally(() => {
        if (isMounted) {
          setIsLoading(false);
        }
      });

    return () => {
      isMounted = false;
    };
  }, [accessToken, onReauthenticate, query]);

  useEffect(() => {
    if (preview && !items.some((item) => item.passkeyMask === preview)) {
      setView((current) => ({ ...current, preview: null }));
    }
  }, [items, preview, setView]);

  const inputPasskeys = parseLineSeparatedValues(passkeyInput);

  const runBulkPasskeyAction = async (path: string, mode: "revoke" | "rotate") => {
    try {
      setIsSubmitting(true);
      setError(null);
      setStatus(null);
      const payload =
        mode === "rotate"
          ? {
              items: inputPasskeys.map((passkey) => ({
                passkey,
                expiresAtUtc: fromLocalDateTimeInput(rotateExpiryInput)
              }))
            }
          : {
              items: inputPasskeys.map((passkey) => ({ passkey }))
            };

      const operationResult = await apiMutation<BulkOperationResultDto, typeof payload>(
        path,
        "POST",
        accessToken,
        payload,
        onReauthenticate
      );

      setResult(operationResult);
      setStatus(formatText(labels.status, { mode: toTitleCase(mode), succeeded: operationResult.succeededCount, total: operationResult.totalCount }));
      setView((current) => ({ ...current, modal: null, id: null }));
      await reload();
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : labels.operationError);
    } finally {
      setIsSubmitting(false);
    }
  };

  const openActionModal = () => {
    setError(null);
    setStatus(null);
    setResult(null);
    setView((current) => ({ ...current, modal: "create", id: null }));
  };

  return (
    <div className="space-y-6">
      <CatalogToolbar
        title={labels.cardTitle}
        description="Search passkeys and run revoke or rotate workflows."
        totalCount={totalCount}
        search={query.search}
        onSearchChange={(value) => setView((current) => ({ ...current, query: { ...current.query, search: value, page: 1 } }))}
        searchPlaceholder="Search passkeys"
        filter={query.filter}
        onFilterChange={(value) => setView((current) => ({ ...current, query: { ...current.query, filter: value, page: 1 } }))}
        filterOptions={[
          { value: "all", label: "All passkeys" },
          { value: "active", label: "Active" },
          { value: "revoked", label: "Revoked" },
          { value: "expired", label: "Expired" }
        ]}
        sortValue={query.sort}
        onSortChange={(value) => setView((current) => ({ ...current, query: { ...current.query, sort: value, page: 1 } }))}
        sortOptions={[
          { value: "userid:asc", label: "User A-Z" },
          { value: "userid:desc", label: "User Z-A" },
          { value: "expires:desc", label: "Expiry latest first" },
          { value: "expires:asc", label: "Expiry earliest first" },
          { value: "version:desc", label: "Version high-low" }
        ]}
        pageSize={query.pageSize}
        onPageSizeChange={(value) => setView((current) => ({ ...current, query: { ...current.query, pageSize: value, page: 1 } }))}
        createLabel="Batch action"
        onCreate={openActionModal}
      />
      {status ? <div className="app-notice-success">{status}</div> : null}
      {error ? <div className="app-notice-warn">{error}</div> : null}
      {result && result.passkeyItems.length > 0 ? (
        <div className="app-selection-summary">
          <div className="space-y-1">
            <div className="text-sm font-semibold text-ink">Last batch action</div>
            <div className="text-sm text-steel">{result.succeededCount} succeeded, {result.failedCount} failed, {result.totalCount} processed</div>
          </div>
          <button type="button" className="app-button-secondary py-2.5" onClick={() => setResult(null)}>Clear result</button>
        </div>
      ) : null}
      <div className="app-data-table">
        <div className="overflow-x-auto">
          <table className="min-w-full text-sm">
            <thead className="app-table-head">
              <tr>
                <th className="px-5 py-4"><SortHeaderButton label={labels.tableMask} active={activeSortField === "mask"} direction={activeSortDirection as "asc" | "desc"} onClick={() => setView((current) => ({ ...current, query: { ...current.query, sort: activeSortField === "mask" && activeSortDirection === "asc" ? "mask:desc" : "mask:asc", page: 1 } }))} /></th>
                <th className="px-5 py-4"><SortHeaderButton label={labels.tableUser} active={activeSortField === "userid"} direction={activeSortDirection as "asc" | "desc"} onClick={() => setView((current) => ({ ...current, query: { ...current.query, sort: activeSortField === "userid" && activeSortDirection === "asc" ? "userid:desc" : "userid:asc", page: 1 } }))} /></th>
                <th className="px-5 py-4">{labels.tableState}</th>
                <th className="px-5 py-4"><SortHeaderButton label={labels.tableExpires} active={activeSortField === "expires"} direction={activeSortDirection as "asc" | "desc"} onClick={() => setView((current) => ({ ...current, query: { ...current.query, sort: activeSortField === "expires" && activeSortDirection === "asc" ? "expires:desc" : "expires:asc", page: 1 } }))} /></th>
                <th className="px-5 py-4"><SortHeaderButton label={labels.tableVersion} active={activeSortField === "version"} direction={activeSortDirection as "asc" | "desc"} onClick={() => setView((current) => ({ ...current, query: { ...current.query, sort: activeSortField === "version" && activeSortDirection === "asc" ? "version:desc" : "version:asc", page: 1 } }))} /></th>
                <th className="px-5 py-4 text-right">Actions</th>
              </tr>
            </thead>
            <tbody>
              {isLoading ? (
                <TableStateRow colSpan={6} title="Loading passkeys" message="Refreshing masked tracker credentials from the backend." />
              ) : error && totalCount === 0 ? (
                <TableStateRow colSpan={6} title="Unable to load passkeys" message={error} />
              ) : items.length === 0 ? (
                <TableStateRow colSpan={6} title="No passkeys match this view" message="Try a broader search or change the current filter." />
              ) : items.map((item) => (
                <CatalogTableRow key={`${item.passkeyMask}-${item.userId}`} onOpen={() => setView((current) => ({ ...current, preview: item.passkeyMask }))}>
                  <td className="px-5 py-4">
                    <div className="font-mono text-xs text-ink">{item.passkeyMask}</div>
                    <div className="app-inline-id">
                      <CopyValueButton value={item.passkeyMask} label={`Copy passkey mask ${item.passkeyMask}`} />
                    </div>
                  </td>
                  <td className="px-5 py-4">
                    <div className="font-mono text-xs text-ink">{item.userId}</div>
                    <div className="app-inline-id">
                      <CopyValueButton value={item.userId} label={`Copy user id ${item.userId}`} />
                    </div>
                  </td>
                  <td className="px-5 py-4"><StatusPill tone={item.isRevoked ? "warn" : "good"}>{item.isRevoked ? dictionary.common.revoked : dictionary.common.active}</StatusPill></td>
                  <td className="px-5 py-4 text-steel">{item.expiresAtUtc ? new Date(item.expiresAtUtc).toLocaleString() : dictionary.common.never}</td>
                  <td className="px-5 py-4 text-steel">{item.version}</td>
                  <td className="px-5 py-4 text-right">
                    <RowActionsMenu items={[{ label: "Preview", onClick: () => setView((current) => ({ ...current, preview: item.passkeyMask })) }]} />
                  </td>
                </CatalogTableRow>
              ))}
            </tbody>
          </table>
        </div>
      </div>
      <PaginationFooter page={query.page} pageCount={pageCount} totalCount={totalCount} pageSize={query.pageSize} onPageChange={(page) => setView((current) => ({ ...current, query: { ...current.query, page } }))} />
      <Modal open={modal === "create"} onClose={() => setView((current) => ({ ...current, modal: null, id: null }))} title={labels.actionTitle} description="Provide raw passkeys one per line and run revoke or rotate without leaving the catalog." width="wide">
        <div className="space-y-4">
          <label className="space-y-2">
            <span className="text-sm font-medium text-ink">{labels.rawPasskeys}</span>
            <textarea className="app-input min-h-48 font-mono text-sm" value={passkeyInput} onChange={(event) => setPasskeyInput(event.target.value)} placeholder={labels.placeholder} />
          </label>
          <label className="space-y-2">
            <span className="text-sm font-medium text-ink">{labels.expiryOverride}</span>
            <input className="app-input" type="datetime-local" value={rotateExpiryInput} onChange={(event) => setRotateExpiryInput(event.target.value)} />
          </label>
          <div className="flex flex-wrap justify-end gap-3">
            <ModalDismissButton onClose={() => setView((current) => ({ ...current, modal: null, id: null }))}>Cancel</ModalDismissButton>
            <button type="button" className="app-button-secondary" disabled={!canRevoke || isSubmitting || inputPasskeys.length === 0} onClick={() => void runBulkPasskeyAction("/api/admin/passkeys/bulk/revoke", "revoke")}>{labels.revoke}</button>
            <button type="button" className="app-button-primary" disabled={!canRotate || isSubmitting || inputPasskeys.length === 0} onClick={() => void runBulkPasskeyAction("/api/admin/passkeys/bulk/rotate", "rotate")}>{labels.rotate}</button>
          </div>
        </div>
      </Modal>
      <PreviewDrawer open={previewItem != null} title={previewItem?.passkeyMask ?? ""} subtitle={previewItem ? `User ${previewItem.userId}` : undefined} onClose={() => setView((current) => ({ ...current, preview: null }))}>
        {previewItem ? (
          <div className="space-y-4">
            <div className="app-detail-grid">
              <div className="app-subtle-panel space-y-2"><div className="app-kicker">Owner</div><div className="font-mono text-xs text-ink">{previewItem.userId}</div></div>
              <div className="app-subtle-panel space-y-2"><div className="app-kicker">State</div><StatusPill tone={previewItem.isRevoked ? "warn" : "good"}>{previewItem.isRevoked ? dictionary.common.revoked : dictionary.common.active}</StatusPill></div>
              <div className="app-subtle-panel space-y-2"><div className="app-kicker">Expires</div><div className="font-semibold text-ink">{previewItem.expiresAtUtc ? new Date(previewItem.expiresAtUtc).toLocaleString() : dictionary.common.never}</div></div>
              <div className="app-subtle-panel space-y-2"><div className="app-kicker">Version</div><div className="font-semibold text-ink">{previewItem.version}</div></div>
            </div>
            {result?.passkeyItems?.some((item) => item.passkeyMask === previewItem.passkeyMask) ? (
              <div className="space-y-3">
                <div className="app-kicker">Latest operation</div>
                {result.passkeyItems.filter((item) => item.passkeyMask === previewItem.passkeyMask).map((item) => (
                  <div key={`${item.passkeyMask}-${item.newPasskeyMask ?? "same"}`} className="app-subtle-panel space-y-2">
                    <div className="flex items-center justify-between gap-3">
                      <StatusPill tone={item.succeeded ? "good" : "warn"}>{item.succeeded ? labels.completed : labels.failed}</StatusPill>
                      {item.newPasskeyMask ? <div className="font-mono text-xs text-steel">{item.newPasskeyMask}</div> : null}
                    </div>
                    {item.errorMessage ? <div className="text-sm text-ember">{item.errorMessage}</div> : null}
                    {item.newPasskey ? <div className="rounded-2xl bg-moss/10 px-3 py-3"><div className="text-xs uppercase tracking-[0.18em] text-moss">{labels.newPasskey}</div><div className="mt-1 break-all font-mono text-xs text-moss">{item.newPasskey}</div></div> : null}
                  </div>
                ))}
              </div>
            ) : null}
          </div>
        ) : null}
      </PreviewDrawer>
    </div>
  );
}

function BansPage({
  accessToken,
  onReauthenticate,
  capabilities
}: {
  accessToken: string;
  onReauthenticate: (fresh: boolean) => Promise<void>;
  capabilities: CapabilityDto[];
}) {
  const { dictionary } = useI18n();
  const labels = dictionary.bans;
  const [view, setView] = useCatalogViewState({
    search: "",
    filter: "all",
    sort: "scope:asc",
    page: 1,
    pageSize: 25
  });
  const { query, modal, id } = view;
  const [items, setItems] = useState<BanRuleAdminDto[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [status, setStatus] = useState<string | null>(null);
  const emptyForm: BanFormState = {
    scope: "user",
    subject: "",
    reason: "",
    expiresAtLocal: "",
    expectedVersion: undefined
  };
  const [form, setForm] = useState<BanFormState>(emptyForm);
  const [confirmDeleteOpen, setConfirmDeleteOpen] = useState(false);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [isLoading, setIsLoading] = useState(true);

  const canWrite = hasGrantedCapability(capabilities, "admin.write.ban");
  const canExpire = hasGrantedCapability(capabilities, "admin.expire.ban");
  const canDelete = hasGrantedCapability(capabilities, "admin.delete.ban");

  const reload = async () => {
      const page = await apiRequest<PageResult<BanRuleAdminDto>>(`/api/admin/bans?${buildGridQueryString(query)}`, accessToken, onReauthenticate);
      setItems(page.items);
      setTotalCount(page.totalCount);
  };

  useEffect(() => {
    let isMounted = true;

    setIsLoading(true);
    reload()
      .then(() => {
        if (isMounted) {
          setError(null);
        }
      })
      .catch((requestError) => {
        if (isMounted) {
          setError(requestError instanceof Error ? requestError.message : labels.loadError);
          setItems([]);
          setTotalCount(0);
        }
      })
      .finally(() => {
        if (isMounted) {
          setIsLoading(false);
        }
      });

    return () => {
      isMounted = false;
    };
  }, [accessToken, onReauthenticate, query]);

  useEffect(() => {
    if (modal !== "edit") {
      return;
    }

    const record = tryParseBanRecordId(id);
    if (!record) {
      setView((current) => ({ ...current, modal: null, id: null }));
      return;
    }

    const item = items.find((candidate) => candidate.scope === record.scope && candidate.subject === record.subject);
    if (!item) {
      setView((current) => ({ ...current, modal: null, id: null }));
      return;
    }

    setForm({
      scope: item.scope,
      subject: item.subject,
      reason: item.reason,
      expiresAtLocal: toLocalDateTimeInput(item.expiresAtUtc),
      expectedVersion: item.version
    });
  }, [id, items, modal, setView]);

  const fillForm = (item: BanRuleAdminDto) => {
    setForm({
      scope: item.scope,
      subject: item.subject,
      reason: item.reason,
      expiresAtLocal: toLocalDateTimeInput(item.expiresAtUtc),
      expectedVersion: item.version
    });
    setView((current) => ({ ...current, modal: "edit", id: toBanRecordId(item.scope, item.subject) }));
    setStatus(formatText(labels.loadedToEditor, { scope: item.scope, subject: item.subject }));
    setError(null);
  };

  const openCreate = () => {
    setForm(emptyForm);
    setView((current) => ({ ...current, modal: "create", id: null }));
    setError(null);
    setStatus(null);
  };

  const saveBan = async () => {
    try {
      setIsSubmitting(true);
      setStatus(null);
      setError(null);
      await apiMutation<BanRuleAdminDto, { reason: string; expiresAtUtc: string | null; expectedVersion?: number }>(
        `/api/admin/bans/${encodeURIComponent(form.scope)}/${encodeURIComponent(form.subject)}`,
        "PUT",
        accessToken,
        {
          reason: form.reason,
          expiresAtUtc: fromLocalDateTimeInput(form.expiresAtLocal),
          expectedVersion: form.expectedVersion
        },
        onReauthenticate
      );

      setStatus(labels.saveStatus);
      setView((current) => ({ ...current, modal: null, id: null }));
      await reload();
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : labels.saveError);
    } finally {
      setIsSubmitting(false);
    }
  };

  const expireBan = async () => {
    try {
      setIsSubmitting(true);
      setStatus(null);
      setError(null);
      const expiresAtUtc = fromLocalDateTimeInput(form.expiresAtLocal);
      if (!expiresAtUtc) {
        throw new Error(labels.expiryRequired);
      }

      await apiMutation<BulkOperationResultDto, { items: Array<{ scope: string; subject: string; expiresAtUtc: string; expectedVersion?: number }> }>(
        "/api/admin/bans/bulk/expire",
        "POST",
        accessToken,
        {
          items: [
            {
              scope: form.scope,
              subject: form.subject,
              expiresAtUtc,
              expectedVersion: form.expectedVersion
            }
          ]
        },
        onReauthenticate
      );

      setStatus(labels.expireStatus);
      setView((current) => ({ ...current, modal: null, id: null }));
      await reload();
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : labels.expireError);
    } finally {
      setIsSubmitting(false);
    }
  };

  const deleteBan = async () => {
    try {
      setIsSubmitting(true);
      setStatus(null);
      setError(null);
      await apiMutation<BulkOperationResultDto, { items: Array<{ scope: string; subject: string; expectedVersion?: number }> }>(
        "/api/admin/bans/bulk/delete",
        "POST",
        accessToken,
        {
          items: [
            {
              scope: form.scope,
              subject: form.subject,
              expectedVersion: form.expectedVersion
            }
          ]
        },
        onReauthenticate
      );

      setStatus(labels.deleteStatus);
      setForm(emptyForm);
      setView((current) => ({ ...current, modal: null, id: null }));
      await reload();
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : labels.deleteError);
    } finally {
      setIsSubmitting(false);
    }
  };

  const [activeSortField, activeSortDirection = "asc"] = query.sort.split(":");
  const pageCount = Math.max(1, Math.ceil(totalCount / query.pageSize));

  return (
    <div className="space-y-6">
      <CatalogToolbar
        title={labels.cardTitle}
        description="Search and manage enforcement rules."
        totalCount={totalCount}
        search={query.search}
        onSearchChange={(value) => setView((current) => ({ ...current, query: { ...current.query, search: value, page: 1 } }))}
        searchPlaceholder="Search rules"
        filter={query.filter}
        onFilterChange={(value) => setView((current) => ({ ...current, query: { ...current.query, filter: value, page: 1 } }))}
        filterOptions={[
          { value: "all", label: "All rules" },
          { value: "active", label: "Active" },
          { value: "expired", label: "Expired" }
        ]}
        sortValue={query.sort}
        onSortChange={(value) => setView((current) => ({ ...current, query: { ...current.query, sort: value, page: 1 } }))}
        sortOptions={[
          { value: "scope:asc", label: "Scope A-Z" },
          { value: "subject:asc", label: "Subject A-Z" },
          { value: "expires:desc", label: "Expiry latest first" },
          { value: "expires:asc", label: "Expiry earliest first" }
        ]}
        pageSize={query.pageSize}
        onPageSizeChange={(value) => setView((current) => ({ ...current, query: { ...current.query, pageSize: value, page: 1 } }))}
        createLabel="Create rule"
        onCreate={openCreate}
      />
      {status ? <div className="app-notice-success">{status}</div> : null}
      {error ? <div className="app-notice-warn">{error}</div> : null}

      <div className="app-data-table">
        <div className="overflow-x-auto">
          <table className="min-w-full text-sm">
            <thead className="app-table-head">
              <tr>
                <th className="px-5 py-4"><SortHeaderButton label={labels.scope} active={activeSortField === "scope"} direction={activeSortDirection as "asc" | "desc"} onClick={() => setView((current) => ({ ...current, query: { ...current.query, sort: activeSortField === "scope" && activeSortDirection === "asc" ? "scope:desc" : "scope:asc", page: 1 } }))} /></th>
                <th className="px-5 py-4"><SortHeaderButton label={labels.subject} active={activeSortField === "subject"} direction={activeSortDirection as "asc" | "desc"} onClick={() => setView((current) => ({ ...current, query: { ...current.query, sort: activeSortField === "subject" && activeSortDirection === "asc" ? "subject:desc" : "subject:asc", page: 1 } }))} /></th>
                <th className="px-5 py-4">{labels.reason}</th>
                <th className="px-5 py-4"><SortHeaderButton label={labels.expiresLabel} active={activeSortField === "expires"} direction={activeSortDirection as "asc" | "desc"} onClick={() => setView((current) => ({ ...current, query: { ...current.query, sort: activeSortField === "expires" && activeSortDirection === "asc" ? "expires:desc" : "expires:asc", page: 1 } }))} /></th>
                <th className="px-5 py-4">{dictionary.common.version}</th>
                <th className="px-5 py-4 text-right">Actions</th>
              </tr>
            </thead>
            <tbody>
              {isLoading ? (
                <TableStateRow colSpan={6} title="Loading ban rules" message="Refreshing the enforcement catalog from the backend." />
              ) : error && totalCount === 0 ? (
                <TableStateRow colSpan={6} title="Unable to load ban rules" message={error} />
              ) : items.length === 0 ? (
                <TableStateRow colSpan={6} title="No ban rules match this view" message="Try a broader search or switch the current filter." />
              ) : items.map((item) => (
                <CatalogTableRow key={`${item.scope}:${item.subject}`} onOpen={() => fillForm(item)}>
                  <td className="px-5 py-4 font-semibold text-ink">{item.scope}</td>
                  <td className="px-5 py-4">
                    <div className="font-mono text-xs text-ink">{item.subject}</div>
                    <div className="app-inline-id">
                      <CopyValueButton value={item.subject} label={`Copy ban subject ${item.subject}`} />
                    </div>
                  </td>
                  <td className="px-5 py-4 text-steel">{item.reason}</td>
                  <td className="px-5 py-4 text-steel">{item.expiresAtUtc ? new Date(item.expiresAtUtc).toLocaleString() : dictionary.common.never}</td>
                  <td className="px-5 py-4 text-steel">{item.version}</td>
                  <td className="px-5 py-4 text-right">
                    <div className="flex justify-end items-center gap-2">
                      <button type="button" className="app-button-secondary py-2.5" onClick={() => fillForm(item)}>Edit</button>
                      <RowActionsMenu
                        items={[
                          {
                            label: "Delete",
                            tone: "danger",
                            disabled: !canDelete,
                            onClick: () => {
                              fillForm(item);
                              setConfirmDeleteOpen(true);
                            }
                          }
                        ]}
                      />
                    </div>
                  </td>
                </CatalogTableRow>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      <PaginationFooter
        page={query.page}
        pageCount={pageCount}
        totalCount={totalCount}
        pageSize={query.pageSize}
        onPageChange={(page) => setView((current) => ({ ...current, query: { ...current.query, page } }))}
      />

      <Modal
        open={modal === "create" || modal === "edit"}
        onClose={() => setView((current) => ({ ...current, modal: null, id: null }))}
        title={form.expectedVersion ? "Edit ban rule" : "Create ban rule"}
        description="Create, expire or update enforcement rules without leaving the catalog."
        width="wide"
      >
        <div className="space-y-4">
          <div className="app-form-grid">
            <input className="app-input" placeholder={labels.scope} value={form.scope} onChange={(event) => setForm((current) => ({ ...current, scope: event.target.value }))} />
            <input className="app-input" placeholder={labels.subject} value={form.subject} onChange={(event) => setForm((current) => ({ ...current, subject: event.target.value }))} />
          </div>
          <textarea className="app-input min-h-32" placeholder={labels.reason} value={form.reason} onChange={(event) => setForm((current) => ({ ...current, reason: event.target.value }))} />
          <input className="app-input" type="datetime-local" value={form.expiresAtLocal} onChange={(event) => setForm((current) => ({ ...current, expiresAtLocal: event.target.value }))} />
          <div className="flex flex-wrap justify-end gap-3">
            {form.expectedVersion ? (
              <button type="button" className="app-button-secondary" disabled={!canExpire || isSubmitting || !form.scope.trim() || !form.subject.trim()} onClick={() => void expireBan()}>
                {labels.expireBan}
              </button>
            ) : null}
            <ModalDismissButton onClose={() => setView((current) => ({ ...current, modal: null, id: null }))}>Cancel</ModalDismissButton>
            <button type="button" className="app-button-primary" disabled={!canWrite || isSubmitting || !form.scope.trim() || !form.subject.trim() || !form.reason.trim()} onClick={() => void saveBan()}>
              {form.expectedVersion ? labels.saveBan : "Create rule"}
            </button>
          </div>
        </div>
      </Modal>

      <ConfirmActionModal
        open={confirmDeleteOpen}
        title="Delete ban rule"
        description={`Delete rule ${form.scope}:${form.subject}? This cannot be undone.`}
        confirmLabel="Delete rule"
        onClose={() => setConfirmDeleteOpen(false)}
        onConfirm={() => {
          setConfirmDeleteOpen(false);
          void deleteBan();
        }}
      />
    </div>
  );
}

function TrackerAccessPage({
  accessToken,
  onReauthenticate,
  capabilities
}: {
  accessToken: string;
  onReauthenticate: (fresh: boolean) => Promise<void>;
  capabilities: CapabilityDto[];
}) {
  const { dictionary } = useI18n();
  const labels = dictionary.permissionsPage;
  const [view, setView] = useCatalogViewState({
    search: "",
    filter: "all",
    sort: "userid:asc",
    page: 1,
    pageSize: 25
  });
  const { query, preview, modal, id } = view;
  const [items, setItems] = useState<TrackerAccessAdminDto[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [status, setStatus] = useState<string | null>(null);
  const [result, setResult] = useState<BulkOperationResultDto | null>(null);
  const [selectedUserIds, setSelectedUserIds] = useState<string[]>([]);
  const [form, setForm] = useState<TrackerAccessFormState>({
    userId: "",
    canLeech: true,
    canSeed: true,
    canScrape: true,
    canUsePrivateTracker: true,
    expectedVersion: undefined
  });
  const [editorMode, setEditorMode] = useState<"single" | "bulk">("single");
  const [isBulkModalOpen, setIsBulkModalOpen] = useState(false);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [isLoading, setIsLoading] = useState(true);
  const canWrite = hasGrantedCapability(capabilities, "admin.write.permissions");
  const trackerAccessItems = result?.trackerAccessItems ?? result?.permissionItems ?? [];
  const previewItem = items.find((item) => item.userId === preview) ?? null;
  const [activeSortField, activeSortDirection = "asc"] = query.sort.split(":");
  const pageCount = Math.max(1, Math.ceil(totalCount / query.pageSize));

  const reload = async () => {
    const page = await apiRequest<PageResult<TrackerAccessAdminDto>>(
      `/api/admin/tracker-access?${buildGridQueryString(query)}`,
      accessToken,
      onReauthenticate
    );
    setItems(page.items);
    setTotalCount(page.totalCount);
    setSelectedUserIds((current) => current.filter((userId) => page.items.some((item) => item.userId === userId)));
  };

  useEffect(() => {
    let isMounted = true;

    setIsLoading(true);
    reload()
      .then(() => {
        if (isMounted) {
          setError(null);
        }
      })
      .catch((requestError) => {
        if (isMounted) {
          setError(requestError instanceof Error ? requestError.message : labels.loadError);
          setItems([]);
          setTotalCount(0);
        }
      })
      .finally(() => {
        if (isMounted) {
          setIsLoading(false);
        }
      });

    return () => {
      isMounted = false;
    };
  }, [accessToken, onReauthenticate, query]);

  useEffect(() => {
    if (preview && !items.some((item) => item.userId === preview)) {
      setView((current) => ({ ...current, preview: null }));
    }
  }, [items, preview, setView]);

  useEffect(() => {
    if (modal !== "edit") {
      return;
    }

    if (!id) {
      setView((current) => ({ ...current, modal: null, id: null }));
      return;
    }

    const item = items.find((candidate) => candidate.userId === id);
    if (!item) {
      setView((current) => ({ ...current, modal: null, id: null }));
      return;
    }

    setForm(toTrackerAccessForm(item));
    setEditorMode("single");
  }, [id, items, modal, setView]);

  const loadUser = (item: TrackerAccessAdminDto) => {
    setForm(toTrackerAccessForm(item));
    setEditorMode("single");
    setView((current) => ({ ...current, modal: "edit", id: item.userId }));
    setStatus(formatText(labels.loadedStatus, { userId: item.userId }));
    setError(null);
  };

  const openCreate = () => {
    setForm({
      userId: "",
      canLeech: true,
      canSeed: true,
      canScrape: true,
      canUsePrivateTracker: true,
      expectedVersion: undefined
    });
    setEditorMode("single");
    setView((current) => ({ ...current, modal: "create", id: null }));
    setError(null);
    setStatus(null);
  };

  const openBulkEditor = () => {
    setForm((current) => ({
      ...current,
      userId: "",
      expectedVersion: undefined
    }));
    setEditorMode("bulk");
    setIsBulkModalOpen(true);
    setView((current) => ({ ...current, modal: null, id: null }));
    setError(null);
  };

  const toggleSelection = (userId: string) => {
    setSelectedUserIds((current) =>
      current.includes(userId) ? current.filter((value) => value !== userId) : [...current, userId]
    );
  };

  const savePermissions = async () => {
    try {
      setIsSubmitting(true);
      setStatus(null);
      setError(null);
      setResult(null);
      await apiMutation<TrackerAccessAdminDto, Omit<TrackerAccessFormState, "userId">>(
        `/api/admin/users/${encodeURIComponent(form.userId)}/tracker-access`,
        "PUT",
        accessToken,
        {
          canLeech: form.canLeech,
          canSeed: form.canSeed,
          canScrape: form.canScrape,
          canUsePrivateTracker: form.canUsePrivateTracker,
          expectedVersion: form.expectedVersion
        },
        onReauthenticate
      );

      setStatus(labels.saveStatus);
      setView((current) => ({ ...current, modal: null, id: null }));
      await reload();
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : labels.saveError);
    } finally {
      setIsSubmitting(false);
    }
  };

  const bulkApplyPermissions = async () => {
    try {
      setIsSubmitting(true);
      setStatus(null);
      setError(null);
      setResult(null);
      const selectedItems = items
        .filter((item) => selectedUserIds.includes(item.userId))
        .map((item) => ({
          userId: item.userId,
          canLeech: form.canLeech,
          canSeed: form.canSeed,
          canScrape: form.canScrape,
          canUsePrivateTracker: form.canUsePrivateTracker,
          expectedVersion: item.version
        }));

      const operationResult = await apiMutation<
        BulkOperationResultDto,
        {
          items: Array<{
            userId: string;
            canLeech: boolean;
            canSeed: boolean;
            canScrape: boolean;
            canUsePrivateTracker: boolean;
            expectedVersion: number;
          }>;
        }
      >(
        "/api/admin/users/bulk/tracker-access",
        "PUT",
        accessToken,
        { items: selectedItems },
        onReauthenticate
      );

      setResult(operationResult);
      setStatus(formatText(labels.bulkStatus, { succeeded: operationResult.succeededCount, total: operationResult.totalCount }));
      setIsBulkModalOpen(false);
      setView((current) => ({ ...current, modal: null, id: null }));
      await reload();
      setSelectedUserIds([]);
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : labels.bulkError);
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div className="space-y-6">
      <CatalogToolbar
        title={labels.cardTitle}
        description="Search and manage tracker access assignments."
        totalCount={totalCount}
        search={query.search}
        onSearchChange={(value) => setView((current) => ({ ...current, query: { ...current.query, search: value, page: 1 } }))}
        searchPlaceholder="Search users or access state"
        filter={query.filter}
        onFilterChange={(value) => setView((current) => ({ ...current, query: { ...current.query, filter: value, page: 1 } }))}
        filterOptions={[
          { value: "all", label: "All access" },
          { value: "private", label: "Private tracker" },
          { value: "public", label: "Public only" },
          { value: "seed", label: "Can seed" },
          { value: "leech", label: "Can leech" },
          { value: "scrape", label: "Can scrape" }
        ]}
        sortValue={query.sort}
        onSortChange={(value) => setView((current) => ({ ...current, query: { ...current.query, sort: value, page: 1 } }))}
        sortOptions={[
          { value: "userid:asc", label: "User A-Z" },
          { value: "userid:desc", label: "User Z-A" },
          { value: "private:desc", label: "Private first" },
          { value: "leech:desc", label: "Leech enabled first" },
          { value: "seed:desc", label: "Seed enabled first" },
          { value: "version:desc", label: "Version high-low" }
        ]}
        pageSize={query.pageSize}
        onPageSizeChange={(value) => setView((current) => ({ ...current, query: { ...current.query, pageSize: value, page: 1 } }))}
        createLabel="New tracker access"
        onCreate={openCreate}
      />
      {status ? <div className="app-notice-success">{status}</div> : null}
      {error ? <div className="app-notice-warn">{error}</div> : null}
      {result && trackerAccessItems.length > 0 ? (
        <div className="app-selection-summary">
          <div className="space-y-1">
            <div className="text-sm font-semibold text-ink">Last access update</div>
            <div className="text-sm text-steel">{result.succeededCount} succeeded, {result.failedCount} failed, {result.totalCount} processed</div>
          </div>
          <button type="button" className="app-button-secondary py-2.5" onClick={() => setResult(null)}>Clear result</button>
        </div>
      ) : null}
      {selectedUserIds.length > 0 ? (
        <div className="app-selection-summary">
          <div className="space-y-1">
            <div className="text-sm font-semibold text-ink">{selectedUserIds.length} user(s) selected</div>
            <div className="text-sm text-steel">Apply the current access envelope to the selected users in one modal workflow.</div>
          </div>
          <div className="flex flex-wrap gap-3">
            <button type="button" className="app-button-secondary py-2.5" onClick={() => setSelectedUserIds([])}>Clear selection</button>
            <button type="button" className="app-button-primary py-2.5" disabled={!canWrite} onClick={openBulkEditor}>{labels.applySelected} ({selectedUserIds.length})</button>
          </div>
        </div>
      ) : null}
      <div className="app-data-table">
        <div className="overflow-x-auto">
          <table className="min-w-full text-sm">
            <thead className="app-table-head">
              <tr>
                <th className="w-14 px-5 py-4" />
                <th className="px-5 py-4"><SortHeaderButton label={labels.userId} active={activeSortField === "userid"} direction={activeSortDirection as "asc" | "desc"} onClick={() => setView((current) => ({ ...current, query: { ...current.query, sort: activeSortField === "userid" && activeSortDirection === "asc" ? "userid:desc" : "userid:asc", page: 1 } }))} /></th>
                <th className="px-5 py-4"><SortHeaderButton label={labels.leech} active={activeSortField === "leech"} direction={activeSortDirection as "asc" | "desc"} onClick={() => setView((current) => ({ ...current, query: { ...current.query, sort: activeSortField === "leech" && activeSortDirection === "asc" ? "leech:desc" : "leech:asc", page: 1 } }))} /></th>
                <th className="px-5 py-4"><SortHeaderButton label={labels.seed} active={activeSortField === "seed"} direction={activeSortDirection as "asc" | "desc"} onClick={() => setView((current) => ({ ...current, query: { ...current.query, sort: activeSortField === "seed" && activeSortDirection === "asc" ? "seed:desc" : "seed:asc", page: 1 } }))} /></th>
                <th className="px-5 py-4"><SortHeaderButton label={labels.scrape} active={activeSortField === "scrape"} direction={activeSortDirection as "asc" | "desc"} onClick={() => setView((current) => ({ ...current, query: { ...current.query, sort: activeSortField === "scrape" && activeSortDirection === "asc" ? "scrape:desc" : "scrape:asc", page: 1 } }))} /></th>
                <th className="px-5 py-4"><SortHeaderButton label={labels.privateTracker} active={activeSortField === "private"} direction={activeSortDirection as "asc" | "desc"} onClick={() => setView((current) => ({ ...current, query: { ...current.query, sort: activeSortField === "private" && activeSortDirection === "asc" ? "private:desc" : "private:asc", page: 1 } }))} /></th>
                <th className="px-5 py-4"><SortHeaderButton label={dictionary.common.version} active={activeSortField === "version"} direction={activeSortDirection as "asc" | "desc"} onClick={() => setView((current) => ({ ...current, query: { ...current.query, sort: activeSortField === "version" && activeSortDirection === "asc" ? "version:desc" : "version:asc", page: 1 } }))} /></th>
                <th className="px-5 py-4 text-right">Actions</th>
              </tr>
            </thead>
            <tbody>
              {isLoading ? (
                <TableStateRow colSpan={8} title="Loading tracker access" message="Refreshing tracker access rights from the backend." />
              ) : error && totalCount === 0 ? (
                <TableStateRow colSpan={8} title="Unable to load tracker access" message={error} />
              ) : items.length === 0 ? (
                <TableStateRow colSpan={8} title="No tracker access records match this view" message="Try a broader search or switch the current filter." />
              ) : items.map((item) => (
                <CatalogTableRow key={item.userId} onOpen={() => setView((current) => ({ ...current, preview: item.userId }))}>
                  <td className="px-5 py-4"><input type="checkbox" checked={selectedUserIds.includes(item.userId)} onChange={() => toggleSelection(item.userId)} /></td>
                  <td className="px-5 py-4">
                    <div className="font-mono text-xs font-semibold text-ink">{item.userId}</div>
                    <div className="app-inline-id">
                      <CopyValueButton value={item.userId} label={`Copy user id ${item.userId}`} />
                    </div>
                  </td>
                  <td className="px-5 py-4"><StatusPill tone={item.canLeech ? "good" : "neutral"}>{formatBool(item.canLeech, dictionary)}</StatusPill></td>
                  <td className="px-5 py-4"><StatusPill tone={item.canSeed ? "good" : "neutral"}>{formatBool(item.canSeed, dictionary)}</StatusPill></td>
                  <td className="px-5 py-4"><StatusPill tone={item.canScrape ? "good" : "neutral"}>{formatBool(item.canScrape, dictionary)}</StatusPill></td>
                  <td className="px-5 py-4"><StatusPill tone={item.canUsePrivateTracker ? "good" : "neutral"}>{formatBool(item.canUsePrivateTracker, dictionary)}</StatusPill></td>
                  <td className="px-5 py-4 text-steel">{item.version}</td>
                  <td className="px-5 py-4 text-right">
                    <div className="flex justify-end items-center gap-2">
                      <button type="button" className="app-button-secondary py-2.5" onClick={() => loadUser(item)}>{labels.edit}</button>
                      <RowActionsMenu items={[{ label: "Preview", onClick: () => setView((current) => ({ ...current, preview: item.userId })) }]} />
                    </div>
                  </td>
                </CatalogTableRow>
              ))}
            </tbody>
          </table>
        </div>
      </div>
      <PaginationFooter page={query.page} pageCount={pageCount} totalCount={totalCount} pageSize={query.pageSize} onPageChange={(page) => setView((current) => ({ ...current, query: { ...current.query, page } }))} />
      <Modal open={editorMode === "bulk" ? isBulkModalOpen : modal === "create" || modal === "edit"} onClose={() => { setIsBulkModalOpen(false); setView((current) => ({ ...current, modal: null, id: null })); }} title={editorMode === "bulk" ? "Apply tracker access to selected users" : form.expectedVersion ? labels.editorTitle : "Create tracker access"} description={editorMode === "bulk" ? "Use one access envelope for the current selection." : "Edit tracker access rights without leaving the catalog."} width="wide">
        <div className="space-y-4">
          {editorMode === "bulk" ? (
            <div className="app-subtle-panel space-y-2"><div className="app-kicker">Selection</div><div className="font-semibold text-ink">{selectedUserIds.length} user(s)</div></div>
          ) : (
            <label className="space-y-2"><span className="text-sm font-medium text-ink">{labels.userId}</span><input className="app-input font-mono text-sm" value={form.userId} onChange={(event) => setForm((current) => ({ ...current, userId: event.target.value }))} /></label>
          )}
          <div className="grid gap-3 md:grid-cols-2">
            <label className="flex items-center gap-3 rounded-2xl border border-ink/10 bg-slate-50 px-4 py-3 text-sm text-ink"><input type="checkbox" checked={form.canLeech} onChange={(event) => setForm((current) => ({ ...current, canLeech: event.target.checked }))} />{labels.canLeech}</label>
            <label className="flex items-center gap-3 rounded-2xl border border-ink/10 bg-slate-50 px-4 py-3 text-sm text-ink"><input type="checkbox" checked={form.canSeed} onChange={(event) => setForm((current) => ({ ...current, canSeed: event.target.checked }))} />{labels.canSeed}</label>
            <label className="flex items-center gap-3 rounded-2xl border border-ink/10 bg-slate-50 px-4 py-3 text-sm text-ink"><input type="checkbox" checked={form.canScrape} onChange={(event) => setForm((current) => ({ ...current, canScrape: event.target.checked }))} />{labels.canScrape}</label>
            <label className="flex items-center gap-3 rounded-2xl border border-ink/10 bg-slate-50 px-4 py-3 text-sm text-ink"><input type="checkbox" checked={form.canUsePrivateTracker} onChange={(event) => setForm((current) => ({ ...current, canUsePrivateTracker: event.target.checked }))} />{labels.canUsePrivateTracker}</label>
          </div>
          <div className="flex flex-wrap justify-end gap-3">
            <ModalDismissButton onClose={() => { setIsBulkModalOpen(false); setView((current) => ({ ...current, modal: null, id: null })); }}>Cancel</ModalDismissButton>
            <button type="button" disabled={!canWrite || isSubmitting || (editorMode === "single" ? !form.userId.trim() : selectedUserIds.length === 0)} className="app-button-primary" onClick={() => void (editorMode === "bulk" ? bulkApplyPermissions() : savePermissions())}>
              {editorMode === "bulk" ? `${labels.applySelected} (${selectedUserIds.length})` : labels.savePermissions}
            </button>
          </div>
        </div>
      </Modal>
      <PreviewDrawer open={previewItem != null} title={previewItem?.userId ?? ""} subtitle="Current tracker access rights" onClose={() => setView((current) => ({ ...current, preview: null }))}>
        {previewItem ? (
          <div className="space-y-4">
            <div className="app-detail-grid">
              <div className="app-subtle-panel space-y-2"><div className="app-kicker">{labels.leech}</div><StatusPill tone={previewItem.canLeech ? "good" : "neutral"}>{formatBool(previewItem.canLeech, dictionary)}</StatusPill></div>
              <div className="app-subtle-panel space-y-2"><div className="app-kicker">{labels.seed}</div><StatusPill tone={previewItem.canSeed ? "good" : "neutral"}>{formatBool(previewItem.canSeed, dictionary)}</StatusPill></div>
              <div className="app-subtle-panel space-y-2"><div className="app-kicker">{labels.scrape}</div><StatusPill tone={previewItem.canScrape ? "good" : "neutral"}>{formatBool(previewItem.canScrape, dictionary)}</StatusPill></div>
              <div className="app-subtle-panel space-y-2"><div className="app-kicker">{labels.privateTracker}</div><StatusPill tone={previewItem.canUsePrivateTracker ? "good" : "neutral"}>{formatBool(previewItem.canUsePrivateTracker, dictionary)}</StatusPill></div>
            </div>
            <div className="app-subtle-panel space-y-2"><div className="app-kicker">Version</div><div className="font-semibold text-ink">{previewItem.version}</div></div>
            {trackerAccessItems.some((item) => item.userId === previewItem.userId) ? (
              <div className="space-y-3">
                <div className="app-kicker">Latest operation</div>
                {trackerAccessItems.filter((item) => item.userId === previewItem.userId).map((item) => (
                  <div key={item.userId} className="app-subtle-panel space-y-2">
                    <StatusPill tone={item.succeeded ? "good" : "warn"}>{item.succeeded ? labels.succeeded : labels.failed}</StatusPill>
                    {item.errorMessage ? <div className="text-sm text-ember">{item.errorMessage}</div> : null}
                  </div>
                ))}
              </div>
            ) : null}
          </div>
        ) : null}
      </PreviewDrawer>
    </div>
  );
}

function AuditPage({
  accessToken,
  onReauthenticate
}: {
  accessToken: string;
  onReauthenticate: (fresh: boolean) => Promise<void>;
}) {
  const { dictionary } = useI18n();
  const labels = dictionary.audit;
  const [view, setView] = useCatalogViewState({
    search: "",
    filter: "all",
    sort: "occurred:desc",
    page: 1,
    pageSize: 25
  });
  const { query, preview } = view;
  const [items, setItems] = useState<AuditRecordDto[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const previewItem = items.find((item) => item.id === preview) ?? null;

  const toggleSort = (field: string) => {
    setView((current) => {
      const [activeField, activeDirection] = current.query.sort.split(":");
      if (activeField === field) {
        return { ...current, query: { ...current.query, sort: `${field}:${activeDirection === "asc" ? "desc" : "asc"}`, page: 1 } };
      }

      return { ...current, query: { ...current.query, sort: `${field}:asc`, page: 1 } };
    });
  };

  useEffect(() => {
    let isMounted = true;

    setIsLoading(true);
    apiRequest<PageResult<AuditRecordDto>>(`/api/admin/audit?${buildGridQueryString(query)}`, accessToken, onReauthenticate)
      .then((page) => {
        if (isMounted) {
          setItems(page.items);
          setTotalCount(page.totalCount);
          setError(null);
        }
      })
      .catch((requestError) => {
        if (isMounted) {
          setError(requestError instanceof Error ? requestError.message : labels.loadError);
          setItems([]);
          setTotalCount(0);
        }
      })
      .finally(() => {
        if (isMounted) {
          setIsLoading(false);
        }
      });

    return () => {
      isMounted = false;
    };
  }, [accessToken, onReauthenticate, query]);

  useEffect(() => {
    if (preview && !items.some((item) => item.id === preview)) {
      setView((current) => ({ ...current, preview: null }));
    }
  }, [items, preview, setView]);

  const [activeSortField, activeSortDirection = "desc"] = query.sort.split(":");
  const pageCount = Math.max(1, Math.ceil(totalCount / query.pageSize));

  return (
    <div className="space-y-6">
      <CatalogToolbar
        title={labels.title}
        description="Search audit events and inspect details without leaving the log."
        totalCount={totalCount}
        search={query.search}
        onSearchChange={(value) => setView((current) => ({ ...current, query: { ...current.query, search: value, page: 1 } }))}
        searchPlaceholder="Search actor, action or target"
        filter={query.filter}
        onFilterChange={(value) => setView((current) => ({ ...current, query: { ...current.query, filter: value, page: 1 } }))}
        filterOptions={[
          { value: "all", label: "All events" },
          { value: "success", label: "Successful" },
          { value: "failure", label: "Failed" },
          { value: "warn", label: "Warnings" },
          { value: "error", label: "Errors" }
        ]}
        sortValue={query.sort}
        onSortChange={(value) => setView((current) => ({ ...current, query: { ...current.query, sort: value, page: 1 } }))}
        sortOptions={[
          { value: "occurred:desc", label: "Newest first" },
          { value: "occurred:asc", label: "Oldest first" },
          { value: "action:asc", label: "Action A-Z" },
          { value: "severity:desc", label: "Severity high-low" },
          { value: "actor:asc", label: "Actor A-Z" }
        ]}
        pageSize={query.pageSize}
        onPageSizeChange={(value) => setView((current) => ({ ...current, query: { ...current.query, pageSize: value, page: 1 } }))}
      />

      <div className="app-data-table">
        <div className="overflow-x-auto">
          <table className="min-w-full text-sm">
            <thead className="app-table-head">
              <tr>
                <th className="px-5 py-4"><SortHeaderButton label={labels.tableAction} active={activeSortField === "action"} direction={activeSortDirection as "asc" | "desc"} onClick={() => toggleSort("action")} /></th>
                <th className="px-5 py-4"><SortHeaderButton label={labels.tableActor} active={activeSortField === "actor"} direction={activeSortDirection as "asc" | "desc"} onClick={() => toggleSort("actor")} /></th>
                <th className="px-5 py-4">{labels.tableRole}</th>
                <th className="px-5 py-4"><SortHeaderButton label={labels.tableSeverity} active={activeSortField === "severity"} direction={activeSortDirection as "asc" | "desc"} onClick={() => toggleSort("severity")} /></th>
                <th className="px-5 py-4">{labels.entity}</th>
                <th className="px-5 py-4">{labels.result}</th>
                <th className="px-5 py-4"><SortHeaderButton label={labels.occurred} active={activeSortField === "occurred"} direction={activeSortDirection as "asc" | "desc"} onClick={() => toggleSort("occurred")} /></th>
                <th className="px-5 py-4 text-right">Actions</th>
              </tr>
            </thead>
            <tbody>
              {isLoading ? (
                <TableStateRow colSpan={8} title="Loading audit log" message="Refreshing the latest operational and access events." />
              ) : error && totalCount === 0 ? (
                <TableStateRow colSpan={8} title="Unable to load audit log" message={error} />
              ) : items.length === 0 ? (
                <TableStateRow colSpan={8} title="No audit events match this view" message="Try a broader search or relax the current filter." />
              ) : items.map((item) => (
                <CatalogTableRow key={item.id} onOpen={() => setView((current) => ({ ...current, preview: item.id }))}>
                  <td className="px-5 py-4 font-semibold text-ink">{item.action}</td>
                  <td className="px-5 py-4 font-mono text-xs text-ink">{item.actorId}</td>
                  <td className="px-5 py-4 text-steel">{item.actorRole}</td>
                  <td className="px-5 py-4">
                    <span className={item.severity === "error" || item.severity === "high" ? "app-chip-warn" : "app-chip-soft"}>{item.severity}</span>
                  </td>
                  <td className="px-5 py-4 text-steel">{item.entityType} / {item.entityId}</td>
                  <td className="px-5 py-4 text-steel">{item.result}</td>
                  <td className="px-5 py-4">
                    <div className="text-steel">{new Date(item.occurredAtUtc).toLocaleString()}</div>
                    <div className="app-inline-id">
                      <span className="font-mono">{item.correlationId || "N/A"}</span>
                      {item.correlationId ? <CopyValueButton value={item.correlationId} label={`Copy correlation id ${item.correlationId}`} /> : null}
                    </div>
                  </td>
                  <td className="px-5 py-4 text-right">
                    <RowActionsMenu items={[{ label: "View", onClick: () => setView((current) => ({ ...current, preview: item.id })) }]} />
                  </td>
                </CatalogTableRow>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      <PaginationFooter
        page={query.page}
        pageCount={pageCount}
        totalCount={totalCount}
        pageSize={query.pageSize}
        onPageChange={(page) => setView((current) => ({ ...current, query: { ...current.query, page } }))}
      />

      <PreviewDrawer
        open={previewItem !== null}
        title={previewItem?.action ?? ""}
        subtitle={previewItem ? `${previewItem.entityType} / ${previewItem.entityId}` : undefined}
        onClose={() => setView((current) => ({ ...current, preview: null }))}
      >
        {previewItem ? (
          <div className="space-y-4">
            <div className="app-detail-grid">
              <div className="app-subtle-panel space-y-2">
                <div className="app-kicker">Actor</div>
                <div className="font-semibold text-ink">{previewItem.actorId}</div>
                <div className="text-sm text-steel">{previewItem.actorRole}</div>
              </div>
              <div className="app-subtle-panel space-y-2">
                <div className="app-kicker">Result</div>
                <div className="font-semibold text-ink">{previewItem.result}</div>
                <div className="text-sm text-steel">{previewItem.severity}</div>
              </div>
              <div className="app-subtle-panel space-y-2">
                <div className="app-kicker">Occurred</div>
                <div className="font-semibold text-ink">{new Date(previewItem.occurredAtUtc).toLocaleString()}</div>
              </div>
            </div>
            <div className="app-subtle-panel space-y-2">
              <div className="app-kicker">Correlation</div>
              <div className="font-mono text-xs text-steel">{previewItem.correlationId || "N/A"}</div>
            </div>
            <div className="app-subtle-panel space-y-2">
              <div className="app-kicker">Origin</div>
              <div className="text-sm text-steel">IP: {previewItem.ipAddress || "Unknown"}</div>
            </div>
          </div>
        ) : null}
      </PreviewDrawer>
    </div>
  );
}

function MaintenancePage({
  accessToken,
  onReauthenticate
}: {
  accessToken: string;
  onReauthenticate: (fresh: boolean) => Promise<void>;
}) {
  const { dictionary } = useI18n();
  const labels = dictionary.maintenance;
  const [view, setView] = useCatalogViewState({
    search: "",
    filter: "all",
    sort: "requested:desc",
    page: 1,
    pageSize: 25
  });
  const { query, preview } = view;
  const [items, setItems] = useState<MaintenanceRunDto[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const previewItem = items.find((item) => item.id === preview) ?? null;
  const [activeSortField, activeSortDirection = "desc"] = query.sort.split(":");
  const pageCount = Math.max(1, Math.ceil(totalCount / query.pageSize));

  useEffect(() => {
    let isMounted = true;

    setIsLoading(true);
    apiRequest<PageResult<MaintenanceRunDto>>(
      `/api/admin/maintenance?${buildGridQueryString(query)}`,
      accessToken,
      onReauthenticate
    )
      .then((page) => {
        if (!isMounted) {
          return;
        }
        setItems(page.items);
        setTotalCount(page.totalCount);
        setError(null);
      })
      .catch((requestError) => {
        if (!isMounted) {
          return;
        }

        setError(requestError instanceof Error ? requestError.message : labels.loadError);
        setItems([]);
        setTotalCount(0);
      })
      .finally(() => {
        if (isMounted) {
          setIsLoading(false);
        }
      });

    return () => {
      isMounted = false;
    };
  }, [accessToken, labels.loadError, onReauthenticate, query]);

  useEffect(() => {
    if (preview && !items.some((item) => item.id === preview)) {
      setView((current) => ({ ...current, preview: null }));
    }
  }, [items, preview, setView]);

  const toggleSort = (field: string) => {
    const nextDirection =
      activeSortField === field && activeSortDirection === "asc"
        ? "desc"
        : "asc";
    setView((current) => ({
      ...current,
      query: { ...current.query, sort: `${field}:${nextDirection}`, page: 1 }
    }));
  };

  return (
    <div className="space-y-6">
      <CatalogToolbar
        title={labels.title}
        description="Review maintenance history and inspect operational runs without leaving the admin surface."
        totalCount={totalCount}
        search={query.search}
        onSearchChange={(value) => setView((current) => ({ ...current, query: { ...current.query, search: value, page: 1 } }))}
        searchPlaceholder="Search operation, requester or correlation"
        filter={query.filter}
        onFilterChange={(value) => setView((current) => ({ ...current, query: { ...current.query, filter: value, page: 1 } }))}
        filterOptions={[
          { value: "all", label: "All runs" },
          { value: "completed", label: "Completed" },
          { value: "failed", label: "Failed" },
          { value: "running", label: "Running" }
        ]}
        sortValue={query.sort}
        onSortChange={(value) => setView((current) => ({ ...current, query: { ...current.query, sort: value, page: 1 } }))}
        sortOptions={[
          { value: "requested:desc", label: "Newest first" },
          { value: "requested:asc", label: "Oldest first" },
          { value: "operation:asc", label: "Operation A-Z" },
          { value: "status:asc", label: "Status A-Z" },
          { value: "status:desc", label: "Status Z-A" }
        ]}
        pageSize={query.pageSize}
        onPageSizeChange={(value) => setView((current) => ({ ...current, query: { ...current.query, pageSize: value, page: 1 } }))}
      />

      <div className="app-data-table">
        <div className="overflow-x-auto">
          <table className="min-w-full text-sm">
            <thead className="app-table-head">
              <tr>
                <th className="px-5 py-4">
                  <SortHeaderButton
                    label={labels.tableOperation}
                    active={activeSortField === "operation"}
                    direction={activeSortDirection as "asc" | "desc"}
                    onClick={() => toggleSort("operation")}
                  />
                </th>
                <th className="px-5 py-4">{labels.tableRequestedBy}</th>
                <th className="px-5 py-4">
                  <SortHeaderButton
                    label={labels.tableRequestedAt}
                    active={activeSortField === "requested"}
                    direction={activeSortDirection as "asc" | "desc"}
                    onClick={() => toggleSort("requested")}
                  />
                </th>
                <th className="px-5 py-4">
                  <SortHeaderButton
                    label={labels.tableStatus}
                    active={activeSortField === "status"}
                    direction={activeSortDirection as "asc" | "desc"}
                    onClick={() => toggleSort("status")}
                  />
                </th>
                <th className="px-5 py-4">{labels.tableCorrelation}</th>
                <th className="px-5 py-4 text-right">Actions</th>
              </tr>
            </thead>
            <tbody>
              {isLoading ? (
                <TableStateRow colSpan={6} title="Loading maintenance history" message="Refreshing operational maintenance runs." />
              ) : error && totalCount === 0 ? (
                <TableStateRow colSpan={6} title="Unable to load maintenance history" message={error} />
              ) : items.length === 0 ? (
                <TableStateRow colSpan={6} title={labels.title} message={labels.empty} />
              ) : (
                items.map((item) => (
                  <CatalogTableRow key={item.id} onOpen={() => setView((current) => ({ ...current, preview: item.id }))}>
                    <td className="px-5 py-4 font-semibold text-ink">{item.operation}</td>
                    <td className="px-5 py-4 text-steel">{item.requestedBy}</td>
                    <td className="px-5 py-4 text-steel">{new Date(item.requestedAtUtc).toLocaleString()}</td>
                    <td className="px-5 py-4">
                      <span className={item.status === "failed" ? "app-chip-warn" : item.status === "running" ? "app-chip-strong" : "app-chip-soft"}>
                        {item.status}
                      </span>
                    </td>
                    <td className="px-5 py-4">
                      <div className="font-mono text-xs text-steel">{item.correlationId}</div>
                      <div className="app-inline-id">
                        <CopyValueButton value={item.correlationId} label={`Copy correlation id ${item.correlationId}`} />
                      </div>
                    </td>
                    <td className="px-5 py-4 text-right">
                      <RowActionsMenu items={[{ label: "View", onClick: () => setView((current) => ({ ...current, preview: item.id })) }]} />
                    </td>
                  </CatalogTableRow>
                ))
              )}
            </tbody>
          </table>
        </div>
      </div>

      <PaginationFooter
        page={query.page}
        pageCount={pageCount}
        totalCount={totalCount}
        pageSize={query.pageSize}
        onPageChange={(page) => setView((current) => ({ ...current, query: { ...current.query, page } }))}
      />

      <PreviewDrawer
        open={previewItem !== null}
        title={previewItem?.operation ?? ""}
        subtitle={previewItem?.requestedBy}
        onClose={() => setView((current) => ({ ...current, preview: null }))}
      >
        {previewItem ? (
          <div className="space-y-4">
            <div className="app-detail-grid">
              <div className="app-subtle-panel space-y-2">
                <div className="app-kicker">{labels.tableRequestedBy}</div>
                <div className="font-semibold text-ink">{previewItem.requestedBy}</div>
              </div>
              <div className="app-subtle-panel space-y-2">
                <div className="app-kicker">{labels.tableStatus}</div>
                <div className="font-semibold text-ink">{previewItem.status}</div>
              </div>
              <div className="app-subtle-panel space-y-2">
                <div className="app-kicker">{labels.tableRequestedAt}</div>
                <div className="font-semibold text-ink">{new Date(previewItem.requestedAtUtc).toLocaleString()}</div>
              </div>
            </div>
            <div className="app-subtle-panel space-y-2">
              <div className="app-kicker">{labels.tableCorrelation}</div>
              <div className="font-mono text-xs text-steel">{previewItem.correlationId}</div>
            </div>
            <div className="app-subtle-panel space-y-2">
              <div className="app-kicker">Run id</div>
              <div className="font-mono text-xs text-steel">{previewItem.id}</div>
            </div>
          </div>
        ) : null}
      </PreviewDrawer>
    </div>
  );
}

function CallbackPage({
  manager,
  onSignedIn
}: {
  manager: UserManager;
  onSignedIn: (user: User | null) => void;
}) {
  const { dictionary } = useI18n();
  const navigate = useNavigate();
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let isMounted = true;

    manager
      .signinCallback()
      .then((signedInUser) => {
        if (!isMounted) {
          return;
        }

        onSignedIn(signedInUser);
        if (typeof window !== "undefined") {
          window.sessionStorage.removeItem(adminSignedOutStorageKey);
        }
        const returnTo = typeof signedInUser.state === "object" && signedInUser.state && "returnTo" in signedInUser.state
          ? String((signedInUser.state as { returnTo?: string }).returnTo ?? "/")
          : "/";
        navigate(returnTo, { replace: true });
      })
      .catch((callbackError) => {
        if (isMounted) {
          setError(callbackError instanceof Error ? callbackError.message : dictionary.auth.callbackError);
        }
      });

    return () => {
      isMounted = false;
    };
  }, [manager, navigate, onSignedIn]);

  return (
    <div className="flex min-h-screen items-center justify-center px-6">
      <Card title={dictionary.auth.callbackTitle} eyebrow="OIDC callback">
        <p className="text-sm text-ink/60">{error ?? dictionary.auth.callbackBody}</p>
      </Card>
    </div>
  );
}

function Shell({
  user,
  session,
  onSignin,
  onSignout,
  children
}: {
  user: User;
  session: AdminSessionResponse;
  onSignin: (fresh?: boolean) => Promise<void>;
  onSignout: () => Promise<void>;
  children: React.ReactNode;
}) {
  const { dictionary } = useI18n();
  const location = useLocation();
  const capabilities = session.capabilities ?? [];
  const routeMeta = getRouteMeta(location.pathname, dictionary);
  const [profileMenuOpen, setProfileMenuOpen] = useState(false);
  const [sectionMenuOpen, setSectionMenuOpen] = useState<string | null>(null);
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false);
  const navbarRef = useRef<HTMLElement | null>(null);
  const navigationSections = useMemo(
    (): NavigationSection[] => [
      {
        id: "overview",
        label: "Overview",
        to: "/",
        icon: "overview",
        links: [
          hasPermission(session.permissions, "admin.dashboard.view")
            ? { to: "/", label: dictionary.routes.overviewTitle, description: "Cluster health and access posture.", icon: "overview" }
            : null
        ].filter((link): link is NavigationLink => link !== null)
      },
      {
        id: "access",
        label: "Access Management",
        to: "/admin-users",
        icon: "users",
        links: [
          hasPermission(session.permissions, permissionKeys.usersView)
            ? { to: "/admin-users", label: "Admin users", description: "Provision accounts and assign roles.", icon: "users" }
            : null,
          hasPermission(session.permissions, permissionKeys.rolesView)
            ? { to: "/roles", label: "Roles", description: "Control system and custom admin roles.", icon: "roles" }
            : null,
          hasPermission(session.permissions, permissionKeys.permissionGroupsView)
            ? { to: "/permission-groups", label: "Groups", description: "Bundle permissions into reusable sets.", icon: "groups" }
            : null
        ].filter((link): link is NavigationLink => link !== null)
      },
      {
        id: "tracker",
        label: "Tracker",
        to: "/torrents",
        icon: "torrents",
        links: [
          hasPermission(session.permissions, "admin.torrents.view")
            ? { to: "/torrents", label: "Torrent catalog", description: "Manage tracker mode and rollout policies.", icon: "torrents" }
            : null,
          hasPermission(session.permissions, "admin.passkeys.view")
            ? { to: "/passkeys", label: "Passkeys", description: "Rotate and revoke private tracker credentials.", icon: "passkeys" }
            : null,
          hasPermission(session.permissions, "admin.tracker_access.view")
            ? { to: "/permissions", label: "Tracker access", description: "Control leech, seed, scrape and private access.", icon: "trackerAccess" }
            : null,
          hasPermission(session.permissions, "admin.bans.view")
            ? { to: "/bans", label: "Ban rules", description: "Create and expire enforcement rules.", icon: "bans" }
            : null
        ].filter((link): link is NavigationLink => link !== null)
      },
      {
        id: "audit",
        label: "Audit",
        to: "/audit",
        icon: "audit",
        links: [
          hasPermission(session.permissions, permissionKeys.auditView)
            ? { to: "/audit", label: dictionary.routes.auditTitle, description: "Inspect privileged activity and outcomes.", icon: "audit" }
            : null,
          hasGrantedCapability(capabilities, "admin.read.maintenance")
            ? {
                to: "/maintenance",
                label: dictionary.routes.maintenanceTitle,
                description: "Review maintenance runs and cache refresh history.",
                icon: "maintenance"
              }
            : null
        ].filter((link): link is NavigationLink => link !== null)
      }
    ].filter((section) => section.links.length > 0),
    [capabilities, dictionary, session.permissions]
  );

  const displayName =
    user.profile.name ??
    user.profile.preferred_username ??
    session.userName ??
    user.profile.sub ??
    "admin";
  const role =
    typeof user.profile.role === "string"
      ? user.profile.role
      : Array.isArray(user.profile.role)
        ? user.profile.role[0]
        : session.role || "viewer";

  useEffect(() => {
    setProfileMenuOpen(false);
    setSectionMenuOpen(null);
    setMobileMenuOpen(false);
  }, [location.pathname]);

  useEffect(() => {
    function handlePointerDown(event: MouseEvent) {
      const target = event.target;
      if (!(target instanceof Node)) {
        return;
      }

      if (!navbarRef.current?.contains(target)) {
        setProfileMenuOpen(false);
        setSectionMenuOpen(null);
        setMobileMenuOpen(false);
      }
    }

    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape") {
        setProfileMenuOpen(false);
        setSectionMenuOpen(null);
        setMobileMenuOpen(false);
      }
    }

    document.addEventListener("mousedown", handlePointerDown);
    document.addEventListener("keydown", handleKeyDown);

    return () => {
      document.removeEventListener("mousedown", handlePointerDown);
      document.removeEventListener("keydown", handleKeyDown);
    };
  }, []);

  const activeSection =
    navigationSections.find((section) =>
      section.links.some((link) => link.to === location.pathname || (link.to !== "/" && location.pathname.startsWith(`${link.to}/`)))
    ) ?? (location.pathname === "/profile" ? navigationSections.find((section) => section.id === "access") ?? navigationSections[0] : navigationSections[0]);

  return (
    <div className="app-shell">
      <div className="app-shell-grid">
        <nav ref={navbarRef} className="app-navbar">
          <div className="app-navbar-row">
            <div className="app-navbar-brand">
              <button
                type="button"
                className="app-navbar-mobile-toggle"
                onClick={() => {
                  setMobileMenuOpen((value) => !value);
                  setSectionMenuOpen(null);
                }}
                aria-expanded={mobileMenuOpen}
              >
                <svg viewBox="0 0 24 24" className="h-5 w-5 fill-none stroke-current" strokeWidth="1.8">
                  <path d="M4 7h16" />
                  <path d="M4 12h16" />
                  <path d="M4 17h16" />
                </svg>
                <span>Menu</span>
              </button>
              <NavLink to="/" end className="app-navbar-wordmark">
                <span className="app-navbar-wordmark-mark">BT</span>
                <span className="app-navbar-wordmark-text">BeeTracker</span>
              </NavLink>
              <div className="app-navbar-inline-nav">
                {navigationSections.map((section) => (
                  <div key={section.id} className="relative">
                    {section.links.length === 1 ? (
                      <NavLink
                        to={section.links[0].to}
                        end={section.links[0].to === "/"}
                        className={() => `app-navbar-link ${activeSection?.id === section.id ? "app-navbar-link-active" : "app-navbar-link-idle"}`}
                      >
                        <span className="app-navbar-link-icon">
                          <NavigationItemIcon icon={section.icon} />
                        </span>
                        <span>{section.label}</span>
                      </NavLink>
                    ) : (
                      <button
                        type="button"
                        onClick={() => setSectionMenuOpen((value) => value === section.id ? null : section.id)}
                        className={`app-navbar-link ${activeSection?.id === section.id ? "app-navbar-link-active" : "app-navbar-link-idle"}`}
                        aria-expanded={sectionMenuOpen === section.id}
                      >
                        <span className="app-navbar-link-icon">
                          <NavigationItemIcon icon={section.icon} />
                        </span>
                        <span>{section.label}</span>
                        <svg
                          viewBox="0 0 24 24"
                          className={`h-4 w-4 transition ${sectionMenuOpen === section.id ? "rotate-180" : ""}`}
                          fill="none"
                          stroke="currentColor"
                          strokeWidth="1.8"
                        >
                          <path d="m6 9 6 6 6-6" />
                        </svg>
                      </button>
                    )}
                    {section.links.length > 1 && sectionMenuOpen === section.id ? (
                      <div className="app-navbar-dropdown">
                        <div className="grid gap-1.5 p-2">
                          {section.links.map((link) => (
                            <NavLink
                              key={link.to}
                              to={link.to}
                              end={link.to === "/"}
                              className={({ isActive }) => `app-tools-menu-link ${isActive ? "app-tools-menu-link-active" : "app-tools-menu-link-idle"}`}
                            >
                              <span className="app-tools-menu-icon">
                                <NavigationItemIcon icon={link.icon} />
                              </span>
                              <span className="min-w-0">
                                <span className="block">{link.label}</span>
                                <span className="mt-0.5 block text-xs font-normal text-steel/80">{link.description}</span>
                              </span>
                            </NavLink>
                          ))}
                        </div>
                      </div>
                    ) : null}
                  </div>
                ))}
              </div>
            </div>
            <div className="app-navbar-actions">
              <div className="relative">
                <button
                  type="button"
                  onClick={() => setProfileMenuOpen((value) => !value)}
                  className="app-profile-trigger"
                  aria-expanded={profileMenuOpen}
                >
                  <div className="app-profile-trigger-hive" aria-hidden="true">
                    {Array.from({ length: 3 }, (_, index) => (
                      <span
                        key={`profile-trigger-hive-${index}`}
                        className="app-profile-trigger-hive-cell"
                        style={{ ["--profile-trigger-hive-index" as string]: index } as React.CSSProperties}
                      />
                    ))}
                  </div>
                  <div className="app-profile-avatar">
                    <span>{displayName.slice(0, 2).toUpperCase()}</span>
                  </div>
                  <div className="min-w-0 text-left">
                    <p className="truncate text-sm font-semibold text-white">{displayName}</p>
                    <p className="truncate text-xs text-slate-300">{role}</p>
                  </div>
                  <svg viewBox="0 0 24 24" className={`h-4 w-4 text-honey-300 transition ${profileMenuOpen ? "rotate-180" : ""}`} fill="none" stroke="currentColor" strokeWidth="1.8">
                    <path d="m6 9 6 6 6-6" />
                  </svg>
                </button>
                {profileMenuOpen ? (
                  <div className="app-profile-menu">
                    <div className="border-b border-slate-200/80 px-4 py-3">
                      <p className="text-xs font-semibold uppercase tracking-[0.18em] text-steel/55">{dictionary.common.signedInAs}</p>
                      <p className="mt-2 text-base font-bold text-ink">{displayName}</p>
                      <div className="mt-2 flex flex-wrap gap-2">
                        <StatusPill tone="good">{session.isAuthenticated ? dictionary.common.sessionActive : dictionary.common.sessionMissing}</StatusPill>
                        <span className="app-chip">{session.permissions.length} {dictionary.common.permissions}</span>
                      </div>
                    </div>
                    <div className="grid gap-2 p-3">
                      <Link to="/profile" className="app-profile-menu-link">Open profile</Link>
                      <Link to="/profile" className="app-profile-menu-link">Profile</Link>
                      <div className="app-subtle-panel !rounded-[18px] !px-3 !py-3">
                        <p className="text-[11px] font-semibold uppercase tracking-[0.16em] text-steel/55">Session</p>
                        <p className="mt-2 text-sm font-semibold text-ink">{role}</p>
                        <p className="mt-1 text-sm text-steel">{session.permissions.length} granted permissions</p>
                      </div>
                      <button type="button" onClick={() => void onSignin(true)} className="app-profile-menu-link text-left">
                        {dictionary.common.reauth}
                      </button>
                      <button type="button" onClick={() => void onSignout()} className="app-profile-menu-link text-left text-rose-600">
                        {dictionary.common.signOut}
                      </button>
                    </div>
                  </div>
                ) : null}
              </div>
            </div>
          </div>
          {mobileMenuOpen ? (
            <div className="app-navbar-mobile-panel">
              <div className="grid gap-3">
                {navigationSections.map((section) => (
                  <div key={`mobile-${section.id}`} className="rounded-[22px] border border-white/10 bg-white/5 p-2">
                    {section.links.length === 1 ? (
                      <NavLink
                        to={section.links[0].to}
                        end={section.links[0].to === "/"}
                        className={({ isActive }) => `app-navbar-mobile-link ${isActive ? "app-navbar-mobile-link-active" : "app-navbar-mobile-link-idle"}`}
                      >
                        <span className="app-navbar-link-icon">
                          <NavigationItemIcon icon={section.icon} />
                        </span>
                        <span>{section.label}</span>
                      </NavLink>
                    ) : (
                      <>
                        <button
                          type="button"
                          className={`app-navbar-mobile-link ${sectionMenuOpen === section.id || activeSection?.id === section.id ? "app-navbar-mobile-link-active" : "app-navbar-mobile-link-idle"}`}
                          onClick={() => setSectionMenuOpen((value) => value === section.id ? null : section.id)}
                          aria-expanded={sectionMenuOpen === section.id}
                        >
                          <span className="app-navbar-link-icon">
                            <NavigationItemIcon icon={section.icon} />
                          </span>
                          <span>{section.label}</span>
                          <svg
                            viewBox="0 0 24 24"
                            className={`ml-auto h-4 w-4 transition ${sectionMenuOpen === section.id ? "rotate-180" : ""}`}
                            fill="none"
                            stroke="currentColor"
                            strokeWidth="1.8"
                          >
                            <path d="m6 9 6 6 6-6" />
                          </svg>
                        </button>
                        {sectionMenuOpen === section.id ? (
                          <div className="mt-2 grid gap-2 px-2 pb-2">
                            {section.links.map((link) => (
                              <NavLink
                                key={`mobile-${link.to}`}
                                to={link.to}
                                end={link.to === "/"}
                                className={({ isActive }) => `app-tools-menu-link ${isActive ? "app-tools-menu-link-active" : "app-tools-menu-link-idle"}`}
                              >
                                <span className="app-tools-menu-icon">
                                  <NavigationItemIcon icon={link.icon} />
                                </span>
                                <span className="min-w-0">
                                  <span className="block">{link.label}</span>
                                  <span className="mt-0.5 block text-xs font-normal text-steel/80">{link.description}</span>
                                </span>
                              </NavLink>
                            ))}
                          </div>
                        ) : null}
                      </>
                    )}
                  </div>
                ))}
              </div>
            </div>
          ) : null}
        </nav>
        <main className="space-y-6 px-4 md:px-6">
          <section className="app-page-header-shell">
            <div className="app-page-header">
              <div className="app-page-header-hive" aria-hidden="true">
                {Array.from({ length: 10 }, (_, index) => (
                  <span
                    key={`hive-cell-${index}`}
                    className="app-page-header-hive-cell"
                    style={{ ["--hive-index" as string]: index } as React.CSSProperties}
                  />
                ))}
              </div>
              <div className="app-toolbar-body">
                <div className="max-w-3xl">
                  <div className="app-breadcrumb">
                    <span>{routeMeta.eyebrow}</span>
                    <span>/</span>
                    <span className="font-semibold text-ink">{routeMeta.title}</span>
                  </div>
                  <h2 className="mt-1.5 text-[1.75rem] font-extrabold tracking-tight text-ink md:text-[1.95rem]">{routeMeta.title}</h2>
                  <p className="mt-1.5 max-w-2xl text-[13px] leading-5 text-steel">{routeMeta.description}</p>
                </div>
              </div>
            </div>
          </section>
          {children}
        </main>
      </div>
    </div>
  );
}

function LandingIcon({
  children,
  tone = "light"
}: {
  children: React.ReactNode;
  tone?: "dark" | "light";
}) {
  return (
    <span
      className={`inline-flex h-11 w-11 items-center justify-center rounded-2xl border ${
        tone === "dark"
          ? "border-white/10 bg-white/10 text-white/80"
          : "border-slate-200 bg-slate-50 text-brand"
      }`}
    >
      {children}
    </span>
  );
}

function SignInScreen({
  onSignin,
  error,
  autoStart = false
}: {
  onSignin: (fresh?: boolean) => Promise<void>;
  error: string | null;
  autoStart?: boolean;
}) {
  const { locale, setLocale, dictionary } = useI18n();
  const landing = dictionary.landing;
  const capabilityCards = [
    {
      title: landing.capabilityRuntimeTitle,
      body: landing.capabilityRuntimeBody,
      icon: (
        <svg viewBox="0 0 24 24" className="h-5 w-5 fill-none stroke-current" strokeWidth="1.8">
          <path d="M5 19V9m7 10V5m7 14v-7" />
        </svg>
      )
    },
    {
      title: landing.capabilityAccessTitle,
      body: landing.capabilityAccessBody,
      icon: (
        <svg viewBox="0 0 24 24" className="h-5 w-5 fill-none stroke-current" strokeWidth="1.8">
          <path d="M12 4l7 4v4c0 4.5-3 7.5-7 8-4-.5-7-3.5-7-8V8l7-4z" />
          <path d="M9.5 12.5l1.8 1.8 3.2-3.6" />
        </svg>
      )
    },
    {
      title: landing.capabilityAuditTitle,
      body: landing.capabilityAuditBody,
      icon: (
        <svg viewBox="0 0 24 24" className="h-5 w-5 fill-none stroke-current" strokeWidth="1.8">
          <path d="M7 7h10M7 12h10M7 17h6" />
          <path d="M5 4h14v16H5z" />
        </svg>
      )
    },
    {
      title: landing.capabilityOperationsTitle,
      body: landing.capabilityOperationsBody,
      icon: (
        <svg viewBox="0 0 24 24" className="h-5 w-5 fill-none stroke-current" strokeWidth="1.8">
          <path d="M6 6h12v12H6z" />
          <path d="M9 3v6M15 15v6M15 3v3M9 18v3" />
        </svg>
      )
    }
  ];

  const signInSteps = [
    { title: landing.stepOneTitle, body: landing.stepOneBody },
    { title: landing.stepTwoTitle, body: landing.stepTwoBody },
    { title: landing.stepThreeTitle, body: landing.stepThreeBody }
  ];

  useEffect(() => {
    if (!autoStart) {
      return;
    }

    void onSignin(false);
  }, [autoStart, onSignin]);

  return (
    <div className="flex min-h-screen items-center justify-center px-6 py-12">
      <div className="w-full max-w-7xl">
        <section className="grid gap-8 lg:grid-cols-[1.25fr,0.75fr]">
          <div className="app-surface overflow-hidden bg-gradient-to-br from-white via-white to-slate-50/70">
            <div className="border-b border-slate-200 px-8 py-7 lg:px-10">
              <div className="flex flex-wrap items-center justify-between gap-4">
                <div>
                  <p className="text-xs uppercase tracking-[0.28em] text-steel/55">BeeTracker</p>
                  <p className="mt-2 text-[11px] font-semibold uppercase tracking-[0.2em] text-steel/70">{landing.eyebrow}</p>
                </div>
                <div className="flex items-center gap-2">
                  <span className="text-[11px] font-semibold uppercase tracking-[0.18em] text-steel/60">{dictionary.localeLabel}</span>
                  <div className="flex rounded-full border border-slate-200 bg-slate-50 p-1">
                    {supportedLocales.map((option) => (
                      <button
                        key={option.code}
                        type="button"
                        onClick={() => setLocale(option.code)}
                        className={`rounded-full px-3 py-1 text-[11px] font-semibold uppercase tracking-[0.16em] transition ${
                          locale === option.code
                            ? "bg-white text-ink shadow-sm"
                            : "text-steel hover:text-ink"
                        }`}
                      >
                        {option.label}
                      </button>
                    ))}
                  </div>
                </div>
              </div>
              <div className="mt-8 max-w-3xl">
                <h1 className="text-4xl font-semibold leading-[1.08] tracking-tight text-ink lg:text-[3.35rem]">
                  {landing.title}
                </h1>
                <p className="mt-5 max-w-2xl text-base leading-7 text-steel">
                  {landing.subtitle}
                </p>
              </div>
            </div>
            <div className="px-8 py-8 lg:px-10">
              <div className="mb-6 max-w-2xl">
                <p className="text-[11px] font-semibold uppercase tracking-[0.18em] text-steel/60">{landing.capabilitiesTitle}</p>
                <p className="mt-3 text-sm leading-6 text-steel">{landing.capabilitiesIntro}</p>
              </div>
              <div className="grid gap-4 md:grid-cols-2">
                {capabilityCards.map((card) => (
                  <div
                    key={card.title}
                    className="rounded-3xl border border-slate-200 bg-white px-5 py-5 shadow-[0_10px_30px_rgba(15,23,42,0.04)] transition hover:border-slate-300"
                  >
                    <div className="flex items-center gap-3">
                      <LandingIcon>{card.icon}</LandingIcon>
                      <p className="text-base font-semibold text-ink">{card.title}</p>
                    </div>
                    <p className="mt-4 text-sm leading-6 text-steel">{card.body}</p>
                  </div>
                ))}
              </div>
            </div>
          </div>
          <Card title={landing.signInTitle} eyebrow={landing.signInEyebrow}>
            <p className="text-sm leading-6 text-steel">{landing.signInBody}</p>
            <div className="mt-6 rounded-2xl border border-slate-200 bg-slate-50/90 px-4 py-4">
              <p className="text-[11px] font-semibold uppercase tracking-[0.18em] text-steel/70">{landing.instructionsTitle}</p>
              <div className="mt-4 space-y-4">
                {signInSteps.map((step, index) => (
                  <div key={step.title} className="flex gap-3">
                    <div className="flex h-7 w-7 shrink-0 items-center justify-center rounded-full border border-slate-200 bg-white text-xs font-semibold text-ink">
                      {index + 1}
                    </div>
                    <div>
                      <p className="text-sm font-semibold text-ink">{step.title}</p>
                      <p className="mt-1 text-sm leading-6 text-steel">{step.body}</p>
                    </div>
                  </div>
                ))}
              </div>
            </div>
            {autoStart && !error ? (
              <p className="mt-4 rounded-2xl bg-brand/10 px-4 py-3 text-sm text-brand">
                Redirecting to sign-in automatically…
              </p>
            ) : null}
            {error ? <p className="mt-4 rounded-2xl bg-ember/10 px-4 py-3 text-sm text-ember">{error}</p> : null}
            <button
              type="button"
              onClick={() => void onSignin(false)}
              className="mt-6 w-full rounded-2xl bg-ink px-5 py-4 text-sm font-semibold text-white transition hover:bg-slate-900"
            >
              {landing.cta}
            </button>
            <p className="mt-3 text-xs leading-5 text-steel/80">{landing.securityNote}</p>
          </Card>
        </section>
      </div>
    </div>
  );
}

function AuthenticatedAdminApp({
  user,
  onUserChanged,
  onSignin,
  onSignout
}: {
  user: User;
  onUserChanged: (user: User | null) => void;
  onSignin: (fresh?: boolean) => Promise<void>;
  onSignout: () => Promise<void>;
}) {
  const { dictionary } = useI18n();
  const accessToken = user.access_token;
  const { session, error } = useAdminSession(accessToken, onSignin);

  useEffect(() => {
    if (!user.expired) {
      return;
    }

    onUserChanged(null);
    void onSignin(false);
  }, [onSignin, onUserChanged, user]);

  if (error) {
    return (
      <div className="flex min-h-screen items-center justify-center px-6">
        <Card title={dictionary.auth.sessionErrorTitle} eyebrow="OIDC">
          <p className="text-sm text-ember">{error}</p>
        </Card>
      </div>
    );
  }

  if (!session) {
    return (
      <div className="flex min-h-screen items-center justify-center px-6">
        <Card title={dictionary.auth.loadingSessionTitle} eyebrow="OIDC">
          <p className="text-sm text-ink/60">{dictionary.auth.loadingSessionBody}</p>
        </Card>
      </div>
    );
  }

  const capabilities = session.capabilities ?? [];

  return (
    <Shell user={user} session={session} onSignin={onSignin} onSignout={onSignout}>
      <Routes>
        <Route path="/" element={<DashboardPage accessToken={accessToken} onReauthenticate={onSignin} capabilities={capabilities} />} />
        <Route
          path="/profile"
          element={
            <PermissionGate permissions={session.permissions} permission={permissionKeys.profileView}>
              <ProfilePage accessToken={accessToken} onReauthenticate={onSignin} permissions={session.permissions} />
            </PermissionGate>
          }
        />
        <Route
          path="/admin-users"
          element={
            <PermissionGate permissions={session.permissions} permission={permissionKeys.usersView}>
              <AdminUsersPage accessToken={accessToken} onReauthenticate={onSignin} permissions={session.permissions} />
            </PermissionGate>
          }
        />
        <Route
          path="/admin-users/new"
          element={
            <PermissionGate permissions={session.permissions} permission={permissionKeys.usersCreate}>
              <AdminUserEditorPage accessToken={accessToken} onReauthenticate={onSignin} permissions={session.permissions} />
            </PermissionGate>
          }
        />
        <Route
          path="/admin-users/:id/edit"
          element={
            <PermissionGate permissions={session.permissions} permission={permissionKeys.usersEdit}>
              <AdminUserEditorPage accessToken={accessToken} onReauthenticate={onSignin} permissions={session.permissions} />
            </PermissionGate>
          }
        />
        <Route
          path="/roles"
          element={
            <PermissionGate permissions={session.permissions} permission={permissionKeys.rolesView}>
              <RolesPage accessToken={accessToken} onReauthenticate={onSignin} permissions={session.permissions} />
            </PermissionGate>
          }
        />
        <Route
          path="/roles/new"
          element={
            <PermissionGate permissions={session.permissions} permission={permissionKeys.rolesCreate}>
              <RoleEditorPage accessToken={accessToken} onReauthenticate={onSignin} permissions={session.permissions} />
            </PermissionGate>
          }
        />
        <Route
          path="/roles/:id/edit"
          element={
            <PermissionGate permissions={session.permissions} permission={permissionKeys.rolesEdit}>
              <RoleEditorPage accessToken={accessToken} onReauthenticate={onSignin} permissions={session.permissions} />
            </PermissionGate>
          }
        />
        <Route
          path="/permission-groups"
          element={
            <PermissionGate permissions={session.permissions} permission={permissionKeys.permissionGroupsView}>
              <PermissionGroupsPage accessToken={accessToken} onReauthenticate={onSignin} permissions={session.permissions} />
            </PermissionGate>
          }
        />
        <Route
          path="/permission-groups/new"
          element={
            <PermissionGate permissions={session.permissions} permission={permissionKeys.permissionGroupsCreate}>
              <PermissionGroupEditorPage accessToken={accessToken} onReauthenticate={onSignin} permissions={session.permissions} />
            </PermissionGate>
          }
        />
        <Route
          path="/permission-groups/:id/edit"
          element={
            <PermissionGate permissions={session.permissions} permission={permissionKeys.permissionGroupsEdit}>
              <PermissionGroupEditorPage accessToken={accessToken} onReauthenticate={onSignin} permissions={session.permissions} />
            </PermissionGate>
          }
        />
        <Route path="/torrents" element={<TorrentsPage accessToken={accessToken} onReauthenticate={onSignin} capabilities={capabilities} />} />
        <Route
          path="/torrents/bulk-policy"
          element={
            <CapabilityGate capabilities={capabilities} action="admin.bulk_upsert.torrent_policy">
              <BulkTorrentPolicyEditorPage accessToken={accessToken} onReauthenticate={onSignin} />
            </CapabilityGate>
          }
        />
        <Route
          path="/passkeys"
          element={
            <CapabilityGate capabilities={capabilities} action="admin.read.passkeys">
              <PasskeysPage accessToken={accessToken} onReauthenticate={onSignin} capabilities={capabilities} />
            </CapabilityGate>
          }
        />
        <Route
          path="/permissions"
          element={
            <CapabilityGate capabilities={capabilities} action="admin.read.permissions">
                    <TrackerAccessPage accessToken={accessToken} onReauthenticate={onSignin} capabilities={capabilities} />
            </CapabilityGate>
          }
        />
        <Route
          path="/bans"
          element={
            <CapabilityGate capabilities={capabilities} action="admin.read.bans">
              <BansPage accessToken={accessToken} onReauthenticate={onSignin} capabilities={capabilities} />
            </CapabilityGate>
          }
        />
        <Route
          path="/torrents/:infoHash"
          element={
            <CapabilityGate capabilities={capabilities} action="admin.write.torrent_policy">
              <TorrentPolicyEditorPage accessToken={accessToken} onReauthenticate={onSignin} />
            </CapabilityGate>
          }
        />
        <Route
          path="/audit"
          element={
            <PermissionGate permissions={session.permissions} permission={permissionKeys.auditView}>
              <AuditPage accessToken={accessToken} onReauthenticate={onSignin} />
            </PermissionGate>
          }
        />
        <Route
          path="/maintenance"
          element={
            <CapabilityGate capabilities={capabilities} action="admin.read.maintenance">
              <MaintenancePage accessToken={accessToken} onReauthenticate={onSignin} />
            </CapabilityGate>
          }
        />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </Shell>
  );
}

export default function App() {
  const { dictionary } = useI18n();
  const { manager, user, setUser, isBootstrapping, bootError, signin, signout } = useAdminOidc();
  const location = useLocation();
  const autoStartSignin =
    typeof window !== "undefined" &&
    window.sessionStorage.getItem(adminSignedOutStorageKey) !== "1";

  if (isBootstrapping) {
    return (
      <div className="flex min-h-screen items-center justify-center px-6">
        <Card title={dictionary.auth.bootstrappingTitle} eyebrow="OIDC">
          <p className="text-sm text-ink/60">{dictionary.auth.bootstrappingBody}</p>
        </Card>
      </div>
    );
  }

  if (bootError) {
    return (
      <div className="flex min-h-screen items-center justify-center px-6">
        <Card title={dictionary.auth.bootstrapFailedTitle} eyebrow="OIDC">
          <p className="text-sm text-ember">{bootError}</p>
        </Card>
      </div>
    );
  }

  if (!manager) {
    return (
      <div className="flex min-h-screen items-center justify-center px-6">
        <Card title={dictionary.auth.bootstrapFailedTitle} eyebrow="OIDC">
          <p className="text-sm text-ember">{dictionary.auth.oidcManagerMissing}</p>
        </Card>
      </div>
    );
  }

  if (location.pathname === "/oidc/callback") {
    return <CallbackPage manager={manager} onSignedIn={setUser} />;
  }

  if (!user) {
    return <SignInScreen onSignin={signin} error={null} autoStart={autoStartSignin} />;
  }

  return (
    <AuthenticatedAdminApp
      user={user}
      onUserChanged={setUser}
      onSignin={signin}
      onSignout={signout}
    />
  );
}
