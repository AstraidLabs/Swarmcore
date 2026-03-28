import { type KeyboardEvent as ReactKeyboardEvent, type ReactNode, useEffect, useMemo, useState } from "react";
import { useSearchParams } from "react-router-dom";
import {
  normalizeCatalogQueryState,
  readCatalogViewState,
  toCatalogViewSearchParams,
  type CatalogModalMode,
  type CatalogQueryState,
  type CatalogViewState
} from "./catalog";

export function useCatalogViewState(defaults: CatalogQueryState) {
  const normalizedDefaults = useMemo(() => normalizeCatalogQueryState(defaults), [defaults]);
  const [searchParams, setSearchParams] = useSearchParams();

  const view = useMemo(
    () => readCatalogViewState(searchParams, normalizedDefaults),
    [normalizedDefaults, searchParams]
  );

  const setView = (value: CatalogViewState | ((current: CatalogViewState) => CatalogViewState)) => {
    const nextView = typeof value === "function" ? value(view) : value;
    setSearchParams(toCatalogViewSearchParams(nextView, normalizedDefaults), { replace: true });
  };

  return [view, setView] as const;
}

const catalogDensityStorageKey = "beetracker.admin.catalogDensity";

type CatalogDensity = "comfortable" | "dense";

function useCatalogDensity() {
  const [density, setDensity] = useState<CatalogDensity>(() => {
    if (typeof window === "undefined") {
      return "dense";
    }

    const storedDensity = window.localStorage.getItem(catalogDensityStorageKey);
    return storedDensity === "comfortable" ? "comfortable" : "dense";
  });

  useEffect(() => {
    document.documentElement.dataset.catalogDensity = density;
    window.localStorage.setItem(catalogDensityStorageKey, density);
  }, [density]);

  return [density, setDensity] as const;
}

function shouldIgnoreRowKeyboardOpen(target: EventTarget | null) {
  if (!(target instanceof HTMLElement)) {
    return false;
  }

  return Boolean(target.closest("button, a, input, select, textarea, [role='button']"));
}

export function CatalogTableRow({
  children,
  onOpen,
  className = ""
}: {
  children: ReactNode;
  onOpen?: () => void;
  className?: string;
}) {
  const handleKeyDown = (event: ReactKeyboardEvent<HTMLTableRowElement>) => {
    if (!onOpen || shouldIgnoreRowKeyboardOpen(event.target)) {
      return;
    }

    if (event.key === "Enter") {
      event.preventDefault();
      onOpen();
    }
  };

  return (
    <tr
      className={`app-table-row border-t border-slate-200/80 align-top ${className}`.trim()}
      tabIndex={onOpen ? 0 : undefined}
      onKeyDown={handleKeyDown}
    >
      {children}
    </tr>
  );
}

export function CopyValueButton({
  value,
  label = "Copy value"
}: {
  value: string;
  label?: string;
}) {
  const [copied, setCopied] = useState(false);

  useEffect(() => {
    if (!copied) {
      return;
    }

    const timeoutId = window.setTimeout(() => setCopied(false), 1200);
    return () => window.clearTimeout(timeoutId);
  }, [copied]);

  const handleClick = async () => {
    try {
      await navigator.clipboard.writeText(value);
      setCopied(true);
    } catch {
      setCopied(false);
    }
  };

  return (
    <button type="button" className="app-copy-button" aria-label={label} title={copied ? "Copied" : label} onClick={handleClick}>
      <svg viewBox="0 0 20 20" className="h-3.5 w-3.5 fill-none stroke-current" strokeWidth="1.7" aria-hidden="true">
        <rect x="7" y="7" width="9" height="9" rx="2" />
        <path d="M5 13H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h7a2 2 0 0 1 2 2v1" />
      </svg>
      <span>{copied ? "Copied" : "Copy"}</span>
    </button>
  );
}

function useEscapeDismiss(open: boolean, onClose: () => void) {
  useEffect(() => {
    if (!open) {
      return;
    }

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        event.preventDefault();
        onClose();
      }
    };

    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [open, onClose]);
}

export function Modal({
  title,
  description,
  open,
  onClose,
  width = "wide",
  children
}: {
  title: string;
  description?: string;
  open: boolean;
  onClose: () => void;
  width?: "medium" | "wide" | "xwide";
  children: ReactNode;
}) {
  useEscapeDismiss(open, onClose);

  if (!open) return null;

  return (
    <div className="app-modal-overlay" onClick={onClose}>
      <div className={`app-modal-card app-modal-card-${width}`} onClick={(event) => event.stopPropagation()}>
        <div className="app-card-header flex items-start justify-between gap-4">
          <div className="space-y-1">
            <div className="app-kicker">Modal</div>
            <h2 className="text-2xl font-bold text-ink">{title}</h2>
            {description ? <p className="text-sm text-steel">{description}</p> : null}
          </div>
          <button type="button" className="app-icon-button" aria-label="Close modal" onClick={onClose}>
            <svg viewBox="0 0 24 24" className="h-4 w-4 fill-none stroke-current" strokeWidth="1.8">
              <path d="m6 6 12 12" />
              <path d="M18 6 6 18" />
            </svg>
          </button>
        </div>
        <div className="app-card-body">{children}</div>
      </div>
    </div>
  );
}

export function CatalogToolbar({
  title,
  description,
  totalCount,
  search,
  onSearchChange,
  sortValue,
  onSortChange,
  sortOptions,
  pageSize,
  onPageSizeChange,
  filter,
  onFilterChange,
  filterOptions,
  createLabel,
  onCreate,
  searchPlaceholder = "Search catalog"
}: {
  title: string;
  description?: string;
  totalCount: number;
  search: string;
  onSearchChange: (value: string) => void;
  sortValue: string;
  onSortChange: (value: string) => void;
  sortOptions: Array<{ value: string; label: string }>;
  pageSize: number;
  onPageSizeChange: (value: number) => void;
  filter: string;
  onFilterChange: (value: string) => void;
  filterOptions: Array<{ value: string; label: string }>;
  createLabel?: string;
  onCreate?: () => void;
  searchPlaceholder?: string;
}) {
  const [density, setDensity] = useCatalogDensity();

  return (
    <div className="app-card">
      <div className="app-card-header app-catalog-header">
        <div className="space-y-1">
          <div className="app-kicker">Catalog</div>
          <div className="flex flex-wrap items-center gap-2">
            <h2 className="text-lg font-bold text-ink">{title}</h2>
            <span className="app-chip">{totalCount} result{totalCount === 1 ? "" : "s"}</span>
          </div>
          {description ? <p className="app-catalog-description">{description}</p> : null}
        </div>
        {createLabel && onCreate ? (
          <div className="flex items-center gap-2">
            <button
              type="button"
              className={`app-density-toggle ${density === "dense" ? "app-density-toggle-active" : ""}`}
              onClick={() => setDensity(density === "dense" ? "comfortable" : "dense")}
            >
              {density === "dense" ? "Dense" : "Comfortable"}
            </button>
            <button type="button" className="app-button-primary" onClick={onCreate}>
              {createLabel}
            </button>
          </div>
        ) : (
          <button
            type="button"
            className={`app-density-toggle ${density === "dense" ? "app-density-toggle-active" : ""}`}
            onClick={() => setDensity(density === "dense" ? "comfortable" : "dense")}
          >
            {density === "dense" ? "Dense" : "Comfortable"}
          </button>
        )}
      </div>
      <div className="app-card-body">
        <div className="app-catalog-toolbar">
          <input
            className="app-input"
            value={search}
            placeholder={searchPlaceholder}
            onChange={(event) => onSearchChange(event.target.value)}
          />
          <select className="app-input" value={filter} onChange={(event) => onFilterChange(event.target.value)}>
            {filterOptions.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
          <select className="app-input" value={sortValue} onChange={(event) => onSortChange(event.target.value)}>
            {sortOptions.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
          <select className="app-input" value={String(pageSize)} onChange={(event) => onPageSizeChange(Number(event.target.value))}>
            {[10, 25, 50, 100, 250].map((option) => (
              <option key={option} value={option}>
                {option} per page
              </option>
            ))}
          </select>
        </div>
      </div>
    </div>
  );
}

export function PaginationFooter({
  page,
  pageCount,
  totalCount,
  pageSize,
  onPageChange
}: {
  page: number;
  pageCount: number;
  totalCount: number;
  pageSize: number;
  onPageChange: (page: number) => void;
}) {
  const normalizedPage = Math.min(Math.max(page, 1), Math.max(pageCount, 1));
  const from = totalCount === 0 ? 0 : (normalizedPage - 1) * pageSize + 1;
  const to = Math.min(normalizedPage * pageSize, totalCount);

  if (pageCount <= 1) {
    return (
      <div className="app-catalog-pagination">
        <p className="text-sm text-steel">
          Showing {from}-{to} of {totalCount} record{totalCount === 1 ? "" : "s"}
        </p>
      </div>
    );
  }

  return (
    <div className="app-catalog-pagination">
      <p className="text-sm text-steel">
        Showing {from}-{to} of {totalCount} records - page {normalizedPage} of {pageCount}
      </p>
      <div className="flex items-center gap-2">
        <button type="button" className="app-button-secondary py-2.5" disabled={page <= 1} onClick={() => onPageChange(page - 1)}>
          Previous
        </button>
        <button type="button" className="app-button-secondary py-2.5" disabled={page >= pageCount} onClick={() => onPageChange(page + 1)}>
          Next
        </button>
      </div>
    </div>
  );
}

export function SortHeaderButton({
  label,
  active,
  direction,
  onClick
}: {
  label: string;
  active: boolean;
  direction: "asc" | "desc";
  onClick: () => void;
}) {
  return (
    <button type="button" className="inline-flex items-center gap-1.5 font-semibold transition hover:text-white" onClick={onClick}>
      <span>{label}</span>
      <span className={active ? "text-honey-200" : "text-white/35"}>
        {active ? (
          <svg viewBox="0 0 16 16" className="h-3 w-3 fill-none stroke-current" strokeWidth="1.8" aria-hidden="true">
            {direction === "asc" ? <path d="M8 12V4m0 0L5.5 6.5M8 4l2.5 2.5" /> : <path d="M8 4v8m0 0-2.5-2.5M8 12l2.5-2.5" />}
          </svg>
        ) : (
          <svg viewBox="0 0 16 16" className="h-3 w-3 fill-none stroke-current" strokeWidth="1.6" aria-hidden="true">
            <path d="M8 3v10M8 3 5.75 5.25M8 3l2.25 2.25M8 13l-2.25-2.25M8 13l2.25-2.25" />
          </svg>
        )}
      </span>
    </button>
  );
}

export function TableStateRow({
  colSpan,
  title,
  message
}: {
  colSpan: number;
  title: string;
  message: string;
}) {
  return (
    <tr>
      <td colSpan={colSpan} className="px-5 py-10">
        <div className="app-table-state">
          <div className="text-base font-semibold text-ink">{title}</div>
          <div className="text-sm text-steel">{message}</div>
        </div>
      </td>
    </tr>
  );
}

export function PreviewDrawer({
  open,
  title,
  subtitle,
  onClose,
  children
}: {
  open: boolean;
  title: string;
  subtitle?: string;
  onClose: () => void;
  children: ReactNode;
}) {
  useEscapeDismiss(open, onClose);

  return (
    <div className={`app-preview-drawer ${open ? "app-preview-drawer-open" : ""}`} aria-hidden={!open}>
      <div className="app-preview-drawer-card">
        <div className="app-preview-drawer-header">
          <div className="space-y-1">
            <div className="app-kicker">Quick preview</div>
            <div className="text-xl font-bold text-ink">{title}</div>
            {subtitle ? <div className="text-sm text-steel">{subtitle}</div> : null}
          </div>
          <button type="button" className="app-icon-button" aria-label="Close preview" onClick={onClose}>
            <svg viewBox="0 0 24 24" className="h-4 w-4 fill-none stroke-current" strokeWidth="1.8">
              <path d="m6 6 12 12" />
              <path d="M18 6 6 18" />
            </svg>
          </button>
        </div>
        <div className="app-preview-drawer-body">{children}</div>
      </div>
    </div>
  );
}

export function ConfirmActionModal({
  open,
  title,
  description,
  confirmLabel,
  tone = "danger",
  onConfirm,
  onClose
}: {
  open: boolean;
  title: string;
  description: string;
  confirmLabel: string;
  tone?: "danger" | "primary";
  onConfirm: () => void;
  onClose: () => void;
}) {
  return (
    <Modal open={open} onClose={onClose} title={title} description={description} width="medium">
      <div className="flex justify-end gap-3">
        <button type="button" className="app-button-secondary" onClick={onClose}>
          Cancel
        </button>
        <button type="button" className={tone === "danger" ? "app-button-danger" : "app-button-primary"} onClick={onConfirm}>
          {confirmLabel}
        </button>
      </div>
    </Modal>
  );
}

export function sanitizeCatalogViewState<TItem>(
  view: CatalogViewState,
  items: readonly TItem[],
  getId: (item: TItem) => string,
  supportsCreate = true
): CatalogViewState {
  const validIds = new Set(items.map(getId));
  const normalizedModal: CatalogModalMode =
    view.modal === "create"
      ? (supportsCreate ? "create" : null)
      : view.modal === "edit" && view.id && validIds.has(view.id)
        ? "edit"
        : null;

  return {
    ...view,
    preview: view.preview && validIds.has(view.preview) ? view.preview : null,
    modal: normalizedModal,
    id: normalizedModal === "edit" && view.id ? view.id : null
  };
}
