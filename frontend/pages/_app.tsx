import { CssBaseline, ThemeProvider } from '@mui/material';
import type { AppProps } from 'next/app';
import 'sweetalert2/dist/sweetalert2.min.css';
import GlobalStyles from '../components/GlobalStyles';
import Layout from '../components/Layout';
import theme from '../lib/theme';

export default function App({ Component, pageProps }: AppProps) {
  return (
    <ThemeProvider theme={theme}>
      <CssBaseline />
      <GlobalStyles />
      <Layout>
        <Component {...pageProps} />
      </Layout>
    </ThemeProvider>
  );
}
