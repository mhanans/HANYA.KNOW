import { ChangeEvent, DragEvent, FormEvent, SyntheticEvent, useCallback, useEffect, useMemo, useState } from 'react';
import { useRouter } from 'next/router';
import {
  Alert,
  Box,
  Button,
  CircularProgress,
  IconButton,
  MenuItem,
  Paper,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Tab,
  Tabs,
  TextField,
  Typography,
  Divider,
} from '@mui/material';
import DeleteIcon from '@mui/icons-material/Delete';
import AddIcon from '@mui/icons-material/Add';
import SyncIcon from '@mui/icons-material/Sync';
import DragIndicatorIcon from '@mui/icons-material/DragIndicator';
import { apiFetch } from '../../../lib/api';
import Autocomplete from '@mui/material/Autocomplete';
import { CostEstimationConfiguration } from '../../../types/cost-estimation';

interface PresalesRole {
  roleName: string;
  expectedLevel: string;
  costPerDay: number;
  monthlySalary?: number;
  ratePerDay?: number;
}

interface PresalesActivity {
  activityName: string;
  displayOrder: number;
}

interface ItemActivityMapping {
  sectionName: string;
  itemName: string;
  activityName: string;
  displayOrder: number;
}

interface TemplateTaskReference {
  sectionName: string;
  sectionOrder: number;
  itemName: string;
  itemOrder: number;
}

interface EstimationColumnRoleMapping {
  estimationColumn: string;
  roleName: string;
}

interface TeamTypeRoleForm {
  id?: number;
  teamTypeId?: number;
  roleName: string;
  headcount: number;
  clientKey?: string;
}

interface TeamTypeForm {
  id?: number;
  name: string;
  minManDays: number;
  maxManDays: number;
  roles: TeamTypeRoleForm[];
  clientKey?: string;
}

interface TimelineEstimatorReferenceResponse {
  id: number;
  projectScale: string;
  totalDurationDays: number;
  phaseDurations: Record<string, number>;
  resourceAllocation: Record<string, number>;
}

interface TimelineEstimatorReference extends TimelineEstimatorReferenceResponse {
  phaseDurationsText: string;
  resourceAllocationText: string;
}

interface TimelineEstimatorReferenceDraft {
  projectScale: string;
  totalDurationDays: number;
  phaseDurationsText: string;
  resourceAllocationText: string;
}

const transformReferenceResponse = (item: TimelineEstimatorReferenceResponse): TimelineEstimatorReference => ({
  ...item,
  phaseDurationsText: stringifyObject(item.phaseDurations ?? {}),
  resourceAllocationText: stringifyObject(item.resourceAllocation ?? {}),
});

interface PresalesConfiguration {
  roles: PresalesRole[];
  activities: PresalesActivity[];
  itemActivities: ItemActivityMapping[];
  estimationColumnRoles: EstimationColumnRoleMapping[];
  teamTypes: TeamTypeForm[];
}

const emptyConfig: PresalesConfiguration = {
  roles: [],
  activities: [],
  itemActivities: [],
  estimationColumnRoles: [],
  teamTypes: [],
};

const createClientKey = () => Math.random().toString(36).slice(2, 11);

const stringifyObject = (value: Record<string, number>) => JSON.stringify(value, null, 2);

const createReferenceDraft = (): TimelineEstimatorReferenceDraft => ({
  projectScale: 'Medium',
  totalDurationDays: 30,
  phaseDurationsText: stringifyObject({ Discovery: 5, Execution: 20, Stabilization: 5 }),
  resourceAllocationText: stringifyObject({ 'Delivery Lead': 1, 'Solution Architect': 1, 'Engineer': 4 }),
});

const parseNumericObject = (
  text: string,
  { allowFloat, label }: { allowFloat: boolean; label: string }
): Record<string, number> => {
  let parsed: unknown;
  try {
    parsed = JSON.parse(text);
  } catch (err) {
    throw new Error(`${label} must be a valid JSON object.`);
  }

  if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) {
    throw new Error(`${label} must be a JSON object.`);
  }

  const result: Record<string, number> = {};
  for (const [key, rawValue] of Object.entries(parsed)) {
    const trimmedKey = key.trim();
    if (!trimmedKey) {
      throw new Error(`${label} cannot contain empty keys.`);
    }
    const numberValue = Number(rawValue);
    if (!Number.isFinite(numberValue) || numberValue <= 0) {
      throw new Error(`${label} must contain positive numbers. Invalid value for "${trimmedKey}".`);
    }
    if (!allowFloat && !Number.isInteger(numberValue)) {
      throw new Error(`${label} must use whole numbers. "${trimmedKey}" has a non-integer value.`);
    }
    result[trimmedKey] = allowFloat ? numberValue : Math.round(numberValue);
  }

  if (Object.keys(result).length === 0) {
    throw new Error(`${label} must contain at least one entry.`);
  }

  return result;
};

const normalizeOrderValue = (value: unknown, fallback = Number.MAX_SAFE_INTEGER) => {
  if (typeof value === 'number' && Number.isFinite(value)) {
    return value;
  }
  if (typeof value === 'string') {
    const parsed = parseInt(value, 10);
    if (Number.isFinite(parsed)) {
      return parsed;
    }
  }
  return fallback;
};

const sortByOrder = <T extends { displayOrder?: number | null }>(items: T[]) =>
  [...items].sort(
    (a, b) =>
      normalizeOrderValue((a as { displayOrder?: unknown }).displayOrder) -
      normalizeOrderValue((b as { displayOrder?: unknown }).displayOrder)
  );

const resequenceList = <T extends { displayOrder?: number | null }>(items: T[]) =>
  items.map(item => ({ ...item, displayOrder: normalizeOrderValue(item.displayOrder, 0) }))
    .map((item, index) => ({ ...item, displayOrder: index + 1 }));

const sanitizeActivitiesFromResponse = (source: unknown): PresalesActivity[] => {
  if (!Array.isArray(source)) {
    return [];
  }

  const normalized = source
    .map(item => {
      if (!item || typeof item !== 'object') {
        return null;
      }
      const activity = item as Partial<PresalesActivity> & { displayOrder?: unknown };
      return {
        activityName: typeof activity.activityName === 'string' ? activity.activityName : '',
        displayOrder: normalizeOrderValue(activity.displayOrder),
      } satisfies PresalesActivity;
    })
    .filter((entry): entry is PresalesActivity => Boolean(entry));

  return resequenceList(sortByOrder(normalized));
};

const sanitizeItemActivitiesFromResponse = (source: unknown): ItemActivityMapping[] => {
  if (!Array.isArray(source)) {
    return [];
  }

  const normalized = source
    .map(item => {
      if (!item || typeof item !== 'object') {
        return null;
      }
      const mapping = item as Partial<ItemActivityMapping> & { displayOrder?: unknown };
      return {
        sectionName: typeof mapping.sectionName === 'string' ? mapping.sectionName : '',
        itemName: typeof mapping.itemName === 'string' ? mapping.itemName : '',
        activityName: typeof mapping.activityName === 'string' ? mapping.activityName : '',
        displayOrder: normalizeOrderValue(mapping.displayOrder),
      } satisfies ItemActivityMapping;
    })
    .filter((entry): entry is ItemActivityMapping => Boolean(entry));

  return resequenceList(sortByOrder(normalized));
};

const reorderList = <T,>(items: T[], fromIndex: number, toIndex: number) => {
  if (fromIndex === toIndex) {
    return [...items];
  }

  const result = [...items];
  if (fromIndex < 0 || fromIndex >= result.length) {
    return result;
  }

  let clampedToIndex = Math.max(0, Math.min(toIndex, result.length));
  const [moved] = result.splice(fromIndex, 1);
  if (moved === undefined) {
    return [...items];
  }

  if (fromIndex < clampedToIndex) {
    clampedToIndex -= 1;
  }

  if (clampedToIndex < 0) {
    clampedToIndex = 0;
  }

  if (clampedToIndex > result.length) {
    clampedToIndex = result.length;
  }

  result.splice(clampedToIndex, 0, moved);
  return result;
};

const buildPresalesPayload = (config: PresalesConfiguration): PresalesConfiguration => {
  const roles = config.roles.map(role => ({
    roleName: typeof role.roleName === 'string' ? role.roleName.trim() : '',
    expectedLevel: typeof role.expectedLevel === 'string' ? role.expectedLevel.trim() : '',
    costPerDay: Number.isFinite(role.costPerDay) ? role.costPerDay : 0,
  }));

  const activities = config.activities
    .map(activity => ({
      activityName: typeof activity.activityName === 'string' ? activity.activityName.trim() : '',
    }))
    .map((activity, index) => ({ ...activity, displayOrder: index + 1 }));

  const itemActivities = config.itemActivities
    .map(mapping => ({
      sectionName: typeof mapping.sectionName === 'string' ? mapping.sectionName.trim() : '',
      itemName: typeof mapping.itemName === 'string' ? mapping.itemName.trim() : '',
      activityName: typeof mapping.activityName === 'string' ? mapping.activityName.trim() : '',
    }))
    .map((mapping, index) => ({ ...mapping, displayOrder: index + 1 }));

  const estimationColumnRoles = config.estimationColumnRoles.map(mapping => ({
    estimationColumn: typeof mapping.estimationColumn === 'string' ? mapping.estimationColumn.trim() : '',
    roleName: typeof mapping.roleName === 'string' ? mapping.roleName.trim() : '',
  }));

  const teamTypes = config.teamTypes
    .map(teamType => {
      const rawMin = Number(teamType.minManDays);
      const rawMax = Number(teamType.maxManDays);
      let minManDays = Number.isFinite(rawMin) ? Math.max(0, Math.round(rawMin)) : 0;
      let maxManDays = Number.isFinite(rawMax) ? Math.max(0, Math.round(rawMax)) : 0;
      if (maxManDays > 0 && minManDays > maxManDays) {
        const temp = minManDays;
        minManDays = maxManDays;
        maxManDays = temp;
      }

      const roles = (teamType.roles ?? [])
        .map(role => ({
          id: role.id,
          teamTypeId: role.teamTypeId,
          roleName: typeof role.roleName === 'string' ? role.roleName.trim() : '',
          headcount: Number.isFinite(role.headcount) ? Math.max(0, Number(role.headcount)) : 0,
        }))
        .filter(role => role.roleName);

      return {
        id: teamType.id,
        name: typeof teamType.name === 'string' ? teamType.name.trim() : '',
        minManDays,
        maxManDays,
        roles,
      };
    })
    .filter(teamType => teamType.name);

  return {
    roles,
    activities,
    itemActivities,
    estimationColumnRoles,
    teamTypes,
  };
};

const transformPresalesResponse = (
  presalesData: any,
  normalizedCost: CostEstimationConfiguration | null
): PresalesConfiguration => {
  const activeRateCardKey = resolveDefaultRateCardKey(normalizedCost ?? null);
  const activeRateCard = normalizedCost?.rateCards?.[activeRateCardKey];

  const rolesSource = Array.isArray(presalesData?.roles) ? presalesData.roles : [];
  const roles = rolesSource.map((role: Partial<PresalesRole>) => {
    const baseRole: PresalesRole = {
      roleName: typeof role.roleName === 'string' ? role.roleName : '',
      expectedLevel: typeof role.expectedLevel === 'string' ? role.expectedLevel : '',
      costPerDay:
        typeof role.costPerDay === 'number' && Number.isFinite(role.costPerDay) ? role.costPerDay : 0,
      monthlySalary: role.monthlySalary,
      ratePerDay: role.ratePerDay,
    };
    const label = buildRoleLabelFromRole(baseRole);
    const costKeys = enumerateCostKeysForRole(baseRole);
    const monthlySalary =
      label && normalizedCost
        ? findFirstDefinedValue(costKeys, normalizedCost.roleMonthlySalaries) ?? baseRole.monthlySalary ?? 0
        : baseRole.monthlySalary ?? 0;
    const ratePerDay =
      label && activeRateCard
        ? findFirstDefinedValue(costKeys, activeRateCard.roleRates) ?? baseRole.ratePerDay ?? 0
        : baseRole.ratePerDay ?? 0;
    return {
      ...baseRole,
      monthlySalary,
      ratePerDay,
    };
  });

  const estimationColumnRolesSource = Array.isArray(presalesData?.estimationColumnRoles)
    ? presalesData.estimationColumnRoles
    : [];
  const estimationColumnRoles = estimationColumnRolesSource.map(
    (mapping: Partial<EstimationColumnRoleMapping>) => ({
      estimationColumn: typeof mapping.estimationColumn === 'string' ? mapping.estimationColumn : '',
      roleName: typeof mapping.roleName === 'string' ? mapping.roleName : '',
    })
  );

  const teamTypesSource = Array.isArray(presalesData?.teamTypes) ? presalesData.teamTypes : [];
  const teamTypes = teamTypesSource.map((teamType: Partial<TeamTypeForm> & { roles?: Partial<TeamTypeRoleForm>[] }) => {
    const roles = Array.isArray(teamType.roles)
      ? teamType.roles.map(role => ({
          id: typeof role?.id === 'number' ? role.id : undefined,
          teamTypeId: typeof role?.teamTypeId === 'number' ? role.teamTypeId : undefined,
          roleName: typeof role?.roleName === 'string' ? role.roleName : '',
          headcount:
            typeof role?.headcount === 'number' && Number.isFinite(role.headcount) ? Number(role.headcount) : 0,
          clientKey: createClientKey(),
        }))
      : [];

    return {
      id: typeof teamType?.id === 'number' ? teamType.id : undefined,
      name: typeof teamType?.name === 'string' ? teamType.name : '',
      minManDays:
        typeof teamType?.minManDays === 'number' && Number.isFinite(teamType.minManDays)
          ? Number(teamType.minManDays)
          : 0,
      maxManDays:
        typeof teamType?.maxManDays === 'number' && Number.isFinite(teamType.maxManDays)
          ? Number(teamType.maxManDays)
          : 0,
      roles,
      clientKey: createClientKey(),
    } satisfies TeamTypeForm;
  });

  return {
    roles,
    activities: sanitizeActivitiesFromResponse(presalesData?.activities),
    itemActivities: sanitizeItemActivitiesFromResponse(presalesData?.itemActivities),
    estimationColumnRoles,
    teamTypes,
  };
};

type TabKey =
  | 'roles'
  | 'activities'
  | 'items'
  | 'columns'
  | 'timelineTeamTypes'
  | 'timelineReferences';
const TAB_KEYS: TabKey[] = [
  'roles',
  'activities',
  'items',
  'columns',
  'timelineTeamTypes',
  'timelineReferences',
];

const ROLE_LABEL_SEPARATOR = ' – ';
const ROLE_VALUE_SEPARATOR = '::';

const normalizeRolePart = (value?: string | null) => (value ?? '').trim();

const buildRoleLabel = (roleName?: string | null, expectedLevel?: string | null) => {
  const name = normalizeRolePart(roleName);
  if (!name) return '';
  const level = normalizeRolePart(expectedLevel);
  return level ? `${name}${ROLE_LABEL_SEPARATOR}${level}` : name;
};

const enumerateCostKeys = (roleName?: string | null, expectedLevel?: string | null) => {
  const name = normalizeRolePart(roleName);
  if (!name) return [] as string[];
  const level = normalizeRolePart(expectedLevel);
  const keys = new Set<string>();
  if (level) {
    keys.add(`${name}${ROLE_LABEL_SEPARATOR}${level}`);
    keys.add(`${name} ${level}`);
    keys.add(`${name}${ROLE_VALUE_SEPARATOR}${level}`);
  }
  keys.add(name);
  return Array.from(keys);
};

const buildRoleLabelFromRole = (role: Pick<PresalesRole, 'roleName' | 'expectedLevel'>) =>
  buildRoleLabel(role.roleName, role.expectedLevel);

const enumerateCostKeysForRole = (role: Pick<PresalesRole, 'roleName' | 'expectedLevel'>) =>
  enumerateCostKeys(role.roleName, role.expectedLevel);

const findFirstDefinedValue = (keys: string[], source?: Record<string, number>) => {
  for (const key of keys) {
    if (!key) continue;
    const value = source?.[key];
    if (typeof value === 'number') {
      return value;
    }
  }
  return undefined;
};

const toNumber = (value: string, fallback = 0) => {
  if (!value) return fallback;
  const sanitized = value.replace(/[^0-9,.-]/g, '');
  if (!sanitized) return fallback;
  const hasComma = sanitized.includes(',');
  let normalized = sanitized;
  if (hasComma) {
    normalized = sanitized.replace(/\./g, '').replace(/,/g, '.');
  } else {
    const thousandSeparated = /^\d{1,3}(\.\d{3})+$/.test(sanitized);
    if (thousandSeparated) {
      normalized = sanitized.replace(/\./g, '');
    }
  }
  const parsed = parseFloat(normalized);
  return Number.isFinite(parsed) ? parsed : fallback;
};

const formatIDR = (value?: number | null) => {
  if (value === null || value === undefined) return '';
  return new Intl.NumberFormat('id-ID', { minimumFractionDigits: 0, maximumFractionDigits: 0 }).format(value);
};

const resolveDefaultRateCardKey = (config?: CostEstimationConfiguration | null) => {
  if (!config) return 'default';
  const keys = Object.keys(config.rateCards ?? {});
  return config.defaultRateCardKey || keys[0] || 'default';
};

const prepareRateCard = (config: CostEstimationConfiguration, key: string) => {
  const rateCards = { ...config.rateCards };
  const existing = rateCards[key];
  const card = existing
    ? { ...existing, roleRates: { ...existing.roleRates } }
    : { displayName: key, roleRates: {} as Record<string, number> };
  rateCards[key] = card;
  return { rateCards, card };
};

export default function PresalesConfigurationPage() {
  const router = useRouter();
  const [config, setConfig] = useState<PresalesConfiguration>(emptyConfig);
  const [costConfig, setCostConfig] = useState<CostEstimationConfiguration | null>(null);
  const [availableTasks, setAvailableTasks] = useState<TemplateTaskReference[]>([]);
  const [availableEstimationColumns, setAvailableEstimationColumns] = useState<string[]>([]);
  const [activeTab, setActiveTab] = useState<TabKey>('roles');
  const [activityDragIndex, setActivityDragIndex] = useState<number | null>(null);
  const [itemActivityDragIndex, setItemActivityDragIndex] = useState<number | null>(null);
  const [syncingItems, setSyncingItems] = useState(false);
  const [syncingEstimationColumns, setSyncingEstimationColumns] = useState(false);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);
  const [timelineReferences, setTimelineReferences] = useState<TimelineEstimatorReference[]>([]);
  const [timelineReferenceDraft, setTimelineReferenceDraft] = useState<TimelineEstimatorReferenceDraft>(
    createReferenceDraft()
  );
  const [timelineReferencesLoading, setTimelineReferencesLoading] = useState(false);
  const [timelineReferencesError, setTimelineReferencesError] = useState<string | null>(null);
  const [timelineReferencesPermissionDenied, setTimelineReferencesPermissionDenied] = useState(false);
  const [timelineReferenceSavingId, setTimelineReferenceSavingId] = useState<number | 'new' | null>(null);

  useEffect(() => {
    if (!router.isReady) return;
    const tabParam = router.query.tab;
    const value = Array.isArray(tabParam) ? tabParam[0] : tabParam;
    if (value) {
      const normalized = value === 'timeline' ? 'timelineTeamTypes' : value;
      if (TAB_KEYS.includes(normalized as TabKey)) {
        setActiveTab(normalized as TabKey);
      }
    }
  }, [router.isReady, router.query.tab]);

  const fetchReferenceData = useCallback(async () => {
    let items: TemplateTaskReference[] = [];
    let estimationColumns: string[] = [];

    try {
      const [itemsRes, columnsRes] = await Promise.all([
        apiFetch('/api/presales/config/items'),
        apiFetch('/api/presales/config/estimation-columns'),
      ]);

      if (itemsRes.ok) {
        try {
          const data = await itemsRes.json();
          if (Array.isArray(data)) {
            items = data
              .map(item => {
                if (!item || typeof item !== 'object') return null;
                const sectionName = typeof item.sectionName === 'string' ? item.sectionName.trim() : '';
                const itemName = typeof item.itemName === 'string' ? item.itemName.trim() : '';
                const rawSectionOrderValue = (item as { sectionOrder?: unknown }).sectionOrder;
                const sectionOrder = typeof rawSectionOrderValue === 'number' && Number.isFinite(rawSectionOrderValue)
                  ? rawSectionOrderValue
                  : 0;
                const rawItemOrderValue = (item as { itemOrder?: unknown }).itemOrder;
                const itemOrder = typeof rawItemOrderValue === 'number' && Number.isFinite(rawItemOrderValue)
                  ? rawItemOrderValue
                  : 0;
                if (!sectionName) return null;
                return {
                  sectionName,
                  sectionOrder,
                  itemName,
                  itemOrder,
                } satisfies TemplateTaskReference;
              })
              .filter((entry): entry is TemplateTaskReference => Boolean(entry));
          } else {
            items = [];
          }
        } catch (err) {
          console.warn('Failed to parse item list', err);
          items = [];
        }
      } else {
        console.warn(`Failed to load item list (${itemsRes.status})`);
        items = [];
      }

      if (columnsRes.ok) {
        try {
          const data = await columnsRes.json();
          estimationColumns = Array.isArray(data) ? data : [];
        } catch (err) {
          console.warn('Failed to parse estimation column list', err);
          estimationColumns = [];
        }
      } else {
        console.warn(`Failed to load estimation column list (${columnsRes.status})`);
        estimationColumns = [];
      }
    } catch (err) {
      console.warn('Failed to load reference data', err);
    }

    setAvailableTasks(items);
    setAvailableEstimationColumns(estimationColumns);
    return { items, estimationColumns };
  }, []);

  const resetTimelineReferenceDraft = useCallback(() => {
    setTimelineReferenceDraft(createReferenceDraft());
  }, []);

  const loadTimelineReferences = useCallback(async () => {
    setTimelineReferencesLoading(true);
    setTimelineReferencesError(null);
    try {
      const res = await apiFetch('/api/TimelineEstimationReferences');
      if (res.status === 403 || res.status === 401) {
        setTimelineReferences([]);
        setTimelineReferencesPermissionDenied(true);
        return;
      }
      if (!res.ok) {
        throw new Error(`Failed to load timeline estimator references (${res.status})`);
      }
      const data = await res.json();
      const items: TimelineEstimatorReferenceResponse[] = Array.isArray(data) ? data : [];
      setTimelineReferences(items.map(transformReferenceResponse));
      setTimelineReferencesPermissionDenied(false);
    } catch (err) {
      setTimelineReferencesError(
        err instanceof Error ? err.message : 'Failed to load timeline estimator references'
      );
    } finally {
      setTimelineReferencesLoading(false);
    }
  }, []);

  const loadConfig = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [presalesRes, costRes] = await Promise.all([
        apiFetch('/api/presales/config'),
        apiFetch('/api/cost-estimations/configuration'),
      ]);
      if (!presalesRes.ok) {
        throw new Error(`Failed to load configuration (${presalesRes.status})`);
      }

      const presalesData = await presalesRes.json();
      let costData: CostEstimationConfiguration | null = null;
      if (costRes.ok) {
        try {
          const json = await costRes.json();
          costData = json ?? null;
        } catch (err) {
          console.warn('Failed to parse cost configuration', err);
        }
      }

      let normalizedCost: CostEstimationConfiguration | null = null;
      if (costData) {
        const key = resolveDefaultRateCardKey(costData);
        const { rateCards } = prepareRateCard(costData, key);
        normalizedCost = {
          ...costData,
          defaultRateCardKey: key,
          rateCards,
          roleMonthlySalaries: { ...costData.roleMonthlySalaries },
        };
      }
      setConfig(transformPresalesResponse(presalesData, normalizedCost));
      setCostConfig(normalizedCost);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load configuration');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    loadConfig();
  }, [loadConfig]);

  useEffect(() => {
    fetchReferenceData();
  }, [fetchReferenceData]);

  useEffect(() => {
    loadTimelineReferences();
  }, [loadTimelineReferences]);

  const handleTabChange = useCallback(
    (_: SyntheticEvent, newValue: string) => {
      if (!TAB_KEYS.includes(newValue as TabKey)) {
        return;
      }
      setActiveTab(newValue as TabKey);
      const nextQuery = { ...router.query };
      if (newValue === 'roles') {
        delete nextQuery.tab;
      } else {
        nextQuery.tab = newValue;
      }
      router.replace({ pathname: router.pathname, query: nextQuery }, undefined, { shallow: true });
    },
    [router]
  );

  const handleActivityDragStart = useCallback((event: DragEvent<HTMLElement>, index: number) => {
    setActivityDragIndex(index);
    if (event.dataTransfer) {
      event.dataTransfer.effectAllowed = 'move';
      event.dataTransfer.setData('text/plain', `${index}`);
    }
  }, []);

  const handleActivityDragEnd = useCallback(() => {
    setActivityDragIndex(null);
  }, []);

  const handleActivityDragOver = useCallback((event: DragEvent<HTMLTableRowElement>) => {
    event.preventDefault();
    if (event.dataTransfer) {
      event.dataTransfer.dropEffect = 'move';
    }
  }, []);

  const updateTimelineReferenceState = useCallback((id: number, patch: Partial<TimelineEstimatorReference>) => {
    setTimelineReferences(prev => prev.map(item => (item.id === id ? { ...item, ...patch } : item)));
  }, []);

  const handleCreateTimelineReference = useCallback(
    async (event: FormEvent<HTMLFormElement>) => {
      event.preventDefault();
      if (timelineReferencesPermissionDenied) {
        setTimelineReferencesError('You do not have permission to manage timeline estimator references.');
        return;
      }

      const projectScale = timelineReferenceDraft.projectScale?.trim();
      const totalDuration = Number(timelineReferenceDraft.totalDurationDays);
      try {
        if (!projectScale) {
          throw new Error('Project scale is required.');
        }
        if (!Number.isFinite(totalDuration) || totalDuration <= 0) {
          throw new Error('Total duration must be greater than zero.');
        }
        const phaseDurations = parseNumericObject(timelineReferenceDraft.phaseDurationsText, {
          allowFloat: false,
          label: 'Phase durations',
        });
        const resourceAllocation = parseNumericObject(timelineReferenceDraft.resourceAllocationText, {
          allowFloat: true,
          label: 'Resource allocation',
        });

        setTimelineReferenceSavingId('new');
        setTimelineReferencesError(null);
        const response = await apiFetch('/api/TimelineEstimationReferences', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            projectScale,
            totalDurationDays: Math.round(totalDuration),
            phaseDurations,
            resourceAllocation,
          }),
        });
        if (!response.ok) {
          const text = await response.text();
          throw new Error(text || 'Failed to create timeline estimator reference.');
        }
        setSuccessMessage('Timeline estimator reference added.');
        resetTimelineReferenceDraft();
        await loadTimelineReferences();
      } catch (err) {
        setTimelineReferencesError(
          err instanceof Error ? err.message : 'Failed to create timeline estimator reference.'
        );
      } finally {
        setTimelineReferenceSavingId(null);
      }
    },
    [
      timelineReferenceDraft,
      timelineReferencesPermissionDenied,
      loadTimelineReferences,
      resetTimelineReferenceDraft,
    ]
  );

  const handleSaveTimelineReference = useCallback(
    async (referenceId: number) => {
      if (timelineReferencesPermissionDenied) {
        setTimelineReferencesError('You do not have permission to manage timeline estimator references.');
        return;
      }
      const target = timelineReferences.find(item => item.id === referenceId);
      if (!target) {
        return;
      }

      const projectScale = target.projectScale?.trim();
      const totalDuration = Number(target.totalDurationDays);
      try {
        if (!projectScale) {
          throw new Error('Project scale is required.');
        }
        if (!Number.isFinite(totalDuration) || totalDuration <= 0) {
          throw new Error('Total duration must be greater than zero.');
        }
        const phaseDurations = parseNumericObject(target.phaseDurationsText, {
          allowFloat: false,
          label: 'Phase durations',
        });
        const resourceAllocation = parseNumericObject(target.resourceAllocationText, {
          allowFloat: true,
          label: 'Resource allocation',
        });

        setTimelineReferenceSavingId(referenceId);
        setTimelineReferencesError(null);
        const response = await apiFetch(`/api/TimelineEstimationReferences/${referenceId}`, {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            projectScale,
            totalDurationDays: Math.round(totalDuration),
            phaseDurations,
            resourceAllocation,
          }),
        });
        if (!response.ok) {
          const text = await response.text();
          throw new Error(text || 'Failed to update timeline estimator reference.');
        }
        setSuccessMessage('Timeline estimator reference updated.');
        await loadTimelineReferences();
      } catch (err) {
        setTimelineReferencesError(
          err instanceof Error ? err.message : 'Failed to update timeline estimator reference.'
        );
      } finally {
        setTimelineReferenceSavingId(null);
      }
    },
    [timelineReferences, timelineReferencesPermissionDenied, loadTimelineReferences]
  );

  const handleDeleteTimelineReference = useCallback(
    async (referenceId: number) => {
      if (timelineReferencesPermissionDenied) {
        setTimelineReferencesError('You do not have permission to manage timeline estimator references.');
        return;
      }
      setTimelineReferenceSavingId(referenceId);
      setTimelineReferencesError(null);
      try {
        const response = await apiFetch(`/api/TimelineEstimationReferences/${referenceId}`, {
          method: 'DELETE',
        });
        if (!response.ok) {
          const text = await response.text();
          throw new Error(text || 'Failed to delete timeline estimator reference.');
        }
        setSuccessMessage('Timeline estimator reference deleted.');
        await loadTimelineReferences();
      } catch (err) {
        setTimelineReferencesError(
          err instanceof Error ? err.message : 'Failed to delete timeline estimator reference.'
        );
      } finally {
        setTimelineReferenceSavingId(null);
      }
    },
    [timelineReferencesPermissionDenied, loadTimelineReferences]
  );

  const handleActivityDrop = useCallback(
    (event: DragEvent<HTMLTableRowElement>, index: number) => {
      event.preventDefault();
      if (event.dataTransfer) {
        event.dataTransfer.dropEffect = 'move';
      }
      if (activityDragIndex === null) {
        return;
      }

      const bounds = event.currentTarget.getBoundingClientRect();
      const offset = event.clientY - bounds.top;
      const shouldPlaceAfter = offset > bounds.height / 2;
      const desiredIndex = shouldPlaceAfter ? index + 1 : index;

      setConfig(prev => {
        const fromIndex = activityDragIndex;
        if (fromIndex < 0 || fromIndex >= prev.activities.length) {
          return prev;
        }

        let targetIndex = Math.max(0, Math.min(desiredIndex, prev.activities.length));
        if (fromIndex < targetIndex) {
          targetIndex -= 1;
        }
        if (targetIndex === fromIndex) {
          return prev;
        }

        const reordered = reorderList(prev.activities, fromIndex, desiredIndex);
        const resequenced = resequenceList(reordered);
        return { ...prev, activities: resequenced };
      });
      setActivityDragIndex(null);
    },
    [activityDragIndex]
  );

  const handleItemActivityDragStart = useCallback((event: DragEvent<HTMLElement>, index: number) => {
    setItemActivityDragIndex(index);
    if (event.dataTransfer) {
      event.dataTransfer.effectAllowed = 'move';
      event.dataTransfer.setData('text/plain', `${index}`);
    }
  }, []);

  const handleItemActivityDragEnd = useCallback(() => {
    setItemActivityDragIndex(null);
  }, []);

  const handleItemActivityDragOver = useCallback((event: DragEvent<HTMLTableRowElement>) => {
    event.preventDefault();
    if (event.dataTransfer) {
      event.dataTransfer.dropEffect = 'move';
    }
  }, []);

  const handleItemActivityDrop = useCallback(
    (event: DragEvent<HTMLTableRowElement>, index: number) => {
      event.preventDefault();
      if (event.dataTransfer) {
        event.dataTransfer.dropEffect = 'move';
      }
      if (itemActivityDragIndex === null) {
        return;
      }

      const bounds = event.currentTarget.getBoundingClientRect();
      const offset = event.clientY - bounds.top;
      const shouldPlaceAfter = offset > bounds.height / 2;
      const desiredIndex = shouldPlaceAfter ? index + 1 : index;

      setConfig(prev => {
        const fromIndex = itemActivityDragIndex;
        if (fromIndex < 0 || fromIndex >= prev.itemActivities.length) {
          return prev;
        }

        let targetIndex = Math.max(0, Math.min(desiredIndex, prev.itemActivities.length));
        if (fromIndex < targetIndex) {
          targetIndex -= 1;
        }
        if (targetIndex === fromIndex) {
          return prev;
        }

        const reordered = reorderList(prev.itemActivities, fromIndex, desiredIndex).map((mapping, orderIndex) => ({
          ...mapping,
          displayOrder: orderIndex + 1,
        }));
        return { ...prev, itemActivities: reordered };
      });
      setItemActivityDragIndex(null);
    },
    [itemActivityDragIndex]
  );

  const handleRoleChange = useCallback((index: number, key: keyof PresalesRole, value: string) => {
    setConfig(prev => {
      const roles = [...prev.roles];
      const previous = { ...roles[index] };
      const updated = { ...previous };
      if (key === 'costPerDay') {
        updated.costPerDay = toNumber(value, 0);
      } else if (key === 'roleName') {
        updated.roleName = value;
      } else if (key === 'expectedLevel') {
        updated.expectedLevel = value;
      }
      roles[index] = updated;

      if (key === 'roleName' || key === 'expectedLevel') {
        const oldKeys = enumerateCostKeysForRole(previous);
        const newLabel = buildRoleLabelFromRole(updated);
        setCostConfig(current => {
          if (!current) return current;
          const roleMonthlySalaries = { ...current.roleMonthlySalaries };
          const keyName = resolveDefaultRateCardKey(current);
          const { rateCards, card } = prepareRateCard(current, keyName);
          const roleRates = { ...card.roleRates };

          const previousSalary = findFirstDefinedValue(oldKeys, roleMonthlySalaries);
          const previousRate = findFirstDefinedValue(oldKeys, roleRates);

          for (const candidate of oldKeys) {
            if (candidate) {
              delete roleMonthlySalaries[candidate];
              delete roleRates[candidate];
            }
          }

          if (newLabel) {
            roleMonthlySalaries[newLabel] = previousSalary ?? updated.monthlySalary ?? 0;
            const finalRate = previousRate ?? updated.ratePerDay ?? 0;
            if (finalRate > 0) {
              roleRates[newLabel] = finalRate;
            } else {
              delete roleRates[newLabel];
            }
          }

          rateCards[keyName] = { ...card, roleRates };
          return { ...current, roleMonthlySalaries, rateCards, defaultRateCardKey: keyName };
        });
      }

      return { ...prev, roles };
    });
  }, []);

  const handleActivityChange = useCallback((index: number, key: keyof PresalesActivity, value: string) => {
    if (key !== 'activityName') {
      return;
    }
    setConfig(prev => {
      const activities = [...prev.activities];
      const activity = { ...activities[index], activityName: value };
      activities[index] = activity;
      return { ...prev, activities: resequenceList(activities) };
    });
  }, []);

  const handleRoleSalaryChange = useCallback((index: number, value: string) => {
    const salary = toNumber(value, 0);
    setConfig(prev => {
      const roles = [...prev.roles];
      const role = { ...roles[index] };
      role.monthlySalary = salary;
      roles[index] = role;
      const label = buildRoleLabelFromRole(role);
      if (label) {
        const oldKeys = enumerateCostKeysForRole(role);
        setCostConfig(current => {
          if (!current) return current;
          const roleMonthlySalaries = { ...current.roleMonthlySalaries };
          for (const keyOption of oldKeys) {
            if (keyOption && keyOption !== label) {
              delete roleMonthlySalaries[keyOption];
            }
          }
          roleMonthlySalaries[label] = salary;
          return {
            ...current,
            roleMonthlySalaries,
          };
        });
      }
      return { ...prev, roles };
    });
  }, []);

  const handleRoleRateChange = useCallback((index: number, value: string) => {
    const rate = toNumber(value, 0);
    setConfig(prev => {
      const roles = [...prev.roles];
      const role = { ...roles[index] };
      role.ratePerDay = rate;
      roles[index] = role;
      const label = buildRoleLabelFromRole(role);
      if (label) {
        const oldKeys = enumerateCostKeysForRole(role);
        setCostConfig(current => {
          if (!current) return current;
          const keyName = resolveDefaultRateCardKey(current);
          const { rateCards, card } = prepareRateCard(current, keyName);
          const roleRates = { ...card.roleRates };
          for (const keyOption of oldKeys) {
            if (keyOption && keyOption !== label) {
              delete roleRates[keyOption];
            }
          }
          if (rate > 0) {
            roleRates[label] = rate;
          } else {
            delete roleRates[label];
          }
          rateCards[keyName] = { ...card, roleRates };
          return { ...current, rateCards, defaultRateCardKey: keyName };
        });
      }
      return { ...prev, roles };
    });
  }, []);

  const handleEstimationRoleChange = useCallback((index: number, key: 'estimationColumn' | 'roleName', value: string) => {
    setConfig(prev => {
      const estimationColumnRoles = [...prev.estimationColumnRoles];
      const mapping = { ...estimationColumnRoles[index] };
      if (key === 'estimationColumn') {
        mapping.estimationColumn = value;
      } else if (key === 'roleName') {
        mapping.roleName = value;
      }
      estimationColumnRoles[index] = mapping;
      return { ...prev, estimationColumnRoles };
    });
  }, []);

  const addRole = () =>
    setConfig(prev => ({
      ...prev,
      roles: [
        ...prev.roles,
        { roleName: '', expectedLevel: '', costPerDay: 0, monthlySalary: undefined, ratePerDay: undefined },
      ],
    }));
  const addActivity = () =>
    setConfig(prev => ({
      ...prev,
      activities: resequenceList([...prev.activities, { activityName: '', displayOrder: prev.activities.length + 1 }]),
    }));
  const addItemActivity = () =>
    setConfig(prev => ({
      ...prev,
      itemActivities: [...prev.itemActivities, { sectionName: '', itemName: '', activityName: '', displayOrder: 0 }],
    }));
  const addEstimationColumnRole = () =>
    setConfig(prev => ({
      ...prev,
      estimationColumnRoles: [...prev.estimationColumnRoles, { estimationColumn: '', roleName: '' }],
    }));

  const addTeamType = () =>
    setConfig(prev => ({
      ...prev,
      teamTypes: [
        ...prev.teamTypes,
        { id: undefined, name: '', minManDays: 0, maxManDays: 0, roles: [], clientKey: createClientKey() },
      ],
    }));

  const handleTeamTypeChange = (index: number, key: 'name' | 'minManDays' | 'maxManDays', value: string) => {
    setConfig(prev => {
      const teamTypes = [...prev.teamTypes];
      const current = { ...teamTypes[index] };
      if (key === 'name') {
        current.name = value;
      } else {
        const numeric = Number(value);
        current[key] = Number.isFinite(numeric) ? numeric : 0;
      }
      teamTypes[index] = current;
      return { ...prev, teamTypes };
    });
  };

  const removeTeamType = (index: number) =>
    setConfig(prev => ({
      ...prev,
      teamTypes: prev.teamTypes.filter((_, i) => i !== index),
    }));

  const addTeamTypeRole = (teamTypeIndex: number) => {
    setConfig(prev => {
      const teamTypes = [...prev.teamTypes];
      const current = { ...teamTypes[teamTypeIndex] };
      const roles = [...(current.roles ?? [])];
      roles.push({ id: undefined, teamTypeId: current.id, roleName: '', headcount: 1, clientKey: createClientKey() });
      current.roles = roles;
      teamTypes[teamTypeIndex] = current;
      return { ...prev, teamTypes };
    });
  };

  const handleTeamTypeRoleChange = (
    teamTypeIndex: number,
    roleIndex: number,
    key: 'roleName' | 'headcount',
    value: string
  ) => {
    setConfig(prev => {
      const teamTypes = [...prev.teamTypes];
      const teamType = { ...teamTypes[teamTypeIndex] };
      const roles = [...(teamType.roles ?? [])];
      const current = { ...roles[roleIndex] };
      if (key === 'roleName') {
        current.roleName = value;
      } else {
        const numeric = Number(value);
        current.headcount = Number.isFinite(numeric) ? Math.max(0, numeric) : 0;
      }
      roles[roleIndex] = current;
      teamType.roles = roles;
      teamTypes[teamTypeIndex] = teamType;
      return { ...prev, teamTypes };
    });
  };

  const removeTeamTypeRole = (teamTypeIndex: number, roleIndex: number) => {
    setConfig(prev => {
      const teamTypes = [...prev.teamTypes];
      const teamType = { ...teamTypes[teamTypeIndex] };
      const roles = [...(teamType.roles ?? [])].filter((_, index) => index !== roleIndex);
      teamType.roles = roles;
      teamTypes[teamTypeIndex] = teamType;
      return { ...prev, teamTypes };
    });
  };

  const removeRole = (index: number) =>
    setConfig(prev => {
      const roles = prev.roles.filter((_, i) => i !== index);
      const removed = prev.roles[index];
      if (removed) {
        const label = buildRoleLabelFromRole(removed);
        const oldKeys = enumerateCostKeysForRole(removed);
        setCostConfig(current => {
          if (!current) return current;
          const roleMonthlySalaries = { ...current.roleMonthlySalaries };
          for (const keyOption of oldKeys) {
            if (keyOption) {
              delete roleMonthlySalaries[keyOption];
            }
          }
          const keyName = resolveDefaultRateCardKey(current);
          const { rateCards, card } = prepareRateCard(current, keyName);
          const roleRates = { ...card.roleRates };
          for (const keyOption of oldKeys) {
            if (keyOption) {
              delete roleRates[keyOption];
            }
          }
          if (label) {
            delete roleMonthlySalaries[label];
            delete roleRates[label];
          }
          rateCards[keyName] = { ...card, roleRates };
          return { ...current, roleMonthlySalaries, rateCards, defaultRateCardKey: keyName };
        });
      }
      return { ...prev, roles };
    });
  const removeActivity = (index: number) =>
    setConfig(prev => ({
      ...prev,
      activities: resequenceList(prev.activities.filter((_, i) => i !== index)),
    }));
  const removeItemActivity = (index: number) =>
    setConfig(prev => ({ ...prev, itemActivities: prev.itemActivities.filter((_, i) => i !== index) }));
  const removeEstimationColumnRole = (index: number) =>
    setConfig(prev => ({ ...prev, estimationColumnRoles: prev.estimationColumnRoles.filter((_, i) => i !== index) }));

  const handleSyncItemsFromTemplate = useCallback(async () => {
    setSyncingItems(true);
    setError(null);
    setSuccessMessage(null);
    try {
      const { items } = await fetchReferenceData();
      let added = 0;
      setConfig(prev => {
        const existing = new Set(
          prev.itemActivities.map(mapping => {
            const section = mapping.sectionName?.trim().toLowerCase() ?? '';
            const item = mapping.itemName?.trim().toLowerCase() ?? '';
            return `${section}::${item}`;
          })
        );

        const additions: ItemActivityMapping[] = [];
        for (const task of items ?? []) {
          if (!task) continue;
          const section = task.sectionName?.trim();
          const item = task.itemName?.trim();
          if (!section || !item) {
            continue;
          }

          const key = `${section.toLowerCase()}::${item.toLowerCase()}`;
          if (existing.has(key)) {
            continue;
          }

          existing.add(key);
          additions.push({ sectionName: section, itemName: item, activityName: '', displayOrder: 0 });
        }

        added = additions.length;
        if (added === 0) {
          return prev;
        }
        const nextActivities = [...prev.itemActivities, ...additions];
        return { ...prev, itemActivities: nextActivities };
      });
      if (added > 0) {
        setSuccessMessage(`Added ${added} template item${added > 1 ? 's' : ''} that need activity mapping.`);
      } else {
        setSuccessMessage('All project template items already exist in the mapping.');
      }
    } catch (err) {
      console.warn('Failed to sync template items', err);
      setError(err instanceof Error ? err.message : 'Failed to sync template items');
    } finally {
      setSyncingItems(false);
    }
  }, [fetchReferenceData]);

  const handleSyncEstimationColumns = useCallback(async () => {
    setSyncingEstimationColumns(true);
    setError(null);
    setSuccessMessage(null);
    try {
      const { estimationColumns } = await fetchReferenceData();
      let added = 0;
      setConfig(prev => {
        const existing = new Set(
          prev.estimationColumnRoles
            .map(mapping => mapping.estimationColumn?.trim().toLowerCase())
            .filter((name): name is string => Boolean(name))
        );
        const additions = (estimationColumns ?? [])
          .filter(column => {
            if (!column) return false;
            const key = column.trim().toLowerCase();
            return key.length > 0 && !existing.has(key);
          })
          .map(column => ({ estimationColumn: column, roleName: '' }));
        added = additions.length;
        if (added === 0) {
          return prev;
        }
        const nextRoles = [...prev.estimationColumnRoles, ...additions];
        return { ...prev, estimationColumnRoles: nextRoles };
      });
      if (added > 0) {
        setSuccessMessage(`Added ${added} estimation column${added > 1 ? 's' : ''} that need role allocation.`);
      } else {
        setSuccessMessage('All estimation columns already have role allocations.');
      }
    } catch (err) {
      console.warn('Failed to sync estimation columns', err);
      setError(err instanceof Error ? err.message : 'Failed to sync estimation columns');
    } finally {
      setSyncingEstimationColumns(false);
    }
  }, [fetchReferenceData]);

  const handleSave = useCallback(async () => {
    setSaving(true);
    setError(null);
    setSuccessMessage(null);
    try {
      const payload = buildPresalesPayload(config);
      const presalesResponse = await apiFetch('/api/presales/config', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload),
      });
      if (!presalesResponse.ok) {
        const text = await presalesResponse.text();
        throw new Error(text || 'Failed to save configuration');
      }
      const presalesData = await presalesResponse.json();

      let updatedCost = costConfig;
      if (costConfig) {
        const costResponse = await apiFetch('/api/cost-estimations/configuration', {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(costConfig),
        });
        if (!costResponse.ok) {
          const text = await costResponse.text();
          throw new Error(text || 'Failed to save cost configuration');
        }
        const savedCost = await costResponse.json();
        if (savedCost) {
          const keyName = resolveDefaultRateCardKey(savedCost);
          const { rateCards } = prepareRateCard(savedCost, keyName);
          updatedCost = {
            ...savedCost,
            defaultRateCardKey: keyName,
            rateCards,
            roleMonthlySalaries: { ...savedCost.roleMonthlySalaries },
          };
          setCostConfig(updatedCost);
        }
      }

      const activeRateCardKey = resolveDefaultRateCardKey(updatedCost);
      const activeRateCard = updatedCost?.rateCards?.[activeRateCardKey];

      setConfig(transformPresalesResponse(presalesData, updatedCost));
      setSuccessMessage('Configuration saved successfully.');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to save configuration');
    } finally {
      setSaving(false);
    }
  }, [config, costConfig]);

  const roleOptions = useMemo(() => {
    const seen = new Set<string>();
    return config.roles.reduce<{ value: string; label: string }[]>((acc, role) => {
      const name = normalizeRolePart(role.roleName);
      if (!name) return acc;
      const key = name.toLowerCase();
      if (seen.has(key)) {
        return acc;
      }
      seen.add(key);
      acc.push({ value: name, label: name });
      return acc;
    }, []);
  }, [config.roles]);

  const isCreatingReference = timelineReferenceSavingId === 'new';
  const activityNames = useMemo(
    () => config.activities.map(activity => activity.activityName?.trim()).filter((name): name is string => Boolean(name)),
    [config.activities]
  );

  const activityOrderLookup = useMemo(() => {
    const map = new Map<string, number>();
    for (const activity of config.activities) {
      const name = activity.activityName?.trim();
      if (!name) continue;
      map.set(name, typeof activity.displayOrder === 'number' ? activity.displayOrder : Number(activity.displayOrder) || 0);
    }
    return map;
  }, [config.activities]);

  const sectionOrderLookup = useMemo(() => {
    const map = new Map<string, number>();
    for (const task of availableTasks) {
      const section = task.sectionName?.trim();
      if (!section) continue;
      const order = typeof task.sectionOrder === 'number' ? task.sectionOrder : 0;
      const current = map.get(section);
      if (current === undefined || order < current) {
        map.set(section, order);
      }
    }
    return map;
  }, [availableTasks]);

  const itemOrderLookup = useMemo(() => {
    const map = new Map<string, number>();
    for (const task of availableTasks) {
      const section = task.sectionName?.trim();
      if (!section) continue;
      const item = task.itemName?.trim() ?? '';
      const key = `${section}\0${item}`;
      const order = typeof task.itemOrder === 'number' ? task.itemOrder : (item ? 0 : -1);
      const current = map.get(key);
      if (current === undefined || order < current) {
        map.set(key, order);
      }
    }
    return map;
  }, [availableTasks]);

  const sectionOptions = useMemo(
    () =>
      Array.from(sectionOrderLookup.entries())
        .sort((a, b) => a[1] - b[1] || a[0].localeCompare(b[0]))
        .map(([value]) => value),
    [sectionOrderLookup]
  );

  const itemsBySection = useMemo(() => {
    const map = new Map<string, string[]>();
    sectionOptions.forEach(section => {
      map.set(section, ['']);
    });

    for (const task of availableTasks) {
      const section = task.sectionName?.trim();
      if (!section) continue;
      if (!map.has(section)) {
        map.set(section, ['']);
      }

      const item = task.itemName?.trim() ?? '';
      if (!item) {
        continue;
      }

      const list = map.get(section)!;
      if (!list.some(existing => existing.localeCompare(item, undefined, { sensitivity: 'accent' }) === 0)) {
        list.push(item);
      }
    }

    map.forEach((list, section) => {
      list.sort((a, b) => {
        const orderA = itemOrderLookup.get(`${section}\0${a}`) ?? (a ? Number.MAX_SAFE_INTEGER : -1);
        const orderB = itemOrderLookup.get(`${section}\0${b}`) ?? (b ? Number.MAX_SAFE_INTEGER : -1);
        if (orderA !== orderB) {
          return orderA - orderB;
        }
        return a.localeCompare(b);
      });
    });

    return map;
  }, [availableTasks, itemOrderLookup, sectionOptions]);

  const computeDefaultDisplayOrder = useCallback(
    (sectionName: string, itemName: string, activityName: string) => {
      const normalizedActivity = (activityName ?? '').trim();
      const normalizedSection = (sectionName ?? '').trim();
      const normalizedItem = (itemName ?? '').trim();
      const activityOrder = activityOrderLookup.get(normalizedActivity) ?? 10_000;
      const sectionOrder = sectionOrderLookup.get(normalizedSection) ?? 10_000;
      const itemKey = `${normalizedSection}\0${normalizedItem}`;
      const rawItemOrder = itemOrderLookup.get(itemKey);
      const itemOrder = rawItemOrder !== undefined ? rawItemOrder : (normalizedItem ? 1_000 : -1);
      const normalizedItemOrder = itemOrder >= 0 ? itemOrder + 1 : 0;
      return activityOrder * 1_000_000 + sectionOrder * 1_000 + normalizedItemOrder;
    },
    [activityOrderLookup, sectionOrderLookup, itemOrderLookup]
  );

  const applyDefaultDisplayOrder = useCallback(
    (mapping: ItemActivityMapping): ItemActivityMapping => {
      if (mapping.displayOrder && mapping.displayOrder > 0) {
        return mapping;
      }

      const computed = computeDefaultDisplayOrder(mapping.sectionName, mapping.itemName, mapping.activityName);
      if (computed <= 0 || computed === mapping.displayOrder) {
        return mapping;
      }

      return { ...mapping, displayOrder: computed };
    },
    [computeDefaultDisplayOrder]
  );

  useEffect(() => {
    setConfig(prev => {
      let changed = false;
      const nextItemActivities = prev.itemActivities.map(mapping => {
        const updated = applyDefaultDisplayOrder(mapping);
        if (updated !== mapping) {
          changed = true;
        }
        return updated;
      });
      if (!changed) {
        return prev;
      }
      return { ...prev, itemActivities: nextItemActivities };
    });
  }, [applyDefaultDisplayOrder]);

  const handleItemActivityChange = useCallback(
    (index: number, key: keyof ItemActivityMapping, value: string) => {
      setConfig(prev => {
        const itemActivities = [...prev.itemActivities];
        const current = { ...itemActivities[index] };

        if (key === 'sectionName') {
          current.sectionName = value;
          const normalizedSection = value.trim();
          if (!normalizedSection) {
            current.itemName = '';
          } else if (current.itemName) {
            const normalizedItem = current.itemName.trim();
            const normalizedSectionLower = normalizedSection.toLowerCase();
            const normalizedItemLower = normalizedItem.toLowerCase();
            const hasItem = availableTasks.some(task => {
              const taskSection = task.sectionName ? task.sectionName.trim().toLowerCase() : '';
              if (!taskSection || taskSection !== normalizedSectionLower) {
                return false;
              }
              const taskItem = task.itemName ? task.itemName.trim().toLowerCase() : '';
              return taskItem === normalizedItemLower;
            });
            if (!hasItem) {
              current.itemName = '';
            }
          }
          current.displayOrder = 0;
        } else if (key === 'itemName') {
          current.itemName = value;
          current.displayOrder = 0;
        } else if (key === 'activityName') {
          current.activityName = value;
          current.displayOrder = 0;
        }

        const updated = applyDefaultDisplayOrder(current);
        itemActivities[index] = updated;
        return { ...prev, itemActivities };
      });
    },
    [applyDefaultDisplayOrder, availableTasks]
  );


  return (
    <Box sx={{ maxWidth: 1200, mx: 'auto', py: 6, display: 'flex', flexDirection: 'column', gap: 3 }}>
      <Stack
        direction={{ xs: 'column', sm: 'row' }}
        spacing={2}
        justifyContent="space-between"
        alignItems={{ xs: 'flex-start', sm: 'center' }}
      >
        <Box>
          <Typography variant="h1" gutterBottom>
            Pre-Sales Timeline Configuration
          </Typography>
          <Typography variant="body1" color="text.secondary">
            Define the master data that drives project timeline generation.
          </Typography>
        </Box>
        <Button variant="contained" color="primary" onClick={handleSave} disabled={saving || loading}>
          {saving ? 'Saving…' : 'Save Changes'}
        </Button>
      </Stack>
      <Paper variant="outlined" sx={{ p: 3, bgcolor: 'background.paper', borderRadius: 3 }}>
        {loading ? (
          <Stack direction="row" alignItems="center" spacing={2} justifyContent="center" sx={{ py: 6 }}>
            <CircularProgress size={32} />
            <Typography variant="body1">Loading presales configuration…</Typography>
          </Stack>
        ) : (
          <Stack spacing={4}>
            {error && <Alert severity="error">{error}</Alert>}
            {successMessage && <Alert severity="success">{successMessage}</Alert>}

            <Tabs
              value={activeTab}
              onChange={handleTabChange}
              variant="scrollable"
              scrollButtons="auto"
              allowScrollButtonsMobile
            >
              <Tab label="Roles & Rates" value="roles" />
              <Tab label="Activity Groupings" value="activities" />
              <Tab label="Item → Activity Mapping" value="items" />
              <Tab label="Estimation Column → Role Allocation" value="columns" />
              <Tab label="Timeline Estimator — Team Types" value="timelineTeamTypes" />
              <Tab label="Timeline Estimator — References" value="timelineReferences" />
            </Tabs>

            {activeTab === 'roles' && (
              <Stack spacing={2}>
                <Stack direction="row" justifyContent="space-between" alignItems="center">
                  <Typography variant="h2" className="section-title">Roles &amp; Rates</Typography>
                  <Button startIcon={<AddIcon />} variant="outlined" onClick={addRole}>Add Role</Button>
                </Stack>
                <TableContainer>
                  <Table size="small">
                    <TableHead>
                      <TableRow>
                        <TableCell>Role Name</TableCell>
                        <TableCell>Expected Level</TableCell>
                        <TableCell>Monthly Salary (IDR)</TableCell>
                        <TableCell>Billing Rate / Day</TableCell>
                        <TableCell align="right">Actions</TableCell>
                      </TableRow>
                    </TableHead>
                    <TableBody>
                      {config.roles.map((role, index) => (
                        <TableRow key={index}>
                          <TableCell>
                            <TextField
                              fullWidth
                              size="small"
                              value={role.roleName}
                              onChange={(event: ChangeEvent<HTMLInputElement>) => handleRoleChange(index, 'roleName', event.target.value)}
                              placeholder="e.g. Architect"
                            />
                          </TableCell>
                          <TableCell>
                            <TextField
                              fullWidth
                              size="small"
                              value={role.expectedLevel}
                              onChange={(event: ChangeEvent<HTMLInputElement>) => handleRoleChange(index, 'expectedLevel', event.target.value)}
                              placeholder="e.g. Senior"
                            />
                          </TableCell>
                          <TableCell>
                            <TextField
                              fullWidth
                              size="small"
                              value={formatIDR(role.monthlySalary)}
                              inputProps={{ inputMode: 'numeric', pattern: '[0-9.,]*' }}
                              onChange={(event: ChangeEvent<HTMLInputElement>) => handleRoleSalaryChange(index, event.target.value)}
                              placeholder="e.g. 15.000.000"
                            />
                          </TableCell>
                          <TableCell>
                            <TextField
                              fullWidth
                              size="small"
                              value={formatIDR(role.ratePerDay)}
                              inputProps={{ inputMode: 'numeric', pattern: '[0-9.,]*' }}
                              onChange={(event: ChangeEvent<HTMLInputElement>) => handleRoleRateChange(index, event.target.value)}
                              placeholder="e.g. 1.500.000"
                            />
                          </TableCell>
                          <TableCell align="right">
                            <IconButton onClick={() => removeRole(index)} aria-label="Remove role">
                              <DeleteIcon />
                            </IconButton>
                          </TableCell>
                        </TableRow>
                      ))}
                    </TableBody>
                  </Table>
                </TableContainer>
              </Stack>
            )}

            {activeTab === 'activities' && (
              <Stack spacing={2}>
                <Stack direction="row" justifyContent="space-between" alignItems="center">
                  <Typography variant="h2" className="section-title">Activity Groupings</Typography>
                  <Button startIcon={<AddIcon />} variant="outlined" onClick={addActivity}>Add Activity</Button>
                </Stack>
                <TableContainer>
                  <Table size="small">
                    <TableHead>
                      <TableRow>
                        <TableCell width={80}>Order</TableCell>
                        <TableCell>Activity Name</TableCell>
                        <TableCell align="right">Actions</TableCell>
                      </TableRow>
                    </TableHead>
                    <TableBody>
                      {config.activities.map((activity, index) => (
                        <TableRow
                          key={index}
                          onDrop={event => handleActivityDrop(event, index)}
                          onDragOver={handleActivityDragOver}
                        >
                          <TableCell width={80} sx={{ color: 'text.secondary' }}>
                            <Stack direction="row" alignItems="center" spacing={1}>
                              <Box
                                component="span"
                                draggable
                                onDragStart={event => handleActivityDragStart(event, index)}
                                onDragEnd={handleActivityDragEnd}
                                sx={{ cursor: 'grab', display: 'inline-flex', color: 'text.secondary' }}
                                aria-label="Drag to reorder activity"
                              >
                                <DragIndicatorIcon fontSize="small" />
                              </Box>
                              <Typography variant="body2" color="text.secondary">
                                {index + 1}
                              </Typography>
                            </Stack>
                          </TableCell>
                          <TableCell>
                            <TextField
                              fullWidth
                              size="small"
                              value={activity.activityName}
                              onChange={(event: ChangeEvent<HTMLInputElement>) => handleActivityChange(index, 'activityName', event.target.value)}
                              placeholder="e.g. Analysis & Design"
                            />
                          </TableCell>
                          <TableCell align="right">
                            <IconButton onClick={() => removeActivity(index)} aria-label="Remove activity">
                              <DeleteIcon />
                            </IconButton>
                          </TableCell>
                        </TableRow>
                      ))}
                    </TableBody>
                  </Table>
                </TableContainer>
              </Stack>
            )}

            {activeTab === 'items' && (
              <Stack spacing={2}>
                <Stack direction={{ xs: 'column', sm: 'row' }} justifyContent="space-between" alignItems={{ xs: 'flex-start', sm: 'center' }} spacing={1}>
                  <Typography variant="h2" className="section-title">Item → Activity Mapping</Typography>
                  <Stack direction="row" spacing={1} alignItems="center">
                    <Button
                      startIcon={<SyncIcon />}
                      variant="outlined"
                      onClick={handleSyncItemsFromTemplate}
                      disabled={syncingItems || loading}
                    >
                      {syncingItems ? 'Syncing…' : 'Sync Template Items'}
                    </Button>
                    <Button startIcon={<AddIcon />} variant="outlined" onClick={addItemActivity}>Add Mapping</Button>
                  </Stack>
                </Stack>
                <TableContainer>
                  <Table size="small">
                    <TableHead>
                      <TableRow>
                        <TableCell width={80}>Order</TableCell>
                        <TableCell>Section</TableCell>
                        <TableCell>Item</TableCell>
                        <TableCell>Activity</TableCell>
                        <TableCell align="right">Actions</TableCell>
                      </TableRow>
                    </TableHead>
                    <TableBody>
                      {config.itemActivities.map((mapping, index) => {
                        const sectionValue = mapping.sectionName || '';
                        const normalizedSection = sectionValue.trim();
                        const itemOptions = itemsBySection.get(normalizedSection) ?? [''];
                        return (
                          <TableRow
                            key={index}
                            onDrop={event => handleItemActivityDrop(event, index)}
                            onDragOver={handleItemActivityDragOver}
                          >
                            <TableCell width={80} sx={{ color: 'text.secondary' }}>
                              <Stack direction="row" alignItems="center" spacing={1}>
                                <Box
                                  component="span"
                                  draggable
                                  onDragStart={event => handleItemActivityDragStart(event, index)}
                                  onDragEnd={handleItemActivityDragEnd}
                                  sx={{ cursor: 'grab', display: 'inline-flex', color: 'text.secondary' }}
                                  aria-label="Drag to reorder mapping"
                                >
                                  <DragIndicatorIcon fontSize="small" />
                                </Box>
                                <Typography variant="body2" color="text.secondary">
                                  {index + 1}
                                </Typography>
                              </Stack>
                            </TableCell>
                            <TableCell>
                              <Autocomplete
                                options={sectionOptions}
                                freeSolo
                                autoSelect
                                value={sectionValue}
                                onChange={(_, newValue) => handleItemActivityChange(index, 'sectionName', newValue ?? '')}
                                onInputChange={(_, newValue) => handleItemActivityChange(index, 'sectionName', newValue ?? '')}
                                renderInput={params => (
                                  <TextField
                                    {...params}
                                    fullWidth
                                    size="small"
                                    placeholder="Select section"
                                  />
                                )}
                                isOptionEqualToValue={(option, value) => option === value}
                              />
                            </TableCell>
                            <TableCell>
                              <Autocomplete
                                options={itemOptions}
                                freeSolo
                                autoSelect
                                value={mapping.itemName || ''}
                                onChange={(_, newValue) => handleItemActivityChange(index, 'itemName', newValue ?? '')}
                                onInputChange={(_, newValue) => handleItemActivityChange(index, 'itemName', newValue ?? '')}
                                getOptionLabel={option => (option ? option : 'Entire Section')}
                                renderInput={params => (
                                  <TextField
                                    {...params}
                                    fullWidth
                                    size="small"
                                    placeholder="Select item or leave blank for entire section"
                                  />
                                )}
                                isOptionEqualToValue={(option, value) => option === value}
                              />
                            </TableCell>
                            <TableCell>
                              <TextField
                                select
                                fullWidth
                                size="small"
                                value={mapping.activityName}
                                onChange={(event: ChangeEvent<HTMLInputElement>) => handleItemActivityChange(index, 'activityName', event.target.value)}
                              >
                                {activityNames.length === 0 && <MenuItem value="">—</MenuItem>}
                                {activityNames.map(name => (
                                  <MenuItem key={name} value={name}>{name}</MenuItem>
                                ))}
                              </TextField>
                            </TableCell>
                            <TableCell align="right">
                              <IconButton onClick={() => removeItemActivity(index)} aria-label="Remove mapping">
                                <DeleteIcon />
                              </IconButton>
                            </TableCell>
                          </TableRow>
                        );
                      })}
                    </TableBody>
                  </Table>
                </TableContainer>
              </Stack>
            )}

            {activeTab === 'columns' && (
              <Stack spacing={2}>
                <Stack direction={{ xs: 'column', sm: 'row' }} justifyContent="space-between" alignItems={{ xs: 'flex-start', sm: 'center' }} spacing={1}>
                  <Typography variant="h2" className="section-title">Estimation Column → Role Allocation</Typography>
                  <Stack direction="row" spacing={1} alignItems="center">
                    <Button
                      startIcon={<SyncIcon />}
                      variant="outlined"
                      onClick={handleSyncEstimationColumns}
                      disabled={syncingEstimationColumns || loading}
                    >
                      {syncingEstimationColumns ? 'Syncing…' : 'Sync Estimation Columns'}
                    </Button>
                    <Button startIcon={<AddIcon />} variant="outlined" onClick={addEstimationColumnRole}>Add Allocation</Button>
                  </Stack>
                </Stack>
                <TableContainer>
                  <Table size="small">
                    <TableHead>
                      <TableRow>
                        <TableCell>Estimation Column</TableCell>
                        <TableCell>Role</TableCell>
                        <TableCell align="right">Actions</TableCell>
                      </TableRow>
                    </TableHead>
                    <TableBody>
                      {config.estimationColumnRoles.map((mapping, index) => (
                        <TableRow key={index}>
                          <TableCell>
                            <Autocomplete
                              options={availableEstimationColumns}
                              value={mapping.estimationColumn || ''}
                              onChange={(_, newValue) => handleEstimationRoleChange(index, 'estimationColumn', newValue ?? '')}
                              autoHighlight
                              renderInput={params => (
                                <TextField
                                  {...params}
                                  fullWidth
                                  size="small"
                                  placeholder="Select estimation column"
                                />
                              )}
                            />
                          </TableCell>
                          <TableCell>
                            <TextField
                              select
                              fullWidth
                              size="small"
                              value={mapping.roleName || ''}
                              onChange={(event: ChangeEvent<HTMLInputElement>) => handleEstimationRoleChange(index, 'roleName', event.target.value)}
                              SelectProps={{
                                displayEmpty: true,
                                renderValue: (selected: unknown) => {
                                  if (typeof selected !== 'string' || !selected) {
                                    return '—';
                                  }
                                  const option = roleOptions.find(item => item.value === selected);
                                  return option?.label ?? selected;
                                },
                              }}
                            >
                              <MenuItem value="">—</MenuItem>
                              {roleOptions.map(option => (
                                <MenuItem key={option.value} value={option.value}>{option.label}</MenuItem>
                              ))}
                            </TextField>
                          </TableCell>
                          <TableCell align="right">
                            <IconButton onClick={() => removeEstimationColumnRole(index)} aria-label="Remove allocation">
                              <DeleteIcon />
                            </IconButton>
                          </TableCell>
                        </TableRow>
                      ))}
                    </TableBody>
                  </Table>
                </TableContainer>
              </Stack>
            )}

            {activeTab === 'timelineTeamTypes' && (
              <Stack spacing={3}>
                <Stack spacing={1}>
                  <Typography variant="h2" className="section-title">Estimator Team Types</Typography>
                  <Typography variant="body2" color="text.secondary">
                    Define the default presales squads that the Timeline Estimator recommends for different man-day ranges before
                    the workflow continues to Timeline Generation and Estimated Cost Generation.
                  </Typography>
                </Stack>

                <Stack spacing={2}>
                  {config.teamTypes.length === 0 ? (
                    <Paper variant="outlined" sx={{ p: 3, borderRadius: 2 }}>
                      <Typography variant="body2" color="text.secondary">
                        No team types configured yet. Add a team type to describe the typical mix of roles for a given workload.
                      </Typography>
                    </Paper>
                  ) : (
                    config.teamTypes.map((teamType, index) => (
                      <Paper
                        key={teamType.clientKey ?? teamType.id ?? index}
                        variant="outlined"
                        sx={{ p: 3, borderRadius: 2 }}
                      >
                        <Stack spacing={2}>
                          <Stack
                            direction={{ xs: 'column', md: 'row' }}
                            spacing={2}
                            alignItems={{ xs: 'flex-start', md: 'flex-end' }}
                          >
                            <TextField
                              label="Team Type Name"
                              fullWidth
                              size="small"
                              value={teamType.name}
                              onChange={(event: ChangeEvent<HTMLInputElement>) =>
                                handleTeamTypeChange(index, 'name', event.target.value)
                              }
                              placeholder="e.g. Core Delivery Squad"
                            />
                            <TextField
                              label="Minimum Man-Days"
                              type="number"
                              size="small"
                              value={teamType.minManDays ?? 0}
                              onChange={(event: ChangeEvent<HTMLInputElement>) =>
                                handleTeamTypeChange(index, 'minManDays', event.target.value)
                              }
                              inputProps={{ min: 0 }}
                              sx={{ width: { xs: '100%', md: 180 } }}
                            />
                            <TextField
                              label="Maximum Man-Days"
                              type="number"
                              size="small"
                              value={teamType.maxManDays ?? 0}
                              onChange={(event: ChangeEvent<HTMLInputElement>) =>
                                handleTeamTypeChange(index, 'maxManDays', event.target.value)
                              }
                              inputProps={{ min: 0 }}
                              sx={{ width: { xs: '100%', md: 180 } }}
                            />
                            <Button
                              variant="text"
                              color="error"
                              startIcon={<DeleteIcon />}
                              onClick={() => removeTeamType(index)}
                              sx={{ alignSelf: { xs: 'flex-start', md: 'center' } }}
                            >
                              Remove
                            </Button>
                          </Stack>

                          <TableContainer>
                            <Table size="small">
                              <TableHead>
                                <TableRow>
                                  <TableCell>Role Name</TableCell>
                                  <TableCell width={160}>Headcount</TableCell>
                                  <TableCell align="right">Actions</TableCell>
                                </TableRow>
                              </TableHead>
                              <TableBody>
                                {teamType.roles?.length ? (
                                  teamType.roles.map((role, roleIndex) => (
                                    <TableRow key={role.clientKey ?? role.id ?? roleIndex}>
                                      <TableCell>
                                        <TextField
                                          select
                                          fullWidth
                                          size="small"
                                          value={role.roleName || ''}
                                          onChange={(event: ChangeEvent<HTMLInputElement>) =>
                                            handleTeamTypeRoleChange(index, roleIndex, 'roleName', event.target.value)
                                          }
                                          SelectProps={{
                                            displayEmpty: true,
                                            renderValue: (selected: unknown) => {
                                              if (typeof selected !== 'string' || !selected) {
                                                return 'Select role';
                                              }
                                              const option = roleOptions.find(item => item.value === selected);
                                              return option?.label ?? selected;
                                            },
                                          }}
                                        >
                                          <MenuItem value="">Select role</MenuItem>
                                          {roleOptions.map(option => (
                                            <MenuItem key={option.value} value={option.value}>{option.label}</MenuItem>
                                          ))}
                                        </TextField>
                                      </TableCell>
                                      <TableCell width={160}>
                                        <TextField
                                          type="number"
                                          size="small"
                                          value={role.headcount ?? 0}
                                          onChange={(event: ChangeEvent<HTMLInputElement>) =>
                                            handleTeamTypeRoleChange(index, roleIndex, 'headcount', event.target.value)
                                          }
                                          inputProps={{ min: 0, step: 0.1 }}
                                        />
                                      </TableCell>
                                      <TableCell align="right">
                                        <IconButton
                                          aria-label="Remove role"
                                          onClick={() => removeTeamTypeRole(index, roleIndex)}
                                        >
                                          <DeleteIcon />
                                        </IconButton>
                                      </TableCell>
                                    </TableRow>
                                  ))
                                ) : (
                                  <TableRow>
                                    <TableCell colSpan={3} align="center" sx={{ color: 'text.secondary' }}>
                                      No roles configured for this team type yet.
                                    </TableCell>
                                  </TableRow>
                                )}
                              </TableBody>
                            </Table>
                          </TableContainer>

                          <Stack direction="row" justifyContent="flex-end">
                            <Button
                              variant="outlined"
                              startIcon={<AddIcon />}
                              onClick={() => addTeamTypeRole(index)}
                            >
                              Add Role
                            </Button>
                          </Stack>
                        </Stack>
                      </Paper>
                    ))
                  )}
                </Stack>

                <Button
                  startIcon={<AddIcon />}
                  variant="outlined"
                  onClick={addTeamType}
                  sx={{ alignSelf: 'flex-start' }}
                >
                  Add Team Type
                </Button>

              </Stack>
            )}

            {activeTab === 'timelineReferences' && (
              <Stack spacing={3}>
                <Stack spacing={1}>
                  <Typography variant="h2" className="section-title">Timeline Estimator References</Typography>
                  <Typography variant="body2" color="text.secondary">
                    Maintain the historical phase durations and resource allocations that the Timeline Estimator consults before
                    it summarizes the Presales Workspace → Timeline Estimator → Timeline Generation → Estimated Cost Generation
                    flow.
                  </Typography>
                </Stack>

                {timelineReferencesPermissionDenied && (
                  <Alert severity="warning">
                    Your role does not currently have access to the timeline estimator reference catalog. Ask an administrator to
                    grant the <code>timeline-estimation-references</code> permission if you need to manage these records.
                  </Alert>
                )}
                {timelineReferencesError && <Alert severity="error">{timelineReferencesError}</Alert>}

                <Box component="form" onSubmit={handleCreateTimelineReference}>
                  <Paper variant="outlined" sx={{ p: 3, borderRadius: 2 }}>
                    <Stack spacing={2}>
                      <Typography variant="subtitle1">Add Reference</Typography>
                      <Stack direction={{ xs: 'column', md: 'row' }} spacing={2}>
                        <TextField
                          label="Project Scale"
                          placeholder="e.g. Medium"
                          fullWidth
                          size="small"
                          value={timelineReferenceDraft.projectScale}
                          onChange={(event: ChangeEvent<HTMLInputElement>) =>
                            setTimelineReferenceDraft(prev => ({ ...prev, projectScale: event.target.value }))
                          }
                          disabled={timelineReferencesPermissionDenied || isCreatingReference}
                          required
                        />
                        <TextField
                          label="Total Duration (days)"
                          type="number"
                          size="small"
                          value={timelineReferenceDraft.totalDurationDays}
                          onChange={(event: ChangeEvent<HTMLInputElement>) =>
                            setTimelineReferenceDraft(prev => ({
                              ...prev,
                              totalDurationDays: Number(event.target.value) || 0,
                            }))
                          }
                          inputProps={{ min: 1 }}
                          sx={{ width: { xs: '100%', md: 200 } }}
                          disabled={timelineReferencesPermissionDenied || isCreatingReference}
                          required
                        />
                      </Stack>
                      <TextField
                        label="Phase Durations (JSON)"
                        multiline
                        minRows={4}
                        value={timelineReferenceDraft.phaseDurationsText}
                        onChange={(event: ChangeEvent<HTMLInputElement>) =>
                          setTimelineReferenceDraft(prev => ({ ...prev, phaseDurationsText: event.target.value }))
                        }
                        placeholder={'{\n  "Discovery": 5,\n  "Build": 20,\n  "Stabilization": 5\n}'}
                        disabled={timelineReferencesPermissionDenied || isCreatingReference}
                        required
                      />
                      <TextField
                        label="Resource Allocation (JSON)"
                        multiline
                        minRows={4}
                        value={timelineReferenceDraft.resourceAllocationText}
                        onChange={(event: ChangeEvent<HTMLInputElement>) =>
                          setTimelineReferenceDraft(prev => ({ ...prev, resourceAllocationText: event.target.value }))
                        }
                        placeholder={'{\n  "Delivery Lead": 1,\n  "Engineer": 4\n}'}
                        disabled={timelineReferencesPermissionDenied || isCreatingReference}
                        required
                      />
                      <Stack direction="row" spacing={1} justifyContent="flex-end">
                        <Button
                          type="button"
                          variant="text"
                          color="inherit"
                          onClick={resetTimelineReferenceDraft}
                          disabled={timelineReferencesPermissionDenied || isCreatingReference}
                        >
                          Reset
                        </Button>
                        <Button
                          type="submit"
                          variant="contained"
                          disabled={timelineReferencesPermissionDenied || isCreatingReference}
                        >
                          {isCreatingReference ? 'Saving…' : 'Add Reference'}
                        </Button>
                      </Stack>
                    </Stack>
                  </Paper>
                </Box>

                <Paper variant="outlined" sx={{ p: 3, borderRadius: 2 }}>
                  <Stack spacing={2}>
                    <Typography variant="subtitle1">Existing References</Typography>
                    {timelineReferencesLoading ? (
                      <Stack direction="row" spacing={2} alignItems="center" justifyContent="center" sx={{ py: 4 }}>
                        <CircularProgress size={24} />
                        <Typography variant="body2">Loading reference library…</Typography>
                      </Stack>
                    ) : timelineReferences.length === 0 ? (
                      <Typography variant="body2" color="text.secondary">
                        No timeline estimator references found.
                      </Typography>
                    ) : (
                      <TableContainer>
                        <Table size="small">
                          <TableHead>
                            <TableRow>
                              <TableCell>Project Scale</TableCell>
                              <TableCell width={160}>Total Duration (days)</TableCell>
                              <TableCell>Phase Durations (JSON)</TableCell>
                              <TableCell>Resource Allocation (JSON)</TableCell>
                              <TableCell align="right">Actions</TableCell>
                            </TableRow>
                          </TableHead>
                          <TableBody>
                            {timelineReferences.map(reference => {
                              const isSaving = timelineReferenceSavingId === reference.id;
                              return (
                                <TableRow key={reference.id} hover>
                                  <TableCell>
                                    <TextField
                                      fullWidth
                                      size="small"
                                      value={reference.projectScale}
                                      onChange={(event: ChangeEvent<HTMLInputElement>) =>
                                        updateTimelineReferenceState(reference.id, { projectScale: event.target.value })
                                      }
                                      disabled={timelineReferencesPermissionDenied || isSaving}
                                    />
                                  </TableCell>
                                  <TableCell width={160}>
                                    <TextField
                                      type="number"
                                      size="small"
                                      value={reference.totalDurationDays}
                                      onChange={(event: ChangeEvent<HTMLInputElement>) =>
                                        updateTimelineReferenceState(reference.id, {
                                          totalDurationDays: Number(event.target.value) || 0,
                                        })
                                      }
                                      inputProps={{ min: 1 }}
                                      disabled={timelineReferencesPermissionDenied || isSaving}
                                    />
                                  </TableCell>
                                  <TableCell>
                                    <TextField
                                      multiline
                                      minRows={4}
                                      value={reference.phaseDurationsText}
                                      onChange={(event: ChangeEvent<HTMLInputElement>) =>
                                        updateTimelineReferenceState(reference.id, {
                                          phaseDurationsText: event.target.value,
                                        })
                                      }
                                      disabled={timelineReferencesPermissionDenied || isSaving}
                                    />
                                  </TableCell>
                                  <TableCell>
                                    <TextField
                                      multiline
                                      minRows={4}
                                      value={reference.resourceAllocationText}
                                      onChange={(event: ChangeEvent<HTMLInputElement>) =>
                                        updateTimelineReferenceState(reference.id, {
                                          resourceAllocationText: event.target.value,
                                        })
                                      }
                                      disabled={timelineReferencesPermissionDenied || isSaving}
                                    />
                                  </TableCell>
                                  <TableCell align="right">
                                    <Stack direction="row" spacing={1} justifyContent="flex-end">
                                      <Button
                                        size="small"
                                        variant="contained"
                                        onClick={() => handleSaveTimelineReference(reference.id)}
                                        disabled={timelineReferencesPermissionDenied || isSaving}
                                      >
                                        {isSaving ? 'Saving…' : 'Save'}
                                      </Button>
                                      <Button
                                        size="small"
                                        color="error"
                                        onClick={() => handleDeleteTimelineReference(reference.id)}
                                        disabled={timelineReferencesPermissionDenied || isSaving}
                                      >
                                        {isSaving ? 'Removing…' : 'Delete'}
                                      </Button>
                                    </Stack>
                                  </TableCell>
                                </TableRow>
                              );
                            })}
                          </TableBody>
                        </Table>
                      </TableContainer>
                    )}
                  </Stack>
                </Paper>
              </Stack>
            )}
          </Stack>
        )}
      </Paper>
    </Box>
  );
}
