import { useRouter } from 'next/router';
import { useState } from 'react';
import { Box, Button, Stack, Typography } from '@mui/material';
import RefreshIcon from '@mui/icons-material/Refresh';
import AssessmentHistory from '../../components/pre-sales/AssessmentHistory';

export default function PresalesAiHistoryPage() {
  const router = useRouter();
  const [refreshToken, setRefreshToken] = useState(0);

  return (
    <Box sx={{ maxWidth: 1200, mx: 'auto', py: 6 }}>
      <Stack spacing={4}>
        <Box>
          <Typography variant="h1" gutterBottom>Presales AI History</Typography>
          <Typography variant="body1" color="text.secondary">
            Browse saved assessments and relaunch them in the workspace when you need a fast head start.
          </Typography>
        </Box>
        <Box display="flex" justifyContent="flex-end">
          <Button
            variant="outlined"
            startIcon={<RefreshIcon />}
            onClick={() => setRefreshToken(token => token + 1)}
          >
            Refresh
          </Button>
        </Box>
        <AssessmentHistory
          refreshToken={refreshToken}
          onSelect={id =>
            router.push({ pathname: '/pre-sales/workspace', query: { jobId: id } })
          }
        />
      </Stack>
    </Box>
  );
}
