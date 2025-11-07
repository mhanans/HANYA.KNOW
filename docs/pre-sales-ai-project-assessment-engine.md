# Pre-Sales Blueprint · AI Project Assessment Engine

Blueprint ini menjadi referensi tunggal untuk tim Pre-Sales dalam mengimplementasikan modul asesmen proyek berbasis AI. Dokumen menguraikan secara terpisah spesifikasi UI, arsitektur teknis, dan model data.

## 1. Ringkasan Eksekutif & Visi
- Mentransformasikan estimasi proyek dari aktivitas manual menjadi alur kerja cerdas yang konsisten.
- Menangkap dan menskalakan pengetahuan institusional sehingga akurasi estimasi meningkat seiring waktu.

## 2. Spesifikasi UI/UX & Fungsional

### UI 1 — Admin: Manajemen Project Template
- **Tujuan:** CRUD penuh untuk `Project Template`.
- **Komponen:**
  - Data grid dengan kolom *Template Name*, *Created By*, *Last Modified*, dan kolom aksi (paginasi, pencarian, sortir).
  - Tombol *Create New Template* menuju Template Editor.
  - Tombol *Edit* dan *Delete* dengan modal konfirmasi.
- **Model Data:**
  ```csharp
  public class ProjectTemplateMetadata {
      public int Id { get; set; }
      public string TemplateName { get; set; }
      public string CreatedBy { get; set; }
      public DateTime LastModified { get; set; }
  }
  ```

### UI 2 — Admin: Template Editor
- **Tujuan:** Editor visual untuk membangun struktur template lengkap.
- **Komponen:**
  - Field nama template (wajib).
  - Input kolom estimasi dinamis berbasis tag/multi-select dengan drag-and-drop urutan.
  - Editor pohon untuk seksi dan item (mendukung drag-and-drop, input Section Name, Type, Item ID/Name/Detail).
- **Fungsional:** penandaan perubahan belum disimpan, konfirmasi ketika keluar tanpa menyimpan.
- **Data:** objek `ProjectTemplate` penuh dalam JSON.

### UI 3 — Workspace Asesmen Proyek
- **Tujuan:** Ruang kerja utama untuk menjalankan analisis AI dan menyempurnakan asesmen.
- **Komponen:**
  - Area inisiasi (pilih template, unggah scope document, mulai analisis).
  - Indikator progres AI real-time.
  - *Assessment Tree Grid* dengan seksi collapsible, kolom `Needed?`, kolom estimasi dinamis, input numerik in-line, total otomatis per baris/seksi/grand total.
  - Tombol *Save Assessment* dan *Export to Excel*.

## 3. Arsitektur Teknis & Alur Agen
1. **Agent 1 — Document Ingestion:** mengubah file unggahan menjadi teks terstruktur.
2. **Agent 2 — Template Matching & Applicability:** menentukan `is_needed` per item template.
3. **Agent 3 — Timeline Estimator:** merangkum hasil asesmen menjadi estimasi timeline terstruktur sebelum diteruskan ke generator timeline.
4. **Agent 4 — Timeline Generation:** menyusun urutan fase proyek berdasarkan output estimator, termasuk fase paralel atau bertumpuk.
5. **Agent 5 — Estimated Cost Generation:** menghitung biaya dengan mempertimbangkan jadwal final dan kebutuhan peran.
6. **Agent 6 — Feedback & Refinement (async):** menyimpan hasil final ke Vector DB sebagai pembelajaran.

### 3.1 Timeline Estimator Service
- **Input:** hasil asesmen dari Workspace (scope dan kebutuhan per item).
- **Proses:**
  - Membaca tabel referensi `timeline_estimator_reference` dengan struktur berikut: `Id`, `ProjectScale (Long|Medium|Short)`, `DurationPerPhase` (JSON detail setiap fase), `TotalDuration`, `ResourcePerRole` (Dev, PM, BA).
  - Menggunakan kemiripan karakteristik (skala proyek, kompleksitas item, kebutuhan resource) untuk menyusun estimasi timeline awal.
- **Output:** tabel estimasi baru dengan format yang sama (`Project`, `ProjectScale`, `DurationPerPhase`, `TotalDuration`, `ResourcePerRole`). Total durasi tidak harus sama dengan penjumlahan tiap fase karena fase dapat berjalan paralel atau tumpang tindih.
- **Catatan:** timeline generator wajib memakai output ini; asesmen tidak boleh langsung melompat ke timeline generation tanpa proses estimator.

### Endpoint API
- `GET /api/templates` dan `GET /api/templates/{id}`
- `POST /api/templates`, `PUT /api/templates/{id}`, `DELETE /api/templates/{id}`
- `POST /api/assessment/analyze`
- `POST /api/assessment/save`
- `GET /api/assessment/{id}/export`

## 4. Model Data & Skema

### 4.1 SQL Database
```sql
-- Menyimpan definisi dari setiap template
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
);
```

### 4.2 DTO C#
```csharp
// Untuk Template
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
}
```

### 4.3 Metadata Vector Database
```json
{
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
}
```

## 5. Hak Akses & Navigasi
- Modul tersedia pada grup menu **Pre-Sales** dengan label *Project Templates*, *Assessment Workspace*, dan *Presales AI History*.
- Gunakan key `pre-sales-project-templates`, `pre-sales-assessment-workspace`, dan `admin-presales-history` di pengaturan Role UI untuk memberikan akses sesuai kebutuhan.

## 6. Implementasi Modul

### 6.1 Halaman Pre-Sales
- **Project Templates (`/pre-sales/project-templates`)** – data grid CRUD untuk metadata template dengan tombol *Create*, *Edit*, dan *Delete*.
- **Template Editor (`/pre-sales/project-templates/[id]`)** – editor visual berbasis tree dengan dukungan reorder kolom dan item, guard perubahan belum tersimpan, serta aksi *Save*/*Cancel*.
- **Assessment Workspace (`/pre-sales/workspace`)** – ruang kerja utama untuk menjalankan analisis AI, menandai item yang diperlukan, mengisi estimasi numerik, menyimpan perubahan, dan mengekspor hasil ke Excel.
- **Presales AI History (`/pre-sales/presales-ai-history`)** – tabel ringkasan asesmen untuk membuka atau menghapus hasil sebelumnya.
- **Blueprint (`docs/pre-sales-ai-project-assessment-engine.md`)** – dokumentasi produk tersedia dalam repositori untuk referensi cepat tim Pre-Sales.

### 6.2 Endpoint Backend
- `GET /api/templates` – daftar metadata template untuk dropdown dan grid administrasi.
- `GET /api/templates/{id}` – mengambil struktur template lengkap untuk editor.
- `POST /api/templates`, `PUT /api/templates/{id}`, `DELETE /api/templates/{id}` – operasi CRUD template dengan audit `created_by` dan timestamp.
- `POST /api/assessment/analyze` – men-generate objek `ProjectAssessment` awal berdasarkan template dan dokumen scope.
- `POST /api/assessment/save` – menyimpan asesmen (insert/update) dalam tabel `project_assessments`.
- `GET /api/assessment/{id}/export` – menghasilkan file Excel (`.xlsx`) dengan total per item, seksi, dan grand total.

### 6.3 Skema Database
- **project_templates** – menyimpan nama template, JSON struktur (`template_data`), serta metadata `created_by_user_id`, `created_at`, `last_modified_at`.
- **project_assessments** – menyimpan hasil asesmen dalam JSON (`assessment_data`) dengan relasi ke template, metadata pengguna/timestamp, serta kolom indeks `project_name` dan `status` untuk kebutuhan pelaporan.

