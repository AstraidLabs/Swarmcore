import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { describe, expect, it } from "vitest";
import { PermissionGate, computeInheritedPermissions, permissionKeys, sortPermissionKeys } from "./rbac";

describe("rbac helpers", () => {
  it("sortPermissionKeys returns a stable alphabetical copy", () => {
    const result = sortPermissionKeys([
      "admin.users.edit",
      "admin.audit.view",
      "admin.users.create"
    ]);

    expect(result).toEqual([
      "admin.audit.view",
      "admin.users.create",
      "admin.users.edit"
    ]);
  });

  it("computeInheritedPermissions removes direct grants from effective permissions", () => {
    const result = computeInheritedPermissions(
      [
        "admin.roles.view",
        "admin.roles.assign_permissions",
        "admin.permission_groups.view"
      ],
      [
        "admin.roles.view"
      ]
    );

    expect(result).toEqual([
      "admin.permission_groups.view",
      "admin.roles.assign_permissions"
    ]);
  });
});

describe("PermissionGate", () => {
  it("renders children when the permission is granted", () => {
    render(
      <MemoryRouter>
        <PermissionGate permissions={[permissionKeys.rolesView]} permission={permissionKeys.rolesView}>
          <div>Roles page</div>
        </PermissionGate>
      </MemoryRouter>
    );

    expect(screen.getByText("Roles page")).toBeInTheDocument();
  });

  it("renders an access denied panel with the missing permission when access is denied", () => {
    render(
      <MemoryRouter>
        <PermissionGate permissions={[]} permission={permissionKeys.rolesAssignPermissions}>
          <div>Hidden content</div>
        </PermissionGate>
      </MemoryRouter>
    );

    expect(screen.getByText("Access denied")).toBeInTheDocument();
    expect(screen.getByText(permissionKeys.rolesAssignPermissions)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Go to dashboard" })).toHaveAttribute("href", "/");
    expect(screen.queryByText("Hidden content")).not.toBeInTheDocument();
  });
});
