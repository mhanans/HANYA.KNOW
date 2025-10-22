import Link from 'next/link';
import { Box, Button, Card, CardContent, Stack, Typography } from '@mui/material';

export default function NotFound() {
  return (
    <Box sx={{ maxWidth: 720, mx: 'auto' }}>
      <Card>
        <CardContent>
          <Stack spacing={3} textAlign="center">
            <Typography variant="h1">Page Not Found</Typography>
            <Typography color="text.secondary">
              Sorry, we couldn't find the page you're looking for.
            </Typography>
            <Button component={Link} href="/" variant="contained" color="primary">
              Go Home
            </Button>
          </Stack>
        </CardContent>
      </Card>
    </Box>
  );
}
