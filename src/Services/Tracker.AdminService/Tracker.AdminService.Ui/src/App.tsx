import { type ReactNode, useEffect, useMemo, useState } from "react";
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

type UserPermissionAdminDto = {
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
  permissionItems: BulkUserPermissionOperationItemDto[];
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

type BulkUserPermissionOperationItemDto = {
  userId: string;
  succeeded: boolean;
  errorCode?: string | null;
  errorMessage?: string | null;
  snapshot?: UserPermissionAdminDto | null;
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

type PermissionFormState = {
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

const bulkTorrentPolicySelectionStorageKey = "beetracker.admin.bulkPolicySelection";

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

function toPermissionForm(snapshot: UserPermissionAdminDto): PermissionFormState {
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
      ? "border border-moss/20 bg-moss/10 text-moss"
      : tone === "warn"
        ? "border border-ember/20 bg-ember/10 text-ember"
        : "border border-slate-300 bg-slate-100 text-steel";

  return <span className={`inline-flex items-center rounded-full px-3 py-1 text-[11px] font-semibold uppercase tracking-[0.18em] ${classes}`}>{children}</span>;
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

type SortDirection = "asc" | "desc";

type DataGridColumn<T> = {
  key: string;
  header: string;
  render: (item: T) => ReactNode;
  sortValue?: (item: T) => string | number;
  searchValue?: (item: T) => string;
  className?: string;
};

function DataGrid<T>({
  items,
  columns,
  keyFn,
  emptyMessage,
  defaultPageSize = 20,
  pageSizeOptions = [10, 20, 50, 100]
}: {
  items: T[];
  columns: DataGridColumn<T>[];
  keyFn: (item: T) => string;
  emptyMessage: string;
  defaultPageSize?: number;
  pageSizeOptions?: number[];
}) {
  const { dictionary } = useI18n();
  const grid = dictionary.dataGrid;

  const [search, setSearch] = useState("");
  const [sort, setSort] = useState<{ key: string; direction: SortDirection } | null>(null);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(defaultPageSize);

  const itemCount = items.length;
  useEffect(() => { setPage(1); }, [search, itemCount]);

  const searchLower = search.toLowerCase();
  const filtered = search
    ? items.filter((item) =>
        columns.some((col) => {
          const value = col.searchValue?.(item);
          return value !== undefined && value.toLowerCase().includes(searchLower);
        })
      )
    : items;

  const sorted = sort
    ? [...filtered].sort((a, b) => {
        const col = columns.find((c) => c.key === sort.key);
        if (!col?.sortValue) return 0;
        const aVal = col.sortValue(a);
        const bVal = col.sortValue(b);
        const cmp =
          typeof aVal === "number" && typeof bVal === "number"
            ? aVal - bVal
            : String(aVal).localeCompare(String(bVal));
        return sort.direction === "asc" ? cmp : -cmp;
      })
    : filtered;

  const totalPages = Math.max(1, Math.ceil(sorted.length / pageSize));
  const safePage = Math.min(page, totalPages);
  const start = (safePage - 1) * pageSize;
  const pageItems = sorted.slice(start, start + pageSize);

  const toggleSort = (key: string) => {
    setSort((current) => {
      if (current?.key === key) {
        return current.direction === "asc" ? { key, direction: "desc" as const } : null;
      }
      return { key, direction: "asc" as const };
    });
  };

  return (
    <div>
      <div className="mb-4">
        <input
          type="text"
          placeholder={grid.searchPlaceholder}
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          className="w-full rounded-2xl border border-ink/15 bg-white px-4 py-3 text-sm text-ink placeholder:text-ink/40 focus:border-ink/30 focus:outline-none"
        />
      </div>

      <div className="overflow-hidden rounded-3xl border border-ink/10">
        <div className="overflow-x-auto">
          <table className="min-w-full divide-y divide-ink/10 text-sm">
            <thead className="bg-ink text-left text-xs uppercase tracking-[0.2em] text-white/70">
              <tr>
                {columns.map((col) => (
                  <th
                    key={col.key}
                    className={`px-4 py-3 ${col.sortValue ? "cursor-pointer select-none transition-colors hover:text-white" : ""} ${col.className ?? ""}`}
                    onClick={col.sortValue ? () => toggleSort(col.key) : undefined}
                  >
                    <span className="inline-flex items-center gap-1.5">
                      {col.header}
                      {col.sortValue && sort?.key === col.key ? (
                        <span className="text-white/90">{sort.direction === "asc" ? "\u2191" : "\u2193"}</span>
                      ) : col.sortValue ? (
                        <span className="text-white/30">{"\u2195"}</span>
                      ) : null}
                    </span>
                  </th>
                ))}
              </tr>
            </thead>
            <tbody className="divide-y divide-ink/10 bg-white">
              {pageItems.map((item) => (
                <tr key={keyFn(item)} className="transition-colors hover:bg-slate-50/80">
                  {columns.map((col) => (
                    <td key={col.key} className={`px-4 py-3 ${col.className ?? ""}`}>
                      {col.render(item)}
                    </td>
                  ))}
                </tr>
              ))}
              {pageItems.length === 0 ? (
                <tr>
                  <td className="px-4 py-8 text-center text-ink/50" colSpan={columns.length}>
                    {search ? grid.noResults : emptyMessage}
                  </td>
                </tr>
              ) : null}
            </tbody>
          </table>
        </div>
      </div>

      {sorted.length > 0 ? (
        <div className="mt-4 flex flex-wrap items-center justify-between gap-4 text-sm">
          <div className="flex items-center gap-2 text-ink/55">
            <span>{grid.rowsPerPage}:</span>
            <select
              value={pageSize}
              onChange={(e) => { setPageSize(Number(e.target.value)); setPage(1); }}
              className="rounded-xl border border-ink/15 bg-white px-2 py-1.5 text-sm text-ink"
            >
              {pageSizeOptions.map((size) => (
                <option key={size} value={size}>{size}</option>
              ))}
            </select>
          </div>
          <div className="flex items-center gap-3">
            <span className="text-ink/55">
              {formatText(grid.showingEntries, { from: start + 1, to: Math.min(start + pageSize, sorted.length), total: sorted.length })}
            </span>
            <div className="flex gap-1">
              <button type="button" disabled={safePage <= 1} onClick={() => setPage(1)} className="rounded-xl border border-ink/15 px-2.5 py-1.5 text-ink/70 transition-colors hover:bg-slate-50 disabled:opacity-40">{"\u00AB"}</button>
              <button type="button" disabled={safePage <= 1} onClick={() => setPage((p) => Math.max(1, p - 1))} className="rounded-xl border border-ink/15 px-2.5 py-1.5 text-ink/70 transition-colors hover:bg-slate-50 disabled:opacity-40">{"\u2039"}</button>
              <span className="flex items-center px-3 text-ink/60">{safePage} / {totalPages}</span>
              <button type="button" disabled={safePage >= totalPages} onClick={() => setPage((p) => Math.min(totalPages, p + 1))} className="rounded-xl border border-ink/15 px-2.5 py-1.5 text-ink/70 transition-colors hover:bg-slate-50 disabled:opacity-40">{"\u203A"}</button>
              <button type="button" disabled={safePage >= totalPages} onClick={() => setPage(totalPages)} className="rounded-xl border border-ink/15 px-2.5 py-1.5 text-ink/70 transition-colors hover:bg-slate-50 disabled:opacity-40">{"\u00BB"}</button>
            </div>
          </div>
        </div>
      ) : null}
    </div>
  );
}

function getRouteMeta(pathname: string, dictionary: I18nDictionary): { eyebrow: string; title: string; description: string } {
  const routes = dictionary.routes;
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
  const privilegedCapabilities = capabilities.filter((capability) => capability.granted && capability.confirmationSeverity === "high").length;
  const grantedCapabilities = capabilities.filter((capability) => capability.granted);
  const readinessPercent = overview.activeNodeCount > 0
    ? Math.round((readyNodes / overview.activeNodeCount) * 100)
    : 0;
  const capabilityCategories = Array.from(new Set(grantedCapabilities.map((capability) => capability.category)));

  return (
    <div className="space-y-6">
      <section className="grid gap-4 xl:grid-cols-2">
        <div className="app-card px-6 py-5">
          <p className="text-[11px] font-semibold uppercase tracking-[0.18em] text-steel/70">{dashboard.readinessTitle}</p>
          <div className="mt-4 flex items-end justify-between">
            <p className="text-4xl font-semibold text-ink">{readinessPercent}%</p>
            <span className="text-sm text-steel">{readyNodes}/{overview.activeNodeCount}</span>
          </div>
          <p className="mt-3 text-sm leading-6 text-steel">
            {degradedNodes > 0
              ? `${degradedNodes} ${dashboard.readinessNeedsAttention}`
              : dashboard.readinessReady}
          </p>
          <div className="mt-4 h-1.5 rounded-full bg-slate-100">
            <div className="h-1.5 rounded-full bg-moss" style={{ width: `${readinessPercent}%` }} />
          </div>
        </div>
        <div className="app-card px-6 py-5">
          <p className="text-[11px] font-semibold uppercase tracking-[0.18em] text-steel/70">{dashboard.postureTitle}</p>
          <div className="mt-4 flex items-end justify-between">
            <p className="text-4xl font-semibold text-ink">{grantedCapabilities.length}</p>
            <span className="text-sm text-steel">{capabilityCategories.length} {dashboard.postureDomains}</span>
          </div>
          <p className="mt-3 text-sm leading-6 text-steel">
            {privilegedCapabilities} {dashboard.postureProtected}
          </p>
          <div className="mt-4 h-1.5 rounded-full bg-slate-100">
            <div className="h-1.5 rounded-full bg-amber-500" style={{ width: `${capabilities.length ? (privilegedCapabilities / capabilities.length) * 100 : 0}%` }} />
          </div>
        </div>
      </section>
      <div className="grid gap-6 xl:grid-cols-[1.15fr,0.85fr]">
        <Card title={dashboard.readinessMapTitle} eyebrow={dashboard.operationsEyebrow}>
          <div className="grid gap-4 md:grid-cols-2">
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
        <div className="space-y-6">
          <Card title={dashboard.whyTitle} eyebrow={dashboard.productEyebrow}>
            <div className="space-y-4 text-sm leading-6 text-steel">
              <p>{dashboard.whyBody}</p>
              <div className="rounded-2xl border border-slate-200 bg-slate-50 px-4 py-4">
                <p className="text-[11px] font-semibold uppercase tracking-[0.18em] text-steel/70">{dashboard.observedSnapshot}</p>
                <p className="mt-2 text-sm text-ink">{new Date(overview.observedAtUtc).toLocaleString()}</p>
                <p className="mt-2 text-sm text-steel">{dashboard.snapshotBody}</p>
              </div>
              <ul className="space-y-2 text-ink">
                <li>{dashboard.bulletRuntime}</li>
                <li>{dashboard.bulletOperator}</li>
                <li>{dashboard.bulletConfig}</li>
              </ul>
            </div>
          </Card>
          <Card title={dashboard.capabilitiesTitle} eyebrow={dashboard.capabilitiesEyebrow}>
            <div className="space-y-3">
              {grantedCapabilities.map((capability) => (
                <div key={capability.action} className="rounded-2xl border border-slate-200 bg-white px-4 py-4">
                  <div className="flex items-center justify-between gap-4">
                    <div>
                      <p className="font-medium text-ink">{capability.displayName}</p>
                      <p className="text-sm text-steel">{toTitleCase(capability.category)}</p>
                    </div>
                    <StatusPill tone={capability.confirmationSeverity === "high" ? "warn" : "neutral"}>
                      {capability.confirmationSeverity}
                    </StatusPill>
                  </div>
                </div>
              ))}
            </div>
          </Card>
        </div>
      </div>
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
  const [items, setItems] = useState<TorrentAdminDto[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [selectedInfoHashes, setSelectedInfoHashes] = useState<string[]>([]);
  const [status, setStatus] = useState<string | null>(null);
  const [result, setResult] = useState<BulkOperationResultDto | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const canEditPolicy = hasGrantedCapability(capabilities, "admin.write.torrent_policy");
  const canActivate = hasGrantedCapability(capabilities, "admin.activate.torrent");
  const canDeactivate = hasGrantedCapability(capabilities, "admin.deactivate.torrent");
  const canBulkEditPolicy = hasGrantedCapability(capabilities, "admin.bulk_upsert.torrent_policy");

  useEffect(() => {
    const banner = location.state as NavigationBannerState | null;
    if (!banner?.message) {
      return;
    }

    setStatus(banner.message);
    navigate(location.pathname, { replace: true, state: null });
  }, [location.pathname, location.state, navigate]);

  useEffect(() => {
    let isMounted = true;

    apiRequest<TorrentAdminDto[]>("/api/admin/torrents?page=1&pageSize=50", accessToken, onReauthenticate)
      .then((value) => {
        if (isMounted) {
          setItems(value);
          setSelectedInfoHashes((current) => current.filter((infoHash) => value.some((item) => item.infoHash === infoHash)));
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
  }, [accessToken, onReauthenticate]);

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
      const refreshed = await apiRequest<TorrentAdminDto[]>("/api/admin/torrents?page=1&pageSize=50", accessToken, onReauthenticate);
      setItems(refreshed);
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
    <Card title={labels.cardTitle} eyebrow={labels.eyebrow}>
      {error ? <p className="text-sm text-ember">{error}</p> : null}
      <div className="mb-4 flex flex-wrap gap-3">
        <button
          type="button"
          disabled={!canActivate || isSubmitting || selectedInfoHashes.length === 0}
          onClick={() => void runLifecycle("activate")}
          className="rounded-2xl border border-ink/20 px-4 py-3 text-sm font-semibold text-ink transition hover:bg-ink/5 disabled:cursor-not-allowed disabled:opacity-60"
        >
          {labels.activateSelection}
        </button>
        <button
          type="button"
          disabled={!canDeactivate || isSubmitting || selectedInfoHashes.length === 0}
          onClick={() => void runLifecycle("deactivate")}
          className="rounded-2xl border border-ember/30 px-4 py-3 text-sm font-semibold text-ember transition hover:bg-ember/5 disabled:cursor-not-allowed disabled:opacity-60"
        >
          {labels.deactivateSelection}
        </button>
        <button
          type="button"
          disabled={!canBulkEditPolicy || isSubmitting || selectedInfoHashes.length === 0}
          onClick={openBulkPolicyEditor}
          className="rounded-2xl border border-moss/30 px-4 py-3 text-sm font-semibold text-moss transition hover:bg-moss/5 disabled:cursor-not-allowed disabled:opacity-60"
        >
          {labels.openBulkPolicy}
        </button>
        <p className="self-center text-sm text-ink/55">{selectedInfoHashes.length} {labels.selectedCount}</p>
      </div>
      {status ? <p className="mb-4 text-sm text-moss">{status}</p> : null}
      {result && result.torrentItems.length > 0 ? (
        <div className="mb-4 space-y-2">
          {result.torrentItems.map((item) => (
            <div key={item.infoHash} className="rounded-2xl border border-ink/10 bg-slate-50 px-4 py-3">
              <div className="flex items-center justify-between gap-3">
                <p className="font-mono text-xs text-ink">{item.infoHash}</p>
                <StatusPill tone={item.succeeded ? "good" : "warn"}>{item.succeeded ? labels.applied : labels.failed}</StatusPill>
              </div>
              {item.errorMessage ? <p className="mt-2 text-sm text-ember">{item.errorMessage}</p> : null}
            </div>
          ))}
        </div>
      ) : null}
      <DataGrid<TorrentAdminDto>
        items={items}
        keyFn={(item) => item.infoHash}
        emptyMessage={labels.empty}
        columns={[
          {
            key: "select",
            header: labels.tableSelect,
            render: (item) => (
              <input
                type="checkbox"
                checked={selectedInfoHashes.includes(item.infoHash)}
                onChange={() => toggleSelection(item.infoHash)}
              />
            )
          },
          {
            key: "infoHash",
            header: labels.tableInfoHash,
            render: (item) => <span className="font-mono text-xs">{item.infoHash}</span>,
            sortValue: (item) => item.infoHash,
            searchValue: (item) => item.infoHash,
            className: "text-ink"
          },
          {
            key: "mode",
            header: labels.tableMode,
            render: (item) => formatMode(item.isPrivate, dictionary),
            sortValue: (item) => (item.isPrivate ? 1 : 0),
            searchValue: (item) => formatMode(item.isPrivate, dictionary)
          },
          {
            key: "state",
            header: labels.tableState,
            render: (item) => formatState(item.isEnabled, dictionary),
            sortValue: (item) => (item.isEnabled ? 1 : 0),
            searchValue: (item) => formatState(item.isEnabled, dictionary)
          },
          {
            key: "interval",
            header: labels.tableInterval,
            render: (item) => `${item.announceIntervalSeconds}s`,
            sortValue: (item) => item.announceIntervalSeconds
          },
          {
            key: "numwant",
            header: labels.tableNumwant,
            render: (item) => `${item.defaultNumWant} / ${item.maxNumWant}`,
            sortValue: (item) => item.defaultNumWant
          },
          {
            key: "action",
            header: labels.tableAction,
            render: (item) =>
              canEditPolicy ? (
                <Link className="font-semibold text-ink underline" to={`/torrents/${item.infoHash}`}>
                  {labels.openPolicy}
                </Link>
              ) : (
                <span className="text-ink/40">{dictionary.common.readOnly}</span>
              )
          }
        ]}
      />
    </Card>
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
  const [items, setItems] = useState<PasskeyAdminDto[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [passkeyInput, setPasskeyInput] = useState("");
  const [rotateExpiryInput, setRotateExpiryInput] = useState("");
  const [result, setResult] = useState<BulkOperationResultDto | null>(null);
  const [status, setStatus] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const canRevoke = hasGrantedCapability(capabilities, "admin.revoke.passkey");
  const canRotate = hasGrantedCapability(capabilities, "admin.rotate.passkey");

  const reload = async () => {
    const value = await apiRequest<PasskeyAdminDto[]>("/api/admin/passkeys?page=1&pageSize=50", accessToken, onReauthenticate);
    setItems(value);
  };

  useEffect(() => {
    let isMounted = true;

    reload()
      .catch((requestError) => {
        if (isMounted) {
          setError(requestError instanceof Error ? requestError.message : labels.loadError);
        }
      });

    return () => {
      isMounted = false;
    };
  }, [accessToken, onReauthenticate]);

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
      await reload();
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : labels.operationError);
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div className="grid gap-6 xl:grid-cols-[1.05fr,0.95fr]">
      <Card title={labels.cardTitle} eyebrow={labels.eyebrow}>
        {error ? <p className="mb-4 text-sm text-ember">{error}</p> : null}
        <DataGrid<PasskeyAdminDto>
          items={items}
          keyFn={(item) => `${item.passkeyMask}-${item.userId}`}
          emptyMessage={labels.empty}
          columns={[
            {
              key: "mask",
              header: labels.tableMask,
              render: (item) => <span className="font-mono text-xs">{item.passkeyMask}</span>,
              sortValue: (item) => item.passkeyMask,
              searchValue: (item) => item.passkeyMask,
              className: "text-ink"
            },
            {
              key: "user",
              header: labels.tableUser,
              render: (item) => <span className="font-mono text-xs">{item.userId}</span>,
              sortValue: (item) => item.userId,
              searchValue: (item) => item.userId,
              className: "text-ink"
            },
            {
              key: "state",
              header: labels.tableState,
              render: (item) => (
                <StatusPill tone={item.isRevoked ? "warn" : "good"}>
                  {item.isRevoked ? dictionary.common.revoked : dictionary.common.active}
                </StatusPill>
              ),
              sortValue: (item) => (item.isRevoked ? 1 : 0),
              searchValue: (item) => item.isRevoked ? dictionary.common.revoked : dictionary.common.active
            },
            {
              key: "expires",
              header: labels.tableExpires,
              render: (item) => item.expiresAtUtc ? new Date(item.expiresAtUtc).toLocaleString() : dictionary.common.never,
              sortValue: (item) => item.expiresAtUtc ? new Date(item.expiresAtUtc).getTime() : Number.MAX_SAFE_INTEGER
            },
            {
              key: "version",
              header: labels.tableVersion,
              render: (item) => item.version,
              sortValue: (item) => item.version
            }
          ]}
        />
      </Card>
      <Card title={labels.actionTitle} eyebrow={labels.actionEyebrow}>
        <label className="space-y-2">
          <span className="text-sm font-medium text-ink">{labels.rawPasskeys}</span>
          <textarea
            className="min-h-48 w-full rounded-2xl border border-ink/15 px-4 py-3 font-mono text-sm"
            value={passkeyInput}
            onChange={(event) => setPasskeyInput(event.target.value)}
            placeholder={labels.placeholder}
          />
        </label>
        <label className="mt-4 block space-y-2">
          <span className="text-sm font-medium text-ink">{labels.expiryOverride}</span>
          <input
            className="w-full rounded-2xl border border-ink/15 px-4 py-3"
            type="datetime-local"
            value={rotateExpiryInput}
            onChange={(event) => setRotateExpiryInput(event.target.value)}
          />
        </label>
        <div className="mt-6 flex flex-wrap gap-3">
          <button
            type="button"
            disabled={!canRevoke || isSubmitting || inputPasskeys.length === 0}
            onClick={() => void runBulkPasskeyAction("/api/admin/passkeys/bulk/revoke", "revoke")}
            className="rounded-2xl border border-ink/20 px-5 py-3 text-sm font-semibold text-ink transition hover:bg-ink/5 disabled:cursor-not-allowed disabled:opacity-60"
          >
            {labels.revoke}
          </button>
          <button
            type="button"
            disabled={!canRotate || isSubmitting || inputPasskeys.length === 0}
            onClick={() => void runBulkPasskeyAction("/api/admin/passkeys/bulk/rotate", "rotate")}
            className="rounded-2xl bg-ink px-5 py-3 text-sm font-semibold text-white transition hover:bg-slate-900 disabled:cursor-not-allowed disabled:opacity-60"
          >
            {labels.rotate}
          </button>
        </div>
        {status ? <p className="mt-4 text-sm text-moss">{status}</p> : null}
        {result ? (
          <div className="mt-5 space-y-3">
            {result.passkeyItems.map((item) => (
              <div key={`${item.passkeyMask}-${item.newPasskeyMask ?? "same"}`} className="rounded-2xl border border-ink/10 bg-slate-50 px-4 py-3">
                <div className="flex items-center justify-between gap-3">
                  <p className="font-medium text-ink">{item.passkeyMask}</p>
                  <StatusPill tone={item.succeeded ? "good" : "warn"}>{item.succeeded ? labels.completed : labels.failed}</StatusPill>
                </div>
                {item.errorMessage ? <p className="mt-2 text-sm text-ember">{item.errorMessage}</p> : null}
                {item.newPasskey ? (
                  <div className="mt-2 rounded-2xl bg-moss/10 px-3 py-2">
                    <p className="text-xs uppercase tracking-[0.18em] text-moss">{labels.newPasskey}</p>
                    <p className="mt-1 break-all font-mono text-xs text-moss">{item.newPasskey}</p>
                  </div>
                ) : null}
              </div>
            ))}
          </div>
        ) : null}
      </Card>
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
  const [items, setItems] = useState<BanRuleAdminDto[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [status, setStatus] = useState<string | null>(null);
  const [form, setForm] = useState<BanFormState>({
    scope: "user",
    subject: "",
    reason: "",
    expiresAtLocal: "",
    expectedVersion: undefined
  });
  const [isSubmitting, setIsSubmitting] = useState(false);

  const canWrite = hasGrantedCapability(capabilities, "admin.write.ban");
  const canExpire = hasGrantedCapability(capabilities, "admin.expire.ban");
  const canDelete = hasGrantedCapability(capabilities, "admin.delete.ban");

  const reload = async () => {
    const value = await apiRequest<BanRuleAdminDto[]>("/api/admin/bans?page=1&pageSize=50", accessToken, onReauthenticate);
    setItems(value);
  };

  useEffect(() => {
    let isMounted = true;

    reload().catch((requestError) => {
      if (isMounted) {
          setError(requestError instanceof Error ? requestError.message : labels.loadError);
      }
    });

    return () => {
      isMounted = false;
    };
  }, [accessToken, onReauthenticate]);

  const fillForm = (item: BanRuleAdminDto) => {
    setForm({
      scope: item.scope,
      subject: item.subject,
      reason: item.reason,
      expiresAtLocal: toLocalDateTimeInput(item.expiresAtUtc),
      expectedVersion: item.version
    });
    setStatus(formatText(labels.loadedToEditor, { scope: item.scope, subject: item.subject }));
    setError(null);
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
      setForm((current) => ({ ...current, subject: "", reason: "", expiresAtLocal: "", expectedVersion: undefined }));
      await reload();
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : labels.deleteError);
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div className="grid gap-6 xl:grid-cols-[1.05fr,0.95fr]">
      <Card title={labels.cardTitle} eyebrow={labels.eyebrow}>
        {error ? <p className="mb-4 text-sm text-ember">{error}</p> : null}
        <DataGrid<BanRuleAdminDto>
          items={items}
          keyFn={(item) => `${item.scope}:${item.subject}`}
          emptyMessage={labels.empty}
          columns={[
            {
              key: "scope",
              header: labels.scope,
              render: (item) => <span className="font-semibold">{item.scope}</span>,
              sortValue: (item) => item.scope,
              searchValue: (item) => item.scope
            },
            {
              key: "subject",
              header: labels.subject,
              render: (item) => <span className="font-mono text-xs">{item.subject}</span>,
              sortValue: (item) => item.subject,
              searchValue: (item) => item.subject,
              className: "text-ink"
            },
            {
              key: "reason",
              header: labels.reason,
              render: (item) => <span className="text-ink/60">{item.reason}</span>,
              searchValue: (item) => item.reason
            },
            {
              key: "expires",
              header: labels.expiresLabel,
              render: (item) => item.expiresAtUtc ? new Date(item.expiresAtUtc).toLocaleString() : dictionary.common.never,
              sortValue: (item) => item.expiresAtUtc ? new Date(item.expiresAtUtc).getTime() : Number.MAX_SAFE_INTEGER
            },
            {
              key: "version",
              header: dictionary.common.version,
              render: (item) => item.version,
              sortValue: (item) => item.version
            },
            {
              key: "action",
              header: "",
              render: (item) => (
                <button
                  type="button"
                  onClick={() => fillForm(item)}
                  className="rounded-2xl border border-ink/20 px-3 py-1.5 text-xs font-semibold text-ink transition hover:bg-ink/5"
                >
                  {labels.openRule}
                </button>
              )
            }
          ]}
        />
      </Card>
      <Card title={labels.editorTitle} eyebrow={labels.editorEyebrow}>
        <div className="grid gap-4 md:grid-cols-2">
          <label className="space-y-2">
            <span className="text-sm font-medium text-ink">{labels.scope}</span>
            <input className="w-full rounded-2xl border border-ink/15 px-4 py-3" value={form.scope} onChange={(event) => setForm((current) => ({ ...current, scope: event.target.value }))} />
          </label>
          <label className="space-y-2">
            <span className="text-sm font-medium text-ink">{labels.subject}</span>
            <input className="w-full rounded-2xl border border-ink/15 px-4 py-3" value={form.subject} onChange={(event) => setForm((current) => ({ ...current, subject: event.target.value }))} />
          </label>
        </div>
        <label className="mt-4 block space-y-2">
          <span className="text-sm font-medium text-ink">{labels.reason}</span>
          <textarea className="min-h-32 w-full rounded-2xl border border-ink/15 px-4 py-3" value={form.reason} onChange={(event) => setForm((current) => ({ ...current, reason: event.target.value }))} />
        </label>
        <label className="mt-4 block space-y-2">
          <span className="text-sm font-medium text-ink">{labels.expiresAt}</span>
          <input className="w-full rounded-2xl border border-ink/15 px-4 py-3" type="datetime-local" value={form.expiresAtLocal} onChange={(event) => setForm((current) => ({ ...current, expiresAtLocal: event.target.value }))} />
        </label>
        <div className="mt-6 flex flex-wrap gap-3">
          <button
            type="button"
            disabled={!canWrite || isSubmitting || !form.scope.trim() || !form.subject.trim() || !form.reason.trim()}
            onClick={() => void saveBan()}
            className="rounded-2xl bg-ink px-5 py-3 text-sm font-semibold text-white transition hover:bg-slate-900 disabled:cursor-not-allowed disabled:opacity-60"
          >
            {labels.saveBan}
          </button>
          <button
            type="button"
            disabled={!canExpire || isSubmitting || !form.scope.trim() || !form.subject.trim()}
            onClick={() => void expireBan()}
            className="rounded-2xl border border-ink/20 px-5 py-3 text-sm font-semibold text-ink transition hover:bg-ink/5 disabled:cursor-not-allowed disabled:opacity-60"
          >
            {labels.expireBan}
          </button>
          <button
            type="button"
            disabled={!canDelete || isSubmitting || !form.scope.trim() || !form.subject.trim()}
            onClick={() => void deleteBan()}
            className="rounded-2xl border border-ember/30 px-5 py-3 text-sm font-semibold text-ember transition hover:bg-ember/5 disabled:cursor-not-allowed disabled:opacity-60"
          >
            {labels.deleteBan}
          </button>
        </div>
        {status ? <p className="mt-4 text-sm text-moss">{status}</p> : null}
        {error ? <p className="mt-2 text-sm text-ember">{error}</p> : null}
      </Card>
    </div>
  );
}

function PermissionsPage({
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
  const [items, setItems] = useState<UserPermissionAdminDto[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [status, setStatus] = useState<string | null>(null);
  const [result, setResult] = useState<BulkOperationResultDto | null>(null);
  const [selectedUserIds, setSelectedUserIds] = useState<string[]>([]);
  const [form, setForm] = useState<PermissionFormState>({
    userId: "",
    canLeech: true,
    canSeed: true,
    canScrape: true,
    canUsePrivateTracker: true,
    expectedVersion: undefined
  });
  const [isSubmitting, setIsSubmitting] = useState(false);
  const canWrite = hasGrantedCapability(capabilities, "admin.write.permissions");

  const reload = async () => {
    const value = await apiRequest<UserPermissionAdminDto[]>("/api/admin/permissions?page=1&pageSize=50", accessToken, onReauthenticate);
    setItems(value);
    setSelectedUserIds((current) => current.filter((userId) => value.some((item) => item.userId === userId)));
  };

  useEffect(() => {
    let isMounted = true;

    reload().catch((requestError) => {
      if (isMounted) {
        setError(requestError instanceof Error ? requestError.message : labels.loadError);
      }
    });

    return () => {
      isMounted = false;
    };
  }, [accessToken, onReauthenticate]);

  const loadUser = (item: UserPermissionAdminDto) => {
    setForm(toPermissionForm(item));
    setStatus(formatText(labels.loadedStatus, { userId: item.userId }));
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
      await apiMutation<UserPermissionAdminDto, Omit<PermissionFormState, "userId">>(
        `/api/admin/users/${encodeURIComponent(form.userId)}/permissions`,
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
        "/api/admin/users/bulk/permissions",
        "PUT",
        accessToken,
        { items: selectedItems },
        onReauthenticate
      );

      setResult(operationResult);
      setStatus(formatText(labels.bulkStatus, { succeeded: operationResult.succeededCount, total: operationResult.totalCount }));
      await reload();
      setSelectedUserIds([]);
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : labels.bulkError);
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div className="grid gap-6 xl:grid-cols-[1.05fr,0.95fr]">
      <Card title={labels.cardTitle} eyebrow={labels.eyebrow}>
        {error ? <p className="mb-4 text-sm text-ember">{error}</p> : null}
        {status ? <p className="mb-4 text-sm text-moss">{status}</p> : null}
        {result && result.permissionItems.length > 0 ? (
          <div className="mb-4 space-y-2">
            {result.permissionItems.map((item) => (
              <div key={item.userId} className="rounded-2xl border border-ink/10 bg-slate-50 px-4 py-3">
                <div className="flex items-center justify-between gap-3">
                  <p className="font-mono text-xs text-ink">{item.userId}</p>
                  <StatusPill tone={item.succeeded ? "good" : "warn"}>{item.succeeded ? labels.succeeded : labels.failed}</StatusPill>
                </div>
                {item.errorMessage ? <p className="mt-2 text-sm text-ember">{item.errorMessage}</p> : null}
              </div>
            ))}
          </div>
        ) : null}
        <DataGrid<UserPermissionAdminDto>
          items={items}
          keyFn={(item) => item.userId}
          emptyMessage={labels.empty}
          columns={[
            {
              key: "select",
              header: "",
              render: (item) => (
                <input
                  type="checkbox"
                  checked={selectedUserIds.includes(item.userId)}
                  onChange={() => toggleSelection(item.userId)}
                />
              )
            },
            {
              key: "userId",
              header: labels.userId,
              render: (item) => <span className="font-mono text-xs font-semibold">{item.userId}</span>,
              sortValue: (item) => item.userId,
              searchValue: (item) => item.userId,
              className: "text-ink"
            },
            {
              key: "leech",
              header: labels.leech,
              render: (item) => (
                <StatusPill tone={item.canLeech ? "good" : "neutral"}>
                  {formatBool(item.canLeech, dictionary)}
                </StatusPill>
              ),
              sortValue: (item) => (item.canLeech ? 1 : 0)
            },
            {
              key: "seed",
              header: labels.seed,
              render: (item) => (
                <StatusPill tone={item.canSeed ? "good" : "neutral"}>
                  {formatBool(item.canSeed, dictionary)}
                </StatusPill>
              ),
              sortValue: (item) => (item.canSeed ? 1 : 0)
            },
            {
              key: "scrape",
              header: labels.scrape,
              render: (item) => (
                <StatusPill tone={item.canScrape ? "good" : "neutral"}>
                  {formatBool(item.canScrape, dictionary)}
                </StatusPill>
              ),
              sortValue: (item) => (item.canScrape ? 1 : 0)
            },
            {
              key: "private",
              header: labels.privateTracker,
              render: (item) => (
                <StatusPill tone={item.canUsePrivateTracker ? "good" : "neutral"}>
                  {formatBool(item.canUsePrivateTracker, dictionary)}
                </StatusPill>
              ),
              sortValue: (item) => (item.canUsePrivateTracker ? 1 : 0)
            },
            {
              key: "version",
              header: dictionary.common.version,
              render: (item) => item.version,
              sortValue: (item) => item.version
            },
            {
              key: "action",
              header: "",
              render: (item) => (
                <button
                  type="button"
                  onClick={() => loadUser(item)}
                  className="rounded-2xl border border-ink/20 px-3 py-1.5 text-xs font-semibold text-ink transition hover:bg-ink/5"
                >
                  {labels.edit}
                </button>
              )
            }
          ]}
        />
      </Card>
      <Card title={labels.editorTitle} eyebrow={labels.editorEyebrow}>
        <label className="space-y-2">
          <span className="text-sm font-medium text-ink">{labels.userId}</span>
          <input className="w-full rounded-2xl border border-ink/15 px-4 py-3 font-mono text-sm" value={form.userId} onChange={(event) => setForm((current) => ({ ...current, userId: event.target.value }))} />
        </label>
        <div className="mt-5 grid gap-3 md:grid-cols-2">
          <label className="flex items-center gap-3 rounded-2xl border border-ink/10 bg-slate-50 px-4 py-3 text-sm text-ink">
            <input type="checkbox" checked={form.canLeech} onChange={(event) => setForm((current) => ({ ...current, canLeech: event.target.checked }))} />
            {labels.canLeech}
          </label>
          <label className="flex items-center gap-3 rounded-2xl border border-ink/10 bg-slate-50 px-4 py-3 text-sm text-ink">
            <input type="checkbox" checked={form.canSeed} onChange={(event) => setForm((current) => ({ ...current, canSeed: event.target.checked }))} />
            {labels.canSeed}
          </label>
          <label className="flex items-center gap-3 rounded-2xl border border-ink/10 bg-slate-50 px-4 py-3 text-sm text-ink">
            <input type="checkbox" checked={form.canScrape} onChange={(event) => setForm((current) => ({ ...current, canScrape: event.target.checked }))} />
            {labels.canScrape}
          </label>
          <label className="flex items-center gap-3 rounded-2xl border border-ink/10 bg-slate-50 px-4 py-3 text-sm text-ink">
            <input type="checkbox" checked={form.canUsePrivateTracker} onChange={(event) => setForm((current) => ({ ...current, canUsePrivateTracker: event.target.checked }))} />
            {labels.canUsePrivateTracker}
          </label>
        </div>
        <div className="mt-6 flex flex-wrap gap-3">
          <button
            type="button"
            disabled={!canWrite || isSubmitting || !form.userId.trim()}
            onClick={() => void savePermissions()}
            className="rounded-2xl bg-ink px-5 py-3 text-sm font-semibold text-white transition hover:bg-slate-900 disabled:cursor-not-allowed disabled:opacity-60"
          >
            {labels.savePermissions}
          </button>
          <button
            type="button"
            disabled={!canWrite || isSubmitting || selectedUserIds.length === 0}
            onClick={() => void bulkApplyPermissions()}
            className="rounded-2xl border border-ink/20 px-5 py-3 text-sm font-semibold text-ink transition hover:bg-ink/5 disabled:cursor-not-allowed disabled:opacity-60"
          >
            {labels.applySelected} ({selectedUserIds.length})
          </button>
        </div>
        {error ? <p className="mt-2 text-sm text-ember">{error}</p> : null}
      </Card>
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
  const [items, setItems] = useState<AuditRecordDto[]>([]);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let isMounted = true;

    apiRequest<AuditRecordDto[]>("/api/admin/audit?page=1&pageSize=25", accessToken, onReauthenticate)
      .then((value) => {
        if (isMounted) {
          setItems(value);
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
  }, [accessToken, onReauthenticate]);

  return (
    <Card title={labels.title} eyebrow={labels.eyebrow}>
      {error ? <p className="text-sm text-ember">{error}</p> : null}
      <DataGrid<AuditRecordDto>
        items={items}
        keyFn={(item) => item.id}
        emptyMessage={labels.empty}
        defaultPageSize={20}
        columns={[
          {
            key: "action",
            header: labels.tableAction,
            render: (item) => <span className="font-semibold">{item.action}</span>,
            sortValue: (item) => item.action,
            searchValue: (item) => item.action
          },
          {
            key: "actor",
            header: labels.tableActor,
            render: (item) => <span className="font-mono text-xs">{item.actorId}</span>,
            sortValue: (item) => item.actorId,
            searchValue: (item) => item.actorId,
            className: "text-ink"
          },
          {
            key: "role",
            header: labels.tableRole,
            render: (item) => item.actorRole,
            sortValue: (item) => item.actorRole,
            searchValue: (item) => item.actorRole
          },
          {
            key: "severity",
            header: labels.tableSeverity,
            render: (item) => (
              <StatusPill tone={item.severity === "high" ? "warn" : "neutral"}>
                {item.severity}
              </StatusPill>
            ),
            sortValue: (item) => item.severity,
            searchValue: (item) => item.severity
          },
          {
            key: "entity",
            header: labels.entity,
            render: (item) => <span className="text-ink/70">{item.entityType} / {item.entityId}</span>,
            sortValue: (item) => item.entityType,
            searchValue: (item) => `${item.entityType} ${item.entityId}`
          },
          {
            key: "result",
            header: labels.result,
            render: (item) => item.result,
            sortValue: (item) => item.result,
            searchValue: (item) => item.result
          },
          {
            key: "occurred",
            header: labels.occurred,
            render: (item) => new Date(item.occurredAtUtc).toLocaleString(),
            sortValue: (item) => new Date(item.occurredAtUtc).getTime()
          }
        ]}
      />
    </Card>
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
  const links = useMemo(
    () => [
      { to: "/", label: dictionary.routes.overviewTitle },
      { to: "/torrents", label: dictionary.routes.torrentsTitle },
      { to: "/passkeys", label: dictionary.routes.passkeysTitle },
      { to: "/permissions", label: dictionary.routes.permissionsTitle },
      { to: "/bans", label: dictionary.routes.bansTitle },
      { to: "/audit", label: dictionary.routes.auditTitle }
    ],
    [dictionary]
  );
  const location = useLocation();
  const routeMeta = getRouteMeta(location.pathname, dictionary);

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

  return (
    <div className="min-h-screen px-4 py-4 md:px-6 md:py-6">
      <div className="mx-auto grid max-w-[1600px] gap-6 lg:grid-cols-[280px,1fr]">
        <aside className="app-surface overflow-hidden">
          <div className="border-b border-slate-200 px-6 py-6">
          <div>
            <p className="text-xs uppercase tracking-[0.24em] text-steel/60">BeeTracker</p>
            <h1 className="mt-2 text-2xl font-semibold tracking-tight text-ink">{dictionary.landing.eyebrow}</h1>
            <p className="mt-3 text-sm leading-6 text-steel">
              {dictionary.landing.capabilitiesIntro}
            </p>
          </div>
          </div>
          <div className="px-6 py-5">
            <p className="text-[11px] font-semibold uppercase tracking-[0.18em] text-steel/60">{dictionary.common.navigation}</p>
            <nav className="mt-4 space-y-1.5">
            {links.map((link) => (
              <NavLink
                key={link.to}
                to={link.to}
                end={link.to === "/"}
                className={({ isActive }) =>
                  `flex items-center justify-between rounded-2xl border px-4 py-3 text-sm font-medium transition ${
                    isActive
                      ? "border-slate-200 bg-slate-50 text-ink shadow-sm"
                      : "border-transparent text-steel hover:border-slate-200 hover:bg-slate-50 hover:text-ink"
                  }`
                }
              >
                <span>{link.label}</span>
                <span className="text-xs text-steel/45">›</span>
              </NavLink>
            ))}
          </nav>
          </div>
          <div className="border-t border-slate-200 px-6 py-5">
            <div className="rounded-3xl border border-slate-200 bg-slate-50 px-5 py-5">
              <p className="text-xs uppercase tracking-[0.2em] text-steel/60">{dictionary.common.signedInAs}</p>
              <p className="mt-2 text-lg font-semibold text-ink">{displayName}</p>
              <p className="text-sm text-steel">{role}</p>
              <div className="mt-4 flex flex-wrap gap-2">
                <StatusPill tone="good">{session.isAuthenticated ? dictionary.common.sessionActive : dictionary.common.sessionMissing}</StatusPill>
                <StatusPill tone="neutral">{session.permissions.length} {dictionary.common.permissions}</StatusPill>
              </div>
            </div>
            <div className="mt-5 flex gap-3">
            <button
              type="button"
              onClick={() => void onSignin(true)}
              className="app-button-secondary flex-1"
            >
              {dictionary.common.reauth}
            </button>
            <button
              type="button"
              onClick={() => void onSignout()}
              className="app-button-danger flex-1"
            >
              {dictionary.common.signOut}
            </button>
          </div>
          </div>
        </aside>
        <main className="space-y-6">
          <section className="app-surface overflow-hidden">
            <div className="border-b border-slate-200/80 bg-white px-6 py-4">
              <div className="flex flex-col gap-4 xl:flex-row xl:items-center xl:justify-between">
                <div>
                  <div className="flex items-center gap-2 text-xs text-steel/70">
                    <span>{dictionary.common.dashboard}</span>
                    <span>/</span>
                    <span>{routeMeta.eyebrow}</span>
                    <span>/</span>
                    <span className="font-semibold text-ink">{routeMeta.title}</span>
                  </div>
                  <h2 className="mt-3 text-3xl font-semibold tracking-tight text-ink">{routeMeta.title}</h2>
                  <p className="mt-2 max-w-3xl text-sm leading-6 text-steel">{routeMeta.description}</p>
                </div>
                <div className="flex flex-wrap items-center gap-3">
                  <button type="button" className="rounded-full border border-slate-200 bg-white px-3 py-2 text-sm text-steel shadow-sm">⌕</button>
                  <button type="button" className="rounded-full border border-slate-200 bg-white px-3 py-2 text-sm text-steel shadow-sm">⟳</button>
                  <div className="rounded-2xl border border-slate-200 bg-slate-50 px-4 py-3 text-right">
                    <p className="text-[11px] font-semibold uppercase tracking-[0.18em] text-steel/70">{dictionary.common.sessionRole}</p>
                    <p className="mt-1 text-sm font-semibold text-ink">{role}</p>
                  </div>
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

function SignInScreen({ onSignin, error }: { onSignin: (fresh?: boolean) => Promise<void>; error: string | null }) {
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
  }, [onUserChanged, user]);

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
              <PermissionsPage accessToken={accessToken} onReauthenticate={onSignin} capabilities={capabilities} />
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
        <Route path="/audit" element={<AuditPage accessToken={accessToken} onReauthenticate={onSignin} />} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </Shell>
  );
}

export default function App() {
  const { dictionary } = useI18n();
  const { manager, user, setUser, isBootstrapping, bootError, signin, signout } = useAdminOidc();
  const location = useLocation();

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
    return <SignInScreen onSignin={signin} error={null} />;
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
