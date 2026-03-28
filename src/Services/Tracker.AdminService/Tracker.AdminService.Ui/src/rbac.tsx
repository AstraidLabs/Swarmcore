import { useDeferredValue, useEffect, useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { type PageResult } from "./catalog";
import { CatalogToolbar, ConfirmActionModal, Modal, PaginationFooter, PreviewDrawer, SortHeaderButton, TableStateRow, useCatalogViewState } from "./catalog.tsx";

type PageProps = {
  accessToken: string;
  onReauthenticate: (fresh?: boolean) => Promise<void>;
  permissions: string[];
};

type UpdateProfileRequest = {
  displayName: string;
  timeZone: string;
};

type AdminProfileDetailResponse = {
  userId: string;
  userName: string;
  email: string;
  displayName: string;
  timeZone: string;
  isActive: boolean;
  accountState: string;
  roles: string[];
  effectivePermissions: string[];
  createdAtUtc: string;
  lastLoginAtUtc?: string | null;
};

type PaginatedResult<T> = PageResult<T>;

type AdminUserListItemDto = {
  userId: string;
  userName: string;
  email: string;
  displayName: string;
  isActive: boolean;
  roles: string[];
  createdAtUtc: string;
  lastLoginAtUtc?: string | null;
};

type AdminUserDetailDto = {
  userId: string;
  userName: string;
  email: string;
  displayName: string;
  isActive: boolean;
  timeZone: string;
  roles: string[];
  effectivePermissions: string[];
  createdAtUtc: string;
  lastLoginAtUtc?: string | null;
};

type CreateAdminUserRequest = {
  userName: string;
  email: string;
  password: string;
  displayName: string;
  roles: string[];
};

type UpdateAdminUserRequest = {
  displayName: string;
  email: string;
};

type RoleListItemDto = {
  roleId: string;
  name: string;
  description: string;
  isSystemRole: boolean;
  priority: number;
  userCount: number;
  createdAtUtc: string;
};

type RoleDetailDto = {
  roleId: string;
  name: string;
  description: string;
  isSystemRole: boolean;
  priority: number;
  permissionGroupIds: string[];
  directPermissionKeys: string[];
  effectivePermissionKeys: string[];
  createdAtUtc: string;
  updatedAtUtc: string;
};

type PermissionGroupListItemDto = {
  id: string;
  name: string;
  description: string;
  isSystemGroup: boolean;
  permissionCount: number;
  createdAtUtc: string;
};

type PermissionGroupDetailDto = {
  id: string;
  name: string;
  description: string;
  isSystemGroup: boolean;
  permissionKeys: string[];
  createdAtUtc: string;
  updatedAtUtc: string;
};

type PermissionDefinitionDto = {
  id: string;
  key: string;
  name: string;
  description: string;
  category: string;
  isSystemPermission: boolean;
};

type CreateRoleRequest = {
  name: string;
  description: string;
  priority: number;
};

type UpdateRoleRequest = {
  description: string;
  priority: number;
};

type CreatePermissionGroupRequest = {
  name: string;
  description: string;
};

type UpdatePermissionGroupRequest = {
  name: string;
  description: string;
};

type SortDirection = "asc" | "desc";

export const permissionKeys = {
  dashboardView: "admin.dashboard.view",
  profileView: "admin.profile.view",
  profileEdit: "admin.profile.edit",
  usersView: "admin.users.view",
  usersCreate: "admin.users.create",
  usersEdit: "admin.users.edit",
  usersActivate: "admin.users.activate",
  usersDeactivate: "admin.users.deactivate",
  usersAssignRoles: "admin.users.assign_roles",
  usersResetPassword: "admin.users.reset_password",
  rolesView: "admin.roles.view",
  rolesCreate: "admin.roles.create",
  rolesEdit: "admin.roles.edit",
  rolesDelete: "admin.roles.delete",
  rolesAssignPermissions: "admin.roles.assign_permissions",
  permissionGroupsView: "admin.permission_groups.view",
  permissionGroupsCreate: "admin.permission_groups.create",
  permissionGroupsEdit: "admin.permission_groups.edit",
  permissionGroupsDelete: "admin.permission_groups.delete",
  permissionCatalogView: "admin.permission_catalog.view",
  auditView: "admin.audit.view"
} as const;

function compareValues(left: string | number | boolean, right: string | number | boolean, direction: SortDirection) {
  const multiplier = direction === "asc" ? 1 : -1;
  if (typeof left === "number" && typeof right === "number") {
    return (left - right) * multiplier;
  }

  const leftValue = typeof left === "boolean" ? Number(left) : String(left).toLowerCase();
  const rightValue = typeof right === "boolean" ? Number(right) : String(right).toLowerCase();
  if (leftValue < rightValue) return -1 * multiplier;
  if (leftValue > rightValue) return 1 * multiplier;
  return 0;
}

async function requestJson<T>(
  path: string,
  accessToken: string,
  onReauthenticate: (fresh?: boolean) => Promise<void>,
  init?: RequestInit
): Promise<T> {
  const headers = new Headers(init?.headers ?? {});
  headers.set("Authorization", `Bearer ${accessToken}`);
  if (init?.body && !headers.has("Content-Type")) {
    headers.set("Content-Type", "application/json");
  }

  const response = await fetch(path, { ...init, headers });
  if (response.status === 401) {
    await onReauthenticate(true);
    throw new Error("Session refresh required.");
  }
  if (!response.ok) {
    try {
      const payload = await response.json();
      throw new Error(payload?.message ?? payload?.detail ?? payload?.title ?? response.statusText);
    } catch {
      throw new Error(response.statusText || "Request failed.");
    }
  }
  if (response.status === 204) {
    return undefined as T;
  }
  return response.json() as Promise<T>;
}

export function hasPermission(grantedPermissions: string[], requiredPermission: string) {
  return grantedPermissions.some((item) => item.localeCompare(requiredPermission, undefined, { sensitivity: "accent" }) === 0);
}

export function sortPermissionKeys(permissionList: string[]) {
  return [...permissionList].sort((left, right) => left.localeCompare(right));
}

export function computeInheritedPermissions(effectivePermissions: string[], directPermissions: string[]) {
  const directPermissionSet = new Set(directPermissions);
  return sortPermissionKeys(effectivePermissions.filter((item) => !directPermissionSet.has(item)));
}

function formatDateTime(value?: string | null) {
  if (!value) return "Never";
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString("cs-CZ");
}

function formatDate(value?: string | null) {
  if (!value) return "N/A";
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleDateString("cs-CZ");
}

function PermissionList({ permissions }: { permissions: string[] }) {
  if (permissions.length === 0) {
    return <div className="app-empty-state">No permissions in this selection.</div>;
  }

  return (
    <div className="flex flex-wrap gap-2">
      {permissions.map((permission) => (
        <span key={permission} className="app-chip">
          {permission}
        </span>
      ))}
    </div>
  );
}

function PermissionBreakdown({
  directPermissions,
  effectivePermissions
}: {
  directPermissions: string[];
  effectivePermissions: string[];
}) {
  const inheritedPermissions = useMemo(
    () => computeInheritedPermissions(effectivePermissions, directPermissions),
    [directPermissions, effectivePermissions]
  );

  return (
    <div className="space-y-4">
      <div className="space-y-2">
        <h3 className="text-sm font-semibold text-ink">Direct permissions</h3>
        <PermissionList permissions={sortPermissionKeys(directPermissions)} />
      </div>
      <div className="space-y-2">
        <h3 className="text-sm font-semibold text-ink">Inherited permissions</h3>
        <PermissionList permissions={inheritedPermissions} />
      </div>
      <div className="space-y-2">
        <h3 className="text-sm font-semibold text-ink">Effective union</h3>
        <PermissionList permissions={sortPermissionKeys(effectivePermissions)} />
      </div>
    </div>
  );
}

export function PermissionGate({
  permissions,
  permission,
  children
}: {
  permissions: string[];
  permission: string;
  children: ReactNode;
}) {
  if (hasPermission(permissions, permission)) {
    return <>{children}</>;
  }

  return (
    <div className="app-card">
      <div className="app-card-body space-y-4">
        <div className="space-y-2">
          <div className="app-kicker">Authorization</div>
          <h2 className="text-2xl font-bold text-ink">Access denied</h2>
          <p className="text-sm text-steel">The current session does not have the permission required to open this screen.</p>
        </div>
        <div className="app-subtle-panel">
          <div className="text-xs font-semibold uppercase tracking-[0.18em] text-steel/60">Missing permission</div>
          <div className="mt-2 font-mono text-sm text-ink">{permission}</div>
        </div>
        <div className="flex flex-wrap gap-3">
          <Link className="app-button-primary" to="/">Go to dashboard</Link>
          <Link className="app-button-secondary" to="/profile">Open profile</Link>
        </div>
      </div>
    </div>
  );
}

export function ProfilePage({ accessToken, onReauthenticate }: PageProps) {
  const [profile, setProfile] = useState<AdminProfileDetailResponse | null>(null);
  const [form, setForm] = useState<UpdateProfileRequest>({ displayName: "", timeZone: "UTC" });
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    requestJson<AdminProfileDetailResponse>("/api/admin/rbac/profile", accessToken, onReauthenticate)
      .then((value) => {
        setProfile(value);
        setForm({ displayName: value.displayName, timeZone: value.timeZone });
      })
      .catch((requestError) => setError(requestError instanceof Error ? requestError.message : "Unable to load profile."));
  }, [accessToken, onReauthenticate]);

  const save = async () => {
    setError(null);
    setMessage(null);
    try {
      await requestJson("/api/admin/rbac/profile", accessToken, onReauthenticate, {
        method: "PUT",
        body: JSON.stringify(form)
      });
      const refreshed = await requestJson<AdminProfileDetailResponse>("/api/admin/rbac/profile", accessToken, onReauthenticate);
      setProfile(refreshed);
      setMessage("Profile updated.");
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : "Unable to update profile.");
    }
  };

  return (
    <div className="app-page-stack">
      <div className="app-card">
        <div className="app-card-header">
          <div className="app-kicker">Self-service</div>
          <h1 className="text-4xl font-extrabold tracking-tight text-ink">Admin profile</h1>
        </div>
        <div className="app-card-body app-form-grid">
          <div className="space-y-3">
            <label className="space-y-2">
              <span className="text-sm font-medium text-ink">Display name</span>
              <input className="app-input" value={form.displayName} onChange={(event) => setForm((current) => ({ ...current, displayName: event.target.value }))} />
            </label>
            <label className="space-y-2">
              <span className="text-sm font-medium text-ink">Time zone</span>
              <input className="app-input" value={form.timeZone} onChange={(event) => setForm((current) => ({ ...current, timeZone: event.target.value }))} />
            </label>
            <button type="button" className="app-button-primary" onClick={save}>Save profile</button>
            {message ? <div className="app-notice-success">{message}</div> : null}
            {error ? <div className="app-notice-warn">{error}</div> : null}
          </div>
          <div className="space-y-4">
            <div className="app-subtle-panel space-y-2">
              <div className="app-kicker">Identity</div>
              <div className="text-sm text-steel">User name</div>
              <div className="font-semibold text-ink">{profile?.userName ?? "Loading..."}</div>
              <div className="text-sm text-steel">Email</div>
              <div className="font-semibold text-ink">{profile?.email ?? "Loading..."}</div>
            </div>
            <div className="app-subtle-panel space-y-2">
              <div className="app-kicker">Effective permissions</div>
              <PermissionList permissions={sortPermissionKeys(profile?.effectivePermissions ?? [])} />
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

export function AdminUsersPage({ accessToken, onReauthenticate }: PageProps) {
  const [view, setView] = useCatalogViewState({
    search: "",
    filter: "all",
    sort: "name:asc",
    page: 1,
    pageSize: 25
  });
  const { query, preview: previewUserId, modal, id: routeUserId } = view;
  const [items, setItems] = useState<AdminUserListItemDto[]>([]);
  const [roleOptions, setRoleOptions] = useState<RoleListItemDto[]>([]);
  const [selectedUserIds, setSelectedUserIds] = useState<string[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [editingUserId, setEditingUserId] = useState<string | null>(null);
  const [editingDetail, setEditingDetail] = useState<AdminUserDetailDto | null>(null);
  const [confirmBulkMode, setConfirmBulkMode] = useState<"activate" | "deactivate" | null>(null);
  const [createForm, setCreateForm] = useState<CreateAdminUserRequest>({ userName: "", email: "", password: "", displayName: "", roles: [] });
  const [editForm, setEditForm] = useState({ displayName: "", email: "", roles: [] as string[], resetPassword: "" });
  const deferredSearch = useDeferredValue(query.search);

  const load = async () => {
    setIsLoading(true);
    try {
      const params = new URLSearchParams({
        page: String(query.page),
        pageSize: String(query.pageSize),
        filter: query.filter,
        sort: query.sort
      });
      if (deferredSearch.trim()) {
        params.set("search", deferredSearch.trim());
      }

      const [users, roles] = await Promise.all([
        requestJson<PaginatedResult<AdminUserListItemDto>>(`/api/admin/rbac/users?${params.toString()}`, accessToken, onReauthenticate),
        requestJson<PaginatedResult<RoleListItemDto>>("/api/admin/rbac/roles?page=1&pageSize=250&filter=all&sort=name:asc", accessToken, onReauthenticate)
      ]);
      setItems(users.items);
      setTotalCount(users.totalCount);
      setRoleOptions(roles.items);
      setSelectedUserIds((current) => current.filter((id) => users.items.some((item) => item.userId === id)));
    } finally {
      setIsLoading(false);
    }
  };

  useEffect(() => {
    load().catch((requestError) => setError(requestError instanceof Error ? requestError.message : "Unable to load admin users."));
  }, [accessToken, deferredSearch, onReauthenticate, query.filter, query.page, query.pageSize, query.sort]);

  useEffect(() => {
    if (!previewUserId || isLoading) return;
    if (!items.some((item) => item.userId === previewUserId)) {
      setView((current) => ({ ...current, preview: null }));
    }
  }, [isLoading, items, previewUserId, setView]);

  useEffect(() => {
    if (modal === "create") {
      setEditingUserId(null);
      setEditingDetail(null);
      return;
    }

    if (modal !== "edit" || !routeUserId) {
      setEditingUserId(null);
      setEditingDetail(null);
      return;
    }

    if (editingUserId === routeUserId && editingDetail) {
      return;
    }

    openEdit(routeUserId).catch((requestError) => {
      setError(requestError instanceof Error ? requestError.message : "Unable to open user editor.");
      setView((current) => ({ ...current, modal: null, id: null }));
    });
  }, [editingDetail, editingUserId, modal, routeUserId, setView]);

  const [activeSortField, activeSortDirection] = query.sort.split(":") as [string, SortDirection];
  const selectedRecords = items.filter((item) => selectedUserIds.includes(item.userId));
  const selectedActiveIds = selectedRecords.filter((item) => item.isActive).map((item) => item.userId);
  const selectedInactiveIds = selectedRecords.filter((item) => !item.isActive).map((item) => item.userId);
  const allPageSelected = items.length > 0 && items.every((item) => selectedUserIds.includes(item.userId));
  const previewUser = items.find((item) => item.userId === previewUserId) ?? null;
  const pageCount = Math.max(1, Math.ceil(totalCount / query.pageSize));

  const toggleRole = (currentRoles: string[], role: string) =>
    currentRoles.includes(role) ? currentRoles.filter((item) => item !== role) : [...currentRoles, role];

  const openEdit = async (userId: string) => {
    const detail = await requestJson<AdminUserDetailDto>(`/api/admin/rbac/users/${encodeURIComponent(userId)}`, accessToken, onReauthenticate);
    setEditingDetail(detail);
    setEditForm({ displayName: detail.displayName, email: detail.email, roles: detail.roles, resetPassword: "" });
    setEditingUserId(userId);
    setView((current) => ({ ...current, modal: "edit", id: userId, preview: current.preview === userId ? null : current.preview }));
  };

  const saveCreate = async () => {
    try {
      await requestJson("/api/admin/rbac/users", accessToken, onReauthenticate, { method: "POST", body: JSON.stringify(createForm) });
      setView((current) => ({ ...current, modal: null, id: null }));
      setCreateForm({ userName: "", email: "", password: "", displayName: "", roles: [] });
      setMessage("Admin user created.");
      await load();
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : "Unable to create admin user.");
    }
  };

  const saveEdit = async () => {
    if (!editingUserId) return;
    try {
      await requestJson(`/api/admin/rbac/users/${encodeURIComponent(editingUserId)}`, accessToken, onReauthenticate, {
        method: "PUT",
        body: JSON.stringify({ displayName: editForm.displayName, email: editForm.email } satisfies UpdateAdminUserRequest)
      });
      await requestJson(`/api/admin/rbac/users/${encodeURIComponent(editingUserId)}/roles`, accessToken, onReauthenticate, {
        method: "PUT",
        body: JSON.stringify({ roles: editForm.roles })
      });
      if (editForm.resetPassword.trim()) {
        await requestJson(`/api/admin/rbac/users/${encodeURIComponent(editingUserId)}/reset-password`, accessToken, onReauthenticate, {
          method: "POST",
          body: JSON.stringify({ newPassword: editForm.resetPassword })
        });
      }
      setEditingUserId(null);
      setEditingDetail(null);
      setView((current) => ({ ...current, modal: null, id: null }));
      setMessage("Admin user updated.");
      await load();
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : "Unable to update admin user.");
    }
  };

  const changeActivation = async (userIds: string[], activate: boolean) => {
    try {
      if (userIds.length === 1) {
        await requestJson(`/api/admin/rbac/users/${encodeURIComponent(userIds[0])}/${activate ? "activate" : "deactivate"}`, accessToken, onReauthenticate, { method: "POST" });
      } else {
        await requestJson(`/api/admin/rbac/users/bulk-${activate ? "activate" : "deactivate"}`, accessToken, onReauthenticate, {
          method: "POST",
          body: JSON.stringify({ userIds })
        });
      }
      setMessage(activate ? "Admin user activation updated." : "Admin user deactivation updated.");
      setSelectedUserIds([]);
      await load();
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : "Unable to update account state.");
    }
  };

  const toggleSort = (field: string) => {
      setView((current) => {
        const currentSort = current.query.sort;
      const [currentField, currentDirection] = currentSort.split(":") as [string, SortDirection];
      if (currentField === field) {
        return { ...current, query: { ...current.query, sort: `${field}:${currentDirection === "asc" ? "desc" : "asc"}`, page: 1 } };
      }
      const defaultDirection: SortDirection = field === "created" || field === "lastLogin" ? "desc" : "asc";
      return { ...current, query: { ...current.query, sort: `${field}:${defaultDirection}`, page: 1 } };
    });
  };

  const toggleUserSelection = (userId: string) => {
    setSelectedUserIds((current) => current.includes(userId) ? current.filter((item) => item !== userId) : [...current, userId]);
  };

  const toggleCurrentPageSelection = () => {
    setSelectedUserIds((current) => {
      if (allPageSelected) {
        return current.filter((id) => !items.some((item) => item.userId === id));
      }
      const next = new Set(current);
      items.forEach((item) => next.add(item.userId));
      return [...next];
    });
  };

  return (
    <div className="app-page-stack">
      <CatalogToolbar
        title="Admin users"
        description="Search and manage admin accounts from one grid."
        totalCount={totalCount}
        search={query.search}
        onSearchChange={(value) => setView((current) => ({ ...current, query: { ...current.query, search: value, page: 1 } }))}
        searchPlaceholder="Search users, emails or roles"
        sortValue={query.sort}
        onSortChange={(value) => setView((current) => ({ ...current, query: { ...current.query, sort: value, page: 1 } }))}
        sortOptions={[
          { value: "name:asc", label: "Name A-Z" },
          { value: "created:desc", label: "Newest first" },
          { value: "lastLogin:desc", label: "Recent activity" },
          { value: "email:asc", label: "Email A-Z" }
        ]}
        pageSize={query.pageSize}
        onPageSizeChange={(value) => setView((current) => ({ ...current, query: { ...current.query, pageSize: value, page: 1 } }))}
        filter={query.filter}
        onFilterChange={(value) => setView((current) => ({ ...current, query: { ...current.query, filter: value, page: 1 } }))}
        filterOptions={[
          { value: "all", label: "All users" },
          { value: "active", label: "Active only" },
          { value: "inactive", label: "Inactive only" },
          { value: "superadmin", label: "SuperAdmins" }
        ]}
        createLabel="Create admin user"
        onCreate={() => setView((current) => ({ ...current, modal: "create", id: null, preview: null }))}
      />
      {message ? <div className="app-notice-success">{message}</div> : null}
      {error ? <div className="app-notice-warn">{error}</div> : null}
      {selectedUserIds.length > 0 ? (
        <div className="app-selection-summary">
          <div className="text-sm font-medium text-ink">{selectedUserIds.length} selected</div>
          <div className="flex flex-wrap gap-3">
            <button type="button" className="app-button-secondary py-2.5" onClick={() => setSelectedUserIds([])}>
              Clear selection
            </button>
            <button type="button" className="app-button-secondary py-2.5" disabled={selectedInactiveIds.length === 0} onClick={() => setConfirmBulkMode("activate")}>
              Activate selected ({selectedInactiveIds.length})
            </button>
            <button type="button" className="app-button-danger py-2.5" disabled={selectedActiveIds.length === 0} onClick={() => setConfirmBulkMode("deactivate")}>
              Deactivate selected ({selectedActiveIds.length})
            </button>
          </div>
        </div>
      ) : null}

      <div className="app-data-table">
        <div className="overflow-x-auto">
          <table className="min-w-full text-sm">
            <thead className="app-table-head">
              <tr>
                <th className="px-5 py-4">
                  <input type="checkbox" checked={allPageSelected} aria-label="Select current page" onChange={toggleCurrentPageSelection} />
                </th>
                <th className="px-5 py-4">
                  <SortHeaderButton label="User" active={activeSortField === "name"} direction={activeSortDirection} onClick={() => toggleSort("name")} />
                </th>
                <th className="px-5 py-4">
                  <SortHeaderButton label="Email" active={activeSortField === "email"} direction={activeSortDirection} onClick={() => toggleSort("email")} />
                </th>
                <th className="px-5 py-4">Roles</th>
                <th className="px-5 py-4">Status</th>
                <th className="px-5 py-4">
                  <SortHeaderButton label="Created" active={activeSortField === "created"} direction={activeSortDirection} onClick={() => toggleSort("created")} />
                </th>
                <th className="px-5 py-4">
                  <SortHeaderButton label="Last login" active={activeSortField === "lastLogin"} direction={activeSortDirection} onClick={() => toggleSort("lastLogin")} />
                </th>
                <th className="px-5 py-4 text-right">Actions</th>
              </tr>
            </thead>
            <tbody>
              {isLoading ? (
                <TableStateRow colSpan={8} title="Loading admin users" message="Fetching the latest catalog and role assignments." />
              ) : error && totalCount === 0 ? (
                <TableStateRow colSpan={8} title="Unable to load admin users" message={error} />
              ) : items.length === 0 ? (
                <TableStateRow colSpan={8} title="No users match this view" message="Try a broader search, different filter or larger page size." />
              ) : items.map((item) => (
                <tr key={item.userId} className="app-table-row border-t border-slate-200/80 align-top">
                  <td className="px-5 py-4">
                    <input type="checkbox" checked={selectedUserIds.includes(item.userId)} aria-label={`Select ${item.userName}`} onChange={() => toggleUserSelection(item.userId)} />
                  </td>
                  <td className="px-5 py-4">
                    <div className="font-semibold text-ink">{item.displayName || item.userName}</div>
                    <div className="text-xs text-steel">{item.userName}</div>
                  </td>
                  <td className="px-5 py-4 text-steel">{item.email}</td>
                  <td className="px-5 py-4">
                    <div className="flex flex-wrap gap-2">
                      {item.roles.map((role) => (
                        <span key={role} className="app-chip">{role}</span>
                      ))}
                    </div>
                  </td>
                  <td className="px-5 py-4">
                    <span className={item.isActive ? "app-chip-soft" : "app-chip"}>{item.isActive ? "Active" : "Inactive"}</span>
                  </td>
                  <td className="px-5 py-4 text-steel">{formatDate(item.createdAtUtc)}</td>
                  <td className="px-5 py-4 text-steel">{formatDateTime(item.lastLoginAtUtc)}</td>
                  <td className="px-5 py-4">
                    <div className="flex justify-end gap-2">
                      <button type="button" className="app-button-secondary px-4 py-2.5" onClick={() => openEdit(item.userId).catch((requestError) => setError(requestError instanceof Error ? requestError.message : "Unable to open user editor."))}>Edit</button>
                      <button type="button" className={item.isActive ? "app-button-danger px-4 py-2.5" : "app-button-secondary px-4 py-2.5"} onClick={() => changeActivation([item.userId], !item.isActive)}>
                        {item.isActive ? "Deactivate" : "Activate"}
                      </button>
                      <button type="button" className="app-button-secondary px-4 py-2.5" onClick={() => setView((current) => ({ ...current, preview: item.userId }))}>
                        Preview
                      </button>
                    </div>
                  </td>
                </tr>
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
        onPageChange={(value) => setView((current) => ({ ...current, query: { ...current.query, page: value } }))}
      />

      <Modal
        open={modal === "create"}
        onClose={() => setView((current) => ({ ...current, modal: null, id: null }))}
        title="Create admin user"
        description="Provision a new admin account and assign initial roles."
      >
        <div className="space-y-4">
          <div className="app-form-grid">
            <input className="app-input" placeholder="User name" value={createForm.userName} onChange={(event) => setCreateForm((current) => ({ ...current, userName: event.target.value }))} />
            <input className="app-input" placeholder="Display name" value={createForm.displayName} onChange={(event) => setCreateForm((current) => ({ ...current, displayName: event.target.value }))} />
            <input className="app-input" placeholder="Email" value={createForm.email} onChange={(event) => setCreateForm((current) => ({ ...current, email: event.target.value }))} />
            <input className="app-input" placeholder="Temporary password" value={createForm.password} onChange={(event) => setCreateForm((current) => ({ ...current, password: event.target.value }))} />
          </div>
          <div className="space-y-3">
            <div className="text-sm font-semibold text-ink">Roles</div>
            <div className="flex flex-wrap gap-2">
              {roleOptions.map((role) => (
                <label key={role.roleId} className="app-checkbox-chip">
                  <input type="checkbox" checked={createForm.roles.includes(role.name)} onChange={() => setCreateForm((current) => ({ ...current, roles: toggleRole(current.roles, role.name) }))} />
                  <span>{role.name}</span>
                </label>
              ))}
            </div>
          </div>
          <div className="flex justify-end gap-3">
            <button type="button" className="app-button-secondary" onClick={() => setView((current) => ({ ...current, modal: null, id: null }))}>Cancel</button>
            <button type="button" className="app-button-primary" onClick={saveCreate}>Create admin user</button>
          </div>
        </div>
      </Modal>

      <Modal
        open={modal === "edit" && editingUserId !== null}
        onClose={() => {
          setEditingUserId(null);
          setEditingDetail(null);
          setView((current) => ({ ...current, modal: null, id: null }));
        }}
        title="Edit admin user"
        description="Update profile data, role assignments and lifecycle controls."
        width="xwide"
      >
        <div className="grid gap-6 xl:grid-cols-[1.1fr,0.9fr]">
          <div className="space-y-4">
            <div className="app-form-grid">
              <input className="app-input" value={editingDetail?.userName ?? ""} disabled />
              <input className="app-input" value={editForm.displayName} onChange={(event) => setEditForm((current) => ({ ...current, displayName: event.target.value }))} />
              <input className="app-input md:col-span-2" value={editForm.email} onChange={(event) => setEditForm((current) => ({ ...current, email: event.target.value }))} />
            </div>
            <div className="space-y-3">
              <div className="text-sm font-semibold text-ink">Assigned roles</div>
              <div className="flex flex-wrap gap-2">
                {roleOptions.map((role) => (
                  <label key={role.roleId} className="app-checkbox-chip">
                    <input type="checkbox" checked={editForm.roles.includes(role.name)} onChange={() => setEditForm((current) => ({ ...current, roles: toggleRole(current.roles, role.name) }))} />
                    <span>{role.name}</span>
                  </label>
                ))}
              </div>
            </div>
            <div className="space-y-2">
              <div className="text-sm font-semibold text-ink">Reset password</div>
              <input className="app-input" placeholder="Leave empty to keep current password" value={editForm.resetPassword} onChange={(event) => setEditForm((current) => ({ ...current, resetPassword: event.target.value }))} />
            </div>
            <div className="flex flex-wrap gap-3">
              <button type="button" className="app-button-primary" onClick={saveEdit}>Save changes</button>
              {editingDetail ? (
                <button
                  type="button"
                  className={editingDetail.isActive ? "app-button-danger" : "app-button-secondary"}
                  onClick={() => changeActivation([editingDetail.userId], !editingDetail.isActive)}
                >
                  {editingDetail.isActive ? "Deactivate user" : "Activate user"}
                </button>
              ) : null}
            </div>
          </div>
          <div className="space-y-4">
            <div className="app-subtle-panel space-y-2">
              <div className="app-kicker">Identity</div>
              <div className="text-sm text-steel">Created</div>
              <div className="font-semibold text-ink">{formatDateTime(editingDetail?.createdAtUtc)}</div>
              <div className="text-sm text-steel">Last login</div>
              <div className="font-semibold text-ink">{formatDateTime(editingDetail?.lastLoginAtUtc)}</div>
              <div className="text-sm text-steel">Time zone</div>
              <div className="font-semibold text-ink">{editingDetail?.timeZone ?? "N/A"}</div>
            </div>
            <div className="app-subtle-panel space-y-2">
              <div className="app-kicker">Effective permissions</div>
              <PermissionList permissions={sortPermissionKeys(editingDetail?.effectivePermissions ?? [])} />
            </div>
          </div>
        </div>
      </Modal>
      <ConfirmActionModal
        open={confirmBulkMode === "activate"}
        title="Activate selected users"
        description={`This will activate ${selectedInactiveIds.length} selected account(s).`}
        confirmLabel="Activate selected"
        tone="primary"
        onClose={() => setConfirmBulkMode(null)}
        onConfirm={() => {
          setConfirmBulkMode(null);
          void changeActivation(selectedInactiveIds, true);
        }}
      />
      <ConfirmActionModal
        open={confirmBulkMode === "deactivate"}
        title="Deactivate selected users"
        description={`This will deactivate ${selectedActiveIds.length} selected account(s).`}
        confirmLabel="Deactivate selected"
        onClose={() => setConfirmBulkMode(null)}
        onConfirm={() => {
          setConfirmBulkMode(null);
          void changeActivation(selectedActiveIds, false);
        }}
      />
      <PreviewDrawer
        open={previewUser !== null}
        title={previewUser?.displayName || previewUser?.userName || ""}
        subtitle={previewUser?.email}
        onClose={() => setView((current) => ({ ...current, preview: null }))}
      >
        {previewUser ? (
          <div className="space-y-4">
            <div className="app-detail-grid">
              <div className="app-subtle-panel space-y-2">
                <div className="app-kicker">Identity</div>
                <div className="text-sm text-steel">User name</div>
                <div className="font-semibold text-ink">{previewUser.userName}</div>
              </div>
              <div className="app-subtle-panel space-y-2">
                <div className="app-kicker">Status</div>
                <div className="font-semibold text-ink">{previewUser.isActive ? "Active" : "Inactive"}</div>
              </div>
              <div className="app-subtle-panel space-y-2">
                <div className="app-kicker">Created</div>
                <div className="font-semibold text-ink">{formatDate(previewUser.createdAtUtc)}</div>
              </div>
              <div className="app-subtle-panel space-y-2">
                <div className="app-kicker">Last login</div>
                <div className="font-semibold text-ink">{formatDateTime(previewUser.lastLoginAtUtc)}</div>
              </div>
            </div>
            <div className="space-y-2">
              <div className="text-sm font-semibold text-ink">Assigned roles</div>
              <div className="flex flex-wrap gap-2">
                {previewUser.roles.map((role) => (
                  <span key={role} className="app-chip">{role}</span>
                ))}
              </div>
            </div>
            <div className="flex flex-wrap gap-3">
              <button type="button" className="app-button-primary" onClick={() => void openEdit(previewUser.userId)}>Edit user</button>
              <button type="button" className={previewUser.isActive ? "app-button-danger" : "app-button-secondary"} onClick={() => void changeActivation([previewUser.userId], !previewUser.isActive)}>
                {previewUser.isActive ? "Deactivate" : "Activate"}
              </button>
            </div>
          </div>
        ) : null}
      </PreviewDrawer>
    </div>
  );
}

export function RolesPage({ accessToken, onReauthenticate }: PageProps) {
  const [view, setView] = useCatalogViewState({
    search: "",
    filter: "all",
    sort: "priority:desc",
    page: 1,
    pageSize: 25
  });
  const { query, preview: previewRoleId, modal, id: routeRoleId } = view;
  const [items, setItems] = useState<RoleListItemDto[]>([]);
  const [groups, setGroups] = useState<PermissionGroupListItemDto[]>([]);
  const [permissions, setPermissions] = useState<PermissionDefinitionDto[]>([]);
  const [selectedRoleIds, setSelectedRoleIds] = useState<string[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [editingRole, setEditingRole] = useState<RoleDetailDto | null>(null);
  const [confirmDeleteRolesOpen, setConfirmDeleteRolesOpen] = useState(false);
  const [permissionSearch, setPermissionSearch] = useState("");
  const [createForm, setCreateForm] = useState<CreateRoleRequest>({ name: "", description: "", priority: 100 });
  const [editForm, setEditForm] = useState({ description: "", priority: 100, permissionGroupIds: [] as string[], directPermissionKeys: [] as string[] });
  const deferredSearch = useDeferredValue(query.search);
  const deferredPermissionSearch = useDeferredValue(permissionSearch);

  const load = async () => {
    setIsLoading(true);
    try {
      const query = new URLSearchParams({
        page: String(query.page),
        pageSize: String(query.pageSize),
        filter: query.filter,
        sort: query.sort
      });
      if (deferredSearch.trim()) {
        query.set("search", deferredSearch.trim());
      }

      const [roleResult, groupResult, permissionItems] = await Promise.all([
        requestJson<PaginatedResult<RoleListItemDto>>(`/api/admin/rbac/roles?${query.toString()}`, accessToken, onReauthenticate),
        requestJson<PaginatedResult<PermissionGroupListItemDto>>("/api/admin/rbac/permission-groups?page=1&pageSize=250&filter=all&sort=name:asc", accessToken, onReauthenticate),
        requestJson<PermissionDefinitionDto[]>("/api/admin/rbac/permissions", accessToken, onReauthenticate)
      ]);
      setItems(roleResult.items);
      setTotalCount(roleResult.totalCount);
      setGroups(groupResult.items);
      setPermissions(permissionItems);
      setSelectedRoleIds((current) => current.filter((id) => roleResult.items.some((item) => item.roleId === id)));
    } finally {
      setIsLoading(false);
    }
  };

  useEffect(() => {
    load().catch((requestError) => setError(requestError instanceof Error ? requestError.message : "Unable to load roles."));
  }, [accessToken, deferredSearch, onReauthenticate, query.filter, query.page, query.pageSize, query.sort]);

  useEffect(() => {
    if (!previewRoleId || isLoading) return;
    if (!items.some((item) => item.roleId === previewRoleId)) {
      setView((current) => ({ ...current, preview: null }));
    }
  }, [isLoading, items, previewRoleId, setView]);

  useEffect(() => {
    if (modal === "create") {
      setEditingRole(null);
      return;
    }

    if (modal !== "edit" || !routeRoleId) {
      setEditingRole(null);
      return;
    }

    if (editingRole?.roleId === routeRoleId) {
      return;
    }

    openEdit(routeRoleId).catch((requestError) => {
      setError(requestError instanceof Error ? requestError.message : "Unable to open role editor.");
      setView((current) => ({ ...current, modal: null, id: null }));
    });
  }, [editingRole, modal, routeRoleId, setView]);

  const [activeSortField, activeSortDirection] = query.sort.split(":") as [string, SortDirection];
  const selectedRoles = items.filter((item) => selectedRoleIds.includes(item.roleId));
  const selectedDeletableRoleIds = selectedRoles.filter((item) => !item.isSystemRole).map((item) => item.roleId);
  const allPageSelected = items.length > 0 && items.every((item) => selectedRoleIds.includes(item.roleId));
  const previewRole = items.find((item) => item.roleId === previewRoleId) ?? null;
  const filteredPermissionOptions = useMemo(() => {
    const normalizedSearch = deferredPermissionSearch.trim().toLowerCase();
    return permissions.filter((item) => !normalizedSearch || [item.key, item.name, item.category].join(" ").toLowerCase().includes(normalizedSearch));
  }, [deferredPermissionSearch, permissions]);

  const toggleString = (values: string[], value: string) =>
    values.includes(value) ? values.filter((item) => item !== value) : [...values, value];

  const openEdit = async (roleId: string) => {
    const detail = await requestJson<RoleDetailDto>(`/api/admin/rbac/roles/${encodeURIComponent(roleId)}`, accessToken, onReauthenticate);
      setEditingRole(detail);
      setEditForm({
        description: detail.description,
        priority: detail.priority,
        permissionGroupIds: detail.permissionGroupIds,
        directPermissionKeys: detail.directPermissionKeys
      });
      setView((current) => ({ ...current, modal: "edit", id: roleId, preview: current.preview === roleId ? null : current.preview }));
    };

  const createRole = async () => {
    try {
      await requestJson("/api/admin/rbac/roles", accessToken, onReauthenticate, { method: "POST", body: JSON.stringify(createForm) });
      setView((current) => ({ ...current, modal: null, id: null }));
      setCreateForm({ name: "", description: "", priority: 100 });
      setMessage("Role created.");
      await load();
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : "Unable to create role.");
    }
  };

  const saveRole = async () => {
    if (!editingRole) return;
    try {
      await requestJson(`/api/admin/rbac/roles/${encodeURIComponent(editingRole.roleId)}`, accessToken, onReauthenticate, {
        method: "PUT",
        body: JSON.stringify({ description: editForm.description, priority: editForm.priority } satisfies UpdateRoleRequest)
      });
      await requestJson(`/api/admin/rbac/roles/${encodeURIComponent(editingRole.roleId)}/permission-groups`, accessToken, onReauthenticate, {
        method: "PUT",
        body: JSON.stringify({ permissionGroupIds: editForm.permissionGroupIds })
      });
      await requestJson(`/api/admin/rbac/roles/${encodeURIComponent(editingRole.roleId)}/permissions`, accessToken, onReauthenticate, {
        method: "PUT",
        body: JSON.stringify({ permissionKeys: editForm.directPermissionKeys })
      });
      setEditingRole(null);
      setView((current) => ({ ...current, modal: null, id: null }));
      setMessage("Role updated.");
      await load();
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : "Unable to update role.");
    }
  };

  const deleteRoles = async (roleIds: string[]) => {
    if (roleIds.length === 0) return;
    try {
      if (roleIds.length === 1) {
        await requestJson(`/api/admin/rbac/roles/${encodeURIComponent(roleIds[0])}`, accessToken, onReauthenticate, { method: "DELETE" });
      } else {
        await requestJson("/api/admin/rbac/roles/bulk-delete", accessToken, onReauthenticate, {
          method: "POST",
          body: JSON.stringify({ roleIds })
        });
      }
      setMessage(roleIds.length === 1 ? "Role deleted." : `${roleIds.length} roles deleted.`);
      setSelectedRoleIds([]);
      await load();
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : "Unable to delete selected roles.");
    }
  };

  const toggleSort = (field: string) => {
      setView((current) => {
      const [currentField, currentDirection] = current.query.sort.split(":") as [string, SortDirection];
      if (currentField === field) {
        return { ...current, query: { ...current.query, sort: `${field}:${currentDirection === "asc" ? "desc" : "asc"}`, page: 1 } };
      }
      return { ...current, query: { ...current.query, sort: `${field}:${field === "priority" || field === "users" ? "desc" : "asc"}`, page: 1 } };
    });
  };

  const toggleRoleSelection = (roleId: string) => {
    setSelectedRoleIds((current) => current.includes(roleId) ? current.filter((item) => item !== roleId) : [...current, roleId]);
  };

  const toggleCurrentPageSelection = () => {
    setSelectedRoleIds((current) => {
      if (allPageSelected) {
        return current.filter((id) => !items.some((item) => item.roleId === id));
      }
      const next = new Set(current);
      items.forEach((item) => next.add(item.roleId));
      return [...next];
    });
  };

  return (
    <div className="app-page-stack">
      <CatalogToolbar
        title="Roles"
        description="Search and manage roles from one grid."
        totalCount={totalCount}
        search={query.search}
        onSearchChange={(value) => setView((current) => ({ ...current, query: { ...current.query, search: value, page: 1 } }))}
        searchPlaceholder="Search roles or descriptions"
        sortValue={query.sort}
        onSortChange={(value) => setView((current) => ({ ...current, query: { ...current.query, sort: value, page: 1 } }))}
        sortOptions={[
          { value: "priority:desc", label: "Priority high to low" },
          { value: "name:asc", label: "Name A-Z" },
          { value: "users:desc", label: "Most assigned first" }
        ]}
        pageSize={query.pageSize}
        onPageSizeChange={(value) => setView((current) => ({ ...current, query: { ...current.query, pageSize: value, page: 1 } }))}
        filter={query.filter}
        onFilterChange={(value) => setView((current) => ({ ...current, query: { ...current.query, filter: value, page: 1 } }))}
        filterOptions={[
          { value: "all", label: "All roles" },
          { value: "system", label: "System roles" },
          { value: "custom", label: "Custom roles" }
        ]}
        createLabel="Create role"
        onCreate={() => setView((current) => ({ ...current, modal: "create", id: null, preview: null }))}
      />
      {message ? <div className="app-notice-success">{message}</div> : null}
      {error ? <div className="app-notice-warn">{error}</div> : null}
      {selectedRoleIds.length > 0 ? (
        <div className="app-selection-summary">
          <div className="text-sm font-medium text-ink">{selectedRoleIds.length} selected</div>
          <div className="flex flex-wrap gap-3">
            <button type="button" className="app-button-secondary py-2.5" onClick={() => setSelectedRoleIds([])}>
              Clear selection
            </button>
            <button
              type="button"
              className="app-button-danger py-2.5"
              disabled={selectedDeletableRoleIds.length === 0}
              onClick={() => setConfirmDeleteRolesOpen(true)}
            >
              Delete selected ({selectedDeletableRoleIds.length})
            </button>
          </div>
        </div>
      ) : null}

      <div className="app-data-table">
        <div className="overflow-x-auto">
          <table className="min-w-full text-sm">
            <thead className="app-table-head">
              <tr>
                <th className="px-5 py-4">
                  <input type="checkbox" checked={allPageSelected} aria-label="Select current page" onChange={toggleCurrentPageSelection} />
                </th>
                <th className="px-5 py-4">
                  <SortHeaderButton label="Role" active={activeSortField === "name"} direction={activeSortDirection} onClick={() => toggleSort("name")} />
                </th>
                <th className="px-5 py-4">Description</th>
                <th className="px-5 py-4">Type</th>
                <th className="px-5 py-4">
                  <SortHeaderButton label="Priority" active={activeSortField === "priority"} direction={activeSortDirection} onClick={() => toggleSort("priority")} />
                </th>
                <th className="px-5 py-4">
                  <SortHeaderButton label="Users" active={activeSortField === "users"} direction={activeSortDirection} onClick={() => toggleSort("users")} />
                </th>
                <th className="px-5 py-4">Created</th>
                <th className="px-5 py-4 text-right">Actions</th>
              </tr>
            </thead>
            <tbody>
              {isLoading ? (
                <TableStateRow colSpan={8} title="Loading roles" message="Refreshing the role catalog and permission references." />
              ) : error && totalCount === 0 ? (
                <TableStateRow colSpan={8} title="Unable to load roles" message={error} />
              ) : items.length === 0 ? (
                <TableStateRow colSpan={8} title="No roles match this view" message="Try a broader search, a different filter or another sort order." />
              ) : items.map((item) => (
                <tr key={item.roleId} className="app-table-row border-t border-slate-200/80 align-top">
                  <td className="px-5 py-4">
                    <input type="checkbox" checked={selectedRoleIds.includes(item.roleId)} aria-label={`Select ${item.name}`} onChange={() => toggleRoleSelection(item.roleId)} />
                  </td>
                  <td className="px-5 py-4 font-semibold text-ink">{item.name}</td>
                  <td className="px-5 py-4 text-steel">{item.description}</td>
                  <td className="px-5 py-4">
                    <span className={item.isSystemRole ? "app-chip-soft" : "app-chip"}>{item.isSystemRole ? "System" : "Custom"}</span>
                  </td>
                  <td className="px-5 py-4 text-steel">{item.priority}</td>
                  <td className="px-5 py-4 text-steel">{item.userCount}</td>
                  <td className="px-5 py-4 text-steel">{formatDate(item.createdAtUtc)}</td>
                  <td className="px-5 py-4">
                    <div className="flex justify-end gap-2">
                      <button type="button" className="app-button-secondary px-4 py-2.5" onClick={() => openEdit(item.roleId).catch((requestError) => setError(requestError instanceof Error ? requestError.message : "Unable to open role editor."))}>Edit</button>
                      <button
                        type="button"
                        className={item.isSystemRole ? "app-button-secondary px-4 py-2.5 opacity-60" : "app-button-danger px-4 py-2.5"}
                        disabled={item.isSystemRole}
                        onClick={() => {
                          setSelectedRoleIds([item.roleId]);
                          setConfirmDeleteRolesOpen(true);
                        }}
                      >
                        {item.isSystemRole ? "Protected" : "Delete"}
                      </button>
                      <button type="button" className="app-button-secondary px-4 py-2.5" onClick={() => setView((current) => ({ ...current, preview: item.roleId }))}>
                        Preview
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      <PaginationFooter
        page={query.page}
        pageCount={Math.max(1, Math.ceil(totalCount / query.pageSize))}
        totalCount={totalCount}
        pageSize={query.pageSize}
        onPageChange={(value) => setView((current) => ({ ...current, query: { ...current.query, page: value } }))}
      />

      <Modal
        open={modal === "create"}
        onClose={() => setView((current) => ({ ...current, modal: null, id: null }))}
        title="Create role"
        description="Create the role first, then refine its bundle and direct grants."
      >
        <div className="space-y-4">
          <div className="app-form-grid">
            <input className="app-input" placeholder="Role name" value={createForm.name} onChange={(event) => setCreateForm((current) => ({ ...current, name: event.target.value }))} />
            <input className="app-input" type="number" placeholder="Priority" value={createForm.priority} onChange={(event) => setCreateForm((current) => ({ ...current, priority: Number(event.target.value) }))} />
            <textarea className="app-input md:col-span-2 min-h-[140px]" placeholder="Description" value={createForm.description} onChange={(event) => setCreateForm((current) => ({ ...current, description: event.target.value }))} />
          </div>
          <div className="flex justify-end gap-3">
            <button type="button" className="app-button-secondary" onClick={() => setView((current) => ({ ...current, modal: null, id: null }))}>Cancel</button>
            <button type="button" className="app-button-primary" onClick={createRole}>Create role</button>
          </div>
        </div>
      </Modal>

      <Modal
        open={modal === "edit" && editingRole !== null}
        onClose={() => {
          setEditingRole(null);
          setView((current) => ({ ...current, modal: null, id: null }));
        }}
        title="Edit role"
        description="Manage role metadata, assigned groups and direct permissions."
        width="xwide"
      >
        <div className="grid gap-6 xl:grid-cols-[1fr,1fr]">
          <div className="space-y-4">
            <div className="app-form-grid">
              <input className="app-input" value={editingRole?.name ?? ""} disabled />
              <input className="app-input" type="number" value={editForm.priority} onChange={(event) => setEditForm((current) => ({ ...current, priority: Number(event.target.value) }))} />
              <textarea className="app-input md:col-span-2 min-h-[140px]" value={editForm.description} onChange={(event) => setEditForm((current) => ({ ...current, description: event.target.value }))} />
            </div>
            <div className="space-y-3">
              <div className="text-sm font-semibold text-ink">Permission groups</div>
              <div className="flex flex-wrap gap-2">
                {groups.map((group) => (
                  <label key={group.id} className="app-checkbox-chip">
                    <input type="checkbox" checked={editForm.permissionGroupIds.includes(group.id)} onChange={() => setEditForm((current) => ({ ...current, permissionGroupIds: toggleString(current.permissionGroupIds, group.id) }))} />
                    <span>{group.name}</span>
                  </label>
                ))}
          </div>
        </div>
      </div>
          <div className="space-y-4">
            <label className="space-y-2">
              <span className="text-sm font-semibold text-ink">Filter permission catalog</span>
              <input className="app-input" value={permissionSearch} onChange={(event) => setPermissionSearch(event.target.value)} placeholder="Search permissions by key, name or category" />
            </label>
            <div className="app-permission-grid max-h-[420px] overflow-auto pr-1">
              {filteredPermissionOptions.map((permission) => (
                <label key={permission.id} className="app-permission-item flex items-start gap-3">
                  <input type="checkbox" checked={editForm.directPermissionKeys.includes(permission.key)} onChange={() => setEditForm((current) => ({ ...current, directPermissionKeys: toggleString(current.directPermissionKeys, permission.key) }))} />
                  <span className="space-y-1">
                    <span className="block font-medium text-ink">{permission.name}</span>
                    <span className="block text-xs text-steel">{permission.key}</span>
                  </span>
                </label>
              ))}
            </div>
          </div>
        </div>
        <div className="mt-6 space-y-4">
          <PermissionBreakdown directPermissions={editForm.directPermissionKeys} effectivePermissions={editingRole?.effectivePermissionKeys ?? []} />
          <div className="flex justify-end gap-3">
            <button type="button" className="app-button-secondary" onClick={() => {
              setEditingRole(null);
              setView((current) => ({ ...current, modal: null, id: null }));
            }}>Cancel</button>
            <button type="button" className="app-button-primary" onClick={saveRole}>Save role</button>
          </div>
        </div>
      </Modal>
      <ConfirmActionModal
        open={confirmDeleteRolesOpen}
        title="Delete selected roles"
        description={`This will permanently delete ${selectedDeletableRoleIds.length} custom role(s). System roles stay protected.`}
        confirmLabel="Delete selected"
        onClose={() => setConfirmDeleteRolesOpen(false)}
        onConfirm={() => {
          setConfirmDeleteRolesOpen(false);
          void deleteRoles(selectedDeletableRoleIds);
        }}
      />
      <PreviewDrawer
        open={previewRole !== null}
        title={previewRole?.name ?? ""}
        subtitle={previewRole?.description}
        onClose={() => setView((current) => ({ ...current, preview: null }))}
      >
        {previewRole ? (
          <div className="space-y-4">
            <div className="app-detail-grid">
              <div className="app-subtle-panel space-y-2">
                <div className="app-kicker">Type</div>
                <div className="font-semibold text-ink">{previewRole.isSystemRole ? "System role" : "Custom role"}</div>
              </div>
              <div className="app-subtle-panel space-y-2">
                <div className="app-kicker">Priority</div>
                <div className="font-semibold text-ink">{previewRole.priority}</div>
              </div>
              <div className="app-subtle-panel space-y-2">
                <div className="app-kicker">Users</div>
                <div className="font-semibold text-ink">{previewRole.userCount}</div>
              </div>
              <div className="app-subtle-panel space-y-2">
                <div className="app-kicker">Created</div>
                <div className="font-semibold text-ink">{formatDate(previewRole.createdAtUtc)}</div>
              </div>
            </div>
            <div className="flex flex-wrap gap-3">
              <button type="button" className="app-button-primary" onClick={() => void openEdit(previewRole.roleId)}>Edit role</button>
            </div>
          </div>
        ) : null}
      </PreviewDrawer>
    </div>
  );
}

export function PermissionGroupsPage({ accessToken, onReauthenticate }: PageProps) {
  const [view, setView] = useCatalogViewState({
    search: "",
    filter: "all",
    sort: "name:asc",
    page: 1,
    pageSize: 25
  });
  const { query, preview: previewGroupId, modal, id: routeGroupId } = view;
  const [items, setItems] = useState<PermissionGroupListItemDto[]>([]);
  const [permissions, setPermissions] = useState<PermissionDefinitionDto[]>([]);
  const [selectedGroupIds, setSelectedGroupIds] = useState<string[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [editingGroup, setEditingGroup] = useState<PermissionGroupDetailDto | null>(null);
  const [confirmDeleteGroupsOpen, setConfirmDeleteGroupsOpen] = useState(false);
  const [permissionSearch, setPermissionSearch] = useState("");
  const [createForm, setCreateForm] = useState<CreatePermissionGroupRequest>({ name: "", description: "" });
  const [editForm, setEditForm] = useState({ name: "", description: "", permissionKeys: [] as string[] });
  const deferredSearch = useDeferredValue(query.search);
  const deferredPermissionSearch = useDeferredValue(permissionSearch);

  const load = async () => {
    setIsLoading(true);
    try {
      const query = new URLSearchParams({
        page: String(query.page),
        pageSize: String(query.pageSize),
        filter: query.filter,
        sort: query.sort
      });
      if (deferredSearch.trim()) {
        query.set("search", deferredSearch.trim());
      }

      const [groupResult, permissionItems] = await Promise.all([
        requestJson<PaginatedResult<PermissionGroupListItemDto>>(`/api/admin/rbac/permission-groups?${query.toString()}`, accessToken, onReauthenticate),
        requestJson<PermissionDefinitionDto[]>("/api/admin/rbac/permissions", accessToken, onReauthenticate)
      ]);
      setItems(groupResult.items);
      setTotalCount(groupResult.totalCount);
      setPermissions(permissionItems);
      setSelectedGroupIds((current) => current.filter((id) => groupResult.items.some((item) => item.id === id)));
    } finally {
      setIsLoading(false);
    }
  };

  useEffect(() => {
    load().catch((requestError) => setError(requestError instanceof Error ? requestError.message : "Unable to load permission groups."));
  }, [accessToken, deferredSearch, onReauthenticate, query.filter, query.page, query.pageSize, query.sort]);

  useEffect(() => {
    if (!previewGroupId || isLoading) return;
    if (!items.some((item) => item.id === previewGroupId)) {
      setView((current) => ({ ...current, preview: null }));
    }
  }, [isLoading, items, previewGroupId, setView]);

  useEffect(() => {
    if (modal === "create") {
      setEditingGroup(null);
      return;
    }

    if (modal !== "edit" || !routeGroupId) {
      setEditingGroup(null);
      return;
    }

    if (editingGroup?.id === routeGroupId) {
      return;
    }

    openEdit(routeGroupId).catch((requestError) => {
      setError(requestError instanceof Error ? requestError.message : "Unable to open group editor.");
      setView((current) => ({ ...current, modal: null, id: null }));
    });
  }, [editingGroup, modal, routeGroupId, setView]);

  const [activeSortField, activeSortDirection] = query.sort.split(":") as [string, SortDirection];
  const selectedGroups = items.filter((item) => selectedGroupIds.includes(item.id));
  const selectedDeletableGroupIds = selectedGroups.filter((item) => !item.isSystemGroup).map((item) => item.id);
  const allPageSelected = items.length > 0 && items.every((item) => selectedGroupIds.includes(item.id));
  const previewGroup = items.find((item) => item.id === previewGroupId) ?? null;
  const filteredPermissionOptions = useMemo(() => {
    const normalizedSearch = deferredPermissionSearch.trim().toLowerCase();
    return permissions.filter((item) => !normalizedSearch || [item.key, item.name, item.category].join(" ").toLowerCase().includes(normalizedSearch));
  }, [deferredPermissionSearch, permissions]);

  const openEdit = async (groupId: string) => {
    const detail = await requestJson<PermissionGroupDetailDto>(`/api/admin/rbac/permission-groups/${groupId}`, accessToken, onReauthenticate);
    setEditingGroup(detail);
    setEditForm({ name: detail.name, description: detail.description, permissionKeys: detail.permissionKeys });
    setView((current) => ({ ...current, modal: "edit", id: groupId, preview: current.preview === groupId ? null : current.preview }));
  };

  const togglePermission = (permissionKey: string) =>
    setEditForm((current) => ({
      ...current,
      permissionKeys: current.permissionKeys.includes(permissionKey)
        ? current.permissionKeys.filter((item) => item !== permissionKey)
        : [...current.permissionKeys, permissionKey]
    }));

  const createGroup = async () => {
    try {
      await requestJson("/api/admin/rbac/permission-groups", accessToken, onReauthenticate, { method: "POST", body: JSON.stringify(createForm) });
      setView((current) => ({ ...current, modal: null, id: null }));
      setCreateForm({ name: "", description: "" });
      setMessage("Permission group created.");
      await load();
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : "Unable to create permission group.");
    }
  };

  const saveGroup = async () => {
    if (!editingGroup) return;
    try {
      await requestJson(`/api/admin/rbac/permission-groups/${editingGroup.id}`, accessToken, onReauthenticate, {
        method: "PUT",
        body: JSON.stringify({ name: editForm.name, description: editForm.description } satisfies UpdatePermissionGroupRequest)
      });
      await requestJson(`/api/admin/rbac/permission-groups/${editingGroup.id}/permissions`, accessToken, onReauthenticate, {
        method: "PUT",
        body: JSON.stringify({ permissionKeys: editForm.permissionKeys })
      });
      setEditingGroup(null);
      setView((current) => ({ ...current, modal: null, id: null }));
      setMessage("Permission group updated.");
      await load();
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : "Unable to update permission group.");
    }
  };

  const deleteGroups = async (groupIds: string[]) => {
    if (groupIds.length === 0) return;
    try {
      if (groupIds.length === 1) {
        await requestJson(`/api/admin/rbac/permission-groups/${groupIds[0]}`, accessToken, onReauthenticate, { method: "DELETE" });
      } else {
        await requestJson("/api/admin/rbac/permission-groups/bulk-delete", accessToken, onReauthenticate, {
          method: "POST",
          body: JSON.stringify({ groupIds })
        });
      }
      setMessage(groupIds.length === 1 ? "Permission group deleted." : `${groupIds.length} permission groups deleted.`);
      setSelectedGroupIds([]);
      await load();
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : "Unable to delete selected permission groups.");
    }
  };

  const toggleSort = (field: string) => {
      setView((current) => {
      const [currentField, currentDirection] = current.query.sort.split(":") as [string, SortDirection];
      if (currentField === field) {
        return { ...current, query: { ...current.query, sort: `${field}:${currentDirection === "asc" ? "desc" : "asc"}`, page: 1 } };
      }
      return { ...current, query: { ...current.query, sort: `${field}:${field === "permissions" ? "desc" : "asc"}`, page: 1 } };
    });
  };

  const toggleGroupSelection = (groupId: string) => {
    setSelectedGroupIds((current) => current.includes(groupId) ? current.filter((item) => item !== groupId) : [...current, groupId]);
  };

  const toggleCurrentPageSelection = () => {
    setSelectedGroupIds((current) => {
      if (allPageSelected) {
        return current.filter((id) => !items.some((item) => item.id === id));
      }
      const next = new Set(current);
      items.forEach((item) => next.add(item.id));
      return [...next];
    });
  };

  return (
    <div className="app-page-stack">
      <CatalogToolbar
        title="Permission groups"
        description="Search and manage reusable permission groups."
        totalCount={totalCount}
        search={query.search}
        onSearchChange={(value) => setView((current) => ({ ...current, query: { ...current.query, search: value, page: 1 } }))}
        searchPlaceholder="Search groups or descriptions"
        sortValue={query.sort}
        onSortChange={(value) => setView((current) => ({ ...current, query: { ...current.query, sort: value, page: 1 } }))}
        sortOptions={[
          { value: "name:asc", label: "Name A-Z" },
          { value: "permissions:desc", label: "Most permissions first" }
        ]}
        pageSize={query.pageSize}
        onPageSizeChange={(value) => setView((current) => ({ ...current, query: { ...current.query, pageSize: value, page: 1 } }))}
        filter={query.filter}
        onFilterChange={(value) => setView((current) => ({ ...current, query: { ...current.query, filter: value, page: 1 } }))}
        filterOptions={[
          { value: "all", label: "All groups" },
          { value: "system", label: "System groups" },
          { value: "custom", label: "Custom groups" }
        ]}
        createLabel="Create group"
        onCreate={() => setView((current) => ({ ...current, modal: "create", id: null, preview: null }))}
      />
      {message ? <div className="app-notice-success">{message}</div> : null}
      {error ? <div className="app-notice-warn">{error}</div> : null}
      {selectedGroupIds.length > 0 ? (
        <div className="app-selection-summary">
          <div className="text-sm font-medium text-ink">{selectedGroupIds.length} selected</div>
          <div className="flex flex-wrap gap-3">
            <button type="button" className="app-button-secondary py-2.5" onClick={() => setSelectedGroupIds([])}>
              Clear selection
            </button>
            <button
              type="button"
              className="app-button-danger py-2.5"
              disabled={selectedDeletableGroupIds.length === 0}
              onClick={() => setConfirmDeleteGroupsOpen(true)}
            >
              Delete selected ({selectedDeletableGroupIds.length})
            </button>
          </div>
        </div>
      ) : null}

      <div className="app-data-table">
        <div className="overflow-x-auto">
          <table className="min-w-full text-sm">
            <thead className="app-table-head">
              <tr>
                <th className="px-5 py-4">
                  <input type="checkbox" checked={allPageSelected} aria-label="Select current page" onChange={toggleCurrentPageSelection} />
                </th>
                <th className="px-5 py-4">
                  <SortHeaderButton label="Group" active={activeSortField === "name"} direction={activeSortDirection} onClick={() => toggleSort("name")} />
                </th>
                <th className="px-5 py-4">Description</th>
                <th className="px-5 py-4">Type</th>
                <th className="px-5 py-4">
                  <SortHeaderButton label="Permissions" active={activeSortField === "permissions"} direction={activeSortDirection} onClick={() => toggleSort("permissions")} />
                </th>
                <th className="px-5 py-4">Created</th>
                <th className="px-5 py-4 text-right">Actions</th>
              </tr>
            </thead>
            <tbody>
              {isLoading ? (
                <TableStateRow colSpan={7} title="Loading permission groups" message="Refreshing the bundle catalog and permission definitions." />
              ) : error && totalCount === 0 ? (
                <TableStateRow colSpan={7} title="Unable to load permission groups" message={error} />
              ) : items.length === 0 ? (
                <TableStateRow colSpan={7} title="No permission groups match this view" message="Try a broader search or switch the current filter." />
              ) : items.map((item) => (
                <tr key={item.id} className="app-table-row border-t border-slate-200/80 align-top">
                  <td className="px-5 py-4">
                    <input type="checkbox" checked={selectedGroupIds.includes(item.id)} aria-label={`Select ${item.name}`} onChange={() => toggleGroupSelection(item.id)} />
                  </td>
                  <td className="px-5 py-4 font-semibold text-ink">{item.name}</td>
                  <td className="px-5 py-4 text-steel">{item.description}</td>
                  <td className="px-5 py-4">
                    <span className={item.isSystemGroup ? "app-chip-soft" : "app-chip"}>{item.isSystemGroup ? "System" : "Custom"}</span>
                  </td>
                  <td className="px-5 py-4 text-steel">{item.permissionCount}</td>
                  <td className="px-5 py-4 text-steel">{formatDate(item.createdAtUtc)}</td>
                  <td className="px-5 py-4">
                    <div className="flex justify-end gap-2">
                      <button type="button" className="app-button-secondary px-4 py-2.5" onClick={() => openEdit(item.id).catch((requestError) => setError(requestError instanceof Error ? requestError.message : "Unable to open group editor."))}>Edit</button>
                      <button
                        type="button"
                        className={item.isSystemGroup ? "app-button-secondary px-4 py-2.5 opacity-60" : "app-button-danger px-4 py-2.5"}
                        disabled={item.isSystemGroup}
                        onClick={() => {
                          setSelectedGroupIds([item.id]);
                          setConfirmDeleteGroupsOpen(true);
                        }}
                      >
                        {item.isSystemGroup ? "Protected" : "Delete"}
                      </button>
                      <button type="button" className="app-button-secondary px-4 py-2.5" onClick={() => setView((current) => ({ ...current, preview: item.id }))}>
                        Preview
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      <PaginationFooter
        page={query.page}
        pageCount={Math.max(1, Math.ceil(totalCount / query.pageSize))}
        totalCount={totalCount}
        pageSize={query.pageSize}
        onPageChange={(value) => setView((current) => ({ ...current, query: { ...current.query, page: value } }))}
      />

      <Modal
        open={modal === "create"}
        onClose={() => setView((current) => ({ ...current, modal: null, id: null }))}
        title="Create permission group"
        description="Create the bundle first, then attach permissions from the shared catalog."
      >
        <div className="space-y-4">
          <div className="app-form-grid">
            <input className="app-input" placeholder="Group name" value={createForm.name} onChange={(event) => setCreateForm((current) => ({ ...current, name: event.target.value }))} />
            <input className="app-input" placeholder="Description" value={createForm.description} onChange={(event) => setCreateForm((current) => ({ ...current, description: event.target.value }))} />
          </div>
          <div className="flex justify-end gap-3">
            <button type="button" className="app-button-secondary" onClick={() => setView((current) => ({ ...current, modal: null, id: null }))}>Cancel</button>
            <button type="button" className="app-button-primary" onClick={createGroup}>Create group</button>
          </div>
        </div>
      </Modal>

      <Modal
        open={modal === "edit" && editingGroup !== null}
        onClose={() => {
          setEditingGroup(null);
          setView((current) => ({ ...current, modal: null, id: null }));
        }}
        title="Edit permission group"
        description="Manage bundle metadata and permission assignments."
        width="xwide"
      >
        <div className="space-y-4">
          <div className="app-form-grid">
            <input className="app-input" value={editForm.name} onChange={(event) => setEditForm((current) => ({ ...current, name: event.target.value }))} />
            <input className="app-input" value={editForm.description} onChange={(event) => setEditForm((current) => ({ ...current, description: event.target.value }))} />
          </div>
          <label className="space-y-2">
            <span className="text-sm font-semibold text-ink">Filter permission catalog</span>
            <input className="app-input" value={permissionSearch} onChange={(event) => setPermissionSearch(event.target.value)} placeholder="Search permissions by key, name or category" />
          </label>
          <div className="app-permission-grid max-h-[460px] overflow-auto pr-1">
            {filteredPermissionOptions.map((permission) => (
              <label key={permission.id} className="app-permission-item flex items-start gap-3">
                <input type="checkbox" checked={editForm.permissionKeys.includes(permission.key)} onChange={() => togglePermission(permission.key)} />
                <span className="space-y-1">
                  <span className="block font-medium text-ink">{permission.name}</span>
                  <span className="block text-xs text-steel">{permission.key}</span>
                </span>
              </label>
            ))}
          </div>
          <div className="space-y-2">
            <div className="text-sm font-semibold text-ink">Permissions in group</div>
            <PermissionList permissions={sortPermissionKeys(editForm.permissionKeys)} />
          </div>
          <div className="flex justify-end gap-3">
            <button type="button" className="app-button-secondary" onClick={() => {
              setEditingGroup(null);
              setView((current) => ({ ...current, modal: null, id: null }));
            }}>Cancel</button>
            <button type="button" className="app-button-primary" onClick={saveGroup}>Save group</button>
          </div>
        </div>
      </Modal>
      <ConfirmActionModal
        open={confirmDeleteGroupsOpen}
        title="Delete selected permission groups"
        description={`This will permanently delete ${selectedDeletableGroupIds.length} custom group(s). System groups stay protected.`}
        confirmLabel="Delete selected"
        onClose={() => setConfirmDeleteGroupsOpen(false)}
        onConfirm={() => {
          setConfirmDeleteGroupsOpen(false);
          void deleteGroups(selectedDeletableGroupIds);
        }}
      />
      <PreviewDrawer
        open={previewGroup !== null}
        title={previewGroup?.name ?? ""}
        subtitle={previewGroup?.description}
        onClose={() => setView((current) => ({ ...current, preview: null }))}
      >
        {previewGroup ? (
          <div className="space-y-4">
            <div className="app-detail-grid">
              <div className="app-subtle-panel space-y-2">
                <div className="app-kicker">Type</div>
                <div className="font-semibold text-ink">{previewGroup.isSystemGroup ? "System group" : "Custom group"}</div>
              </div>
              <div className="app-subtle-panel space-y-2">
                <div className="app-kicker">Permissions</div>
                <div className="font-semibold text-ink">{previewGroup.permissionCount}</div>
              </div>
              <div className="app-subtle-panel space-y-2">
                <div className="app-kicker">Created</div>
                <div className="font-semibold text-ink">{formatDate(previewGroup.createdAtUtc)}</div>
              </div>
            </div>
            <div className="flex flex-wrap gap-3">
              <button type="button" className="app-button-primary" onClick={() => void openEdit(previewGroup.id)}>Edit group</button>
            </div>
          </div>
        ) : null}
      </PreviewDrawer>
    </div>
  );
}
