using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using JournalApp.Components.Models;
using Microsoft.Data.Sqlite;

namespace JournalApp.Components.Services
{
    public class AuthDatabase : IDisposable
    {
        private readonly string _dbPath;
        private readonly SqliteConnection _connection;

        public AuthDatabase()
        {
            _dbPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "journal_auth.db");

            _connection = new SqliteConnection($"Data Source={_dbPath}");
            _connection.Open();

            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS Users (
                    UserId INTEGER PRIMARY KEY AUTOINCREMENT,
                    Username TEXT UNIQUE NOT NULL,
                    PasswordHash TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    LastLogin TEXT
                )";
            cmd.ExecuteNonQuery();

            AddDefaultUserIfNeeded();
        }

        private void AddDefaultUserIfNeeded()
        {
            using var checkCmd = _connection.CreateCommand();
            checkCmd.CommandText = "SELECT COUNT(*) FROM Users";
            var count = Convert.ToInt32(checkCmd.ExecuteScalar());

            if (count == 0)
            {
                using var insertCmd = _connection.CreateCommand();
                insertCmd.CommandText = @"
                    INSERT INTO Users (Username, PasswordHash, CreatedAt, LastLogin)
                    VALUES (@username, @passwordHash, @createdAt, @lastLogin)";
                
                insertCmd.Parameters.AddWithValue("@username", "user");
                insertCmd.Parameters.AddWithValue("@passwordHash", HashPassword("password"));
                insertCmd.Parameters.AddWithValue("@createdAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                insertCmd.Parameters.AddWithValue("@lastLogin", DBNull.Value);

                insertCmd.ExecuteNonQuery();
            }
        }

        private string HashPassword(string password)
        {
            using var sha256 = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(password);
            var hash = sha256.ComputeHash(bytes);
            return Convert.ToBase64String(hash);
        }

        public async Task<bool> UserExists(string username)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Users WHERE Username = @username";
            cmd.Parameters.AddWithValue("@username", username);

            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(result) > 0;
        }

        public async Task<User?> GetUserByUsername(string username)
        {
            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = @"
                    SELECT UserId, Username, CreatedAt, LastLogin
                    FROM Users
                    WHERE Username = @username
                    LIMIT 1";
                cmd.Parameters.AddWithValue("@username", username);

                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return new User
                    {
                        UserId = reader.GetInt32(0),
                        Username = reader.GetString(1),
                        CreatedAt = DateTime.Parse(reader.GetString(2)),
                        LastLogin = reader.IsDBNull(3) ? null : DateTime.Parse(reader.GetString(3))
                    };
                }

                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"GetUserByUsername error: {ex.Message}");
                return null;
            }
        }

        public async Task<User?> Login(string username, string password)
        {
            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = @"
                    SELECT UserId, Username, CreatedAt, LastLogin
                    FROM Users
                    WHERE Username = @username AND PasswordHash = @passwordHash";
                cmd.Parameters.AddWithValue("@username", username);
                cmd.Parameters.AddWithValue("@passwordHash", HashPassword(password));

                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    var user = new User
                    {
                        UserId = reader.GetInt32(0),
                        Username = reader.GetString(1),
                        CreatedAt = DateTime.Parse(reader.GetString(2)),
                        LastLogin = reader.IsDBNull(3) ? null : DateTime.Parse(reader.GetString(3))
                    };

                    await UpdateLastLogin(user.UserId);
                    return user;
                }

                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Login error: {ex.Message}");
                return null;
            }
        }

        private async Task UpdateLastLogin(int userId)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "UPDATE Users SET LastLogin = @lastLogin WHERE UserId = @userId";
            cmd.Parameters.AddWithValue("@lastLogin", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@userId", userId);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<bool> Register(string username, string password)
        {
            if (await UserExists(username)) return false;

            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO Users (Username, PasswordHash, CreatedAt)
                VALUES (@username, @passwordHash, @createdAt)";
            cmd.Parameters.AddWithValue("@username", username);
            cmd.Parameters.AddWithValue("@passwordHash", HashPassword(password));
            cmd.Parameters.AddWithValue("@createdAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            await cmd.ExecuteNonQueryAsync();
            return true;
        }

        public async Task<bool> ChangePassword(int userId, string currentPassword, string newPassword)
        {
            using var verifyCmd = _connection.CreateCommand();
            verifyCmd.CommandText = "SELECT COUNT(*) FROM Users WHERE UserId = @userId AND PasswordHash = @passwordHash";
            verifyCmd.Parameters.AddWithValue("@userId", userId);
            verifyCmd.Parameters.AddWithValue("@passwordHash", HashPassword(currentPassword));

            var isValid = Convert.ToInt32(await verifyCmd.ExecuteScalarAsync()) > 0;
            if (!isValid) return false;

            using var updateCmd = _connection.CreateCommand();
            updateCmd.CommandText = "UPDATE Users SET PasswordHash = @newPasswordHash WHERE UserId = @userId";
            updateCmd.Parameters.AddWithValue("@newPasswordHash", HashPassword(newPassword));
            updateCmd.Parameters.AddWithValue("@userId", userId);

            await updateCmd.ExecuteNonQueryAsync();
            return true;
        }

        public async Task<User?> GetUserById(int userId)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
                SELECT UserId, Username, CreatedAt, LastLogin
                FROM Users
                WHERE UserId = @userId";
            cmd.Parameters.AddWithValue("@userId", userId);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new User
                {
                    UserId = reader.GetInt32(0),
                    Username = reader.GetString(1),
                    CreatedAt = DateTime.Parse(reader.GetString(2)),
                    LastLogin = reader.IsDBNull(3) ? null : DateTime.Parse(reader.GetString(3))
                };
            }

            return null;
        }

        public async Task<bool> DeleteAccount(int userId, string password)
        {
            using var verifyCmd = _connection.CreateCommand();
            verifyCmd.CommandText = "SELECT COUNT(*) FROM Users WHERE UserId = @userId AND PasswordHash = @passwordHash";
            verifyCmd.Parameters.AddWithValue("@userId", userId);
            verifyCmd.Parameters.AddWithValue("@passwordHash", HashPassword(password));

            var isValid = Convert.ToInt32(await verifyCmd.ExecuteScalarAsync()) > 0;
            if (!isValid) return false;

            using var deleteCmd = _connection.CreateCommand();
            deleteCmd.CommandText = "DELETE FROM Users WHERE UserId = @userId";
            deleteCmd.Parameters.AddWithValue("@userId", userId);

            await deleteCmd.ExecuteNonQueryAsync();
            return true;
        }

        public async Task<int> GetTotalUsers()
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Users";
            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }

        public void Dispose()
        {
            _connection?.Close();
            _connection?.Dispose();
        }
    }
}