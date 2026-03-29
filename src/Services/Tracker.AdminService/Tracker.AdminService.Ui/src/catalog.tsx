import { type KeyboardEvent as ReactKeyboardEvent, type ReactNode, useEffect, useMemo, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { Link, useSearchParams } from "react-router-dom";
import {
  normalizeCatalogQueryState,
  readCatalogViewState,
  toCatalogViewSearchParams,
  type CatalogModalMode,
  type CatalogQueryState,
  type CatalogViewState
} from "./catalog";

export function PlusIcon({ className = "h-4 w-4" }: { className?: string }) {
  return (
    <svg viewBox="0 0 20 20" className={`${className} fill-none stroke-current`.trim()} strokeWidth="1.8" aria-hidden="true">
      <path d="M10 4v12" />
      <path d="M4 10h12" />
    </svg>
  );
}

export function PencilIcon({ className = "h-4 w-4" }: { className?: string }) {
  return (
    <svg viewBox="0 0 20 20" className={`${className} fill-none stroke-current`.trim()} strokeWidth="1.8" aria-hidden="true">
      <path d="m13.75 3.75 2.5 2.5" />
      <path d="M4 16l3.25-.75L16 6.5 13.5 4 4.75 12.75 4 16Z" />
    </svg>
  );
}

export function EyeIcon({ className = "h-4 w-4" }: { className?: string }) {
  return (
    <svg viewBox="0 0 20 20" className={`${className} fill-none stroke-current`.trim()} strokeWidth="1.8" aria-hidden="true">
      <path d="M2.5 10s2.75-4.5 7.5-4.5S17.5 10 17.5 10s-2.75 4.5-7.5 4.5S2.5 10 2.5 10Z" />
      <circle cx="10" cy="10" r="2.25" />
    </svg>
  );
}

export function TrashIcon({ className = "h-4 w-4" }: { className?: string }) {
  return (
    <svg viewBox="0 0 20 20" className={`${className} fill-none stroke-current`.trim()} strokeWidth="1.8" aria-hidden="true">
      <path d="M3.5 5.5h13" />
      <path d="M7.5 5.5V4.25A1.25 1.25 0 0 1 8.75 3h2.5a1.25 1.25 0 0 1 1.25 1.25V5.5" />
      <path d="M6.25 5.5v10.25A1.25 1.25 0 0 0 7.5 17h5a1.25 1.25 0 0 0 1.25-1.25V5.5" />
      <path d="M8 8v6" />
      <path d="M12 8v6" />
    </svg>
  );
}

export function PowerIcon({ className = "h-4 w-4" }: { className?: string }) {
  return (
    <svg viewBox="0 0 20 20" className={`${className} fill-none stroke-current`.trim()} strokeWidth="1.8" aria-hidden="true">
      <path d="M10 2.75v6.5" />
      <path d="M6 4.75a6 6 0 1 0 8 0" />
    </svg>
  );
}

export function SettingsIcon({ className = "h-4 w-4" }: { className?: string }) {
  return (
    <svg viewBox="0 0 20 20" className={`${className} fill-none stroke-current`.trim()} strokeWidth="1.8" aria-hidden="true">
      <path d="M10 3.25v2.1" />
      <path d="M10 14.65v2.1" />
      <path d="m5.2 5.2 1.5 1.5" />
      <path d="m13.3 13.3 1.5 1.5" />
      <path d="M3.25 10h2.1" />
      <path d="M14.65 10h2.1" />
      <path d="m5.2 14.8 1.5-1.5" />
      <path d="m13.3 6.7 1.5-1.5" />
      <circle cx="10" cy="10" r="2.65" />
    </svg>
  );
}

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

function dismissClick(event: ReactMouseEventLike, onClose: () => void) {
  event.preventDefault();
  event.stopPropagation();
  onClose();
}

type ReactMouseEventLike = {
  preventDefault: () => void;
  stopPropagation: () => void;
};

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
    <button
      type="button"
      className={`app-copy-button ${copied ? "app-copy-button-copied" : ""}`.trim()}
      aria-label={copied ? "Copied" : label}
      title={copied ? "Copied" : label}
      onClick={handleClick}
    >
      <svg viewBox="0 0 20 20" className="h-3.5 w-3.5 fill-none stroke-current" strokeWidth="1.7" aria-hidden="true">
        <rect x="7" y="7" width="9" height="9" rx="2" />
        <path d="M5 13H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h7a2 2 0 0 1 2 2v1" />
      </svg>
      <span className="sr-only">{copied ? "Copied" : label}</span>
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
          <button
            type="button"
            className="app-modal-close-button"
            aria-label="Close modal"
            onMouseDown={(event) => {
              event.preventDefault();
              event.stopPropagation();
            }}
            onClick={(event) => dismissClick(event, onClose)}
          >
            <svg viewBox="0 0 24 24" className="h-4 w-4 fill-none stroke-current" strokeWidth="2">
              <path d="m6 6 12 12" />
              <path d="M18 6 6 18" />
            </svg>
            <span>Close</span>
          </button>
        </div>
        <div className="app-card-body">{children}</div>
      </div>
    </div>
  );
}

export function ModalDismissButton({
  onClose,
  children = "Cancel",
  className = "app-button-secondary"
}: {
  onClose: () => void;
  children?: ReactNode;
  className?: string;
}) {
  return (
    <button
      type="button"
      className={className}
      onMouseDown={(event) => {
        event.preventDefault();
        event.stopPropagation();
      }}
      onClick={(event) => dismissClick(event, onClose)}
    >
      <svg viewBox="0 0 20 20" className="app-button-icon fill-none stroke-current" strokeWidth="1.8" aria-hidden="true">
        <path d="m6 6 8 8" />
        <path d="m14 6-8 8" />
      </svg>
      {children}
    </button>
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
  secondaryLabel,
  onSecondaryAction,
  secondaryHref,
  createLabel,
  onCreate,
  createHref,
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
  secondaryLabel?: string;
  onSecondaryAction?: () => void;
  secondaryHref?: string;
  createLabel?: string;
  onCreate?: () => void;
  createHref?: string;
  searchPlaceholder?: string;
}) {
  const [density, setDensity] = useCatalogDensity();

  return (
    <div className="app-card">
      <div className="app-card-body app-catalog-toolbar-shell">
        <h2 className="sr-only">{title}</h2>
        {description ? <p className="sr-only">{description}</p> : null}
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
          <button
            type="button"
            className={`app-density-toggle ${density === "dense" ? "app-density-toggle-active" : ""}`}
            aria-label={density === "dense" ? "Switch to comfortable density" : "Switch to dense density"}
            title={density === "dense" ? "Comfortable density" : "Dense density"}
            onClick={() => setDensity(density === "dense" ? "comfortable" : "dense")}
          >
            <svg viewBox="0 0 20 20" className="h-4 w-4 fill-none stroke-current" strokeWidth="1.7" aria-hidden="true">
              <path d="M4 5h12" />
              <path d="M4 10h12" />
              <path d="M4 15h12" />
            </svg>
            <span className="sr-only">{density === "dense" ? "Comfortable" : "Dense"}</span>
          </button>
            {secondaryLabel && secondaryHref ? (
              <Link to={secondaryHref} className="app-button-secondary app-toolbar-secondary-action inline-flex items-center gap-2">
                {secondaryLabel}
              </Link>
            ) : secondaryLabel && onSecondaryAction ? (
              <button type="button" className="app-button-secondary app-toolbar-secondary-action inline-flex items-center gap-2" onClick={onSecondaryAction}>
                {secondaryLabel}
              </button>
            ) : null}
            {createLabel && createHref ? (
              <Link to={createHref} className="app-button-primary app-toolbar-primary-action inline-flex items-center gap-2">
                <PlusIcon className="app-button-icon" />
                {createLabel}
              </Link>
            ) : createLabel && onCreate ? (
              <button type="button" className="app-button-primary app-toolbar-primary-action" onClick={onCreate}>
                <PlusIcon className="app-button-icon" />
                {createLabel}
              </button>
            ) : null}
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
    <button
      type="button"
      className="inline-flex items-center gap-1.5 text-[10px] font-semibold uppercase tracking-[0.04em] transition hover:text-white"
      onClick={onClick}
    >
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
      <td colSpan={colSpan} className="px-3 py-2">
        <div className="app-table-state">
          <div className="text-sm font-semibold text-ink">{title}</div>
          <div className="text-[11px] text-steel">{message}</div>
        </div>
      </td>
    </tr>
  );
}

export type RowActionItem = {
  label: string;
  onClick: () => void;
  tone?: "default" | "danger";
  disabled?: boolean;
  icon?: ReactNode;
};

export function RowActionsMenu({
  label = "More actions",
  items
}: {
  label?: string;
  items: RowActionItem[];
}) {
  const enabledItems = items.filter((item) => !item.disabled);
  const [open, setOpen] = useState(false);
  const triggerRef = useRef<HTMLButtonElement | null>(null);
  const menuRef = useRef<HTMLDivElement | null>(null);
  const [menuPosition, setMenuPosition] = useState<{ top: number; left: number } | null>(null);
  useEscapeDismiss(open, () => setOpen(false));

  useEffect(() => {
    if (!open) {
      return;
    }

    const updatePosition = () => {
      const rect = triggerRef.current?.getBoundingClientRect();
      if (!rect) {
        return;
      }

      const menuWidth = 188;
      const viewportPadding = 12;
      const left = Math.max(
        viewportPadding,
        Math.min(rect.right - menuWidth, window.innerWidth - menuWidth - viewportPadding)
      );

      setMenuPosition({
        top: rect.bottom + 8,
        left
      });
    };

    const handlePointerDown = (event: PointerEvent) => {
      const target = event.target as Node;
      if (triggerRef.current?.contains(target) || menuRef.current?.contains(target)) {
        return;
      }

      setOpen(false);
    };

    updatePosition();
    window.addEventListener("resize", updatePosition);
    window.addEventListener("scroll", updatePosition, true);
    window.addEventListener("pointerdown", handlePointerDown);

    return () => {
      window.removeEventListener("resize", updatePosition);
      window.removeEventListener("scroll", updatePosition, true);
      window.removeEventListener("pointerdown", handlePointerDown);
    };
  }, [open]);

  if (enabledItems.length === 0) {
    return null;
  }

  return (
    <div className="app-row-actions">
      <button
        type="button"
        ref={triggerRef}
        className="app-row-actions-trigger"
        aria-haspopup="menu"
        aria-expanded={open}
        aria-label={label}
        onMouseDown={(event) => {
          event.preventDefault();
          event.stopPropagation();
        }}
        onClick={(event) => {
          event.preventDefault();
          event.stopPropagation();
          setOpen((value) => !value);
        }}
      >
        <svg viewBox="0 0 20 20" className="h-4 w-4 fill-current" aria-hidden="true">
          <circle cx="5" cy="10" r="1.6" />
          <circle cx="10" cy="10" r="1.6" />
          <circle cx="15" cy="10" r="1.6" />
        </svg>
      </button>
      {open && menuPosition && typeof document !== "undefined"
        ? createPortal(
            <div
              ref={menuRef}
              className="app-row-actions-menu"
              role="menu"
              style={{ top: `${menuPosition.top}px`, left: `${menuPosition.left}px` }}
            >
              {enabledItems.map((item) => (
                <button
                  key={item.label}
                  type="button"
                  role="menuitem"
                  className={`app-row-actions-item ${item.tone === "danger" ? "app-row-actions-item-danger" : ""}`.trim()}
                  onMouseDown={(event) => {
                    event.preventDefault();
                    event.stopPropagation();
                  }}
                  onClick={(event) => {
                    event.preventDefault();
                    event.stopPropagation();
                    setOpen(false);
                    item.onClick();
                  }}
                >
                  {item.icon ? <span className="app-row-actions-item-icon">{item.icon}</span> : null}
                  {item.label}
                </button>
              ))}
            </div>,
            document.body
          )
        : null}
    </div>
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
        <button type="button" className="app-button-secondary inline-flex items-center gap-2" onClick={onClose}>
          <svg viewBox="0 0 20 20" className="app-button-icon fill-none stroke-current" strokeWidth="1.8" aria-hidden="true">
            <path d="m6 6 8 8" />
            <path d="m14 6-8 8" />
          </svg>
          Cancel
        </button>
        <button type="button" className={`${tone === "danger" ? "app-button-danger" : "app-button-primary"} inline-flex items-center gap-2`} onClick={onConfirm}>
          {tone === "danger" ? <TrashIcon className="app-button-icon" /> : <PencilIcon className="app-button-icon" />}
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
