import { type ReactNode, useEffect, useState } from "react";
import { Link } from "react-router-dom";

export const permissionKeys = {
  auditView: "admin.audit.view",
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
  permissionGroupsDelete: "admin.permission_groups.delete"
} as const;

export function hasPermission(permissions: string[], permission: string) {
  return permissions.includes(permission);
}

export function sortPermissionKeys(permissions: string[]) {
  return [...permissions].sort((left, right) => left.localeCompare(right));
}

export function computeInheritedPermissions(effectivePermissions: string[], directPermissions: string[]) {
  const direct = new Set(directPermissions);
  return sortPermissionKeys(effectivePermissions.filter((permission) => !direct.has(permission)));
}

type PageProps = {
  accessToken: string;
  onReauthenticate: (fresh: boolean) => Promise<void>;
  permissions: string[];
};

type ApiError = { message?: string; details?: string[] };
type Paged<T> = { items: T[] };
type Permission = { key: string; category: string };
type AdminProfile = { userName: string; email: string; displayName: string; timeZone: string; accountState: string; roles: string[]; effectivePermissions: string[] };
type UserItem = { userId: string; userName: string; email: string; displayName: string; isActive: boolean; roles: string[] };
type UserDetail = UserItem & { effectivePermissions: string[] };
type RoleItem = { roleId: string; name: string; description: string; isSystemRole: boolean; priority: number; userCount: number };
type RoleDetail = RoleItem & { permissionGroupIds: string[]; directPermissionKeys: string[]; effectivePermissionKeys: string[] };
type GroupItem = { id: string; name: string; description: string; isSystemGroup: boolean };
type GroupDetail = GroupItem & { permissionKeys: string[] };

function Card({ title, eyebrow, children }: { title: string; eyebrow?: string; children: ReactNode }) {
  return (
    <section className="app-card">
      <div className="app-card-header">
        {eyebrow ? <p className="app-kicker">{eyebrow}</p> : null}
        <h2 className="mt-2 text-[1.7rem] font-bold tracking-tight text-ink">{title}</h2>
      </div>
      <div className="app-card-body">{children}</div>
    </section>
  );
}

function Pill({ children, tone = "default" }: { children: string; tone?: "default" | "soft" }) {
  return <span className={tone === "soft" ? "app-chip-soft" : "app-chip"}>{children}</span>;
}

function Notice({ tone, children }: { tone: "info" | "success" | "warn"; children: ReactNode }) {
  return <div className={tone === "info" ? "app-notice-info" : tone === "success" ? "app-notice-success" : "app-notice-warn"}>{children}</div>;
}

function Workbench({
  catalog,
  editor,
  inspector
}: {
  catalog: ReactNode;
  editor: ReactNode;
  inspector: ReactNode;
}) {
  return <div className="grid gap-6 xl:grid-cols-[0.78fr,1fr,0.88fr]">{catalog}{editor}{inspector}</div>;
}

function SelectionButton({
  active,
  title,
  description,
  meta,
  onClick
}: {
  active: boolean;
  title: string;
  description: string;
  meta?: string;
  onClick: () => void;
}) {
  return (
    <button type="button" onClick={onClick} className={`app-selection-card ${active ? "app-selection-card-active" : ""}`}>
      <p className="text-base font-semibold text-ink">{title}</p>
      <p className="mt-1 text-sm leading-6 text-steel">{description}</p>
      {meta ? <p className="mt-3 text-xs uppercase tracking-[0.18em] text-steel/60">{meta}</p> : null}
    </button>
  );
}

function AccessDeniedPanel({ permission }: { permission: string }) {
  return (
    <Card title="Access denied" eyebrow="Authorization">
      <div className="app-section-stack">
        <Notice tone="warn">The current admin session does not have the permission required to open this page.</Notice>
        <div className="flex flex-wrap gap-2">
          <Pill>{permission}</Pill>
        </div>
        <p className="text-sm leading-7 text-steel">
          Ask a SuperAdmin to grant the permission, or continue in a section already available in the current session.
        </p>
        <div className="flex flex-wrap gap-3">
          <Link to="/" className="app-button-primary">Go to dashboard</Link>
          <Link to="/profile" className="app-button-secondary">Open profile</Link>
        </div>
      </div>
    </Card>
  );
}

function PermissionList({ title, permissions, empty }: { title: string; permissions: string[]; empty: string }) {
  return (
    <div className="app-section-stack">
      <div className="flex items-center justify-between gap-3">
        <h3 className="text-sm font-semibold text-ink">{title}</h3>
        <span className="text-xs uppercase tracking-[0.18em] text-steel/55">{permissions.length}</span>
      </div>
      {permissions.length > 0 ? (
        <div className="flex flex-wrap gap-2">
          {sortPermissionKeys(permissions).map((permission) => <Pill key={`${title}-${permission}`}>{permission}</Pill>)}
        </div>
      ) : (
        <div className="app-empty-state">{empty}</div>
      )}
    </div>
  );
}

function PermissionBreakdown({ detail, groups }: { detail: RoleDetail; groups: GroupItem[] }) {
  const inherited = computeInheritedPermissions(detail.effectivePermissionKeys, detail.directPermissionKeys);
  const groupNames = groups.filter((group) => detail.permissionGroupIds.includes(group.id)).map((group) => group.name).sort((left, right) => left.localeCompare(right));

  return (
    <div className="app-section-stack">
      <div className="app-subtle-panel">
        <p className="text-xs font-semibold uppercase tracking-[0.2em] text-steel/55">Impact</p>
        <p className="mt-3 text-sm leading-7 text-steel">
          Review what this role gets directly versus what it inherits through permission groups before saving privileged changes.
        </p>
      </div>
      <PermissionList title="Assigned groups" permissions={groupNames} empty="No permission groups are attached to this role." />
      <PermissionList title="Direct permissions" permissions={detail.directPermissionKeys} empty="No direct permissions are assigned." />
      <PermissionList title="Inherited permissions" permissions={inherited} empty="No inherited permissions are currently resolved." />
      <PermissionList title="Effective permissions" permissions={detail.effectivePermissionKeys} empty="The role does not resolve any effective permissions." />
    </div>
  );
}

async function request<T>(path: string, accessToken: string, onReauthenticate: (fresh: boolean) => Promise<void>, init?: RequestInit) {
  const response = await fetch(path, { ...init, headers: { Authorization: `Bearer ${accessToken}`, "Content-Type": "application/json", ...(init?.headers ?? {}) } });
  if (response.status === 401) { await onReauthenticate(true); throw new Error("Admin authentication is required."); }
  if (!response.ok) {
    const error = (await response.json().catch(() => ({}))) as ApiError;
    throw new Error(error.details?.join(" ") ?? error.message ?? `Request failed (${response.status}).`);
  }
  if (response.status === 204) return null as T;
  return response.json() as Promise<T>;
}

export function PermissionGate({ permissions, permission, children }: { permissions: string[]; permission: string; children: ReactNode }) {
  if (!hasPermission(permissions, permission)) return <AccessDeniedPanel permission={permission} />;
  return <>{children}</>;
}

export function ProfilePage({ accessToken, onReauthenticate, permissions }: PageProps) {
  const [profile, setProfile] = useState<AdminProfile | null>(null);
  const [displayName, setDisplayName] = useState("");
  const [timeZone, setTimeZone] = useState("UTC");
  const [error, setError] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);

  useEffect(() => {
    request<AdminProfile>("/api/admin/rbac/profile", accessToken, onReauthenticate)
      .then((value) => { setProfile(value); setDisplayName(value.displayName); setTimeZone(value.timeZone); })
      .catch((value) => setError(value instanceof Error ? value.message : "Unable to load profile."));
  }, [accessToken, onReauthenticate]);

  const save = async () => {
    try {
      setError(null);
      await request("/api/admin/rbac/profile", accessToken, onReauthenticate, { method: "PUT", body: JSON.stringify({ displayName, timeZone }) });
      setMessage("Profile updated.");
    } catch (value) {
      setError(value instanceof Error ? value.message : "Profile update failed.");
    }
  };

  return (
    <Workbench
      catalog={<Card title="Current admin" eyebrow="Identity">{profile ? <div className="app-section-stack text-sm text-steel"><div className="app-subtle-panel"><p className="text-xl font-bold text-ink">{profile.displayName}</p><p className="mt-1">{profile.email}</p></div><p><span className="font-semibold text-ink">User:</span> {profile.userName}</p><p><span className="font-semibold text-ink">State:</span> {profile.accountState}</p><div className="flex flex-wrap gap-2">{profile.roles.map((role) => <Pill key={role} tone="soft">{role}</Pill>)}</div></div> : <div className="app-empty-state">Loading profile…</div>}</Card>}
      editor={<Card title="Profile settings" eyebrow="Self-service"><div className="app-section-stack">{error ? <Notice tone="warn">{error}</Notice> : null}{message ? <Notice tone="success">{message}</Notice> : null}<div className="app-form-grid"><input className="app-input" value={displayName} onChange={(event) => setDisplayName(event.target.value)} placeholder="Display name" /><input className="app-input" value={timeZone} onChange={(event) => setTimeZone(event.target.value)} placeholder="Time zone" /></div><button type="button" onClick={() => void save()} disabled={!hasPermission(permissions, permissionKeys.profileEdit)} className="app-button-primary disabled:opacity-50">Save profile</button></div></Card>}
      inspector={<Card title="Effective session permissions" eyebrow="Authorization"><PermissionList title="Granted permissions" permissions={profile?.effectivePermissions ?? []} empty="No effective permissions were returned for the current session." /></Card>}
    />
  );
}

export function AdminUsersPage({ accessToken, onReauthenticate, permissions }: PageProps) {
  const [users, setUsers] = useState<UserItem[]>([]);
  const [roles, setRoles] = useState<RoleItem[]>([]);
  const [detail, setDetail] = useState<UserDetail | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [createModel, setCreateModel] = useState({ userName: "", email: "", password: "", displayName: "", roles: [] as string[] });
  const [editModel, setEditModel] = useState({ displayName: "", email: "", roles: [] as string[], resetPassword: "" });

  const load = async (userId?: string) => {
    const [loadedUsers, loadedRoles] = await Promise.all([request<Paged<UserItem>>("/api/admin/rbac/users?page=1&pageSize=50", accessToken, onReauthenticate), request<RoleItem[]>("/api/admin/rbac/roles", accessToken, onReauthenticate)]);
    setUsers(loadedUsers.items);
    setRoles(loadedRoles);
    const selected = userId ?? detail?.userId ?? loadedUsers.items[0]?.userId;
    if (selected) {
      const loadedDetail = await request<UserDetail>(`/api/admin/rbac/users/${selected}`, accessToken, onReauthenticate);
      setDetail(loadedDetail);
      setEditModel({ displayName: loadedDetail.displayName, email: loadedDetail.email, roles: loadedDetail.roles, resetPassword: "" });
    }
  };

  useEffect(() => {
    load().catch((value) => setError(value instanceof Error ? value.message : "Unable to load admin users."));
  }, [accessToken, onReauthenticate]);

  const toggleRole = (rolesToChange: string[], role: string) => rolesToChange.includes(role) ? rolesToChange.filter((item) => item !== role) : [...rolesToChange, role];
  const run = async (action: () => Promise<void>, success: string) => {
    try { setError(null); await action(); setMessage(success); await load(detail?.userId); }
    catch (value) { setError(value instanceof Error ? value.message : success); }
  };

  return (
    <Workbench
      catalog={<Card title="Admin users" eyebrow="Catalog"><div className="app-section-stack">{message ? <Notice tone="success">{message}</Notice> : null}{error ? <Notice tone="warn">{error}</Notice> : null}{users.length > 0 ? users.map((user) => <SelectionButton key={user.userId} active={detail?.userId === user.userId} title={user.displayName} description={`${user.userName} · ${user.email}`} meta={user.roles.join(", ")} onClick={() => void load(user.userId)} />) : <div className="app-empty-state">No admin users are currently provisioned.</div>}</div></Card>}
      editor={<Card title="Selected user" eyebrow="Editor">{detail ? <div className="app-section-stack"><div className="app-form-grid"><input className="app-input" value={editModel.displayName} onChange={(event) => setEditModel({ ...editModel, displayName: event.target.value })} /><input className="app-input" value={editModel.email} onChange={(event) => setEditModel({ ...editModel, email: event.target.value })} /></div><div className="flex flex-wrap gap-2">{roles.map((role) => <label key={role.roleId} className="app-checkbox-chip"><input type="checkbox" checked={editModel.roles.includes(role.name)} onChange={() => setEditModel({ ...editModel, roles: toggleRole(editModel.roles, role.name) })} />{role.name}</label>)}</div><div className="flex flex-wrap gap-3"><button type="button" onClick={() => void run(async () => { await request(`/api/admin/rbac/users/${detail.userId}`, accessToken, onReauthenticate, { method: "PUT", body: JSON.stringify({ displayName: editModel.displayName, email: editModel.email }) }); await request(`/api/admin/rbac/users/${detail.userId}/roles`, accessToken, onReauthenticate, { method: "PUT", body: JSON.stringify({ roles: editModel.roles }) }); }, "Admin user updated.")} disabled={!hasPermission(permissions, permissionKeys.usersEdit)} className="app-button-primary disabled:opacity-50">Save user</button><button type="button" onClick={() => void run(async () => { await request(`/api/admin/rbac/users/${detail.userId}/activate`, accessToken, onReauthenticate, { method: "POST", body: "{}" }); }, "User activated.")} disabled={!hasPermission(permissions, permissionKeys.usersActivate)} className="app-button-secondary disabled:opacity-50">Activate</button><button type="button" onClick={() => void run(async () => { await request(`/api/admin/rbac/users/${detail.userId}/deactivate`, accessToken, onReauthenticate, { method: "POST", body: "{}" }); }, "User deactivated.")} disabled={!hasPermission(permissions, permissionKeys.usersDeactivate)} className="app-button-secondary disabled:opacity-50">Deactivate</button></div><div className="flex gap-3"><input className="app-input" placeholder="New password" value={editModel.resetPassword} onChange={(event) => setEditModel({ ...editModel, resetPassword: event.target.value })} /><button type="button" onClick={() => void run(async () => { await request(`/api/admin/rbac/users/${detail.userId}/reset-password`, accessToken, onReauthenticate, { method: "POST", body: JSON.stringify({ newPassword: editModel.resetPassword }) }); setEditModel((current) => ({ ...current, resetPassword: "" })); }, "Password reset completed.")} disabled={!hasPermission(permissions, permissionKeys.usersResetPassword)} className="app-button-danger disabled:opacity-50">Reset</button></div></div> : <div className="app-empty-state">Select an admin user to edit roles, account state and password workflow.</div>}</Card>}
      inspector={<div className="app-page-stack"><Card title="Provision new admin" eyebrow="Create"><div className="app-section-stack"><div className="app-form-grid"><input className="app-input" placeholder="User name" value={createModel.userName} onChange={(event) => setCreateModel({ ...createModel, userName: event.target.value })} /><input className="app-input" placeholder="Display name" value={createModel.displayName} onChange={(event) => setCreateModel({ ...createModel, displayName: event.target.value })} /><input className="app-input" placeholder="Email" value={createModel.email} onChange={(event) => setCreateModel({ ...createModel, email: event.target.value })} /><input className="app-input" placeholder="Temporary password" value={createModel.password} onChange={(event) => setCreateModel({ ...createModel, password: event.target.value })} /></div><div className="flex flex-wrap gap-2">{roles.map((role) => <label key={role.roleId} className="app-checkbox-chip"><input type="checkbox" checked={createModel.roles.includes(role.name)} onChange={() => setCreateModel({ ...createModel, roles: toggleRole(createModel.roles, role.name) })} />{role.name}</label>)}</div><button type="button" onClick={() => void run(async () => { await request("/api/admin/rbac/users", accessToken, onReauthenticate, { method: "POST", body: JSON.stringify(createModel) }); setCreateModel({ userName: "", email: "", password: "", displayName: "", roles: [] }); }, "Admin user created.")} disabled={!hasPermission(permissions, permissionKeys.usersCreate)} className="app-button-primary disabled:opacity-50">Create admin user</button></div></Card><Card title="Effective permissions" eyebrow="Impact"><PermissionList title="Selected user permissions" permissions={detail?.effectivePermissions ?? []} empty="Select an admin user to review effective permissions." /></Card></div>}
    />
  );
}

export function RolesPage({ accessToken, onReauthenticate, permissions }: PageProps) {
  const [roles, setRoles] = useState<RoleItem[]>([]);
  const [groups, setGroups] = useState<GroupItem[]>([]);
  const [catalog, setCatalog] = useState<Permission[]>([]);
  const [detail, setDetail] = useState<RoleDetail | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [createModel, setCreateModel] = useState({ name: "", description: "", priority: 100 });
  const [editModel, setEditModel] = useState({ description: "", priority: 100, groups: [] as string[], permissions: [] as string[] });

  const load = async (roleId?: string) => {
    const [loadedRoles, loadedGroups, loadedCatalog] = await Promise.all([request<RoleItem[]>("/api/admin/rbac/roles", accessToken, onReauthenticate), request<GroupItem[]>("/api/admin/rbac/permission-groups", accessToken, onReauthenticate), request<Permission[]>("/api/admin/rbac/permissions", accessToken, onReauthenticate)]);
    setRoles(loadedRoles); setGroups(loadedGroups); setCatalog(loadedCatalog);
    const selected = roleId ?? detail?.roleId ?? loadedRoles[0]?.roleId;
    if (selected) {
      const loadedDetail = await request<RoleDetail>(`/api/admin/rbac/roles/${selected}`, accessToken, onReauthenticate);
      setDetail(loadedDetail);
      setEditModel({ description: loadedDetail.description, priority: loadedDetail.priority, groups: loadedDetail.permissionGroupIds, permissions: loadedDetail.directPermissionKeys });
    }
  };

  useEffect(() => { load().catch((value) => setError(value instanceof Error ? value.message : "Unable to load roles.")); }, [accessToken, onReauthenticate]);
  const flip = (items: string[], value: string) => items.includes(value) ? items.filter((item) => item !== value) : [...items, value];
  const run = async (action: () => Promise<void>, success: string) => {
    try { setError(null); await action(); setMessage(success); await load(detail?.roleId); }
    catch (value) { setError(value instanceof Error ? value.message : success); }
  };

  return (
    <Workbench
      catalog={<Card title="Roles" eyebrow="Catalog"><div className="app-section-stack">{message ? <Notice tone="success">{message}</Notice> : null}{error ? <Notice tone="warn">{error}</Notice> : null}{roles.map((role) => <SelectionButton key={role.roleId} active={detail?.roleId === role.roleId} title={role.name} description={role.description} meta={`${role.userCount} users · priority ${role.priority}`} onClick={() => void load(role.roleId)} />)}</div></Card>}
      editor={<Card title="Role editor" eyebrow="Permissions model">{detail ? <div className="app-section-stack"><div className="app-form-grid"><input className="app-input-muted" value={detail.name} disabled /><input className="app-input" type="number" value={editModel.priority} onChange={(event) => setEditModel({ ...editModel, priority: Number(event.target.value) })} /></div><textarea className="app-input min-h-28" value={editModel.description} onChange={(event) => setEditModel({ ...editModel, description: event.target.value })} /><div className="app-section-stack"><h3 className="text-sm font-semibold text-ink">Permission groups</h3><div className="flex flex-wrap gap-2">{groups.map((group) => <label key={group.id} className="app-checkbox-chip"><input type="checkbox" checked={editModel.groups.includes(group.id)} onChange={() => setEditModel({ ...editModel, groups: flip(editModel.groups, group.id) })} />{group.name}</label>)}</div></div><div className="app-section-stack"><h3 className="text-sm font-semibold text-ink">Direct permissions</h3><div className="app-permission-grid">{catalog.map((permission) => <label key={permission.key} className="app-permission-item"><input type="checkbox" checked={editModel.permissions.includes(permission.key)} onChange={() => setEditModel({ ...editModel, permissions: flip(editModel.permissions, permission.key) })} className="mr-2" />{permission.key}</label>)}</div></div><div className="flex flex-wrap gap-3"><button type="button" onClick={() => void run(async () => { await request(`/api/admin/rbac/roles/${detail.roleId}`, accessToken, onReauthenticate, { method: "PUT", body: JSON.stringify({ description: editModel.description, priority: editModel.priority }) }); await request(`/api/admin/rbac/roles/${detail.roleId}/permission-groups`, accessToken, onReauthenticate, { method: "PUT", body: JSON.stringify({ permissionGroupIds: editModel.groups }) }); await request(`/api/admin/rbac/roles/${detail.roleId}/permissions`, accessToken, onReauthenticate, { method: "PUT", body: JSON.stringify({ permissionKeys: editModel.permissions }) }); }, "Role updated.")} disabled={!hasPermission(permissions, permissionKeys.rolesEdit)} className="app-button-primary disabled:opacity-50">Save role</button><button type="button" onClick={() => void run(async () => { await request(`/api/admin/rbac/roles/${detail.roleId}`, accessToken, onReauthenticate, { method: "DELETE" }); setDetail(null); }, "Role deleted.")} disabled={detail.isSystemRole || !hasPermission(permissions, permissionKeys.rolesDelete)} className="app-button-danger disabled:opacity-50">Delete role</button></div></div> : <div className="app-empty-state">Select a role to edit description, grouping and direct permissions.</div>}</Card>}
      inspector={<div className="app-page-stack"><Card title="Create role" eyebrow="Model access"><div className="app-section-stack"><div className="app-form-grid"><input className="app-input" placeholder="Role name" value={createModel.name} onChange={(event) => setCreateModel({ ...createModel, name: event.target.value })} /><input className="app-input" placeholder="Description" value={createModel.description} onChange={(event) => setCreateModel({ ...createModel, description: event.target.value })} /><input className="app-input" type="number" value={createModel.priority} onChange={(event) => setCreateModel({ ...createModel, priority: Number(event.target.value) })} /></div><button type="button" onClick={() => void run(async () => { await request("/api/admin/rbac/roles", accessToken, onReauthenticate, { method: "POST", body: JSON.stringify(createModel) }); setCreateModel({ name: "", description: "", priority: 100 }); }, "Role created.")} disabled={!hasPermission(permissions, permissionKeys.rolesCreate)} className="app-button-primary disabled:opacity-50">Create role</button></div></Card><Card title="Permission impact" eyebrow="Inspector">{detail ? <PermissionBreakdown detail={detail} groups={groups} /> : <div className="app-empty-state">Select a role to review direct, inherited and effective permissions.</div>}</Card></div>}
    />
  );
}

export function PermissionGroupsPage({ accessToken, onReauthenticate, permissions }: PageProps) {
  const [groups, setGroups] = useState<GroupItem[]>([]);
  const [catalog, setCatalog] = useState<Permission[]>([]);
  const [detail, setDetail] = useState<GroupDetail | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [createModel, setCreateModel] = useState({ name: "", description: "" });
  const [editModel, setEditModel] = useState({ name: "", description: "", permissions: [] as string[] });

  const load = async (groupId?: string) => {
    const [loadedGroups, loadedCatalog] = await Promise.all([request<GroupItem[]>("/api/admin/rbac/permission-groups", accessToken, onReauthenticate), request<Permission[]>("/api/admin/rbac/permissions", accessToken, onReauthenticate)]);
    setGroups(loadedGroups); setCatalog(loadedCatalog);
    const selected = groupId ?? detail?.id ?? loadedGroups[0]?.id;
    if (selected) {
      const loadedDetail = await request<GroupDetail>(`/api/admin/rbac/permission-groups/${selected}`, accessToken, onReauthenticate);
      setDetail(loadedDetail);
      setEditModel({ name: loadedDetail.name, description: loadedDetail.description, permissions: loadedDetail.permissionKeys });
    }
  };

  useEffect(() => { load().catch((value) => setError(value instanceof Error ? value.message : "Unable to load permission groups.")); }, [accessToken, onReauthenticate]);
  const flip = (items: string[], value: string) => items.includes(value) ? items.filter((item) => item !== value) : [...items, value];
  const run = async (action: () => Promise<void>, success: string) => {
    try { setError(null); await action(); setMessage(success); await load(detail?.id); }
    catch (value) { setError(value instanceof Error ? value.message : success); }
  };

  return (
    <Workbench
      catalog={<Card title="Permission groups" eyebrow="Catalog"><div className="app-section-stack">{message ? <Notice tone="success">{message}</Notice> : null}{error ? <Notice tone="warn">{error}</Notice> : null}{groups.map((group) => <SelectionButton key={group.id} active={detail?.id === group.id} title={group.name} description={group.description} meta={group.isSystemGroup ? "system group" : "custom group"} onClick={() => void load(group.id)} />)}</div></Card>}
      editor={<Card title="Permission group editor" eyebrow="Assignments">{detail ? <div className="app-section-stack"><div className="app-form-grid"><input className={detail.isSystemGroup ? "app-input-muted" : "app-input"} value={editModel.name} onChange={(event) => setEditModel({ ...editModel, name: event.target.value })} disabled={detail.isSystemGroup} /><input className="app-input" value={editModel.description} onChange={(event) => setEditModel({ ...editModel, description: event.target.value })} /></div><div className="app-permission-grid">{catalog.map((permission) => <label key={permission.key} className="app-permission-item"><input type="checkbox" checked={editModel.permissions.includes(permission.key)} onChange={() => setEditModel({ ...editModel, permissions: flip(editModel.permissions, permission.key) })} className="mr-2" />{permission.key}</label>)}</div><div className="flex flex-wrap gap-3"><button type="button" onClick={() => void run(async () => { await request(`/api/admin/rbac/permission-groups/${detail.id}`, accessToken, onReauthenticate, { method: "PUT", body: JSON.stringify({ name: editModel.name, description: editModel.description }) }); await request(`/api/admin/rbac/permission-groups/${detail.id}/permissions`, accessToken, onReauthenticate, { method: "PUT", body: JSON.stringify({ permissionKeys: editModel.permissions }) }); }, "Permission group updated.")} disabled={!hasPermission(permissions, permissionKeys.permissionGroupsEdit)} className="app-button-primary disabled:opacity-50">Save group</button><button type="button" onClick={() => void run(async () => { await request(`/api/admin/rbac/permission-groups/${detail.id}`, accessToken, onReauthenticate, { method: "DELETE" }); setDetail(null); }, "Permission group deleted.")} disabled={detail.isSystemGroup || !hasPermission(permissions, permissionKeys.permissionGroupsDelete)} className="app-button-danger disabled:opacity-50">Delete group</button></div></div> : <div className="app-empty-state">Select a permission group to manage its reusable permission bundle.</div>}</Card>}
      inspector={<div className="app-page-stack"><Card title="Create permission group" eyebrow="New bundle"><div className="app-section-stack"><div className="app-form-grid"><input className="app-input" placeholder="Group name" value={createModel.name} onChange={(event) => setCreateModel({ ...createModel, name: event.target.value })} /><input className="app-input" placeholder="Description" value={createModel.description} onChange={(event) => setCreateModel({ ...createModel, description: event.target.value })} /></div><button type="button" onClick={() => void run(async () => { await request("/api/admin/rbac/permission-groups", accessToken, onReauthenticate, { method: "POST", body: JSON.stringify(createModel) }); setCreateModel({ name: "", description: "" }); }, "Permission group created.")} disabled={!hasPermission(permissions, permissionKeys.permissionGroupsCreate)} className="app-button-primary disabled:opacity-50">Create group</button></div></Card><Card title="Selected bundle" eyebrow="Inspector"><PermissionList title="Permissions in group" permissions={detail?.permissionKeys ?? []} empty="Select a permission group to review the bundled permissions." /></Card></div>}
    />
  );
}
