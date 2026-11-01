// Repositories/MonitoringRepository.cs
// Converted from Form1.vb SQL queries � E-Karte System
// C# ASP.NET Core + Dapper (or plain SqlClient � swap as needed)

using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using MachineKarte.Web.Models;

namespace MachineKarte.Web.Repositories
{
    public class MonitoringRepository
    {
        private readonly string _connStr;

        public MonitoringRepository(IConfiguration config)
        {
            _connStr = config.GetConnectionString("DBConnection")
                       ?? throw new InvalidOperationException("DBConnection not configured.");
        }

        // ----------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------

        private SqlConnection Open() => new(_connStr);

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

        // ----------------------------------------------------------------
        // Dropdown helpers
        // ----------------------------------------------------------------

        /// <summary>
        /// Gets distinct machine numbers for cbMachine dropdown.
        /// Mirrors cbFloor_SelectedIndexChanged in Form1.vb.
        /// </summary>
        public async Task<List<string>> GetMachinesAsync(
            string userType, string process, string floor)
        {
            var results = new List<string>();
            await using var conn = Open();
            await conn.OpenAsync();

            string sql;
            string colName;

            if (userType is "PM" or "CC" or "EEE" or "VIS")
            {
                sql = @"SELECT DISTINCT a.MCNo AS Val
                        FROM [eMaintenanceSystemDB].[dbo].[MCList] a
                        INNER JOIN [eMaintenanceSystemDB].[dbo].[Process] b
                            ON a.ProcessID = b.ProcessID
                        WHERE b.ProcessName = @Process
                          AND (@Location = 'All' OR a.MCLocation = @Location)
                        ORDER BY a.MCNo ASC";
                colName = "Val";
            }
            else // TE
            {
                sql = @"SELECT DISTINCT TesterNo AS Val
                        FROM [eMaintenanceSystemDB].[dbo].[TEBMMaintenance]
                        WHERE Process   = @Process
                          AND (@Location = 'All' OR Location = @Location)
                          AND TesterNo <> ''
                        ORDER BY TesterNo ASC";
                colName = "Val";
            }

            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Process", process);
            cmd.Parameters.AddWithValue("@Location", floor);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                results.Add(reader[colName].ToString()!);

            return results;
        }

        // ----------------------------------------------------------------
        // Paged machine summary (drives card column headers)
        // Mirrors LoadMCName() strQuery2 � PM/CC/EEE/VIS and TE branches
        // ----------------------------------------------------------------

        public async Task<(List<MachineSummary> Machines, int TotalPages)> GetPagedMachineSummariesAsync(
            MonitoringFilter f)
        {
            int top2 = f.Page * 10;
            int top1 = (f.Page * 10) - 10;

            string addQuery = BuildUserFilter(f.UserType);
            bool isMachine = f.UserType is "PM" or "CC" or "EEE" or "VIS";

            string sql = isMachine
                ? BuildMachineSummarySQL(f.Process, addQuery, top1, top2, f.Machine == "All")
                : BuildTEMachineSummarySQL(addQuery, top1, top2, f.Machine == "All");

            var list = new List<MachineSummary>();
            await using var conn = Open();
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);

            cmd.Parameters.AddWithValue("@Range1", f.DateFrom);
            cmd.Parameters.AddWithValue("@Range2", f.DateTo);
            cmd.Parameters.AddWithValue("@Location", f.Floor);
            cmd.Parameters.AddWithValue("@Process", f.Process);
            if (f.Machine != "All")
                cmd.Parameters.AddWithValue("@Mcno", f.Machine);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
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
            await reader.NextResultAsync();
            await reader.ReadAsync();
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

        // ----------------------------------------------------------------
        // BM card rows per machine (drives uC_Test card content)
        // Mirrors LoadMCName1() strQuery
        // ----------------------------------------------------------------

        public async Task<List<BMCardRow>> GetBMCardsAsync(
            string userType, string process, string location,
            string machineNo, string dateFrom, string dateTo, string machineType = "")
        {
            string sql = BuildBMCardSQL(userType, process);

            var list = new List<BMCardRow>();
            await using var conn = Open();
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);

            cmd.Parameters.AddWithValue("@Range1", dateFrom);
            cmd.Parameters.AddWithValue("@Range2", dateTo);
            cmd.Parameters.AddWithValue("@Location", location);
            cmd.Parameters.AddWithValue("@Mcno", machineNo);
            cmd.Parameters.AddWithValue("@MCType", machineType);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new BMCardRow
                {
                    DeviceName = reader["Devicename"].ToString()!,
                    IsMCD = reader["isMCD"].ToString()!,
                    Process = reader["process"].ToString()!,
                    Location = reader["location"].ToString()!,
                    MachineNo = reader["Mcno"].ToString()!,
                    MachineType = reader.HasColumn("mctype")
                                    ? reader["mctype"].ToString()! : string.Empty,
                    TimeOfRequest = reader["Timeofrequest"].ToString()!,
                    BMCase = reader["BMCase"].ToString()!,
                    BMCaseId = reader.HasColumn("BMCaseID")
                                    ? reader["BMCaseID"].ToString()! : string.Empty,
                    BMId = reader["bmid"] is DBNull ? 0 : Convert.ToInt32(reader["bmid"]),
                    OcapCount = reader.HasColumn("OCAPCount") && reader["OCAPCount"] is not DBNull
                                    ? Convert.ToInt32(reader["OCAPCount"]) : 0,
                    HaveAction = reader["HaveAction"].ToString()!,
                    BMContentId = reader["BMContentID"].ToString()!,
                    BMContent = reader.HasColumn("BMContent")
                                    ? reader["BMContent"].ToString()! : string.Empty,
                    Priority = reader.HasColumn("Priority")
                                    ? reader["Priority"].ToString()! : string.Empty,
                    MCStatus = reader.HasColumn("MCStatus")
                                    ? reader["MCStatus"].ToString()! : string.Empty,
                    FontColor = reader.HasColumn("FontColor")
                                    ? reader["FontColor"].ToString()! : "None",
                    ActionMarker = reader.HasColumn("Accre")
                                    ? reader["Accre"].ToString()! : string.Empty,
                    SectionMarker = reader.HasColumn("Sect")
                                    ? reader["Sect"].ToString()! : string.Empty,
                    DepartmentMarker = reader.HasColumn("Dept")
                                    ? reader["Dept"].ToString()! : string.Empty,
                    DivisionMarker = reader.HasColumn("Div")
                                    ? reader["Div"].ToString()! : string.Empty
                });
            }
            ApplyLegacyActionColors(list);
            ApplyLegacyRepeatColors(list, userType, process);
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


        // ----------------------------------------------------------------
        // PC Status tracking (UpdatePCStatus / CheckIfExisting from Form1)
        // ----------------------------------------------------------------

        public async Task UpsertPCStatusAsync(string pcName, string ipAddress, string version)
        {
            await using var conn = Open();
            await conn.OpenAsync();

            bool exists = false;
            await using (var chk = new SqlCommand(
                "SELECT Seqno FROM [eMaintenanceSystemDB].[dbo].[MachineKarte_PCStatus] WHERE PCName = @PCname",
                conn))
            {
                chk.Parameters.AddWithValue("@PCname", pcName);
                var res = await chk.ExecuteScalarAsync();
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
            await cmd.ExecuteNonQueryAsync();
        }

        // ----------------------------------------------------------------
        // Private SQL builders
        // ----------------------------------------------------------------

        private static string BuildUserFilter(string userType) => userType switch
        {
            "PM" => "(a.BMStatusID NOT IN ('30','31'))",
            "CC" => "(Turnover IN ('CC')  AND BMStatusID IN (6,7,8,9,11,20,19,14))",
            "EEE" => "(Turnover IN ('PE')  AND BMStatusID IN (6,7,8,9,11,20,19,14))",
            "VIS" => "(Turnover IN ('VIS') AND BMStatusID IN (6,7,8,9,11,20,19,14))",
            _ => "1=1"
        };

        private static string BuildMachineSummarySQL(
            string process, string addQuery, int top1, int top2, bool allMachines)
        {
            string machineFilter = allMachines ? string.Empty : "AND (Mcno = @Mcno)";
            string tbl = $"[eMaintenanceSystemDB].[dbo].[{process}BMMaintenance]";
            string counts = $@"
                    SELECT COUNT(mcno) AS MACHINENAME, MCNO,
                        COALESCE(c.actionline,4) AS actionline, a.MCType
                    FROM {tbl} a
                    INNER JOIN [eMaintenanceSystemDB].[dbo].[Process] D ON a.Process = d.ProcessName
                    INNER JOIN [eMaintenanceSystemDB].[dbo].[MCType]  c ON a.MCType = c.MCType AND c.ProcessID = d.ProcessID
                    INNER JOIN [eMaintenanceSystemDB].[dbo].[BMCase]  b ON a.BMCaseID = b.BMCaseID
                    WHERE (TimeOfRequest BETWEEN @Range1 AND @Range2)
                      AND PMCategoryID = 1 AND d.ProcessName = @Process
                      AND (@Location = 'All' OR a.Location = @Location)
                      {machineFilter}
                      AND {addQuery}
                      AND a.BMCaseID <> 0
                    GROUP BY MCNO, actionline, a.MCType";
            return BuildPagedSummarySQL(counts, "MACHINENAME DESC, MCNO ASC", top1, top2);
        }

        private static string BuildTEMachineSummarySQL(
            string addQuery, int top1, int top2, bool allMachines)
        {
            string machineFilter = allMachines ? string.Empty : "AND (testerno = @Mcno)";

            string counts = $@"
                    SELECT COUNT(MCType) AS MACHINENAME, MCType, testerno,
                        COALESCE(c.actionline,4) AS actionline
                    FROM [eMaintenanceSystemDB].[dbo].[TEBMMaintenance] a
                    INNER JOIN [eMaintenanceSystemDB].[dbo].[Process] D ON a.Process = d.ProcessName
                    INNER JOIN [eMaintenanceSystemDB].[dbo].[testertype] c
                        ON a.MCType = c.testertype AND c.ProcessID = d.ProcessID AND (@Location = 'All' OR c.location = @Location)
                    WHERE (TimeOfRequest BETWEEN @Range1 AND @Range2)
                      AND (PMCategoryID = 1 OR PMCategoryID = 4)
                      AND d.ProcessName = @Process
                      AND (@Location = 'All' OR a.Location = @Location)
                      {machineFilter}
                      AND a.BMStatusID NOT IN ('30','31','1','2','3','7','17')
                      AND BMUnitID <> 0
                    GROUP BY MCType, testerno, actionline";
            return BuildPagedSummarySQL(counts, "MACHINENAME DESC, MCType ASC, testerno ASC", top1, top2);
        }

        private static string BuildPagedSummarySQL(string counts, string order, int top1, int top2) => $@"
            SELECT * FROM (SELECT TOP {top2} * FROM ({counts}) counts ORDER BY {order}) x
            EXCEPT
            SELECT * FROM (SELECT TOP {top1} * FROM ({counts}) counts ORDER BY {order}) y
            ORDER BY {order};
            SELECT COUNT(*) FROM ({counts}) counts;";

        private static string BuildBMCardSQL(string userType, string process)
        {
            if (userType == "TE")
            {
                return $@"
                    SELECT a.Devicename,
                           CASE WHEN EXISTS (SELECT 1 FROM MachineKarte_MCD
                                   WHERE REPLACE(Devicename,' ','') = REPLACE(a.devicename,' ','') AND isactive = 1) THEN 'Yes' ELSE 'No' END AS isMCD,
                           a.process, a.location, a.testerNo AS Mcno, a.mctype,
                           FORMAT(a.Timeofrequest,'MM/dd/yyyy HH:mm:ss') AS Timeofrequest,
                           b.bmteunit AS BMCase, a.BMCaseID AS BMCaseID, a.bmid,
                           (SELECT COUNT(*) FROM gridBMOCAP WHERE BMCaseID = a.BMCaseID AND Process = a.process) AS OCAPCount,
                           CASE WHEN EXISTS (SELECT 1 FROM MachineKarte_ActionCard_TE WHERE bmid = a.bmID) THEN 'Yes' ELSE 'No' END AS HaveAction,
                           COALESCE(CASE WHEN a.AcredBMContentID <> a.BMContentID THEN
                               CASE WHEN a.AcredBMContentID = '' OR a.AcredBMContentID = '0' OR a.AcredBMContentID IS NULL
                                    THEN a.BMContentID ELSE a.AcredBMContentID END
                               ELSE a.BMContentID END, 0) AS BMContentID,
                           Priority,
                           (SELECT BMContent FROM BMContent WHERE BMContentID = a.BMContentID AND process = '{process}') AS BMContent,
                           c.Action AS Accre, c.Comment AS Sect, c.Comment2 AS Dept,
                           c.Comment3 AS Div, c.mcstatus AS MCStatus, 'None' AS FontColor
                    FROM [eMaintenanceSystemDB].[dbo].[TEBMMaintenance] a
                    INNER JOIN [eMaintenanceSystemDB].[dbo].[BMTEUnit] b ON a.bmunitid = b.bmteunitid
                    OUTER APPLY (SELECT TOP (1) ac.* FROM [eMaintenanceSystemDB].[dbo].[MachineKarte_ActionCard_TE] ac
                                 WHERE ac.bmID = a.bmID ORDER BY ac.Seqno DESC) c
                    WHERE (a.timeofrequest BETWEEN @Range1 AND @Range2)
                      AND a.process = '{process}'
                      AND (a.Pmcategoryid = '1' OR a.Pmcategoryid = '4')
                      AND a.BMStatusID NOT IN ('30','31','1','2','3','7','17')
                      AND a.bmunitid <> 0
                      AND (@Location = 'All' OR a.Location = @Location)
                      AND (@Mcno = 'All' OR a.testerno = @Mcno)
                      AND (@MCType = '' OR a.MCType = @MCType)
                    ORDER BY a.testerNo DESC, a.MCType DESC, a.Timeofrequest DESC, b.bmteunit";
            }

            // CC / EEE / VIS / PM
            string addQuery = userType switch
            {
                "CC" => "(Turnover IN ('CC')  AND BMStatusID IN (6,7,8,9,11,20,19,14))",
                "EEE" => "(Turnover IN ('PE')  AND BMStatusID IN (6,7,8,9,11,20,19,14))",
                "VIS" => "(Turnover IN ('VIS') AND BMStatusID IN (6,7,8,9,11,20,19,14))",
                _ => "(a.BMStatusID NOT IN ('30','31'))"
            };
            string actionTable = userType switch
            {
                "CC" => "MachineKarte_ActionCard_CC",
                "EEE" => "MachineKarte_ActionCard_EEE",
                "VIS" => "MachineKarte_ActionCard_VIS",
                _ => "MachineKarte_ActionCard"
            };
            bool isWhy = userType is "CC" or "EEE" or "VIS";
            string caseTable = isWhy ? "BMWhy" : "BMCase";
            string caseId = isWhy ? "Why4" : "BMCaseID";
            string joinId = isWhy ? "BMWhyID" : "BMCaseID";
            string caseText = isWhy ? "BMWhy" : "BMCase";

            return $@"
                SELECT a.Devicename,
                       CASE WHEN EXISTS (SELECT 1 FROM MachineKarte_MCD
                               WHERE REPLACE(Devicename,' ','') = REPLACE(a.devicename,' ','') AND isactive = 1) THEN 'Yes' ELSE 'No' END AS isMCD,
                       a.process, a.location, a.Mcno AS Mcno, a.MCType,
                       FORMAT(a.Timeofrequest,'MM/dd/yyyy HH:mm:ss') AS Timeofrequest,
                       b.{caseText} AS BMCase, a.{caseId} AS BMCaseID, a.bmid,
                       (SELECT COUNT(*) FROM gridBMOCAP WHERE BMCaseID = a.BMCaseID AND Process = a.process) AS OCAPCount,
                       CASE WHEN EXISTS (SELECT 1 FROM {actionTable} WHERE bmid = a.bmID AND machineno = a.mcno) THEN 'Yes' ELSE 'No' END AS HaveAction,
                       COALESCE(CASE WHEN a.AcredBMContentID <> a.BMContentID THEN
                           CASE WHEN a.AcredBMContentID = '' OR a.AcredBMContentID = '0' OR a.AcredBMContentID IS NULL
                                THEN a.BMContentID ELSE a.AcredBMContentID END
                           ELSE a.BMContentID END, 0) AS BMContentID,
                       (SELECT BMContent FROM BMContent WHERE BMContentID = a.BMContentID AND process = '{process}') AS BMContent,
                       c.Action AS Accre, c.Comment AS Sect, c.Comment2 AS Dept,
                       c.Comment3 AS Div, c.mcstatus AS MCStatus, 'None' AS FontColor
                FROM [eMaintenanceSystemDB].[dbo].[{process}BMMaintenance] a
                INNER JOIN [eMaintenanceSystemDB].[dbo].[{caseTable}] b ON a.{caseId} = b.{joinId}
                OUTER APPLY (SELECT TOP (1) ac.* FROM {actionTable} ac
                             WHERE ac.bmID = a.bmID AND ac.machineno = a.mcno ORDER BY ac.Seqno DESC) c
                WHERE (a.timeofrequest BETWEEN @Range1 AND @Range2)
                  AND a.process = '{process}'
                  AND a.PMCategoryID = 1
                  AND {addQuery}
                  AND a.{caseId} <> 0
                  AND (@Location = 'All' OR a.Location = @Location)
                  AND (@Mcno = 'All' OR a.Mcno = @Mcno)
                  AND (@MCType = '' OR a.MCType = @MCType)
                ORDER BY a.Mcno DESC, a.MCType DESC, a.Timeofrequest DESC, b.{caseText}";
        }
    }

}
