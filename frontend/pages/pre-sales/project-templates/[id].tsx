import { useRouter } from 'next/router';
import TemplateEditorPage from '../../../components/pre-sales/TemplateEditorPage';

export default function EditProjectTemplatePage() {
  const router = useRouter();
  const { id } = router.query;

  if (!router.isReady) {
    return (
      <div className="page-container">
        <div className="card"><p>Loading template…</p></div>
      </div>
    );
  }

  const rawId = Array.isArray(id) ? id[0] : id;
  const parsedId = rawId ? Number(rawId) : NaN;

  if (Number.isNaN(parsedId)) {
    return (
      <div className="page-container">
        <div className="card"><p>Template ID tidak valid.</p></div>
      </div>
    );
  }

  return <TemplateEditorPage mode="edit" templateId={parsedId} />;
}
