import { useRouter } from 'next/router';
import { useState } from 'react';
import { Box, Button } from '@mui/material';
import RefreshIcon from '@mui/icons-material/Refresh';
import AssessmentHistory from '../../components/pre-sales/AssessmentHistory';

export default function PresalesAiHistoryPage() {
  const router = useRouter();
  const [refreshToken, setRefreshToken] = useState(0);

  return (
    <Box sx={{ maxWidth: 1200, mx: 'auto', py: 6, display: 'flex', flexDirection: 'column', gap: 3 }}>
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
          router.push({ pathname: '/pre-sales/workspace', query: { assessmentId: id } })
        }
      />
    </Box>
  );
}
