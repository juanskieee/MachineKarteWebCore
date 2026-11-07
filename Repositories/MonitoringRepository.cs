using MachineKarte.Web.Services;
// Monitoring reads mapped from Form1.vb and uc_Test.vb.
using System.Data;
using System.Globalization;
using MachineKarte.Web.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;

namespace MachineKarte.Web.Repositories;

public class MonitoringRepository
{
    private readonly DatabaseConnectionResolver _connections;
    private readonly IMemoryCache? _cache;

    public MonitoringRepository(DatabaseConnectionResolver connections, IMemoryCache? cache = null) =>
        (_connections, _cache) = (connections, cache);
    // Helpers

    /// <summary>
    /// Maps location index (1-9) from Panel2 label name suffix to floor code.
    /// Mirrors checkProcessAndLocation() in Form1.vb.
    /// </summary>
    public static string LocationIndexToCode(int idx) => idx switch
    {
        1 => "LSI1-1F",
        2 => "LSI1-2F",
        3 => "LSI2-1F",
        4 => "LSI2-2F",
        5 => "LSI2-3F",
        6 => "LSI3-2F",
        7 => "LSI3-4F",
        8 => "LSI3-3F",
        9 => "LSI3-1F",
        _ => string.Empty
    };

    /// <summary>
    /// Returns the floor list relevant for a given process.
    /// Mirrors strLocation() array population in LoadMCName1().
    /// </summary>
    public static string[] ProcessFloors(string process) => process.ToUpper() switch
    {
        "DC" or "DB" or "WB"
            => new[] { "LSI1-1F", "LSI2-1F", "LSI2-2F", "LSI2-3F", "LSI3-2F" },
        "MP"
            => new[] { "LSI1-1F", "LSI2-1F", "LSI2-2F", "LSI2-3F", "LSI3-1F" },
        "PL"
            => new[] { "LSI1-1F", "LSI2-1F", "LSI3-1F" },
        "FL" or "FT" or "TP"
            => new[] { "LSI1-2F", "LSI2-1F", "LSI2-2F", "LSI2-3F", "LSI3-3F" },
        "SING" or "ATC"
            => new[] { "LSI1-2F", "LSI1-1F", "LSI2-1F", "LSI2-2F", "LSI2-3F", "LSI3-3F" },
        "EDS"
            => new[] { "LSI3-4F" },
        "MEMS"
            => new[] { "LSI3-2F" },
        _ => new[] { "LSI1-1F", "LSI1-2F", "LSI2-1F", "LSI2-2F",
                       "LSI2-3F", "LSI3-2F", "LSI3-4F", "LSI3-1F", "LSI3-3F" }
    };
    // Dropdown helpers
    /// <summary>
    /// Gets distinct machine numbers for cbMachine dropdown.
    /// Mirrors cbFloor_SelectedIndexChanged in Form1.vb.
    /// </summary>
    public async Task<List<string>> GetMachinesAsync(
        string userType, string process, string floor, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var cacheKey = (typeof(MonitoringRepository), _connections, userType is "PM" or "CC" or "EEE" or "VIS", process, floor);
        if (_cache?.TryGetValue(cacheKey, out string[]? cached) == true)
            return cached!.ToList();
        var results = new List<string>();
        await using var conn = await _connections.OpenConnectionAsync(cancellationToken);

        string sql;
        string colName;

        if (userType is "PM" or "CC" or "EEE" or "VIS")
        {
            sql = @"SELECT DISTINCT a.MCNo AS Val
                    FROM [eMaintenanceSystemDB].[dbo].[MCList] a
                    INNER JOIN [eMaintenanceSystemDB].[dbo].[Process] b
                        ON a.ProcessID = b.ProcessID
                    WHERE b.ProcessName = @Process
                      AND a.MCLocation = @Location
                    ORDER BY a.MCNo ASC";
            colName = "Val";
        }
        else // TE
        {
            sql = @"SELECT DISTINCT TesterNo AS Val
                    FROM [eMaintenanceSystemDB].[dbo].[TEBMMaintenance]
                    WHERE Process   = @Process
                      AND Location = @Location
                      AND TesterNo <> ''
                    ORDER BY TesterNo ASC";
            colName = "Val";
        }

        // Direct repository callers retain their legacy All-floor behavior.
        if (floor == "All")
            sql = sql.Replace("AND a.MCLocation = @Location", "").Replace("AND Location = @Location", "");
        await using var cmd = new SqlCommand(sql, conn);
        RepositorySql.AddVarChar(cmd, "@Process", process, 20);
        RepositorySql.AddVarChar(cmd, "@Location", floor, 20);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            results.Add(reader[colName].ToString()!);

        cancellationToken.ThrowIfCancellationRequested();
        _cache?.Set(cacheKey, results.ToArray(), TimeSpan.FromSeconds(30));
        return results;
    }
    // Paged machine summary (drives card column headers)
    // Mirrors LoadMCName() strQuery2 � PM/CC/EEE/VIS and TE branches
    public async Task<(List<MachineSummary> Machines, int TotalPages)> GetPagedMachineSummariesAsync(
        MonitoringFilter f, CancellationToken cancellationToken = default, bool allPages = false)
    {
        ValidateProcess(f.UserType, f.Process);
        int top2 = allPages ? int.MaxValue : f.Page * 10;
        int top1 = allPages ? 0 : (f.Page * 10) - 10;

        string addQuery = BuildUserFilter(f.UserType);
        bool isMachine = f.UserType is "PM" or "CC" or "EEE" or "VIS";

        string sql = isMachine
            ? BuildMachineSummarySQL(f.Process, addQuery, top1, top2, f.Machine == "All")
            : BuildTEMachineSummarySQL(addQuery, top1, top2, f.Machine == "All");

        var list = new List<MachineSummary>();
        await using var conn = await _connections.OpenConnectionAsync(cancellationToken);
        await using var cmd = new SqlCommand(sql, conn);

        RepositorySql.AddDateRange(cmd, f.DateFrom, f.DateTo);
        RepositorySql.AddVarChar(cmd, "@Location", f.Floor, 20);
        RepositorySql.AddVarChar(cmd, "@Process", f.Process, 20);
        if (f.Machine != "All")
            RepositorySql.AddVarChar(cmd, "@Mcno", f.Machine, 50);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new MachineSummary
            {
                MachineNo = isMachine
                              ? reader["MCNO"].ToString()!
                              : reader["testerno"].ToString()!,
                MachineType = reader["MCType"].ToString()!,
                BMCount = Convert.ToInt32(reader["MACHINENAME"]),
                ActionLine = reader["actionline"] is DBNull
                              ? 4
                              : Convert.ToInt32(reader["actionline"])
            });
        }
        await reader.NextResultAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var totalMachines = reader.GetInt32(0);
        return (list, Math.Max(1, (int)Math.Ceiling(totalMachines / 10d)));
    }

    private static void ApplyLegacyRepeatColors(
        List<BMCardRow> cards,
        string userType,
        string process)
    {
        for (var start = 0; start < cards.Count; start++)
        {
            var current = cards[start];
            var uncolored = string.Equals(current.FontColor, "None", StringComparison.OrdinalIgnoreCase);
            var lastInMachine = start + 1 == cards.Count ||
                (current.MachineNo, current.MachineType, current.Location) !=
                (cards[start + 1].MachineNo, cards[start + 1].MachineType, cards[start + 1].Location);
            // Legacy skips already-colored group members, except its final-row branch.
            current.McdNeedsAction = current.IsMCD == "Yes" && current.HaveAction != "Yes" &&
                (uncolored || lastInMachine);
            if (!uncolored)
                continue;

            if (userType.Equals("TE", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(cards[start].Priority, "QUALITY BM", StringComparison.Ordinal))
                    continue;

                var matchingCase = ApplyRepeatColor(
                    cards,
                    start,
                    4,
                    card => card.Priority,
                    "Red");
                if (matchingCase) current.McdNeedsAction = false;
                continue;
            }

            ApplyRepeatColor(
                cards,
                start,
                5,
                card => card.BMContentId,
                GetLegacyContentColor(process, cards[start].BMContentId));
        }
    }

    private static bool ApplyRepeatColor(
        List<BMCardRow> cards,
        int start,
        int maximumDepth,
        Func<BMCardRow, string> repeatedValue,
        string color)
    {
        var contentDepth = MatchingDepth(cards, start, maximumDepth, repeatedValue);
        if (contentDepth == 0)
            return false;

        var caseDepth = MatchingDepth(cards, start, contentDepth, card => card.BMCase);
        if (caseDepth == 0)
            return false;

        MarkWithinSevenDays(cards, start, caseDepth, color);
        // uc_Test increments the counter at the detected group head, before moving to the next group.
        var head = cards[start];
        head.NeedsAction = head.FontColor != "None" && color != "Black" &&
            head.HaveAction != "Yes" &&
            !string.Equals(head.MCStatus, "NO RE-OCCURRENCE AFTER 7 DAYS", StringComparison.OrdinalIgnoreCase);
        if (head.NeedsAction) head.McdNeedsAction = false;
        return true;
    }

    private static int MatchingDepth(
        List<BMCardRow> cards,
        int start,
        int maximumDepth,
        Func<BMCardRow, string> valueSelector)
    {
        var depth = 0;
        for (var offset = 1; offset <= maximumDepth && start + offset < cards.Count; offset++)
        {
            if (!string.Equals(cards[start].MachineNo, cards[start + offset].MachineNo,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(cards[start].MachineType, cards[start + offset].MachineType,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(cards[start].Location, cards[start + offset].Location,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(valueSelector(cards[start]), valueSelector(cards[start + offset]),
                    StringComparison.Ordinal))
                break;

            depth = offset;
        }

        return depth;
    }

    private static void MarkWithinSevenDays(
        List<BMCardRow> cards,
        int start,
        int depth,
        string color)
    {
        if (!TryParseLegacyDate(cards[start].TimeOfRequest, out var currentDate))
            return;

        for (var offset = 1; offset <= depth; offset++)
        {
            if (!TryParseLegacyDate(cards[start + offset].TimeOfRequest, out var nextDate))
                continue;

            var days = (currentDate - nextDate).Days;
            if (days < 0 || days > 7)
                continue;

            if (offset == 1)
                cards[start].FontColor = color;

            cards[start + offset].FontColor = color;
        }
    }

    private static bool TryParseLegacyDate(string value, out DateTime date)
    {
        return DateTime.TryParseExact(
            value,
            "MM/dd/yyyy HH:mm:ss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);
    }

    private static string GetLegacyContentColor(string process, string contentId)
    {
        if (!int.TryParse(contentId, out var id)) return "Black";

        var ids = process.ToUpperInvariant() switch
        {
            "EDS" => (Blue: 13, Red: 16),
            "DC" => (Blue: 48, Red: 49),
            "DB" => (Blue: 18, Red: 19),
            "WB" => (Blue: 45, Red: 46),
            "MP" => (Blue: 24, Red: 25),
            "PL" => (Blue: 42, Red: 43),
            "FL" or "SING" or "ATC" => (Blue: 1, Red: 2),
            "FT" => (Blue: 31, Red: 33),
            "TP" => (Blue: 9, Red: 30),
            "MEMS" => (Blue: 72, Red: 74),
            _ => (Blue: -1, Red: -2)
        };

        return id == ids.Blue ? "Blue" : id == ids.Red ? "Red" : "Black";
    }
    // BM card rows per machine (drives uC_Test card content)
    // Mirrors LoadMCName1() strQuery
    public async Task<List<BMCardRow>> GetBMCardsAsync(
        string userType, string process, string location,
        string machineNo, string dateFrom, string dateTo, string machineType = "",
        CancellationToken cancellationToken = default)
    {
        ValidateProcess(userType, process);
        string sql = BuildBMCardSQL(userType, process);

        var list = new List<BMCardRow>();
        await using var conn = await _connections.OpenConnectionAsync(cancellationToken);
        await using var cmd = new SqlCommand(sql, conn);

        RepositorySql.AddDateRange(cmd, dateFrom, dateTo);
        RepositorySql.AddVarChar(cmd, "@Process", process, 20);
        RepositorySql.AddVarChar(cmd, "@Location", location, 20);
        RepositorySql.AddVarChar(cmd, "@Mcno", machineNo, 50);
        RepositorySql.AddVarChar(cmd, "@MCType", machineType, 50);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        // The optional columns are fixed for this result set, not per BM row.
        var optionalColumns = RepositorySql.GetColumns(reader);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new BMCardRow
            {
                DeviceName = reader["Devicename"].ToString()!,
                IsMCD = reader["isMCD"].ToString()!,
                Process = reader["process"].ToString()!,
                Location = reader["location"].ToString()!,
                MachineNo = reader["Mcno"].ToString()!,
                MachineType = optionalColumns.Contains("mctype")
                                ? reader["mctype"].ToString()! : string.Empty,
                TimeOfRequest = reader["Timeofrequest"] is DateTime requested
                    ? requested.ToString("MM/dd/yyyy HH:mm:ss", CultureInfo.InvariantCulture)
                    : string.Empty,
                BMCase = reader["BMCase"].ToString()!,
                BMCaseId = optionalColumns.Contains("BMCaseID")
                                ? reader["BMCaseID"].ToString()! : string.Empty,
                BMId = reader["bmid"] is DBNull ? 0 : Convert.ToInt32(reader["bmid"]),
                OcapCount = optionalColumns.Contains("OCAPCount") && reader["OCAPCount"] is not DBNull
                                ? Convert.ToInt32(reader["OCAPCount"]) : 0,
                HaveAction = reader["HaveAction"].ToString()!,
                BMContentId = reader["BMContentID"].ToString()!,
                BMContent = optionalColumns.Contains("BMContent")
                                ? reader["BMContent"].ToString()! : string.Empty,
                Priority = optionalColumns.Contains("Priority")
                                ? reader["Priority"].ToString()! : string.Empty,
                MCStatus = optionalColumns.Contains("MCStatus")
                                ? reader["MCStatus"].ToString()! : string.Empty,
                FontColor = optionalColumns.Contains("FontColor")
                                ? reader["FontColor"].ToString()! : "None",
                ActionMarker = optionalColumns.Contains("Accre")
                                ? reader["Accre"].ToString()! : string.Empty,
                SectionMarker = optionalColumns.Contains("Sect")
                                ? reader["Sect"].ToString()! : string.Empty,
                DepartmentMarker = optionalColumns.Contains("Dept")
                                ? reader["Dept"].ToString()! : string.Empty,
                DivisionMarker = optionalColumns.Contains("Div")
                                ? reader["Div"].ToString()! : string.Empty
            });
        }
        // The shared all-floor read may interleave locations. Color complete
        // columns independently, exactly as the single-column request does.
        foreach (var group in list.GroupBy(row => (row.MachineNo, row.MachineType, row.Location)))
        {
            var column = group.ToList();
            ApplyLegacyActionColors(column);
            ApplyLegacyRepeatColors(column, userType, process);
        }
        return list;
    }

    private static void ApplyLegacyActionColors(List<BMCardRow> cards)
    {
        foreach (var card in cards)
            card.ActionColor = card.MCStatus.Trim().ToUpperInvariant() switch
            {
                "RUNNING (ONGOING INVESTIGATION/MONITORING)" => "DarkGray",
                "RUNNING (UNDER 100% INSPECTION)" => "DarkOrange",
                "RUNNING (LOT EVALUATION)" => "LightGreen",
                "NO RE-OCCURRENCE AFTER 7 DAYS" => "Green",
                "STOP (WAITING FOR INSPECTION RESULTS)" => "Violet",
                "STOP (WAITING FOR QC INSTRUCTION)" => "Aqua",
                _ => "Red"
            };

        // uc_Test.CheckIfSolve uses the machine's newest BM as its reference,
        // then overrides existing actions in repeated cases after seven days.
        foreach (var machine in cards.GroupBy(card => (card.MachineNo, card.MachineType, card.Location)))
        {
            if (!TryParseLegacyDate(machine.First().TimeOfRequest, out var referenceDate)) continue;
            foreach (var cases in machine.GroupBy(card => card.BMCase).Where(group => group.Count() > 1))
            {
                var dated = cases.Where(card => TryParseLegacyDate(card.TimeOfRequest, out _))
                    .Select(card => (Card: card, Date: DateTime.ParseExact(card.TimeOfRequest,
                        "MM/dd/yyyy HH:mm:ss", CultureInfo.InvariantCulture))).ToList();
                if (dated.Count == 0) continue;
                var latest = dated.Max(row => row.Date);
                foreach (var row in dated)
                    if (row.Card.HaveAction == "Yes" &&
                        ((referenceDate - latest).Days >= 7 || (latest - row.Date).Days >= 7))
                        row.Card.ActionColor = "Green";
            }
        }
    }
    // PC Status tracking (UpdatePCStatus / CheckIfExisting from Form1)
    public async Task UpsertPCStatusAsync(
        string pcName, string ipAddress, string version, CancellationToken cancellationToken = default)
    {
        await using var conn = await _connections.OpenConnectionAsync(cancellationToken);

        bool exists = false;
        await using (var chk = new SqlCommand(
            "SELECT Seqno FROM [eMaintenanceSystemDB].[dbo].[MachineKarte_PCStatus] WHERE PCName = @PCname",
            conn))
        {
            chk.Parameters.AddWithValue("@PCname", pcName);
            var res = await chk.ExecuteScalarAsync(cancellationToken);
            exists = res != null;
        }

        string sql = exists
            ? "UPDATE [eMaintenanceSystemDB].[dbo].[MachineKarte_PCStatus] SET IPAdd=@IPAdd, LastOpen=@LastOpen, Version=@Version, UserType=@UserType WHERE PCName=@PCname"
            : "INSERT INTO [eMaintenanceSystemDB].[dbo].[MachineKarte_PCStatus] (PCName,IPAdd,LastOpen,Version,UserType) VALUES (@PCname,@IPAdd,@LastOpen,@Version,@UserType)";

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@PCname", pcName);
        cmd.Parameters.AddWithValue("@IPAdd", ipAddress);
        cmd.Parameters.AddWithValue("@LastOpen", DateTime.Now.ToString("MM/dd/yyyy hh:mm:ss"));
        cmd.Parameters.AddWithValue("@Version", version);
        cmd.Parameters.AddWithValue("@UserType", "PM");
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
    // Private SQL builders
    private static void ValidateProcess(string userType, string process)
    {
        if (!MachineSummaryConfig.IsValidProcess(process, userType == "TE"))
            throw new ArgumentException($"Unknown process '{process}'.", nameof(process));
    }

    private static string BuildUserFilter(string userType) => userType switch
    {
        "PM" => "(a.BMStatusID NOT IN ('30','31'))",
        "CC" => "(a.Turnover = 'CC' AND a.BMStatusID IN (6,7,8,9,11,20,19,14))",
        "EEE" => "(a.Turnover = 'PE' AND a.BMStatusID IN (6,7,8,9,11,20,19,14))",
        "VIS" => "(a.Turnover = 'VIS' AND a.BMStatusID IN (6,7,8,9,11,20,19,14))",
        _ => "1=1"
    };

    private static string BuildMachineSummarySQL(
        string process,
        string addQuery,
        int top1,
        int top2,
        bool allMachines)
    {
        string machineFilter = allMachines
            ? string.Empty
            : "AND a.Mcno = @Mcno";

        string counts = $@"
            SELECT
                COUNT_BIG(*) AS MACHINENAME,
                a.MCNO,
                COALESCE(c.actionline,4) AS actionline,
                a.MCType
            FROM dbo.{process}BMMaintenance a

            INNER JOIN dbo.Process d
                ON a.Process = d.ProcessName

            INNER JOIN dbo.MCType c
                ON a.MCType = c.MCType
               AND c.ProcessID = d.ProcessID

            WHERE
                a.TimeOfRequest >= @Range1
                AND a.TimeOfRequest < DATEADD(DAY,1,@Range2)
                AND a.PMCategoryID = 1
                AND d.ProcessName = @Process
                AND (@Location = 'All' OR a.Location = @Location)
                {machineFilter}
                AND {addQuery}
                AND a.BMCaseID <> 0
            GROUP BY
                a.MCNO,
                COALESCE(c.actionline,4),
                a.MCType";

        return BuildPagedSummarySQL(
            counts,
            "MACHINENAME DESC, MCNO ASC, MCType ASC, actionline ASC",
            top1,
            top2);
    }

    private static string BuildTEMachineSummarySQL(
        string addQuery,
        int top1,
        int top2,
        bool allMachines)
    {
        string machineFilter = allMachines
            ? string.Empty
            : "AND a.TesterNo = @Mcno";

        string counts = @"
            SELECT
                COUNT_BIG(*) AS MACHINENAME,
                a.MCType,
                a.TesterNo,
                COALESCE(c.actionline,4) AS actionline
            FROM dbo.TEBMMaintenance a

            INNER JOIN dbo.Process d
                ON a.Process = d.ProcessName

            INNER JOIN dbo.TesterType c
                ON a.MCType = c.TesterType
               AND c.ProcessID = d.ProcessID
               AND c.Location = a.Location

            WHERE
                a.TimeOfRequest >= @Range1
                AND a.TimeOfRequest < DATEADD(DAY,1,@Range2)
                AND a.PMCategoryID IN (1,4)
                AND d.ProcessName = @Process
                AND (@Location = 'All' OR a.Location = @Location)
                " + machineFilter + @"
                AND a.BMStatusID NOT IN (30,31,1,2,3,7,17)
                AND a.BMUnitID <> 0
            GROUP BY
                a.MCType,
                a.TesterNo,
                COALESCE(c.actionline,4)";

        return BuildPagedSummarySQL(
            counts,
            "MACHINENAME DESC, MCType ASC, TesterNo ASC, actionline ASC",
            top1,
            top2);
    }

    // Grouped rows are already unique; reuse the aggregation for page and total.
    private static string BuildPagedSummarySQL(string counts, string order, int top1, int top2) => $@"
        SET NOCOUNT ON;
        ;WITH MachineCounts AS ({counts})
        SELECT *, ROW_NUMBER() OVER (ORDER BY {order}) AS PageRow
        INTO #MonitoringMachineCounts FROM MachineCounts;
        SELECT * FROM #MonitoringMachineCounts
        WHERE PageRow > {top1} AND PageRow <= {top2} ORDER BY PageRow;
        SELECT COUNT(*) FROM #MonitoringMachineCounts;
        DROP TABLE #MonitoringMachineCounts;";

    private static string BuildBMCardSQL(string userType, string process)
    {
        bool te = userType == "TE";
        bool why = userType is "CC" or "EEE" or "VIS";
        string table = te ? "TEBMMaintenance" : $"{process}BMMaintenance";
        string machineColumn = te ? "TesterNo" : "MCNo";
        string caseTable = te ? "BMTEUnit" : why ? "BMWhy" : "BMCase";
        string caseId = why ? "Why4" : "BMCaseID";
        string joinColumn = te ? "BMUnitID" : caseId;
        string joinId = te ? "BMTEUnitID" : why ? "BMWhyID" : "BMCaseID";
        string actionTable = userType switch
        {
            "TE" => "MachineKarte_ActionCard_TE",
            "CC" => "MachineKarte_ActionCard_CC",
            "EEE" => "MachineKarte_ActionCard_EEE",
            "VIS" => "MachineKarte_ActionCard_VIS",
            _ => "MachineKarte_ActionCard"
        };
        string category = te ? "a.PMCategoryID IN (1,4)" : "a.PMCategoryID = 1";
        string status = te ? "a.BMStatusID NOT IN (30,31,1,2,3,7,17)" : BuildUserFilter(why ? userType : "PM");
        string actionMachineFilter = te ? string.Empty : "AND ac.MachineNo = f.Mcno";
        string actionPartition = te ? "ac.BMID" : "ac.BMID, ac.MachineNo";
        string actionJoin = te ? string.Empty : "AND la.MachineNo = f.Mcno";
        string priority = te ? ", a.Priority" : string.Empty;
        string priorityResult = te ? ", f.Priority" : string.Empty;

        // Filter before ranking actions or counting OCAP rows. TE actions use BMID alone,
        // as uc_Test does; the other four modes also require the machine number.
        return $@"
        ;WITH FilteredBM AS
        (
            SELECT a.DeviceName, a.Process, a.Location, a.{machineColumn} AS Mcno,
                a.MCType, a.TimeOfRequest, b.{caseTable} AS BMCase, a.{caseId} AS BMCaseID,
                a.BMID, a.BMCaseID AS OcapCaseID, a.BMContentID AS OriginalBMContentID,
                COALESCE(CASE WHEN a.AcredBMContentID <> a.BMContentID THEN
                    CASE WHEN a.AcredBMContentID IS NULL OR a.AcredBMContentID IN ('','0')
                        THEN a.BMContentID ELSE a.AcredBMContentID END
                    ELSE a.BMContentID END, 0) AS BMContentID{priority}
            FROM [eMaintenanceSystemDB].[dbo].[{table}] a
            INNER JOIN [eMaintenanceSystemDB].[dbo].[{caseTable}] b ON a.{joinColumn} = b.{joinId}
            WHERE a.TimeOfRequest >= @Range1
                AND a.TimeOfRequest < DATEADD(DAY,1,@Range2)
                AND a.Process = @Process
                AND {category} AND {status}
                AND a.{joinColumn} <> 0
                AND (@Location = 'All' OR a.Location = @Location)
                AND (@Mcno = 'All' OR a.{machineColumn} = @Mcno)
                AND (@MCType = '' OR a.MCType = @MCType)
        ),
        LatestAction AS
        (
            SELECT ac.BMID, ac.MachineNo, ac.Action, ac.Comment, ac.Comment2, ac.Comment3, ac.MCStatus,
                ROW_NUMBER() OVER (PARTITION BY {actionPartition} ORDER BY ac.Seqno DESC) AS RN
            FROM [eMaintenanceSystemDB].[dbo].[{actionTable}] ac
            WHERE EXISTS (SELECT 1 FROM FilteredBM f WHERE f.BMID = ac.BMID {actionMachineFilter})
        ),
        OCAPCounts AS
        (
            SELECT o.BMCaseID, COUNT(*) AS OCAPCount
            FROM [eMaintenanceSystemDB].[dbo].[gridBMOCAP] o
            WHERE o.Process = @Process
                AND EXISTS (SELECT 1 FROM FilteredBM f WHERE f.OcapCaseID = o.BMCaseID)
            GROUP BY o.BMCaseID
        )
        SELECT f.DeviceName,
            CASE WHEN EXISTS (SELECT 1 FROM [eMaintenanceSystemDB].[dbo].[MachineKarte_MCD] m
                WHERE m.IsActive = 1 AND REPLACE(m.DeviceName,' ','') = REPLACE(f.DeviceName,' ',''))
                THEN 'Yes' ELSE 'No' END AS isMCD,
            f.Process, f.Location, f.Mcno, f.MCType, f.TimeOfRequest, f.BMCase, f.BMCaseID, f.BMID,
            COALESCE(o.OCAPCount,0) AS OCAPCount,
            CASE WHEN la.BMID IS NOT NULL THEN 'Yes' ELSE 'No' END AS HaveAction,
            f.BMContentID, bc.BMContent{priorityResult},
            la.Action AS Accre, la.Comment AS Sect, la.Comment2 AS Dept, la.Comment3 AS Div,
            la.MCStatus, 'None' AS FontColor
        FROM FilteredBM f
        LEFT JOIN LatestAction la ON la.BMID = f.BMID {actionJoin} AND la.RN = 1
        LEFT JOIN OCAPCounts o ON o.BMCaseID = f.OcapCaseID
        LEFT JOIN [eMaintenanceSystemDB].[dbo].[BMContent] bc
            ON bc.BMContentID = f.OriginalBMContentID AND bc.Process = f.Process
        ORDER BY f.Mcno DESC, f.MCType DESC, f.TimeOfRequest DESC, f.BMCase";
    }
}
