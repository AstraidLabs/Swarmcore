import { describe, expect, it } from "vitest";
import { readCatalogViewState, toCatalogViewSearchParams } from "./catalog";

describe("catalog view state", () => {
  const defaults = {
    search: "",
    filter: "all",
    sort: "name:asc",
    page: 1,
    pageSize: 25
  };

  it("reads query and deep-linkable view state from search params", () => {
    const params = new URLSearchParams("search=admin&filter=system&sort=priority:desc&page=3&pageSize=100&preview=role-1&modal=edit&id=role-1");

    const result = readCatalogViewState(params, defaults);

    expect(result).toEqual({
      query: {
        search: "admin",
        filter: "system",
        sort: "priority:desc",
        page: 3,
        pageSize: 100
      },
      preview: "role-1",
      modal: "edit",
      id: "role-1"
    });
  });

  it("writes only non-default query values and valid modal edit id", () => {
    const params = toCatalogViewSearchParams(
      {
        query: {
          search: "admin",
          filter: "system",
          sort: "priority:desc",
          page: 2,
          pageSize: 100
        },
        preview: "role-1",
        modal: "edit",
        id: "role-1"
      },
      defaults
    );

    expect(params.toString()).toBe(
      "search=admin&filter=system&sort=priority%3Adesc&page=2&pageSize=100&preview=role-1&modal=edit&id=role-1"
    );
  });

  it("does not persist id for create modals", () => {
    const params = toCatalogViewSearchParams(
      {
        query: defaults,
        preview: null,
        modal: "create",
        id: "stale-id"
      },
      defaults
    );

    expect(params.toString()).toBe("modal=create");
  });
});
