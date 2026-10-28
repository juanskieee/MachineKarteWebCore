// Models/RawDataModels.cs
namespace MachineKarte.Web.Models
{
    // ── Query parameters passed from the calling page ──────────────────────────
    public class RawDataRequest
    {
        public string Process { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string Machine { get; set; } = string.Empty;
        public string DateFrom { get; set; } = string.Empty;
        public string DateTo { get; set; } = string.Empty;
    }

    // ── Frequency-recurrence table (DataGridView2 / Chart1) ────────────────────
    /// <summary>
    /// One row = one month/frequency bucket, e.g. "3x | April 2024 | 5 occurrences".
    /// </summary>
    public class FrequencyRow
    {
        public string Frequency { get; set; } = string.Empty;   // "2x","3x",…
        public string DateLabel { get; set; } = string.Empty;   // "April 2024"
        public int DateMonth { get; set; }
        public int DateYear { get; set; }
        public int Total { get; set; }
    }

    // ── BM-case-by-month table (DataGridView1 / Chart2) ────────────────────────
    /// <summary>
    /// One row = one BMCase in one month, e.g. "Sensor Fault | April | 2024 | 7".
    /// </summary>
    public class BmCaseRow
    {
        public string BMCase { get; set; } = string.Empty;
        public int BMCount { get; set; }
        public string DateMonth { get; set; } = string.Empty;   // month name
        public int DateNum { get; set; }                   // 1-12
        public int DateYear { get; set; }
    }

    // ── Chart series helpers ────────────────────────────────────────────────────
    public class ChartSeries
    {
        public string Name { get; set; } = string.Empty;
        public List<ChartPoint> Points { get; set; } = new();
    }

    public class ChartPoint
    {
        public string Label { get; set; } = string.Empty;
        public int Value { get; set; }
    }

    // ── Combined response ───────────────────────────────────────────────────────
    public class RawDataResult
    {
        public List<FrequencyRow> FrequencyTable { get; set; } = new();
        public List<BmCaseRow> BmCaseTable { get; set; } = new();
        public List<ChartSeries> FrequencyChart { get; set; } = new();   // Chart1
        public List<ChartSeries> BmCaseChart { get; set; } = new();   // Chart2
    }
}