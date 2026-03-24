import { createContext, useContext, useMemo, useState } from "react";

export type SupportedLocale = "en" | "cs" | "de" | "es" | "fr";

type LandingTranslations = {
  eyebrow: string;
  title: string;
  subtitle: string;
  capabilitiesTitle: string;
  capabilitiesIntro: string;
  capabilityRuntimeTitle: string;
  capabilityRuntimeBody: string;
  capabilityAccessTitle: string;
  capabilityAccessBody: string;
  capabilityAuditTitle: string;
  capabilityAuditBody: string;
  capabilityOperationsTitle: string;
  capabilityOperationsBody: string;
  signInEyebrow: string;
  signInTitle: string;
  signInBody: string;
  instructionsTitle: string;
  stepOneTitle: string;
  stepOneBody: string;
  stepTwoTitle: string;
  stepTwoBody: string;
  stepThreeTitle: string;
  stepThreeBody: string;
  securityNote: string;
  cta: string;
};

type CommonTranslations = {
  dashboard: string;
  navigation: string;
  signedInAs: string;
  session: string;
  sessionActive: string;
  sessionMissing: string;
  permissions: string;
  reauth: string;
  signOut: string;
  loading: string;
  current: string;
  proposed: string;
  field: string;
  total: string;
  applicable: string;
  rejected: string;
  warnings: string;
  version: string;
  expires: string;
  never: string;
  mode: string;
  state: string;
  interval: string;
  scrape: string;
  privateMode: string;
  publicMode: string;
  enabled: string;
  disabled: string;
  allowed: string;
  active: string;
  revoked: string;
  yes: string;
  no: string;
  ready: string;
  degraded: string;
  open: string;
  edit: string;
  readOnly: string;
  preview: string;
  apply: string;
  save: string;
  delete: string;
  selected: string;
  accessDenied: string;
  authorization: string;
  accessDeniedBody: string;
  sessionRole: string;
};

type RouteMetaTranslations = {
  overviewEyebrow: string;
  overviewTitle: string;
  overviewDescription: string;
  torrentsEyebrow: string;
  torrentsTitle: string;
  torrentsDescription: string;
  torrentPolicyEyebrow: string;
  torrentPolicyTitle: string;
  torrentPolicyDescription: string;
  bulkPolicyEyebrow: string;
  bulkPolicyTitle: string;
  bulkPolicyDescription: string;
  passkeysEyebrow: string;
  passkeysTitle: string;
  passkeysDescription: string;
  permissionsEyebrow: string;
  permissionsTitle: string;
  permissionsDescription: string;
  bansEyebrow: string;
  bansTitle: string;
  bansDescription: string;
  auditEyebrow: string;
  auditTitle: string;
  auditDescription: string;
};

type DashboardTranslations = {
  loadError: string;
  loading: string;
  readinessTitle: string;
  readinessReady: string;
  readinessNeedsAttention: string;
  postureTitle: string;
  postureDomains: string;
  postureProtected: string;
  operationsEyebrow: string;
  readinessMapTitle: string;
  lastHeartbeat: string;
  productEyebrow: string;
  whyTitle: string;
  whyBody: string;
  observedSnapshot: string;
  snapshotBody: string;
  bulletRuntime: string;
  bulletOperator: string;
  bulletConfig: string;
  capabilitiesTitle: string;
  capabilitiesEyebrow: string;
};

type TorrentsTranslations = {
  cardTitle: string;
  eyebrow: string;
  loadError: string;
  activateSelection: string;
  deactivateSelection: string;
  openBulkPolicy: string;
  selectedCount: string;
  lifecycleStatus: string;
  lifecycleErrorActivate: string;
  lifecycleErrorDeactivate: string;
  applied: string;
  failed: string;
  tableSelect: string;
  tableInfoHash: string;
  tableMode: string;
  tableState: string;
  tableInterval: string;
  tableNumwant: string;
  tableAction: string;
  openPolicy: string;
  empty: string;
};

type PolicyEditorTranslations = {
  loadError: string;
  loading: string;
  editTitle: string;
  editEyebrow: string;
  infoHash: string;
  announceInterval: string;
  minAnnounceInterval: string;
  defaultNumwant: string;
  maxNumwant: string;
  privateTracker: string;
  enabled: string;
  allowScrape: string;
  previewChanges: string;
  applyPolicy: string;
  previewGenerated: string;
  dryRunFailed: string;
  updated: string;
  saveFailed: string;
  previewTitle: string;
  previewEyebrow: string;
  currentSnapshot: string;
  dryRunResult: string;
  canApply: string;
  rejected: string;
  versionTarget: string;
  previewHint: string;
};

type BulkPolicyTranslations = {
  requiresSelection: string;
  formNotReady: string;
  dryRunStatus: string;
  dryRunError: string;
  applyStatus: string;
  applySuccessRedirect: string;
  applyError: string;
  loading: string;
  title: string;
  eyebrow: string;
  selectedInRollout: string;
  previewRollout: string;
  applyRollout: string;
  backToCatalog: string;
  previewTitle: string;
  previewEyebrow: string;
  previewHint: string;
};

type PasskeysTranslations = {
  loadError: string;
  cardTitle: string;
  eyebrow: string;
  tableMask: string;
  tableUser: string;
  tableState: string;
  tableExpires: string;
  tableVersion: string;
  empty: string;
  actionTitle: string;
  actionEyebrow: string;
  rawPasskeys: string;
  placeholder: string;
  expiryOverride: string;
  revoke: string;
  rotate: string;
  status: string;
  operationError: string;
  completed: string;
  failed: string;
  newPasskey: string;
};

type BansTranslations = {
  loadError: string;
  loadedToEditor: string;
  saveStatus: string;
  saveError: string;
  expiryRequired: string;
  expireStatus: string;
  expireError: string;
  deleteStatus: string;
  deleteError: string;
  cardTitle: string;
  eyebrow: string;
  expiresLabel: string;
  openRule: string;
  empty: string;
  editorTitle: string;
  editorEyebrow: string;
  scope: string;
  subject: string;
  reason: string;
  expiresAt: string;
  saveBan: string;
  expireBan: string;
  deleteBan: string;
};

type PermissionsTranslations = {
  loadError: string;
  loadedStatus: string;
  saveStatus: string;
  saveError: string;
  bulkStatus: string;
  bulkError: string;
  cardTitle: string;
  eyebrow: string;
  succeeded: string;
  failed: string;
  leech: string;
  seed: string;
  scrape: string;
  privateTracker: string;
  edit: string;
  empty: string;
  editorTitle: string;
  editorEyebrow: string;
  userId: string;
  canLeech: string;
  canSeed: string;
  canScrape: string;
  canUsePrivateTracker: string;
  savePermissions: string;
  applySelected: string;
};

type AuditTranslations = {
  loadError: string;
  title: string;
  eyebrow: string;
  tableAction: string;
  tableActor: string;
  tableRole: string;
  tableSeverity: string;
  entity: string;
  result: string;
  correlation: string;
  occurred: string;
  empty: string;
};

type DataGridTranslations = {
  searchPlaceholder: string;
  rowsPerPage: string;
  noResults: string;
  showingEntries: string;
};

type AuthTranslations = {
  bootstrappingTitle: string;
  bootstrappingBody: string;
  bootstrapFailedTitle: string;
  oidcManagerMissing: string;
  sessionErrorTitle: string;
  loadingSessionTitle: string;
  loadingSessionBody: string;
  callbackTitle: string;
  callbackBody: string;
  callbackError: string;
};

export type I18nDictionary = {
  localeLabel: string;
  landing: LandingTranslations;
  common: CommonTranslations;
  routes: RouteMetaTranslations;
  dashboard: DashboardTranslations;
  torrents: TorrentsTranslations;
  policyEditor: PolicyEditorTranslations;
  bulkPolicy: BulkPolicyTranslations;
  passkeys: PasskeysTranslations;
  bans: BansTranslations;
  permissionsPage: PermissionsTranslations;
  audit: AuditTranslations;
  dataGrid: DataGridTranslations;
  auth: AuthTranslations;
};

type I18nContextValue = {
  locale: SupportedLocale;
  setLocale: (locale: SupportedLocale) => void;
  dictionary: I18nDictionary;
};

const localeStorageKey = "beetracker.admin.locale";

const english: I18nDictionary = {
  localeLabel: "Language",
  landing: {
    eyebrow: "Admin panel",
    title: "Runtime core for tracker infrastructure.",
    subtitle: "Keep tracker traffic in the runtime core. Use the admin panel for access, policy, audit and maintenance.",
    capabilitiesTitle: "Core capabilities",
    capabilitiesIntro: "Built for teams that need fast tracker runtime and clear administration.",
    capabilityRuntimeTitle: "Announce and scrape runtime",
    capabilityRuntimeBody: "Handle announce, scrape, peer selection and live swarm state in one runtime layer.",
    capabilityAccessTitle: "Private tracker access",
    capabilityAccessBody: "Manage passkeys, permissions, bans and access rules for private tracker workflows.",
    capabilityAuditTitle: "Policy and audit trail",
    capabilityAuditBody: "Review policy changes, privileged actions and rollout history in one audit trail.",
    capabilityOperationsTitle: "Operations and maintenance",
    capabilityOperationsBody: "Apply tracker policy changes and maintenance tasks without touching runtime flow.",
    signInEyebrow: "Admin sign-in",
    signInTitle: "Sign in",
    signInBody: "Open the admin panel for access, policy and maintenance.",
    instructionsTitle: "Start here",
    stepOneTitle: "Sign in",
    stepOneBody: "Sign in with your admin account.",
    stepTwoTitle: "Choose a workflow",
    stepTwoBody: "Open the part of the tracker you need to manage.",
    stepThreeTitle: "Review sensitive changes",
    stepThreeBody: "Confirm sensitive changes when required.",
    securityNote: "Use an account with the required permissions.",
    cta: "Continue to admin panel"
  },
  common: {
    dashboard: "Dashboard",
    navigation: "Navigation",
    signedInAs: "Signed in as",
    session: "Session",
    sessionActive: "session active",
    sessionMissing: "session missing",
    permissions: "permissions",
    reauth: "Re-auth",
    signOut: "Sign out",
    loading: "Loading",
    current: "Current",
    proposed: "Proposed",
    field: "Field",
    total: "Total",
    applicable: "Applicable",
    rejected: "Rejected",
    warnings: "Warnings",
    version: "Version",
    expires: "Expires",
    never: "never",
    mode: "Mode",
    state: "State",
    interval: "Interval",
    scrape: "Scrape",
    privateMode: "private",
    publicMode: "public",
    enabled: "enabled",
    disabled: "disabled",
    allowed: "allowed",
    active: "active",
    revoked: "revoked",
    yes: "yes",
    no: "no",
    ready: "ready",
    degraded: "degraded",
    open: "Open",
    edit: "Edit",
    readOnly: "Read-only",
    preview: "Preview",
    apply: "Apply",
    save: "Save",
    delete: "Delete",
    selected: "selected",
    accessDenied: "Access denied",
    authorization: "Authorization",
    accessDeniedBody: "The current session does not have the permission required to open this workflow.",
    sessionRole: "Session"
  },
  routes: {
    overviewEyebrow: "Operations",
    overviewTitle: "Cluster overview",
    overviewDescription: "Monitor gateway readiness, access posture and the live state of the BeeTracker admin panel.",
    torrentsEyebrow: "Tracker Policy",
    torrentsTitle: "Torrent catalog",
    torrentsDescription: "Manage tracker mode, lifecycle state and rollout-safe policy changes for the torrents served by BeeTracker.",
    torrentPolicyEyebrow: "Policy Change",
    torrentPolicyTitle: "Torrent policy",
    torrentPolicyDescription: "Review the current tracker policy, preview the next state and apply an audited configuration change for one torrent.",
    bulkPolicyEyebrow: "Policy Rollout",
    bulkPolicyTitle: "Bulk torrent policy rollout",
    bulkPolicyDescription: "Preview, validate and apply shared tracker policy changes with dry-run results before the rollout reaches production.",
    passkeysEyebrow: "Private Access",
    passkeysTitle: "Passkey management",
    passkeysDescription: "Rotate and revoke private tracker credentials while keeping raw secrets out of the read side.",
    permissionsEyebrow: "Access Policy",
    permissionsTitle: "User permissions",
    permissionsDescription: "Control leech, seed, scrape and private tracker access through auditable admin workflows.",
    bansEyebrow: "Enforcement",
    bansTitle: "Ban rules",
    bansDescription: "Create, expire and remove enforcement rules with explicit scope, subject and audit visibility.",
    auditEyebrow: "Audit",
    auditTitle: "Audit trail",
    auditDescription: "Review privileged actions, session identity and correlation trails across BeeTracker."
  },
  dashboard: {
    loadError: "Unable to load the dashboard overview.",
    loading: "Loading dashboard health and gateway state...",
    readinessTitle: "Gateway readiness",
    readinessReady: "All active gateway nodes are reporting ready.",
    readinessNeedsAttention: "gateway node(s) currently require attention.",
    postureTitle: "Access posture",
    postureDomains: "domains",
    postureProtected: "privileged workflow(s) are protected by recent reauthentication.",
    operationsEyebrow: "Operations",
    readinessMapTitle: "Gateway readiness map",
    lastHeartbeat: "Last heartbeat recorded at",
    productEyebrow: "Product",
    whyTitle: "Why this admin panel exists",
    whyBody: "BeeTracker gives teams a dedicated surface for policy, access and operational control without dragging configuration, audit and reporting concerns into tracker runtime.",
    observedSnapshot: "Observed cluster snapshot",
    snapshotBody: "This page summarizes the current operational state across gateway and admin boundaries.",
    bulletRuntime: "Tracker runtime stays thin, explicit and isolated.",
    bulletOperator: "Administrative actions stay capability-aware, auditable and reversible where possible.",
    bulletConfig: "Configuration and coordination stay predictable across multi-node deployments.",
    capabilitiesTitle: "Granted capabilities",
    capabilitiesEyebrow: "Authorization"
  },
  torrents: {
    cardTitle: "Torrents",
    eyebrow: "Configuration",
    loadError: "Unable to load the tracker catalog.",
    activateSelection: "Activate selection",
    deactivateSelection: "Deactivate selection",
    openBulkPolicy: "Open bulk policy rollout",
    selectedCount: "torrent(s) selected",
    lifecycleStatus: "rollout completed: {succeeded} of {total} torrents updated successfully.",
    lifecycleErrorActivate: "Unable to activate torrents.",
    lifecycleErrorDeactivate: "Unable to deactivate torrents.",
    applied: "applied",
    failed: "failed",
    tableSelect: "Select",
    tableInfoHash: "Info hash",
    tableMode: "Mode",
    tableState: "State",
    tableInterval: "Interval",
    tableNumwant: "NumWant",
    tableAction: "Action",
    openPolicy: "Open policy",
    empty: "No tracker torrents are available in the current catalog view."
  },
  policyEditor: {
    loadError: "Unable to load the selected torrent policy.",
    loading: "Loading the current torrent policy snapshot...",
    editTitle: "Edit torrent policy",
    editEyebrow: "Tracker policy",
    infoHash: "Info hash",
    announceInterval: "Announce interval (s)",
    minAnnounceInterval: "Min announce interval (s)",
    defaultNumwant: "Default numwant",
    maxNumwant: "Max numwant",
    privateTracker: "Private tracker",
    enabled: "Enabled",
    allowScrape: "Allow scrape",
    previewChanges: "Preview changes",
    applyPolicy: "Apply policy",
    previewGenerated: "Preview generated. Review the proposed policy before applying it.",
    dryRunFailed: "Dry-run failed.",
    updated: "Torrent policy updated.",
    saveFailed: "Save failed.",
    previewTitle: "Preview and current state",
    previewEyebrow: "Dry-run",
    currentSnapshot: "Current snapshot",
    dryRunResult: "Dry-run result",
    canApply: "can apply",
    rejected: "rejected",
    versionTarget: "Version target",
    previewHint: "Run a dry-run first to inspect warnings, version changes and the exact proposed snapshot."
  },
  bulkPolicy: {
    requiresSelection: "Bulk policy rollout requires at least one selected torrent.",
    formNotReady: "Bulk torrent policy form is not ready.",
    dryRunStatus: "Dry-run completed: {applicable}/{total} items are currently applicable.",
    dryRunError: "Bulk policy dry-run failed.",
    applyStatus: "Bulk policy update completed: {succeeded}/{total} succeeded.",
    applySuccessRedirect: "Bulk torrent policy update succeeded for {count} torrents.",
    applyError: "Bulk policy apply failed.",
    loading: "Resolving the selected torrent set...",
    title: "Bulk torrent policy rollout",
    eyebrow: "Policy rollout",
    selectedInRollout: "Selected torrents in rollout",
    previewRollout: "Preview rollout",
    applyRollout: "Apply rollout",
    backToCatalog: "Back to catalog",
    previewTitle: "Rollout preview",
    previewEyebrow: "Server validation",
    previewHint: "Run a preview to inspect proposed snapshots, server warnings and rejected items before the rollout is applied."
  },
  passkeys: {
    loadError: "Unable to load private tracker passkeys.",
    cardTitle: "Passkeys",
    eyebrow: "Private access",
    tableMask: "Mask",
    tableUser: "User",
    tableState: "State",
    tableExpires: "Expires",
    tableVersion: "Version",
    empty: "No passkeys are currently available in this view.",
    actionTitle: "Passkey batch actions",
    actionEyebrow: "Privileged action",
    rawPasskeys: "Raw passkeys, one per line",
    placeholder: "bootstrap-passkey\nsecond-passkey",
    expiryOverride: "Optional expiry override for rotation",
    revoke: "Revoke passkeys",
    rotate: "Rotate passkeys",
    status: "{mode} operation completed: {succeeded} of {total} passkeys succeeded.",
    operationError: "Passkey batch operation failed.",
    completed: "completed",
    failed: "failed",
    newPasskey: "New passkey"
  },
  bans: {
    loadError: "Unable to load enforcement rules.",
    loadedToEditor: "Loaded {scope}/{subject} into the editor.",
    saveStatus: "Enforcement rule saved.",
    saveError: "Saving ban failed.",
    expiryRequired: "A concrete expiry timestamp is required to expire a ban.",
    expireStatus: "Enforcement rule expired.",
    expireError: "Expiring ban failed.",
    deleteStatus: "Enforcement rule deleted.",
    deleteError: "Deleting ban failed.",
    cardTitle: "Ban rules",
    eyebrow: "Enforcement",
    expiresLabel: "Expires",
    openRule: "Open rule",
    empty: "No enforcement rules are currently defined.",
    editorTitle: "Create or update ban",
    editorEyebrow: "Privileged mutation",
    scope: "Scope",
    subject: "Subject",
    reason: "Reason",
    expiresAt: "Expires at",
    saveBan: "Save ban",
    expireBan: "Expire ban",
    deleteBan: "Delete ban"
  },
  permissionsPage: {
    loadError: "Unable to load permissions.",
    loadedStatus: "Loaded permissions for {userId}.",
    saveStatus: "Permissions saved.",
    saveError: "Saving permissions failed.",
    bulkStatus: "Bulk permission update completed: {succeeded}/{total} succeeded.",
    bulkError: "Bulk permission update failed.",
    cardTitle: "Permissions",
    eyebrow: "Access policy",
    succeeded: "succeeded",
    failed: "failed",
    leech: "Leech",
    seed: "Seed",
    scrape: "Scrape",
    privateTracker: "Private",
    edit: "Edit",
    empty: "No user permissions were returned.",
    editorTitle: "Edit user permissions",
    editorEyebrow: "Privileged mutation",
    userId: "User ID",
    canLeech: "Can leech",
    canSeed: "Can seed",
    canScrape: "Can scrape",
    canUsePrivateTracker: "Can use private tracker",
    savePermissions: "Save permissions",
    applySelected: "Apply to selected"
  },
  audit: {
    loadError: "Unable to load audit records.",
    title: "Audit history",
    eyebrow: "Operational trace",
    tableAction: "Action",
    tableActor: "Actor",
    tableRole: "Role",
    tableSeverity: "Severity",
    entity: "Entity",
    result: "Result",
    correlation: "Correlation",
    occurred: "Occurred",
    empty: "No audit records were returned."
  },
  dataGrid: {
    searchPlaceholder: "Search...",
    rowsPerPage: "Rows per page",
    noResults: "No matching results.",
    showingEntries: "{from}\u2013{to} of {total}"
  },
  auth: {
    bootstrappingTitle: "Bootstrapping admin UI",
    bootstrappingBody: "Loading the admin client configuration and restoring the current session...",
    bootstrapFailedTitle: "Admin UI bootstrap failed",
    oidcManagerMissing: "The sign-in client manager could not be created.",
    sessionErrorTitle: "Admin session error",
    loadingSessionTitle: "Loading admin session",
    loadingSessionBody: "Resolving admin session and capabilities...",
    callbackTitle: "Completing sign-in",
    callbackBody: "Finalizing the admin session and restoring the dashboard...",
    callbackError: "Sign-in callback failed."
  }
};

function withLocale(localeLabel: string, landing: Partial<LandingTranslations>): I18nDictionary {
  return {
    ...english,
    localeLabel,
    landing: {
      ...english.landing,
      ...landing
    }
  };
}

const dictionaries: Record<SupportedLocale, I18nDictionary> = {
  en: english,
  cs: withLocale("Jazyk", {
    eyebrow: "Admin panel",
    title: "Runtime jádro pro tracker infrastrukturu.",
    subtitle: "Nechte tracker provoz v runtime jádru. Admin panel slouží pro přístup, policy, audit a údržbu.",
    capabilitiesTitle: "Hlavní možnosti",
    capabilitiesIntro: "Navržené pro týmy, které potřebují rychlý tracker runtime a jasnou správu.",
    capabilityRuntimeTitle: "Announce a scrape runtime",
    capabilityRuntimeBody:
      "Obsluhuje announce, scrape, výběr peerů a živý swarm stav v jedné runtime vrstvě.",
    capabilityAccessTitle: "Privátní přístup",
    capabilityAccessBody:
      "Spravuje passkeys, oprávnění, bany a pravidla přístupu pro private tracker workflow.",
    capabilityAuditTitle: "Policy a auditní stopa",
    capabilityAuditBody:
      "Udržuje změny policy, privilegované akce a historii rolloutů v jedné auditní stopě.",
    capabilityOperationsTitle: "Provoz a údržba",
    capabilityOperationsBody:
      "Umožňuje měnit tracker policy a spouštět maintenance bez zásahu do runtime toku.",
    signInEyebrow: "Přihlášení administrace",
    signInTitle: "Přihlášení",
    signInBody: "Otevřete admin panel pro přístup, policy a údržbu.",
    instructionsTitle: "Začněte zde",
    stepOneTitle: "Přihlaste se",
    stepOneBody: "Přihlaste se svým administrátorským účtem.",
    stepTwoTitle: "Vyberte oblast správy",
    stepTwoBody: "Otevřete část trackeru, kterou potřebujete spravovat.",
    stepThreeTitle: "Potvrďte citlivé změny",
    stepThreeBody: "Potvrďte citlivé změny, pokud je to potřeba.",
    securityNote: "Použijte účet s potřebnými oprávněními.",
    cta: "Pokračovat do admin panelu"
  }),
  de: withLocale("Sprache", {
    eyebrow: "Admin-Panel",
    title: "Runtime-Kern für Tracker-Infrastruktur.",
    subtitle:
      "Lassen Sie den Tracker-Verkehr im Runtime-Kern. Das Admin-Panel dient für Zugriff, Richtlinien, Audit und Wartung.",
    capabilitiesTitle: "Kernfunktionen",
    capabilitiesIntro:
      "Für Teams, die eine schnelle Tracker-Runtime und klare Verwaltung brauchen.",
    capabilityRuntimeTitle: "Announce- und Scrape-Runtime",
    capabilityRuntimeBody:
      "Bearbeitet Announce, Scrape, Peer-Auswahl und Live-Swarm-Status in einer Runtime-Schicht.",
    capabilityAccessTitle: "Privater Zugriff",
    capabilityAccessBody:
      "Verwaltet Passkeys, Berechtigungen, Sperren und Zugriffsregeln für private Tracker-Workflows.",
    capabilityAuditTitle: "Richtlinien und Audit-Spur",
    capabilityAuditBody:
      "Hält Richtlinienänderungen, privilegierte Aktionen und Rollout-Verlauf in einer Audit-Spur sichtbar.",
    capabilityOperationsTitle: "Betrieb und Wartung",
    capabilityOperationsBody:
      "Ändern Sie Tracker-Richtlinien und Wartungsaufgaben ohne Eingriff in den Runtime-Fluss.",
    signInEyebrow: "Admin-Anmeldung",
    signInTitle: "Anmelden",
    signInBody: "Öffnen Sie das Admin-Panel für Zugriff, Richtlinien und Wartung.",
    instructionsTitle: "So starten Sie",
    stepOneTitle: "Anmelden",
    stepOneBody: "Melden Sie sich mit Ihrem Admin-Konto an.",
    stepTwoTitle: "Workflow wählen",
    stepTwoBody: "Öffnen Sie den Teil des Trackers, den Sie verwalten möchten.",
    stepThreeTitle: "Sensible Änderungen prüfen",
    stepThreeBody: "Bestätigen Sie sensible Änderungen, wenn dies erforderlich ist.",
    securityNote: "Verwenden Sie ein Konto mit den erforderlichen Berechtigungen.",
    cta: "Zum Admin-Panel"
  }),
  es: withLocale("Idioma", {
    eyebrow: "Panel de administración",
    title: "Núcleo runtime para infraestructura tracker.",
    subtitle:
      "Mantén el tráfico del tracker en el núcleo runtime. El panel sirve para acceso, políticas, auditoría y mantenimiento.",
    capabilitiesTitle: "Capacidades principales",
    capabilitiesIntro:
      "Pensado para equipos que necesitan un runtime rápido y una administración clara.",
    capabilityRuntimeTitle: "Runtime de announce y scrape",
    capabilityRuntimeBody:
      "Procesa announce, scrape, selección de peers y estado vivo del swarm en una sola capa runtime.",
    capabilityAccessTitle: "Acceso privado",
    capabilityAccessBody:
      "Gestiona passkeys, permisos, bloqueos y reglas de acceso para flujos de tracker privado.",
    capabilityAuditTitle: "Política y rastro de auditoría",
    capabilityAuditBody:
      "Mantiene cambios de política, acciones privilegiadas e historial de despliegues en un mismo rastro de auditoría.",
    capabilityOperationsTitle: "Operación y mantenimiento",
    capabilityOperationsBody:
      "Aplica cambios de política y tareas de mantenimiento sin tocar el flujo runtime.",
    signInEyebrow: "Acceso administrativo",
    signInTitle: "Iniciar sesión",
    signInBody: "Abre el panel para acceso, políticas y mantenimiento.",
    instructionsTitle: "Empieza aquí",
    stepOneTitle: "Inicia sesión",
    stepOneBody: "Accede con tu cuenta de administración.",
    stepTwoTitle: "Elige un flujo",
    stepTwoBody: "Abre la parte del tracker que necesitas administrar.",
    stepThreeTitle: "Revisa cambios sensibles",
    stepThreeBody: "Confirma los cambios sensibles cuando sea necesario.",
    securityNote: "Usa una cuenta con los permisos requeridos.",
    cta: "Continuar al panel"
  }),
  fr: withLocale("Langue", {
    eyebrow: "Panneau d’administration",
    title: "Cœur runtime pour infrastructure tracker.",
    subtitle:
      "Gardez le trafic du tracker dans le cœur runtime. Le panneau sert à l’accès, aux règles, à l’audit et à la maintenance.",
    capabilitiesTitle: "Fonctions principales",
    capabilitiesIntro:
      "Conçu pour les équipes qui ont besoin d’un runtime rapide et d’une administration claire.",
    capabilityRuntimeTitle: "Runtime announce et scrape",
    capabilityRuntimeBody:
      "Traite announce, scrape, sélection des peers et état vivant du swarm dans une seule couche runtime.",
    capabilityAccessTitle: "Accès privé",
    capabilityAccessBody:
      "Gère passkeys, permissions, bannissements et règles d’accès pour les workflows de tracker privé.",
    capabilityAuditTitle: "Règles et piste d’audit",
    capabilityAuditBody:
      "Regroupe changements de règles, actions privilégiées et historique des déploiements dans une même piste d’audit.",
    capabilityOperationsTitle: "Exploitation et maintenance",
    capabilityOperationsBody:
      "Appliquez les changements de règles et les tâches de maintenance sans toucher au flux runtime.",
    signInEyebrow: "Connexion administrateur",
    signInTitle: "Connexion",
    signInBody: "Ouvrez le panneau pour l’accès, les règles et la maintenance.",
    instructionsTitle: "Commencer ici",
    stepOneTitle: "Connectez-vous",
    stepOneBody: "Connectez-vous avec votre compte administrateur.",
    stepTwoTitle: "Choisissez un workflow",
    stepTwoBody: "Ouvrez la partie du tracker que vous devez administrer.",
    stepThreeTitle: "Vérifiez les changements sensibles",
    stepThreeBody: "Confirmez les changements sensibles lorsque c’est nécessaire.",
    securityNote: "Utilisez un compte disposant des permissions requises.",
    cta: "Continuer vers le panneau"
  })
};
const I18nContext = createContext<I18nContextValue | null>(null);

function getInitialLocale(): SupportedLocale {
  if (typeof window === "undefined") {
    return "en";
  }

  const stored = window.localStorage.getItem(localeStorageKey);
  if (stored && stored in dictionaries) {
    return stored as SupportedLocale;
  }

  const browserLanguage = window.navigator.language.toLowerCase();
  if (browserLanguage.startsWith("cs")) return "cs";
  if (browserLanguage.startsWith("de")) return "de";
  if (browserLanguage.startsWith("es")) return "es";
  if (browserLanguage.startsWith("fr")) return "fr";
  return "en";
}

export function I18nProvider({ children }: { children: React.ReactNode }) {
  const [locale, setLocaleState] = useState<SupportedLocale>(getInitialLocale);

  const setLocale = (nextLocale: SupportedLocale) => {
    setLocaleState(nextLocale);
    if (typeof window !== "undefined") {
      window.localStorage.setItem(localeStorageKey, nextLocale);
    }
  };

  const value = useMemo<I18nContextValue>(() => ({
    locale,
    setLocale,
    dictionary: dictionaries[locale]
  }), [locale]);

  return <I18nContext.Provider value={value}>{children}</I18nContext.Provider>;
}

export function useI18n() {
  const context = useContext(I18nContext);
  if (!context) {
    throw new Error("useI18n must be used within I18nProvider.");
  }

  return context;
}

export const supportedLocales: Array<{ code: SupportedLocale; label: string }> = [
  { code: "en", label: "EN" },
  { code: "cs", label: "CZ" },
  { code: "de", label: "DE" },
  { code: "es", label: "ES" },
  { code: "fr", label: "FR" }
];

