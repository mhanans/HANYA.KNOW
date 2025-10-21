import Head from 'next/head';

const sqlSchema = `-- Menyimpan definisi dari setiap template
CREATE TABLE ProjectTemplates (
    Id INT PRIMARY KEY IDENTITY,
    TemplateName NVARCHAR(255) NOT NULL,
    -- Seluruh struktur template (seksi, item, kolom) disimpan sebagai JSON
    TemplateData NVARCHAR(MAX) NOT NULL, 
    CreatedByUserId INT,
    CreatedAt DATETIME2 DEFAULT GETUTCDATE(),
    LastModifiedAt DATETIME2
);

-- Menyimpan hasil akhir dari setiap asesmen yang dibuat
CREATE TABLE ProjectAssessments (
    Id INT PRIMARY KEY IDENTITY,
    ProjectId INT, -- Opsional, bisa di-link ke project lain
    TemplateId INT FOREIGN KEY REFERENCES ProjectTemplates(Id),
    -- Seluruh hasil asesmen (item terpilih, estimasi final) disimpan sebagai JSON
    AssessmentData NVARCHAR(MAX) NOT NULL, 
    CreatedByUserId INT,
    CreatedAt DATETIME2 DEFAULT GETUTCDATE(),
    LastModifiedAt DATETIME2
);`;

const knowledgeBaseSql = `CREATE TABLE KnowledgeBaseDocuments (
    Id INT PRIMARY KEY IDENTITY,
    OriginalFileName NVARCHAR(255) NOT NULL,
    StoragePath NVARCHAR(1024) NOT NULL,
    ProjectName NVARCHAR(255) NULL,
    DocumentType NVARCHAR(100) NULL,
    ProjectCompletionDate DATE NULL,
    ProcessingStatus NVARCHAR(50) NOT NULL,
    ErrorMessage NVARCHAR(MAX) NULL,
    UploadedByUserId INT NOT NULL,
    UploadedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE()
);`;

const dtoCode = `// Untuk Template
public class ProjectTemplate {
    public int? Id { get; set; }
    public string TemplateName { get; set; }
    public List<string> EstimationColumns { get; set; }
    public List<TemplateSection> Sections { get; set; }
}
public class TemplateSection {
    public string SectionName { get; set; }
    public string Type { get; set; } // "Project-Level" atau "App-Level"
    public List<TemplateItem> Items { get; set; }
}
public class TemplateItem {
    public string ItemId { get; set; }
    public string ItemName { get; set; }
    public string ItemDetail { get; set; }
}

// Untuk Asesmen
public class ProjectAssessment {
    public int? Id { get; set; }
    public int TemplateId { get; set; }
    public List<AssessmentSection> Sections { get; set; }
}
public class AssessmentSection {
    public string SectionName { get; set; }
    public List<AssessmentItem> Items { get; set; }
}
public class AssessmentItem {
    public string ItemId { get; set; }
    public bool IsNeeded { get; set; }
    public string ItemName { get; set; }
    // Dictionary untuk menangani kolom estimasi yang dinamis
    public Dictionary<string, double?> Estimates { get; set; }
}`;

const vectorMetadata = `{
  "content": "Item Development: General Pop-up Notification",
  "metadata": {
    "assessment_id": 101,
    "template_id": 1,
    "item_id": "ID-01",
    "item_category": "Item Development",
    "item_type": "App-Level",
    "final_estimates": {
      "Requirement": 3,
      "SIT": 2,
      "BE": 8,
      "FE": 6
    }
  }
}`;

const knowledgeBaseMetadata = `{
  "content": "The pop-up notification must adhere to the brand guidelines specified in Appendix A and must be dismissible by the user...",
  "metadata": {
    "source_type": "knowledge_base",
    "document_id": 456,
    "document_name": "Project_Phoenix_FSD_v2.pdf",
    "project_name": "Project Phoenix",
    "document_type": "Functional Specification (FSD)",
    "page_number": 27
  }
}`;

export default function AiProjectAssessmentEngine() {
  return (
    <div className="page-container">
      <Head>
        <title>Pre-Sales · AI Project Assessment Engine</title>
      </Head>
      <div className="page-header">
        <div>
          <h1>AI Project Assessment Engine</h1>
          <p>Blueprint komprehensif untuk modul asesmen proyek berbasis AI.</p>
        </div>
        <span className="status-badge pass">Pre-Sales</span>
      </div>

      <section className="card">
        <h2>Ringkasan Eksekutif &amp; Visi</h2>
        <p>
          Dokumen ini mendefinisikan platform <strong>AI Project Assessment Engine</strong> sebagai sumber kebenaran
          tunggal bagi desainer, developer, dan project manager. Sistem mengubah estimasi proyek dari proses manual
          menjadi alur kerja cerdas yang konsisten dan dapat diskalakan.
        </p>
        <p>
          Visi utama adalah menciptakan platform yang menangkap pengetahuan institusional dan meningkat akurasinya seiring
          waktu melalui pembelajaran berkelanjutan dari asesmen yang selesai.
        </p>
      </section>

      <section className="card">
        <h2>Bagian 1 · Spesifikasi UI/UX &amp; Fungsional</h2>
        <p>
          Bagian ini memetakan halaman yang dibutuhkan, komponen utama, dan ekspektasi pengalaman pengguna.
        </p>
        <article>
          <h3>UI 1 · Halaman Admin - Manajemen Project Template</h3>
          <ul>
            <li><strong>Tujuan:</strong> CRUD penuh untuk <code>Project Template</code>.</li>
            <li>
              <strong>Data Grid:</strong> Kolom <em>Template Name</em>, <em>Created By</em>, <em>Last Modified</em>, dan aksi,
              lengkap dengan paginasi, pencarian, dan sortir.
            </li>
            <li>
              <strong>Aksi:</strong> tombol <em>Create</em>, <em>Edit</em>, dan <em>Delete</em> dengan modal konfirmasi.
            </li>
            <li>
              <strong>Model Data:</strong>
              <pre><code>{`public class ProjectTemplateMetadata {
    public int Id { get; set; }
    public string TemplateName { get; set; }
    public string CreatedBy { get; set; }
    public DateTime LastModified { get; set; }
}`}</code></pre>
            </li>
          </ul>
        </article>
        <article>
          <h3>UI 2 · Halaman Admin - Template Editor</h3>
          <ul>
            <li><strong>Tujuan:</strong> Editor visual untuk struktur template lengkap.</li>
            <li>
              <strong>Komponen:</strong> field nama template, <em>tag input</em> kolom estimasi dengan drag &amp; drop,
              serta <em>tree editor</em> hierarki seksi dan item.
            </li>
            <li>
              <strong>Fungsional:</strong> dukung reorder, penandaan perubahan belum disimpan, dan modal konfirmasi saat keluar.
            </li>
            <li><strong>Data:</strong> memuat dan menyimpan objek <code>ProjectTemplate</code> penuh.</li>
          </ul>
        </article>
        <article>
          <h3>UI 3 · Workspace Asesmen Proyek</h3>
          <ul>
            <li>
              <strong>Inisiasi:</strong> pilih template, unggah dokumen scope, lalu mulai analisis AI dengan indikator progres
              langsung.
            </li>
            <li>
              <strong>Assessment Tree Grid:</strong> struktur hirarki yang dapat dilipat dengan kolom estimasi dinamis, checkbox
              <em>Needed?</em>, input numerik in-line, dan perhitungan total otomatis.
            </li>
            <li>
              <strong>Area Aksi:</strong> simpan asesmen dan ekspor ke Excel setelah penyimpanan berhasil.
            </li>
          </ul>
        </article>
        <article>
          <h3>UI 4 · Admin &ndash; Presales AI History</h3>
          <ul>
            <li>
              <strong>Tujuan:</strong> pusat unggah dan kurasi dokumen historis (mis. SOW, asesmen terdahulu) yang menjadi
              memori jangka panjang AI.
            </li>
            <li>
              <strong>Navigasi:</strong> tampil di bawah menu <em>Admin/Settings</em> berdampingan dengan <em>Template Management</em>.
            </li>
            <li>
              <strong>Zona Unggah:</strong> drag-and-drop modern dengan dukungan batch, menerima format <code>.pdf</code> dan
              menampilkan daftar staging (nama, ukuran, status) sebelum diproses.
            </li>
            <li>
              <strong>Form Metadata Kontekstual:</strong> per dokumen dapat menambahkan <em>Project Name</em> (input teks) dan
              <em>Client Type</em> (dropdown &ndash; CR, New Application, dll) untuk memperkaya vektor.
            </li>
            <li>
              <strong>Primary Action:</strong> tombol <em>Process &amp; Add to Knowledge Base</em> yang men-disable saat proses
              berjalan dan menampilkan indikator loading.
            </li>
            <li>
              <strong>Data Grid Dokumen Terkelola:</strong> kolom <em>File Name</em>, <em>Project Name</em>, <em>Document Type</em>,
              <em>Date Processed</em>, <em>Status</em>, dan <em>Actions</em> dengan badge status <em>Processing</em>, <em>Successfully
              Indexed</em>, atau <em>Failed</em>. Tooltip error muncul untuk status gagal.
            </li>
            <li>
              <strong>Aksi:</strong> ikon <em>Delete</em> (konfirmasi modal, menghapus vektor terkait) dan <em>Re-process</em> untuk
              mengantre ulang dokumen yang gagal.
            </li>
            <li>
              <strong>UX:</strong> proses non-blocking, feedback instan, serta pesan error ramah pengguna seperti
              &ldquo;File format not supported&rdquo; atau &ldquo;Document appears to be corrupted&rdquo;.
            </li>
          </ul>
        </article>
      </section>

      <section className="card">
        <h2>Bagian 2 · Arsitektur Teknis &amp; Alur Agen</h2>
        <ol>
          <li><strong>Agent 1 – Document Ingestion:</strong> ekstraksi teks terstruktur dari file unggahan.</li>
          <li><strong>Agent 2 – Template Matching:</strong> menentukan <code>is_needed</code> untuk tiap item template.</li>
          <li><strong>Agent 3 – Hierarchical Estimation &amp; RAG:</strong> menghasilkan estimasi per kolom berbasis data historis.</li>
          <li><strong>Agent 4 – Feedback &amp; Refinement:</strong> menyimpan hasil akhir ke Vector DB untuk pembelajaran.</li>
        </ol>
        <h3>Endpoint API</h3>
        <ul>
          <li><code>GET /api/templates</code> &amp; <code>GET /api/templates/{'{id}'}</code> untuk metadata dan detail template.</li>
          <li>
            <code>POST /api/templates</code>, <code>PUT /api/templates/{'{id}'}</code>, <code>DELETE /api/templates/{'{id}'}</code>.
          </li>
          <li><code>POST /api/assessment/analyze</code> memicu orkestrasi agen dan mengembalikan <code>ProjectAssessment</code>.</li>
          <li><code>POST /api/assessment/save</code> menyimpan perubahan asesmen.</li>
          <li><code>GET /api/assessment/{'{id}'}/export</code> menghasilkan Excel.</li>
          <li><code>POST /api/knowledge-base/upload</code> menerima file + metadata, menyimpan sementara, dan mengantre job asinkron (HTTP 202).</li>
          <li><code>GET /api/knowledge-base/documents</code> memuat data grid dokumen yang sudah diproses.</li>
          <li><code>DELETE /api/knowledge-base/documents/{'{id}'}</code> menghapus dokumen dan menjadwalkan penghapusan vektor.</li>
        </ul>
        <h3>Alur Asinkron Knowledge Base</h3>
        <ol>
          <li><strong>Enqueue:</strong> endpoint upload menyimpan file ke storage sementara, membuat entri <em>Pending</em> di
            <code>KnowledgeBaseDocuments</code>, lalu mengantre job per dokumen (Hangfire/Quartz).</li>
          <li><strong>Agent KB-1 &ndash; Ingestion &amp; Chunking:</strong> mengambil file, mengekstrak konten (mis. iTextSharp untuk
            PDF), lalu membagi menjadi chunk 500-1000 token dengan menjaga batas alami (paragraf, tabel).</li>
          <li><strong>Agent KB-2 &ndash; Embedding &amp; Indexing:</strong> membuat embedding (Gemini/LLM), menyimpan ke Vector DB dengan
            metadata kaya, dan memperbarui status menjadi <em>Successfully Indexed</em> atau <em>Failed</em> dengan pesan error.</li>
        </ol>
        <p>
          Agent 3 pada alur asesmen utama kini melakukan retrieval gabungan: histori asesmen terstruktur serta <em>knowledge base</em>
          dokumen untuk konteks yang lebih kaya.
        </p>
      </section>

      <section className="card">
        <h2>Bagian 3 · Model Data &amp; Skema</h2>
        <article>
          <h3>1. SQL Database Schema</h3>
          <pre><code>{`${sqlSchema}

-- Menyimpan riwayat dokumen knowledge base
${knowledgeBaseSql}`}</code></pre>
        </article>
        <article>
          <h3>2. C# DTOs</h3>
          <pre><code>{dtoCode}</code></pre>
        </article>
        <article>
          <h3>3. Vector Database Metadata</h3>
          <pre><code>{`${vectorMetadata}

-- Metadata khusus knowledge base
${knowledgeBaseMetadata}`}</code></pre>
        </article>
      </section>

      <section className="card">
        <h2>Ikhtisar Navigasi</h2>
        <p>
          Modul ini ditambahkan di grup menu <strong>Pre-Sales</strong>. Pastikan role yang memerlukan akses memiliki izin
          <code>pre-sales-ai-assessment</code> pada pengaturan Role UI.
        </p>
      </section>
    </div>
  );
}
