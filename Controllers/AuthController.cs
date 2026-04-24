using Microsoft.AspNetCore.Mvc;
using MySqlConnector;
using EleaveAPI.Models;

namespace EleaveAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IConfiguration _config;

    public AuthController(IConfiguration config)
    {
        _config = config;
    }

    [HttpPost("login")]
    public IActionResult Login([FromBody] LoginRequest request)
    {
        var connStr = _config.GetConnectionString("DefaultConnection");
        Console.WriteLine("CONNECTION STRING: " + connStr);

        using var conn = new MySqlConnection(connStr);
        conn.Open();

        var debugQuery = "SELECT DATABASE()";
        using var debugCmd = new MySqlCommand(debugQuery, conn);
        var dbName = debugCmd.ExecuteScalar();
        Console.WriteLine("CURRENT DB: " + dbName);

        var query = "SELECT * FROM users WHERE email = @Email AND password = @Password";

        using var cmd = new MySqlCommand(query, conn);
        cmd.Parameters.AddWithValue("@Email", request.Email);
        cmd.Parameters.AddWithValue("@Password", request.Password);

        using var reader = cmd.ExecuteReader();

        if (reader.Read())
        {
            return Ok(new
            {
                message = "Login success",
                user = new
                {
                    id = reader["id"],
                    name = reader["name"],
                    role = reader["role"]
                }
            });
        }

        return Unauthorized(new { message = "Invalid email or password" });
    }
    [HttpPost("register")]
    public IActionResult Register([FromBody] RegisterRequest request)
    {
        using var conn = new MySqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();

        // cek email
        var checkQuery = "SELECT COUNT(*) FROM users WHERE email = @Email";
        using var checkCmd = new MySqlCommand(checkQuery, conn);
        checkCmd.Parameters.AddWithValue("@Email", request.Email);

        var count = Convert.ToInt32(checkCmd.ExecuteScalar());

        if (count > 0)
        {
            return BadRequest(new { message = "Email already registered" });
        }

        // insert user
        var insertQuery = @"
            INSERT INTO users (name, email, password, role, supervisor_id, created_at)
            VALUES (@Name, @Email, @Password, @Role, @SupervisorId, NOW())";

        using var cmd = new MySqlCommand(insertQuery, conn);
        cmd.Parameters.AddWithValue("@Name", request.Name);
        cmd.Parameters.AddWithValue("@Email", request.Email);
        cmd.Parameters.AddWithValue("@Password", request.Password);
        cmd.Parameters.AddWithValue("@Role", request.Role);
        cmd.Parameters.AddWithValue("@SupervisorId", request.SupervisorId);

        cmd.ExecuteNonQuery();

        return Ok(new { message = "Register success" });
    }

}
