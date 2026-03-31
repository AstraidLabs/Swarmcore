import { type ReactNode, useCallback, useEffect, useMemo, useRef, useState } from "react";
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
import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import { type I18nDictionary, supportedLocales, useI18n } from "./i18n";
import {
  AdminUserEditorPage,
  AdminUsersPage,
  PermissionGate,
  PermissionGroupEditorPage,
  PermissionGroupsPage,
  ProfilePage,
  RoleEditorPage,
  RolesPage,
  hasPermission,
  permissionKeys
} from "./rbac";
import { buildGridQueryString, type CatalogQueryState, type PageResult } from "./catalog";
import { CatalogTableRow, CatalogToolbar, ConfirmActionModal, CopyValueButton, EyeIcon, PaginationFooter, PencilIcon, PowerIcon, PreviewDrawer, RowActionsMenu, SettingsIcon, SortHeaderButton, TableStateRow, TrashIcon, useCatalogViewState } from "./catalog.tsx";

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
  id: string;
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

type TrackerAccessAdminDto = {
  userId: string;
  canLeech: boolean;
  canSeed: boolean;
  canScrape: boolean;
  canUsePrivateTracker: boolean;
  version: number;
};

type SwarmSummaryDto = {
  infoHash: string;
  seeders: number;
  leechers: number;
  downloaded: number;
};

type SwarmPeerDto = {
  peerId: string;
  ip: string;
  port: number;
  uploaded: number;
  downloaded: number;
  left: number;
  isSeeder: boolean;
};

type AggregatedSwarmListResultDto = {
  totalCount: number;
  items: SwarmSummaryDto[];
  respondedNodeCount: number;
  totalNodeCount: number;
  failedNodeIds: string[];
  observedAtUtc: string;
};

type AggregatedSwarmDetailDto = {
  infoHash: string;
  seeders: number;
  leechers: number;
  downloaded: number;
  peers: SwarmPeerDto[];
  contributingNodeIds: string[];
  failedNodeIds: string[];
  observedAtUtc: string;
};

type AggregatedSwarmCleanupResultDto = {
  infoHash: string;
  totalRemovedPeers: number;
  nodeResults: Array<{ nodeId: string; infoHash: string; removedPeers: number }>;
  failedNodeIds: string[];
  observedAtUtc: string;
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

type MaintenanceRunDto = {
  id: string;
  operation: string;
  requestedBy: string;
  requestedAtUtc: string;
  status: string;
  correlationId: string;
};

type TrackerNodeConfigViewDto = {
  overview: {
    nodeKey: string;
    version: number;
    nodeId: string;
    nodeName: string;
    environment: string;
    region: string;
    publicBaseUrl: string;
    internalBaseUrl: string;
    httpEnabled: boolean;
    scrapeEnabled: boolean;
    udpEnabled: boolean;
    publicTrackerEnabled: boolean;
    privateTrackerEnabled: boolean;
    ipv6Enabled: boolean;
    shardCount: number;
    compactResponsesByDefault: boolean;
    allowNonCompactResponses: boolean;
    requiresRestart: boolean;
    applyMode: string;
    updatedAtUtc: string;
    updatedBy: string;
  };
  protocol: {
    announceRoute: string;
    privateAnnounceRoute: string;
    scrapeRoute: string;
    httpAnnounceEnabled: boolean;
    httpScrapeEnabled: boolean;
    udpEnabled: boolean;
    udpBindAddress: string;
    udpPort: number;
    udpScrapeEnabled: boolean;
    defaultAnnounceIntervalSeconds: number;
    minAnnounceIntervalSeconds: number;
    defaultNumWant: number;
    maxNumWant: number;
    compactResponsesByDefault: boolean;
    allowNonCompactResponses: boolean;
    allowPasskeyInPath: boolean;
    allowPasskeyInQuery: boolean;
    allowClientIpOverride: boolean;
  };
  runtime: {
    shardCount: number;
    peerTtlSeconds: number;
    cleanupIntervalSeconds: number;
    maxPeersPerResponse: number;
    maxPeersPerSwarm?: number | null;
    preferLocalShardPeers: boolean;
    enableCompletedAccounting: boolean;
    enableIPv6Peers: boolean;
  };
  coordination: {
    redis: { enabled: boolean; summary: string; healthy: boolean };
    postgres: { enabled: boolean; summary: string; healthy: boolean };
    keyPrefix: string;
    invalidationChannel: string;
    migrateOnStart: boolean;
    persistTelemetry: boolean;
    persistAudit: boolean;
    telemetryBatchSize: number;
    telemetryFlushIntervalMilliseconds: number;
    heartbeatTtlSeconds: number;
    ownershipLeaseDurationSeconds: number;
    ownershipRefreshIntervalSeconds: number;
    swarmSummaryPublishIntervalSeconds: number;
    swarmSummaryTtlSeconds: number;
  };
  policy: {
    enablePublicTracker: boolean;
    enablePrivateTracker: boolean;
    requirePasskeyForPrivateTracker: boolean;
    allowPublicScrape: boolean;
    allowPrivateScrape: boolean;
    defaultTorrentVisibility: string;
    strictnessProfile: string;
    compatibilityMode: string;
  };
  observability: {
    enableHealthEndpoints: boolean;
    enableMetrics: boolean;
    enableTracing: boolean;
    enableDiagnosticsEndpoints: boolean;
    liveRoute: string;
    readyRoute: string;
    startupRoute: string;
  };
  abuse: {
    maxAnnounceQueryLength: number;
    maxScrapeQueryLength: number;
    maxQueryParameterCount: number;
    hardMaxNumWant: number;
    enableAnnouncePasskeyRateLimit: boolean;
    announcePerPasskeyPerSecond: number;
    enableAnnounceIpRateLimit: boolean;
    announcePerIpPerSecond: number;
    enableScrapeIpRateLimit: boolean;
    scrapePerIpPerSecond: number;
    rejectOversizedRequests: boolean;
    maxScrapeInfoHashes: number;
  };
  validation: {
    isValid: boolean;
    errors: string[];
    warnings: string[];
  };
};

type GovernanceStateDto = {
  announceDisabled: boolean;
  scrapeDisabled: boolean;
  globalMaintenanceMode: boolean;
  readOnlyMode: boolean;
  emergencyAbuseMitigation: boolean;
  udpDisabled: boolean;
  ipv6Frozen: boolean;
  policyFreezeMode: boolean;
  compatibilityMode: string;
  strictnessProfile: string;
};

type GovernanceUpdateRequest = {
  announceDisabled?: boolean | null;
  scrapeDisabled?: boolean | null;
  globalMaintenanceMode?: boolean | null;
  readOnlyMode?: boolean | null;
  emergencyAbuseMitigation?: boolean | null;
  udpDisabled?: boolean | null;
  ipv6Frozen?: boolean | null;
  policyFreezeMode?: boolean | null;
  compatibilityMode?: string | null;
  strictnessProfile?: string | null;
};

type NodeOperationalStateDto = {
  nodeId: string;
  state: number;
  updatedAtUtc: string;
};

type LiveStatsMessage = {
  nodeId: string;
  activePeers: number;
  activeSwarms: number;
  telemetryQueueLength: number;
  timestamp: string;
};

type NotificationOutboxItemDto = {
  id: string;
  recipient: string;
  subject: string;
  templateName?: string | null;
  createdAtUtc: string;
  scheduledAtUtc?: string | null;
  processedAtUtc?: string | null;
  status: string;
  retryCount: number;
  lastError?: string | null;
  correlationId?: string | null;
};

type NotificationOutboxDetailDto = NotificationOutboxItemDto & {
  attempts: Array<{
    id: string;
    attemptedAtUtc: string;
    succeeded: boolean;
    errorMessage?: string | null;
    smtpStatusCode?: number | null;
    durationMs: number;
  }>;
};

type NotificationOutboxStatsDto = {
  pendingCount: number;
  processingCount: number;
  sentCount: number;
  failedCount: number;
  cancelledCount: number;
  totalCount: number;
};

type AbuseDiagnosticsDto = {
  trackedIps: number;
  trackedPasskeys: number;
  warnedCount: number;
  softRestrictedCount: number;
  hardBlockedCount: number;
  topOffenders: AbuseDiagnosticsEntryDto[];
};

type AbuseDiagnosticsEntryDto = {
  key: string;
  keyType: string;
  malformedRequestCount: number;
  deniedPolicyCount: number;
  peerIdAnomalyCount: number;
  suspiciousPatternCount: number;
  scrapeAmplificationCount: number;
  totalScore: number;
  restrictionLevel: string;
};

type ClusterShardDiagnosticsDto = {
  observedAtUtc: string;
  totalShards: number;
  ownedShards: number;
  unownedShards: number;
  shards: Array<{
    shardId: number;
    ownerNodeId: string | null;
    locallyOwned: boolean;
    leaseExpiresAtUtc: string | null;
  }>;
};

type ClusterNodeStateDto = {
  nodeId: string;
  region: string;
  operationalState: string;
  ownedShardCount: number;
  heartbeatObservedAtUtc: string;
  heartbeatFresh: boolean;
};

type DashboardSummaryDto = {
  activeNodeCount: number;
  readyNodeCount: number;
  degradedNodeCount: number;
  totalOwnedShards: number;
  notificationStats: NotificationOutboxStatsDto;
  nodeStates: ClusterNodeStateDto[];
  observedAtUtc: string;
};

type TrackerNodeCatalogItemDto = {
  nodeKey: string;
  nodeName: string;
  nodeId: string;
  environment: string;
  region: string;
  httpEnabled: boolean;
  udpEnabled: boolean;
  publicTrackerEnabled: boolean;
  privateTrackerEnabled: boolean;
  requiresRestart: boolean;
  applyMode: string;
  updatedAtUtc: string;
  updatedBy: string;
  errorCount: number;
  warningCount: number;
};

type TrackerNodeConfigValidationResultDto = {
  isValid: boolean;
  issues: Array<{ code: string; path: string; severity: "Warning" | "Error"; message: string }>;
};

type TrackerNodeConfigFormState = {
  nodeKey: string;
  nodeId: string;
  nodeName: string;
  environment: string;
  region: string;
  publicBaseUrl: string;
  internalBaseUrl: string;
  supportsHttp: boolean;
  supportsUdp: boolean;
  supportsPrivateTracker: boolean;
  supportsPublicTracker: boolean;
  announceRoute: string;
  privateAnnounceRoute: string;
  scrapeRoute: string;
  httpAnnounceEnabled: boolean;
  httpScrapeEnabled: boolean;
  defaultAnnounceIntervalSeconds: number;
  minAnnounceIntervalSeconds: number;
  defaultNumWant: number;
  maxNumWant: number;
  compactResponsesByDefault: boolean;
  allowNonCompactResponses: boolean;
  allowPasskeyInPath: boolean;
  allowPasskeyInQuery: boolean;
  allowClientIpOverride: boolean;
  emitWarningMessages: boolean;
  udpEnabled: boolean;
  udpBindAddress: string;
  udpPort: number;
  udpConnectionTimeoutSeconds: number;
  udpReceiveBufferSize: number;
  udpMaxDatagramSize: number;
  udpScrapeEnabled: boolean;
  udpMaxScrapeInfoHashes: number;
  shardCount: number;
  peerTtlSeconds: number;
  cleanupIntervalSeconds: number;
  maxPeersPerResponse: number;
  maxPeersPerSwarm: number;
  preferLocalShardPeers: boolean;
  enableCompletedAccounting: boolean;
  enableIPv6Peers: boolean;
  enablePublicTracker: boolean;
  enablePrivateTracker: boolean;
  requirePasskeyForPrivateTracker: boolean;
  allowPublicScrape: boolean;
  allowPrivateScrape: boolean;
  defaultTorrentVisibility: string;
  strictnessProfile: string;
  compatibilityMode: string;
  redisEnabled: boolean;
  redisConnection: string;
  redisKeyPrefix: string;
  redisPolicyCacheTtlSeconds: number;
  redisSnapshotCacheTtlSeconds: number;
  redisInvalidationChannel: string;
  redisHeartbeatTtlSeconds: number;
  redisOwnershipLeaseDurationSeconds: number;
  redisOwnershipRefreshIntervalSeconds: number;
  redisSwarmSummaryPublishIntervalSeconds: number;
  redisSwarmSummaryTtlSeconds: number;
  postgresEnabled: boolean;
  postgresConnectionString: string;
  migrateOnStart: boolean;
  persistTelemetry: boolean;
  persistAudit: boolean;
  telemetryBatchSize: number;
  telemetryFlushIntervalMilliseconds: number;
  maxAnnounceQueryLength: number;
  maxScrapeQueryLength: number;
  maxQueryParameterCount: number;
  hardMaxNumWant: number;
  enableAnnouncePasskeyRateLimit: boolean;
  announcePerPasskeyPerSecond: number;
  enableAnnounceIpRateLimit: boolean;
  announcePerIpPerSecond: number;
  enableScrapeIpRateLimit: boolean;
  scrapePerIpPerSecond: number;
  rejectOversizedRequests: boolean;
  maxScrapeInfoHashes: number;
  enableHealthEndpoints: boolean;
  enableMetrics: boolean;
  enableTracing: boolean;
  enableDiagnosticsEndpoints: boolean;
  liveRoute: string;
  readyRoute: string;
  startupRoute: string;
  applyMode: "Dynamic" | "RestartRecommended" | "StartupOnly";
};

function normalizeTrackerNodeApplyMode(value: unknown): TrackerNodeConfigFormState["applyMode"] {
  if (value === 0 || value === "Dynamic") {
    return "Dynamic";
  }

  if (value === 2 || value === "StartupOnly") {
    return "StartupOnly";
  }

  return "RestartRecommended";
}

function createDefaultTrackerNodeConfigFormState(): TrackerNodeConfigFormState {
  return {
    nodeKey: "",
    nodeId: "",
    nodeName: "",
    environment: "Development",
    region: "local",
    publicBaseUrl: "http://localhost:18081",
    internalBaseUrl: "http://localhost:18081",
    supportsHttp: true,
    supportsUdp: true,
    supportsPrivateTracker: true,
    supportsPublicTracker: true,
    announceRoute: "/announce",
    privateAnnounceRoute: "/announce/private/{passkey}",
    scrapeRoute: "/scrape",
    httpAnnounceEnabled: true,
    httpScrapeEnabled: true,
    defaultAnnounceIntervalSeconds: 1800,
    minAnnounceIntervalSeconds: 900,
    defaultNumWant: 50,
    maxNumWant: 100,
    compactResponsesByDefault: true,
    allowNonCompactResponses: false,
    allowPasskeyInPath: true,
    allowPasskeyInQuery: false,
    allowClientIpOverride: false,
    emitWarningMessages: false,
    udpEnabled: true,
    udpBindAddress: "0.0.0.0",
    udpPort: 6969,
    udpConnectionTimeoutSeconds: 60,
    udpReceiveBufferSize: 65535,
    udpMaxDatagramSize: 65535,
    udpScrapeEnabled: true,
    udpMaxScrapeInfoHashes: 70,
    shardCount: 16,
    peerTtlSeconds: 2700,
    cleanupIntervalSeconds: 300,
    maxPeersPerResponse: 100,
    maxPeersPerSwarm: 5000,
    preferLocalShardPeers: true,
    enableCompletedAccounting: true,
    enableIPv6Peers: true,
    enablePublicTracker: true,
    enablePrivateTracker: true,
    requirePasskeyForPrivateTracker: true,
    allowPublicScrape: false,
    allowPrivateScrape: true,
    defaultTorrentVisibility: "private",
    strictnessProfile: "balanced",
    compatibilityMode: "standard",
    redisEnabled: true,
    redisConnection: "",
    redisKeyPrefix: "beetracker",
    redisPolicyCacheTtlSeconds: 60,
    redisSnapshotCacheTtlSeconds: 30,
    redisInvalidationChannel: "tracker:cache-invalidation",
    redisHeartbeatTtlSeconds: 30,
    redisOwnershipLeaseDurationSeconds: 45,
    redisOwnershipRefreshIntervalSeconds: 15,
    redisSwarmSummaryPublishIntervalSeconds: 30,
    redisSwarmSummaryTtlSeconds: 90,
    postgresEnabled: true,
    postgresConnectionString: "",
    migrateOnStart: false,
    persistTelemetry: true,
    persistAudit: true,
    telemetryBatchSize: 500,
    telemetryFlushIntervalMilliseconds: 5000,
    maxAnnounceQueryLength: 4096,
    maxScrapeQueryLength: 8192,
    maxQueryParameterCount: 32,
    hardMaxNumWant: 100,
    enableAnnouncePasskeyRateLimit: true,
    announcePerPasskeyPerSecond: 8,
    enableAnnounceIpRateLimit: true,
    announcePerIpPerSecond: 24,
    enableScrapeIpRateLimit: true,
    scrapePerIpPerSecond: 8,
    rejectOversizedRequests: true,
    maxScrapeInfoHashes: 70,
    enableHealthEndpoints: true,
    enableMetrics: true,
    enableTracing: true,
    enableDiagnosticsEndpoints: true,
    liveRoute: "/health/live",
    readyRoute: "/health/ready",
    startupRoute: "/health/startup",
    applyMode: "RestartRecommended"
  };
}

function toTrackerNodeConfigFormState(view: TrackerNodeConfigViewDto): TrackerNodeConfigFormState {
  return {
    nodeKey: view.overview.nodeKey,
    nodeId: view.overview.nodeId,
    nodeName: view.overview.nodeName,
    environment: view.overview.environment,
    region: view.overview.region,
    publicBaseUrl: view.overview.publicBaseUrl,
    internalBaseUrl: view.overview.internalBaseUrl,
    supportsHttp: view.capabilities.supportsHttp,
    supportsUdp: view.capabilities.supportsUdp,
    supportsPrivateTracker: view.capabilities.supportsPrivateTracker,
    supportsPublicTracker: view.capabilities.supportsPublicTracker,
    announceRoute: view.protocol.announceRoute,
    privateAnnounceRoute: view.protocol.privateAnnounceRoute,
    scrapeRoute: view.protocol.scrapeRoute,
    httpAnnounceEnabled: view.protocol.httpAnnounceEnabled,
    httpScrapeEnabled: view.protocol.httpScrapeEnabled,
    defaultAnnounceIntervalSeconds: view.protocol.defaultAnnounceIntervalSeconds,
    minAnnounceIntervalSeconds: view.protocol.minAnnounceIntervalSeconds,
    defaultNumWant: view.protocol.defaultNumWant,
    maxNumWant: view.protocol.maxNumWant,
    compactResponsesByDefault: view.protocol.compactResponsesByDefault,
    allowNonCompactResponses: view.protocol.allowNonCompactResponses,
    allowPasskeyInPath: view.protocol.allowPasskeyInPath,
    allowPasskeyInQuery: view.protocol.allowPasskeyInQuery,
    allowClientIpOverride: view.protocol.allowClientIpOverride,
    emitWarningMessages: false,
    udpEnabled: view.protocol.udpEnabled,
    udpBindAddress: view.protocol.udpBindAddress,
    udpPort: view.protocol.udpPort,
    udpConnectionTimeoutSeconds: 60,
    udpReceiveBufferSize: 65535,
    udpMaxDatagramSize: 65535,
    udpScrapeEnabled: view.protocol.udpScrapeEnabled,
    udpMaxScrapeInfoHashes: view.abuse.maxScrapeInfoHashes,
    shardCount: view.runtime.shardCount,
    peerTtlSeconds: view.runtime.peerTtlSeconds,
    cleanupIntervalSeconds: view.runtime.cleanupIntervalSeconds,
    maxPeersPerResponse: view.runtime.maxPeersPerResponse,
    maxPeersPerSwarm: view.runtime.maxPeersPerSwarm ?? 0,
    preferLocalShardPeers: view.runtime.preferLocalShardPeers,
    enableCompletedAccounting: view.runtime.enableCompletedAccounting,
    enableIPv6Peers: view.runtime.enableIPv6Peers,
    enablePublicTracker: view.policy.enablePublicTracker,
    enablePrivateTracker: view.policy.enablePrivateTracker,
    requirePasskeyForPrivateTracker: view.policy.requirePasskeyForPrivateTracker,
    allowPublicScrape: view.policy.allowPublicScrape,
    allowPrivateScrape: view.policy.allowPrivateScrape,
    defaultTorrentVisibility: view.policy.defaultTorrentVisibility,
    strictnessProfile: view.policy.strictnessProfile,
    compatibilityMode: view.policy.compatibilityMode,
    redisEnabled: view.coordination.redis.enabled,
    redisConnection: "",
    redisKeyPrefix: view.coordination.keyPrefix,
    redisPolicyCacheTtlSeconds: 60,
    redisSnapshotCacheTtlSeconds: 30,
    redisInvalidationChannel: view.coordination.invalidationChannel,
    redisHeartbeatTtlSeconds: view.coordination.heartbeatTtlSeconds,
    redisOwnershipLeaseDurationSeconds: view.coordination.ownershipLeaseDurationSeconds,
    redisOwnershipRefreshIntervalSeconds: view.coordination.ownershipRefreshIntervalSeconds,
    redisSwarmSummaryPublishIntervalSeconds: view.coordination.swarmSummaryPublishIntervalSeconds,
    redisSwarmSummaryTtlSeconds: view.coordination.swarmSummaryTtlSeconds,
    postgresEnabled: view.coordination.postgres.enabled,
    postgresConnectionString: "",
    migrateOnStart: view.coordination.migrateOnStart,
    persistTelemetry: view.coordination.persistTelemetry,
    persistAudit: view.coordination.persistAudit,
    telemetryBatchSize: view.coordination.telemetryBatchSize,
    telemetryFlushIntervalMilliseconds: view.coordination.telemetryFlushIntervalMilliseconds,
    maxAnnounceQueryLength: view.abuse.maxAnnounceQueryLength,
    maxScrapeQueryLength: view.abuse.maxScrapeQueryLength,
    maxQueryParameterCount: view.abuse.maxQueryParameterCount,
    hardMaxNumWant: view.abuse.hardMaxNumWant,
    enableAnnouncePasskeyRateLimit: view.abuse.enableAnnouncePasskeyRateLimit,
    announcePerPasskeyPerSecond: view.abuse.announcePerPasskeyPerSecond,
    enableAnnounceIpRateLimit: view.abuse.enableAnnounceIpRateLimit,
    announcePerIpPerSecond: view.abuse.announcePerIpPerSecond,
    enableScrapeIpRateLimit: view.abuse.enableScrapeIpRateLimit,
    scrapePerIpPerSecond: view.abuse.scrapePerIpPerSecond,
    rejectOversizedRequests: view.abuse.rejectOversizedRequests,
    maxScrapeInfoHashes: view.abuse.maxScrapeInfoHashes,
    enableHealthEndpoints: view.observability.enableHealthEndpoints,
    enableMetrics: view.observability.enableMetrics,
    enableTracing: view.observability.enableTracing,
    enableDiagnosticsEndpoints: view.observability.enableDiagnosticsEndpoints,
    liveRoute: view.observability.liveRoute,
    readyRoute: view.observability.readyRoute,
    startupRoute: view.observability.startupRoute,
    applyMode: normalizeTrackerNodeApplyMode(view.overview.applyMode)
  };
}

function toTrackerNodeConfigurationDocument(form: TrackerNodeConfigFormState) {
  return {
    identity: {
      nodeId: form.nodeId.trim(),
      nodeName: form.nodeName.trim(),
      environment: form.environment.trim(),
      region: form.region.trim(),
      publicBaseUrl: form.publicBaseUrl.trim(),
      internalBaseUrl: form.internalBaseUrl.trim(),
      supportsHttp: form.supportsHttp,
      supportsUdp: form.supportsUdp,
      supportsPrivateTracker: form.supportsPrivateTracker,
      supportsPublicTracker: form.supportsPublicTracker
    },
    http: {
      enableAnnounce: form.httpAnnounceEnabled,
      enableScrape: form.httpScrapeEnabled,
      announceRoute: form.announceRoute.trim(),
      privateAnnounceRoute: form.privateAnnounceRoute.trim(),
      scrapeRoute: form.scrapeRoute.trim(),
      defaultAnnounceIntervalSeconds: form.defaultAnnounceIntervalSeconds,
      minAnnounceIntervalSeconds: form.minAnnounceIntervalSeconds,
      defaultNumWant: form.defaultNumWant,
      maxNumWant: form.maxNumWant,
      compactResponsesByDefault: form.compactResponsesByDefault,
      allowNonCompactResponses: form.allowNonCompactResponses,
      allowPasskeyInPath: form.allowPasskeyInPath,
      allowPasskeyInQuery: form.allowPasskeyInQuery,
      allowClientIpOverride: form.allowClientIpOverride,
      emitWarningMessages: form.emitWarningMessages
    },
    udp: {
      enabled: form.udpEnabled,
      bindAddress: form.udpBindAddress.trim(),
      port: form.udpPort,
      connectionTimeoutSeconds: form.udpConnectionTimeoutSeconds,
      receiveBufferSize: form.udpReceiveBufferSize,
      maxDatagramSize: form.udpMaxDatagramSize,
      enableScrape: form.udpScrapeEnabled,
      maxScrapeInfoHashes: form.udpMaxScrapeInfoHashes
    },
    runtime: {
      shardCount: form.shardCount,
      peerTtlSeconds: form.peerTtlSeconds,
      cleanupIntervalSeconds: form.cleanupIntervalSeconds,
      maxPeersPerResponse: form.maxPeersPerResponse,
      maxPeersPerSwarm: form.maxPeersPerSwarm > 0 ? form.maxPeersPerSwarm : null,
      preferLocalShardPeers: form.preferLocalShardPeers,
      enableCompletedAccounting: form.enableCompletedAccounting,
      enableIPv6Peers: form.enableIPv6Peers
    },
    policy: {
      enablePublicTracker: form.enablePublicTracker,
      enablePrivateTracker: form.enablePrivateTracker,
      requirePasskeyForPrivateTracker: form.requirePasskeyForPrivateTracker,
      allowPublicScrape: form.allowPublicScrape,
      allowPrivateScrape: form.allowPrivateScrape,
      defaultTorrentVisibility: form.defaultTorrentVisibility.trim(),
      strictnessProfile: form.strictnessProfile.trim(),
      compatibilityMode: form.compatibilityMode.trim()
    },
    redis: {
      enabled: form.redisEnabled,
      configuration: form.redisConnection.trim(),
      keyPrefix: form.redisKeyPrefix.trim(),
      policyCacheTtlSeconds: form.redisPolicyCacheTtlSeconds,
      snapshotCacheTtlSeconds: form.redisSnapshotCacheTtlSeconds,
      invalidationChannel: form.redisInvalidationChannel.trim(),
      heartbeatTtlSeconds: form.redisHeartbeatTtlSeconds,
      ownershipLeaseDurationSeconds: form.redisOwnershipLeaseDurationSeconds,
      ownershipRefreshIntervalSeconds: form.redisOwnershipRefreshIntervalSeconds,
      swarmSummaryPublishIntervalSeconds: form.redisSwarmSummaryPublishIntervalSeconds,
      swarmSummaryTtlSeconds: form.redisSwarmSummaryTtlSeconds
    },
    postgres: {
      enabled: form.postgresEnabled,
      connectionString: form.postgresConnectionString.trim(),
      migrateOnStart: form.migrateOnStart,
      persistTelemetry: form.persistTelemetry,
      persistAudit: form.persistAudit,
      telemetryBatchSize: form.telemetryBatchSize,
      telemetryFlushIntervalMilliseconds: form.telemetryFlushIntervalMilliseconds
    },
    abuseProtection: {
      maxAnnounceQueryLength: form.maxAnnounceQueryLength,
      maxScrapeQueryLength: form.maxScrapeQueryLength,
      maxQueryParameterCount: form.maxQueryParameterCount,
      hardMaxNumWant: form.hardMaxNumWant,
      enableAnnouncePasskeyRateLimit: form.enableAnnouncePasskeyRateLimit,
      announcePerPasskeyPerSecond: form.announcePerPasskeyPerSecond,
      enableAnnounceIpRateLimit: form.enableAnnounceIpRateLimit,
      announcePerIpPerSecond: form.announcePerIpPerSecond,
      enableScrapeIpRateLimit: form.enableScrapeIpRateLimit,
      scrapePerIpPerSecond: form.scrapePerIpPerSecond,
      rejectOversizedRequests: form.rejectOversizedRequests,
      maxScrapeInfoHashes: form.maxScrapeInfoHashes
    },
    observability: {
      enableHealthEndpoints: form.enableHealthEndpoints,
      enableMetrics: form.enableMetrics,
      enableTracing: form.enableTracing,
      enableDiagnosticsEndpoints: form.enableDiagnosticsEndpoints,
      liveRoute: form.liveRoute.trim(),
      readyRoute: form.readyRoute.trim(),
      startupRoute: form.startupRoute.trim()
    }
  };
}

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
  permissionItems: BulkTrackerAccessOperationItemDto[];
  trackerAccessItems?: BulkTrackerAccessOperationItemDto[] | null;
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

type BulkTrackerAccessOperationItemDto = {
  userId: string;
  succeeded: boolean;
  errorCode?: string | null;
  errorMessage?: string | null;
  snapshot?: TrackerAccessAdminDto | null;
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

type PasskeyFormState = {
  passkey: string;
  userId: string;
  isRevoked: boolean;
  expiresAtLocal: string;
  expectedVersion?: number;
};

type BanFormState = {
  scope: string;
  subject: string;
  reason: string;
  expiresAtLocal: string;
  expectedVersion?: number;
};

type TrackerAccessFormState = {
  userId: string;
  canLeech: boolean;
  canSeed: boolean;
  canScrape: boolean;
  canUsePrivateTracker: boolean;
  expectedVersion?: number;
};

type PasskeyMutationPairDto = {
  revokedSnapshot: {
    id: string;
    passkey: string;
    userId: string;
    isRevoked: boolean;
    expiresAtUtc: string | null;
    version: number;
  };
  newSnapshot: {
    id: string;
    passkey: string;
    userId: string;
    isRevoked: boolean;
    expiresAtUtc: string | null;
    version: number;
  };
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

type NavigationLink = {
  to: string;
  label: string;
  description: string;
  icon: "overview" | "users" | "roles" | "groups" | "torrents" | "passkeys" | "trackerAccess" | "bans" | "audit" | "maintenance" | "nodeConfig";
};

type NavigationSection = {
  id: string;
  label: string;
  to: string;
  icon: NavigationLink["icon"];
  links: NavigationLink[];
};

const bulkTorrentPolicySelectionStorageKey = "beetracker.admin.bulkPolicySelection";
const bulkTrackerAccessSelectionStorageKey = "beetracker.admin.bulkTrackerAccessSelection";
const adminSignedOutStorageKey = "beetracker.admin.signedout";

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

function toBanRecordId(scope: string, subject: string) {
  return `${encodeURIComponent(scope)}::${encodeURIComponent(subject)}`;
}

function tryParseBanRecordId(value: string | null) {
  if (!value) {
    return null;
  }

  const separatorIndex = value.indexOf("::");
  if (separatorIndex <= 0 || separatorIndex >= value.length - 2) {
    return null;
  }

  try {
    return {
      scope: decodeURIComponent(value.slice(0, separatorIndex)),
      subject: decodeURIComponent(value.slice(separatorIndex + 2))
    };
  } catch {
    return null;
  }
}

function NavigationItemIcon({ icon }: { icon: NavigationLink["icon"] }) {
  switch (icon) {
    case "overview":
      return (
        <svg viewBox="0 0 24 24" className="h-4 w-4 fill-none stroke-current" strokeWidth="1.8">
          <path d="M3 11.5 12 4l9 7.5" />
          <path d="M5.5 10.5V20h13V10.5" />
        </svg>
      );
    case "users":
      return (
        <svg viewBox="0 0 24 24" className="h-4 w-4 fill-none stroke-current" strokeWidth="1.8">
          <path d="M16 19v-1a4 4 0 0 0-4-4H7a4 4 0 0 0-4 4v1" />
          <circle cx="9.5" cy="7.5" r="3.5" />
          <path d="M17 10.5a3 3 0 1 0 0-6" />
          <path d="M21 19v-1a4 4 0 0 0-3-3.87" />
        </svg>
      );
    case "roles":
      return (
        <svg viewBox="0 0 24 24" className="h-4 w-4 fill-none stroke-current" strokeWidth="1.8">
          <path d="m12 3 7 4v5c0 4.5-3 7.5-7 9-4-1.5-7-4.5-7-9V7l7-4Z" />
          <path d="m9.5 12 1.7 1.7 3.3-3.7" />
        </svg>
      );
    case "groups":
      return (
        <svg viewBox="0 0 24 24" className="h-4 w-4 fill-none stroke-current" strokeWidth="1.8">
          <rect x="3" y="5" width="7" height="6" rx="1.5" />
          <rect x="14" y="5" width="7" height="6" rx="1.5" />
          <rect x="8.5" y="14" width="7" height="6" rx="1.5" />
        </svg>
      );
    case "torrents":
      return (
        <svg viewBox="0 0 24 24" className="h-4 w-4 fill-none stroke-current" strokeWidth="1.8">
          <path d="M12 4v10" />
          <path d="m8 10 4 4 4-4" />
          <rect x="4" y="16" width="16" height="4" rx="2" />
        </svg>
      );
    case "passkeys":
      return (
        <svg viewBox="0 0 24 24" className="h-4 w-4 fill-none stroke-current" strokeWidth="1.8">
          <circle cx="8.5" cy="11.5" r="3.5" />
          <path d="M12 11.5h8" />
          <path d="M17 11.5v3" />
          <path d="M20 11.5v2" />
        </svg>
      );
    case "trackerAccess":
      return (
        <svg viewBox="0 0 24 24" className="h-4 w-4 fill-none stroke-current" strokeWidth="1.8">
          <path d="M4 12h16" />
          <path d="M12 4v16" />
          <circle cx="12" cy="12" r="8" />
        </svg>
      );
    case "bans":
      return (
        <svg viewBox="0 0 24 24" className="h-4 w-4 fill-none stroke-current" strokeWidth="1.8">
          <circle cx="12" cy="12" r="8" />
          <path d="m8.5 15.5 7-7" />
        </svg>
      );
    case "audit":
      return (
        <svg viewBox="0 0 24 24" className="h-4 w-4 fill-none stroke-current" strokeWidth="1.8">
          <path d="M8 6h11" />
          <path d="M8 12h11" />
          <path d="M8 18h11" />
          <path d="M4 6h.01" />
          <path d="M4 12h.01" />
          <path d="M4 18h.01" />
        </svg>
      );
    case "maintenance":
      return (
        <svg viewBox="0 0 24 24" className="h-4 w-4 fill-none stroke-current" strokeWidth="1.8">
          <circle cx="8" cy="16" r="2.5" />
          <circle cx="16" cy="8" r="2.5" />
          <path d="m9.8 14.2 4.4-4.4" />
          <path d="M6.5 10.5 4 8l4-4 2.5 2.5" />
          <path d="M13.5 19.5 16 22l4-4-2.5-2.5" />
        </svg>
      );
    case "nodeConfig":
      return <SettingsIcon className="h-4 w-4" />;
  }
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

  const signin = useCallback(
    async (forceFreshLogin = false) => {
      if (!manager) {
        return;
      }

      if (typeof window !== "undefined") {
        window.sessionStorage.removeItem(adminSignedOutStorageKey);
      }

      await manager.signinRedirect({
        state: {
          returnTo: `${location.pathname}${location.search}${location.hash}`
        },
        extraQueryParams: forceFreshLogin ? { prompt: "login" } : undefined
      });
    },
    [location.hash, location.pathname, location.search, manager]
  );

  const signout = useCallback(async () => {
    if (manager) {
      await manager.removeUser();
    }

    if (typeof window !== "undefined") {
      window.sessionStorage.setItem(adminSignedOutStorageKey, "1");
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
  }, [manager]);

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

async function apiDelete(path: string, accessToken: string, onReauthenticate: (fresh: boolean) => Promise<void>): Promise<void> {
  const response = await fetch(path, {
    method: "DELETE",
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

    throw new Error(errorPayload.message ?? "Admin mutation is forbidden.");
  }

  if (!response.ok) {
    const errorPayload = await readAdminError(response);
    throw new Error(errorPayload.message ?? `Admin mutation failed (${response.status}).`);
  }
}

function buildReturnTo(pathname: string, search: string) {
  return `${pathname}${search}`;
}

function sanitizeReturnTo(returnTo: string | null, fallback: string) {
  if (!returnTo || !returnTo.startsWith("/")) {
    return fallback;
  }

  return returnTo;
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

function toPasskeyForm(snapshot: PasskeyAdminDto): PasskeyFormState {
  return {
    passkey: "",
    userId: snapshot.userId,
    isRevoked: snapshot.isRevoked,
    expiresAtLocal: toLocalDateTimeInput(snapshot.expiresAtUtc),
    expectedVersion: snapshot.version
  };
}

function toTrackerAccessForm(snapshot: TrackerAccessAdminDto): TrackerAccessFormState {
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

function readBulkTrackerAccessSelection(): TrackerAccessAdminDto[] {
  if (typeof window === "undefined") {
    return [];
  }

  const raw = window.sessionStorage.getItem(bulkTrackerAccessSelectionStorageKey);
  if (!raw) {
    return [];
  }

  try {
    const parsed = JSON.parse(raw) as TrackerAccessAdminDto[];
    return Array.isArray(parsed) ? parsed : [];
  } catch {
    return [];
  }
}

function persistBulkTrackerAccessSelection(items: TrackerAccessAdminDto[]) {
  if (typeof window === "undefined") {
    return;
  }

  if (items.length === 0) {
    window.sessionStorage.removeItem(bulkTrackerAccessSelectionStorageKey);
    return;
  }

  window.sessionStorage.setItem(bulkTrackerAccessSelectionStorageKey, JSON.stringify(items));
}

function clearBulkTrackerAccessSelection() {
  if (typeof window === "undefined") {
    return;
  }

  window.sessionStorage.removeItem(bulkTrackerAccessSelectionStorageKey);
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
      ? "border border-moss/15 bg-moss/10 text-moss"
      : tone === "warn"
        ? "border border-rose-200 bg-rose-50 text-rose-700"
        : "border border-slate-200 bg-white text-steel";

  return <span className={`inline-flex items-center rounded-full px-3 py-1.5 text-[11px] font-semibold uppercase tracking-[0.18em] ${classes}`}>{children}</span>;
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

function getRouteMeta(pathname: string, dictionary: I18nDictionary): { eyebrow: string; title: string; description: string } {
  const routes = dictionary.routes;
  if (pathname.startsWith("/profile")) {
    return { eyebrow: "RBAC", title: "Admin profile", description: "Update your admin profile and inspect effective permissions issued into the current session." };
  }
  if (pathname.startsWith("/admin-users")) {
    return { eyebrow: "RBAC", title: "Admin users", description: "Provision admin accounts, assign roles, and enforce account state transitions with auditability." };
  }
  if (pathname.startsWith("/roles")) {
    return { eyebrow: "RBAC", title: "Roles", description: "Manage system and custom roles, attach permission groups, and review effective permissions." };
  }
  if (pathname.startsWith("/permission-groups")) {
    return { eyebrow: "RBAC", title: "Permission groups", description: "Bundle permissions into reusable groups and attach them to roles." };
  }
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
  if (pathname.startsWith("/maintenance")) {
    return {
      eyebrow: routes.maintenanceEyebrow,
      title: routes.maintenanceTitle,
      description: routes.maintenanceDescription
    };
  }
  if (pathname.startsWith("/abuse")) {
    return {
      eyebrow: "Security",
      title: "Abuse intelligence",
      description: "Monitor abuse scoring, restriction levels, and top offenders per gateway node."
    };
  }
  if (pathname.startsWith("/cluster")) {
    return {
      eyebrow: "Operations",
      title: "Cluster state",
      description: "Visualize node states, shard ownership distribution, and cluster health."
    };
  }
  if (pathname.startsWith("/notifications")) {
    return {
      eyebrow: "Operations",
      title: "Notification outbox",
      description: "Monitor email delivery status, retry failed sends, and cancel pending notifications."
    };
  }
  if (pathname.startsWith("/governance")) {
    return {
      eyebrow: "Operations",
      title: "Governance controls",
      description: "Toggle runtime governance flags and manage node lifecycle for individual gateway instances."
    };
  }
  if (pathname.startsWith("/tracker-node") || pathname.startsWith("/tracker-nodes")) {
    return {
      eyebrow: "Tracker",
      title: "Tracker node configurations",
      description: "Select, create and edit tracker node profiles with the same catalog workflow used across the admin."
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

function CapabilityAnyGate({
  capabilities,
  actions,
  children
}: {
  capabilities: CapabilityDto[];
  actions: string[];
  children: React.ReactNode;
}) {
  const { dictionary } = useI18n();
  if (!actions.some((action) => hasGrantedCapability(capabilities, action))) {
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
  const [summary, setSummary] = useState<DashboardSummaryDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [liveStats, setLiveStats] = useState<Map<string, LiveStatsMessage>>(new Map());
  const [liveConnected, setLiveConnected] = useState(false);

  useEffect(() => {
    let isMounted = true;

    Promise.all([
      apiRequest<ClusterOverviewDto>("/api/admin/cluster-overview", accessToken, onReauthenticate),
      apiRequest<DashboardSummaryDto>("/api/admin/dashboard/summary", accessToken, onReauthenticate)
    ])
      .then(([clusterOverview, dashboardSummary]) => {
        if (isMounted) {
          setOverview(clusterOverview);
          setSummary(dashboardSummary);
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

  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl("/hubs/live-stats", { accessTokenFactory: () => accessToken })
      .configureLogging(LogLevel.Warning)
      .withAutomaticReconnect()
      .build();

    connection.on("StatsUpdate", (message: string) => {
      try {
        const stats: LiveStatsMessage = JSON.parse(message);
        setLiveStats((prev) => {
          const next = new Map(prev);
          next.set(stats.nodeId, stats);
          return next;
        });
      } catch { /* ignore malformed messages */ }
    });

    connection.on("DashboardSummary", (message: string) => {
      try {
        const updated: DashboardSummaryDto = JSON.parse(message);
        setSummary(updated);
      } catch { /* ignore malformed messages */ }
    });

    connection.start()
      .then(() => {
        setLiveConnected(true);
        connection.invoke("JoinDashboard").catch(() => {});
      })
      .catch(() => setLiveConnected(false));

    connection.onreconnected(() => {
      setLiveConnected(true);
      connection.invoke("JoinDashboard").catch(() => {});
    });
    connection.onclose(() => setLiveConnected(false));

    return () => {
      connection.invoke("LeaveDashboard").catch(() => {});
      connection.stop();
    };
  }, [accessToken]);

  if (error) {
    return <Card title={dictionary.routes.overviewTitle}><p className="text-sm text-ember">{error}</p></Card>;
  }

  if (!overview) {
    return <Card title={dictionary.routes.overviewTitle}><p className="text-sm text-ink/60">{dashboard.loading}</p></Card>;
  }

  const readyNodes = overview.nodes.filter((node) => node.ready).length;
  const degradedNodes = overview.nodes.length - readyNodes;
  const grantedCapabilities = capabilities.filter((capability) => capability.granted);
  const readinessPercent = overview.activeNodeCount > 0
    ? Math.round((readyNodes / overview.activeNodeCount) * 100)
    : 0;
  const capabilityCategories = Array.from(new Set(grantedCapabilities.map((capability) => capability.category)));
  const totalPeers = Array.from(liveStats.values()).reduce((sum, s) => sum + s.activePeers, 0);
  const totalSwarms = Array.from(liveStats.values()).reduce((sum, s) => sum + s.activeSwarms, 0);
  const totalQueueLength = Array.from(liveStats.values()).reduce((sum, s) => sum + s.telemetryQueueLength, 0);

  return (
    <div className="space-y-6">
      {/* Primary stat cards */}
      <section className="grid gap-4 xl:grid-cols-4">
        <div className="app-stat-card">
          <p className="text-[11px] font-semibold uppercase tracking-[0.18em] text-steel/70">{dashboard.readinessTitle}</p>
          <div className="mt-4 flex items-end justify-between">
            <p className="app-stat-value">{readinessPercent}%</p>
            <span className="text-sm text-steel">{readyNodes}/{overview.activeNodeCount}</span>
          </div>
          <p className="mt-3 text-sm leading-6 text-steel">
            {degradedNodes > 0
              ? `${degradedNodes} ${dashboard.readinessNeedsAttention}`
              : dashboard.readinessReady}
          </p>
          <div className="app-stat-bar">
            <div className="app-stat-bar-fill bg-moss" style={{ width: `${readinessPercent}%` }} />
          </div>
        </div>
        <div className="app-stat-card">
          <p className="text-[11px] font-semibold uppercase tracking-[0.18em] text-steel/70">{dashboard.activePeersTitle}</p>
          <div className="mt-4 flex items-end justify-between">
            <p className="app-stat-value">{totalPeers.toLocaleString()}</p>
            <StatusPill tone={liveConnected ? "good" : "neutral"}>{liveConnected ? "Live" : "Offline"}</StatusPill>
          </div>
          <p className="mt-3 text-sm leading-6 text-steel">
            {dashboard.activePeersBody.replace("{count}", String(liveStats.size)).replace("{noun}", liveStats.size === 1 ? "node" : "nodes")}
          </p>
          <div className="app-stat-bar">
            <div className="app-stat-bar-fill bg-brand" style={{ width: `${liveStats.size > 0 ? 100 : 0}%` }} />
          </div>
        </div>
        <div className="app-stat-card">
          <p className="text-[11px] font-semibold uppercase tracking-[0.18em] text-steel/70">{dashboard.activeSwarmsTitle}</p>
          <div className="mt-4 flex items-end justify-between">
            <p className="app-stat-value">{totalSwarms.toLocaleString()}</p>
            <span className="text-sm text-steel">{liveStats.size} {liveStats.size === 1 ? "node" : "nodes"}</span>
          </div>
          <p className="mt-3 text-sm leading-6 text-steel">
            {dashboard.activeSwarmsBody}
          </p>
          <div className="app-stat-bar">
            <div className="app-stat-bar-fill bg-brand" style={{ width: `${liveStats.size > 0 ? 100 : 0}%` }} />
          </div>
        </div>
        <div className="app-stat-card">
          <p className="text-[11px] font-semibold uppercase tracking-[0.18em] text-steel/70">{dashboard.observedSnapshot}</p>
          <div className="mt-4 flex items-end justify-between">
            <p className="text-2xl font-bold tracking-tight text-ink">{new Date(overview.observedAtUtc).toLocaleTimeString()}</p>
            <StatusPill tone={degradedNodes > 0 ? "warn" : "good"}>
              {degradedNodes > 0 ? dictionary.common.degraded : dictionary.common.ready}
            </StatusPill>
          </div>
          <p className="mt-3 text-sm leading-6 text-steel">
            {new Date(overview.observedAtUtc).toLocaleDateString()} · {overview.nodes.length} {dashboard.snapshotNodesLabel}
          </p>
          <div className="mt-4 flex flex-wrap gap-2">
            {capabilityCategories.slice(0, 3).map((category) => (
              <span key={category} className="app-chip">{toTitleCase(category)}</span>
            ))}
          </div>
        </div>
      </section>

      {/* Tracker traffic summary widget */}
      <Card title={dashboard.trafficSummaryTitle} eyebrow={dashboard.trafficSummaryEyebrow}>
        <div className="grid gap-6 md:grid-cols-4">
          <div className="text-center">
            <p className="text-3xl font-bold tracking-tight text-ink">{totalPeers.toLocaleString()}</p>
            <p className="mt-1 text-[11px] font-semibold uppercase tracking-[0.18em] text-steel/70">{dashboard.trafficTotalPeers}</p>
          </div>
          <div className="text-center">
            <p className="text-3xl font-bold tracking-tight text-ink">{totalSwarms.toLocaleString()}</p>
            <p className="mt-1 text-[11px] font-semibold uppercase tracking-[0.18em] text-steel/70">{dashboard.trafficTotalSwarms}</p>
          </div>
          <div className="text-center">
            <p className="text-3xl font-bold tracking-tight text-ink">{totalQueueLength.toLocaleString()}</p>
            <p className="mt-1 text-[11px] font-semibold uppercase tracking-[0.18em] text-steel/70">{dashboard.trafficQueueDepth}</p>
          </div>
          <div className="text-center">
            <p className="text-3xl font-bold tracking-tight text-ink">{liveStats.size}</p>
            <p className="mt-1 text-[11px] font-semibold uppercase tracking-[0.18em] text-steel/70">{dashboard.trafficReportingNodes}</p>
          </div>
        </div>
        {liveStats.size > 0 && (
          <div className="mt-6 overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-slate-200 text-left text-[11px] font-semibold uppercase tracking-[0.18em] text-steel/70">
                  <th className="pb-3 pr-4">{dashboard.trafficNodeColumn}</th>
                  <th className="pb-3 pr-4 text-right">{dashboard.trafficPeersColumn}</th>
                  <th className="pb-3 pr-4 text-right">{dashboard.trafficSwarmsColumn}</th>
                  <th className="pb-3 pr-4 text-right">{dashboard.trafficQueueColumn}</th>
                  <th className="pb-3 text-right">{dashboard.trafficUpdatedColumn}</th>
                </tr>
              </thead>
              <tbody>
                {Array.from(liveStats.entries()).map(([nodeId, stats]) => (
                  <tr key={nodeId} className="border-b border-slate-100">
                    <td className="py-3 pr-4 font-medium text-ink">{nodeId}</td>
                    <td className="py-3 pr-4 text-right text-ink">{stats.activePeers.toLocaleString()}</td>
                    <td className="py-3 pr-4 text-right text-ink">{stats.activeSwarms.toLocaleString()}</td>
                    <td className="py-3 pr-4 text-right text-ink">{stats.telemetryQueueLength.toLocaleString()}</td>
                    <td className="py-3 text-right text-steel">{new Date(stats.timestamp).toLocaleTimeString()}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>

      {/* Notification health widget + Security summary widget */}
      <section className="grid gap-4 xl:grid-cols-2">
        {summary && (
          <Card title={dashboard.notificationHealthTitle} eyebrow={dashboard.notificationHealthEyebrow}>
            <div className="grid grid-cols-3 gap-4 sm:grid-cols-5">
              <div className="text-center">
                <p className="text-2xl font-bold text-ink">{summary.notificationStats.pendingCount}</p>
                <p className="mt-1 text-[10px] font-semibold uppercase tracking-[0.18em] text-steel/70">{dashboard.notifPending}</p>
              </div>
              <div className="text-center">
                <p className="text-2xl font-bold text-ink">{summary.notificationStats.processingCount}</p>
                <p className="mt-1 text-[10px] font-semibold uppercase tracking-[0.18em] text-steel/70">{dashboard.notifProcessing}</p>
              </div>
              <div className="text-center">
                <p className="text-2xl font-bold text-moss">{summary.notificationStats.sentCount}</p>
                <p className="mt-1 text-[10px] font-semibold uppercase tracking-[0.18em] text-steel/70">{dashboard.notifSent}</p>
              </div>
              <div className="text-center">
                <p className={`text-2xl font-bold ${summary.notificationStats.failedCount > 0 ? "text-rose-600" : "text-ink"}`}>
                  {summary.notificationStats.failedCount}
                </p>
                <p className="mt-1 text-[10px] font-semibold uppercase tracking-[0.18em] text-steel/70">{dashboard.notifFailed}</p>
              </div>
              <div className="text-center">
                <p className="text-2xl font-bold text-ink">{summary.notificationStats.cancelledCount}</p>
                <p className="mt-1 text-[10px] font-semibold uppercase tracking-[0.18em] text-steel/70">{dashboard.notifCancelled}</p>
              </div>
            </div>
            <div className="mt-5">
              <div className="flex items-center justify-between text-sm">
                <span className="text-steel">{dashboard.notifTotalLabel}</span>
                <span className="font-semibold text-ink">{summary.notificationStats.totalCount}</span>
              </div>
              {summary.notificationStats.totalCount > 0 && (
                <div className="mt-2 flex h-2.5 overflow-hidden rounded-full bg-slate-100">
                  <div className="bg-moss" style={{ width: `${(summary.notificationStats.sentCount / summary.notificationStats.totalCount) * 100}%` }} />
                  <div className="bg-amber-400" style={{ width: `${(summary.notificationStats.processingCount / summary.notificationStats.totalCount) * 100}%` }} />
                  <div className="bg-sky-400" style={{ width: `${(summary.notificationStats.pendingCount / summary.notificationStats.totalCount) * 100}%` }} />
                  <div className="bg-rose-500" style={{ width: `${(summary.notificationStats.failedCount / summary.notificationStats.totalCount) * 100}%` }} />
                </div>
              )}
            </div>
          </Card>
        )}

        {summary && (
          <Card title={dashboard.securitySummaryTitle} eyebrow={dashboard.securitySummaryEyebrow}>
            <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
              <div className="text-center">
                <p className="text-2xl font-bold text-ink">{summary.activeNodeCount}</p>
                <p className="mt-1 text-[10px] font-semibold uppercase tracking-[0.18em] text-steel/70">{dashboard.securityActiveNodes}</p>
              </div>
              <div className="text-center">
                <p className={`text-2xl font-bold ${summary.degradedNodeCount > 0 ? "text-rose-600" : "text-moss"}`}>
                  {summary.degradedNodeCount}
                </p>
                <p className="mt-1 text-[10px] font-semibold uppercase tracking-[0.18em] text-steel/70">{dashboard.securityDegradedNodes}</p>
              </div>
              <div className="text-center">
                <p className="text-2xl font-bold text-ink">{summary.totalOwnedShards}</p>
                <p className="mt-1 text-[10px] font-semibold uppercase tracking-[0.18em] text-steel/70">{dashboard.securityOwnedShards}</p>
              </div>
              <div className="text-center">
                <p className="text-2xl font-bold text-ink">
                  {summary.nodeStates.filter((n) => n.heartbeatFresh).length}/{summary.nodeStates.length}
                </p>
                <p className="mt-1 text-[10px] font-semibold uppercase tracking-[0.18em] text-steel/70">{dashboard.securityFreshHeartbeats}</p>
              </div>
            </div>
            {summary.nodeStates.length > 0 && (
              <div className="mt-5 space-y-2">
                {summary.nodeStates.map((node) => (
                  <div key={node.nodeId} className="flex items-center justify-between rounded-xl border border-slate-100 bg-slate-50/60 px-4 py-2.5">
                    <div className="flex items-center gap-3">
                      <span className={`inline-block h-2 w-2 rounded-full ${node.heartbeatFresh ? "bg-moss" : "bg-rose-500"}`} />
                      <span className="text-sm font-medium text-ink">{node.nodeId}</span>
                      <span className="text-xs text-steel">{node.region}</span>
                    </div>
                    <div className="flex items-center gap-3">
                      <span className="text-xs text-steel">{node.ownedShardCount} {dashboard.securityShardsLabel}</span>
                      <StatusPill tone={node.operationalState === "Active" ? "good" : "warn"}>
                        {node.operationalState}
                      </StatusPill>
                    </div>
                  </div>
                ))}
              </div>
            )}
          </Card>
        )}
      </section>

      {/* Cluster-wide aggregated stats widget */}
      {summary && (
        <Card title={dashboard.clusterStatsTitle} eyebrow={dashboard.clusterStatsEyebrow}>
          <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
            {summary.nodeStates.map((node) => {
              const nodeLiveStats = liveStats.get(node.nodeId);
              return (
                <div key={node.nodeId} className="rounded-2xl border border-slate-200 bg-slate-50/80 px-5 py-4">
                  <div className="flex items-start justify-between gap-4">
                    <div>
                      <p className="text-sm font-semibold text-ink">{node.nodeId}</p>
                      <p className="mt-1 text-xs uppercase tracking-[0.18em] text-steel/70">{node.region}</p>
                    </div>
                    <StatusPill tone={node.heartbeatFresh ? "good" : "warn"}>
                      {node.operationalState}
                    </StatusPill>
                  </div>
                  <div className="mt-4 grid grid-cols-3 gap-3 text-center">
                    <div>
                      <p className="text-lg font-bold text-ink">{nodeLiveStats ? nodeLiveStats.activePeers.toLocaleString() : "\u2014"}</p>
                      <p className="text-[10px] uppercase tracking-[0.18em] text-steel/70">{dashboard.clusterStatsPeers}</p>
                    </div>
                    <div>
                      <p className="text-lg font-bold text-ink">{nodeLiveStats ? nodeLiveStats.activeSwarms.toLocaleString() : "\u2014"}</p>
                      <p className="text-[10px] uppercase tracking-[0.18em] text-steel/70">{dashboard.clusterStatsSwarms}</p>
                    </div>
                    <div>
                      <p className="text-lg font-bold text-ink">{node.ownedShardCount}</p>
                      <p className="text-[10px] uppercase tracking-[0.18em] text-steel/70">{dashboard.clusterStatsShards}</p>
                    </div>
                  </div>
                  <p className="mt-3 text-xs text-steel">
                    {dashboard.lastHeartbeat} {new Date(node.heartbeatObservedAtUtc).toLocaleTimeString()}
                  </p>
                </div>
              );
            })}
          </div>
        </Card>
      )}

      {/* Readiness map (existing, retained) */}
      <Card title={dashboard.readinessMapTitle} eyebrow={dashboard.operationsEyebrow}>
        <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
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
    </div>
  );
}

// ─── Governance Controls Page ───────────────────────────────────────────────

function GovernancePage({
  accessToken,
  onReauthenticate,
  permissions
}: {
  accessToken: string;
  onReauthenticate: (fresh: boolean) => Promise<void>;
  permissions: string[];
}) {
  const [nodes, setNodes] = useState<TrackerNodeCatalogItemDto[]>([]);
  const [selectedNodeKey, setSelectedNodeKey] = useState<string | null>(null);
  const [governance, setGovernance] = useState<GovernanceStateDto | null>(null);
  const [nodeState, setNodeState] = useState<NodeOperationalStateDto | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [status, setStatus] = useState<string | null>(null);
  const canEdit = hasPermission(permissions, "admin.system_settings.edit");
  const canMaintenance = hasPermission(permissions, "admin.maintenance.execute");

  useEffect(() => {
    let isMounted = true;
    apiRequest<PageResult<TrackerNodeCatalogItemDto>>(
      "/api/admin/nodes?pageSize=100",
      accessToken,
      onReauthenticate
    )
      .then((page) => {
        if (isMounted) {
          setNodes(page.items);
          if (page.items.length > 0 && !selectedNodeKey) {
            setSelectedNodeKey(page.items[0].nodeKey);
          }
        }
      })
      .catch((err) => {
        if (isMounted) setError(err instanceof Error ? err.message : "Failed to load nodes.");
      });
    return () => { isMounted = false; };
  }, [accessToken, onReauthenticate]);

  useEffect(() => {
    if (!selectedNodeKey) return;
    let isMounted = true;
    setIsLoading(true);
    setGovernance(null);
    setNodeState(null);
    setError(null);

    const selectedNode = nodes.find((n) => n.nodeKey === selectedNodeKey);
    const nodeId = selectedNode?.nodeId ?? selectedNodeKey;

    Promise.all([
      apiRequest<GovernanceStateDto>(
        `/api/admin/gateway/${encodeURIComponent(selectedNodeKey)}/governance`,
        accessToken,
        onReauthenticate
      ).catch(() => null),
      apiRequest<NodeOperationalStateDto>(
        `/api/admin/gateway/${encodeURIComponent(selectedNodeKey)}/nodes/${encodeURIComponent(nodeId)}/state`,
        accessToken,
        onReauthenticate
      ).catch(() => null)
    ]).then(([gov, ns]) => {
      if (isMounted) {
        if (!gov) {
          setError("Unable to reach the gateway node. Verify the node is running and its internal URL is configured.");
        } else {
          setGovernance(gov);
        }
        setNodeState(ns);
        setIsLoading(false);
      }
    });

    return () => { isMounted = false; };
  }, [selectedNodeKey, accessToken, onReauthenticate, nodes]);

  const updateGovernance = async (patch: GovernanceUpdateRequest) => {
    if (!selectedNodeKey || !canEdit) return;
    setSaving(true);
    setStatus(null);
    try {
      const updated = await apiMutation<GovernanceStateDto, GovernanceUpdateRequest>(
        `/api/admin/gateway/${encodeURIComponent(selectedNodeKey)}/governance`,
        "POST",
        accessToken,
        patch,
        onReauthenticate
      );
      setGovernance(updated);
      setStatus("Governance state updated.");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Governance update failed.");
    } finally {
      setSaving(false);
    }
  };

  const changeNodeState = async (action: "drain" | "maintenance" | "activate") => {
    if (!selectedNodeKey || !canMaintenance) return;
    const selectedNode = nodes.find((n) => n.nodeKey === selectedNodeKey);
    const nodeId = selectedNode?.nodeId ?? selectedNodeKey;
    setSaving(true);
    setStatus(null);
    try {
      const result = await apiMutation<NodeOperationalStateDto, Record<string, never>>(
        `/api/admin/gateway/${encodeURIComponent(selectedNodeKey)}/nodes/${encodeURIComponent(nodeId)}/${action}`,
        "POST",
        accessToken,
        {},
        onReauthenticate
      );
      setNodeState(result);
      setStatus(`Node transitioned to ${["Active", "Draining", "Maintenance"][result.state] ?? "unknown"}.`);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Node state change failed.");
    } finally {
      setSaving(false);
    }
  };

  const nodeStateName = nodeState ? ["Active", "Draining", "Maintenance"][nodeState.state] ?? "Unknown" : null;
  const nodeStateTone = nodeState
    ? nodeState.state === 0 ? "good" : nodeState.state === 1 ? "warn" : "warn"
    : "neutral";

  return (
    <div className="app-page-stack">
      {status && (
        <div className="app-notice-success">
          <p>{status}</p>
        </div>
      )}
      {error && (
        <div className="app-notice-warn">
          <p>{error}</p>
        </div>
      )}

      <Card title="Target node" eyebrow="Gateway">
        <div className="flex flex-wrap items-end gap-4">
          <label className="flex flex-col gap-1.5 text-sm">
            <span className="font-medium text-steel">Node</span>
            <select
              className="app-input w-64"
              value={selectedNodeKey ?? ""}
              onChange={(e) => setSelectedNodeKey(e.target.value || null)}
            >
              {nodes.length === 0 && <option value="">No nodes configured</option>}
              {nodes.map((node) => (
                <option key={node.nodeKey} value={node.nodeKey}>
                  {node.nodeName} ({node.nodeKey}) &mdash; {node.region}
                </option>
              ))}
            </select>
          </label>
          {nodeState && (
            <div className="flex items-center gap-3">
              <StatusPill tone={nodeStateTone as "good" | "warn" | "neutral"}>{nodeStateName ?? "Unknown"}</StatusPill>
              <span className="text-xs text-steel">since {new Date(nodeState.updatedAtUtc).toLocaleString()}</span>
            </div>
          )}
        </div>
      </Card>

      {isLoading && selectedNodeKey && (
        <Card title="Loading"><p className="text-sm text-ink/60">Fetching governance state from gateway...</p></Card>
      )}

      {governance && (
        <Card title="Runtime governance flags" eyebrow="Controls">
          <div className="grid gap-4 md:grid-cols-2">
            <GovernanceToggle label="Announce disabled" description="Reject all announce requests." value={governance.announceDisabled} disabled={!canEdit || saving} onChange={(v) => updateGovernance({ announceDisabled: v })} />
            <GovernanceToggle label="Scrape disabled" description="Reject all scrape requests." value={governance.scrapeDisabled} disabled={!canEdit || saving} onChange={(v) => updateGovernance({ scrapeDisabled: v })} />
            <GovernanceToggle label="Global maintenance mode" description="Return maintenance response to all clients." value={governance.globalMaintenanceMode} disabled={!canEdit || saving} onChange={(v) => updateGovernance({ globalMaintenanceMode: v })} />
            <GovernanceToggle label="Read-only mode" description="Accept announce reads but reject state mutations." value={governance.readOnlyMode} disabled={!canEdit || saving} onChange={(v) => updateGovernance({ readOnlyMode: v })} />
            <GovernanceToggle label="Emergency abuse mitigation" description="Activate aggressive abuse scoring and blocking." value={governance.emergencyAbuseMitigation} disabled={!canEdit || saving} onChange={(v) => updateGovernance({ emergencyAbuseMitigation: v })} />
            <GovernanceToggle label="UDP disabled" description="Reject all UDP announce/scrape requests." value={governance.udpDisabled} disabled={!canEdit || saving} onChange={(v) => updateGovernance({ udpDisabled: v })} />
            <GovernanceToggle label="IPv6 frozen" description="Stop accepting new IPv6 peer registrations." value={governance.ipv6Frozen} disabled={!canEdit || saving} onChange={(v) => updateGovernance({ ipv6Frozen: v })} />
            <GovernanceToggle label="Policy freeze mode" description="Ignore runtime policy changes until unfrozen." value={governance.policyFreezeMode} disabled={!canEdit || saving} onChange={(v) => updateGovernance({ policyFreezeMode: v })} />
          </div>
          <div className="mt-6 grid gap-4 md:grid-cols-2">
            <label className="flex flex-col gap-1.5 text-sm">
              <span className="font-medium text-steel">Compatibility mode</span>
              <select
                className="app-input"
                value={governance.compatibilityMode}
                disabled={!canEdit || saving}
                onChange={(e) => updateGovernance({ compatibilityMode: e.target.value })}
              >
                <option value="Standard">Standard</option>
                <option value="Legacy">Legacy</option>
                <option value="Relaxed">Relaxed</option>
              </select>
            </label>
            <label className="flex flex-col gap-1.5 text-sm">
              <span className="font-medium text-steel">Strictness profile</span>
              <select
                className="app-input"
                value={governance.strictnessProfile}
                disabled={!canEdit || saving}
                onChange={(e) => updateGovernance({ strictnessProfile: e.target.value })}
              >
                <option value="Default">Default</option>
                <option value="Strict">Strict</option>
                <option value="Permissive">Permissive</option>
              </select>
            </label>
          </div>
        </Card>
      )}

      {canMaintenance && selectedNodeKey && !isLoading && (
        <Card title="Node lifecycle" eyebrow="Operations">
          <p className="mb-4 text-sm text-steel">
            Transition the selected node between operational states. Draining removes it from the load balancer pool; Maintenance signals planned outage.
          </p>
          <div className="flex flex-wrap gap-3">
            <button type="button" className="app-button-secondary" disabled={saving || nodeState?.state === 0} onClick={() => changeNodeState("activate")}>
              Activate
            </button>
            <button type="button" className="app-button-secondary" disabled={saving || nodeState?.state === 1} onClick={() => changeNodeState("drain")}>
              Drain
            </button>
            <button type="button" className="app-button-secondary" disabled={saving || nodeState?.state === 2} onClick={() => changeNodeState("maintenance")}>
              Maintenance
            </button>
          </div>
        </Card>
      )}
    </div>
  );
}

function GovernanceToggle({
  label,
  description,
  value,
  disabled,
  onChange
}: {
  label: string;
  description: string;
  value: boolean;
  disabled: boolean;
  onChange: (v: boolean) => void;
}) {
  return (
    <div className="flex items-start justify-between gap-4 rounded-2xl border border-slate-200 bg-slate-50/80 px-5 py-4">
      <div>
        <p className="text-sm font-semibold text-ink">{label}</p>
        <p className="mt-1 text-xs text-steel">{description}</p>
      </div>
      <button
        type="button"
        role="switch"
        aria-checked={value}
        disabled={disabled}
        onClick={() => onChange(!value)}
        className={`relative inline-flex h-6 w-11 shrink-0 cursor-pointer items-center rounded-full transition-colors focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand ${value ? "bg-brand" : "bg-slate-300"} ${disabled ? "opacity-50 cursor-not-allowed" : ""}`}
      >
        <span className={`inline-block h-4 w-4 rounded-full bg-white shadow-sm transition-transform ${value ? "translate-x-[22px]" : "translate-x-[3px]"}`} />
      </button>
    </div>
  );
}

// ─── Abuse Intelligence Page ────────────────────────────────────────────────

function AbusePage({
  accessToken,
  onReauthenticate
}: {
  accessToken: string;
  onReauthenticate: (fresh: boolean) => Promise<void>;
}) {
  const [nodes, setNodes] = useState<TrackerNodeCatalogItemDto[]>([]);
  const [selectedNodeKey, setSelectedNodeKey] = useState<string | null>(null);
  const [abuse, setAbuse] = useState<AbuseDiagnosticsDto | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let isMounted = true;
    apiRequest<PageResult<TrackerNodeCatalogItemDto>>(
      "/api/admin/nodes?pageSize=100",
      accessToken,
      onReauthenticate
    ).then((page) => {
      if (isMounted) {
        setNodes(page.items);
        if (page.items.length > 0 && !selectedNodeKey) {
          setSelectedNodeKey(page.items[0].nodeKey);
        }
      }
    }).catch((err) => {
      if (isMounted) setError(err instanceof Error ? err.message : "Failed to load nodes.");
    });
    return () => { isMounted = false; };
  }, [accessToken, onReauthenticate]);

  useEffect(() => {
    if (!selectedNodeKey) return;
    let isMounted = true;
    setIsLoading(true);
    setAbuse(null);
    setError(null);

    apiRequest<AbuseDiagnosticsDto>(
      `/api/admin/gateway/${encodeURIComponent(selectedNodeKey)}/abuse/diagnostics`,
      accessToken,
      onReauthenticate
    ).then((data) => {
      if (isMounted) { setAbuse(data); setIsLoading(false); }
    }).catch((err) => {
      if (isMounted) {
        setError(err instanceof Error ? err.message : "Unable to reach gateway node.");
        setIsLoading(false);
      }
    });

    return () => { isMounted = false; };
  }, [selectedNodeKey, accessToken, onReauthenticate]);

  const restrictionTone = (level: string): "good" | "warn" | "neutral" =>
    level === "None" ? "good" : level === "Warned" ? "neutral" : "warn";

  return (
    <div className="app-page-stack">
      {error && <div className="app-notice-warn"><p>{error}</p></div>}

      <Card title="Target node" eyebrow="Gateway">
        <label className="flex flex-col gap-1.5 text-sm">
          <span className="font-medium text-steel">Node</span>
          <select
            className="app-input w-64"
            value={selectedNodeKey ?? ""}
            onChange={(e) => setSelectedNodeKey(e.target.value || null)}
          >
            {nodes.length === 0 && <option value="">No nodes configured</option>}
            {nodes.map((node) => (
              <option key={node.nodeKey} value={node.nodeKey}>
                {node.nodeName} ({node.nodeKey}) &mdash; {node.region}
              </option>
            ))}
          </select>
        </label>
      </Card>

      {isLoading && selectedNodeKey && (
        <Card title="Loading"><p className="text-sm text-ink/60">Fetching abuse diagnostics from gateway...</p></Card>
      )}

      {abuse && (
        <>
          <section className="grid gap-4 md:grid-cols-5">
            {([
              ["Tracked IPs", abuse.trackedIps],
              ["Tracked Passkeys", abuse.trackedPasskeys],
              ["Warned", abuse.warnedCount],
              ["Soft restricted", abuse.softRestrictedCount],
              ["Hard blocked", abuse.hardBlockedCount]
            ] as const).map(([label, count]) => (
              <div key={label} className="app-stat-card">
                <p className="text-[11px] font-semibold uppercase tracking-[0.18em] text-steel/70">{label}</p>
                <p className="mt-2 text-2xl font-bold text-ink">{count.toLocaleString()}</p>
              </div>
            ))}
          </section>

          <Card title="Top offenders" eyebrow="Abuse Intelligence">
            {abuse.topOffenders.length === 0 ? (
              <p className="text-sm text-steel">No abuse activity detected on this node.</p>
            ) : (
              <div className="app-data-table">
                <div className="overflow-x-auto">
                  <table className="min-w-full text-sm">
                    <thead className="app-table-head">
                      <tr>
                        <th className="px-5 py-4">Key</th>
                        <th className="px-5 py-4">Type</th>
                        <th className="px-5 py-4">Score</th>
                        <th className="px-5 py-4">Malformed</th>
                        <th className="px-5 py-4">Policy denied</th>
                        <th className="px-5 py-4">PeerID anomaly</th>
                        <th className="px-5 py-4">Suspicious</th>
                        <th className="px-5 py-4">Scrape amp.</th>
                        <th className="px-5 py-4">Restriction</th>
                      </tr>
                    </thead>
                    <tbody>
                      {abuse.topOffenders.map((offender) => (
                        <tr key={offender.key} className="border-t border-slate-100 hover:bg-slate-50/60">
                          <td className="px-5 py-4 font-mono text-xs font-medium text-ink">{offender.key}</td>
                          <td className="px-5 py-4 text-steel">{offender.keyType}</td>
                          <td className="px-5 py-4 font-semibold text-ink">{offender.totalScore}</td>
                          <td className="px-5 py-4 text-steel">{offender.malformedRequestCount}</td>
                          <td className="px-5 py-4 text-steel">{offender.deniedPolicyCount}</td>
                          <td className="px-5 py-4 text-steel">{offender.peerIdAnomalyCount}</td>
                          <td className="px-5 py-4 text-steel">{offender.suspiciousPatternCount}</td>
                          <td className="px-5 py-4 text-steel">{offender.scrapeAmplificationCount}</td>
                          <td className="px-5 py-4">
                            <StatusPill tone={restrictionTone(offender.restrictionLevel)}>{offender.restrictionLevel}</StatusPill>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </div>
            )}
          </Card>
        </>
      )}
    </div>
  );
}

// ─── Cluster Visualization Page ─────────────────────────────────────────────

function ClusterPage({
  accessToken,
  onReauthenticate
}: {
  accessToken: string;
  onReauthenticate: (fresh: boolean) => Promise<void>;
}) {
  const [overview, setOverview] = useState<ClusterOverviewDto | null>(null);
  const [nodes, setNodes] = useState<ClusterNodeStateDto[]>([]);
  const [shards, setShards] = useState<ClusterShardDiagnosticsDto | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let isMounted = true;
    setIsLoading(true);

    Promise.all([
      apiRequest<ClusterOverviewDto>("/api/admin/cluster-overview", accessToken, onReauthenticate).catch(() => null),
      apiRequest<ClusterNodeStateDto[]>("/api/admin/cluster/nodes", accessToken, onReauthenticate).catch(() => []),
      apiRequest<ClusterShardDiagnosticsDto>("/api/admin/cluster/shards", accessToken, onReauthenticate).catch(() => null)
    ]).then(([ov, ns, sh]) => {
      if (isMounted) {
        if (ov) setOverview(ov);
        setNodes(ns ?? []);
        if (sh) setShards(sh);
        if (!ov && !sh) setError("Unable to load cluster data.");
        setIsLoading(false);
      }
    });

    return () => { isMounted = false; };
  }, [accessToken, onReauthenticate]);

  if (isLoading) {
    return <Card title="Cluster"><p className="text-sm text-ink/60">Loading cluster state...</p></Card>;
  }

  const shardOwnershipPercent = shards
    ? Math.round((shards.ownedShards / Math.max(shards.totalShards, 1)) * 100)
    : 0;

  return (
    <div className="app-page-stack">
      {error && <div className="app-notice-warn"><p>{error}</p></div>}

      {overview && (
        <section className="grid gap-4 md:grid-cols-3">
          <div className="app-stat-card">
            <p className="text-[11px] font-semibold uppercase tracking-[0.18em] text-steel/70">Active nodes</p>
            <div className="mt-4 flex items-end justify-between">
              <p className="app-stat-value">{overview.activeNodeCount}</p>
              <StatusPill tone={overview.nodes.every((n) => n.ready) ? "good" : "warn"}>
                {overview.nodes.every((n) => n.ready) ? "Healthy" : "Degraded"}
              </StatusPill>
            </div>
            <p className="mt-3 text-sm text-steel">
              {overview.nodes.filter((n) => n.ready).length} ready, {overview.nodes.filter((n) => !n.ready).length} degraded
            </p>
          </div>
          {shards && (
            <>
              <div className="app-stat-card">
                <p className="text-[11px] font-semibold uppercase tracking-[0.18em] text-steel/70">Shard ownership</p>
                <div className="mt-4 flex items-end justify-between">
                  <p className="app-stat-value">{shardOwnershipPercent}%</p>
                  <span className="text-sm text-steel">{shards.ownedShards}/{shards.totalShards}</span>
                </div>
                <div className="mt-3 app-stat-bar">
                  <div className="app-stat-bar-fill bg-brand" style={{ width: `${shardOwnershipPercent}%` }} />
                </div>
              </div>
              <div className="app-stat-card">
                <p className="text-[11px] font-semibold uppercase tracking-[0.18em] text-steel/70">Unowned shards</p>
                <div className="mt-4 flex items-end justify-between">
                  <p className="app-stat-value">{shards.unownedShards}</p>
                  <StatusPill tone={shards.unownedShards === 0 ? "good" : "warn"}>
                    {shards.unownedShards === 0 ? "Complete" : "Gaps"}
                  </StatusPill>
                </div>
                <p className="mt-3 text-sm text-steel">
                  Observed at {new Date(shards.observedAtUtc).toLocaleTimeString()}
                </p>
              </div>
            </>
          )}
        </section>
      )}

      {nodes.length > 0 && (
        <Card title="Node states" eyebrow="Cluster">
          <div className="app-data-table">
            <div className="overflow-x-auto">
              <table className="min-w-full text-sm">
                <thead className="app-table-head">
                  <tr>
                    <th className="px-5 py-4">Node ID</th>
                    <th className="px-5 py-4">Region</th>
                    <th className="px-5 py-4">State</th>
                    <th className="px-5 py-4">Owned shards</th>
                    <th className="px-5 py-4">Last heartbeat</th>
                    <th className="px-5 py-4">Fresh</th>
                  </tr>
                </thead>
                <tbody>
                  {nodes.map((node) => (
                    <tr key={node.nodeId} className="border-t border-slate-100 hover:bg-slate-50/60">
                      <td className="px-5 py-4 font-medium text-ink">{node.nodeId}</td>
                      <td className="px-5 py-4 text-steel">{node.region}</td>
                      <td className="px-5 py-4">
                        <StatusPill tone={node.operationalState === "Active" ? "good" : "warn"}>{node.operationalState}</StatusPill>
                      </td>
                      <td className="px-5 py-4 text-steel">{node.ownedShardCount}</td>
                      <td className="px-5 py-4 text-steel">{new Date(node.heartbeatObservedAtUtc).toLocaleTimeString()}</td>
                      <td className="px-5 py-4">
                        <StatusPill tone={node.heartbeatFresh ? "good" : "warn"}>{node.heartbeatFresh ? "Yes" : "Stale"}</StatusPill>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        </Card>
      )}

      {shards && shards.shards.length > 0 && (
        <Card title="Shard ownership map" eyebrow="Distribution">
          <div className="flex flex-wrap gap-1">
            {shards.shards.map((shard) => (
              <div
                key={shard.shardId}
                title={`Shard ${shard.shardId}: ${shard.ownerNodeId ?? "unowned"}`}
                className={`h-3 w-3 rounded-sm ${shard.ownerNodeId ? (shard.locallyOwned ? "bg-brand" : "bg-moss/60") : "bg-slate-300"}`}
              />
            ))}
          </div>
          <div className="mt-4 flex gap-6 text-xs text-steel">
            <span className="flex items-center gap-1.5"><span className="inline-block h-3 w-3 rounded-sm bg-brand" /> Local</span>
            <span className="flex items-center gap-1.5"><span className="inline-block h-3 w-3 rounded-sm bg-moss/60" /> Remote</span>
            <span className="flex items-center gap-1.5"><span className="inline-block h-3 w-3 rounded-sm bg-slate-300" /> Unowned</span>
          </div>
        </Card>
      )}
    </div>
  );
}

function TrackerNodesPage({
  accessToken,
  onReauthenticate,
  permissions
}: {
  accessToken: string;
  onReauthenticate: (fresh: boolean) => Promise<void>;
  permissions: string[];
}) {
  const navigate = useNavigate();
  const location = useLocation();
  const canWrite = hasPermission(permissions, "admin.maintenance.execute");
  const [view, setView] = useCatalogViewState({
    search: "",
    filter: "all",
    sort: "nodekey:asc",
    page: 1,
    pageSize: 25
  });
  const { query, preview } = view;
  const [items, setItems] = useState<TrackerNodeCatalogItemDto[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [status, setStatus] = useState<string | null>(null);
  const previewItem = items.find((item) => item.nodeKey === preview) ?? null;
  const activeSortField = query.sort.split(":")[0] ?? "nodekey";
  const pageCount = Math.max(1, Math.ceil(totalCount / query.pageSize));
  const returnTo = encodeURIComponent(buildReturnTo(location.pathname, location.search));

  useEffect(() => {
    const banner = location.state as NavigationBannerState | null;
    if (!banner?.message) {
      return;
    }

    setStatus(banner.message);
    navigate(location.pathname, { replace: true, state: null });
  }, [location.pathname, location.state, navigate]);

  const reload = async () => {
    const page = await apiRequest<PageResult<TrackerNodeCatalogItemDto>>(
      `/api/admin/nodes?${buildGridQueryString(query)}`,
      accessToken,
      onReauthenticate
    );
    setItems(page.items);
    setTotalCount(page.totalCount);
  };

  useEffect(() => {
    let isMounted = true;

    setIsLoading(true);
    reload()
      .then(() => {
        if (isMounted) {
          setError(null);
        }
      })
      .catch((requestError) => {
        if (isMounted) {
          setError(requestError instanceof Error ? requestError.message : "Unable to load tracker node configurations.");
          setItems([]);
          setTotalCount(0);
        }
      })
      .finally(() => {
        if (isMounted) {
          setIsLoading(false);
        }
      });

    return () => {
      isMounted = false;
    };
  }, [accessToken, onReauthenticate, query]);

  useEffect(() => {
    if (preview && !items.some((item) => item.nodeKey === preview)) {
      setView((current) => ({ ...current, preview: null }));
    }
  }, [items, preview, setView]);

  const toggleSort = (field: string, defaultDirection: "asc" | "desc" = "asc") => {
    setView((current) => {
      const [currentField, currentDirection = "asc"] = current.query.sort.split(":");
      const nextDirection =
        currentField === field ? (currentDirection === "asc" ? "desc" : "asc") : defaultDirection;

      return {
        ...current,
        query: {
          ...current.query,
          sort: `${field}:${nextDirection}`,
          page: 1
        }
      };
    });
  };

  const formatProtocols = (item: TrackerNodeCatalogItemDto) => {
    const protocols = [item.httpEnabled ? "HTTP" : null, item.udpEnabled ? "UDP" : null].filter(
      (value): value is string => value !== null
    );
    return protocols.length > 0 ? protocols.join(" / ") : "Disabled";
  };

  const formatPolicy = (item: TrackerNodeCatalogItemDto) => {
    const modes = [item.publicTrackerEnabled ? "Public" : null, item.privateTrackerEnabled ? "Private" : null].filter(
      (value): value is string => value !== null
    );
    return modes.length > 0 ? modes.join(" / ") : "Disabled";
  };

  return (
    <div className="app-page-stack">
      <CatalogToolbar
        title="Node configurations"
        description="Select a tracker node profile, review effective status and open the dedicated configuration editor."
        totalCount={totalCount}
        search={query.search}
        onSearchChange={(value) => setView((current) => ({ ...current, query: { ...current.query, search: value, page: 1 } }))}
        searchPlaceholder="Search node key, name or region"
        filter={query.filter}
        onFilterChange={(value) => setView((current) => ({ ...current, query: { ...current.query, filter: value, page: 1 } }))}
        filterOptions={[{ value: "all", label: "All nodes" }]}
        sortValue={query.sort}
        onSortChange={(value) => setView((current) => ({ ...current, query: { ...current.query, sort: value, page: 1 } }))}
        sortOptions={[
          { value: "nodekey:asc", label: "Node key A-Z" },
          { value: "nodename:asc", label: "Node name A-Z" },
          { value: "environment:asc", label: "Environment A-Z" },
          { value: "region:asc", label: "Region A-Z" },
          { value: "updated:desc", label: "Updated newest" }
        ]}
        pageSize={query.pageSize}
        onPageSizeChange={(value) => setView((current) => ({ ...current, query: { ...current.query, pageSize: value, page: 1 } }))}
        createLabel="Create node config"
        createHref={canWrite ? `/tracker-nodes/new?returnTo=${returnTo}` : undefined}
      />

      {status ? <div className="app-notice-success">{status}</div> : null}
      {error ? <div className="app-notice-warn">{error}</div> : null}

      <div className="app-data-table">
        <div className="overflow-x-auto">
          <table className="min-w-full text-sm">
            <thead className="app-table-head">
              <tr>
                <th className="px-5 py-4">
                  <SortHeaderButton
                    label="Node"
                    active={activeSortField === "nodekey" || activeSortField === "nodename"}
                    direction={query.sort.endsWith(":desc") ? "desc" : "asc"}
                    onClick={() => toggleSort("nodekey")}
                  />
                </th>
                <th className="px-5 py-4">
                  <SortHeaderButton
                    label="Environment"
                    active={activeSortField === "environment" || activeSortField === "region"}
                    direction={query.sort.endsWith(":desc") ? "desc" : "asc"}
                    onClick={() => toggleSort("environment")}
                  />
                </th>
                <th className="px-5 py-4">Protocols</th>
                <th className="px-5 py-4">Policy</th>
                <th className="px-5 py-4">Apply</th>
                <th className="px-5 py-4">
                  <SortHeaderButton
                    label="Updated"
                    active={activeSortField === "updated"}
                    direction={query.sort.endsWith(":desc") ? "desc" : "asc"}
                    onClick={() => toggleSort("updated", "desc")}
                  />
                </th>
                <th className="px-5 py-4 text-right">Actions</th>
              </tr>
            </thead>
            <tbody>
              {isLoading ? (
                <TableStateRow
                  colSpan={7}
                  title="Loading node configurations"
                  message="Refreshing persisted tracker node profiles from the backend."
                />
              ) : items.length === 0 ? (
                <TableStateRow
                  colSpan={7}
                  title="No node configurations"
                  message="Create the first tracker node profile to start configuring protocols, runtime and coordination."
                />
              ) : (
                items.map((item) => (
                  <CatalogTableRow
                    key={item.nodeKey}
                    onOpen={() => setView((current) => ({ ...current, preview: item.nodeKey }))}
                  >
                    <td className="px-5 py-4">
                      <div className="app-grid-primary">{item.nodeName || item.nodeKey}</div>
                      <div className="app-grid-secondary">{item.environment} / {item.region}</div>
                      <div className="app-grid-meta inline-flex items-center gap-2">
                        <span className="font-mono">{item.nodeKey}</span>
                        <CopyValueButton value={item.nodeKey} label="Copy node key" />
                      </div>
                    </td>
                    <td className="px-5 py-4">
                      <div className="app-grid-primary">{item.environment}</div>
                      <div className="app-grid-secondary">{item.region}</div>
                      <div className="app-grid-meta">{item.nodeId || "Node id not configured"}</div>
                    </td>
                    <td className="px-5 py-4">
                      <div className="app-grid-primary">{formatProtocols(item)}</div>
                      <div className="app-grid-meta">{item.httpEnabled ? "HTTP enabled" : "HTTP disabled"}</div>
                    </td>
                    <td className="px-5 py-4">
                      <div className="app-grid-primary">{formatPolicy(item)}</div>
                      <div className="app-grid-meta">
                        {item.privateTrackerEnabled ? "Private tracker ready" : "Private tracker disabled"}
                      </div>
                    </td>
                    <td className="px-5 py-4">
                      <span className={item.requiresRestart ? "app-chip-warn" : "app-chip-soft"}>
                        {item.requiresRestart ? "Restart recommended" : "Dynamic"}
                      </span>
                      <div className="app-grid-meta mt-2">{item.applyMode}</div>
                    </td>
                    <td className="px-5 py-4">
                      <div className="app-grid-primary">{formatDateTime(item.updatedAtUtc)}</div>
                      <div className="app-grid-meta">{item.updatedBy}</div>
                    </td>
                    <td className="px-5 py-4 text-right">
                      <div className="flex justify-end gap-2">
                        <Link
                          className="app-button-secondary py-2.5 inline-flex items-center gap-2"
                          to={`/tracker-nodes/${encodeURIComponent(item.nodeKey)}/edit?returnTo=${returnTo}`}
                        >
                          <PencilIcon className="app-button-icon" />
                          Edit
                        </Link>
                        <RowActionsMenu
                          items={[
                            {
                              label: "Preview",
                              icon: <EyeIcon />,
                              onClick: () => setView((current) => ({ ...current, preview: item.nodeKey }))
                            }
                          ]}
                        />
                      </div>
                    </td>
                  </CatalogTableRow>
                ))
              )}
            </tbody>
          </table>
        </div>
      </div>

      <PaginationFooter
        page={query.page}
        pageSize={query.pageSize}
        totalCount={totalCount}
        pageCount={pageCount}
        onPageChange={(value) => setView((current) => ({ ...current, query: { ...current.query, page: value } }))}
      />

      <PreviewDrawer
        open={previewItem !== null}
        title={previewItem?.nodeName || previewItem?.nodeKey || "Node configuration"}
        subtitle={previewItem ? `${previewItem.environment} / ${previewItem.region}` : undefined}
        onClose={() => setView((current) => ({ ...current, preview: null }))}
      >
        {previewItem ? (
          <div className="space-y-4">
            <div className="app-subtle-panel space-y-2">
              <div className="app-kicker">Identity</div>
              <div className="text-sm font-semibold text-ink">{previewItem.nodeId || "Node id not configured"}</div>
              <div className="text-sm text-steel">{previewItem.nodeKey}</div>
            </div>
            <div className="grid gap-3 md:grid-cols-2">
              <div className="app-subtle-panel space-y-2">
                <div className="app-kicker">Protocols</div>
                <div className="text-sm font-semibold text-ink">{formatProtocols(previewItem)}</div>
                <div className="text-xs text-steel">{previewItem.httpEnabled ? "HTTP enabled" : "HTTP disabled"} · {previewItem.udpEnabled ? "UDP enabled" : "UDP disabled"}</div>
              </div>
              <div className="app-subtle-panel space-y-2">
                <div className="app-kicker">Validation</div>
                <div className="text-sm font-semibold text-ink">{previewItem.errorCount} errors / {previewItem.warningCount} warnings</div>
                <div className="text-xs text-steel">{previewItem.requiresRestart ? "Restart recommended" : "Dynamic apply mode"}</div>
              </div>
            </div>
            <div className="flex justify-end">
              <Link
                className="app-button-primary inline-flex items-center gap-2"
                to={`/tracker-nodes/${encodeURIComponent(previewItem.nodeKey)}/edit?returnTo=${returnTo}`}
              >
                <PencilIcon className="app-button-icon" />
                Edit node config
              </Link>
            </div>
          </div>
        ) : null}
      </PreviewDrawer>
    </div>
  );
}

function TrackerEditorLayout({
  eyebrow,
  title,
  description,
  error,
  message,
  children
}: {
  eyebrow: string;
  title: string;
  description: string;
  error?: string | null;
  message?: string | null;
  children: React.ReactNode;
}) {
  return (
    <div className="app-page-stack">
      <div className="app-card">
        <div className="app-card-header">
          <div className="space-y-1">
            <div className="app-kicker">{eyebrow}</div>
            <h2 className="text-2xl font-bold text-ink">{title}</h2>
            <p className="text-sm text-steel">{description}</p>
          </div>
        </div>
        <div className="app-card-body space-y-4">
          {message ? <div className="app-notice-success">{message}</div> : null}
          {error ? <div className="app-notice-warn">{error}</div> : null}
          {children}
        </div>
      </div>
    </div>
  );
}

function TrackerEditorSummary({
  eyebrow = "Summary",
  title,
  items,
  children
}: {
  eyebrow?: string;
  title: string;
  items: Array<{ label: string; value: ReactNode; tone?: "default" | "mono" }>;
  children?: ReactNode;
}) {
  return (
    <div className="app-subtle-panel space-y-4">
      <div>
        <div className="app-kicker">{eyebrow}</div>
        <div className="text-sm font-semibold text-ink">{title}</div>
      </div>
      <div className="space-y-3">
        {items.map((item) => (
          <div key={item.label} className="space-y-1">
            <div className="app-kicker">{item.label}</div>
            <div className={item.tone === "mono" ? "break-all font-mono text-sm text-ink" : "text-sm font-medium text-ink"}>
              {item.value}
            </div>
          </div>
        ))}
      </div>
      {children}
    </div>
  );
}

function TrackerReadOnlySummary({
  items
}: {
  items: Array<{ label: string; value: ReactNode; tone?: "default" | "mono" }>;
}) {
  return (
    <div className="app-detail-grid">
      {items.map((item) => (
        <div key={item.label} className="app-subtle-panel space-y-2">
          <div className="app-kicker">{item.label}</div>
          <div className={item.tone === "mono" ? "break-all font-mono text-sm text-ink" : "text-sm font-semibold text-ink"}>
            {item.value}
          </div>
        </div>
      ))}
    </div>
  );
}

function TrackerOperationSummary({
  title = "Latest operation",
  children
}: {
  title?: string;
  children: ReactNode;
}) {
  return (
    <div className="space-y-3">
      <div className="app-kicker">{title}</div>
      {children}
    </div>
  );
}

function TrackerPreviewActions({
  children
}: {
  children: ReactNode;
}) {
  return <div className="flex flex-wrap justify-end gap-3">{children}</div>;
}

function TrackerEditorFooter({
  left,
  right
}: {
  left?: ReactNode;
  right: ReactNode;
}) {
  return (
    <div className="flex flex-wrap items-center justify-between gap-3 border-t border-ink/10 pt-4">
      <div className="flex flex-wrap items-center gap-3">{left}</div>
      <div className="flex flex-wrap items-center justify-end gap-3">{right}</div>
    </div>
  );
}

function TrackerConfigSection({
  title,
  description,
  children
}: {
  title: string;
  description: string;
  children: React.ReactNode;
}) {
  return (
    <div className="app-subtle-panel space-y-4">
      <div>
        <div className="app-kicker">{title}</div>
        <div className="text-sm text-steel">{description}</div>
      </div>
      {children}
    </div>
  );
}

function TrackerConfigField({
  label,
  value,
  onChange,
  type = "text",
  mono = false,
  placeholder,
  disabled = false
}: {
  label: string;
  value: string | number;
  onChange: (value: string) => void;
  type?: "text" | "number";
  mono?: boolean;
  placeholder?: string;
  disabled?: boolean;
}) {
  return (
    <label className="space-y-2">
      <span className="text-sm font-medium text-ink">{label}</span>
      <input
        className={`app-input ${mono ? "font-mono text-sm" : ""}`}
        type={type}
        value={value}
        disabled={disabled}
        placeholder={placeholder}
        onChange={(event) => onChange(event.target.value)}
      />
    </label>
  );
}

function TrackerConfigSelect({
  label,
  value,
  onChange,
  options
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  options: Array<{ value: string; label: string }>;
}) {
  return (
    <label className="space-y-2">
      <span className="text-sm font-medium text-ink">{label}</span>
      <select className="app-input" value={value} onChange={(event) => onChange(event.target.value)}>
        {options.map((option) => (
          <option key={option.value} value={option.value}>
            {option.label}
          </option>
        ))}
      </select>
    </label>
  );
}

function TrackerConfigToggle({
  label,
  checked,
  onChange
}: {
  label: string;
  checked: boolean;
  onChange: (checked: boolean) => void;
}) {
  return (
    <label className="flex items-center gap-3 rounded-2xl border border-ink/10 bg-slate-50 px-4 py-3 text-sm text-ink">
      <input type="checkbox" checked={checked} onChange={(event) => onChange(event.target.checked)} />
      {label}
    </label>
  );
}

function TrackerNodeConfigPage({
  accessToken,
  onReauthenticate,
  permissions
}: {
  accessToken: string;
  onReauthenticate: (fresh: boolean) => Promise<void>;
  permissions: string[];
}) {
  const navigate = useNavigate();
  const location = useLocation();
  const params = useParams();
  const nodeKeyParam = params.nodeKey ?? null;
  const isCreate = nodeKeyParam === null;
  const returnTo = sanitizeReturnTo(new URLSearchParams(location.search).get("returnTo"), "/tracker-nodes");
  const canWrite = hasPermission(permissions, "admin.maintenance.execute");
  const [form, setForm] = useState<TrackerNodeConfigFormState>(() => createDefaultTrackerNodeConfigFormState());
  const [view, setView] = useState<TrackerNodeConfigViewDto | null>(null);
  const [version, setVersion] = useState<number | null>(null);
  const [issues, setIssues] = useState<TrackerNodeConfigValidationResultDto["issues"]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [isSaving, setIsSaving] = useState(false);
  const [isValidating, setIsValidating] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [confirmDeleteOpen, setConfirmDeleteOpen] = useState(false);

  const hydrateFromView = (loaded: TrackerNodeConfigViewDto) => {
    setView(loaded);
    setForm(toTrackerNodeConfigFormState(loaded));
    setVersion(loaded.overview.version);
    setIssues([
      ...loaded.validation.errors.map((item, index) => ({ code: `error-${index + 1}`, path: "configuration", severity: "Error" as const, message: item })),
      ...loaded.validation.warnings.map((item, index) => ({ code: `warning-${index + 1}`, path: "configuration", severity: "Warning" as const, message: item }))
    ]);
  };

  const loadConfiguration = async () => {
    if (isCreate || !nodeKeyParam) {
      setView(null);
      setVersion(null);
      setForm(createDefaultTrackerNodeConfigFormState());
      setIssues([]);
      setError(null);
      setMessage("Create a new tracker node profile and save it to add it to the shared node catalog.");
      setIsLoading(false);
      return;
    }

    try {
      setIsLoading(true);
      const loaded = await apiRequest<TrackerNodeConfigViewDto>(
        `/api/admin/nodes/${encodeURIComponent(nodeKeyParam)}/config`,
        accessToken,
        onReauthenticate
      );
      hydrateFromView(loaded);
      setError(null);
    } catch (requestError) {
      const failure = requestError instanceof Error ? requestError.message : "Unable to load tracker node configuration.";
      if (failure.includes("(404)")) {
        setError("The selected tracker node profile was not found.");
        return;
      }

      setError(failure);
    } finally {
      setIsLoading(false);
    }
  };

  useEffect(() => {
    void loadConfiguration();
  }, [accessToken, isCreate, nodeKeyParam, onReauthenticate]);

  const setText = <T extends keyof TrackerNodeConfigFormState>(field: T, value: string) =>
    setForm((current) => ({ ...current, [field]: value }));

  const setNumber = <T extends keyof TrackerNodeConfigFormState>(field: T, value: string) =>
    setForm((current) => ({ ...current, [field]: Number.isFinite(Number(value)) ? Number(value) : 0 }));

  const setBool = <T extends keyof TrackerNodeConfigFormState>(field: T, value: boolean) =>
    setForm((current) => ({ ...current, [field]: value }));

  const validateConfiguration = async () => {
    try {
      setIsValidating(true);
      setError(null);
      const validation = await apiMutation<TrackerNodeConfigValidationResultDto, ReturnType<typeof toTrackerNodeConfigurationDocument>>(
        "/api/admin/nodes/validate",
        "POST",
        accessToken,
        toTrackerNodeConfigurationDocument(form),
        onReauthenticate
      );
      setIssues(validation.issues);
      setMessage(validation.isValid ? "Configuration validation passed." : "Configuration validation returned issues.");
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : "Unable to validate tracker node configuration.");
    } finally {
      setIsValidating(false);
    }
  };

  const saveConfiguration = async () => {
    const effectiveNodeKey = (isCreate ? form.nodeKey : nodeKeyParam ?? "").trim();
    if (!effectiveNodeKey) {
      setError("Node key is required before saving the tracker node configuration.");
      return;
    }

    try {
      setIsSaving(true);
      setError(null);
      const response = await apiMutation<
        { version: number; nodeKey: string; requiresRestart: boolean },
        { configuration: ReturnType<typeof toTrackerNodeConfigurationDocument>; applyMode: number; expectedVersion?: number }
      >(
        `/api/admin/nodes/${encodeURIComponent(effectiveNodeKey)}/config`,
        "PUT",
        accessToken,
        {
          configuration: toTrackerNodeConfigurationDocument(form),
          applyMode: form.applyMode === "Dynamic" ? 0 : form.applyMode === "StartupOnly" ? 2 : 1,
          expectedVersion: version ?? undefined
        },
        onReauthenticate
      );
      navigate(returnTo, {
        state: {
          message: response.requiresRestart
            ? `Node configuration '${response.nodeKey}' saved. Restart is recommended for full application.`
            : `Node configuration '${response.nodeKey}' saved.`,
          tone: response.requiresRestart ? "warn" : "good"
        } satisfies NavigationBannerState
      });
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : "Unable to save tracker node configuration.");
    } finally {
      setIsSaving(false);
    }
  };

  const deleteConfiguration = async () => {
    if (isCreate || !nodeKeyParam) {
      return;
    }

    try {
      setIsSaving(true);
      setError(null);
      const path = version === null
        ? `/api/admin/nodes/${encodeURIComponent(nodeKeyParam)}/config`
        : `/api/admin/nodes/${encodeURIComponent(nodeKeyParam)}/config?expectedVersion=${encodeURIComponent(String(version))}`;
      await apiDelete(
        path,
        accessToken,
        onReauthenticate
      );
      navigate(returnTo, {
        state: {
          message: `Node configuration '${nodeKeyParam}' deleted.`,
          tone: "good"
        } satisfies NavigationBannerState
      });
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : "Unable to delete tracker node configuration.");
    } finally {
      setIsSaving(false);
    }
  };

  const summaryCards = [
    {
      title: "Node",
      primary: form.nodeName || "Unnamed tracker node",
      secondary: `${form.environment} / ${form.region}`,
      meta: form.nodeKey || form.nodeId || "node key not configured"
    },
    {
      title: "Effective mode",
      primary: view?.overview.requiresRestart ? "Restart recommended" : "Dynamic-ready",
      secondary: `Apply mode: ${normalizeTrackerNodeApplyMode(view?.overview.applyMode ?? form.applyMode)}`,
      meta: `Version ${view?.overview.version ?? version ?? "new"}`
    },
    {
      title: "Dependencies",
      primary: view?.coordination.redis.summary ?? "Redis summary unavailable",
      secondary: view?.coordination.postgres.summary ?? "PostgreSQL summary unavailable",
      meta: `Updated by ${view?.overview.updatedBy ?? "n/a"}`
    }
  ];

  return (
    <TrackerEditorLayout
      eyebrow="Tracker node"
      title={isCreate ? "Create tracker node configuration" : "Edit tracker node configuration"}
      description={isCreate ? "Create a new tracker node profile, then validate and save it into the shared node catalog." : "Configure a persisted tracker node profile, validate startup safety and inspect the effective operational summary."}
      error={error}
      message={message}
    >
      {isLoading ? (
        <div className="app-subtle-panel text-sm text-steel">Loading tracker node configuration…</div>
      ) : (
        <div className="space-y-5">
          <div className="app-detail-grid">
            {summaryCards.map((card) => (
              <div key={card.title} className="app-subtle-panel space-y-2">
                <div className="app-kicker">{card.title}</div>
                <div className="font-semibold text-ink">{card.primary}</div>
                <div className="text-sm text-steel">{card.secondary}</div>
                <div className="text-xs text-steel">{card.meta}</div>
              </div>
            ))}
          </div>

          {issues.length > 0 ? (
            <div className="rounded-3xl border border-amber-200 bg-amber-50 px-4 py-4">
              <div className="text-sm font-semibold text-ink">Validation issues</div>
              <div className="mt-3 space-y-2 text-sm text-steel">
                {issues.map((issue) => (
                  <div key={`${issue.code}-${issue.path}`} className="flex items-start justify-between gap-3">
                    <div>
                      <div className="font-medium text-ink">{issue.message}</div>
                      <div className="text-xs text-steel/80">{issue.path} · {issue.code}</div>
                    </div>
                    <span className={issue.severity === "Error" ? "app-chip-warn" : "app-chip-soft"}>{issue.severity}</span>
                  </div>
                ))}
              </div>
            </div>
          ) : null}

          <TrackerConfigSection title="Identity" description="Node identity, URLs and advertised capabilities.">
            <div className="app-form-grid">
              <TrackerConfigField label="Node key" value={form.nodeKey} mono disabled={!isCreate} onChange={(value) => setText("nodeKey", value)} />
              <TrackerConfigField label="Node id" value={form.nodeId} mono onChange={(value) => setText("nodeId", value)} />
              <TrackerConfigField label="Node name" value={form.nodeName} onChange={(value) => setText("nodeName", value)} />
              <TrackerConfigField label="Environment" value={form.environment} onChange={(value) => setText("environment", value)} />
              <TrackerConfigField label="Region" value={form.region} onChange={(value) => setText("region", value)} />
              <TrackerConfigField label="Public base URL" value={form.publicBaseUrl} onChange={(value) => setText("publicBaseUrl", value)} />
              <TrackerConfigField label="Internal base URL" value={form.internalBaseUrl} onChange={(value) => setText("internalBaseUrl", value)} />
            </div>
            <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
              <TrackerConfigToggle label="Supports HTTP" checked={form.supportsHttp} onChange={(value) => setBool("supportsHttp", value)} />
              <TrackerConfigToggle label="Supports UDP" checked={form.supportsUdp} onChange={(value) => setBool("supportsUdp", value)} />
              <TrackerConfigToggle label="Supports public tracker" checked={form.supportsPublicTracker} onChange={(value) => setBool("supportsPublicTracker", value)} />
              <TrackerConfigToggle label="Supports private tracker" checked={form.supportsPrivateTracker} onChange={(value) => setBool("supportsPrivateTracker", value)} />
            </div>
          </TrackerConfigSection>

          <TrackerConfigSection title="Protocols" description="HTTP announce/scrape surface and UDP listener settings.">
            <div className="app-form-grid">
              <TrackerConfigField label="Announce route" value={form.announceRoute} mono onChange={(value) => setText("announceRoute", value)} />
              <TrackerConfigField label="Private announce route" value={form.privateAnnounceRoute} mono onChange={(value) => setText("privateAnnounceRoute", value)} />
              <TrackerConfigField label="Scrape route" value={form.scrapeRoute} mono onChange={(value) => setText("scrapeRoute", value)} />
              <TrackerConfigField label="UDP bind address" value={form.udpBindAddress} mono onChange={(value) => setText("udpBindAddress", value)} />
              <TrackerConfigField label="Default announce interval (s)" type="number" value={form.defaultAnnounceIntervalSeconds} onChange={(value) => setNumber("defaultAnnounceIntervalSeconds", value)} />
              <TrackerConfigField label="Minimum announce interval (s)" type="number" value={form.minAnnounceIntervalSeconds} onChange={(value) => setNumber("minAnnounceIntervalSeconds", value)} />
              <TrackerConfigField label="Default numwant" type="number" value={form.defaultNumWant} onChange={(value) => setNumber("defaultNumWant", value)} />
              <TrackerConfigField label="Max numwant" type="number" value={form.maxNumWant} onChange={(value) => setNumber("maxNumWant", value)} />
              <TrackerConfigField label="UDP port" type="number" value={form.udpPort} onChange={(value) => setNumber("udpPort", value)} />
              <TrackerConfigField label="UDP timeout (s)" type="number" value={form.udpConnectionTimeoutSeconds} onChange={(value) => setNumber("udpConnectionTimeoutSeconds", value)} />
              <TrackerConfigField label="UDP receive buffer" type="number" value={form.udpReceiveBufferSize} onChange={(value) => setNumber("udpReceiveBufferSize", value)} />
              <TrackerConfigField label="UDP max datagram" type="number" value={form.udpMaxDatagramSize} onChange={(value) => setNumber("udpMaxDatagramSize", value)} />
            </div>
            <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-5">
              <TrackerConfigToggle label="Enable HTTP announce" checked={form.httpAnnounceEnabled} onChange={(value) => setBool("httpAnnounceEnabled", value)} />
              <TrackerConfigToggle label="Enable HTTP scrape" checked={form.httpScrapeEnabled} onChange={(value) => setBool("httpScrapeEnabled", value)} />
              <TrackerConfigToggle label="Enable UDP" checked={form.udpEnabled} onChange={(value) => setBool("udpEnabled", value)} />
              <TrackerConfigToggle label="Enable UDP scrape" checked={form.udpScrapeEnabled} onChange={(value) => setBool("udpScrapeEnabled", value)} />
              <TrackerConfigToggle label="Compact by default" checked={form.compactResponsesByDefault} onChange={(value) => setBool("compactResponsesByDefault", value)} />
              <TrackerConfigToggle label="Allow non-compact" checked={form.allowNonCompactResponses} onChange={(value) => setBool("allowNonCompactResponses", value)} />
              <TrackerConfigToggle label="Passkey in path" checked={form.allowPasskeyInPath} onChange={(value) => setBool("allowPasskeyInPath", value)} />
              <TrackerConfigToggle label="Passkey in query" checked={form.allowPasskeyInQuery} onChange={(value) => setBool("allowPasskeyInQuery", value)} />
              <TrackerConfigToggle label="Allow client IP override" checked={form.allowClientIpOverride} onChange={(value) => setBool("allowClientIpOverride", value)} />
              <TrackerConfigToggle label="Emit warning messages" checked={form.emitWarningMessages} onChange={(value) => setBool("emitWarningMessages", value)} />
            </div>
          </TrackerConfigSection>

          <div className="grid gap-4 xl:grid-cols-2">
            <TrackerConfigSection title="Runtime" description="Node-local runtime store and peer selection behavior.">
              <div className="app-form-grid">
                <TrackerConfigField label="Shard count" type="number" value={form.shardCount} onChange={(value) => setNumber("shardCount", value)} />
                <TrackerConfigField label="Peer TTL (s)" type="number" value={form.peerTtlSeconds} onChange={(value) => setNumber("peerTtlSeconds", value)} />
                <TrackerConfigField label="Cleanup interval (s)" type="number" value={form.cleanupIntervalSeconds} onChange={(value) => setNumber("cleanupIntervalSeconds", value)} />
                <TrackerConfigField label="Max peers per response" type="number" value={form.maxPeersPerResponse} onChange={(value) => setNumber("maxPeersPerResponse", value)} />
                <TrackerConfigField label="Max peers per swarm" type="number" value={form.maxPeersPerSwarm} onChange={(value) => setNumber("maxPeersPerSwarm", value)} />
              </div>
              <div className="grid gap-3 md:grid-cols-3">
                <TrackerConfigToggle label="Prefer local peers" checked={form.preferLocalShardPeers} onChange={(value) => setBool("preferLocalShardPeers", value)} />
                <TrackerConfigToggle label="Completed accounting" checked={form.enableCompletedAccounting} onChange={(value) => setBool("enableCompletedAccounting", value)} />
                <TrackerConfigToggle label="Enable IPv6 peers" checked={form.enableIPv6Peers} onChange={(value) => setBool("enableIPv6Peers", value)} />
              </div>
            </TrackerConfigSection>

            <TrackerConfigSection title="Policy" description="Public/private surface and compatibility defaults.">
              <div className="app-form-grid">
                <TrackerConfigSelect label="Default torrent visibility" value={form.defaultTorrentVisibility} onChange={(value) => setText("defaultTorrentVisibility", value)} options={[{ value: "private", label: "Private" }, { value: "public", label: "Public" }]} />
                <TrackerConfigSelect label="Strictness profile" value={form.strictnessProfile} onChange={(value) => setText("strictnessProfile", value)} options={[{ value: "balanced", label: "Balanced" }, { value: "strict", label: "Strict" }, { value: "compatibility", label: "Compatibility" }]} />
                <TrackerConfigSelect label="Compatibility mode" value={form.compatibilityMode} onChange={(value) => setText("compatibilityMode", value)} options={[{ value: "standard", label: "Standard" }, { value: "legacy", label: "Legacy" }, { value: "strict", label: "Strict" }]} />
                <TrackerConfigSelect label="Apply mode" value={form.applyMode} onChange={(value) => setText("applyMode", normalizeTrackerNodeApplyMode(value))} options={[{ value: "Dynamic", label: "Dynamic" }, { value: "RestartRecommended", label: "Restart recommended" }, { value: "StartupOnly", label: "Startup only" }]} />
              </div>
              <div className="grid gap-3 md:grid-cols-2">
                <TrackerConfigToggle label="Enable public tracker" checked={form.enablePublicTracker} onChange={(value) => setBool("enablePublicTracker", value)} />
                <TrackerConfigToggle label="Enable private tracker" checked={form.enablePrivateTracker} onChange={(value) => setBool("enablePrivateTracker", value)} />
                <TrackerConfigToggle label="Require passkey for private" checked={form.requirePasskeyForPrivateTracker} onChange={(value) => setBool("requirePasskeyForPrivateTracker", value)} />
                <TrackerConfigToggle label="Allow public scrape" checked={form.allowPublicScrape} onChange={(value) => setBool("allowPublicScrape", value)} />
                <TrackerConfigToggle label="Allow private scrape" checked={form.allowPrivateScrape} onChange={(value) => setBool("allowPrivateScrape", value)} />
              </div>
            </TrackerConfigSection>
          </div>

          <div className="grid gap-4 xl:grid-cols-2">
            <TrackerConfigSection title="Redis coordination" description="L2 cache and lightweight coordination. Leave connection blank to preserve the stored secret.">
              <div className="app-form-grid">
                <TrackerConfigField label="Redis connection" value={form.redisConnection} mono placeholder="Leave blank to keep the stored secret" onChange={(value) => setText("redisConnection", value)} />
                <TrackerConfigField label="Key prefix" value={form.redisKeyPrefix} onChange={(value) => setText("redisKeyPrefix", value)} />
                <TrackerConfigField label="Invalidation channel" value={form.redisInvalidationChannel} onChange={(value) => setText("redisInvalidationChannel", value)} />
                <TrackerConfigField label="Policy cache TTL" type="number" value={form.redisPolicyCacheTtlSeconds} onChange={(value) => setNumber("redisPolicyCacheTtlSeconds", value)} />
                <TrackerConfigField label="Snapshot cache TTL" type="number" value={form.redisSnapshotCacheTtlSeconds} onChange={(value) => setNumber("redisSnapshotCacheTtlSeconds", value)} />
                <TrackerConfigField label="Heartbeat TTL" type="number" value={form.redisHeartbeatTtlSeconds} onChange={(value) => setNumber("redisHeartbeatTtlSeconds", value)} />
                <TrackerConfigField label="Ownership lease" type="number" value={form.redisOwnershipLeaseDurationSeconds} onChange={(value) => setNumber("redisOwnershipLeaseDurationSeconds", value)} />
                <TrackerConfigField label="Ownership refresh" type="number" value={form.redisOwnershipRefreshIntervalSeconds} onChange={(value) => setNumber("redisOwnershipRefreshIntervalSeconds", value)} />
                <TrackerConfigField label="Swarm publish interval" type="number" value={form.redisSwarmSummaryPublishIntervalSeconds} onChange={(value) => setNumber("redisSwarmSummaryPublishIntervalSeconds", value)} />
                <TrackerConfigField label="Swarm summary TTL" type="number" value={form.redisSwarmSummaryTtlSeconds} onChange={(value) => setNumber("redisSwarmSummaryTtlSeconds", value)} />
              </div>
              <TrackerConfigToggle label="Enable Redis coordination" checked={form.redisEnabled} onChange={(value) => setBool("redisEnabled", value)} />
            </TrackerConfigSection>

            <TrackerConfigSection title="PostgreSQL persistence" description="Persistence and batching controls. Leave connection blank to preserve the stored secret.">
              <div className="app-form-grid">
                <TrackerConfigField label="PostgreSQL connection" value={form.postgresConnectionString} mono placeholder="Leave blank to keep the stored secret" onChange={(value) => setText("postgresConnectionString", value)} />
                <TrackerConfigField label="Telemetry batch size" type="number" value={form.telemetryBatchSize} onChange={(value) => setNumber("telemetryBatchSize", value)} />
                <TrackerConfigField label="Telemetry flush (ms)" type="number" value={form.telemetryFlushIntervalMilliseconds} onChange={(value) => setNumber("telemetryFlushIntervalMilliseconds", value)} />
              </div>
              <div className="grid gap-3 md:grid-cols-2">
                <TrackerConfigToggle label="Enable PostgreSQL persistence" checked={form.postgresEnabled} onChange={(value) => setBool("postgresEnabled", value)} />
                <TrackerConfigToggle label="Migrate on start" checked={form.migrateOnStart} onChange={(value) => setBool("migrateOnStart", value)} />
                <TrackerConfigToggle label="Persist telemetry" checked={form.persistTelemetry} onChange={(value) => setBool("persistTelemetry", value)} />
                <TrackerConfigToggle label="Persist audit" checked={form.persistAudit} onChange={(value) => setBool("persistAudit", value)} />
              </div>
            </TrackerConfigSection>
          </div>

          <div className="grid gap-4 xl:grid-cols-2">
            <TrackerConfigSection title="Abuse protection" description="Request size limits, numwant caps and rate limiting.">
              <div className="app-form-grid">
                <TrackerConfigField label="Max announce query length" type="number" value={form.maxAnnounceQueryLength} onChange={(value) => setNumber("maxAnnounceQueryLength", value)} />
                <TrackerConfigField label="Max scrape query length" type="number" value={form.maxScrapeQueryLength} onChange={(value) => setNumber("maxScrapeQueryLength", value)} />
                <TrackerConfigField label="Max query parameter count" type="number" value={form.maxQueryParameterCount} onChange={(value) => setNumber("maxQueryParameterCount", value)} />
                <TrackerConfigField label="Hard max numwant" type="number" value={form.hardMaxNumWant} onChange={(value) => setNumber("hardMaxNumWant", value)} />
                <TrackerConfigField label="Announce per passkey/sec" type="number" value={form.announcePerPasskeyPerSecond} onChange={(value) => setNumber("announcePerPasskeyPerSecond", value)} />
                <TrackerConfigField label="Announce per IP/sec" type="number" value={form.announcePerIpPerSecond} onChange={(value) => setNumber("announcePerIpPerSecond", value)} />
                <TrackerConfigField label="Scrape per IP/sec" type="number" value={form.scrapePerIpPerSecond} onChange={(value) => setNumber("scrapePerIpPerSecond", value)} />
                <TrackerConfigField label="Max scrape info hashes" type="number" value={form.maxScrapeInfoHashes} onChange={(value) => setNumber("maxScrapeInfoHashes", value)} />
              </div>
              <div className="grid gap-3 md:grid-cols-2">
                <TrackerConfigToggle label="Passkey announce rate limit" checked={form.enableAnnouncePasskeyRateLimit} onChange={(value) => setBool("enableAnnouncePasskeyRateLimit", value)} />
                <TrackerConfigToggle label="IP announce rate limit" checked={form.enableAnnounceIpRateLimit} onChange={(value) => setBool("enableAnnounceIpRateLimit", value)} />
                <TrackerConfigToggle label="IP scrape rate limit" checked={form.enableScrapeIpRateLimit} onChange={(value) => setBool("enableScrapeIpRateLimit", value)} />
                <TrackerConfigToggle label="Reject oversized requests" checked={form.rejectOversizedRequests} onChange={(value) => setBool("rejectOversizedRequests", value)} />
              </div>
            </TrackerConfigSection>

            <TrackerConfigSection title="Observability" description="Health, diagnostics and startup exposure.">
              <div className="app-form-grid">
                <TrackerConfigField label="Live route" value={form.liveRoute} mono onChange={(value) => setText("liveRoute", value)} />
                <TrackerConfigField label="Ready route" value={form.readyRoute} mono onChange={(value) => setText("readyRoute", value)} />
                <TrackerConfigField label="Startup route" value={form.startupRoute} mono onChange={(value) => setText("startupRoute", value)} />
              </div>
              <div className="grid gap-3 md:grid-cols-2">
                <TrackerConfigToggle label="Enable health endpoints" checked={form.enableHealthEndpoints} onChange={(value) => setBool("enableHealthEndpoints", value)} />
                <TrackerConfigToggle label="Enable metrics" checked={form.enableMetrics} onChange={(value) => setBool("enableMetrics", value)} />
                <TrackerConfigToggle label="Enable tracing" checked={form.enableTracing} onChange={(value) => setBool("enableTracing", value)} />
                <TrackerConfigToggle label="Enable diagnostics" checked={form.enableDiagnosticsEndpoints} onChange={(value) => setBool("enableDiagnosticsEndpoints", value)} />
              </div>
            </TrackerConfigSection>
          </div>

          <div className="flex flex-wrap justify-end gap-3">
            {!isCreate ? (
              <button
                type="button"
                className="app-button-danger inline-flex items-center gap-2"
                disabled={!canWrite || isSaving}
                onClick={() => setConfirmDeleteOpen(true)}
              >
                <TrashIcon className="app-button-icon" />
                Delete node configuration
              </button>
            ) : null}
            <Link to={returnTo} className="app-button-secondary inline-flex items-center gap-2">
              Cancel
            </Link>
            <button type="button" className="app-button-secondary inline-flex items-center gap-2" disabled={isValidating} onClick={() => void validateConfiguration()}>
              <SettingsIcon className="app-button-icon" />
              Validate configuration
            </button>
            <button type="button" className="app-button-primary inline-flex items-center gap-2" disabled={!canWrite || isSaving} onClick={() => void saveConfiguration()}>
              <PencilIcon className="app-button-icon" />
              {isCreate ? "Create node configuration" : "Save node configuration"}
            </button>
          </div>
        </div>
      )}
      <ConfirmActionModal
        open={confirmDeleteOpen}
        title="Delete node configuration"
        description={`Delete node configuration '${form.nodeKey || nodeKeyParam || "selected node"}'? This cannot be undone.`}
        confirmLabel="Delete node configuration"
        onClose={() => setConfirmDeleteOpen(false)}
        onConfirm={() => {
          setConfirmDeleteOpen(false);
          void deleteConfiguration();
        }}
      />
    </TrackerEditorLayout>
  );
}

async function findTrackerAccessRecord(
  accessToken: string,
  onReauthenticate: (fresh?: boolean) => Promise<void>,
  userId: string
) {
  return await apiRequest<TrackerAccessAdminDto | null>(
    `/api/admin/tracker-access/${encodeURIComponent(userId)}`,
    accessToken,
    onReauthenticate
  );
}

async function findPasskeyRecord(
  accessToken: string,
  onReauthenticate: (fresh?: boolean) => Promise<void>,
  id: string
) {
  return await apiRequest<PasskeyAdminDto | null>(
    `/api/admin/passkeys/${encodeURIComponent(id)}`,
    accessToken,
    onReauthenticate
  );
}

async function findBanRuleRecord(
  accessToken: string,
  onReauthenticate: (fresh?: boolean) => Promise<void>,
  scope: string,
  subject: string
) {
  return await apiRequest<BanRuleAdminDto | null>(
    `/api/admin/bans/${encodeURIComponent(scope)}/${encodeURIComponent(subject)}`,
    accessToken,
    onReauthenticate
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
  const [view, setView] = useCatalogViewState({
    search: "",
    filter: "all",
    sort: "infohash:asc",
    page: 1,
    pageSize: 25
  });
  const { query, preview } = view;
  const [items, setItems] = useState<TorrentAdminDto[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [selectedInfoHashes, setSelectedInfoHashes] = useState<string[]>([]);
  const [status, setStatus] = useState<string | null>(null);
  const [result, setResult] = useState<BulkOperationResultDto | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [isLoading, setIsLoading] = useState(true);
  const canEditPolicy = hasGrantedCapability(capabilities, "admin.write.torrent_policy");
  const canActivate = hasGrantedCapability(capabilities, "admin.activate.torrent");
  const canDeactivate = hasGrantedCapability(capabilities, "admin.deactivate.torrent");
  const canBulkEditPolicy = hasGrantedCapability(capabilities, "admin.bulk_upsert.torrent_policy");
  const canCreatePolicy = hasGrantedCapability(capabilities, "admin.write.torrent_policy");
  const previewItem = items.find((item) => item.infoHash === preview) ?? null;
  const [activeSortField, activeSortDirection = "asc"] = query.sort.split(":");
  const pageCount = Math.max(1, Math.ceil(totalCount / query.pageSize));

  useEffect(() => {
    const banner = location.state as NavigationBannerState | null;
    if (!banner?.message) {
      return;
    }

    setStatus(banner.message);
    navigate(location.pathname, { replace: true, state: null });
  }, [location.pathname, location.state, navigate]);

  const reload = async () => {
    const page = await apiRequest<PageResult<TorrentAdminDto>>(
      `/api/admin/torrents?${buildGridQueryString(query)}`,
      accessToken,
      onReauthenticate
    );
    setItems(page.items);
    setTotalCount(page.totalCount);
    setSelectedInfoHashes((current) => current.filter((infoHash) => page.items.some((item) => item.infoHash === infoHash)));
  };

  useEffect(() => {
    let isMounted = true;

    setIsLoading(true);
    reload()
      .then(() => {
        if (isMounted) {
          setError(null);
        }
      })
      .catch((requestError) => {
        if (isMounted) {
          setError(requestError instanceof Error ? requestError.message : labels.loadError);
          setItems([]);
          setTotalCount(0);
        }
      })
      .finally(() => {
        if (isMounted) {
          setIsLoading(false);
        }
      });

    return () => {
      isMounted = false;
    };
  }, [accessToken, onReauthenticate, query]);

  useEffect(() => {
    if (preview && !items.some((item) => item.infoHash === preview)) {
      setView((current) => ({ ...current, preview: null }));
    }
  }, [items, preview, setView]);

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
      await reload();
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

  const openCreate = () => {
    navigate(`/torrents/new?returnTo=${encodeURIComponent(buildReturnTo(location.pathname, location.search))}`);
  };

  return (
    <div className="space-y-6">
      <CatalogToolbar
        title={labels.cardTitle}
        description="Search torrents, review lifecycle state and launch policy workflows."
        totalCount={totalCount}
        search={query.search}
        onSearchChange={(value) => setView((current) => ({ ...current, query: { ...current.query, search: value, page: 1 } }))}
        searchPlaceholder="Search torrents"
        filter={query.filter}
        onFilterChange={(value) => setView((current) => ({ ...current, query: { ...current.query, filter: value, page: 1 } }))}
        filterOptions={[
          { value: "all", label: "All torrents" },
          { value: "enabled", label: "Enabled" },
          { value: "disabled", label: "Disabled" },
          { value: "private", label: "Private" },
          { value: "public", label: "Public" }
        ]}
        sortValue={query.sort}
        onSortChange={(value) => setView((current) => ({ ...current, query: { ...current.query, sort: value, page: 1 } }))}
        sortOptions={[
          { value: "infohash:asc", label: "Info hash A-Z" },
          { value: "infohash:desc", label: "Info hash Z-A" },
          { value: "enabled:desc", label: "Enabled first" },
          { value: "private:desc", label: "Private first" },
          { value: "interval:desc", label: "Interval high-low" }
        ]}
        pageSize={query.pageSize}
        onPageSizeChange={(value) => setView((current) => ({ ...current, query: { ...current.query, pageSize: value, page: 1 } }))}
        createLabel="Create torrent"
        createHref={canCreatePolicy ? `/torrents/new?returnTo=${encodeURIComponent(buildReturnTo(location.pathname, location.search))}` : undefined}
      />
      {status ? <div className="app-notice-success">{status}</div> : null}
      {error ? <div className="app-notice-warn">{error}</div> : null}
      {result && result.torrentItems.length > 0 ? (
        <div className="app-selection-summary">
          <div className="space-y-1">
            <div className="text-sm font-semibold text-ink">Last lifecycle operation</div>
            <div className="text-sm text-steel">{result.succeededCount} succeeded, {result.failedCount} failed, {result.totalCount} processed</div>
          </div>
          <button type="button" className="app-button-secondary py-2.5" onClick={() => setResult(null)}>Clear result</button>
        </div>
      ) : null}
      {selectedInfoHashes.length > 0 ? (
        <div className="app-selection-summary">
          <div className="space-y-1">
            <div className="text-sm font-semibold text-ink">{selectedInfoHashes.length} torrent(s) selected</div>
            <div className="text-sm text-steel">Run lifecycle actions or open the bulk policy workflow for the current selection.</div>
          </div>
          <div className="flex flex-wrap gap-3">
            <button type="button" className="app-button-secondary py-2.5" onClick={() => { setSelectedInfoHashes([]); clearBulkTorrentPolicySelection(); }}>Clear selection</button>
            <button type="button" className="app-button-secondary py-2.5 inline-flex items-center gap-2" disabled={!canActivate || isSubmitting || selectedInfoHashes.length === 0} onClick={() => void runLifecycle("activate")}><PowerIcon className="app-button-icon" />{labels.activateSelection}</button>
            <button type="button" className="app-button-danger py-2.5 inline-flex items-center gap-2" disabled={!canDeactivate || isSubmitting || selectedInfoHashes.length === 0} onClick={() => void runLifecycle("deactivate")}><PowerIcon className="app-button-icon" />{labels.deactivateSelection}</button>
            <button type="button" className="app-button-primary py-2.5 inline-flex items-center gap-2" disabled={!canBulkEditPolicy || isSubmitting || selectedInfoHashes.length === 0} onClick={openBulkPolicyEditor}><SettingsIcon className="app-button-icon" />{labels.openBulkPolicy}</button>
          </div>
        </div>
      ) : null}
      <div className="app-data-table">
        <div className="overflow-x-auto">
          <table className="min-w-full text-sm">
            <thead className="app-table-head">
              <tr>
                <th className="w-14 px-5 py-4">{labels.tableSelect}</th>
                <th className="px-5 py-4"><SortHeaderButton label={labels.tableInfoHash} active={activeSortField === "infohash"} direction={activeSortDirection as "asc" | "desc"} onClick={() => setView((current) => ({ ...current, query: { ...current.query, sort: activeSortField === "infohash" && activeSortDirection === "asc" ? "infohash:desc" : "infohash:asc", page: 1 } }))} /></th>
                <th className="px-5 py-4"><SortHeaderButton label={labels.tableMode} active={activeSortField === "private"} direction={activeSortDirection as "asc" | "desc"} onClick={() => setView((current) => ({ ...current, query: { ...current.query, sort: activeSortField === "private" && activeSortDirection === "asc" ? "private:desc" : "private:asc", page: 1 } }))} /></th>
                <th className="px-5 py-4"><SortHeaderButton label={labels.tableState} active={activeSortField === "enabled"} direction={activeSortDirection as "asc" | "desc"} onClick={() => setView((current) => ({ ...current, query: { ...current.query, sort: activeSortField === "enabled" && activeSortDirection === "asc" ? "enabled:desc" : "enabled:asc", page: 1 } }))} /></th>
                <th className="px-5 py-4"><SortHeaderButton label={labels.tableInterval} active={activeSortField === "interval"} direction={activeSortDirection as "asc" | "desc"} onClick={() => setView((current) => ({ ...current, query: { ...current.query, sort: activeSortField === "interval" && activeSortDirection === "asc" ? "interval:desc" : "interval:asc", page: 1 } }))} /></th>
                <th className="px-5 py-4">{labels.tableNumwant}</th>
                <th className="px-5 py-4 text-right">{labels.tableAction}</th>
              </tr>
            </thead>
            <tbody>
              {isLoading ? (
                <TableStateRow colSpan={7} title="Loading torrents" message="Refreshing tracker policy records from the backend." />
              ) : error && totalCount === 0 ? (
                <TableStateRow colSpan={7} title="Unable to load torrents" message={error} />
              ) : items.length === 0 ? (
                <TableStateRow colSpan={7} title="No torrents match this view" message="Try a broader search or switch the current filter." />
              ) : items.map((item) => (
                <CatalogTableRow key={item.infoHash} onOpen={() => setView((current) => ({ ...current, preview: item.infoHash }))}>
                  <td className="px-5 py-4"><input type="checkbox" checked={selectedInfoHashes.includes(item.infoHash)} onChange={() => toggleSelection(item.infoHash)} /></td>
                  <td className="px-5 py-4">
                    <div className="app-grid-primary font-mono">{item.infoHash}</div>
                    <div className="app-inline-id">
                      <CopyValueButton value={item.infoHash} label={`Copy info hash ${item.infoHash}`} />
                    </div>
                  </td>
                  <td className="px-5 py-4 text-steel">{formatMode(item.isPrivate, dictionary)}</td>
                  <td className="px-5 py-4 text-steel">{formatState(item.isEnabled, dictionary)}</td>
                  <td className="px-5 py-4 text-steel">{item.announceIntervalSeconds}s</td>
                  <td className="px-5 py-4 text-steel">{item.defaultNumWant} / {item.maxNumWant}</td>
                  <td className="px-5 py-4 text-right">
                    <div className="flex justify-end items-center gap-2">
                      {canEditPolicy ? (
                        <Link className="app-button-secondary py-2.5 no-underline inline-flex items-center gap-2" to={`/torrents/${encodeURIComponent(item.infoHash)}/edit?returnTo=${encodeURIComponent(buildReturnTo(location.pathname, location.search))}`}>
                          <SettingsIcon className="app-button-icon" />
                          {labels.openPolicy}
                        </Link>
                      ) : (
                        <span className="app-chip-muted">Read only</span>
                      )}
                      <RowActionsMenu items={[{ label: "Preview", icon: <EyeIcon />, onClick: () => setView((current) => ({ ...current, preview: item.infoHash })) }]} />
                    </div>
                  </td>
                </CatalogTableRow>
              ))}
            </tbody>
          </table>
        </div>
      </div>
      <PaginationFooter page={query.page} pageCount={pageCount} totalCount={totalCount} pageSize={query.pageSize} onPageChange={(page) => setView((current) => ({ ...current, query: { ...current.query, page } }))} />
      <PreviewDrawer open={previewItem != null} title={previewItem?.infoHash ?? ""} subtitle="Current torrent policy snapshot" onClose={() => setView((current) => ({ ...current, preview: null }))}>
        {previewItem ? (
          <div className="space-y-4">
            <TrackerReadOnlySummary
              items={[
                { label: dictionary.common.mode, value: formatMode(previewItem.isPrivate, dictionary) },
                { label: dictionary.common.state, value: formatState(previewItem.isEnabled, dictionary) },
                { label: dictionary.common.interval, value: `${previewItem.announceIntervalSeconds}s / min ${previewItem.minAnnounceIntervalSeconds}s` },
                { label: dictionary.common.scrape, value: formatScrape(previewItem.allowScrape, dictionary) },
                { label: "Numwant", value: `${previewItem.defaultNumWant} / ${previewItem.maxNumWant}` },
                { label: dictionary.common.version, value: previewItem.version }
              ]}
            />
            {result?.torrentItems?.some((item) => item.infoHash === previewItem.infoHash) ? (
              <TrackerOperationSummary>
                {result.torrentItems.filter((item) => item.infoHash === previewItem.infoHash).map((item) => (
                  <div key={item.infoHash} className="app-subtle-panel space-y-2">
                    <StatusPill tone={item.succeeded ? "good" : "warn"}>{item.succeeded ? labels.applied : labels.failed}</StatusPill>
                    {item.errorMessage ? <div className="text-sm text-ember">{item.errorMessage}</div> : null}
                  </div>
                ))}
              </TrackerOperationSummary>
            ) : null}
            <TrackerPreviewActions>
              {canEditPolicy ? (
                <Link
                  className="app-button-primary inline-flex items-center gap-2"
                  to={`/torrents/${encodeURIComponent(previewItem.infoHash)}/edit?returnTo=${encodeURIComponent(buildReturnTo(location.pathname, location.search))}`}
                >
                  <SettingsIcon className="app-button-icon" />
                  Edit policy
                </Link>
              ) : null}
            </TrackerPreviewActions>
          </div>
        ) : null}
      </PreviewDrawer>
    </div>
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
  const navigate = useNavigate();
  const location = useLocation();
  const params = useParams();
  const infoHash = params.infoHash ?? "";
  const isCreate = !infoHash;
  const returnTo = sanitizeReturnTo(new URLSearchParams(location.search).get("returnTo"), "/torrents");
  const [infoHashInput, setInfoHashInput] = useState(infoHash);
  const [current, setCurrent] = useState<TorrentAdminDto | null>(null);
  const [form, setForm] = useState<TorrentPolicyFormState | null>(
    isCreate
      ? {
          isPrivate: true,
          isEnabled: true,
          announceIntervalSeconds: 1800,
          minAnnounceIntervalSeconds: 900,
          defaultNumWant: 50,
          maxNumWant: 100,
          allowScrape: true,
          expectedVersion: undefined
        }
      : null
  );
  const [dryRun, setDryRun] = useState<TorrentPolicyDryRunItemDto | null>(null);
  const [status, setStatus] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [confirmDeleteOpen, setConfirmDeleteOpen] = useState(false);

  useEffect(() => {
    if (isCreate) {
      return;
    }

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
  }, [accessToken, infoHash, isCreate, onReauthenticate]);

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
              infoHash: infoHashInput.trim(),
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
      const targetInfoHash = infoHashInput.trim();
      if (!targetInfoHash) {
        throw new Error("Info hash is required.");
      }

      const snapshot = await apiMutation<TorrentAdminDto, ReturnType<typeof buildPayload>>(
        `/api/admin/torrents/${encodeURIComponent(targetInfoHash)}/policy`,
        "PUT",
        accessToken,
        buildPayload(),
        onReauthenticate
      );

      setCurrent(snapshot);
      setInfoHashInput(snapshot.infoHash);
      setForm(toPolicyForm(snapshot));
      setDryRun(null);
      navigate(returnTo, {
        replace: true,
        state: {
          message: isCreate ? "Torrent policy created." : labels.updated,
          tone: "good"
        } satisfies NavigationBannerState
      });
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : labels.saveFailed);
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleDelete = async () => {
    try {
      setIsSubmitting(true);
      setStatus(null);
      setError(null);
      if (!current) {
        throw new Error("Torrent is not loaded.");
      }

      await apiDelete(
        `/api/admin/torrents/${encodeURIComponent(current.infoHash)}?expectedVersion=${encodeURIComponent(String(current.version))}`,
        accessToken,
        onReauthenticate
      );

      navigate(returnTo, {
        replace: true,
        state: {
          message: "Torrent deleted.",
          tone: "good"
        } satisfies NavigationBannerState
      });
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : "Deleting torrent failed.");
    } finally {
      setIsSubmitting(false);
    }
  };

  if (error && !form) {
    return (
      <TrackerEditorLayout
        eyebrow={dictionary.routes.torrentPolicyEyebrow}
        title={dictionary.routes.torrentPolicyTitle}
        description={labels.loading}
        error={error}
      >
        <></>
      </TrackerEditorLayout>
    );
  }

  if (!form || (!isCreate && !current)) {
    return (
      <TrackerEditorLayout
        eyebrow={dictionary.routes.torrentPolicyEyebrow}
        title={dictionary.routes.torrentPolicyTitle}
        description={labels.loading}
      >
        <div className="text-sm text-ink/60">{labels.loading}</div>
      </TrackerEditorLayout>
    );
  }

  return (
    <TrackerEditorLayout
      eyebrow={labels.editEyebrow}
      title={labels.editTitle}
      description={isCreate ? "Create a torrent policy in a dedicated editor." : "Update or remove a torrent policy outside the list grid."}
      error={error}
      message={status}
    >
      <div className="grid gap-6 xl:grid-cols-[1.1fr,0.9fr]">
        <div className="space-y-6">
          <label className="space-y-2">
            <span className="text-sm font-medium text-ink">{labels.infoHash}</span>
            <input
              className="app-input font-mono text-sm"
              value={infoHashInput}
              disabled={!isCreate || isSubmitting}
              onChange={(event) => setInfoHashInput(event.target.value)}
            />
          </label>
          <div className="grid gap-5 md:grid-cols-2">
            <label className="space-y-2">
              <span className="text-sm font-medium text-ink">{labels.announceInterval}</span>
              <input className="app-input" type="number" value={form.announceIntervalSeconds} onChange={(event) => updateField("announceIntervalSeconds", Number(event.target.value))} />
            </label>
            <label className="space-y-2">
              <span className="text-sm font-medium text-ink">{labels.minAnnounceInterval}</span>
              <input className="app-input" type="number" value={form.minAnnounceIntervalSeconds} onChange={(event) => updateField("minAnnounceIntervalSeconds", Number(event.target.value))} />
            </label>
            <label className="space-y-2">
              <span className="text-sm font-medium text-ink">{labels.defaultNumwant}</span>
              <input className="app-input" type="number" value={form.defaultNumWant} onChange={(event) => updateField("defaultNumWant", Number(event.target.value))} />
            </label>
            <label className="space-y-2">
              <span className="text-sm font-medium text-ink">{labels.maxNumwant}</span>
              <input className="app-input" type="number" value={form.maxNumWant} onChange={(event) => updateField("maxNumWant", Number(event.target.value))} />
            </label>
          </div>
          <div className="grid gap-3 md:grid-cols-3">
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
          <TrackerEditorFooter
            left={!isCreate ? (
              <button
                type="button"
                disabled={isSubmitting}
                onClick={() => setConfirmDeleteOpen(true)}
                className="app-button-danger inline-flex items-center gap-2"
              >
                <TrashIcon className="app-button-icon" />
                Delete torrent
              </button>
            ) : undefined}
            right={
              <>
                <button type="button" className="app-button-secondary" disabled={isSubmitting} onClick={() => navigate(returnTo)}>
                  Cancel
                </button>
                <button
                  type="button"
                  disabled={isSubmitting}
                  onClick={() => void handleDryRun()}
                  className="app-button-secondary inline-flex items-center gap-2"
                >
                  <EyeIcon className="app-button-icon" />
                  {labels.previewChanges}
                </button>
                <button
                  type="button"
                  disabled={isSubmitting || !infoHashInput.trim()}
                  onClick={() => void handleSave()}
                  className="app-button-primary inline-flex items-center gap-2"
                >
                  <PencilIcon className="app-button-icon" />
                  {isCreate ? "Create torrent" : labels.applyPolicy}
                </button>
              </>
            }
          />
        </div>
        <div className="app-editor-sidebar">
          <TrackerEditorSummary
            eyebrow={labels.previewEyebrow}
            title={current ? labels.currentSnapshot : "Draft policy"}
            items={[
              { label: labels.infoHash, value: infoHashInput || "Not set", tone: "mono" },
              { label: dictionary.common.mode, value: current ? formatMode(current.isPrivate, dictionary) : formatMode(form.isPrivate, dictionary) },
              { label: dictionary.common.state, value: current ? formatState(current.isEnabled, dictionary) : formatState(form.isEnabled, dictionary) },
              { label: dictionary.common.interval, value: `${current?.announceIntervalSeconds ?? form.announceIntervalSeconds}s / min ${current?.minAnnounceIntervalSeconds ?? form.minAnnounceIntervalSeconds}s` },
              { label: "Numwant", value: `${current?.defaultNumWant ?? form.defaultNumWant} / ${current?.maxNumWant ?? form.maxNumWant}` },
              { label: dictionary.common.scrape, value: current ? formatScrape(current.allowScrape, dictionary) : formatScrape(form.allowScrape, dictionary) },
              { label: dictionary.common.version, value: current?.version ?? form.expectedVersion ?? "New" }
            ]}
          >
            {!current ? <div className="text-sm text-steel">A new torrent policy will appear here after the first save.</div> : null}
          </TrackerEditorSummary>
          {dryRun ? (
            <TrackerEditorSummary
              eyebrow={labels.previewEyebrow}
              title={labels.dryRunResult}
              items={[
                { label: dictionary.common.mode, value: formatMode(dryRun.proposedSnapshot.isPrivate, dictionary) },
                { label: dictionary.common.state, value: formatState(dryRun.proposedSnapshot.isEnabled, dictionary) },
                { label: dictionary.common.interval, value: `${dryRun.proposedSnapshot.announceIntervalSeconds}s / min ${dryRun.proposedSnapshot.minAnnounceIntervalSeconds}s` },
                { label: "Numwant", value: `${dryRun.proposedSnapshot.defaultNumWant} / ${dryRun.proposedSnapshot.maxNumWant}` },
                { label: dictionary.common.scrape, value: formatScrape(dryRun.proposedSnapshot.allowScrape, dictionary) },
                { label: labels.versionTarget, value: dryRun.proposedSnapshot.version }
              ]}
            >
              <StatusPill tone={dryRun.canApply ? "good" : "warn"}>{dryRun.canApply ? labels.canApply : labels.rejected}</StatusPill>
              {dryRun.errorMessage ? <div className="text-sm text-ember">{dryRun.errorMessage}</div> : null}
              {dryRun.warnings.length > 0 ? (
                <div className="space-y-2">
                  <div className="app-kicker">{dictionary.common.warnings}</div>
                  <ul className="space-y-1 text-sm text-ember">
                    {dryRun.warnings.map((warning) => (
                      <li key={warning}>{warning}</li>
                    ))}
                  </ul>
                </div>
              ) : null}
            </TrackerEditorSummary>
          ) : (
            <TrackerEditorSummary
              eyebrow={labels.previewEyebrow}
              title={labels.previewTitle}
              items={[{ label: "Preview", value: labels.previewHint }]}
            />
          )}
        </div>
      </div>
      <ConfirmActionModal
        open={confirmDeleteOpen}
        title="Delete torrent"
        description={`Delete ${infoHashInput}? This removes the configured torrent policy.`}
        confirmLabel="Delete torrent"
        onClose={() => setConfirmDeleteOpen(false)}
        onConfirm={() => {
          setConfirmDeleteOpen(false);
          void handleDelete();
        }}
      />
    </TrackerEditorLayout>
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
        <div className="mt-6">
          <TrackerEditorFooter
            right={
              <>
                <button
                  type="button"
                  disabled={isSubmitting}
                  onClick={() => {
                    clearBulkTorrentPolicySelection();
                    navigate("/torrents", { replace: true });
                  }}
                  className="app-button-secondary"
                >
                  {labels.backToCatalog}
                </button>
                <button
                  type="button"
                  disabled={isSubmitting || selection.length === 0}
                  onClick={() => void runDryRun()}
                  className="app-button-secondary inline-flex items-center gap-2"
                >
                  <EyeIcon className="app-button-icon" />
                  {labels.previewRollout}
                </button>
                <button
                  type="button"
                  disabled={isSubmitting || selection.length === 0}
                  onClick={() => void applyBulkUpdate()}
                  className="app-button-primary inline-flex items-center gap-2"
                >
                  <PencilIcon className="app-button-icon" />
                  {labels.applyRollout}
                </button>
              </>
            }
          />
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
  const navigate = useNavigate();
  const location = useLocation();
  const [view, setView] = useCatalogViewState({
    search: "",
    filter: "all",
    sort: "userid:asc",
    page: 1,
    pageSize: 25
  });
  const { query, preview } = view;
  const [items, setItems] = useState<PasskeyAdminDto[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [result, setResult] = useState<BulkOperationResultDto | null>(null);
  const [status, setStatus] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const canManage = hasGrantedCapability(capabilities, "admin.write.passkey");
  const canBatchAct =
    hasGrantedCapability(capabilities, "admin.revoke.passkey") ||
    hasGrantedCapability(capabilities, "admin.rotate.passkey");
  const previewItem = items.find((item) => item.passkeyMask === preview) ?? null;
  const [activeSortField, activeSortDirection = "asc"] = query.sort.split(":");
  const pageCount = Math.max(1, Math.ceil(totalCount / query.pageSize));

  useEffect(() => {
    const banner = location.state as NavigationBannerState | null;
    if (!banner?.message) {
      return;
    }

    setStatus(banner.message);
    navigate(location.pathname, { replace: true, state: null });
  }, [location.pathname, location.state, navigate]);

  const reload = async () => {
    const page = await apiRequest<PageResult<PasskeyAdminDto>>(
      `/api/admin/passkeys?${buildGridQueryString(query)}`,
      accessToken,
      onReauthenticate
    );
    setItems(page.items);
    setTotalCount(page.totalCount);
  };

  useEffect(() => {
    let isMounted = true;

    setIsLoading(true);
    reload()
      .then(() => {
        if (isMounted) {
          setError(null);
        }
      })
      .catch((requestError) => {
        if (isMounted) {
          setError(requestError instanceof Error ? requestError.message : labels.loadError);
          setItems([]);
          setTotalCount(0);
        }
      })
      .finally(() => {
        if (isMounted) {
          setIsLoading(false);
        }
      });

    return () => {
      isMounted = false;
    };
  }, [accessToken, onReauthenticate, query]);

  useEffect(() => {
    if (preview && !items.some((item) => item.passkeyMask === preview)) {
      setView((current) => ({ ...current, preview: null }));
    }
  }, [items, preview, setView]);

  const openActionModal = () => {
    setError(null);
    setStatus(null);
    setResult(null);
    navigate(`/passkeys/actions?returnTo=${encodeURIComponent(buildReturnTo(location.pathname, location.search))}`);
  };

  const openCreate = () => {
    setError(null);
    setStatus(null);
    setResult(null);
    navigate(`/passkeys/new?returnTo=${encodeURIComponent(buildReturnTo(location.pathname, location.search))}`);
  };

  return (
    <div className="app-page-stack">
      <CatalogToolbar
        title={labels.cardTitle}
        description="Search passkeys and run revoke or rotate workflows."
        totalCount={totalCount}
        search={query.search}
        onSearchChange={(value) => setView((current) => ({ ...current, query: { ...current.query, search: value, page: 1 } }))}
        searchPlaceholder="Search passkeys"
        filter={query.filter}
        onFilterChange={(value) => setView((current) => ({ ...current, query: { ...current.query, filter: value, page: 1 } }))}
        filterOptions={[
          { value: "all", label: "All passkeys" },
          { value: "active", label: "Active" },
          { value: "revoked", label: "Revoked" },
          { value: "expired", label: "Expired" }
        ]}
        sortValue={query.sort}
        onSortChange={(value) => setView((current) => ({ ...current, query: { ...current.query, sort: value, page: 1 } }))}
        sortOptions={[
          { value: "userid:asc", label: "User A-Z" },
          { value: "userid:desc", label: "User Z-A" },
          { value: "expires:desc", label: "Expiry latest first" },
          { value: "expires:asc", label: "Expiry earliest first" },
          { value: "version:desc", label: "Version high-low" }
        ]}
        pageSize={query.pageSize}
        onPageSizeChange={(value) => setView((current) => ({ ...current, query: { ...current.query, pageSize: value, page: 1 } }))}
        secondaryLabel={canBatchAct ? "Batch actions" : undefined}
        secondaryHref={canBatchAct ? `/passkeys/actions?returnTo=${encodeURIComponent(buildReturnTo(location.pathname, location.search))}` : undefined}
        createLabel={canManage ? "Create passkey" : undefined}
        createHref={canManage ? `/passkeys/new?returnTo=${encodeURIComponent(buildReturnTo(location.pathname, location.search))}` : undefined}
      />
      {status ? <div className="app-notice-success">{status}</div> : null}
      {error ? <div className="app-notice-warn">{error}</div> : null}
      {result && result.passkeyItems.length > 0 ? (
        <div className="app-selection-summary">
          <div className="space-y-1">
            <div className="text-sm font-semibold text-ink">Last batch action</div>
            <div className="text-sm text-steel">{result.succeededCount} succeeded, {result.failedCount} failed, {result.totalCount} processed</div>
          </div>
          <button type="button" className="app-button-secondary py-2.5" onClick={() => setResult(null)}>Clear result</button>
        </div>
      ) : null}
      <div className="app-data-table">
        <div className="overflow-x-auto">
          <table className="min-w-full text-sm">
            <thead className="app-table-head">
              <tr>
                <th className="px-5 py-4"><SortHeaderButton label={labels.tableMask} active={activeSortField === "mask"} direction={activeSortDirection as "asc" | "desc"} onClick={() => setView((current) => ({ ...current, query: { ...current.query, sort: activeSortField === "mask" && activeSortDirection === "asc" ? "mask:desc" : "mask:asc", page: 1 } }))} /></th>
                <th className="px-5 py-4"><SortHeaderButton label={labels.tableUser} active={activeSortField === "userid"} direction={activeSortDirection as "asc" | "desc"} onClick={() => setView((current) => ({ ...current, query: { ...current.query, sort: activeSortField === "userid" && activeSortDirection === "asc" ? "userid:desc" : "userid:asc", page: 1 } }))} /></th>
                <th className="px-5 py-4">{labels.tableState}</th>
                <th className="px-5 py-4"><SortHeaderButton label={labels.tableExpires} active={activeSortField === "expires"} direction={activeSortDirection as "asc" | "desc"} onClick={() => setView((current) => ({ ...current, query: { ...current.query, sort: activeSortField === "expires" && activeSortDirection === "asc" ? "expires:desc" : "expires:asc", page: 1 } }))} /></th>
                <th className="px-5 py-4"><SortHeaderButton label={labels.tableVersion} active={activeSortField === "version"} direction={activeSortDirection as "asc" | "desc"} onClick={() => setView((current) => ({ ...current, query: { ...current.query, sort: activeSortField === "version" && activeSortDirection === "asc" ? "version:desc" : "version:asc", page: 1 } }))} /></th>
                <th className="px-5 py-4 text-right">Actions</th>
              </tr>
            </thead>
            <tbody>
              {isLoading ? (
                <TableStateRow colSpan={6} title="Loading passkeys" message="Refreshing masked tracker credentials from the backend." />
              ) : error && totalCount === 0 ? (
                <TableStateRow colSpan={6} title="Unable to load passkeys" message={error} />
              ) : items.length === 0 ? (
                <TableStateRow colSpan={6} title="No passkeys match this view" message="Try a broader search or change the current filter." />
              ) : items.map((item) => (
                <CatalogTableRow key={`${item.passkeyMask}-${item.userId}`} onOpen={() => setView((current) => ({ ...current, preview: item.passkeyMask }))}>
                  <td className="px-5 py-4">
                    <div className="app-grid-primary font-mono">{item.passkeyMask}</div>
                    <div className="app-inline-id">
                      <CopyValueButton value={item.passkeyMask} label={`Copy passkey mask ${item.passkeyMask}`} />
                    </div>
                  </td>
                  <td className="px-5 py-4">
                    <div className="app-grid-primary font-mono">{item.userId}</div>
                    <div className="app-inline-id">
                      <CopyValueButton value={item.userId} label={`Copy user id ${item.userId}`} />
                    </div>
                  </td>
                  <td className="px-5 py-4"><StatusPill tone={item.isRevoked ? "warn" : "good"}>{item.isRevoked ? dictionary.common.revoked : dictionary.common.active}</StatusPill></td>
                  <td className="px-5 py-4 text-steel">{item.expiresAtUtc ? new Date(item.expiresAtUtc).toLocaleString() : dictionary.common.never}</td>
                  <td className="px-5 py-4 text-steel">{item.version}</td>
                  <td className="px-5 py-4 text-right">
                    <div className="flex items-center justify-end gap-2">
                        {canManage ? (
                          <Link className="app-button-secondary py-2.5 inline-flex items-center gap-2" to={`/passkeys/${encodeURIComponent(item.id)}/edit?returnTo=${encodeURIComponent(buildReturnTo(location.pathname, location.search))}`}><PencilIcon className="app-button-icon" />Edit</Link>
                        ) : null}
                      <RowActionsMenu items={[{ label: "Preview", icon: <EyeIcon />, onClick: () => setView((current) => ({ ...current, preview: item.passkeyMask })) }]} />
                    </div>
                  </td>
                </CatalogTableRow>
              ))}
            </tbody>
          </table>
        </div>
      </div>
      <PaginationFooter page={query.page} pageCount={pageCount} totalCount={totalCount} pageSize={query.pageSize} onPageChange={(page) => setView((current) => ({ ...current, query: { ...current.query, page } }))} />
      <PreviewDrawer open={previewItem != null} title={previewItem?.passkeyMask ?? ""} subtitle={previewItem ? `User ${previewItem.userId}` : undefined} onClose={() => setView((current) => ({ ...current, preview: null }))}>
        {previewItem ? (
          <div className="space-y-4">
            <TrackerReadOnlySummary
              items={[
                { label: "Owner", value: previewItem.userId, tone: "mono" },
                { label: "State", value: <StatusPill tone={previewItem.isRevoked ? "warn" : "good"}>{previewItem.isRevoked ? dictionary.common.revoked : dictionary.common.active}</StatusPill> },
                { label: "Expires", value: previewItem.expiresAtUtc ? new Date(previewItem.expiresAtUtc).toLocaleString() : dictionary.common.never },
                { label: "Version", value: previewItem.version }
              ]}
            />
            {result?.passkeyItems?.some((item) => item.passkeyMask === previewItem.passkeyMask) ? (
              <TrackerOperationSummary>
                {result.passkeyItems.filter((item) => item.passkeyMask === previewItem.passkeyMask).map((item) => (
                  <div key={`${item.passkeyMask}-${item.newPasskeyMask ?? "same"}`} className="app-subtle-panel space-y-2">
                    <div className="flex items-center justify-between gap-3">
                      <StatusPill tone={item.succeeded ? "good" : "warn"}>{item.succeeded ? labels.completed : labels.failed}</StatusPill>
                      {item.newPasskeyMask ? <div className="font-mono text-xs text-steel">{item.newPasskeyMask}</div> : null}
                    </div>
                    {item.errorMessage ? <div className="text-sm text-ember">{item.errorMessage}</div> : null}
                    {item.newPasskey ? <div className="rounded-2xl bg-moss/10 px-3 py-3"><div className="text-xs uppercase tracking-[0.18em] text-moss">{labels.newPasskey}</div><div className="mt-1 break-all font-mono text-xs text-moss">{item.newPasskey}</div></div> : null}
                  </div>
                ))}
              </TrackerOperationSummary>
            ) : null}
            {canManage ? (
                <TrackerPreviewActions>
                  <Link className="app-button-primary inline-flex items-center gap-2" to={`/passkeys/${encodeURIComponent(previewItem.id)}/edit?returnTo=${encodeURIComponent(buildReturnTo(location.pathname, location.search))}`}><PencilIcon className="app-button-icon" />Edit passkey</Link>
                </TrackerPreviewActions>
            ) : null}
          </div>
        ) : null}
      </PreviewDrawer>
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
  const navigate = useNavigate();
  const location = useLocation();
  const [view, setView] = useCatalogViewState({
    search: "",
    filter: "all",
    sort: "scope:asc",
    page: 1,
    pageSize: 25
  });
  const { query, preview } = view;
  const [items, setItems] = useState<BanRuleAdminDto[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [status, setStatus] = useState<string | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<BanRuleAdminDto | null>(null);
  const [confirmDeleteOpen, setConfirmDeleteOpen] = useState(false);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [isLoading, setIsLoading] = useState(true);

  const canWrite = hasGrantedCapability(capabilities, "admin.write.ban");
  const canDelete = hasGrantedCapability(capabilities, "admin.delete.ban");
  const previewItem = items.find((item) => toBanRecordId(item.scope, item.subject) === preview) ?? null;

  useEffect(() => {
    const banner = location.state as NavigationBannerState | null;
    if (!banner?.message) {
      return;
    }

    setStatus(banner.message);
    navigate(location.pathname, { replace: true, state: null });
  }, [location.pathname, location.state, navigate]);

  const reload = async () => {
      const page = await apiRequest<PageResult<BanRuleAdminDto>>(`/api/admin/bans?${buildGridQueryString(query)}`, accessToken, onReauthenticate);
      setItems(page.items);
      setTotalCount(page.totalCount);
  };

  useEffect(() => {
    let isMounted = true;

    setIsLoading(true);
    reload()
      .then(() => {
        if (isMounted) {
          setError(null);
        }
      })
      .catch((requestError) => {
        if (isMounted) {
          setError(requestError instanceof Error ? requestError.message : labels.loadError);
          setItems([]);
          setTotalCount(0);
        }
      })
      .finally(() => {
        if (isMounted) {
          setIsLoading(false);
        }
      });

    return () => {
      isMounted = false;
    };
  }, [accessToken, onReauthenticate, query]);

  const openCreate = () => {
    navigate(`/bans/new?returnTo=${encodeURIComponent(buildReturnTo(location.pathname, location.search))}`);
    setError(null);
    setStatus(null);
  };

  const deleteBan = async () => {
    if (!deleteTarget) {
      return;
    }

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
              scope: deleteTarget.scope,
              subject: deleteTarget.subject,
              expectedVersion: deleteTarget.version
            }
          ]
        },
        onReauthenticate
      );

      setStatus(labels.deleteStatus);
      setDeleteTarget(null);
      await reload();
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : labels.deleteError);
    } finally {
      setIsSubmitting(false);
    }
  };

  const [activeSortField, activeSortDirection = "asc"] = query.sort.split(":");
  const pageCount = Math.max(1, Math.ceil(totalCount / query.pageSize));

  return (
    <div className="space-y-6">
      <CatalogToolbar
        title={labels.cardTitle}
        description="Search and manage enforcement rules."
        totalCount={totalCount}
        search={query.search}
        onSearchChange={(value) => setView((current) => ({ ...current, query: { ...current.query, search: value, page: 1 } }))}
        searchPlaceholder="Search rules"
        filter={query.filter}
        onFilterChange={(value) => setView((current) => ({ ...current, query: { ...current.query, filter: value, page: 1 } }))}
        filterOptions={[
          { value: "all", label: "All rules" },
          { value: "active", label: "Active" },
          { value: "expired", label: "Expired" }
        ]}
        sortValue={query.sort}
        onSortChange={(value) => setView((current) => ({ ...current, query: { ...current.query, sort: value, page: 1 } }))}
        sortOptions={[
          { value: "scope:asc", label: "Scope A-Z" },
          { value: "subject:asc", label: "Subject A-Z" },
          { value: "expires:desc", label: "Expiry latest first" },
          { value: "expires:asc", label: "Expiry earliest first" }
        ]}
        pageSize={query.pageSize}
        onPageSizeChange={(value) => setView((current) => ({ ...current, query: { ...current.query, pageSize: value, page: 1 } }))}
        createLabel={canWrite ? "Create ban rule" : undefined}
        createHref={canWrite ? `/bans/new?returnTo=${encodeURIComponent(buildReturnTo(location.pathname, location.search))}` : undefined}
      />
      {status ? <div className="app-notice-success">{status}</div> : null}
      {error ? <div className="app-notice-warn">{error}</div> : null}

      <div className="app-data-table">
        <div className="overflow-x-auto">
          <table className="min-w-full text-sm">
            <thead className="app-table-head">
              <tr>
                <th className="px-5 py-4"><SortHeaderButton label={labels.scope} active={activeSortField === "scope"} direction={activeSortDirection as "asc" | "desc"} onClick={() => setView((current) => ({ ...current, query: { ...current.query, sort: activeSortField === "scope" && activeSortDirection === "asc" ? "scope:desc" : "scope:asc", page: 1 } }))} /></th>
                <th className="px-5 py-4"><SortHeaderButton label={labels.subject} active={activeSortField === "subject"} direction={activeSortDirection as "asc" | "desc"} onClick={() => setView((current) => ({ ...current, query: { ...current.query, sort: activeSortField === "subject" && activeSortDirection === "asc" ? "subject:desc" : "subject:asc", page: 1 } }))} /></th>
                <th className="px-5 py-4">{labels.reason}</th>
                <th className="px-5 py-4"><SortHeaderButton label={labels.expiresLabel} active={activeSortField === "expires"} direction={activeSortDirection as "asc" | "desc"} onClick={() => setView((current) => ({ ...current, query: { ...current.query, sort: activeSortField === "expires" && activeSortDirection === "asc" ? "expires:desc" : "expires:asc", page: 1 } }))} /></th>
                <th className="px-5 py-4">{dictionary.common.version}</th>
                <th className="px-5 py-4 text-right">Actions</th>
              </tr>
            </thead>
            <tbody>
              {isLoading ? (
                <TableStateRow colSpan={6} title="Loading ban rules" message="Refreshing the enforcement catalog from the backend." />
              ) : error && totalCount === 0 ? (
                <TableStateRow colSpan={6} title="Unable to load ban rules" message={error} />
              ) : items.length === 0 ? (
                <TableStateRow colSpan={6} title="No ban rules match this view" message="Try a broader search or switch the current filter." />
              ) : items.map((item) => (
                <CatalogTableRow key={`${item.scope}:${item.subject}`} onOpen={() => setView((current) => ({ ...current, preview: toBanRecordId(item.scope, item.subject) }))}>
                  <td className="px-5 py-4"><div className="app-grid-primary">{item.scope}</div></td>
                  <td className="px-5 py-4">
                    <div className="app-grid-primary font-mono">{item.subject}</div>
                    <div className="app-inline-id">
                      <CopyValueButton value={item.subject} label={`Copy ban subject ${item.subject}`} />
                    </div>
                  </td>
                  <td className="px-5 py-4 text-steel">{item.reason}</td>
                  <td className="px-5 py-4 text-steel">{item.expiresAtUtc ? new Date(item.expiresAtUtc).toLocaleString() : dictionary.common.never}</td>
                  <td className="px-5 py-4 text-steel">{item.version}</td>
                  <td className="px-5 py-4 text-right">
                    <div className="flex justify-end items-center gap-2">
                        {canWrite ? (
                          <Link className="app-button-secondary py-2.5 inline-flex items-center gap-2" to={`/bans/${toBanRecordId(item.scope, item.subject)}/edit?returnTo=${encodeURIComponent(buildReturnTo(location.pathname, location.search))}`}><PencilIcon className="app-button-icon" />Edit</Link>
                        ) : null}
                      <RowActionsMenu
                        items={[
                          {
                            label: "Preview",
                            icon: <EyeIcon />,
                            onClick: () => setView((current) => ({ ...current, preview: toBanRecordId(item.scope, item.subject) }))
                          },
                          {
                            label: "Delete",
                            icon: <TrashIcon />,
                            tone: "danger",
                            disabled: !canDelete,
                            onClick: () => {
                              setDeleteTarget(item);
                              setConfirmDeleteOpen(true);
                            }
                          }
                        ]}
                      />
                    </div>
                  </td>
                </CatalogTableRow>
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
        onPageChange={(page) => setView((current) => ({ ...current, query: { ...current.query, page } }))}
      />
      <ConfirmActionModal
        open={confirmDeleteOpen}
        title="Delete ban rule"
        description={deleteTarget ? `Delete rule ${deleteTarget.scope}:${deleteTarget.subject}? This cannot be undone.` : "Delete this rule?"}
        confirmLabel="Delete rule"
        onClose={() => setConfirmDeleteOpen(false)}
        onConfirm={() => {
          setConfirmDeleteOpen(false);
          void deleteBan();
        }}
      />
      <PreviewDrawer open={previewItem != null} title={previewItem ? `${previewItem.scope}:${previewItem.subject}` : ""} subtitle={previewItem?.reason} onClose={() => setView((current) => ({ ...current, preview: null }))}>
        {previewItem ? (
          <div className="space-y-4">
            <TrackerReadOnlySummary
              items={[
                { label: "Scope", value: previewItem.scope },
                { label: "Subject", value: previewItem.subject, tone: "mono" },
                { label: "Expires", value: previewItem.expiresAtUtc ? new Date(previewItem.expiresAtUtc).toLocaleString() : dictionary.common.never },
                { label: "Version", value: previewItem.version }
              ]}
            />
            {canWrite ? (
                <TrackerPreviewActions>
                  <Link className="app-button-primary inline-flex items-center gap-2" to={`/bans/${toBanRecordId(previewItem.scope, previewItem.subject)}/edit?returnTo=${encodeURIComponent(buildReturnTo(location.pathname, location.search))}`}><PencilIcon className="app-button-icon" />Edit rule</Link>
                </TrackerPreviewActions>
            ) : null}
          </div>
        ) : null}
      </PreviewDrawer>
    </div>
  );
}

function TrackerAccessPage({
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
  const navigate = useNavigate();
  const location = useLocation();
  const [view, setView] = useCatalogViewState({
    search: "",
    filter: "all",
    sort: "userid:asc",
    page: 1,
    pageSize: 25
  });
  const { query, preview } = view;
  const [items, setItems] = useState<TrackerAccessAdminDto[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [status, setStatus] = useState<string | null>(null);
  const [result, setResult] = useState<BulkOperationResultDto | null>(null);
  const [selectedUserIds, setSelectedUserIds] = useState<string[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const canWrite = hasGrantedCapability(capabilities, "admin.write.permissions");
  const trackerAccessItems = result?.trackerAccessItems ?? result?.permissionItems ?? [];
  const previewItem = items.find((item) => item.userId === preview) ?? null;
  const [activeSortField, activeSortDirection = "asc"] = query.sort.split(":");
  const pageCount = Math.max(1, Math.ceil(totalCount / query.pageSize));

  useEffect(() => {
    const banner = location.state as NavigationBannerState | null;
    if (!banner?.message) {
      return;
    }

    setStatus(banner.message);
    navigate(location.pathname, { replace: true, state: null });
  }, [location.pathname, location.state, navigate]);

  const reload = async () => {
    const page = await apiRequest<PageResult<TrackerAccessAdminDto>>(
      `/api/admin/tracker-access?${buildGridQueryString(query)}`,
      accessToken,
      onReauthenticate
    );
    setItems(page.items);
    setTotalCount(page.totalCount);
    setSelectedUserIds((current) => current.filter((userId) => page.items.some((item) => item.userId === userId)));
  };

  useEffect(() => {
    let isMounted = true;

    setIsLoading(true);
    reload()
      .then(() => {
        if (isMounted) {
          setError(null);
        }
      })
      .catch((requestError) => {
        if (isMounted) {
          setError(requestError instanceof Error ? requestError.message : labels.loadError);
          setItems([]);
          setTotalCount(0);
        }
      })
      .finally(() => {
        if (isMounted) {
          setIsLoading(false);
        }
      });

    return () => {
      isMounted = false;
    };
  }, [accessToken, onReauthenticate, query]);

  useEffect(() => {
    if (preview && !items.some((item) => item.userId === preview)) {
      setView((current) => ({ ...current, preview: null }));
    }
  }, [items, preview, setView]);

  const openCreate = () => {
    navigate(`/permissions/new?returnTo=${encodeURIComponent(buildReturnTo(location.pathname, location.search))}`);
    setError(null);
    setStatus(null);
  };

  const openBulkEditor = () => {
    persistBulkTrackerAccessSelection(items.filter((item) => selectedUserIds.includes(item.userId)));
    navigate(`/permissions/bulk-edit?returnTo=${encodeURIComponent(buildReturnTo(location.pathname, location.search))}`);
    setError(null);
  };

  const toggleSelection = (userId: string) => {
    setSelectedUserIds((current) =>
      current.includes(userId) ? current.filter((value) => value !== userId) : [...current, userId]
    );
  };

  return (
    <div className="app-page-stack">
      <CatalogToolbar
        title={labels.cardTitle}
        description="Search and manage tracker access assignments."
        totalCount={totalCount}
        search={query.search}
        onSearchChange={(value) => setView((current) => ({ ...current, query: { ...current.query, search: value, page: 1 } }))}
        searchPlaceholder="Search users or access state"
        filter={query.filter}
        onFilterChange={(value) => setView((current) => ({ ...current, query: { ...current.query, filter: value, page: 1 } }))}
        filterOptions={[
          { value: "all", label: "All access" },
          { value: "private", label: "Private tracker" },
          { value: "public", label: "Public only" },
          { value: "seed", label: "Can seed" },
          { value: "leech", label: "Can leech" },
          { value: "scrape", label: "Can scrape" }
        ]}
        sortValue={query.sort}
        onSortChange={(value) => setView((current) => ({ ...current, query: { ...current.query, sort: value, page: 1 } }))}
        sortOptions={[
          { value: "userid:asc", label: "User A-Z" },
          { value: "userid:desc", label: "User Z-A" },
          { value: "private:desc", label: "Private first" },
          { value: "leech:desc", label: "Leech enabled first" },
          { value: "seed:desc", label: "Seed enabled first" },
          { value: "version:desc", label: "Version high-low" }
        ]}
        pageSize={query.pageSize}
        onPageSizeChange={(value) => setView((current) => ({ ...current, query: { ...current.query, pageSize: value, page: 1 } }))}
        secondaryLabel={canWrite ? "Bulk edit" : undefined}
        secondaryHref={canWrite ? `/permissions/bulk-edit?returnTo=${encodeURIComponent(buildReturnTo(location.pathname, location.search))}` : undefined}
        createLabel={canWrite ? "Create access" : undefined}
        createHref={canWrite ? `/permissions/new?returnTo=${encodeURIComponent(buildReturnTo(location.pathname, location.search))}` : undefined}
      />
      {status ? <div className="app-notice-success">{status}</div> : null}
      {error ? <div className="app-notice-warn">{error}</div> : null}
      {result && trackerAccessItems.length > 0 ? (
        <div className="app-selection-summary">
          <div className="space-y-1">
            <div className="text-sm font-semibold text-ink">Last access update</div>
            <div className="text-sm text-steel">{result.succeededCount} succeeded, {result.failedCount} failed, {result.totalCount} processed</div>
          </div>
          <button type="button" className="app-button-secondary py-2.5" onClick={() => setResult(null)}>Clear result</button>
        </div>
      ) : null}
      {selectedUserIds.length > 0 ? (
        <div className="app-selection-summary">
          <div className="space-y-1">
            <div className="text-sm font-semibold text-ink">{selectedUserIds.length} user(s) selected</div>
            <div className="text-sm text-steel">Open the dedicated bulk editor for the current tracker access selection.</div>
          </div>
          <div className="flex flex-wrap gap-3">
            <button type="button" className="app-button-secondary py-2.5" onClick={() => setSelectedUserIds([])}>Clear selection</button>
            <button type="button" className="app-button-primary py-2.5 inline-flex items-center gap-2" disabled={!canWrite} onClick={openBulkEditor}><PencilIcon className="app-button-icon" />{labels.applySelected} ({selectedUserIds.length})</button>
          </div>
        </div>
      ) : null}
      <div className="app-data-table">
        <div className="overflow-x-auto">
          <table className="min-w-full text-sm">
            <thead className="app-table-head">
              <tr>
                <th className="w-14 px-5 py-4" />
                <th className="px-5 py-4"><SortHeaderButton label={labels.userId} active={activeSortField === "userid"} direction={activeSortDirection as "asc" | "desc"} onClick={() => setView((current) => ({ ...current, query: { ...current.query, sort: activeSortField === "userid" && activeSortDirection === "asc" ? "userid:desc" : "userid:asc", page: 1 } }))} /></th>
                <th className="px-5 py-4"><SortHeaderButton label={labels.leech} active={activeSortField === "leech"} direction={activeSortDirection as "asc" | "desc"} onClick={() => setView((current) => ({ ...current, query: { ...current.query, sort: activeSortField === "leech" && activeSortDirection === "asc" ? "leech:desc" : "leech:asc", page: 1 } }))} /></th>
                <th className="px-5 py-4"><SortHeaderButton label={labels.seed} active={activeSortField === "seed"} direction={activeSortDirection as "asc" | "desc"} onClick={() => setView((current) => ({ ...current, query: { ...current.query, sort: activeSortField === "seed" && activeSortDirection === "asc" ? "seed:desc" : "seed:asc", page: 1 } }))} /></th>
                <th className="px-5 py-4"><SortHeaderButton label={labels.scrape} active={activeSortField === "scrape"} direction={activeSortDirection as "asc" | "desc"} onClick={() => setView((current) => ({ ...current, query: { ...current.query, sort: activeSortField === "scrape" && activeSortDirection === "asc" ? "scrape:desc" : "scrape:asc", page: 1 } }))} /></th>
                <th className="px-5 py-4"><SortHeaderButton label={labels.privateTracker} active={activeSortField === "private"} direction={activeSortDirection as "asc" | "desc"} onClick={() => setView((current) => ({ ...current, query: { ...current.query, sort: activeSortField === "private" && activeSortDirection === "asc" ? "private:desc" : "private:asc", page: 1 } }))} /></th>
                <th className="px-5 py-4"><SortHeaderButton label={dictionary.common.version} active={activeSortField === "version"} direction={activeSortDirection as "asc" | "desc"} onClick={() => setView((current) => ({ ...current, query: { ...current.query, sort: activeSortField === "version" && activeSortDirection === "asc" ? "version:desc" : "version:asc", page: 1 } }))} /></th>
                <th className="px-5 py-4 text-right">Actions</th>
              </tr>
            </thead>
            <tbody>
              {isLoading ? (
                <TableStateRow colSpan={8} title="Loading tracker access" message="Refreshing tracker access rights from the backend." />
              ) : error && totalCount === 0 ? (
                <TableStateRow colSpan={8} title="Unable to load tracker access" message={error} />
              ) : items.length === 0 ? (
                <TableStateRow colSpan={8} title="No tracker access records match this view" message="Try a broader search or switch the current filter." />
              ) : items.map((item) => (
                <CatalogTableRow key={item.userId} onOpen={() => setView((current) => ({ ...current, preview: item.userId }))}>
                  <td className="px-5 py-4"><input type="checkbox" checked={selectedUserIds.includes(item.userId)} onChange={() => toggleSelection(item.userId)} /></td>
                  <td className="px-5 py-4">
                    <div className="app-grid-primary font-mono">{item.userId}</div>
                    <div className="app-inline-id">
                      <CopyValueButton value={item.userId} label={`Copy user id ${item.userId}`} />
                    </div>
                  </td>
                  <td className="px-5 py-4"><StatusPill tone={item.canLeech ? "good" : "neutral"}>{formatBool(item.canLeech, dictionary)}</StatusPill></td>
                  <td className="px-5 py-4"><StatusPill tone={item.canSeed ? "good" : "neutral"}>{formatBool(item.canSeed, dictionary)}</StatusPill></td>
                  <td className="px-5 py-4"><StatusPill tone={item.canScrape ? "good" : "neutral"}>{formatBool(item.canScrape, dictionary)}</StatusPill></td>
                  <td className="px-5 py-4"><StatusPill tone={item.canUsePrivateTracker ? "good" : "neutral"}>{formatBool(item.canUsePrivateTracker, dictionary)}</StatusPill></td>
                  <td className="px-5 py-4 text-steel">{item.version}</td>
                  <td className="px-5 py-4 text-right">
                    <div className="flex justify-end items-center gap-2">
                        {canWrite ? (
                          <Link className="app-button-secondary py-2.5 inline-flex items-center gap-2" to={`/permissions/${encodeURIComponent(item.userId)}/edit?returnTo=${encodeURIComponent(buildReturnTo(location.pathname, location.search))}`}><PencilIcon className="app-button-icon" />{labels.edit}</Link>
                        ) : null}
                      <RowActionsMenu items={[{ label: "Preview", icon: <EyeIcon />, onClick: () => setView((current) => ({ ...current, preview: item.userId })) }]} />
                    </div>
                  </td>
                </CatalogTableRow>
              ))}
            </tbody>
          </table>
        </div>
      </div>
      <PaginationFooter page={query.page} pageCount={pageCount} totalCount={totalCount} pageSize={query.pageSize} onPageChange={(page) => setView((current) => ({ ...current, query: { ...current.query, page } }))} />
      <PreviewDrawer open={previewItem != null} title={previewItem?.userId ?? ""} subtitle="Current tracker access rights" onClose={() => setView((current) => ({ ...current, preview: null }))}>
        {previewItem ? (
          <div className="space-y-4">
            <TrackerReadOnlySummary
              items={[
                { label: labels.leech, value: <StatusPill tone={previewItem.canLeech ? "good" : "neutral"}>{formatBool(previewItem.canLeech, dictionary)}</StatusPill> },
                { label: labels.seed, value: <StatusPill tone={previewItem.canSeed ? "good" : "neutral"}>{formatBool(previewItem.canSeed, dictionary)}</StatusPill> },
                { label: labels.scrape, value: <StatusPill tone={previewItem.canScrape ? "good" : "neutral"}>{formatBool(previewItem.canScrape, dictionary)}</StatusPill> },
                { label: labels.privateTracker, value: <StatusPill tone={previewItem.canUsePrivateTracker ? "good" : "neutral"}>{formatBool(previewItem.canUsePrivateTracker, dictionary)}</StatusPill> },
                { label: "Version", value: previewItem.version }
              ]}
            />
            {trackerAccessItems.some((item) => item.userId === previewItem.userId) ? (
              <TrackerOperationSummary>
                {trackerAccessItems.filter((item) => item.userId === previewItem.userId).map((item) => (
                  <div key={item.userId} className="app-subtle-panel space-y-2">
                    <StatusPill tone={item.succeeded ? "good" : "warn"}>{item.succeeded ? labels.succeeded : labels.failed}</StatusPill>
                    {item.errorMessage ? <div className="text-sm text-ember">{item.errorMessage}</div> : null}
                  </div>
                ))}
              </TrackerOperationSummary>
            ) : null}
            {canWrite ? (
                <TrackerPreviewActions>
                  <Link className="app-button-primary inline-flex items-center gap-2" to={`/permissions/${encodeURIComponent(previewItem.userId)}/edit?returnTo=${encodeURIComponent(buildReturnTo(location.pathname, location.search))}`}><PencilIcon className="app-button-icon" />Edit access</Link>
                </TrackerPreviewActions>
            ) : null}
          </div>
        ) : null}
      </PreviewDrawer>
    </div>
  );
}

function PasskeyEditorPage({
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
  const navigate = useNavigate();
  const location = useLocation();
  const params = useParams();
  const passkeyId = params.id ?? null;
  const isCreate = passkeyId === null;
  const returnTo = sanitizeReturnTo(new URLSearchParams(location.search).get("returnTo"), "/passkeys");
  const canManage = hasGrantedCapability(capabilities, "admin.write.passkey");
  const [form, setForm] = useState<PasskeyFormState>({
    passkey: "",
    userId: "",
    isRevoked: false,
    expiresAtLocal: "",
    expectedVersion: undefined
  });
  const [isLoading, setIsLoading] = useState(!isCreate);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [rotatedPasskey, setRotatedPasskey] = useState<string | null>(null);
  const [confirmDeleteOpen, setConfirmDeleteOpen] = useState(false);

  useEffect(() => {
    if (isCreate || !passkeyId) {
      return;
    }

    let cancelled = false;
    setIsLoading(true);
    void findPasskeyRecord(accessToken, onReauthenticate, passkeyId)
      .then((item) => {
        if (cancelled) {
          return;
        }

        if (!item) {
          setError("Unable to load the requested passkey.");
          return;
        }

        setForm(toPasskeyForm(item));
      })
      .catch((requestError) => {
        if (!cancelled) {
          setError(requestError instanceof Error ? requestError.message : labels.loadError);
        }
      })
      .finally(() => {
        if (!cancelled) {
          setIsLoading(false);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [accessToken, isCreate, labels.loadError, onReauthenticate, passkeyId]);

  const savePasskey = async () => {
    try {
      setIsSubmitting(true);
      setError(null);
      setMessage(null);
      setRotatedPasskey(null);

      if (isCreate) {
        await apiMutation<PasskeyAdminDto, { passkey: string; userId: string; isRevoked: boolean; expiresAtUtc: string | null; expectedVersion?: number }>(
          "/api/admin/passkeys",
          "POST",
          accessToken,
          {
            passkey: form.passkey,
            userId: form.userId,
            isRevoked: form.isRevoked,
            expiresAtUtc: fromLocalDateTimeInput(form.expiresAtLocal),
            expectedVersion: form.expectedVersion
          },
          onReauthenticate
        );

        navigate(returnTo, { state: { message: "Passkey created.", tone: "good" } satisfies NavigationBannerState });
        return;
      }

      await apiMutation<PasskeyAdminDto, { userId: string; isRevoked: boolean; expiresAtUtc: string | null; expectedVersion?: number }>(
        `/api/admin/passkeys/id/${encodeURIComponent(passkeyId!)}`,
        "PUT",
        accessToken,
        {
          userId: form.userId,
          isRevoked: form.isRevoked,
          expiresAtUtc: fromLocalDateTimeInput(form.expiresAtLocal),
          expectedVersion: form.expectedVersion
        },
        onReauthenticate
      );

      navigate(returnTo, { state: { message: "Passkey updated.", tone: "good" } satisfies NavigationBannerState });
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : "Saving passkey failed.");
    } finally {
      setIsSubmitting(false);
    }
  };

  const revokePasskey = async () => {
    if (!passkeyId) {
      return;
    }

    try {
      setIsSubmitting(true);
      setError(null);
      setMessage(null);
      setRotatedPasskey(null);
      const snapshot = await apiMutation<{ id: string; userId: string; isRevoked: boolean; expiresAtUtc: string | null; version: number }, { expectedVersion?: number }>(
        `/api/admin/passkeys/id/${encodeURIComponent(passkeyId)}/revoke`,
        "POST",
        accessToken,
        { expectedVersion: form.expectedVersion },
        onReauthenticate
      );

      setForm((current) => ({ ...current, isRevoked: true, expectedVersion: snapshot.version }));
      setMessage("Passkey revoked.");
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : "Revoking passkey failed.");
    } finally {
      setIsSubmitting(false);
    }
  };

  const rotatePasskey = async () => {
    if (!passkeyId) {
      return;
    }

    try {
      setIsSubmitting(true);
      setError(null);
      setMessage(null);
      const result = await apiMutation<PasskeyMutationPairDto, { expiresAtUtc: string | null; expectedVersion?: number }>(
        `/api/admin/passkeys/id/${encodeURIComponent(passkeyId)}/rotate`,
        "POST",
        accessToken,
        {
          expiresAtUtc: fromLocalDateTimeInput(form.expiresAtLocal),
          expectedVersion: form.expectedVersion
        },
        onReauthenticate
      );

      setRotatedPasskey(result.newSnapshot.passkey);
      setMessage("Passkey rotated. Copy the new passkey now; it will not be shown again.");
      navigate(`/passkeys/${encodeURIComponent(result.newSnapshot.id)}/edit?returnTo=${encodeURIComponent(returnTo)}`, {
        replace: true,
        state: { message: "Passkey rotated. Copy the new passkey now; it will not be shown again.", rotatedPasskey: result.newSnapshot.passkey }
      });
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : "Rotating passkey failed.");
    } finally {
      setIsSubmitting(false);
    }
  };

  const deletePasskey = async () => {
    if (!passkeyId) {
      return;
    }

    try {
      setIsSubmitting(true);
      setError(null);
      setMessage(null);
      await apiDelete(`/api/admin/passkeys/id/${encodeURIComponent(passkeyId)}?expectedVersion=${encodeURIComponent(String(form.expectedVersion ?? ""))}`, accessToken, onReauthenticate);
      navigate(returnTo, { state: { message: "Passkey deleted.", tone: "good" } satisfies NavigationBannerState });
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : "Deleting passkey failed.");
    } finally {
      setIsSubmitting(false);
    }
  };

  useEffect(() => {
    const banner = location.state as (NavigationBannerState & { rotatedPasskey?: string }) | null;
    if (!banner) {
      return;
    }

    if (banner.message) {
      setMessage(banner.message);
    }

    if (banner.rotatedPasskey) {
      setRotatedPasskey(banner.rotatedPasskey);
    }

    navigate(location.pathname + location.search, { replace: true, state: null });
  }, [location.pathname, location.search, location.state, navigate]);

  return (
    <TrackerEditorLayout
      eyebrow="Tracker"
      title={isCreate ? "Create passkey" : "Edit passkey"}
      description={isCreate ? "Create a passkey in a dedicated editor." : "Update, revoke, rotate or delete a passkey outside the list grid."}
      error={error}
      message={message}
    >
      <div className="grid gap-6 xl:grid-cols-[1.1fr,0.9fr]">
        <div className="space-y-4">
          {isCreate ? (
            <label className="space-y-2">
              <span className="text-sm font-medium text-ink">Raw passkey</span>
              <input className="app-input font-mono text-sm" value={form.passkey} onChange={(event) => setForm((current) => ({ ...current, passkey: event.target.value }))} />
            </label>
          ) : null}
          <label className="space-y-2">
            <span className="text-sm font-medium text-ink">User ID</span>
            <input className="app-input font-mono text-sm" value={form.userId} disabled={isLoading} onChange={(event) => setForm((current) => ({ ...current, userId: event.target.value }))} />
          </label>
          <label className="flex items-center gap-3 rounded-2xl border border-ink/10 bg-slate-50 px-4 py-3 text-sm text-ink">
            <input type="checkbox" checked={form.isRevoked} disabled={isLoading} onChange={(event) => setForm((current) => ({ ...current, isRevoked: event.target.checked }))} />
            Revoked
          </label>
          <label className="space-y-2">
            <span className="text-sm font-medium text-ink">Expires at</span>
            <input className="app-input" type="datetime-local" value={form.expiresAtLocal} disabled={isLoading} onChange={(event) => setForm((current) => ({ ...current, expiresAtLocal: event.target.value }))} />
          </label>
          <TrackerEditorFooter
            left={!isCreate ? (
              <>
                <button type="button" className="app-button-secondary inline-flex items-center gap-2" disabled={!canManage || isSubmitting} onClick={() => void revokePasskey()}>
                  <TrashIcon className="app-button-icon" />
                  Revoke
                </button>
                <button type="button" className="app-button-secondary inline-flex items-center gap-2" disabled={!canManage || isSubmitting} onClick={() => void rotatePasskey()}>
                  <PencilIcon className="app-button-icon" />
                  Rotate
                </button>
                <button type="button" className="app-button-danger inline-flex items-center gap-2" disabled={!canManage || isSubmitting} onClick={() => setConfirmDeleteOpen(true)}>
                  <TrashIcon className="app-button-icon" />
                  Delete
                </button>
              </>
            ) : undefined}
            right={
              <>
                <button type="button" className="app-button-secondary" onClick={() => navigate(returnTo)}>
                  Cancel
                </button>
                <button type="button" className="app-button-primary inline-flex items-center gap-2" disabled={!canManage || isLoading || isSubmitting || !form.userId.trim() || (isCreate && !form.passkey.trim())} onClick={() => void savePasskey()}>
                  <PencilIcon className="app-button-icon" />
                  {isCreate ? "Create passkey" : "Save passkey"}
                </button>
              </>
            }
          />
        </div>
        <div className="app-editor-sidebar">
          <TrackerEditorSummary
            title={isCreate ? "Passkey draft" : "Current passkey"}
            items={[
              { label: "Passkey", value: isCreate ? (form.passkey || "Not set") : "Managed by identifier", tone: "mono" },
              { label: "User ID", value: form.userId || "Not set", tone: "mono" },
              { label: "State", value: form.isRevoked ? "Revoked" : "Active" },
              { label: "Expires", value: form.expiresAtLocal || "No expiry" },
              { label: "Version", value: form.expectedVersion ?? "New" }
            ]}
          >
            {rotatedPasskey ? (
              <div className="space-y-2">
                <div className="app-kicker">New passkey</div>
                <div className="flex items-center gap-3">
                  <div className="break-all font-mono text-xs text-ink">{rotatedPasskey}</div>
                  <CopyValueButton value={rotatedPasskey} label="Copy new passkey" />
                </div>
              </div>
            ) : null}
          </TrackerEditorSummary>
        </div>
      </div>
      <ConfirmActionModal
        open={confirmDeleteOpen}
        title="Delete passkey"
        description="Delete this passkey? This cannot be undone."
        confirmLabel="Delete passkey"
        onClose={() => setConfirmDeleteOpen(false)}
        onConfirm={() => {
          setConfirmDeleteOpen(false);
          void deletePasskey();
        }}
      />
    </TrackerEditorLayout>
  );
}

function PasskeyBatchActionPage({
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
  const navigate = useNavigate();
  const location = useLocation();
  const returnTo = sanitizeReturnTo(new URLSearchParams(location.search).get("returnTo"), "/passkeys");
  const [passkeyInput, setPasskeyInput] = useState("");
  const [rotateExpiryInput, setRotateExpiryInput] = useState("");
  const [result, setResult] = useState<BulkOperationResultDto | null>(null);
  const [status, setStatus] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const canRevoke = hasGrantedCapability(capabilities, "admin.revoke.passkey");
  const canRotate = hasGrantedCapability(capabilities, "admin.rotate.passkey");
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
      const message = formatText(labels.status, { mode: toTitleCase(mode), succeeded: operationResult.succeededCount, total: operationResult.totalCount });
      setStatus(message);
      navigate(returnTo, { state: { message, tone: "good" } satisfies NavigationBannerState });
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : labels.operationError);
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <TrackerEditorLayout
      eyebrow="Tracker"
      title={labels.actionTitle}
      description="Provide raw passkeys one per line and run revoke or rotate in a dedicated workflow."
      error={error}
      message={status}
    >
      <div className="space-y-4">
        <label className="space-y-2">
          <span className="text-sm font-medium text-ink">{labels.rawPasskeys}</span>
          <textarea className="app-input min-h-48 font-mono text-sm" value={passkeyInput} onChange={(event) => setPasskeyInput(event.target.value)} placeholder={labels.placeholder} />
        </label>
        <label className="space-y-2">
          <span className="text-sm font-medium text-ink">{labels.expiryOverride}</span>
          <input className="app-input" type="datetime-local" value={rotateExpiryInput} onChange={(event) => setRotateExpiryInput(event.target.value)} />
        </label>
        {result && result.passkeyItems.length > 0 ? (
          <div className="space-y-3">
            {result.passkeyItems.map((item) => (
              <div key={`${item.passkeyMask}-${item.newPasskeyMask ?? "same"}`} className="app-subtle-panel space-y-2">
                <div className="flex items-center justify-between gap-3">
                  <div className="font-mono text-xs text-ink">{item.passkeyMask}</div>
                  <StatusPill tone={item.succeeded ? "good" : "warn"}>{item.succeeded ? labels.completed : labels.failed}</StatusPill>
                </div>
                {item.errorMessage ? <div className="text-sm text-ember">{item.errorMessage}</div> : null}
              </div>
            ))}
          </div>
        ) : null}
        <TrackerEditorFooter
          right={
            <>
              <button type="button" className="app-button-secondary" onClick={() => navigate(returnTo)}>
                Cancel
              </button>
              <button type="button" className="app-button-secondary inline-flex items-center gap-2" disabled={!canRevoke || isSubmitting || inputPasskeys.length === 0} onClick={() => void runBulkPasskeyAction("/api/admin/passkeys/bulk/revoke", "revoke")}>
                <TrashIcon className="app-button-icon" />
                {labels.revoke}
              </button>
              <button type="button" className="app-button-primary inline-flex items-center gap-2" disabled={!canRotate || isSubmitting || inputPasskeys.length === 0} onClick={() => void runBulkPasskeyAction("/api/admin/passkeys/bulk/rotate", "rotate")}>
                <PencilIcon className="app-button-icon" />
                {labels.rotate}
              </button>
            </>
          }
        />
      </div>
    </TrackerEditorLayout>
  );
}

function BanEditorPage({
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
  const navigate = useNavigate();
  const location = useLocation();
  const params = useParams();
  const recordId = params.id ?? null;
  const record = tryParseBanRecordId(recordId);
  const isCreate = record === null;
  const returnTo = sanitizeReturnTo(new URLSearchParams(location.search).get("returnTo"), "/bans");
  const canWrite = hasGrantedCapability(capabilities, "admin.write.ban");
  const canExpire = hasGrantedCapability(capabilities, "admin.expire.ban");
  const canDelete = hasGrantedCapability(capabilities, "admin.delete.ban");
  const [form, setForm] = useState<BanFormState>({
    scope: record?.scope ?? "user",
    subject: record?.subject ?? "",
    reason: "",
    expiresAtLocal: "",
    expectedVersion: undefined
  });
  const [isLoading, setIsLoading] = useState(!isCreate);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [confirmDeleteOpen, setConfirmDeleteOpen] = useState(false);

  useEffect(() => {
    if (isCreate || !record) {
      return;
    }

    let cancelled = false;
    setIsLoading(true);
    void findBanRuleRecord(accessToken, onReauthenticate, record.scope, record.subject)
      .then((item) => {
        if (cancelled) {
          return;
        }

        if (!item) {
          setError("Unable to load the requested ban rule.");
          return;
        }

        setForm({
          scope: item.scope,
          subject: item.subject,
          reason: item.reason,
          expiresAtLocal: toLocalDateTimeInput(item.expiresAtUtc),
          expectedVersion: item.version
        });
      })
      .catch((requestError) => {
        if (!cancelled) {
          setError(requestError instanceof Error ? requestError.message : labels.loadError);
        }
      })
      .finally(() => {
        if (!cancelled) {
          setIsLoading(false);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [accessToken, isCreate, labels.loadError, onReauthenticate, record]);

  const saveBan = async () => {
    try {
      setIsSubmitting(true);
      setError(null);
      setMessage(null);
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

      navigate(returnTo, { state: { message: isCreate ? "Ban rule created." : labels.saveStatus, tone: "good" } satisfies NavigationBannerState });
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : labels.saveError);
    } finally {
      setIsSubmitting(false);
    }
  };

  const expireBan = async () => {
    try {
      setIsSubmitting(true);
      setError(null);
      setMessage(null);
      const expiresAtUtc = fromLocalDateTimeInput(form.expiresAtLocal);
      if (!expiresAtUtc) {
        throw new Error(labels.expiryRequired);
      }

      await apiMutation<BanRuleAdminDto, { expiresAtUtc: string; expectedVersion?: number }>(
        `/api/admin/bans/${encodeURIComponent(form.scope)}/${encodeURIComponent(form.subject)}/expire`,
        "POST",
        accessToken,
        { expiresAtUtc, expectedVersion: form.expectedVersion },
        onReauthenticate
      );

      navigate(returnTo, { state: { message: labels.expireStatus, tone: "good" } satisfies NavigationBannerState });
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : labels.expireError);
    } finally {
      setIsSubmitting(false);
    }
  };

  const deleteBan = async () => {
    try {
      setIsSubmitting(true);
      setError(null);
      setMessage(null);
      await apiDelete(
        `/api/admin/bans/${encodeURIComponent(form.scope)}/${encodeURIComponent(form.subject)}?expectedVersion=${encodeURIComponent(String(form.expectedVersion ?? ""))}`,
        accessToken,
        onReauthenticate
      );

      navigate(returnTo, { state: { message: labels.deleteStatus, tone: "good" } satisfies NavigationBannerState });
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : labels.deleteError);
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <TrackerEditorLayout
      eyebrow="Tracker"
      title={isCreate ? "Create ban rule" : "Edit ban rule"}
      description={isCreate ? "Create an enforcement rule in a dedicated workflow." : "Update, expire or remove a tracker enforcement rule without using an inline modal."}
      error={error}
      message={message}
    >
      <div className="grid gap-6 xl:grid-cols-[1.1fr,0.9fr]">
        <div className="space-y-4">
          <div className="app-form-grid">
            <label className="space-y-2">
              <span className="text-sm font-medium text-ink">{labels.scope}</span>
              <input className="app-input" value={form.scope} disabled={!isCreate || isLoading} onChange={(event) => setForm((current) => ({ ...current, scope: event.target.value }))} />
            </label>
            <label className="space-y-2">
              <span className="text-sm font-medium text-ink">{labels.subject}</span>
              <input className="app-input font-mono text-sm" value={form.subject} disabled={!isCreate || isLoading} onChange={(event) => setForm((current) => ({ ...current, subject: event.target.value }))} />
            </label>
          </div>
          <label className="space-y-2">
            <span className="text-sm font-medium text-ink">{labels.reason}</span>
            <textarea className="app-input min-h-32" value={form.reason} disabled={isLoading} onChange={(event) => setForm((current) => ({ ...current, reason: event.target.value }))} />
          </label>
          <label className="space-y-2">
            <span className="text-sm font-medium text-ink">{labels.expiresLabel}</span>
            <input className="app-input" type="datetime-local" value={form.expiresAtLocal} disabled={isLoading} onChange={(event) => setForm((current) => ({ ...current, expiresAtLocal: event.target.value }))} />
          </label>
          <TrackerEditorFooter
            left={!isCreate ? (
              <>
                <button type="button" className="app-button-secondary inline-flex items-center gap-2" disabled={!canExpire || isSubmitting || !form.scope.trim() || !form.subject.trim()} onClick={() => void expireBan()}>
                  <PowerIcon className="app-button-icon" />
                  {labels.expireBan}
                </button>
                <button type="button" className="app-button-danger inline-flex items-center gap-2" disabled={!canDelete || isSubmitting} onClick={() => setConfirmDeleteOpen(true)}>
                  <TrashIcon className="app-button-icon" />
                  Delete rule
                </button>
              </>
            ) : undefined}
            right={
              <>
                <button type="button" className="app-button-secondary" onClick={() => navigate(returnTo)}>
                  Cancel
                </button>
                <button type="button" className="app-button-primary inline-flex items-center gap-2" disabled={!canWrite || isLoading || isSubmitting || !form.scope.trim() || !form.subject.trim() || !form.reason.trim()} onClick={() => void saveBan()}>
                  <PencilIcon className="app-button-icon" />
                  {isCreate ? "Create rule" : labels.saveBan}
                </button>
              </>
            }
          />
        </div>
        <div className="app-editor-sidebar">
          <TrackerEditorSummary
            title={isCreate ? "Ban draft" : "Current rule"}
            items={[
              { label: labels.scope, value: form.scope || "Not set" },
              { label: labels.subject, value: form.subject || "Not set", tone: "mono" },
              { label: labels.reason, value: form.reason || "Not set" },
              { label: labels.expiresLabel, value: form.expiresAtLocal || "No expiry" },
              { label: "Version", value: form.expectedVersion ?? "New" }
            ]}
          />
        </div>
      </div>
      <ConfirmActionModal
        open={confirmDeleteOpen}
        title="Delete ban rule"
        description={`Delete rule ${form.scope}:${form.subject}? This cannot be undone.`}
        confirmLabel="Delete rule"
        onClose={() => setConfirmDeleteOpen(false)}
        onConfirm={() => {
          setConfirmDeleteOpen(false);
          void deleteBan();
        }}
      />
    </TrackerEditorLayout>
  );
}

function TrackerAccessEditorPage({
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
  const navigate = useNavigate();
  const location = useLocation();
  const params = useParams();
  const userId = params.id ?? null;
  const isCreate = userId === null;
  const returnTo = sanitizeReturnTo(new URLSearchParams(location.search).get("returnTo"), "/permissions");
  const canWrite = hasGrantedCapability(capabilities, "admin.write.permissions");
  const [form, setForm] = useState<TrackerAccessFormState>({
    userId: userId ?? "",
    canLeech: true,
    canSeed: true,
    canScrape: true,
    canUsePrivateTracker: true,
    expectedVersion: undefined
  });
  const [isLoading, setIsLoading] = useState(!isCreate);
  const [isSaving, setIsSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [confirmDeleteOpen, setConfirmDeleteOpen] = useState(false);

  useEffect(() => {
    if (isCreate || !userId) {
      return;
    }

    let cancelled = false;
    setIsLoading(true);
    void findTrackerAccessRecord(accessToken, onReauthenticate, userId)
      .then((item) => {
        if (cancelled) {
          return;
        }

        if (!item) {
          setError("Unable to load the requested tracker access record.");
          return;
        }

        setForm(toTrackerAccessForm(item));
      })
      .catch((requestError) => {
        if (!cancelled) {
          setError(requestError instanceof Error ? requestError.message : labels.loadError);
        }
      })
      .finally(() => {
        if (!cancelled) {
          setIsLoading(false);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [accessToken, isCreate, labels.loadError, onReauthenticate, userId]);

  const savePermissions = async () => {
    try {
      setIsSaving(true);
      setError(null);
      setMessage(null);
      await apiMutation<TrackerAccessAdminDto, Omit<TrackerAccessFormState, "userId">>(
        `/api/admin/users/${encodeURIComponent(form.userId)}/tracker-access`,
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

      navigate(returnTo, { state: { message: labels.saveStatus, tone: "good" } satisfies NavigationBannerState });
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : labels.saveError);
    } finally {
      setIsSaving(false);
    }
  };

  const deletePermissions = async () => {
    if (isCreate || !form.userId.trim()) {
      return;
    }

    try {
      setIsSaving(true);
      setError(null);
      setMessage(null);
      await apiDelete(
        `/api/admin/users/${encodeURIComponent(form.userId)}/tracker-access?expectedVersion=${encodeURIComponent(String(form.expectedVersion ?? ""))}`,
        accessToken,
        onReauthenticate
      );

      navigate(returnTo, { state: { message: "Tracker access deleted.", tone: "good" } satisfies NavigationBannerState });
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : "Deleting tracker access failed.");
    } finally {
      setIsSaving(false);
    }
  };

  return (
    <TrackerEditorLayout
      eyebrow="Tracker"
      title={isCreate ? "Create tracker access" : labels.editorTitle}
      description={isCreate ? "Create a tracker access envelope in a dedicated editor." : "Update tracker access rights outside the list grid."}
      error={error}
      message={message}
    >
      <div className="grid gap-6 xl:grid-cols-[1.1fr,0.9fr]">
        <div className="space-y-4">
          <label className="space-y-2">
            <span className="text-sm font-medium text-ink">{labels.userId}</span>
            <input className="app-input font-mono text-sm" value={form.userId} disabled={!isCreate || isLoading} onChange={(event) => setForm((current) => ({ ...current, userId: event.target.value }))} />
          </label>
          <div className="grid gap-3 md:grid-cols-2">
            <label className="flex items-center gap-3 rounded-2xl border border-ink/10 bg-slate-50 px-4 py-3 text-sm text-ink"><input type="checkbox" checked={form.canLeech} onChange={(event) => setForm((current) => ({ ...current, canLeech: event.target.checked }))} />{labels.canLeech}</label>
            <label className="flex items-center gap-3 rounded-2xl border border-ink/10 bg-slate-50 px-4 py-3 text-sm text-ink"><input type="checkbox" checked={form.canSeed} onChange={(event) => setForm((current) => ({ ...current, canSeed: event.target.checked }))} />{labels.canSeed}</label>
            <label className="flex items-center gap-3 rounded-2xl border border-ink/10 bg-slate-50 px-4 py-3 text-sm text-ink"><input type="checkbox" checked={form.canScrape} onChange={(event) => setForm((current) => ({ ...current, canScrape: event.target.checked }))} />{labels.canScrape}</label>
            <label className="flex items-center gap-3 rounded-2xl border border-ink/10 bg-slate-50 px-4 py-3 text-sm text-ink"><input type="checkbox" checked={form.canUsePrivateTracker} onChange={(event) => setForm((current) => ({ ...current, canUsePrivateTracker: event.target.checked }))} />{labels.canUsePrivateTracker}</label>
          </div>
          <TrackerEditorFooter
            left={!isCreate ? (
              <button type="button" className="app-button-danger inline-flex items-center gap-2" disabled={!canWrite || isSaving} onClick={() => setConfirmDeleteOpen(true)}>
                <TrashIcon className="app-button-icon" />
                Delete access
              </button>
            ) : undefined}
            right={
              <>
                <button type="button" className="app-button-secondary" onClick={() => navigate(returnTo)}>
                  Cancel
                </button>
                <button type="button" disabled={!canWrite || isSaving || isLoading || !form.userId.trim()} className="app-button-primary inline-flex items-center gap-2" onClick={() => void savePermissions()}>
                  <PencilIcon className="app-button-icon" />
                  {labels.savePermissions}
                </button>
              </>
            }
          />
        </div>
        <div className="app-editor-sidebar">
          <TrackerEditorSummary
            title={isCreate ? "Access draft" : "Current access"}
            items={[
              { label: labels.userId, value: form.userId || "Not set", tone: "mono" },
              { label: labels.canLeech, value: form.canLeech ? "Allowed" : "Blocked" },
              { label: labels.canSeed, value: form.canSeed ? "Allowed" : "Blocked" },
              { label: labels.canScrape, value: form.canScrape ? "Allowed" : "Blocked" },
              { label: labels.canUsePrivateTracker, value: form.canUsePrivateTracker ? "Allowed" : "Blocked" },
              { label: "Version", value: form.expectedVersion ?? "New" }
            ]}
          />
        </div>
      </div>
      <ConfirmActionModal
        open={confirmDeleteOpen}
        title="Delete tracker access"
        description={`Delete tracker access for ${form.userId}? This cannot be undone.`}
        confirmLabel="Delete access"
        onClose={() => setConfirmDeleteOpen(false)}
        onConfirm={() => {
          setConfirmDeleteOpen(false);
          void deletePermissions();
        }}
      />
    </TrackerEditorLayout>
  );
}

function BulkTrackerAccessEditorPage({
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
  const navigate = useNavigate();
  const location = useLocation();
  const returnTo = sanitizeReturnTo(new URLSearchParams(location.search).get("returnTo"), "/permissions");
  const canWrite = hasGrantedCapability(capabilities, "admin.write.permissions");
  const [selectedItems] = useState<TrackerAccessAdminDto[]>(() => readBulkTrackerAccessSelection());
  const [form, setForm] = useState<TrackerAccessFormState>({
    userId: "",
    canLeech: true,
    canSeed: true,
    canScrape: true,
    canUsePrivateTracker: true,
    expectedVersion: undefined
  });
  const [isSaving, setIsSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);

  const saveBulk = async () => {
    try {
      setIsSaving(true);
      setError(null);
      setMessage(null);
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
        "/api/admin/users/bulk/tracker-access",
        "PUT",
        accessToken,
        {
          items: selectedItems.map((item) => ({
            userId: item.userId,
            canLeech: form.canLeech,
            canSeed: form.canSeed,
            canScrape: form.canScrape,
            canUsePrivateTracker: form.canUsePrivateTracker,
            expectedVersion: item.version
          }))
        },
        onReauthenticate
      );

      clearBulkTrackerAccessSelection();
      navigate(returnTo, {
        state: {
          message: formatText(labels.bulkStatus, { succeeded: operationResult.succeededCount, total: operationResult.totalCount }),
          tone: "good"
        } satisfies NavigationBannerState
      });
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : labels.bulkError);
    } finally {
      setIsSaving(false);
    }
  };

  return (
    <TrackerEditorLayout
      eyebrow="Tracker"
      title="Apply tracker access to selected users"
      description="Use one access envelope for the current selection in a dedicated bulk editor."
      error={error}
      message={message}
    >
      <div className="space-y-4">
        <div className="app-subtle-panel space-y-2">
          <div className="app-kicker">Selection</div>
          <div className="font-semibold text-ink">{selectedItems.length} user(s)</div>
        </div>
        <div className="grid gap-3 md:grid-cols-2">
          <label className="flex items-center gap-3 rounded-2xl border border-ink/10 bg-slate-50 px-4 py-3 text-sm text-ink"><input type="checkbox" checked={form.canLeech} onChange={(event) => setForm((current) => ({ ...current, canLeech: event.target.checked }))} />{labels.canLeech}</label>
          <label className="flex items-center gap-3 rounded-2xl border border-ink/10 bg-slate-50 px-4 py-3 text-sm text-ink"><input type="checkbox" checked={form.canSeed} onChange={(event) => setForm((current) => ({ ...current, canSeed: event.target.checked }))} />{labels.canSeed}</label>
          <label className="flex items-center gap-3 rounded-2xl border border-ink/10 bg-slate-50 px-4 py-3 text-sm text-ink"><input type="checkbox" checked={form.canScrape} onChange={(event) => setForm((current) => ({ ...current, canScrape: event.target.checked }))} />{labels.canScrape}</label>
          <label className="flex items-center gap-3 rounded-2xl border border-ink/10 bg-slate-50 px-4 py-3 text-sm text-ink"><input type="checkbox" checked={form.canUsePrivateTracker} onChange={(event) => setForm((current) => ({ ...current, canUsePrivateTracker: event.target.checked }))} />{labels.canUsePrivateTracker}</label>
        </div>
        <TrackerEditorFooter
          right={
            <>
              <button type="button" className="app-button-secondary" onClick={() => navigate(returnTo)}>
                Cancel
              </button>
              <button type="button" disabled={!canWrite || isSaving || selectedItems.length === 0} className="app-button-primary inline-flex items-center gap-2" onClick={() => void saveBulk()}>
                <PencilIcon className="app-button-icon" />
                {labels.applySelected} ({selectedItems.length})
              </button>
            </>
          }
        />
      </div>
    </TrackerEditorLayout>
  );
}

// ─── Runtime Swarm Administration ───────────────────────────────────────────

function SwarmsPage({
  accessToken,
  onReauthenticate,
  capabilities
}: {
  accessToken: string;
  onReauthenticate: (fresh: boolean) => Promise<void>;
  capabilities: CapabilityDto[];
}) {
  const [view, setView] = useCatalogViewState({
    search: "",
    filter: "all",
    sort: "peers:desc",
    page: 1,
    pageSize: 25
  });
  const { query, preview } = view;
  const [items, setItems] = useState<SwarmSummaryDto[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [status, setStatus] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [detail, setDetail] = useState<AggregatedSwarmDetailDto | null>(null);
  const [detailLoading, setDetailLoading] = useState(false);
  const [clusterMeta, setClusterMeta] = useState<{ respondedNodeCount: number; totalNodeCount: number; failedNodeIds: string[] } | null>(null);
  const [cleanupConfirm, setCleanupConfirm] = useState<string | null>(null);
  const canCleanup = hasGrantedCapability(capabilities, "admin.cleanup.swarm");
  const pageCount = Math.max(1, Math.ceil(totalCount / query.pageSize));

  const reload = async () => {
    const searchParam = query.search ? `&search=${encodeURIComponent(query.search)}` : "";
    const result = await apiRequest<AggregatedSwarmListResultDto>(
      `/api/admin/swarms?page=${query.page}&pageSize=${query.pageSize}${searchParam}`,
      accessToken,
      onReauthenticate
    );
    setItems(result.items);
    setTotalCount(result.totalCount);
    setClusterMeta({ respondedNodeCount: result.respondedNodeCount, totalNodeCount: result.totalNodeCount, failedNodeIds: result.failedNodeIds });
  };

  useEffect(() => {
    let isMounted = true;
    setIsLoading(true);
    reload()
      .then(() => { if (isMounted) setError(null); })
      .catch((e) => { if (isMounted) { setError(e instanceof Error ? e.message : "Failed to load swarms."); setItems([]); setTotalCount(0); } })
      .finally(() => { if (isMounted) setIsLoading(false); });
    return () => { isMounted = false; };
  }, [accessToken, onReauthenticate, query.search, query.page, query.pageSize]);

  useEffect(() => {
    if (!preview) { setDetail(null); return; }
    let isMounted = true;
    setDetailLoading(true);
    apiRequest<AggregatedSwarmDetailDto>(
      `/api/admin/swarms/${encodeURIComponent(preview)}`,
      accessToken,
      onReauthenticate
    )
      .then((d) => { if (isMounted) setDetail(d); })
      .catch(() => { if (isMounted) setDetail(null); })
      .finally(() => { if (isMounted) setDetailLoading(false); });
    return () => { isMounted = false; };
  }, [preview, accessToken, onReauthenticate]);

  const runCleanup = async (infoHash: string) => {
    try {
      setError(null);
      const result = await apiMutation<AggregatedSwarmCleanupResultDto, Record<string, never>>(
        `/api/admin/swarms/${encodeURIComponent(infoHash)}/cleanup`,
        "POST",
        accessToken,
        {},
        onReauthenticate
      );
      setStatus(`Cleanup complete: ${result.totalRemovedPeers} stale peer(s) removed across ${result.nodeResults.length} node(s).${result.failedNodeIds.length > 0 ? ` ${result.failedNodeIds.length} node(s) unreachable.` : ""}`);
      await reload();
      if (preview === infoHash) {
        setView((current) => ({ ...current, preview: infoHash }));
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : "Cleanup failed.");
    } finally {
      setCleanupConfirm(null);
    }
  };

  return (
    <div className="space-y-6">
      <CatalogToolbar
        title="Runtime swarms"
        description="Inspect active swarms across all gateway nodes. Data is aggregated in real time."
        totalCount={totalCount}
        search={query.search}
        onSearchChange={(value) => setView((current) => ({ ...current, query: { ...current.query, search: value, page: 1 } }))}
        searchPlaceholder="Search by info hash"
        filter={query.filter}
        onFilterChange={() => {}}
        filterOptions={[{ value: "all", label: "All swarms" }]}
        sortValue={query.sort}
        onSortChange={(value) => setView((current) => ({ ...current, query: { ...current.query, sort: value, page: 1 } }))}
        sortOptions={[
          { value: "peers:desc", label: "Most active first" },
          { value: "peers:asc", label: "Least active first" }
        ]}
        pageSize={query.pageSize}
        onPageSizeChange={(value) => setView((current) => ({ ...current, query: { ...current.query, pageSize: value, page: 1 } }))}
      />
      {clusterMeta && clusterMeta.failedNodeIds.length > 0 ? (
        <div className="app-notice-warn">
          Partial cluster response: {clusterMeta.respondedNodeCount}/{clusterMeta.totalNodeCount} nodes responded.
          Unreachable: {clusterMeta.failedNodeIds.join(", ")}.
        </div>
      ) : null}
      {status ? <div className="app-notice-success">{status}</div> : null}
      {error ? <div className="app-notice-warn">{error}</div> : null}
      <div className="app-data-table">
        <table>
          <thead>
            <tr>
              <th className="w-[400px]"><SortHeaderButton label="Info hash" field="infohash" activeField="infohash" activeDirection="asc" onSort={() => {}} /></th>
              <th className="w-24 text-right">Seeders</th>
              <th className="w-24 text-right">Leechers</th>
              <th className="w-24 text-right">Completed</th>
              <th className="w-28 text-right">Total peers</th>
              {canCleanup ? <th className="w-20">Actions</th> : null}
            </tr>
          </thead>
          <tbody>
            {isLoading ? (
              <TableStateRow colSpan={canCleanup ? 6 : 5} state="loading" />
            ) : items.length === 0 ? (
              <TableStateRow colSpan={canCleanup ? 6 : 5} state="empty" message="No active swarms found." />
            ) : (
              items.map((item) => (
                <CatalogTableRow key={item.infoHash} onOpen={() => setView((current) => ({ ...current, preview: item.infoHash }))}>
                  <td className="font-mono text-xs truncate max-w-[400px]" title={item.infoHash}>
                    <CopyValueButton value={item.infoHash}>{item.infoHash}</CopyValueButton>
                  </td>
                  <td className="text-right tabular-nums">{item.seeders.toLocaleString()}</td>
                  <td className="text-right tabular-nums">{item.leechers.toLocaleString()}</td>
                  <td className="text-right tabular-nums">{item.downloaded.toLocaleString()}</td>
                  <td className="text-right tabular-nums font-semibold">{(item.seeders + item.leechers).toLocaleString()}</td>
                  {canCleanup ? (
                    <td>
                      <RowActionsMenu
                        actions={[
                          { label: "Cleanup stale peers", onClick: () => setCleanupConfirm(item.infoHash) }
                        ]}
                      />
                    </td>
                  ) : null}
                </CatalogTableRow>
              ))
            )}
          </tbody>
        </table>
      </div>
      <PaginationFooter page={query.page} pageCount={pageCount} onPageChange={(p) => setView((current) => ({ ...current, query: { ...current.query, page: p } }))} />
      {preview && detail ? (
        <PreviewDrawer title={`Swarm ${detail.infoHash.substring(0, 12)}...`} onClose={() => setView((current) => ({ ...current, preview: null }))}>
          <div className="space-y-4">
            <div className="grid grid-cols-2 gap-4 text-sm">
              <div>
                <div className="text-steel">Info hash</div>
                <div className="font-mono text-xs break-all">{detail.infoHash}</div>
              </div>
              <div>
                <div className="text-steel">Seeders</div>
                <div className="font-semibold">{detail.seeders.toLocaleString()}</div>
              </div>
              <div>
                <div className="text-steel">Leechers</div>
                <div className="font-semibold">{detail.leechers.toLocaleString()}</div>
              </div>
              <div>
                <div className="text-steel">Completed</div>
                <div className="font-semibold">{detail.downloaded.toLocaleString()}</div>
              </div>
            </div>
            {detail.contributingNodeIds.length > 0 ? (
              <div className="text-sm">
                <div className="text-steel">Contributing nodes</div>
                <div>{detail.contributingNodeIds.join(", ")}</div>
              </div>
            ) : null}
            {detail.failedNodeIds.length > 0 ? (
              <div className="text-sm text-amber-600">
                <div className="font-medium">Unreachable nodes</div>
                <div>{detail.failedNodeIds.join(", ")}</div>
              </div>
            ) : null}
            <div>
              <div className="text-steel text-sm mb-2">Peers ({detail.peers.length})</div>
              {detail.peers.length > 0 ? (
                <div className="overflow-x-auto">
                  <table className="w-full text-xs">
                    <thead>
                      <tr className="text-left text-steel">
                        <th className="py-1 pr-3">IP</th>
                        <th className="py-1 pr-3">Port</th>
                        <th className="py-1 pr-3">Type</th>
                        <th className="py-1 pr-3 text-right">Uploaded</th>
                        <th className="py-1 pr-3 text-right">Downloaded</th>
                        <th className="py-1 text-right">Left</th>
                      </tr>
                    </thead>
                    <tbody>
                      {detail.peers.map((peer, idx) => (
                        <tr key={`${peer.peerId}-${idx}`} className="border-t border-separator">
                          <td className="py-1 pr-3 font-mono">{peer.ip}</td>
                          <td className="py-1 pr-3 tabular-nums">{peer.port}</td>
                          <td className="py-1 pr-3">{peer.isSeeder ? "Seeder" : "Leecher"}</td>
                          <td className="py-1 pr-3 text-right tabular-nums">{formatBytes(peer.uploaded)}</td>
                          <td className="py-1 pr-3 text-right tabular-nums">{formatBytes(peer.downloaded)}</td>
                          <td className="py-1 text-right tabular-nums">{formatBytes(peer.left)}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              ) : (
                <div className="text-steel text-sm">No peers in this swarm.</div>
              )}
            </div>
          </div>
        </PreviewDrawer>
      ) : preview && detailLoading ? (
        <PreviewDrawer title="Loading..." onClose={() => setView((current) => ({ ...current, preview: null }))}>
          <div className="text-steel text-sm">Loading swarm details...</div>
        </PreviewDrawer>
      ) : null}
      {cleanupConfirm ? (
        <ConfirmActionModal
          title="Cleanup stale peers"
          message={`Remove expired peers from swarm ${cleanupConfirm.substring(0, 16)}... across all gateway nodes?`}
          confirmLabel="Cleanup"
          severity="medium"
          onConfirm={() => runCleanup(cleanupConfirm)}
          onCancel={() => setCleanupConfirm(null)}
        />
      ) : null}
    </div>
  );
}

function formatBytes(bytes: number): string {
  if (bytes === 0) return "0 B";
  const units = ["B", "KB", "MB", "GB", "TB"];
  const i = Math.floor(Math.log(Math.abs(bytes)) / Math.log(1024));
  const value = bytes / Math.pow(1024, Math.min(i, units.length - 1));
  return `${value.toFixed(i === 0 ? 0 : 1)} ${units[Math.min(i, units.length - 1)]}`;
}

// ─── Audit ──────────────────────────────────────────────────────────────────

function AuditPage({
  accessToken,
  onReauthenticate
}: {
  accessToken: string;
  onReauthenticate: (fresh: boolean) => Promise<void>;
}) {
  const { dictionary } = useI18n();
  const labels = dictionary.audit;
  const [view, setView] = useCatalogViewState({
    search: "",
    filter: "all",
    sort: "occurred:desc",
    page: 1,
    pageSize: 25
  });
  const { query, preview } = view;
  const [items, setItems] = useState<AuditRecordDto[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const previewItem = items.find((item) => item.id === preview) ?? null;

  const toggleSort = (field: string) => {
    setView((current) => {
      const [activeField, activeDirection] = current.query.sort.split(":");
      if (activeField === field) {
        return { ...current, query: { ...current.query, sort: `${field}:${activeDirection === "asc" ? "desc" : "asc"}`, page: 1 } };
      }

      return { ...current, query: { ...current.query, sort: `${field}:asc`, page: 1 } };
    });
  };

  useEffect(() => {
    let isMounted = true;

    setIsLoading(true);
    apiRequest<PageResult<AuditRecordDto>>(`/api/admin/audit?${buildGridQueryString(query)}`, accessToken, onReauthenticate)
      .then((page) => {
        if (isMounted) {
          setItems(page.items);
          setTotalCount(page.totalCount);
          setError(null);
        }
      })
      .catch((requestError) => {
        if (isMounted) {
          setError(requestError instanceof Error ? requestError.message : labels.loadError);
          setItems([]);
          setTotalCount(0);
        }
      })
      .finally(() => {
        if (isMounted) {
          setIsLoading(false);
        }
      });

    return () => {
      isMounted = false;
    };
  }, [accessToken, onReauthenticate, query]);

  useEffect(() => {
    if (preview && !items.some((item) => item.id === preview)) {
      setView((current) => ({ ...current, preview: null }));
    }
  }, [items, preview, setView]);

  const [activeSortField, activeSortDirection = "desc"] = query.sort.split(":");
  const pageCount = Math.max(1, Math.ceil(totalCount / query.pageSize));

  return (
    <div className="space-y-6">
      <CatalogToolbar
        title={labels.title}
        description="Search audit events and inspect details without leaving the log."
        totalCount={totalCount}
        search={query.search}
        onSearchChange={(value) => setView((current) => ({ ...current, query: { ...current.query, search: value, page: 1 } }))}
        searchPlaceholder="Search actor, action or target"
        filter={query.filter}
        onFilterChange={(value) => setView((current) => ({ ...current, query: { ...current.query, filter: value, page: 1 } }))}
        filterOptions={[
          { value: "all", label: "All events" },
          { value: "success", label: "Successful" },
          { value: "failure", label: "Failed" },
          { value: "warn", label: "Warnings" },
          { value: "identity", label: "Identity & RBAC" },
          { value: "policy", label: "Policy & Governance" },
          { value: "node", label: "Nodes & Cluster" },
          { value: "mail", label: "Notifications" },
          { value: "access", label: "Access & Bans" },
          { value: "torrent", label: "Torrents" }
        ]}
        sortValue={query.sort}
        onSortChange={(value) => setView((current) => ({ ...current, query: { ...current.query, sort: value, page: 1 } }))}
        sortOptions={[
          { value: "occurred:desc", label: "Newest first" },
          { value: "occurred:asc", label: "Oldest first" },
          { value: "action:asc", label: "Action A-Z" },
          { value: "severity:desc", label: "Severity high-low" },
          { value: "actor:asc", label: "Actor A-Z" }
        ]}
        pageSize={query.pageSize}
        onPageSizeChange={(value) => setView((current) => ({ ...current, query: { ...current.query, pageSize: value, page: 1 } }))}
      />

      <div className="app-data-table">
        <div className="overflow-x-auto">
          <table className="min-w-full text-sm">
            <thead className="app-table-head">
              <tr>
                <th className="px-5 py-4"><SortHeaderButton label={labels.tableAction} active={activeSortField === "action"} direction={activeSortDirection as "asc" | "desc"} onClick={() => toggleSort("action")} /></th>
                <th className="px-5 py-4"><SortHeaderButton label={labels.tableActor} active={activeSortField === "actor"} direction={activeSortDirection as "asc" | "desc"} onClick={() => toggleSort("actor")} /></th>
                <th className="px-5 py-4">{labels.tableRole}</th>
                <th className="px-5 py-4"><SortHeaderButton label={labels.tableSeverity} active={activeSortField === "severity"} direction={activeSortDirection as "asc" | "desc"} onClick={() => toggleSort("severity")} /></th>
                <th className="px-5 py-4">{labels.entity}</th>
                <th className="px-5 py-4">{labels.result}</th>
                <th className="px-5 py-4"><SortHeaderButton label={labels.occurred} active={activeSortField === "occurred"} direction={activeSortDirection as "asc" | "desc"} onClick={() => toggleSort("occurred")} /></th>
                <th className="px-5 py-4 text-right">Actions</th>
              </tr>
            </thead>
            <tbody>
              {isLoading ? (
                <TableStateRow colSpan={8} title="Loading audit log" message="Refreshing the latest operational and access events." />
              ) : error && totalCount === 0 ? (
                <TableStateRow colSpan={8} title="Unable to load audit log" message={error} />
              ) : items.length === 0 ? (
                <TableStateRow colSpan={8} title="No audit events match this view" message="Try a broader search or relax the current filter." />
              ) : items.map((item) => (
                <CatalogTableRow key={item.id} onOpen={() => setView((current) => ({ ...current, preview: item.id }))}>
                  <td className="px-5 py-4"><div className="app-grid-primary">{item.action}</div></td>
                  <td className="px-5 py-4"><div className="app-grid-primary font-mono">{item.actorId}</div></td>
                  <td className="px-5 py-4 text-steel">{item.actorRole}</td>
                  <td className="px-5 py-4">
                    <span className={item.severity === "error" || item.severity === "high" ? "app-chip-warn" : "app-chip-soft"}>{item.severity}</span>
                  </td>
                  <td className="px-5 py-4 text-steel">{item.entityType} / {item.entityId}</td>
                  <td className="px-5 py-4 text-steel">{item.result}</td>
                  <td className="px-5 py-4">
                    <div className="text-steel">{new Date(item.occurredAtUtc).toLocaleString()}</div>
                    <div className="app-inline-id">
                      <span className="font-mono">{item.correlationId || "N/A"}</span>
                      {item.correlationId ? <CopyValueButton value={item.correlationId} label={`Copy correlation id ${item.correlationId}`} /> : null}
                    </div>
                  </td>
                  <td className="px-5 py-4 text-right">
                    <RowActionsMenu items={[{ label: "View", icon: <EyeIcon />, onClick: () => setView((current) => ({ ...current, preview: item.id })) }]} />
                  </td>
                </CatalogTableRow>
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
        onPageChange={(page) => setView((current) => ({ ...current, query: { ...current.query, page } }))}
      />

      <PreviewDrawer
        open={previewItem !== null}
        title={previewItem?.action ?? ""}
        subtitle={previewItem ? `${previewItem.entityType} / ${previewItem.entityId}` : undefined}
        onClose={() => setView((current) => ({ ...current, preview: null }))}
      >
        {previewItem ? (
          <div className="space-y-4">
            <div className="app-detail-grid">
              <div className="app-subtle-panel space-y-2">
                <div className="app-kicker">Actor</div>
                <div className="font-semibold text-ink">{previewItem.actorId}</div>
                <div className="text-sm text-steel">{previewItem.actorRole}</div>
              </div>
              <div className="app-subtle-panel space-y-2">
                <div className="app-kicker">Result</div>
                <div className="font-semibold text-ink">{previewItem.result}</div>
                <div className="text-sm text-steel">{previewItem.severity}</div>
              </div>
              <div className="app-subtle-panel space-y-2">
                <div className="app-kicker">Occurred</div>
                <div className="font-semibold text-ink">{new Date(previewItem.occurredAtUtc).toLocaleString()}</div>
              </div>
            </div>
            <div className="app-subtle-panel space-y-2">
              <div className="app-kicker">Correlation</div>
              <div className="font-mono text-xs text-steel">{previewItem.correlationId || "N/A"}</div>
            </div>
            <div className="app-subtle-panel space-y-2">
              <div className="app-kicker">Origin</div>
              <div className="text-sm text-steel">IP: {previewItem.ipAddress || "Unknown"}</div>
            </div>
          </div>
        ) : null}
      </PreviewDrawer>
    </div>
  );
}

function MaintenancePage({
  accessToken,
  onReauthenticate
}: {
  accessToken: string;
  onReauthenticate: (fresh: boolean) => Promise<void>;
}) {
  const { dictionary } = useI18n();
  const labels = dictionary.maintenance;
  const [view, setView] = useCatalogViewState({
    search: "",
    filter: "all",
    sort: "requested:desc",
    page: 1,
    pageSize: 25
  });
  const { query, preview } = view;
  const [items, setItems] = useState<MaintenanceRunDto[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const previewItem = items.find((item) => item.id === preview) ?? null;
  const [activeSortField, activeSortDirection = "desc"] = query.sort.split(":");
  const pageCount = Math.max(1, Math.ceil(totalCount / query.pageSize));

  useEffect(() => {
    let isMounted = true;

    setIsLoading(true);
    apiRequest<PageResult<MaintenanceRunDto>>(
      `/api/admin/maintenance?${buildGridQueryString(query)}`,
      accessToken,
      onReauthenticate
    )
      .then((page) => {
        if (!isMounted) {
          return;
        }
        setItems(page.items);
        setTotalCount(page.totalCount);
        setError(null);
      })
      .catch((requestError) => {
        if (!isMounted) {
          return;
        }

        setError(requestError instanceof Error ? requestError.message : labels.loadError);
        setItems([]);
        setTotalCount(0);
      })
      .finally(() => {
        if (isMounted) {
          setIsLoading(false);
        }
      });

    return () => {
      isMounted = false;
    };
  }, [accessToken, labels.loadError, onReauthenticate, query]);

  useEffect(() => {
    if (preview && !items.some((item) => item.id === preview)) {
      setView((current) => ({ ...current, preview: null }));
    }
  }, [items, preview, setView]);

  const toggleSort = (field: string) => {
    const nextDirection =
      activeSortField === field && activeSortDirection === "asc"
        ? "desc"
        : "asc";
    setView((current) => ({
      ...current,
      query: { ...current.query, sort: `${field}:${nextDirection}`, page: 1 }
    }));
  };

  return (
    <div className="space-y-6">
      <CatalogToolbar
        title={labels.title}
        description="Review maintenance history and inspect operational runs without leaving the admin surface."
        totalCount={totalCount}
        search={query.search}
        onSearchChange={(value) => setView((current) => ({ ...current, query: { ...current.query, search: value, page: 1 } }))}
        searchPlaceholder="Search operation, requester or correlation"
        filter={query.filter}
        onFilterChange={(value) => setView((current) => ({ ...current, query: { ...current.query, filter: value, page: 1 } }))}
        filterOptions={[
          { value: "all", label: "All runs" },
          { value: "completed", label: "Completed" },
          { value: "failed", label: "Failed" },
          { value: "running", label: "Running" }
        ]}
        sortValue={query.sort}
        onSortChange={(value) => setView((current) => ({ ...current, query: { ...current.query, sort: value, page: 1 } }))}
        sortOptions={[
          { value: "requested:desc", label: "Newest first" },
          { value: "requested:asc", label: "Oldest first" },
          { value: "operation:asc", label: "Operation A-Z" },
          { value: "status:asc", label: "Status A-Z" },
          { value: "status:desc", label: "Status Z-A" }
        ]}
        pageSize={query.pageSize}
        onPageSizeChange={(value) => setView((current) => ({ ...current, query: { ...current.query, pageSize: value, page: 1 } }))}
      />

      <div className="app-data-table">
        <div className="overflow-x-auto">
          <table className="min-w-full text-sm">
            <thead className="app-table-head">
              <tr>
                <th className="px-5 py-4">
                  <SortHeaderButton
                    label={labels.tableOperation}
                    active={activeSortField === "operation"}
                    direction={activeSortDirection as "asc" | "desc"}
                    onClick={() => toggleSort("operation")}
                  />
                </th>
                <th className="px-5 py-4">{labels.tableRequestedBy}</th>
                <th className="px-5 py-4">
                  <SortHeaderButton
                    label={labels.tableRequestedAt}
                    active={activeSortField === "requested"}
                    direction={activeSortDirection as "asc" | "desc"}
                    onClick={() => toggleSort("requested")}
                  />
                </th>
                <th className="px-5 py-4">
                  <SortHeaderButton
                    label={labels.tableStatus}
                    active={activeSortField === "status"}
                    direction={activeSortDirection as "asc" | "desc"}
                    onClick={() => toggleSort("status")}
                  />
                </th>
                <th className="px-5 py-4">{labels.tableCorrelation}</th>
                <th className="px-5 py-4 text-right">Actions</th>
              </tr>
            </thead>
            <tbody>
              {isLoading ? (
                <TableStateRow colSpan={6} title="Loading maintenance history" message="Refreshing operational maintenance runs." />
              ) : error && totalCount === 0 ? (
                <TableStateRow colSpan={6} title="Unable to load maintenance history" message={error} />
              ) : items.length === 0 ? (
                <TableStateRow colSpan={6} title={labels.title} message={labels.empty} />
              ) : (
                items.map((item) => (
                  <CatalogTableRow key={item.id} onOpen={() => setView((current) => ({ ...current, preview: item.id }))}>
                    <td className="px-5 py-4"><div className="app-grid-primary">{item.operation}</div></td>
                    <td className="px-5 py-4 text-steel">{item.requestedBy}</td>
                    <td className="px-5 py-4 text-steel">{new Date(item.requestedAtUtc).toLocaleString()}</td>
                    <td className="px-5 py-4">
                      <span className={item.status === "failed" ? "app-chip-warn" : item.status === "running" ? "app-chip-strong" : "app-chip-soft"}>
                        {item.status}
                      </span>
                    </td>
                    <td className="px-5 py-4">
                      <div className="font-mono text-xs text-steel">{item.correlationId}</div>
                      <div className="app-inline-id">
                        <CopyValueButton value={item.correlationId} label={`Copy correlation id ${item.correlationId}`} />
                      </div>
                    </td>
                    <td className="px-5 py-4 text-right">
                      <RowActionsMenu items={[{ label: "View", icon: <EyeIcon />, onClick: () => setView((current) => ({ ...current, preview: item.id })) }]} />
                    </td>
                  </CatalogTableRow>
                ))
              )}
            </tbody>
          </table>
        </div>
      </div>

      <PaginationFooter
        page={query.page}
        pageCount={pageCount}
        totalCount={totalCount}
        pageSize={query.pageSize}
        onPageChange={(page) => setView((current) => ({ ...current, query: { ...current.query, page } }))}
      />

      <PreviewDrawer
        open={previewItem !== null}
        title={previewItem?.operation ?? ""}
        subtitle={previewItem?.requestedBy}
        onClose={() => setView((current) => ({ ...current, preview: null }))}
      >
        {previewItem ? (
          <div className="space-y-4">
            <div className="app-detail-grid">
              <div className="app-subtle-panel space-y-2">
                <div className="app-kicker">{labels.tableRequestedBy}</div>
                <div className="font-semibold text-ink">{previewItem.requestedBy}</div>
              </div>
              <div className="app-subtle-panel space-y-2">
                <div className="app-kicker">{labels.tableStatus}</div>
                <div className="font-semibold text-ink">{previewItem.status}</div>
              </div>
              <div className="app-subtle-panel space-y-2">
                <div className="app-kicker">{labels.tableRequestedAt}</div>
                <div className="font-semibold text-ink">{new Date(previewItem.requestedAtUtc).toLocaleString()}</div>
              </div>
            </div>
            <div className="app-subtle-panel space-y-2">
              <div className="app-kicker">{labels.tableCorrelation}</div>
              <div className="font-mono text-xs text-steel">{previewItem.correlationId}</div>
            </div>
            <div className="app-subtle-panel space-y-2">
              <div className="app-kicker">Run id</div>
              <div className="font-mono text-xs text-steel">{previewItem.id}</div>
            </div>
          </div>
        ) : null}
      </PreviewDrawer>
    </div>
  );
}

// ─── Notification Outbox Page ────────────────────────────────────────────────

function NotificationsPage({
  accessToken,
  onReauthenticate,
  permissions
}: {
  accessToken: string;
  onReauthenticate: (fresh: boolean) => Promise<void>;
  permissions: string[];
}) {
  const [view, setView] = useCatalogViewState({
    search: "",
    filter: "all",
    sort: "createdat:desc",
    page: 1,
    pageSize: 25
  });
  const { query, preview } = view;
  const [items, setItems] = useState<NotificationOutboxItemDto[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [stats, setStats] = useState<NotificationOutboxStatsDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [status, setStatus] = useState<string | null>(null);
  const [detail, setDetail] = useState<NotificationOutboxDetailDto | null>(null);
  const canExecute = hasPermission(permissions, "admin.maintenance.execute");
  const previewItem = items.find((item) => item.id === preview) ?? null;
  const [activeSortField, activeSortDirection = "desc"] = query.sort.split(":");
  const pageCount = Math.max(1, Math.ceil(totalCount / query.pageSize));

  const reload = useCallback(async () => {
    const [page, statsResult] = await Promise.all([
      apiRequest<PageResult<NotificationOutboxItemDto>>(
        `/api/admin/notifications?${buildGridQueryString(query)}`,
        accessToken,
        onReauthenticate
      ),
      apiRequest<NotificationOutboxStatsDto>(
        "/api/admin/notifications/stats",
        accessToken,
        onReauthenticate
      )
    ]);
    setItems(page.items);
    setTotalCount(page.totalCount);
    setStats(statsResult);
  }, [accessToken, onReauthenticate, query]);

  useEffect(() => {
    let isMounted = true;
    setIsLoading(true);
    reload()
      .then(() => { if (isMounted) setError(null); })
      .catch((err) => {
        if (isMounted) {
          setError(err instanceof Error ? err.message : "Failed to load notifications.");
          setItems([]);
          setTotalCount(0);
        }
      })
      .finally(() => { if (isMounted) setIsLoading(false); });
    return () => { isMounted = false; };
  }, [reload]);

  useEffect(() => {
    if (!previewItem) { setDetail(null); return; }
    apiRequest<NotificationOutboxDetailDto>(
      `/api/admin/notifications/${previewItem.id}`,
      accessToken,
      onReauthenticate
    ).then(setDetail).catch(() => setDetail(null));
  }, [previewItem, accessToken, onReauthenticate]);

  const toggleSort = (field: string) => {
    const nextDirection = activeSortField === field && activeSortDirection === "asc" ? "desc" : "asc";
    setView((current) => ({ ...current, query: { ...current.query, sort: `${field}:${nextDirection}`, page: 1 } }));
  };

  const handleRetry = async (id: string) => {
    try {
      await apiMutation<unknown, Record<string, never>>(`/api/admin/notifications/${id}/retry`, "POST", accessToken, {}, onReauthenticate);
      setStatus("Notification queued for retry.");
      await reload();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Retry failed.");
    }
  };

  const handleCancel = async (id: string) => {
    try {
      await apiMutation<unknown, Record<string, never>>(`/api/admin/notifications/${id}/cancel`, "POST", accessToken, {}, onReauthenticate);
      setStatus("Notification cancelled.");
      await reload();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Cancel failed.");
    }
  };

  const statusTone = (s: string): "good" | "warn" | "neutral" =>
    s === "Sent" ? "good" : s === "Failed" || s === "Cancelled" ? "warn" : "neutral";

  return (
    <div className="app-page-stack">
      {status && <div className="app-notice-success"><p>{status}</p></div>}
      {error && <div className="app-notice-warn"><p>{error}</p></div>}

      {stats && (
        <section className="grid gap-4 md:grid-cols-5">
          {([
            ["Pending", stats.pendingCount],
            ["Processing", stats.processingCount],
            ["Sent", stats.sentCount],
            ["Failed", stats.failedCount],
            ["Cancelled", stats.cancelledCount]
          ] as const).map(([label, count]) => (
            <div key={label} className="app-stat-card">
              <p className="text-[11px] font-semibold uppercase tracking-[0.18em] text-steel/70">{label}</p>
              <p className="mt-2 text-2xl font-bold text-ink">{count}</p>
            </div>
          ))}
        </section>
      )}

      <CatalogToolbar
        title="Email outbox"
        description="View notification delivery status, retry failed messages, or cancel pending sends."
        totalCount={totalCount}
        search={query.search}
        onSearchChange={(value) => setView((current) => ({ ...current, query: { ...current.query, search: value, page: 1 } }))}
        searchPlaceholder="Search recipient, subject, template or correlation"
        filter={query.filter}
        onFilterChange={(value) => setView((current) => ({ ...current, query: { ...current.query, filter: value, page: 1 } }))}
        filterOptions={[
          { value: "all", label: "All" },
          { value: "Pending", label: "Pending" },
          { value: "Processing", label: "Processing" },
          { value: "Sent", label: "Sent" },
          { value: "Failed", label: "Failed" },
          { value: "Cancelled", label: "Cancelled" }
        ]}
        sortValue={query.sort}
        onSortChange={(value) => setView((current) => ({ ...current, query: { ...current.query, sort: value, page: 1 } }))}
        sortOptions={[
          { value: "createdat:desc", label: "Newest first" },
          { value: "createdat:asc", label: "Oldest first" },
          { value: "recipient:asc", label: "Recipient A-Z" },
          { value: "subject:asc", label: "Subject A-Z" },
          { value: "status:asc", label: "Status A-Z" }
        ]}
        pageSize={query.pageSize}
        onPageSizeChange={(value) => setView((current) => ({ ...current, query: { ...current.query, pageSize: value, page: 1 } }))}
      />

      <div className="app-data-table">
        <div className="overflow-x-auto">
          <table className="min-w-full text-sm">
            <thead className="app-table-head">
              <tr>
                <th className="px-5 py-4">
                  <SortHeaderButton label="Recipient" active={activeSortField === "recipient"} direction={activeSortDirection as "asc" | "desc"} onClick={() => toggleSort("recipient")} />
                </th>
                <th className="px-5 py-4">
                  <SortHeaderButton label="Subject" active={activeSortField === "subject"} direction={activeSortDirection as "asc" | "desc"} onClick={() => toggleSort("subject")} />
                </th>
                <th className="px-5 py-4">Template</th>
                <th className="px-5 py-4">
                  <SortHeaderButton label="Created" active={activeSortField === "createdat"} direction={activeSortDirection as "asc" | "desc"} onClick={() => toggleSort("createdat")} />
                </th>
                <th className="px-5 py-4">
                  <SortHeaderButton label="Status" active={activeSortField === "status"} direction={activeSortDirection as "asc" | "desc"} onClick={() => toggleSort("status")} />
                </th>
                <th className="px-5 py-4">Retries</th>
                {canExecute && <th className="px-5 py-4" />}
              </tr>
            </thead>
            <tbody>
              {isLoading ? (
                <TableStateRow colSpan={canExecute ? 7 : 6} message="Loading notifications..." />
              ) : items.length === 0 ? (
                <TableStateRow colSpan={canExecute ? 7 : 6} message="No notifications found." />
              ) : (
                items.map((item) => (
                  <CatalogTableRow key={item.id} previewKey={item.id} currentPreview={preview} onPreview={(key) => setView((current) => ({ ...current, preview: current.preview === key ? null : key }))}>
                    <td className="px-5 py-4 font-medium text-ink">{item.recipient}</td>
                    <td className="px-5 py-4 max-w-[200px] truncate">{item.subject}</td>
                    <td className="px-5 py-4 text-steel">{item.templateName ?? "—"}</td>
                    <td className="px-5 py-4 text-steel">{new Date(item.createdAtUtc).toLocaleString()}</td>
                    <td className="px-5 py-4"><StatusPill tone={statusTone(item.status)}>{item.status}</StatusPill></td>
                    <td className="px-5 py-4 text-steel">{item.retryCount}</td>
                    {canExecute && (
                      <td className="px-5 py-4">
                        <RowActionsMenu items={[
                          ...(item.status === "Failed" || item.status === "Cancelled" ? [{ label: "Retry", onClick: () => handleRetry(item.id) }] : []),
                          ...(item.status !== "Sent" && item.status !== "Cancelled" ? [{ label: "Cancel", onClick: () => handleCancel(item.id) }] : [])
                        ]} />
                      </td>
                    )}
                  </CatalogTableRow>
                ))
              )}
            </tbody>
          </table>
        </div>
      </div>

      <PaginationFooter
        page={query.page}
        pageCount={pageCount}
        totalCount={totalCount}
        pageSize={query.pageSize}
        onPageChange={(page) => setView((current) => ({ ...current, query: { ...current.query, page } }))}
      />

      <PreviewDrawer
        open={previewItem !== null}
        title={previewItem?.subject ?? ""}
        subtitle={previewItem?.recipient}
        onClose={() => setView((current) => ({ ...current, preview: null }))}
      >
        {previewItem ? (
          <div className="space-y-4">
            <div className="app-detail-grid">
              <div className="app-subtle-panel space-y-2">
                <div className="app-kicker">Recipient</div>
                <div className="font-semibold text-ink">{previewItem.recipient}</div>
              </div>
              <div className="app-subtle-panel space-y-2">
                <div className="app-kicker">Status</div>
                <StatusPill tone={statusTone(previewItem.status)}>{previewItem.status}</StatusPill>
              </div>
              <div className="app-subtle-panel space-y-2">
                <div className="app-kicker">Template</div>
                <div className="font-semibold text-ink">{previewItem.templateName ?? "None"}</div>
              </div>
              <div className="app-subtle-panel space-y-2">
                <div className="app-kicker">Created</div>
                <div className="font-semibold text-ink">{new Date(previewItem.createdAtUtc).toLocaleString()}</div>
              </div>
              {previewItem.processedAtUtc && (
                <div className="app-subtle-panel space-y-2">
                  <div className="app-kicker">Processed</div>
                  <div className="font-semibold text-ink">{new Date(previewItem.processedAtUtc).toLocaleString()}</div>
                </div>
              )}
              {previewItem.lastError && (
                <div className="app-subtle-panel space-y-2 col-span-full">
                  <div className="app-kicker">Last error</div>
                  <div className="text-sm text-ember">{previewItem.lastError}</div>
                </div>
              )}
            </div>
            {detail && detail.attempts.length > 0 && (
              <div>
                <p className="app-kicker mb-2">Delivery attempts</p>
                <div className="space-y-2">
                  {detail.attempts.map((attempt) => (
                    <div key={attempt.id} className="rounded-xl border border-slate-200 bg-slate-50/80 px-4 py-3 text-sm">
                      <div className="flex items-center justify-between">
                        <span className="font-medium text-ink">{new Date(attempt.attemptedAtUtc).toLocaleString()}</span>
                        <StatusPill tone={attempt.succeeded ? "good" : "warn"}>{attempt.succeeded ? "OK" : `Failed${attempt.smtpStatusCode ? ` (${attempt.smtpStatusCode})` : ""}`}</StatusPill>
                      </div>
                      <div className="mt-1 flex gap-4 text-xs text-steel">
                        <span>{attempt.durationMs}ms</span>
                        {attempt.errorMessage && <span className="text-ember">{attempt.errorMessage}</span>}
                      </div>
                    </div>
                  ))}
                </div>
              </div>
            )}
            <div className="app-subtle-panel space-y-2">
              <div className="app-kicker">Notification ID</div>
              <div className="font-mono text-xs text-steel">{previewItem.id}</div>
            </div>
            {previewItem.correlationId && (
              <div className="app-subtle-panel space-y-2">
                <div className="app-kicker">Correlation ID</div>
                <div className="font-mono text-xs text-steel">{previewItem.correlationId}</div>
              </div>
            )}
          </div>
        ) : null}
      </PreviewDrawer>
    </div>
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
        if (typeof window !== "undefined") {
          window.sessionStorage.removeItem(adminSignedOutStorageKey);
        }
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

// ─── Self-Service Pages (unauthenticated) ───────────────────────────────────

function SelfServiceLayout({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div className="flex min-h-screen items-center justify-center bg-slate-50 px-6">
      <div className="w-full max-w-md">
        <div className="mb-8 text-center">
          <span className="text-2xl font-bold tracking-tight text-ink">BeeTracker</span>
          <p className="mt-1 text-sm text-steel">Admin self-service</p>
        </div>
        <Card title={title} eyebrow="Self-Service">
          {children}
        </Card>
        <div className="mt-6 text-center text-sm text-steel">
          <Link to="/" className="text-brand hover:underline">Back to sign in</Link>
        </div>
      </div>
    </div>
  );
}

function SelfServiceRegisterPage() {
  const [form, setForm] = useState({ userName: "", email: "", password: "", confirmPassword: "" });
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState(false);
  const [submitting, setSubmitting] = useState(false);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    setSubmitting(true);
    try {
      const res = await fetch("/api/self-service/admin/register", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(form)
      });
      if (!res.ok) {
        const err = await res.json().catch(() => ({ message: "Registration failed." }));
        setError(err.message ?? "Registration failed.");
      } else {
        setSuccess(true);
      }
    } catch {
      setError("Network error. Please try again.");
    } finally {
      setSubmitting(false);
    }
  };

  if (success) {
    return (
      <SelfServiceLayout title="Registration submitted">
        <p className="text-sm text-steel">Your account has been created. Check your email for an activation link.</p>
        <div className="mt-4">
          <Link to="/self-service/activate" className="text-brand text-sm hover:underline">Have an activation token?</Link>
        </div>
      </SelfServiceLayout>
    );
  }

  return (
    <SelfServiceLayout title="Register admin account">
      {error && <div className="app-notice-warn mb-4"><p>{error}</p></div>}
      <form onSubmit={handleSubmit} className="space-y-4">
        <label className="flex flex-col gap-1.5 text-sm">
          <span className="font-medium text-steel">Username</span>
          <input className="app-input" required value={form.userName} onChange={(e) => setForm({ ...form, userName: e.target.value })} />
        </label>
        <label className="flex flex-col gap-1.5 text-sm">
          <span className="font-medium text-steel">Email</span>
          <input className="app-input" type="email" required value={form.email} onChange={(e) => setForm({ ...form, email: e.target.value })} />
        </label>
        <label className="flex flex-col gap-1.5 text-sm">
          <span className="font-medium text-steel">Password</span>
          <input className="app-input" type="password" required value={form.password} onChange={(e) => setForm({ ...form, password: e.target.value })} />
        </label>
        <label className="flex flex-col gap-1.5 text-sm">
          <span className="font-medium text-steel">Confirm password</span>
          <input className="app-input" type="password" required value={form.confirmPassword} onChange={(e) => setForm({ ...form, confirmPassword: e.target.value })} />
        </label>
        <button type="submit" className="app-button-primary w-full" disabled={submitting}>
          {submitting ? "Registering..." : "Register"}
        </button>
      </form>
      <div className="mt-4 flex justify-between text-sm">
        <Link to="/self-service/forgot-password" className="text-brand hover:underline">Forgot password?</Link>
        <Link to="/self-service/activate" className="text-brand hover:underline">Activate account</Link>
      </div>
    </SelfServiceLayout>
  );
}

function SelfServiceActivatePage() {
  const [token, setToken] = useState(() => new URLSearchParams(window.location.search).get("token") ?? "");
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState(false);
  const [submitting, setSubmitting] = useState(false);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    setSubmitting(true);
    try {
      const res = await fetch("/api/self-service/admin/activate", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ token })
      });
      if (!res.ok) {
        const err = await res.json().catch(() => ({ message: "Activation failed." }));
        setError(err.message ?? "Activation failed.");
      } else {
        setSuccess(true);
      }
    } catch {
      setError("Network error. Please try again.");
    } finally {
      setSubmitting(false);
    }
  };

  if (success) {
    return (
      <SelfServiceLayout title="Account activated">
        <p className="text-sm text-steel">Your account is now active. You can sign in.</p>
      </SelfServiceLayout>
    );
  }

  return (
    <SelfServiceLayout title="Activate account">
      {error && <div className="app-notice-warn mb-4"><p>{error}</p></div>}
      <form onSubmit={handleSubmit} className="space-y-4">
        <label className="flex flex-col gap-1.5 text-sm">
          <span className="font-medium text-steel">Activation token</span>
          <input className="app-input" required value={token} onChange={(e) => setToken(e.target.value)} placeholder="Paste the token from your email" />
        </label>
        <button type="submit" className="app-button-primary w-full" disabled={submitting}>
          {submitting ? "Activating..." : "Activate"}
        </button>
      </form>
    </SelfServiceLayout>
  );
}

function SelfServiceForgotPasswordPage() {
  const [email, setEmail] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState(false);
  const [submitting, setSubmitting] = useState(false);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    setSubmitting(true);
    try {
      const res = await fetch("/api/self-service/admin/password/forgot", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ email })
      });
      if (!res.ok) {
        const err = await res.json().catch(() => ({ message: "Request failed." }));
        setError(err.message ?? "Request failed.");
      } else {
        setSuccess(true);
      }
    } catch {
      setError("Network error. Please try again.");
    } finally {
      setSubmitting(false);
    }
  };

  if (success) {
    return (
      <SelfServiceLayout title="Email sent">
        <p className="text-sm text-steel">If an account exists with that email, a password reset link has been sent.</p>
        <div className="mt-4">
          <Link to="/self-service/reset-password" className="text-brand text-sm hover:underline">Have a reset token?</Link>
        </div>
      </SelfServiceLayout>
    );
  }

  return (
    <SelfServiceLayout title="Forgot password">
      {error && <div className="app-notice-warn mb-4"><p>{error}</p></div>}
      <form onSubmit={handleSubmit} className="space-y-4">
        <label className="flex flex-col gap-1.5 text-sm">
          <span className="font-medium text-steel">Email address</span>
          <input className="app-input" type="email" required value={email} onChange={(e) => setEmail(e.target.value)} />
        </label>
        <button type="submit" className="app-button-primary w-full" disabled={submitting}>
          {submitting ? "Sending..." : "Send reset link"}
        </button>
      </form>
      <div className="mt-4 text-sm">
        <Link to="/self-service/register" className="text-brand hover:underline">Create an account</Link>
      </div>
    </SelfServiceLayout>
  );
}

function SelfServiceResetPasswordPage() {
  const [form, setForm] = useState({
    token: new URLSearchParams(window.location.search).get("token") ?? "",
    newPassword: "",
    confirmNewPassword: ""
  });
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState(false);
  const [submitting, setSubmitting] = useState(false);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    setSubmitting(true);
    try {
      const res = await fetch("/api/self-service/admin/password/reset", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(form)
      });
      if (!res.ok) {
        const err = await res.json().catch(() => ({ message: "Password reset failed." }));
        setError(err.message ?? "Password reset failed.");
      } else {
        setSuccess(true);
      }
    } catch {
      setError("Network error. Please try again.");
    } finally {
      setSubmitting(false);
    }
  };

  if (success) {
    return (
      <SelfServiceLayout title="Password reset">
        <p className="text-sm text-steel">Your password has been reset. You can now sign in with your new password.</p>
      </SelfServiceLayout>
    );
  }

  return (
    <SelfServiceLayout title="Reset password">
      {error && <div className="app-notice-warn mb-4"><p>{error}</p></div>}
      <form onSubmit={handleSubmit} className="space-y-4">
        <label className="flex flex-col gap-1.5 text-sm">
          <span className="font-medium text-steel">Reset token</span>
          <input className="app-input" required value={form.token} onChange={(e) => setForm({ ...form, token: e.target.value })} placeholder="Paste the token from your email" />
        </label>
        <label className="flex flex-col gap-1.5 text-sm">
          <span className="font-medium text-steel">New password</span>
          <input className="app-input" type="password" required value={form.newPassword} onChange={(e) => setForm({ ...form, newPassword: e.target.value })} />
        </label>
        <label className="flex flex-col gap-1.5 text-sm">
          <span className="font-medium text-steel">Confirm new password</span>
          <input className="app-input" type="password" required value={form.confirmNewPassword} onChange={(e) => setForm({ ...form, confirmNewPassword: e.target.value })} />
        </label>
        <button type="submit" className="app-button-primary w-full" disabled={submitting}>
          {submitting ? "Resetting..." : "Reset password"}
        </button>
      </form>
    </SelfServiceLayout>
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
  const location = useLocation();
  const capabilities = session.capabilities ?? [];
  const routeMeta = getRouteMeta(location.pathname, dictionary);
  const [profileMenuOpen, setProfileMenuOpen] = useState(false);
  const [sectionMenuOpen, setSectionMenuOpen] = useState<string | null>(null);
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false);
  const navbarRef = useRef<HTMLElement | null>(null);
  const navigationSections = useMemo(
    (): NavigationSection[] => [
      {
        id: "overview",
        label: "Overview",
        to: "/",
        icon: "overview",
        links: [
          hasPermission(session.permissions, "admin.dashboard.view")
            ? { to: "/", label: dictionary.routes.overviewTitle, description: "Cluster health and access posture.", icon: "overview" }
            : null
        ].filter((link): link is NavigationLink => link !== null)
      },
      {
        id: "access",
        label: "Access Management",
        to: "/admin-users",
        icon: "users",
        links: [
          hasPermission(session.permissions, permissionKeys.usersView)
            ? { to: "/admin-users", label: "Admin users", description: "Provision accounts and assign roles.", icon: "users" }
            : null,
          hasPermission(session.permissions, permissionKeys.rolesView)
            ? { to: "/roles", label: "Roles", description: "Control system and custom admin roles.", icon: "roles" }
            : null,
          hasPermission(session.permissions, permissionKeys.permissionGroupsView)
            ? { to: "/permission-groups", label: "Groups", description: "Bundle permissions into reusable sets.", icon: "groups" }
            : null
        ].filter((link): link is NavigationLink => link !== null)
      },
      {
        id: "tracker",
        label: "Tracker",
        to: "/torrents",
        icon: "torrents",
        links: [
          hasPermission(session.permissions, "admin.torrents.view")
            ? { to: "/torrents", label: "Torrent catalog", description: "Manage tracker mode and rollout policies.", icon: "torrents" }
            : null,
          hasPermission(session.permissions, "admin.passkeys.view")
            ? { to: "/passkeys", label: "Passkeys", description: "Rotate and revoke private tracker credentials.", icon: "passkeys" }
            : null,
          hasPermission(session.permissions, "admin.tracker_access.view")
            ? { to: "/permissions", label: "Tracker access", description: "Control leech, seed, scrape and private access.", icon: "trackerAccess" }
            : null,
          hasPermission(session.permissions, "admin.bans.view")
            ? { to: "/bans", label: "Ban rules", description: "Create and expire enforcement rules.", icon: "bans" }
            : null,
          hasPermission(session.permissions, "admin.system_settings.view")
            ? { to: "/tracker-nodes", label: "Node config", description: "Select and configure tracker node protocols, runtime and coordination.", icon: "nodeConfig" }
            : null,
          hasPermission(session.permissions, "admin.bans.view")
            ? { to: "/abuse", label: "Abuse intelligence", description: "Monitor abuse scoring and top offenders per node.", icon: "bans" }
            : null
        ].filter((link): link is NavigationLink => link !== null)
      },
      {
        id: "operations",
        label: "Operations",
        to: "/governance",
        icon: "maintenance",
        links: [
          hasPermission(session.permissions, "admin.system_settings.view")
            ? { to: "/governance", label: "Governance", description: "Runtime governance flags and node lifecycle controls.", icon: "maintenance" }
            : null,
          hasPermission(session.permissions, "admin.dashboard.view")
            ? { to: "/cluster", label: "Cluster", description: "Node states, shard ownership and cluster health.", icon: "overview" }
            : null,
          hasPermission(session.permissions, "admin.nodes.view")
            ? { to: "/swarms", label: "Swarms", description: "Runtime swarm state aggregated across all gateway nodes.", icon: "torrents" }
            : null,
          hasPermission(session.permissions, permissionKeys.auditView)
            ? { to: "/notifications", label: "Notifications", description: "Email outbox delivery status and retry controls.", icon: "audit" }
            : null
        ].filter((link): link is NavigationLink => link !== null)
      },
      {
        id: "audit",
        label: "Audit",
        to: "/audit",
        icon: "audit",
        links: [
          hasPermission(session.permissions, permissionKeys.auditView)
            ? { to: "/audit", label: dictionary.routes.auditTitle, description: "Inspect privileged activity and outcomes.", icon: "audit" }
            : null,
          hasGrantedCapability(capabilities, "admin.read.maintenance")
            ? {
                to: "/maintenance",
                label: dictionary.routes.maintenanceTitle,
                description: "Review maintenance runs and cache refresh history.",
                icon: "maintenance"
              }
            : null
        ].filter((link): link is NavigationLink => link !== null)
      }
    ].filter((section) => section.links.length > 0),
    [capabilities, dictionary, session.permissions]
  );

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

  useEffect(() => {
    setProfileMenuOpen(false);
    setSectionMenuOpen(null);
    setMobileMenuOpen(false);
  }, [location.pathname]);

  useEffect(() => {
    function handlePointerDown(event: MouseEvent) {
      const target = event.target;
      if (!(target instanceof Node)) {
        return;
      }

      if (!navbarRef.current?.contains(target)) {
        setProfileMenuOpen(false);
        setSectionMenuOpen(null);
        setMobileMenuOpen(false);
      }
    }

    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape") {
        setProfileMenuOpen(false);
        setSectionMenuOpen(null);
        setMobileMenuOpen(false);
      }
    }

    document.addEventListener("mousedown", handlePointerDown);
    document.addEventListener("keydown", handleKeyDown);

    return () => {
      document.removeEventListener("mousedown", handlePointerDown);
      document.removeEventListener("keydown", handleKeyDown);
    };
  }, []);

  const activeSection =
    navigationSections.find((section) =>
      section.links.some((link) => link.to === location.pathname || (link.to !== "/" && location.pathname.startsWith(`${link.to}/`)))
    ) ?? (location.pathname === "/profile" ? navigationSections.find((section) => section.id === "access") ?? navigationSections[0] : navigationSections[0]);

  return (
    <div className="app-shell">
      <div className="app-shell-grid">
        <nav ref={navbarRef} className="app-navbar">
          <div className="app-navbar-row">
            <div className="app-navbar-brand">
              <button
                type="button"
                className="app-navbar-mobile-toggle"
                onClick={() => {
                  setMobileMenuOpen((value) => !value);
                  setSectionMenuOpen(null);
                }}
                aria-expanded={mobileMenuOpen}
              >
                <svg viewBox="0 0 24 24" className="h-5 w-5 fill-none stroke-current" strokeWidth="1.8">
                  <path d="M4 7h16" />
                  <path d="M4 12h16" />
                  <path d="M4 17h16" />
                </svg>
                <span>Menu</span>
              </button>
              <NavLink to="/" end className="app-navbar-wordmark">
                <span className="app-navbar-wordmark-mark">BT</span>
                <span className="app-navbar-wordmark-text">BeeTracker</span>
              </NavLink>
              <div className="app-navbar-inline-nav">
                {navigationSections.map((section) => (
                  <div key={section.id} className="relative">
                    {section.links.length === 1 ? (
                      <NavLink
                        to={section.links[0].to}
                        end={section.links[0].to === "/"}
                        className={() => `app-navbar-link ${activeSection?.id === section.id ? "app-navbar-link-active" : "app-navbar-link-idle"}`}
                      >
                        <span className="app-navbar-link-icon">
                          <NavigationItemIcon icon={section.icon} />
                        </span>
                        <span>{section.label}</span>
                      </NavLink>
                    ) : (
                      <button
                        type="button"
                        onClick={() => setSectionMenuOpen((value) => value === section.id ? null : section.id)}
                        className={`app-navbar-link ${activeSection?.id === section.id ? "app-navbar-link-active" : "app-navbar-link-idle"}`}
                        aria-expanded={sectionMenuOpen === section.id}
                      >
                        <span className="app-navbar-link-icon">
                          <NavigationItemIcon icon={section.icon} />
                        </span>
                        <span>{section.label}</span>
                        <svg
                          viewBox="0 0 24 24"
                          className={`h-4 w-4 transition ${sectionMenuOpen === section.id ? "rotate-180" : ""}`}
                          fill="none"
                          stroke="currentColor"
                          strokeWidth="1.8"
                        >
                          <path d="m6 9 6 6 6-6" />
                        </svg>
                      </button>
                    )}
                    {section.links.length > 1 && sectionMenuOpen === section.id ? (
                      <div className="app-navbar-dropdown">
                        <div className="grid gap-1.5 p-2">
                          {section.links.map((link) => (
                            <NavLink
                              key={link.to}
                              to={link.to}
                              end={link.to === "/"}
                              className={({ isActive }) => `app-tools-menu-link ${isActive ? "app-tools-menu-link-active" : "app-tools-menu-link-idle"}`}
                            >
                              <span className="app-tools-menu-icon">
                                <NavigationItemIcon icon={link.icon} />
                              </span>
                              <span className="min-w-0">
                                <span className="block">{link.label}</span>
                                <span className="mt-0.5 block text-xs font-normal text-steel/80">{link.description}</span>
                              </span>
                            </NavLink>
                          ))}
                        </div>
                      </div>
                    ) : null}
                  </div>
                ))}
              </div>
            </div>
            <div className="app-navbar-actions">
              <div className="relative">
                <button
                  type="button"
                  onClick={() => setProfileMenuOpen((value) => !value)}
                  className="app-profile-trigger"
                  aria-expanded={profileMenuOpen}
                >
                  <div className="app-profile-trigger-hive" aria-hidden="true">
                    {Array.from({ length: 3 }, (_, index) => (
                      <span
                        key={`profile-trigger-hive-${index}`}
                        className="app-profile-trigger-hive-cell"
                        style={{ ["--profile-trigger-hive-index" as string]: index } as React.CSSProperties}
                      />
                    ))}
                  </div>
                  <div className="app-profile-avatar">
                    <span>{displayName.slice(0, 2).toUpperCase()}</span>
                  </div>
                  <div className="min-w-0 text-left">
                    <p className="truncate text-sm font-semibold text-white">{displayName}</p>
                    <p className="truncate text-xs text-slate-300">{role}</p>
                  </div>
                  <svg viewBox="0 0 24 24" className={`h-4 w-4 text-honey-300 transition ${profileMenuOpen ? "rotate-180" : ""}`} fill="none" stroke="currentColor" strokeWidth="1.8">
                    <path d="m6 9 6 6 6-6" />
                  </svg>
                </button>
                {profileMenuOpen ? (
                  <div className="app-profile-menu">
                    <div className="border-b border-slate-200/80 px-4 py-3">
                      <p className="text-xs font-semibold uppercase tracking-[0.18em] text-steel/55">{dictionary.common.signedInAs}</p>
                      <p className="mt-2 text-base font-bold text-ink">{displayName}</p>
                      <div className="mt-2 flex flex-wrap gap-2">
                        <StatusPill tone="good">{session.isAuthenticated ? dictionary.common.sessionActive : dictionary.common.sessionMissing}</StatusPill>
                        <span className="app-chip">{session.permissions.length} {dictionary.common.permissions}</span>
                      </div>
                    </div>
                    <div className="grid gap-2 p-3">
                      <Link to="/profile" className="app-profile-menu-link">Open profile</Link>
                      <Link to="/profile" className="app-profile-menu-link">Profile</Link>
                      <div className="app-subtle-panel !rounded-[18px] !px-3 !py-3">
                        <p className="text-[11px] font-semibold uppercase tracking-[0.16em] text-steel/55">Session</p>
                        <p className="mt-2 text-sm font-semibold text-ink">{role}</p>
                        <p className="mt-1 text-sm text-steel">{session.permissions.length} granted permissions</p>
                      </div>
                      <button type="button" onClick={() => void onSignin(true)} className="app-profile-menu-link text-left">
                        {dictionary.common.reauth}
                      </button>
                      <button type="button" onClick={() => void onSignout()} className="app-profile-menu-link text-left text-rose-600">
                        {dictionary.common.signOut}
                      </button>
                    </div>
                  </div>
                ) : null}
              </div>
            </div>
          </div>
          {mobileMenuOpen ? (
            <div className="app-navbar-mobile-panel">
              <div className="grid gap-3">
                {navigationSections.map((section) => (
                  <div key={`mobile-${section.id}`} className="rounded-[22px] border border-white/10 bg-white/5 p-2">
                    {section.links.length === 1 ? (
                      <NavLink
                        to={section.links[0].to}
                        end={section.links[0].to === "/"}
                        className={({ isActive }) => `app-navbar-mobile-link ${isActive ? "app-navbar-mobile-link-active" : "app-navbar-mobile-link-idle"}`}
                      >
                        <span className="app-navbar-link-icon">
                          <NavigationItemIcon icon={section.icon} />
                        </span>
                        <span>{section.label}</span>
                      </NavLink>
                    ) : (
                      <>
                        <button
                          type="button"
                          className={`app-navbar-mobile-link ${sectionMenuOpen === section.id || activeSection?.id === section.id ? "app-navbar-mobile-link-active" : "app-navbar-mobile-link-idle"}`}
                          onClick={() => setSectionMenuOpen((value) => value === section.id ? null : section.id)}
                          aria-expanded={sectionMenuOpen === section.id}
                        >
                          <span className="app-navbar-link-icon">
                            <NavigationItemIcon icon={section.icon} />
                          </span>
                          <span>{section.label}</span>
                          <svg
                            viewBox="0 0 24 24"
                            className={`ml-auto h-4 w-4 transition ${sectionMenuOpen === section.id ? "rotate-180" : ""}`}
                            fill="none"
                            stroke="currentColor"
                            strokeWidth="1.8"
                          >
                            <path d="m6 9 6 6 6-6" />
                          </svg>
                        </button>
                        {sectionMenuOpen === section.id ? (
                          <div className="mt-2 grid gap-2 px-2 pb-2">
                            {section.links.map((link) => (
                              <NavLink
                                key={`mobile-${link.to}`}
                                to={link.to}
                                end={link.to === "/"}
                                className={({ isActive }) => `app-tools-menu-link ${isActive ? "app-tools-menu-link-active" : "app-tools-menu-link-idle"}`}
                              >
                                <span className="app-tools-menu-icon">
                                  <NavigationItemIcon icon={link.icon} />
                                </span>
                                <span className="min-w-0">
                                  <span className="block">{link.label}</span>
                                  <span className="mt-0.5 block text-xs font-normal text-steel/80">{link.description}</span>
                                </span>
                              </NavLink>
                            ))}
                          </div>
                        ) : null}
                      </>
                    )}
                  </div>
                ))}
              </div>
            </div>
          ) : null}
        </nav>
        <main className="space-y-6 px-4 md:px-6">
          <section className="app-page-header-shell">
            <div className="app-page-header">
              <div className="app-page-header-hive" aria-hidden="true">
                {Array.from({ length: 10 }, (_, index) => (
                  <span
                    key={`hive-cell-${index}`}
                    className="app-page-header-hive-cell"
                    style={{ ["--hive-index" as string]: index } as React.CSSProperties}
                  />
                ))}
              </div>
              <div className="app-toolbar-body">
                <div className="max-w-3xl">
                  <div className="app-breadcrumb">
                    <span>{routeMeta.eyebrow}</span>
                    <span>/</span>
                    <span className="font-semibold text-ink">{routeMeta.title}</span>
                  </div>
                  <h2 className="mt-1.5 text-[1.75rem] font-extrabold tracking-tight text-ink md:text-[1.95rem]">{routeMeta.title}</h2>
                  <p className="mt-1.5 max-w-2xl text-[13px] leading-5 text-steel">{routeMeta.description}</p>
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

function SignInScreen({
  onSignin,
  error,
  autoStart = false
}: {
  onSignin: (fresh?: boolean) => Promise<void>;
  error: string | null;
  autoStart?: boolean;
}) {
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

  useEffect(() => {
    if (!autoStart) {
      return;
    }

    void onSignin(false);
  }, [autoStart, onSignin]);

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
            {autoStart && !error ? (
              <p className="mt-4 rounded-2xl bg-brand/10 px-4 py-3 text-sm text-brand">
                Redirecting to sign-in automatically…
              </p>
            ) : null}
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
    void onSignin(false);
  }, [onSignin, onUserChanged, user]);

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
        <Route
          path="/profile"
          element={
            <PermissionGate permissions={session.permissions} permission={permissionKeys.profileView}>
              <ProfilePage accessToken={accessToken} onReauthenticate={onSignin} permissions={session.permissions} />
            </PermissionGate>
          }
        />
        <Route
          path="/admin-users"
          element={
            <PermissionGate permissions={session.permissions} permission={permissionKeys.usersView}>
              <AdminUsersPage accessToken={accessToken} onReauthenticate={onSignin} permissions={session.permissions} />
            </PermissionGate>
          }
        />
        <Route
          path="/admin-users/new"
          element={
            <PermissionGate permissions={session.permissions} permission={permissionKeys.usersCreate}>
              <AdminUserEditorPage accessToken={accessToken} onReauthenticate={onSignin} permissions={session.permissions} />
            </PermissionGate>
          }
        />
        <Route
          path="/admin-users/:id/edit"
          element={
            <PermissionGate permissions={session.permissions} permission={permissionKeys.usersEdit}>
              <AdminUserEditorPage accessToken={accessToken} onReauthenticate={onSignin} permissions={session.permissions} />
            </PermissionGate>
          }
        />
        <Route
          path="/roles"
          element={
            <PermissionGate permissions={session.permissions} permission={permissionKeys.rolesView}>
              <RolesPage accessToken={accessToken} onReauthenticate={onSignin} permissions={session.permissions} />
            </PermissionGate>
          }
        />
        <Route
          path="/roles/new"
          element={
            <PermissionGate permissions={session.permissions} permission={permissionKeys.rolesCreate}>
              <RoleEditorPage accessToken={accessToken} onReauthenticate={onSignin} permissions={session.permissions} />
            </PermissionGate>
          }
        />
        <Route
          path="/roles/:id/edit"
          element={
            <PermissionGate permissions={session.permissions} permission={permissionKeys.rolesEdit}>
              <RoleEditorPage accessToken={accessToken} onReauthenticate={onSignin} permissions={session.permissions} />
            </PermissionGate>
          }
        />
        <Route
          path="/permission-groups"
          element={
            <PermissionGate permissions={session.permissions} permission={permissionKeys.permissionGroupsView}>
              <PermissionGroupsPage accessToken={accessToken} onReauthenticate={onSignin} permissions={session.permissions} />
            </PermissionGate>
          }
        />
        <Route
          path="/permission-groups/new"
          element={
            <PermissionGate permissions={session.permissions} permission={permissionKeys.permissionGroupsCreate}>
              <PermissionGroupEditorPage accessToken={accessToken} onReauthenticate={onSignin} permissions={session.permissions} />
            </PermissionGate>
          }
        />
        <Route
          path="/permission-groups/:id/edit"
          element={
            <PermissionGate permissions={session.permissions} permission={permissionKeys.permissionGroupsEdit}>
              <PermissionGroupEditorPage accessToken={accessToken} onReauthenticate={onSignin} permissions={session.permissions} />
            </PermissionGate>
          }
        />
        <Route
          path="/torrents"
          element={
            <CapabilityGate capabilities={capabilities} action="admin.read.torrents">
              <TorrentsPage accessToken={accessToken} onReauthenticate={onSignin} capabilities={capabilities} />
            </CapabilityGate>
          }
        />
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
          path="/passkeys/actions"
          element={
            <CapabilityAnyGate capabilities={capabilities} actions={["admin.revoke.passkey", "admin.rotate.passkey"]}>
              <PasskeyBatchActionPage accessToken={accessToken} onReauthenticate={onSignin} capabilities={capabilities} />
            </CapabilityAnyGate>
          }
        />
        <Route
          path="/passkeys/new"
          element={
            <CapabilityGate capabilities={capabilities} action="admin.write.passkey">
              <PasskeyEditorPage accessToken={accessToken} onReauthenticate={onSignin} capabilities={capabilities} />
            </CapabilityGate>
          }
        />
        <Route
          path="/passkeys/:id/edit"
          element={
            <CapabilityGate capabilities={capabilities} action="admin.write.passkey">
              <PasskeyEditorPage accessToken={accessToken} onReauthenticate={onSignin} capabilities={capabilities} />
            </CapabilityGate>
          }
        />
        <Route
          path="/permissions"
          element={
            <CapabilityGate capabilities={capabilities} action="admin.read.permissions">
              <TrackerAccessPage accessToken={accessToken} onReauthenticate={onSignin} capabilities={capabilities} />
            </CapabilityGate>
          }
        />
        <Route
          path="/permissions/new"
          element={
            <CapabilityGate capabilities={capabilities} action="admin.write.permissions">
              <TrackerAccessEditorPage accessToken={accessToken} onReauthenticate={onSignin} capabilities={capabilities} />
            </CapabilityGate>
          }
        />
        <Route
          path="/permissions/:id/edit"
          element={
            <CapabilityGate capabilities={capabilities} action="admin.write.permissions">
              <TrackerAccessEditorPage accessToken={accessToken} onReauthenticate={onSignin} capabilities={capabilities} />
            </CapabilityGate>
          }
        />
        <Route
          path="/permissions/bulk-edit"
          element={
            <CapabilityGate capabilities={capabilities} action="admin.write.permissions">
              <BulkTrackerAccessEditorPage accessToken={accessToken} onReauthenticate={onSignin} capabilities={capabilities} />
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
          path="/bans/new"
          element={
            <CapabilityGate capabilities={capabilities} action="admin.write.ban">
              <BanEditorPage accessToken={accessToken} onReauthenticate={onSignin} capabilities={capabilities} />
            </CapabilityGate>
          }
        />
        <Route
          path="/bans/:id/edit"
          element={
            <CapabilityGate capabilities={capabilities} action="admin.write.ban">
              <BanEditorPage accessToken={accessToken} onReauthenticate={onSignin} capabilities={capabilities} />
            </CapabilityGate>
          }
        />
        <Route
          path="/torrents/new"
          element={
            <CapabilityGate capabilities={capabilities} action="admin.write.torrent_policy">
              <TorrentPolicyEditorPage accessToken={accessToken} onReauthenticate={onSignin} />
            </CapabilityGate>
          }
        />
        <Route
          path="/torrents/:infoHash/edit"
          element={
            <CapabilityGate capabilities={capabilities} action="admin.write.torrent_policy">
              <TorrentPolicyEditorPage accessToken={accessToken} onReauthenticate={onSignin} />
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
        <Route
          path="/audit"
          element={
            <PermissionGate permissions={session.permissions} permission={permissionKeys.auditView}>
              <AuditPage accessToken={accessToken} onReauthenticate={onSignin} />
            </PermissionGate>
          }
        />
        <Route
          path="/maintenance"
          element={
            <CapabilityGate capabilities={capabilities} action="admin.read.maintenance">
              <MaintenancePage accessToken={accessToken} onReauthenticate={onSignin} />
            </CapabilityGate>
          }
        />
        <Route
          path="/governance"
          element={
            <PermissionGate permissions={session.permissions} permission="admin.system_settings.view">
              <GovernancePage accessToken={accessToken} onReauthenticate={onSignin} permissions={session.permissions} />
            </PermissionGate>
          }
        />
        <Route
          path="/abuse"
          element={
            <PermissionGate permissions={session.permissions} permission="admin.bans.view">
              <AbusePage accessToken={accessToken} onReauthenticate={onSignin} />
            </PermissionGate>
          }
        />
        <Route
          path="/cluster"
          element={
            <PermissionGate permissions={session.permissions} permission="admin.dashboard.view">
              <ClusterPage accessToken={accessToken} onReauthenticate={onSignin} />
            </PermissionGate>
          }
        />
        <Route
          path="/swarms"
          element={
            <CapabilityGate capabilities={capabilities} action="admin.read.swarms">
              <SwarmsPage accessToken={accessToken} onReauthenticate={onSignin} capabilities={capabilities} />
            </CapabilityGate>
          }
        />
        <Route
          path="/notifications"
          element={
            <PermissionGate permissions={session.permissions} permission={permissionKeys.auditView}>
              <NotificationsPage accessToken={accessToken} onReauthenticate={onSignin} permissions={session.permissions} />
            </PermissionGate>
          }
        />
        <Route
          path="/tracker-node"
          element={<Navigate to="/tracker-nodes" replace />}
        />
        <Route
          path="/tracker-nodes"
          element={
            <PermissionGate permissions={session.permissions} permission="admin.system_settings.view">
              <TrackerNodesPage accessToken={accessToken} onReauthenticate={onSignin} permissions={session.permissions} />
            </PermissionGate>
          }
        />
        <Route
          path="/tracker-nodes/new"
          element={
            <PermissionGate permissions={session.permissions} permission="admin.system_settings.view">
              <TrackerNodeConfigPage accessToken={accessToken} onReauthenticate={onSignin} permissions={session.permissions} />
            </PermissionGate>
          }
        />
        <Route
          path="/tracker-nodes/:nodeKey/edit"
          element={
            <PermissionGate permissions={session.permissions} permission="admin.system_settings.view">
              <TrackerNodeConfigPage accessToken={accessToken} onReauthenticate={onSignin} permissions={session.permissions} />
            </PermissionGate>
          }
        />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </Shell>
  );
}

export default function App() {
  const { dictionary } = useI18n();
  const { manager, user, setUser, isBootstrapping, bootError, signin, signout } = useAdminOidc();
  const location = useLocation();
  const autoStartSignin =
    typeof window !== "undefined" &&
    window.sessionStorage.getItem(adminSignedOutStorageKey) !== "1";

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

  if (location.pathname.startsWith("/self-service")) {
    return (
      <Routes>
        <Route path="/self-service/register" element={<SelfServiceRegisterPage />} />
        <Route path="/self-service/activate" element={<SelfServiceActivatePage />} />
        <Route path="/self-service/forgot-password" element={<SelfServiceForgotPasswordPage />} />
        <Route path="/self-service/reset-password" element={<SelfServiceResetPasswordPage />} />
        <Route path="*" element={<Navigate to="/self-service/register" replace />} />
      </Routes>
    );
  }

  if (!user) {
    return <SignInScreen onSignin={signin} error={null} autoStart={autoStartSignin} />;
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
