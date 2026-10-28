// Repositories/ActionCardRepository.cs
// Converted from Form_Input.vb SQL — E-Karte System
// C# ASP.NET Core + plain SqlClient

using System.Data;
using Microsoft.Data.SqlClient;
using MachineKarte.Web.Models;

namespace MachineKarte.Web.Repositories
{
    public class ActionCardRepository
    {
        private readonly string _connStr;

        public ActionCardRepository(IConfiguration config)
        {
            _connStr = config.GetConnectionString("DBConnection")
                       ?? throw new InvalidOperationException("DBConnection not configured.");
        }

        private SqlConnection Open() => new(_connStr);

        // ----------------------------------------------------------------
        // LOAD (SELECT *)
        // Mirrors: LoadData(bmid, mcno) private overload in Form_Input.vb
        // ----------------------------------------------------------------

        public async Task<ActionCardData?> LoadAsync(
            string bmId, string mcNo, string userType)
        {
            string table = ActionCardTableMap.GetTable(userType);

            string sql = $@"SELECT * FROM [dbo].[{table}]
                            WHERE bmid = @BmId AND Machineno = @McNo";

            await using var conn = Open();
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@BmId", bmId);
            cmd.Parameters.AddWithValue("@McNo", mcNo);

            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return null;

            return new ActionCardData
            {
                BmId = bmId,
                MachineNo = mcNo,
                UserType = userType,
                Status = "Existing",
                ProblemContent = reader["ProblemContent"].ToString()!,
                InCharge = reader["Incharge"].ToString()!,
                DateCard = reader["DateCard"].ToString()!,
                InvestigationDone = reader["InvestigationDone"].ToString()!,
                Action = reader["Action"].ToString()!,
                McStatus = reader["MCStatus"].ToString()!,

                Comment = reader["Comment"].ToString()!,
                Date1 = reader["Date1"].ToString()!,
                Incharge1 = reader["InCharge1"].ToString()!,

                Comment2 = reader["Comment2"].ToString()!,
                Date2 = reader["Date2"].ToString()!,
                Incharge2 = reader["InCharge2"].ToString()!,

                Comment3 = reader["Comment3"].ToString()!,
                Date3 = reader["Date3"].ToString()!,
                Incharge3 = reader["InCharge3"].ToString()!,

                PMReply1 = reader["PMReply1"].ToString()!,
                PMIncharge1 = reader["PMIncharge1"].ToString()!,
                PMDate1 = reader["PMDate1"].ToString()!,

                PMReply2 = reader["PMReply2"].ToString()!,
                PMIncharge2 = reader["PMIncharge2"].ToString()!,
                PMDate2 = reader["PMDate2"].ToString()!,

                PMReply3 = reader["PMReply3"].ToString()!,
                PMIncharge3 = reader["PMIncharge3"].ToString()!,
                PMDate3 = reader["PMDate3"].ToString()!,
            };
        }

        // ----------------------------------------------------------------
        // INSERT
        // Mirrors: SaveData() in Form_Input.vb
        // ----------------------------------------------------------------

        public async Task InsertAsync(ActionCardData d, string userType)
        {
            string table = ActionCardTableMap.GetTable(userType);

            string sql = $@"INSERT INTO [dbo].[{table}]
                (BMID, Machineno, ProblemContent, Incharge, DateCard,
                 InvestigationDone, Action,
                 Comment,  Date1, Incharge1,
                 Comment2, Date2, Incharge2,
                 Comment3, Date3, Incharge3,
                 MCStatus,
                 PMReply1, PMReply2, PMReply3,
                 PMIncharge1, PMIncharge2, PMIncharge3,
                 PMDate1, PMDate2, PMDate3)
            VALUES
                (@BmId, @McNo, @ProblemContent, @Incharge, @DateCard,
                 @Investigation, @Action,
                 @Comment,  @Date1, @Incharge1,
                 @Comment2, @Date2, @Incharge2,
                 @Comment3, @Date3, @Incharge3,
                 @MCStatus,
                 @PMReply1, @PMReply2, @PMReply3,
                 @PMIncharge1, @PMIncharge2, @PMIncharge3,
                 @PMDate1, @PMDate2, @PMDate3)";

            await using var conn = Open();
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            BindParameters(cmd, d);
            await cmd.ExecuteNonQueryAsync();
        }

        // ----------------------------------------------------------------
        // UPDATE
        // Mirrors: UpdateData() in Form_Input.vb
        // ----------------------------------------------------------------

        public async Task UpdateAsync(ActionCardData d, string userType)
        {
            string table = ActionCardTableMap.GetTable(userType);

            string sql = $@"UPDATE [dbo].[{table}]
                SET ProblemContent  = @ProblemContent,
                    Incharge        = @Incharge,
                    InvestigationDone = @Investigation,
                    Action          = @Action,
                    MCStatus        = @MCStatus,
                    Comment         = @Comment,
                    Date1           = @Date1,
                    Incharge1       = @Incharge1,
                    Comment2        = @Comment2,
                    Date2           = @Date2,
                    Incharge2       = @Incharge2,
                    Comment3        = @Comment3,
                    Date3           = @Date3,
                    Incharge3       = @Incharge3,
                    PMReply1        = @PMReply1,
                    PMReply2        = @PMReply2,
                    PMReply3        = @PMReply3,
                    PMIncharge1     = @PMIncharge1,
                    PMIncharge2     = @PMIncharge2,
                    PMIncharge3     = @PMIncharge3,
                    PMDate1         = @PMDate1,
                    PMDate2         = @PMDate2,
                    PMDate3         = @PMDate3
                WHERE BMID = @BmId AND Machineno = @McNo";

            await using var conn = Open();
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            BindParameters(cmd, d);
            await cmd.ExecuteNonQueryAsync();
        }

        // ----------------------------------------------------------------
        // Shared parameter binding (INSERT and UPDATE share same columns)
        // ----------------------------------------------------------------

        private static void BindParameters(SqlCommand cmd, ActionCardData d)
        {
            string mcStatus = d.McStatus == "-SELECT-" ? string.Empty : d.McStatus;

            cmd.Parameters.AddWithValue("@BmId", d.BmId);
            cmd.Parameters.AddWithValue("@McNo", d.MachineNo);
            cmd.Parameters.AddWithValue("@ProblemContent", d.ProblemContent);
            cmd.Parameters.AddWithValue("@Incharge", d.InCharge);
            cmd.Parameters.AddWithValue("@DateCard", d.DateCard);
            cmd.Parameters.AddWithValue("@Investigation", d.InvestigationDone);
            cmd.Parameters.AddWithValue("@Action", d.Action);
            cmd.Parameters.AddWithValue("@MCStatus", mcStatus);

            cmd.Parameters.AddWithValue("@Comment", d.Comment);
            cmd.Parameters.AddWithValue("@Date1", d.Date1);
            cmd.Parameters.AddWithValue("@Incharge1", d.Incharge1);
            cmd.Parameters.AddWithValue("@Comment2", d.Comment2);
            cmd.Parameters.AddWithValue("@Date2", d.Date2);
            cmd.Parameters.AddWithValue("@Incharge2", d.Incharge2);
            cmd.Parameters.AddWithValue("@Comment3", d.Comment3);
            cmd.Parameters.AddWithValue("@Date3", d.Date3);
            cmd.Parameters.AddWithValue("@Incharge3", d.Incharge3);

            cmd.Parameters.AddWithValue("@PMReply1", d.PMReply1);
            cmd.Parameters.AddWithValue("@PMReply2", d.PMReply2);
            cmd.Parameters.AddWithValue("@PMReply3", d.PMReply3);
            cmd.Parameters.AddWithValue("@PMIncharge1", d.PMIncharge1);
            cmd.Parameters.AddWithValue("@PMIncharge2", d.PMIncharge2);
            cmd.Parameters.AddWithValue("@PMIncharge3", d.PMIncharge3);
            cmd.Parameters.AddWithValue("@PMDate1", d.PMDate1);
            cmd.Parameters.AddWithValue("@PMDate2", d.PMDate2);
            cmd.Parameters.AddWithValue("@PMDate3", d.PMDate3);
        }
    }
}