import { useEffect, useState } from "react";
import { Link } from "react-router-dom";

export const permissionKeys = {
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
  const directSet = new Set(directPermissions);
  return sortPermissionKeys(effectivePermissions.filter((permission) => !directSet.has(permission)));
}

type PageProps = {
  accessToken: string;
  onReauthenticate: (fresh: boolean) => Promise<void>;
  permissions: string[];
};

type ApiError = { message?: string; details?: string[] };

type AdminProfile = { userName: string; email: string; displayName: string; timeZone: string; accountState: string; roles: string[]; effectivePermissions: string[] };
type Paged<T> = { items: T[] };
type UserItem = { userId: string; userName: string; email: string; displayName: string; isActive: boolean; roles: string[] };
type UserDetail = UserItem & { effectivePermissions: string[] };
type RoleItem = { roleId: string; name: string; description: string; isSystemRole: boolean; priority: number; userCount: number };
type RoleDetail = RoleItem & { permissionGroupIds: string[]; directPermissionKeys: string[]; effectivePermissionKeys: string[] };
type GroupItem = { id: string; name: string; description: string; isSystemGroup: boolean };
type GroupDetail = GroupItem & { permissionKeys: string[] };
type Permission = { key: string; category: string };

function Card({ title, eyebrow, children }: { title: string; eyebrow?: string; children: React.ReactNode }) {
  return <section className="app-card"><div className="app-card-header">{eyebrow ? <p className="app-kicker">{eyebrow}</p> : null}<h2 className="mt-2 text-xl font-semibold text-ink">{title}</h2></div><div className="app-card-body">{children}</div></section>;
}

function Pill({ children }: { children: string }) {
  return <span className="inline-flex items-center rounded-full border border-slate-300 bg-slate-100 px-3 py-1 text-[11px] font-semibold uppercase tracking-[0.18em] text-steel">{children}</span>;
}

function AccessDeniedPanel({ permission }: { permission: string }) {
  return (
    <Card title="Access denied" eyebrow="Authorization">
      <div className="space-y-4">
        <p className="text-sm text-ember">
          The current admin session does not have the permission required to open this page.
        </p>
        <div className="flex flex-wrap gap-2">
          <Pill>{permission}</Pill>
        </div>
        <p className="text-sm text-steel">
          Ask a SuperAdmin to grant the permission, or return to a section already available in the current session.
        </p>
        <div className="flex flex-wrap gap-3">
          <Link to="/" className="app-button-primary">Go to dashboard</Link>
          <Link to="/profile" className="app-button-secondary">Open profile</Link>
        </div>
      </div>
    </Card>
  );
}

function PermissionListSection({ title, permissions, emptyMessage }: { title: string; permissions: string[]; emptyMessage: string }) {
  return (
    <div className="space-y-3">
      <div className="flex items-center justify-between gap-3">
        <h3 className="text-sm font-semibold text-ink">{title}</h3>
        <span className="text-xs uppercase tracking-[0.16em] text-steel/70">{permissions.length}</span>
      </div>
      {permissions.length > 0 ? (
        <div className="flex flex-wrap gap-2">
          {sortPermissionKeys(permissions).map((permission) => <Pill key={`${title}-${permission}`}>{permission}</Pill>)}
        </div>
      ) : (
        <p className="text-sm text-steel">{emptyMessage}</p>
      )}
    </div>
  );
}

function RolePermissionBreakdown({ detail, groups }: { detail: RoleDetail; groups: GroupItem[] }) {
  const inheritedPermissions = computeInheritedPermissions(detail.effectivePermissionKeys, detail.directPermissionKeys);
  const assignedGroupNames = groups
    .filter((group) => detail.permissionGroupIds.includes(group.id))
    .map((group) => group.name)
    .sort((left, right) => left.localeCompare(right));

  return (
    <div className="space-y-5 rounded-3xl border border-slate-200 bg-slate-50 px-4 py-5">
      <div>
        <p className="text-xs font-semibold uppercase tracking-[0.18em] text-steel/70">Permission breakdown</p>
        <p className="mt-2 text-sm text-steel">
          Separate direct grants from permissions inherited through permission groups so role review is faster and safer.
        </p>
      </div>
      <div className="space-y-3">
        <h3 className="text-sm font-semibold text-ink">Assigned permission groups</h3>
        {assignedGroupNames.length > 0 ? (
          <div className="flex flex-wrap gap-2">
            {assignedGroupNames.map((groupName) => <Pill key={groupName}>{groupName}</Pill>)}
          </div>
        ) : (
          <p className="text-sm text-steel">No permission groups are assigned to this role.</p>
        )}
      </div>
      <PermissionListSection title="Direct permissions" permissions={detail.directPermissionKeys} emptyMessage="No direct permissions are assigned." />
      <PermissionListSection title="Inherited permissions" permissions={inheritedPermissions} emptyMessage="No inherited permissions are currently resolved through permission groups." />
      <PermissionListSection title="Effective permissions" permissions={detail.effectivePermissionKeys} emptyMessage="The role does not currently resolve any effective permissions." />
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

export function PermissionGate({ permissions, permission, children }: { permissions: string[]; permission: string; children: React.ReactNode }) {
  if (!hasPermission(permissions, permission)) return <AccessDeniedPanel permission={permission} />;
  return <>{children}</>;
}

export function ProfilePage({ accessToken, onReauthenticate, permissions }: PageProps) {
  const [profile, setProfile] = useState<AdminProfile | null>(null);
  const [displayName, setDisplayName] = useState("");
  const [timeZone, setTimeZone] = useState("UTC");
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => { request<AdminProfile>("/api/admin/rbac/profile", accessToken, onReauthenticate).then((value) => { setProfile(value); setDisplayName(value.displayName); setTimeZone(value.timeZone); }).catch((value) => setError(value instanceof Error ? value.message : "Unable to load profile.")); }, [accessToken, onReauthenticate]);
  const save = async () => { try { setError(null); await request("/api/admin/rbac/profile", accessToken, onReauthenticate, { method: "PUT", body: JSON.stringify({ displayName, timeZone }) }); setMessage("Profile updated."); } catch (value) { setError(value instanceof Error ? value.message : "Profile update failed."); } };
  return <div className="grid gap-6 xl:grid-cols-2"><Card title="Admin profile" eyebrow="Self-service">{error ? <p className="rounded-2xl bg-ember/10 px-4 py-3 text-sm text-ember">{error}</p> : null}{message ? <p className="rounded-2xl bg-moss/10 px-4 py-3 text-sm text-moss">{message}</p> : null}{profile ? <div className="space-y-2 text-sm text-steel"><p><span className="font-semibold text-ink">User:</span> {profile.userName}</p><p><span className="font-semibold text-ink">Email:</span> {profile.email}</p><p><span className="font-semibold text-ink">State:</span> {profile.accountState}</p><p><span className="font-semibold text-ink">Roles:</span> {profile.roles.join(", ")}</p></div> : <p className="text-sm text-steel">Loading profile...</p>}</Card><Card title="Profile settings" eyebrow="Permissions"><div className="space-y-4"><input className="w-full rounded-2xl border border-slate-200 px-4 py-3" value={displayName} onChange={(event) => setDisplayName(event.target.value)} /><input className="w-full rounded-2xl border border-slate-200 px-4 py-3" value={timeZone} onChange={(event) => setTimeZone(event.target.value)} /><button type="button" onClick={() => void save()} disabled={!hasPermission(permissions, permissionKeys.profileEdit)} className="app-button-primary disabled:opacity-50">Save profile</button></div><div className="mt-5 flex flex-wrap gap-2">{profile?.effectivePermissions.map((permission) => <Pill key={permission}>{permission}</Pill>)}</div></Card></div>;
}

export function AdminUsersPage({ accessToken, onReauthenticate, permissions }: PageProps) {
  const [users, setUsers] = useState<UserItem[]>([]);
  const [roles, setRoles] = useState<RoleItem[]>([]);
  const [detail, setDetail] = useState<UserDetail | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [createModel, setCreateModel] = useState({ userName: "", email: "", password: "", displayName: "", roles: [] as string[] });
  const [editModel, setEditModel] = useState({ displayName: "", email: "", roles: [] as string[], resetPassword: "" });
  const load = async (userId?: string) => { const [loadedUsers, loadedRoles] = await Promise.all([request<Paged<UserItem>>("/api/admin/rbac/users?page=1&pageSize=50", accessToken, onReauthenticate), request<RoleItem[]>("/api/admin/rbac/roles", accessToken, onReauthenticate)]); setUsers(loadedUsers.items); setRoles(loadedRoles); const selected = userId ?? detail?.userId ?? loadedUsers.items[0]?.userId; if (selected) { const loadedDetail = await request<UserDetail>(`/api/admin/rbac/users/${selected}`, accessToken, onReauthenticate); setDetail(loadedDetail); setEditModel({ displayName: loadedDetail.displayName, email: loadedDetail.email, roles: loadedDetail.roles, resetPassword: "" }); } };
  useEffect(() => { load().catch((value) => setError(value instanceof Error ? value.message : "Unable to load admin users.")); }, [accessToken, onReauthenticate]);
  const toggle = (setState: React.Dispatch<React.SetStateAction<string[]>>, value: string) => setState((current) => current.includes(value) ? current.filter((item) => item !== value) : [...current, value]);
  const run = async (action: () => Promise<void>, success: string) => { try { setError(null); await action(); setMessage(success); await load(detail?.userId); } catch (value) { setError(value instanceof Error ? value.message : success); } };
  return <div className="grid gap-6 xl:grid-cols-[0.9fr,1.1fr]"><Card title="Admin users" eyebrow="RBAC">{message ? <p className="mb-4 rounded-2xl bg-moss/10 px-4 py-3 text-sm text-moss">{message}</p> : null}{error ? <p className="mb-4 rounded-2xl bg-ember/10 px-4 py-3 text-sm text-ember">{error}</p> : null}<div className="space-y-3">{users.map((user) => <button key={user.userId} type="button" onClick={() => void load(user.userId)} className={`w-full rounded-3xl border px-4 py-4 text-left ${detail?.userId === user.userId ? "border-brand bg-brand/5" : "border-slate-200 bg-white"}`}><p className="font-semibold text-ink">{user.displayName}</p><p className="text-sm text-steel">{user.userName} · {user.email}</p><p className="mt-2 text-xs text-steel/75">{user.roles.join(", ")}</p></button>)}</div></Card><div className="space-y-6"><Card title="Create admin user" eyebrow="Provisioning"><div className="grid gap-4 md:grid-cols-2"><input className="rounded-2xl border border-slate-200 px-4 py-3" placeholder="User name" value={createModel.userName} onChange={(event) => setCreateModel({ ...createModel, userName: event.target.value })} /><input className="rounded-2xl border border-slate-200 px-4 py-3" placeholder="Display name" value={createModel.displayName} onChange={(event) => setCreateModel({ ...createModel, displayName: event.target.value })} /><input className="rounded-2xl border border-slate-200 px-4 py-3" placeholder="Email" value={createModel.email} onChange={(event) => setCreateModel({ ...createModel, email: event.target.value })} /><input className="rounded-2xl border border-slate-200 px-4 py-3" placeholder="Temporary password" value={createModel.password} onChange={(event) => setCreateModel({ ...createModel, password: event.target.value })} /></div><div className="mt-4 flex flex-wrap gap-2">{roles.map((role) => <label key={role.roleId} className="rounded-full border border-slate-200 px-3 py-2 text-sm text-steel"><input type="checkbox" checked={createModel.roles.includes(role.name)} onChange={() => setCreateModel({ ...createModel, roles: createModel.roles.includes(role.name) ? createModel.roles.filter((item) => item !== role.name) : [...createModel.roles, role.name] })} className="mr-2" />{role.name}</label>)}</div><button type="button" onClick={() => void run(async () => { await request("/api/admin/rbac/users", accessToken, onReauthenticate, { method: "POST", body: JSON.stringify(createModel) }); setCreateModel({ userName: "", email: "", password: "", displayName: "", roles: [] }); }, "Admin user created.")} disabled={!hasPermission(permissions, permissionKeys.usersCreate)} className="app-button-primary mt-5 disabled:opacity-50">Create admin user</button></Card><Card title="Selected user" eyebrow="Detail">{detail ? <div className="space-y-4"><div className="grid gap-4 md:grid-cols-2"><input className="rounded-2xl border border-slate-200 px-4 py-3" value={editModel.displayName} onChange={(event) => setEditModel({ ...editModel, displayName: event.target.value })} /><input className="rounded-2xl border border-slate-200 px-4 py-3" value={editModel.email} onChange={(event) => setEditModel({ ...editModel, email: event.target.value })} /></div><div className="flex flex-wrap gap-2">{roles.map((role) => <label key={role.roleId} className="rounded-full border border-slate-200 px-3 py-2 text-sm text-steel"><input type="checkbox" checked={editModel.roles.includes(role.name)} onChange={() => toggle((updater) => setEditModel((current) => ({ ...current, roles: updater(current.roles) })), role.name)} className="mr-2" />{role.name}</label>)}</div><div className="flex flex-wrap gap-3"><button type="button" onClick={() => void run(async () => { await request(`/api/admin/rbac/users/${detail.userId}`, accessToken, onReauthenticate, { method: "PUT", body: JSON.stringify({ displayName: editModel.displayName, email: editModel.email }) }); await request(`/api/admin/rbac/users/${detail.userId}/roles`, accessToken, onReauthenticate, { method: "PUT", body: JSON.stringify({ roles: editModel.roles }) }); }, "Admin user updated.")} disabled={!hasPermission(permissions, permissionKeys.usersEdit)} className="app-button-primary disabled:opacity-50">Save user</button><button type="button" onClick={() => void run(async () => { await request(`/api/admin/rbac/users/${detail.userId}/activate`, accessToken, onReauthenticate, { method: "POST", body: "{}" }); }, "User activated.")} disabled={!hasPermission(permissions, permissionKeys.usersActivate)} className="app-button-secondary disabled:opacity-50">Activate</button><button type="button" onClick={() => void run(async () => { await request(`/api/admin/rbac/users/${detail.userId}/deactivate`, accessToken, onReauthenticate, { method: "POST", body: "{}" }); }, "User deactivated.")} disabled={!hasPermission(permissions, permissionKeys.usersDeactivate)} className="app-button-secondary disabled:opacity-50">Deactivate</button></div><div className="flex gap-3"><input className="flex-1 rounded-2xl border border-slate-200 px-4 py-3" placeholder="New password" value={editModel.resetPassword} onChange={(event) => setEditModel({ ...editModel, resetPassword: event.target.value })} /><button type="button" onClick={() => void run(async () => { await request(`/api/admin/rbac/users/${detail.userId}/reset-password`, accessToken, onReauthenticate, { method: "POST", body: JSON.stringify({ newPassword: editModel.resetPassword }) }); setEditModel({ ...editModel, resetPassword: "" }); }, "Password reset completed.")} disabled={!hasPermission(permissions, permissionKeys.usersResetPassword)} className="app-button-danger disabled:opacity-50">Reset</button></div><div className="flex flex-wrap gap-2">{detail.effectivePermissions.map((permission) => <Pill key={permission}>{permission}</Pill>)}</div></div> : <p className="text-sm text-steel">Select an admin user to inspect details.</p>}</Card></div></div>;
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
    const [loadedRoles, loadedGroups, loadedCatalog] = await Promise.all([
      request<RoleItem[]>("/api/admin/rbac/roles", accessToken, onReauthenticate),
      request<GroupItem[]>("/api/admin/rbac/permission-groups", accessToken, onReauthenticate),
      request<Permission[]>("/api/admin/rbac/permissions", accessToken, onReauthenticate)
    ]);

    setRoles(loadedRoles);
    setGroups(loadedGroups);
    setCatalog(loadedCatalog);

    const selected = roleId ?? detail?.roleId ?? loadedRoles[0]?.roleId;
    if (selected) {
      const loadedDetail = await request<RoleDetail>(`/api/admin/rbac/roles/${selected}`, accessToken, onReauthenticate);
      setDetail(loadedDetail);
      setEditModel({
        description: loadedDetail.description,
        priority: loadedDetail.priority,
        groups: loadedDetail.permissionGroupIds,
        permissions: loadedDetail.directPermissionKeys
      });
    }
  };

  useEffect(() => {
    load().catch((value) => setError(value instanceof Error ? value.message : "Unable to load roles."));
  }, [accessToken, onReauthenticate]);

  const flip = (items: string[], value: string) => items.includes(value) ? items.filter((item) => item !== value) : [...items, value];
  const run = async (action: () => Promise<void>, success: string) => {
    try {
      setError(null);
      await action();
      setMessage(success);
      await load(detail?.roleId);
    } catch (value) {
      setError(value instanceof Error ? value.message : success);
    }
  };

  return (
    <div className="grid gap-6 xl:grid-cols-[0.85fr,1.15fr]">
      <Card title="Roles" eyebrow="System + custom">
        {message ? <p className="mb-4 rounded-2xl bg-moss/10 px-4 py-3 text-sm text-moss">{message}</p> : null}
        {error ? <p className="mb-4 rounded-2xl bg-ember/10 px-4 py-3 text-sm text-ember">{error}</p> : null}
        <div className="space-y-3">
          {roles.map((role) => (
            <button
              key={role.roleId}
              type="button"
              onClick={() => void load(role.roleId)}
              className={`w-full rounded-3xl border px-4 py-4 text-left ${detail?.roleId === role.roleId ? "border-brand bg-brand/5" : "border-slate-200 bg-white"}`}
            >
              <p className="font-semibold text-ink">{role.name}</p>
              <p className="text-sm text-steel">{role.description}</p>
              <p className="mt-2 text-xs text-steel/75">{role.userCount} users · priority {role.priority}</p>
            </button>
          ))}
        </div>
      </Card>

      <div className="space-y-6">
        <Card title="Create role" eyebrow="Model access">
          <div className="grid gap-4 md:grid-cols-3">
            <input className="rounded-2xl border border-slate-200 px-4 py-3" placeholder="Role name" value={createModel.name} onChange={(event) => setCreateModel({ ...createModel, name: event.target.value })} />
            <input className="rounded-2xl border border-slate-200 px-4 py-3 md:col-span-2" placeholder="Description" value={createModel.description} onChange={(event) => setCreateModel({ ...createModel, description: event.target.value })} />
            <input className="rounded-2xl border border-slate-200 px-4 py-3" type="number" value={createModel.priority} onChange={(event) => setCreateModel({ ...createModel, priority: Number(event.target.value) })} />
          </div>
          <button
            type="button"
            onClick={() => void run(async () => {
              await request("/api/admin/rbac/roles", accessToken, onReauthenticate, { method: "POST", body: JSON.stringify(createModel) });
              setCreateModel({ name: "", description: "", priority: 100 });
            }, "Role created.")}
            disabled={!hasPermission(permissions, permissionKeys.rolesCreate)}
            className="app-button-primary mt-5 disabled:opacity-50"
          >
            Create role
          </button>
        </Card>

        <Card title="Role detail" eyebrow="Effective permissions">
          {detail ? (
            <div className="space-y-5">
              <div className="grid gap-4 md:grid-cols-2">
                <input className="rounded-2xl border border-slate-200 px-4 py-3" value={detail.name} disabled />
                <input className="rounded-2xl border border-slate-200 px-4 py-3" type="number" value={editModel.priority} onChange={(event) => setEditModel({ ...editModel, priority: Number(event.target.value) })} />
              </div>

              <textarea className="min-h-24 w-full rounded-2xl border border-slate-200 px-4 py-3" value={editModel.description} onChange={(event) => setEditModel({ ...editModel, description: event.target.value })} />

              <div className="flex flex-wrap gap-2">
                {groups.map((group) => (
                  <label key={group.id} className="rounded-full border border-slate-200 px-3 py-2 text-sm text-steel">
                    <input type="checkbox" checked={editModel.groups.includes(group.id)} onChange={() => setEditModel({ ...editModel, groups: flip(editModel.groups, group.id) })} className="mr-2" />
                    {group.name}
                  </label>
                ))}
              </div>

              <div className="grid gap-2 md:grid-cols-2">
                {catalog.map((permission) => (
                  <label key={permission.key} className="rounded-2xl border border-slate-200 bg-slate-50 px-3 py-3 text-sm text-steel">
                    <input type="checkbox" checked={editModel.permissions.includes(permission.key)} onChange={() => setEditModel({ ...editModel, permissions: flip(editModel.permissions, permission.key) })} className="mr-2" />
                    {permission.key}
                  </label>
                ))}
              </div>

              <div className="flex flex-wrap gap-3">
                <button
                  type="button"
                  onClick={() => void run(async () => {
                    await request(`/api/admin/rbac/roles/${detail.roleId}`, accessToken, onReauthenticate, { method: "PUT", body: JSON.stringify({ description: editModel.description, priority: editModel.priority }) });
                    await request(`/api/admin/rbac/roles/${detail.roleId}/permission-groups`, accessToken, onReauthenticate, { method: "PUT", body: JSON.stringify({ permissionGroupIds: editModel.groups }) });
                    await request(`/api/admin/rbac/roles/${detail.roleId}/permissions`, accessToken, onReauthenticate, { method: "PUT", body: JSON.stringify({ permissionKeys: editModel.permissions }) });
                  }, "Role updated.")}
                  disabled={!hasPermission(permissions, permissionKeys.rolesEdit)}
                  className="app-button-primary disabled:opacity-50"
                >
                  Save role
                </button>

                <button
                  type="button"
                  onClick={() => void run(async () => {
                    await request(`/api/admin/rbac/roles/${detail.roleId}`, accessToken, onReauthenticate, { method: "DELETE" });
                    setDetail(null);
                  }, "Role deleted.")}
                  disabled={detail.isSystemRole || !hasPermission(permissions, permissionKeys.rolesDelete)}
                  className="app-button-danger disabled:opacity-50"
                >
                  Delete role
                </button>
              </div>

              <RolePermissionBreakdown detail={detail} groups={groups} />
            </div>
          ) : (
            <p className="text-sm text-steel">Select a role to inspect detail.</p>
          )}
        </Card>
      </div>
    </div>
  );
}

export function PermissionGroupsPage({ accessToken, onReauthenticate, permissions }: PageProps) {
  const [groups, setGroups] = useState<GroupItem[]>([]); const [catalog, setCatalog] = useState<Permission[]>([]); const [detail, setDetail] = useState<GroupDetail | null>(null); const [error, setError] = useState<string | null>(null); const [message, setMessage] = useState<string | null>(null); const [createModel, setCreateModel] = useState({ name: "", description: "" }); const [editModel, setEditModel] = useState({ name: "", description: "", permissions: [] as string[] });
  const load = async (groupId?: string) => { const [loadedGroups, loadedCatalog] = await Promise.all([request<GroupItem[]>("/api/admin/rbac/permission-groups", accessToken, onReauthenticate), request<Permission[]>("/api/admin/rbac/permissions", accessToken, onReauthenticate)]); setGroups(loadedGroups); setCatalog(loadedCatalog); const selected = groupId ?? detail?.id ?? loadedGroups[0]?.id; if (selected) { const loadedDetail = await request<GroupDetail>(`/api/admin/rbac/permission-groups/${selected}`, accessToken, onReauthenticate); setDetail(loadedDetail); setEditModel({ name: loadedDetail.name, description: loadedDetail.description, permissions: loadedDetail.permissionKeys }); } };
  useEffect(() => { load().catch((value) => setError(value instanceof Error ? value.message : "Unable to load permission groups.")); }, [accessToken, onReauthenticate]);
  const run = async (action: () => Promise<void>, success: string) => { try { setError(null); await action(); setMessage(success); await load(detail?.id); } catch (value) { setError(value instanceof Error ? value.message : success); } };
  return <div className="grid gap-6 xl:grid-cols-[0.85fr,1.15fr]"><Card title="Permission groups" eyebrow="Reusable bundles">{message ? <p className="mb-4 rounded-2xl bg-moss/10 px-4 py-3 text-sm text-moss">{message}</p> : null}{error ? <p className="mb-4 rounded-2xl bg-ember/10 px-4 py-3 text-sm text-ember">{error}</p> : null}<div className="space-y-3">{groups.map((group) => <button key={group.id} type="button" onClick={() => void load(group.id)} className={`w-full rounded-3xl border px-4 py-4 text-left ${detail?.id === group.id ? "border-brand bg-brand/5" : "border-slate-200 bg-white"}`}><p className="font-semibold text-ink">{group.name}</p><p className="text-sm text-steel">{group.description}</p></button>)}</div></Card><div className="space-y-6"><Card title="Create permission group" eyebrow="Catalog"><div className="grid gap-4 md:grid-cols-2"><input className="rounded-2xl border border-slate-200 px-4 py-3" placeholder="Group name" value={createModel.name} onChange={(event) => setCreateModel({ ...createModel, name: event.target.value })} /><input className="rounded-2xl border border-slate-200 px-4 py-3" placeholder="Description" value={createModel.description} onChange={(event) => setCreateModel({ ...createModel, description: event.target.value })} /></div><button type="button" onClick={() => void run(async () => { await request("/api/admin/rbac/permission-groups", accessToken, onReauthenticate, { method: "POST", body: JSON.stringify(createModel) }); setCreateModel({ name: "", description: "" }); }, "Permission group created.")} disabled={!hasPermission(permissions, permissionKeys.permissionGroupsCreate)} className="app-button-primary mt-5 disabled:opacity-50">Create group</button></Card><Card title="Permission group detail" eyebrow="Assignments">{detail ? <div className="space-y-4"><div className="grid gap-4 md:grid-cols-2"><input className="rounded-2xl border border-slate-200 px-4 py-3" value={editModel.name} onChange={(event) => setEditModel({ ...editModel, name: event.target.value })} disabled={detail.isSystemGroup} /><input className="rounded-2xl border border-slate-200 px-4 py-3" value={editModel.description} onChange={(event) => setEditModel({ ...editModel, description: event.target.value })} /></div><div className="grid gap-2 md:grid-cols-2">{catalog.map((permission) => <label key={permission.key} className="rounded-2xl border border-slate-200 bg-slate-50 px-3 py-3 text-sm text-steel"><input type="checkbox" checked={editModel.permissions.includes(permission.key)} onChange={() => setEditModel({ ...editModel, permissions: editModel.permissions.includes(permission.key) ? editModel.permissions.filter((item) => item !== permission.key) : [...editModel.permissions, permission.key] })} className="mr-2" />{permission.key}</label>)}</div><div className="flex flex-wrap gap-3"><button type="button" onClick={() => void run(async () => { await request(`/api/admin/rbac/permission-groups/${detail.id}`, accessToken, onReauthenticate, { method: "PUT", body: JSON.stringify({ name: editModel.name, description: editModel.description }) }); await request(`/api/admin/rbac/permission-groups/${detail.id}/permissions`, accessToken, onReauthenticate, { method: "PUT", body: JSON.stringify({ permissionKeys: editModel.permissions }) }); }, "Permission group updated.")} disabled={!hasPermission(permissions, permissionKeys.permissionGroupsEdit)} className="app-button-primary disabled:opacity-50">Save group</button><button type="button" onClick={() => void run(async () => { await request(`/api/admin/rbac/permission-groups/${detail.id}`, accessToken, onReauthenticate, { method: "DELETE" }); setDetail(null); }, "Permission group deleted.")} disabled={detail.isSystemGroup || !hasPermission(permissions, permissionKeys.permissionGroupsDelete)} className="app-button-danger disabled:opacity-50">Delete group</button></div></div> : <p className="text-sm text-steel">Select a permission group to inspect detail.</p>}</Card></div></div>;
}
