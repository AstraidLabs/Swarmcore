export type PageResult<T> = {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages?: number;
  hasPreviousPage?: boolean;
  hasNextPage?: boolean;
};

export type CatalogQueryState = {
  search: string;
  filter: string;
  sort: string;
  page: number;
  pageSize: number;
};

export type CatalogModalMode = "create" | "edit" | null;

export type CatalogViewState = {
  query: CatalogQueryState;
  preview: string | null;
  modal: CatalogModalMode;
  id: string | null;
};

export function normalizeCatalogQueryState(query: CatalogQueryState): CatalogQueryState {
  return {
    search: query.search.trim(),
    filter: query.filter || "all",
    sort: query.sort,
    page: query.page > 0 ? query.page : 1,
    pageSize: query.pageSize > 0 ? query.pageSize : 25
  };
}

export function buildGridQueryString(
  query: CatalogQueryState,
  extra: Record<string, string | number | boolean | null | undefined> = {}
) {
  const normalizedQuery = normalizeCatalogQueryState(query);
  const searchParams = new URLSearchParams();
  if (normalizedQuery.search) {
    searchParams.set("search", normalizedQuery.search);
  }

  if (normalizedQuery.filter && normalizedQuery.filter !== "all") {
    searchParams.set("filter", normalizedQuery.filter);
  }

  if (normalizedQuery.sort) {
    searchParams.set("sort", normalizedQuery.sort);
  }

  searchParams.set("page", String(normalizedQuery.page));
  searchParams.set("pageSize", String(normalizedQuery.pageSize));

  Object.entries(extra).forEach(([key, value]) => {
    if (value !== undefined && value !== null && value !== "") {
      searchParams.set(key, String(value));
    }
  });

  return searchParams.toString();
}

export function readCatalogQueryState(
  searchParams: URLSearchParams,
  defaults: CatalogQueryState
): CatalogQueryState {
  return normalizeCatalogQueryState({
    search: searchParams.get("search") ?? defaults.search,
    filter: searchParams.get("filter") ?? defaults.filter,
    sort: searchParams.get("sort") ?? defaults.sort,
    page: Number(searchParams.get("page") ?? defaults.page),
    pageSize: Number(searchParams.get("pageSize") ?? defaults.pageSize)
  });
}

export function toCatalogSearchParams(
  query: CatalogQueryState,
  defaults: CatalogQueryState
): URLSearchParams {
  const normalizedDefaults = normalizeCatalogQueryState(defaults);
  const normalizedQuery = normalizeCatalogQueryState(query);
  const params = new URLSearchParams();

  if (normalizedQuery.search && normalizedQuery.search !== normalizedDefaults.search) {
    params.set("search", normalizedQuery.search);
  }

  if (normalizedQuery.filter !== normalizedDefaults.filter && normalizedQuery.filter !== "all") {
    params.set("filter", normalizedQuery.filter);
  }

  if (normalizedQuery.sort && normalizedQuery.sort !== normalizedDefaults.sort) {
    params.set("sort", normalizedQuery.sort);
  }

  if (normalizedQuery.page !== normalizedDefaults.page) {
    params.set("page", String(normalizedQuery.page));
  }

  if (normalizedQuery.pageSize !== normalizedDefaults.pageSize) {
    params.set("pageSize", String(normalizedQuery.pageSize));
  }

  return params;
}

export function readCatalogViewState(
  searchParams: URLSearchParams,
  defaults: CatalogQueryState
): CatalogViewState {
  const modal = searchParams.get("modal");
  const normalizedModal: CatalogModalMode = modal === "create" || modal === "edit" ? modal : null;
  const id = searchParams.get("id");
  const preview = searchParams.get("preview");

  return {
    query: readCatalogQueryState(searchParams, defaults),
    preview: preview?.trim() ? preview : null,
    modal: normalizedModal,
    id: id?.trim() ? id : null
  };
}

export function toCatalogViewSearchParams(
  view: CatalogViewState,
  defaults: CatalogQueryState
): URLSearchParams {
  const params = toCatalogSearchParams(view.query, defaults);

  if (view.preview) {
    params.set("preview", view.preview);
  }

  if (view.modal) {
    params.set("modal", view.modal);
  }

  if (view.modal === "edit" && view.id) {
    params.set("id", view.id);
  }

  return params;
}
