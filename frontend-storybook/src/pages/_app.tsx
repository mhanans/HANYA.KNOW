import type { AppProps } from 'next/app';
import '../stories/appLayout.css';
import '../stories/chatInput.css';
import '../stories/categorySelect.css';
import '../stories/sidebar.css';
import '../stories/header.css';
import '../stories/fileUpload.css';
import '../stories/menu.css';
import '../stories/page.css';
import '../stories/chatInterface.css';
import '../stories/chatMessage.css';
import Layout from '../components/Layout';

export default function App({ Component, pageProps }: AppProps) {
  return (
    <Layout>
      <Component {...pageProps} />
    </Layout>
  );
}
