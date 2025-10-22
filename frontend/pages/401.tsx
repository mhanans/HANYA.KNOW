import { Box, Typography } from '@mui/material';

export default function Unauthorized() {
  return (
    <Box sx={{ textAlign: 'center', py: 10 }}>
      <Typography variant="h1">401 - Unauthorized</Typography>
      <Typography variant="body1" color="text.secondary" sx={{ mt: 2 }}>
        You do not have access to this page.
      </Typography>
    </Box>
  );
}
