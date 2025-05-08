using System;
using System.Data.SQLite;
using System.IO;
using Windows_Forms_Chat;

namespace Windows_Forms_Chat
{
    public class DbUserManager
    {
        private static int guestCounter = 0;
        private static object counterLock = new object();
        private static string _connectionString = "Data Source=ChatAppDB.sqlite;Version=3;";

        public DbUserManager(string connectionString = null)
        {
            if (!string.IsNullOrEmpty(connectionString))
            {
                _connectionString = connectionString;
            }
            InitializeDatabase();
        }

        private static void InitializeDatabase()
        {
            string dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ChatAppDB.sqlite");

            try
            {
                if (!File.Exists(dbPath))
                {
                    SQLiteConnection.CreateFile(dbPath);
                    Console.WriteLine($"Created new database file: {Path.GetFileName(dbPath)}");
                }

                using (var conn = new SQLiteConnection($"Data Source={dbPath};Version=3;"))
                {
                    conn.Open();
                    string createUsersTable = @"
                CREATE TABLE IF NOT EXISTS Users (
                    ID INTEGER PRIMARY KEY AUTOINCREMENT,
                    Username TEXT UNIQUE NOT NULL,
                    Password TEXT NOT NULL,
                    Wins INTEGER DEFAULT 0,
                    Losses INTEGER DEFAULT 0,
                    Draws INTEGER DEFAULT 0
                )";

                    using (var cmd = new SQLiteCommand(createUsersTable, conn))
                    {
                        cmd.ExecuteNonQuery();
                    }
                }

                Console.WriteLine("Database schema verified!");
            }
            catch (SQLiteException sqlex)
            {
                Console.WriteLine($"[DATABASE ERROR] SQLite: {sqlex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DATABASE ERROR] General: {ex.Message}");
            }
        }

        public static string GenerateLobbyUsername()
        {
            lock (counterLock) // Thread-safe increment
            {
                guestCounter++;
                Console.WriteLine($"[COUNTER] Generated: user{guestCounter}"); // Debug
                return $"user{guestCounter}";
            }
        }
        public bool UpdateUsername(string oldUsername, string newUsername)
        {
            using (var conn = new SQLiteConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new SQLiteCommand("UPDATE Users SET Username = @NewUsername WHERE Username = @OldUsername", conn))
                {
                    cmd.Parameters.AddWithValue("@NewUsername", newUsername);
                    cmd.Parameters.AddWithValue("@OldUsername", oldUsername);

                    int rowsAffected = cmd.ExecuteNonQuery();
                    return rowsAffected > 0;
                }
            }
        }

        public void UpdateGameStats(string username, int win, int loss, int draw, string opponent)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();

                string query = "UPDATE Users SET Wins = Wins + @Win, Losses = Losses + @Loss, Draws = Draws + @Draw WHERE Username = @Username";
                using (var command = new SQLiteCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Win", win);
                    command.Parameters.AddWithValue("@Loss", loss);
                    command.Parameters.AddWithValue("@Draw", draw);
                    command.Parameters.AddWithValue("@Username", username);
                    command.ExecuteNonQuery();
                }
            }
        }


        public static bool DbUsernameCheck(string username)
        {
            try
            {
                Console.WriteLine($"[DEBUG] DbUsernameCheck entered. Connection string: {(_connectionString != null ? "SET" : "NULL")}");

                using (var connection = new SQLiteConnection(_connectionString))
                {
                    Console.WriteLine($"[DEBUG] Connection created. State: {connection.State}");

                    connection.Open();
                    Console.WriteLine($"[DEBUG] Connection opened. State: {connection.State}");

                    string query = "SELECT COUNT(*) FROM Users WHERE Username = @Username";
                    Console.WriteLine($"[DEBUG] Query prepared: {query}");

                    using (var command = new SQLiteCommand(query, connection))
                    {
                        command.Parameters.AddWithValue("@Username", username);
                        Console.WriteLine($"[DEBUG] Parameter added. Username: {username}");

                        var result = command.ExecuteScalar();
                        Console.WriteLine($"[DEBUG] Query executed. Result: {result}");

                        int count = Convert.ToInt32(result);
                        Console.WriteLine($"[DEBUG] Count: {count}");

                        return count > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DEBUG ERROR] In DbUsernameCheck: {ex.GetType().Name} - {ex.Message}");
                Console.WriteLine($"[DEBUG STACK] {ex.StackTrace}");
                throw; // Re-throw to preserve original error behavior
            }
        }

        public bool TryRegisterUser(string username, string password)
        {
            if (DbUsernameCheck(username)) return false;

            string hashedPassword = SecurityHelper.HashPassword(password);

            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                string query = "INSERT INTO Users (Username, Password) VALUES (@Username, @Password)";

                using (var command = new SQLiteCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Username", username);
                    command.Parameters.AddWithValue("@Password", hashedPassword);
                    return command.ExecuteNonQuery() > 0;
                }
            }
        }

        public bool TryAuthenticateUser(string username, string password)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                connection.Open();
                string query = "SELECT Password FROM Users WHERE Username = @Username";

                using (var command = new SQLiteCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Username", username);
                    var result = command.ExecuteScalar();

                    if (result == null) return false;

                    string storedHashedPassword = result.ToString();
                    string inputHashed = SecurityHelper.HashPassword(password);

                    return storedHashedPassword == inputHashed;
                }
            }
        }

    }
}
