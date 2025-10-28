using System;
using System.Collections.Generic;
using System.Linq;

namespace backend.Services.Estimation;

public static class ComplexityScorer
{
    public static (int fields, int integrations, int workflowSteps, bool hasUpload, bool hasAuthRole, bool hasCrudC, bool hasCrudR, bool hasCrudU, bool hasCrudD)
        ExtractSignals(string detail)
    {
        var text = (detail ?? string.Empty).ToLowerInvariant();
        int fields = Count(text, new[] { "field", "kolom", "input" });
        int integrations = Count(text, new[] { "integrasi", "api", "webhook", "gateway" });
        int workflow = Count(text, new[] { "approval", "review", "step", "tahap" });
        bool upload = text.Contains("upload", StringComparison.Ordinal);
        bool auth = text.Contains("role", StringComparison.Ordinal) || text.Contains("otorisasi", StringComparison.Ordinal) || text.Contains("permission", StringComparison.Ordinal);
        bool create = text.Contains("create", StringComparison.Ordinal) || text.Contains("tambah", StringComparison.Ordinal) || text.Contains("buat", StringComparison.Ordinal);
        bool read = text.Contains("read", StringComparison.Ordinal) || text.Contains("lihat", StringComparison.Ordinal) || text.Contains("daftar", StringComparison.Ordinal);
        bool update = text.Contains("update", StringComparison.Ordinal) || text.Contains("ubah", StringComparison.Ordinal) || text.Contains("edit", StringComparison.Ordinal);
        bool delete = text.Contains("delete", StringComparison.Ordinal) || text.Contains("hapus", StringComparison.Ordinal);
        return (fields, integrations, workflow, upload, auth, create, read, update, delete);
    }

    public static string PickSizeClass(double baseHours, (double xs, double s, double m, double l, double xl) bands, bool adjustCap)
    {
        var values = new[] { bands.xs, bands.s, bands.m, bands.l, bands.xl };
        var labels = new[] { "XS", "S", "M", "L", "XL" };
        var index = Array.FindIndex(values, v => baseHours <= v);
        if (index < 0)
        {
            index = values.Length - 1;
        }
        if (adjustCap && index > 2)
        {
            index = 2;
        }

        return labels[index];
    }

    private static int Count(string text, IEnumerable<string> keys)
    {
        return keys.Count(text.Contains);
    }
}
