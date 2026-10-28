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
            "DC" or "DB" or "WB" or "MP"
                => new[] { "LSI1-1F", "LSI2-1F", "LSI2-2F", "LSI2-3F", "LSI3-2F" },
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

        public async Task<List<MachineSummary>> GetPagedMachineSummariesAsync(
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
            return list;
        }

        private static void ApplyLegacyRepeatColors(
            List<BMCardRow> cards,
            string userType,
            string process)
        {
            for (var start = 0; start < cards.Count; start++)
            {
                var first = cards[start];
                var repeatCount = 1;

                while (start + repeatCount < cards.Count && repeatCount < 6)
                {
                    var previous = cards[start + repeatCount - 1];
                    var current = cards[start + repeatCount];
                    if (!string.Equals(first.MachineNo, current.MachineNo, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(first.BMCase, current.BMCase, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(first.BMContentId, current.BMContentId, StringComparison.OrdinalIgnoreCase) ||
                        !WithinSevenDays(previous.TimeOfRequest, current.TimeOfRequest))
                        break;

                    repeatCount++;
                }

                if (repeatCount < 2) continue;

                var color = userType.Equals("TE", StringComparison.OrdinalIgnoreCase)
                    ? "Red"
                    : GetLegacyContentColor(process, first.BMContentId);

                for (var index = start; index < start + repeatCount; index++)
                    cards[index].FontColor = color;
            }
        }

        private static bool WithinSevenDays(string previousValue, string currentValue)
        {
            if (!DateTime.TryParse(previousValue, CultureInfo.InvariantCulture, DateTimeStyles.None, out var previous) ||
                !DateTime.TryParse(currentValue, CultureInfo.InvariantCulture, DateTimeStyles.None, out var current))
                return false;

            var days = (previous - current).TotalDays;
            return days >= 0 && days <= 7;
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
            string machineNo, string dateFrom, string dateTo)
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
                                    ? reader["FontColor"].ToString()! : "None"
                });
            }
            ApplyLegacyRepeatColors(list, userType, process);
            return list;
        }

        // ----------------------------------------------------------------
        // Floor summary counts for Panel2 labels (BM/MCD counts per floor)
        // ----------------------------------------------------------------

        public async Task<List<FloorSummary>> GetFloorSummariesAsync(
            string userType, string process, string dateFrom, string dateTo)
        {
            string addQuery = BuildUserFilter(userType);
            bool isMachine = userType is "PM" or "CC" or "EEE" or "VIS";

            string sql = isMachine
                ? $@"SELECT a.Location, COUNT(a.Mcno) AS BMCount,
                            SUM(CASE WHEN m.Devicename IS NOT NULL THEN 1 ELSE 0 END) AS MCDCount,
                            COUNT(DISTINCT a.Mcno) AS MachineCount
                     FROM [eMaintenanceSystemDB].[dbo].[{process}BMMaintenance] a
                     INNER JOIN [eMaintenanceSystemDB].[dbo].[Process] d
                         ON a.Process = d.ProcessName
                     INNER JOIN [eMaintenanceSystemDB].[dbo].[BMCase] b
                         ON a.BMCaseID = b.BMCaseID
                     LEFT JOIN [eMaintenanceSystemDB].[dbo].[MachineKarte_MCD] m
                         ON REPLACE(m.Devicename,' ','') = REPLACE(a.Devicename,' ','') AND m.IsActive = 1
                     WHERE (TimeOfRequest BETWEEN @Range1 AND @Range2)
                       AND PMCategoryID = 1
                       AND d.ProcessName = @Process
                       AND a.BMCaseID <> 0
                       AND {addQuery}
                     GROUP BY a.Location"
                : $@"SELECT a.Location, COUNT(a.MCType) AS BMCount,
                            0 AS MCDCount,
                            COUNT(DISTINCT a.TesterNo) AS MachineCount
                     FROM [eMaintenanceSystemDB].[dbo].[TEBMMaintenance] a
                     INNER JOIN [eMaintenanceSystemDB].[dbo].[Process] d
                         ON a.Process = d.ProcessName
                     WHERE (TimeOfRequest BETWEEN @Range1 AND @Range2)
                       AND (PMCategoryID = 1 OR PMCategoryID = 4)
                       AND d.ProcessName = @Process
                       AND a.BMStatusID NOT IN ('30','31','1','2','3','7','17')
                       AND BMUnitID <> 0
                     GROUP BY a.Location";

            var list = new List<FloorSummary>();
            await using var conn = Open();
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);

            cmd.Parameters.AddWithValue("@Range1", dateFrom);
            cmd.Parameters.AddWithValue("@Range2", dateTo);
            cmd.Parameters.AddWithValue("@Process", process);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new FloorSummary
                {
                    FloorCode = reader["Location"].ToString()!,
                    BMCount = Convert.ToInt32(reader["BMCount"]),
                    MCDCount = Convert.ToInt32(reader["MCDCount"]),
                    MachineCount = Convert.ToInt32(reader["MachineCount"])
                });
            }
            return list;
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

            return $@"
                SELECT * FROM (
                    SELECT TOP {top2}
                        COUNT(mcno) AS MACHINENAME, MCNO,
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
                    GROUP BY MCNO, actionline, a.MCType
                    ORDER BY MACHINENAME DESC, MCNO ASC
                ) X
                EXCEPT
                SELECT * FROM (
                    SELECT TOP {top1}
                        COUNT(mcno) AS MACHINENAME, MCNO,
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
                    GROUP BY MCNO, actionline, a.MCType
                    ORDER BY MACHINENAME DESC, MCNO ASC
                ) Y
                ORDER BY MACHINENAME DESC, MCNO ASC";
        }

        private static string BuildTEMachineSummarySQL(
            string addQuery, int top1, int top2, bool allMachines)
        {
            string machineFilter = allMachines ? string.Empty : "AND (testerno = @Mcno)";

            return $@"
                SELECT * FROM (
                    SELECT TOP {top2}
                        COUNT(MCType) AS MACHINENAME, MCType, testerno,
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
                    GROUP BY MCType, testerno, actionline
                    ORDER BY MACHINENAME DESC, MCType ASC
                ) x
                EXCEPT
                SELECT * FROM (
                    SELECT TOP {top1}
                        COUNT(MCType) AS MACHINENAME, MCType, testerno,
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
                    GROUP BY MCType, testerno, actionline
                    ORDER BY MACHINENAME DESC, MCType ASC
                ) y
                ORDER BY MACHINENAME DESC, MCType ASC";
        }

        private static string BuildBMCardSQL(string userType, string process)
        {
            if (userType == "TE")
            {
                return $@"
                    SELECT a.Devicename,
                           ISNULL((SELECT 'Yes' FROM MachineKarte_MCD
                                   WHERE REPLACE(Devicename,' ','') = REPLACE(a.devicename,' ','') AND isactive = 1),'No') AS isMCD,
                           a.process, a.location, a.testerNo AS Mcno, a.mctype,
                           FORMAT(a.Timeofrequest,'MM/dd/yyyy HH:mm:ss') AS Timeofrequest,
                           b.bmteunit AS BMCase, a.BMCaseID AS BMCaseID, a.bmid,
                           (SELECT COUNT(*) FROM gridBMOCAP WHERE BMCaseID = a.BMCaseID AND Process = a.process) AS OCAPCount,
                           ISNULL((SELECT 'Yes' FROM MachineKarte_ActionCard_TE WHERE bmid = a.bmID),'No') AS HaveAction,
                           COALESCE(CASE WHEN a.AcredBMContentID <> a.BMContentID THEN
                               CASE WHEN a.AcredBMContentID = '' OR a.AcredBMContentID = '0' OR a.AcredBMContentID IS NULL
                                    THEN a.BMContentID ELSE a.AcredBMContentID END
                               ELSE a.BMContentID END, 0) AS BMContentID,
                           Priority,
                           (SELECT BMContent FROM BMContent WHERE BMContentID = a.BMContentID AND process = '{process}') AS BMContent,
                           c.mcstatus AS MCStatus, 'None' AS FontColor
                    FROM [eMaintenanceSystemDB].[dbo].[TEBMMaintenance] a
                    INNER JOIN [eMaintenanceSystemDB].[dbo].[BMTEUnit] b ON a.bmunitid = b.bmteunitid
                    LEFT  JOIN [eMaintenanceSystemDB].[dbo].[MachineKarte_ActionCard_TE] c
                        ON a.bmID = c.bmID AND a.testerno = c.machineno
                    WHERE (a.timeofrequest BETWEEN @Range1 AND @Range2)
                      AND a.process = '{process}'
                      AND (a.Pmcategoryid = '1' OR a.Pmcategoryid = '4')
                      AND a.BMStatusID NOT IN ('30','31','1','2','3','7','17')
                      AND a.bmunitid <> 0
                      AND (@Location = 'All' OR a.Location = @Location)
                      AND a.testerno = @Mcno
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

            return $@"
                SELECT a.Devicename,
                       ISNULL((SELECT 'Yes' FROM MachineKarte_MCD
                               WHERE REPLACE(Devicename,' ','') = REPLACE(a.devicename,' ','') AND isactive = 1),'No') AS isMCD,
                       a.process, a.location, a.Mcno AS Mcno,
                       FORMAT(a.Timeofrequest,'MM/dd/yyyy HH:mm:ss') AS Timeofrequest,
                       b.BMWhy AS BMCase, a.BMCaseID AS BMCaseID, a.bmid,
                       (SELECT COUNT(*) FROM gridBMOCAP WHERE BMCaseID = a.BMCaseID AND Process = a.process) AS OCAPCount,
                       ISNULL((SELECT 'Yes' FROM {actionTable} WHERE bmid = a.bmID AND machineno = a.mcno),'No') AS HaveAction,
                       COALESCE(CASE WHEN a.AcredBMContentID <> a.BMContentID THEN
                           CASE WHEN a.AcredBMContentID = '' OR a.AcredBMContentID = '0' OR a.AcredBMContentID IS NULL
                                THEN a.BMContentID ELSE a.AcredBMContentID END
                           ELSE a.BMContentID END, 0) AS BMContentID,
                       Priority,
                       (SELECT BMContent FROM BMContent WHERE BMContentID = a.BMContentID AND process = '{process}') AS BMContent,
                       c.mcstatus AS MCStatus, 'None' AS FontColor
                FROM [eMaintenanceSystemDB].[dbo].[{process}BMMaintenance] a
                INNER JOIN [eMaintenanceSystemDB].[dbo].[BMCase] b ON a.BMCaseID = b.BMCaseID
                LEFT  JOIN {actionTable} c ON a.bmID = c.bmID AND a.mcno = c.machineno
                WHERE (a.timeofrequest BETWEEN @Range1 AND @Range2)
                  AND a.process = '{process}'
                  AND a.PMCategoryID = 1
                  AND {addQuery}
                  AND a.BMCaseID <> 0
                  AND (@Location = 'All' OR a.Location = @Location)
                  AND a.Mcno = @Mcno
                ORDER BY a.Mcno DESC, a.MCType DESC, a.Timeofrequest DESC, b.BMWhy";
        }
    }

}