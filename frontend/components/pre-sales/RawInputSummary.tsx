import React from 'react';
import {
  Paper,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Typography,
} from '@mui/material';

export interface TimelineEstimatorTeamTypeRole {
  id?: number;
  teamTypeId?: number;
  roleName: string;
  headcount: number;
}

export interface TimelineEstimatorTeamType {
  id?: number;
  name: string;
  minManDays: number;
  maxManDays: number;
  roles?: TimelineEstimatorTeamTypeRole[];
}

export interface TimelineEstimatorRawInput {
  activityManDays?: Record<string, number>;
  roleManDays?: Record<string, number>;
  totalRoleManDays?: number;
  durationsPerRole?: Record<string, number>;
  selectedTeamType?: TimelineEstimatorTeamType | null;
  durationAnchor?: number;
}

interface RawInputSummaryProps {
  rawInput: TimelineEstimatorRawInput;
}

const oneDecimal = new Intl.NumberFormat(undefined, {
  minimumFractionDigits: 1,
  maximumFractionDigits: 1,
});

const integerFormatter = new Intl.NumberFormat(undefined, {
  maximumFractionDigits: 0,
});

const formatDecimal = (value: unknown) => {
  const numberValue = typeof value === 'number' ? value : Number(value ?? NaN);
  if (!Number.isFinite(numberValue)) {
    return '—';
  }
  return oneDecimal.format(numberValue);
};

const formatInteger = (value: unknown) => {
  const numberValue = typeof value === 'number' ? value : Number(value ?? NaN);
  if (!Number.isFinite(numberValue)) {
    return '—';
  }
  return integerFormatter.format(numberValue);
};

const RawInputSummary: React.FC<RawInputSummaryProps> = ({ rawInput }) => {
  const activityEntries = Object.entries(rawInput.activityManDays ?? {}).sort((a, b) =>
    a[0].localeCompare(b[0]),
  );

  const roleManDays = rawInput.roleManDays ?? {};
  const durationsLookup = rawInput.durationsPerRole ?? {};
  const combinedRoleKeys = Array.from(
    new Set([
      ...Object.keys(roleManDays),
      ...Object.keys(durationsLookup),
    ]),
  ).sort((a, b) => {
    const durationDiff = (durationsLookup[b] ?? 0) - (durationsLookup[a] ?? 0);
    if (durationDiff !== 0) {
      return durationDiff;
    }
    return a.localeCompare(b);
  });

  const configuredHeadcounts = new Map<string, number>();
  (rawInput.selectedTeamType?.roles ?? []).forEach(role => {
    if (!role || typeof role.roleName !== 'string') {
      return;
    }
    const nameKey = role.roleName.trim().toLowerCase();
    if (!nameKey) {
      return;
    }
    const headcountValue = typeof role.headcount === 'number' ? role.headcount : Number(role.headcount ?? NaN);
    if (Number.isFinite(headcountValue)) {
      configuredHeadcounts.set(nameKey, headcountValue);
    }
  });

  const totalRoleManDays = Number.isFinite(rawInput.totalRoleManDays)
    ? Number(rawInput.totalRoleManDays)
    : combinedRoleKeys.reduce((sum, role) => {
        const value = roleManDays[role];
        return sum + (Number.isFinite(value) ? Number(value) : 0);
      }, 0);

  const teamTypeName = rawInput.selectedTeamType?.name?.trim() || 'Configured team';
  const durationAnchor = Number.isFinite(rawInput.durationAnchor)
    ? Math.max(0, Math.round(Number(rawInput.durationAnchor)))
    : 0;

  return (
    <Paper variant="outlined" sx={{ p: 3, borderRadius: 3 }}>
      <Typography variant="h6" gutterBottom>
        AI Input &amp; Rationale
      </Typography>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
        The AI was provided the following calculated data. Based on a total of{' '}
        <strong>{formatDecimal(totalRoleManDays)} man-days</strong>, the system selected the{' '}
        <strong>'{teamTypeName}'</strong> configuration. This led to a minimum duration bottleneck of{' '}
        <strong>{formatInteger(durationAnchor)} days</strong>, which was used as the primary anchor for AI planning.
      </Typography>

      <Stack direction={{ xs: 'column', md: 'row' }} spacing={2}>
        <TableContainer sx={{ flex: 1 }}>
          <Table size="small">
            <TableHead>
              <TableRow>
                <TableCell>Phase</TableCell>
                <TableCell align="right">Man-Days</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {activityEntries.map(([phase, value]) => (
                <TableRow key={phase}>
                  <TableCell>{phase}</TableCell>
                  <TableCell align="right">{formatDecimal(value)}</TableCell>
                </TableRow>
              ))}
              {activityEntries.length === 0 && (
                <TableRow>
                  <TableCell colSpan={2} align="center">
                    No activity effort data available.
                  </TableCell>
                </TableRow>
              )}
            </TableBody>
          </Table>
        </TableContainer>

        <TableContainer sx={{ flex: 1 }}>
          <Table size="small">
            <TableHead>
              <TableRow>
                <TableCell>Role</TableCell>
                <TableCell align="right">Configured Headcount</TableCell>
                <TableCell align="right">Total Man-Days</TableCell>
                <TableCell align="right">Bottleneck Duration (days)</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {combinedRoleKeys.map(roleName => {
                const key = roleName.trim().toLowerCase();
                const headcount = configuredHeadcounts.get(key);
                const manDays = roleManDays[roleName];
                const duration = durationsLookup[roleName];

                return (
                  <TableRow key={roleName}>
                    <TableCell>{roleName}</TableCell>
                    <TableCell align="right">{formatDecimal(headcount)}</TableCell>
                    <TableCell align="right">{formatDecimal(manDays)}</TableCell>
                    <TableCell align="right">{formatInteger(duration)}</TableCell>
                  </TableRow>
                );
              })}
              {combinedRoleKeys.length === 0 && (
                <TableRow>
                  <TableCell colSpan={4} align="center">
                    No role effort data available.
                  </TableCell>
                </TableRow>
              )}
            </TableBody>
          </Table>
        </TableContainer>
      </Stack>
    </Paper>
  );
};

export default RawInputSummary;
