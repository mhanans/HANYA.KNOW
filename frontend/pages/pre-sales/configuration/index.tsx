import { ChangeEvent, SyntheticEvent, useCallback, useEffect, useMemo, useState } from 'react';
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
} from '@mui/material';
import DeleteIcon from '@mui/icons-material/Delete';
import AddIcon from '@mui/icons-material/Add';
import SyncIcon from '@mui/icons-material/Sync';
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

interface PresalesConfiguration {
  roles: PresalesRole[];
  activities: PresalesActivity[];
  itemActivities: ItemActivityMapping[];
  estimationColumnRoles: EstimationColumnRoleMapping[];
}

const emptyConfig: PresalesConfiguration = {
  roles: [],
  activities: [],
  itemActivities: [],
  estimationColumnRoles: [],
};

type TabKey = 'roles' | 'activities' | 'items' | 'columns';

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
  const [config, setConfig] = useState<PresalesConfiguration>(emptyConfig);
  const [costConfig, setCostConfig] = useState<CostEstimationConfiguration | null>(null);
  const [availableTasks, setAvailableTasks] = useState<TemplateTaskReference[]>([]);
  const [availableEstimationColumns, setAvailableEstimationColumns] = useState<string[]>([]);
  const [activeTab, setActiveTab] = useState<TabKey>('roles');
  const [syncingItems, setSyncingItems] = useState(false);
  const [syncingEstimationColumns, setSyncingEstimationColumns] = useState(false);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);

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

      const activeRateCardKey = resolveDefaultRateCardKey(normalizedCost);
      const activeRateCard = normalizedCost?.rateCards?.[activeRateCardKey];

      setConfig({
        roles: (presalesData.roles ?? []).map((role: PresalesRole) => {
          const label = buildRoleLabelFromRole(role);
          const costKeys = enumerateCostKeysForRole(role);
          const monthlySalary =
            label && normalizedCost
              ? findFirstDefinedValue(costKeys, normalizedCost.roleMonthlySalaries) ?? role.monthlySalary ?? 0
              : role.monthlySalary ?? 0;
          const ratePerDay =
            label && activeRateCard
              ? findFirstDefinedValue(costKeys, activeRateCard.roleRates) ?? role.ratePerDay ?? 0
              : role.ratePerDay ?? 0;
          return {
            ...role,
            monthlySalary,
            ratePerDay,
          };
        }),
        activities: presalesData.activities ?? [],
        itemActivities: (presalesData.itemActivities ?? []).map((mapping: Partial<ItemActivityMapping>) => ({
          sectionName: mapping.sectionName ?? '',
          itemName: mapping.itemName ?? '',
          activityName: mapping.activityName ?? '',
          displayOrder: typeof mapping.displayOrder === 'number' ? mapping.displayOrder : 0,
        })),
        estimationColumnRoles: (presalesData.estimationColumnRoles ?? []).map(
          (mapping: Partial<EstimationColumnRoleMapping>) => ({
            estimationColumn: mapping.estimationColumn ?? '',
            roleName: mapping.roleName ?? '',
          })
        ),
      });
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

  const handleTabChange = useCallback((_: SyntheticEvent, newValue: string) => {
    setActiveTab(newValue as TabKey);
  }, []);

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
    setConfig(prev => {
      const activities = [...prev.activities];
      const activity = { ...activities[index] };
      if (key === 'displayOrder') {
        activity.displayOrder = Math.max(1, Math.round(toNumber(value, 1)));
      } else if (key === 'activityName') {
        activity.activityName = value;
      }
      activities[index] = activity;
      return { ...prev, activities };
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
            const hasItem = availableTasks.some(
              task =>
                task.sectionName.trim().toLowerCase() === normalizedSection.toLowerCase() &&
                task.itemName.trim().toLowerCase() === normalizedItem.toLowerCase()
            );
            if (!hasItem) {
              current.itemName = '';
            }
          }
        } else if (key === 'itemName') {
          current.itemName = value;
        } else if (key === 'activityName') {
          current.activityName = value;
        } else if (key === 'displayOrder') {
          current.displayOrder = value ? parseInt(value, 10) || 0 : 0;
        }

        const updated = applyDefaultDisplayOrder(current);
        itemActivities[index] = updated;
        return { ...prev, itemActivities };
      });
    },
    [applyDefaultDisplayOrder, availableTasks]
  );

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
  const addActivity = () => setConfig(prev => ({ ...prev, activities: [...prev.activities, { activityName: '', displayOrder: prev.activities.length + 1 }] }));
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
  const removeActivity = (index: number) => setConfig(prev => ({ ...prev, activities: prev.activities.filter((_, i) => i !== index) }));
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
      const presalesResponse = await apiFetch('/api/presales/config', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(config),
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

      setConfig({
        roles: (presalesData.roles ?? []).map((role: PresalesRole) => {
          const label = buildRoleLabelFromRole(role);
          const costKeys = enumerateCostKeysForRole(role);
          return {
            ...role,
            monthlySalary:
              label && updatedCost
                ? findFirstDefinedValue(costKeys, updatedCost.roleMonthlySalaries) ?? 0
                : 0,
            ratePerDay:
              label && activeRateCard
                ? findFirstDefinedValue(costKeys, activeRateCard.roleRates) ?? 0
                : 0,
          };
        }),
        activities: presalesData.activities ?? [],
        itemActivities: (presalesData.itemActivities ?? []).map((mapping: Partial<ItemActivityMapping>) => ({
          sectionName: mapping.sectionName ?? '',
          itemName: mapping.itemName ?? '',
          activityName: mapping.activityName ?? '',
          displayOrder: typeof mapping.displayOrder === 'number' ? mapping.displayOrder : 0,
        })),
        estimationColumnRoles: (presalesData.estimationColumnRoles ?? []).map(
          (mapping: Partial<EstimationColumnRoleMapping>) => ({
            estimationColumn: mapping.estimationColumn ?? '',
            roleName: mapping.roleName ?? '',
          })
        ),
      });
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
                        <TableCell>Activity Name</TableCell>
                        <TableCell>Display Order</TableCell>
                        <TableCell align="right">Actions</TableCell>
                      </TableRow>
                    </TableHead>
                    <TableBody>
                      {config.activities.map((activity, index) => (
                        <TableRow key={index}>
                          <TableCell>
                            <TextField
                              fullWidth
                              size="small"
                              value={activity.activityName}
                              onChange={(event: ChangeEvent<HTMLInputElement>) => handleActivityChange(index, 'activityName', event.target.value)}
                              placeholder="e.g. Analysis & Design"
                            />
                          </TableCell>
                          <TableCell>
                            <TextField
                              fullWidth
                              size="small"
                              type="number"
                              inputProps={{ min: 1, step: 1 }}
                              value={activity.displayOrder}
                              onChange={(event: ChangeEvent<HTMLInputElement>) => handleActivityChange(index, 'displayOrder', event.target.value)}
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
                        <TableCell>Section</TableCell>
                        <TableCell>Item</TableCell>
                        <TableCell>Activity</TableCell>
                        <TableCell>Ordering</TableCell>
                        <TableCell align="right">Actions</TableCell>
                      </TableRow>
                    </TableHead>
                    <TableBody>
                      {config.itemActivities.map((mapping, index) => {
                        const sectionValue = mapping.sectionName || '';
                        const normalizedSection = sectionValue.trim();
                        const itemOptions = itemsBySection.get(normalizedSection) ?? [''];
                        return (
                          <TableRow key={index}>
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
                            <TableCell>
                              <TextField
                                fullWidth
                                size="small"
                                type="number"
                                value={mapping.displayOrder ?? 0}
                                onChange={(event: ChangeEvent<HTMLInputElement>) => handleItemActivityChange(index, 'displayOrder', event.target.value)}
                              />
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
          </Stack>
        )}
      </Paper>
    </Box>
  );
}
