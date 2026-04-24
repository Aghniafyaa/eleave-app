using Microsoft.AspNetCore.Mvc;
using MySqlConnector;
using EleaveAPI.Models;

namespace EleaveAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LeaveController : ControllerBase
{
    private readonly IConfiguration _config;

    public LeaveController(IConfiguration config)
    {
        _config = config;
    }

    [HttpPost("request")]
    public IActionResult CreateLeave([FromBody] LeaveRequest request)
    {
        using var conn = new MySqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();

        if (request.StartDate > request.EndDate)
        {
            return BadRequest(new { message = "Start date cannot be after end date" });
        }

        var checkTypeQuery = "SELECT COUNT(*) FROM leave_types WHERE id = @LeaveTypeId";
        using var checkCmd = new MySqlCommand(checkTypeQuery, conn);
        checkCmd.Parameters.AddWithValue("@LeaveTypeId", request.LeaveTypeId);

        var typeExists = Convert.ToInt32(checkCmd.ExecuteScalar());

        if (typeExists == 0)
        {
            return BadRequest(new { message = "Invalid leave type" });
        }

        var query = @"
            INSERT INTO leave_requests (user_id, leave_type_id, start_date, end_date, status, created_at)
            VALUES (@UserId, @LeaveTypeId, @StartDate, @EndDate, 'Pending', NOW())";

        using var cmd = new MySqlCommand(query, conn);
        cmd.Parameters.AddWithValue("@UserId", request.UserId);
        cmd.Parameters.AddWithValue("@LeaveTypeId", request.LeaveTypeId);
        cmd.Parameters.AddWithValue("@StartDate", request.StartDate);
        cmd.Parameters.AddWithValue("@EndDate", request.EndDate);

        cmd.ExecuteNonQuery();

        return Ok(new { message = "Leave request submitted" });
    }

    [HttpGet("types")]
    public IActionResult GetLeaveTypes()
    {
        using var conn = new MySqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();

        var query = "SELECT id, name, max_days FROM leave_types";

        using var cmd = new MySqlCommand(query, conn);
        using var reader = cmd.ExecuteReader();

        var result = new List<object>();

        while (reader.Read())
        {
            result.Add(new
            {
                id = reader["id"],
                name = reader["name"],
                maxDays = reader["max_days"]
            });
        }

        return Ok(result);
    }

    [HttpGet("pending/{supervisorId}")]
    public IActionResult GetPendingRequests(int supervisorId)
    {
    using var conn = new MySqlConnection(_config.GetConnectionString("DefaultConnection"));
    conn.Open();

    var query = @"
        SELECT lr.id, u.name, lt.name AS leave_type,
               lr.start_date, lr.end_date, lr.status
        FROM leave_requests lr
        JOIN users u ON lr.user_id = u.id
        JOIN leave_types lt ON lr.leave_type_id = lt.id
        WHERE u.supervisor_id = @SupervisorId
        AND lr.status = 'Pending'";

    using var cmd = new MySqlCommand(query, conn);
    cmd.Parameters.AddWithValue("@SupervisorId", supervisorId);

    using var reader = cmd.ExecuteReader();

    var result = new List<object>();

    while (reader.Read())
    {
        result.Add(new
        {
            id = reader["id"],
            employeeName = reader["name"],
            leaveType = reader["leave_type"],
            startDate = reader["start_date"],
            endDate = reader["end_date"],
            status = reader["status"]
        });
    }

    return Ok(result);
    }

    [HttpPost("approve")]
    public IActionResult Approve([FromBody] ApprovalRequest request)
    {
        var connStr = _config.GetConnectionString("DefaultConnection");

        using var conn = new MySqlConnection(connStr);
        conn.Open();

        using var transaction = conn.BeginTransaction();

        try
        {
            // 1. Ambil data request
            var getRequestQuery = @"
                SELECT user_id, leave_type_id, start_date, end_date, status 
                FROM leave_requests 
                WHERE id = @RequestId";

            using var getCmd = new MySqlCommand(getRequestQuery, conn, transaction);
            getCmd.Parameters.AddWithValue("@RequestId", request.RequestId);

            using var reader = getCmd.ExecuteReader();

            if (!reader.Read())
            {
                transaction.Rollback();
                return NotFound(new { message = "Request not found" });
            }

            var userId = Convert.ToInt32(reader["user_id"]);
            var leaveTypeId = Convert.ToInt32(reader["leave_type_id"]);
            var startDate = Convert.ToDateTime(reader["start_date"]);
            var endDate = Convert.ToDateTime(reader["end_date"]);
            var currentStatus = reader["status"].ToString();

            reader.Close();

            if (currentStatus != "Pending")
            {
                transaction.Rollback();
                return BadRequest(new { message = "Already processed" });
            }

            // 2. Hitung hari
            int totalDays = (endDate - startDate).Days + 1;

            // 3. Cek saldo
            var checkBalanceQuery = @"
                SELECT remaining_days 
                FROM leave_balances 
                WHERE user_id = @UserId 
                AND leave_type_id = @LeaveTypeId 
                AND year = YEAR(CURDATE())";

            using var balanceCmd = new MySqlCommand(checkBalanceQuery, conn, transaction);
            balanceCmd.Parameters.AddWithValue("@UserId", userId);
            balanceCmd.Parameters.AddWithValue("@LeaveTypeId", leaveTypeId);

            var result = balanceCmd.ExecuteScalar();

            if (result == null)
            {
                transaction.Rollback();
                return BadRequest(new { message = "Leave balance not found" });
            }

            int remaining = Convert.ToInt32(result);

            if (remaining < totalDays)
            {
                transaction.Rollback();
                return BadRequest(new
                {
                    message = "Insufficient leave balance",
                    remaining,
                    requested = totalDays
                });
            }

            // 4. Update status
            var updateStatusQuery = @"
                UPDATE leave_requests 
                SET status = @Status 
                WHERE id = @RequestId";

            using var updateCmd = new MySqlCommand(updateStatusQuery, conn, transaction);
            updateCmd.Parameters.AddWithValue("@Status", request.Status);
            updateCmd.Parameters.AddWithValue("@RequestId", request.RequestId);
            updateCmd.ExecuteNonQuery();

            // 5. Potong saldo kalau approved
            if (request.Status.ToLower() == "approved")
            {
                var updateBalanceQuery = @"
                    UPDATE leave_balances
                    SET remaining_days = remaining_days - @Days
                    WHERE user_id = @UserId 
                    AND leave_type_id = @LeaveTypeId
                    AND year = YEAR(CURDATE())";

                using var cutCmd = new MySqlCommand(updateBalanceQuery, conn, transaction);
                cutCmd.Parameters.AddWithValue("@Days", totalDays);
                cutCmd.Parameters.AddWithValue("@UserId", userId);
                cutCmd.Parameters.AddWithValue("@LeaveTypeId", leaveTypeId);

                cutCmd.ExecuteNonQuery();
            }

            transaction.Commit();

            return Ok(new
            {
                message = "Request processed",
                days_used = totalDays
            });
        }
        catch (Exception ex)
        {
            transaction.Rollback();

            return StatusCode(500, new
            {
                message = "Something went wrong",
                error = ex.Message
            });
        }
    }

    [HttpGet("balance/{userId}")]
    public IActionResult GetLeaveBalance(int userId)
    {
        using var conn = new MySqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();

        var query = @"
            SELECT 
                lt.name AS leave_type,
                lt.max_days,
                lb.remaining_days
            FROM leave_balances lb
            JOIN leave_types lt ON lb.leave_type_id = lt.id
            WHERE lb.user_id = @UserId
            AND lb.year = YEAR(CURDATE())";

        using var cmd = new MySqlCommand(query, conn);
        cmd.Parameters.AddWithValue("@UserId", userId);

        using var reader = cmd.ExecuteReader();

        var result = new List<object>();

        while (reader.Read())
        {
            int maxDays = Convert.ToInt32(reader["max_days"]);
            int remaining = Convert.ToInt32(reader["remaining_days"]);

            result.Add(new
            {
                leaveType = reader["leave_type"],
                maxDays = maxDays,
                usedDays = maxDays - remaining,
                remainingDays = remaining
            });
        }

        return Ok(result);
    }

    [HttpGet("requests/{supervisorId}")]
    public IActionResult GetAllRequests(int supervisorId, [FromQuery] string? status)
    {
        using var conn = new MySqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();

        var query = @"
            SELECT 
                lr.id,
                u.name AS employee_name,
                lt.name AS leave_type,
                lr.start_date,
                lr.end_date,
                lr.status,
                lr.created_at
            FROM leave_requests lr
            JOIN users u ON lr.user_id = u.id
            JOIN leave_types lt ON lr.leave_type_id = lt.id
            WHERE u.supervisor_id = @SupervisorId
        ";

        // 🔥 optional filter status
        if (!string.IsNullOrEmpty(status))
        {
            query += " AND lr.status = @Status";
        }

        query += " ORDER BY lr.created_at DESC";

        using var cmd = new MySqlCommand(query, conn);
        cmd.Parameters.AddWithValue("@SupervisorId", supervisorId);

        if (!string.IsNullOrEmpty(status))
        {
            cmd.Parameters.AddWithValue("@Status", status);
        }

        using var reader = cmd.ExecuteReader();

        var result = new List<object>();

        while (reader.Read())
        {
            var startDate = Convert.ToDateTime(reader["start_date"]);
            var endDate = Convert.ToDateTime(reader["end_date"]);

            result.Add(new
            {
                id = reader["id"],
                employeeName = reader["employee_name"],
                leaveType = reader["leave_type"],
                startDate = startDate,
                endDate = endDate,
                totalDays = (endDate - startDate).Days + 1,
                status = reader["status"],
                createdAt = reader["created_at"]
            });
        }

        return Ok(result);
    }

    [HttpGet("my-history/{userId}")]
    public IActionResult GetMyLeaveHistory(int userId)
    {
        using var conn = new MySqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();

        var query = @"
            SELECT 
                lr.id,
                lt.name AS leave_type,
                lr.start_date,
                lr.end_date,
                lr.status,
                lr.created_at
            FROM leave_requests lr
            JOIN leave_types lt ON lr.leave_type_id = lt.id
            WHERE lr.user_id = @UserId
            ORDER BY lr.created_at DESC
        ";

        using var cmd = new MySqlCommand(query, conn);
        cmd.Parameters.AddWithValue("@UserId", userId);

        using var reader = cmd.ExecuteReader();

        var result = new List<object>();

        while (reader.Read())
        {
            var startDate = Convert.ToDateTime(reader["start_date"]);
            var endDate = Convert.ToDateTime(reader["end_date"]);

            result.Add(new
            {
                id = reader["id"],
                leaveType = reader["leave_type"],
                startDate = startDate,
                endDate = endDate,
                totalDays = (endDate - startDate).Days + 1,
                status = reader["status"],
                createdAt = reader["created_at"]
            });
        }

        return Ok(result);
    }
}