using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using JournalApp.Components.Models;
using Microsoft.Data.Sqlite;

namespace JournalApp.Components.Services
{
    public class DatabaseService : IDisposable
    {
        private readonly string _dbPath;
        private SqliteConnection _connection;

        public DatabaseService()
        {
            _dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "journal.db");
            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            _connection = new SqliteConnection($"Data Source={_dbPath}");
            _connection.Open();

            // Create table if it doesn't exist with the correct column order
            using var command = _connection.CreateCommand();
            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS JournalEntries (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    UserId INTEGER NOT NULL DEFAULT 1,
                    Title TEXT,
                    Content TEXT,
                    EntryDate TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL,
                    PrimaryMood TEXT,
                    SecondaryMood1 TEXT,
                    SecondaryMood2 TEXT,
                    MoodCategory TEXT,
                    Category TEXT,
                    Tags TEXT
                )";
            command.ExecuteNonQuery();
        }

        private JournalEntry ReadEntry(SqliteDataReader reader)
        {
            try
            {
                // Use GetOrdinal to get column indices dynamically to avoid order issues
                int idIndex = reader.GetOrdinal("Id");
                int userIdIndex = reader.GetOrdinal("UserId");
                int titleIndex = reader.GetOrdinal("Title");
                int contentIndex = reader.GetOrdinal("Content");
                int entryDateIndex = reader.GetOrdinal("EntryDate");
                int createdAtIndex = reader.GetOrdinal("CreatedAt");
                int updatedAtIndex = reader.GetOrdinal("UpdatedAt");
                int primaryMoodIndex = reader.GetOrdinal("PrimaryMood");
                int secondaryMood1Index = reader.GetOrdinal("SecondaryMood1");
                int secondaryMood2Index = reader.GetOrdinal("SecondaryMood2");
                int moodCategoryIndex = reader.GetOrdinal("MoodCategory");
                int categoryIndex = reader.GetOrdinal("Category");
                int tagsIndex = reader.GetOrdinal("Tags");

                return new JournalEntry
                {
                    Id = reader.GetInt32(idIndex),
                    UserId = reader.GetInt32(userIdIndex),
                    Title = reader.IsDBNull(titleIndex) ? "" : reader.GetString(titleIndex),
                    Content = reader.IsDBNull(contentIndex) ? "" : reader.GetString(contentIndex),
                    EntryDate = SafeParseDateTime(reader.GetString(entryDateIndex)),
                    CreatedAt = SafeParseDateTime(reader.GetString(createdAtIndex)),
                    UpdatedAt = SafeParseDateTime(reader.GetString(updatedAtIndex)),
                    PrimaryMood = reader.IsDBNull(primaryMoodIndex) ? "" : reader.GetString(primaryMoodIndex),
                    SecondaryMood1 = reader.IsDBNull(secondaryMood1Index) ? "" : reader.GetString(secondaryMood1Index),
                    SecondaryMood2 = reader.IsDBNull(secondaryMood2Index) ? "" : reader.GetString(secondaryMood2Index),
                    MoodCategory = reader.IsDBNull(moodCategoryIndex) ? "" : reader.GetString(moodCategoryIndex),
                    Category = reader.IsDBNull(categoryIndex) ? "" : reader.GetString(categoryIndex),
                    Tags = reader.IsDBNull(tagsIndex) ? "" : reader.GetString(tagsIndex)
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading entry: {ex.Message}");
                // Return empty entry if there's an error
                return new JournalEntry();
            }
        }

        private DateTime SafeParseDateTime(string dateString)
        {
            if (string.IsNullOrEmpty(dateString))
                return DateTime.Now;

            try
            {
                // Try parsing with default method
                if (DateTime.TryParse(dateString, out DateTime result))
                    return result;

                // Try parsing with specific formats
                string[] formats = { 
                    "yyyy-MM-dd", 
                    "yyyy-MM-dd HH:mm:ss", 
                    "MM/dd/yyyy", 
                    "MM/dd/yyyy HH:mm:ss",
                    "dd/MM/yyyy",
                    "dd/MM/yyyy HH:mm:ss"
                };

                foreach (var format in formats)
                {
                    if (DateTime.TryParseExact(dateString, format, 
                        System.Globalization.CultureInfo.InvariantCulture, 
                        System.Globalization.DateTimeStyles.None, out result))
                        return result;
                }

                Console.WriteLine($"Warning: Could not parse date string: '{dateString}'");
                return DateTime.Now;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing date '{dateString}': {ex.Message}");
                return DateTime.Now;
            }
        }

        // ========== NEW METHOD: Get Entries by Date Range ==========
        public async Task<List<JournalEntry>> GetEntriesByDateRange(DateTime startDate, DateTime endDate, int userId)
        {
            var entries = new List<JournalEntry>();
            
            try
            {
                // Format dates for SQLite comparison
                var startDateString = startDate.ToString("yyyy-MM-dd");
                var endDateString = endDate.ToString("yyyy-MM-dd");
                
                Console.WriteLine($"Getting entries for user {userId} from {startDateString} to {endDateString}");

                using var command = _connection.CreateCommand();
                command.CommandText = @"
                    SELECT Id, UserId, Title, Content, EntryDate, CreatedAt, UpdatedAt, 
                           PrimaryMood, SecondaryMood1, SecondaryMood2, MoodCategory, Category, Tags 
                    FROM JournalEntries 
                    WHERE UserId = @userId 
                    AND EntryDate >= @startDate 
                    AND EntryDate <= @endDate
                    ORDER BY EntryDate DESC";
                
                command.Parameters.AddWithValue("@userId", userId);
                command.Parameters.AddWithValue("@startDate", startDateString);
                command.Parameters.AddWithValue("@endDate", endDateString);

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var entry = ReadEntry(reader);
                    if (entry != null && entry.Id > 0) // Only add valid entries
                        entries.Add(entry);
                }
                
                Console.WriteLine($"Found {entries.Count} entries in date range for user {userId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetEntriesByDateRange: {ex.Message}");
                // If the above query fails, try alternative approach
                entries = await GetEntriesByDateRangeFallback(startDate, endDate, userId);
            }
            
            return entries;
        }

        // Fallback method for date range query
        private async Task<List<JournalEntry>> GetEntriesByDateRangeFallback(DateTime startDate, DateTime endDate, int userId)
        {
            var entries = new List<JournalEntry>();
            
            try
            {
                // Get all entries for the user and filter in memory
                var allEntries = await GetEntriesByUserId(userId);
                
                // Filter by date range
                entries = allEntries
                    .Where(e => e.EntryDate.Date >= startDate.Date && e.EntryDate.Date <= endDate.Date)
                    .OrderByDescending(e => e.EntryDate)
                    .ToList();
                
                Console.WriteLine($"Fallback method found {entries.Count} entries in date range");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetEntriesByDateRangeFallback: {ex.Message}");
            }
            
            return entries;
        }

        // Get entry for a specific date by user
        public async Task<JournalEntry?> GetEntryForDate(DateTime date, int userId)
        {
            try
            {
                var dateString = date.ToString("yyyy-MM-dd");
                
                using var command = _connection.CreateCommand();
                command.CommandText = @"
                    SELECT Id, UserId, Title, Content, EntryDate, CreatedAt, UpdatedAt, 
                           PrimaryMood, SecondaryMood1, SecondaryMood2, MoodCategory, Category, Tags 
                    FROM JournalEntries 
                    WHERE UserId = @userId 
                    AND EntryDate LIKE @entryDatePattern 
                    LIMIT 1";
                
                command.Parameters.AddWithValue("@userId", userId);
                command.Parameters.AddWithValue("@entryDatePattern", $"{dateString}%");

                using var reader = await command.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    var entry = ReadEntry(reader);
                    if (entry.Id > 0)
                        return entry;
                }

                Console.WriteLine($"No entry found for date {date:yyyy-MM-dd}");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetEntryForDate: {ex.Message}");
                return null;
            }
        }

        // Get entries by user ID
        public async Task<List<JournalEntry>> GetEntriesByUserId(int userId)
        {
            var entries = new List<JournalEntry>();
            
            try
            {
                using var command = _connection.CreateCommand();
                command.CommandText = @"
                    SELECT Id, UserId, Title, Content, EntryDate, CreatedAt, UpdatedAt, 
                           PrimaryMood, SecondaryMood1, SecondaryMood2, MoodCategory, Category, Tags 
                    FROM JournalEntries 
                    WHERE UserId = @userId 
                    ORDER BY EntryDate DESC";
                
                command.Parameters.AddWithValue("@userId", userId);

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var entry = ReadEntry(reader);
                    if (entry != null && entry.Id > 0) // Only add valid entries
                        entries.Add(entry);
                }
                
                Console.WriteLine($"Loaded {entries.Count} entries for user {userId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetEntriesByUserId: {ex.Message}");
            }
            
            return entries;
        }

        // Get entries by mood
        public async Task<List<JournalEntry>> GetEntriesByMood(string mood, int userId)
        {
            var entries = new List<JournalEntry>();
            
            try
            {
                using var command = _connection.CreateCommand();
                command.CommandText = @"
                    SELECT Id, UserId, Title, Content, EntryDate, CreatedAt, UpdatedAt, 
                           PrimaryMood, SecondaryMood1, SecondaryMood2, MoodCategory, Category, Tags 
                    FROM JournalEntries 
                    WHERE UserId = @userId 
                    AND (PrimaryMood = @mood OR SecondaryMood1 = @mood OR SecondaryMood2 = @mood)
                    ORDER BY EntryDate DESC";
                
                command.Parameters.AddWithValue("@userId", userId);
                command.Parameters.AddWithValue("@mood", mood);

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var entry = ReadEntry(reader);
                    if (entry != null && entry.Id > 0)
                        entries.Add(entry);
                }
                
                Console.WriteLine($"Found {entries.Count} entries with mood '{mood}' for user {userId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetEntriesByMood: {ex.Message}");
            }
            
            return entries;
        }

        // Get entries by tag
        public async Task<List<JournalEntry>> GetEntriesByTag(string tag, int userId)
        {
            var entries = new List<JournalEntry>();
            
            try
            {
                using var command = _connection.CreateCommand();
                command.CommandText = @"
                    SELECT Id, UserId, Title, Content, EntryDate, CreatedAt, UpdatedAt, 
                           PrimaryMood, SecondaryMood1, SecondaryMood2, MoodCategory, Category, Tags 
                    FROM JournalEntries 
                    WHERE UserId = @userId 
                    AND Tags LIKE @tagPattern
                    ORDER BY EntryDate DESC";
                
                command.Parameters.AddWithValue("@userId", userId);
                command.Parameters.AddWithValue("@tagPattern", $"%{tag}%");

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var entry = ReadEntry(reader);
                    if (entry != null && entry.Id > 0)
                        entries.Add(entry);
                }
                
                Console.WriteLine($"Found {entries.Count} entries with tag '{tag}' for user {userId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetEntriesByTag: {ex.Message}");
            }
            
            return entries;
        }

        // Search entries
        public async Task<List<JournalEntry>> SearchEntries(string searchTerm, int userId)
        {
            var entries = new List<JournalEntry>();
            
            try
            {
                using var command = _connection.CreateCommand();
                command.CommandText = @"
                    SELECT Id, UserId, Title, Content, EntryDate, CreatedAt, UpdatedAt, 
                           PrimaryMood, SecondaryMood1, SecondaryMood2, MoodCategory, Category, Tags 
                    FROM JournalEntries 
                    WHERE UserId = @userId 
                    AND (Title LIKE @search OR Content LIKE @search OR Tags LIKE @search)
                    ORDER BY EntryDate DESC";
                
                command.Parameters.AddWithValue("@userId", userId);
                command.Parameters.AddWithValue("@search", $"%{searchTerm}%");

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var entry = ReadEntry(reader);
                    if (entry != null && entry.Id > 0)
                        entries.Add(entry);
                }
                
                Console.WriteLine($"Found {entries.Count} entries matching '{searchTerm}' for user {userId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in SearchEntries: {ex.Message}");
            }
            
            return entries;
        }

        // GetAllEntries with userId filter
        public async Task<List<JournalEntry>> GetAllEntries(int? userId = null)
        {
            var entries = new List<JournalEntry>();
            
            try
            {
                using var command = _connection.CreateCommand();
                
                if (userId.HasValue)
                {
                    command.CommandText = @"
                        SELECT Id, UserId, Title, Content, EntryDate, CreatedAt, UpdatedAt, 
                               PrimaryMood, SecondaryMood1, SecondaryMood2, MoodCategory, Category, Tags 
                        FROM JournalEntries 
                        WHERE UserId = @userId 
                        ORDER BY EntryDate DESC";
                    command.Parameters.AddWithValue("@userId", userId.Value);
                }
                else
                {
                    command.CommandText = @"
                        SELECT Id, UserId, Title, Content, EntryDate, CreatedAt, UpdatedAt, 
                               PrimaryMood, SecondaryMood1, SecondaryMood2, MoodCategory, Category, Tags 
                        FROM JournalEntries 
                        ORDER BY EntryDate DESC";
                }

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var entry = ReadEntry(reader);
                    if (entry != null && entry.Id > 0)
                        entries.Add(entry);
                }
                
                Console.WriteLine($"Loaded {entries.Count} total entries");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetAllEntries: {ex.Message}");
            }
            
            return entries;
        }

        // Get entry by ID with user verification
        public async Task<JournalEntry?> GetEntryById(int id, int? userId = null)
        {
            try
            {
                using var command = _connection.CreateCommand();
                
                if (userId.HasValue)
                {
                    command.CommandText = @"
                        SELECT Id, UserId, Title, Content, EntryDate, CreatedAt, UpdatedAt, 
                               PrimaryMood, SecondaryMood1, SecondaryMood2, MoodCategory, Category, Tags 
                        FROM JournalEntries 
                        WHERE Id = @id AND UserId = @userId";
                    command.Parameters.AddWithValue("@userId", userId.Value);
                }
                else
                {
                    command.CommandText = @"
                        SELECT Id, UserId, Title, Content, EntryDate, CreatedAt, UpdatedAt, 
                               PrimaryMood, SecondaryMood1, SecondaryMood2, MoodCategory, Category, Tags 
                        FROM JournalEntries 
                        WHERE Id = @id";
                }
                
                command.Parameters.AddWithValue("@id", id);

                using var reader = await command.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    var entry = ReadEntry(reader);
                    if (entry.Id > 0)
                        return entry;
                }

                Console.WriteLine($"No entry found with ID {id}");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetEntryById: {ex.Message}");
                return null;
            }
        }

        // Save entry with user ID
        public async Task SaveEntry(JournalEntry entry, int? userId = null)
        {
            try
            {
                if (userId.HasValue)
                    entry.UserId = userId.Value;

                Console.WriteLine($"Saving entry: Id={entry.Id}, Title='{entry.Title}', Content length={entry.Content?.Length}");

                // Ensure EntryDate is set
                if (entry.EntryDate == default)
                {
                    entry.EntryDate = DateTime.Today;
                    Console.WriteLine($"Set default EntryDate: {entry.EntryDate}");
                }

                if (entry.Id == 0)
                {
                    // Insert - NEW entry
                    Console.WriteLine("Inserting new entry");
                    
                    using var command = _connection.CreateCommand();
                    command.CommandText = @"
                        INSERT INTO JournalEntries 
                        (UserId, Title, Content, EntryDate, CreatedAt, UpdatedAt, 
                         PrimaryMood, SecondaryMood1, SecondaryMood2, MoodCategory, Category, Tags) 
                        VALUES (@userId, @title, @content, @entryDate, @createdAt, @updatedAt, 
                                @primaryMood, @secondaryMood1, @secondaryMood2, @moodCategory, @category, @tags)";

                    command.Parameters.AddWithValue("@userId", entry.UserId);
                    command.Parameters.AddWithValue("@title", string.IsNullOrEmpty(entry.Title) ? DBNull.Value : entry.Title);
                    command.Parameters.AddWithValue("@content", string.IsNullOrEmpty(entry.Content) ? DBNull.Value : entry.Content);
                    command.Parameters.AddWithValue("@entryDate", entry.EntryDate.ToString("yyyy-MM-dd"));
                    command.Parameters.AddWithValue("@createdAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    command.Parameters.AddWithValue("@updatedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    command.Parameters.AddWithValue("@primaryMood", string.IsNullOrEmpty(entry.PrimaryMood) ? DBNull.Value : entry.PrimaryMood);
                    command.Parameters.AddWithValue("@secondaryMood1", string.IsNullOrEmpty(entry.SecondaryMood1) ? DBNull.Value : entry.SecondaryMood1);
                    command.Parameters.AddWithValue("@secondaryMood2", string.IsNullOrEmpty(entry.SecondaryMood2) ? DBNull.Value : entry.SecondaryMood2);
                    command.Parameters.AddWithValue("@moodCategory", string.IsNullOrEmpty(entry.MoodCategory) ? DBNull.Value : entry.MoodCategory);
                    command.Parameters.AddWithValue("@category", string.IsNullOrEmpty(entry.Category) ? DBNull.Value : entry.Category);
                    command.Parameters.AddWithValue("@tags", string.IsNullOrEmpty(entry.Tags) ? DBNull.Value : entry.Tags);

                    await command.ExecuteNonQueryAsync();

                    // Get the new ID
                    using var getIdCommand = _connection.CreateCommand();
                    getIdCommand.CommandText = "SELECT last_insert_rowid()";
                    entry.Id = Convert.ToInt32(await getIdCommand.ExecuteScalarAsync());
                    
                    Console.WriteLine($"Inserted new entry with ID: {entry.Id}");
                }
                else
                {
                    // Update - EXISTING entry
                    Console.WriteLine($"Updating existing entry ID: {entry.Id}");
                    
                    using var command = _connection.CreateCommand();
                    command.CommandText = @"
                        UPDATE JournalEntries SET
                            Title = @title,
                            Content = @content,
                            EntryDate = @entryDate,
                            UpdatedAt = @updatedAt,
                            PrimaryMood = @primaryMood,
                            SecondaryMood1 = @secondaryMood1,
                            SecondaryMood2 = @secondaryMood2,
                            MoodCategory = @moodCategory,
                            Category = @category,
                            Tags = @tags
                        WHERE Id = @id";

                    command.Parameters.AddWithValue("@id", entry.Id);
                    command.Parameters.AddWithValue("@title", string.IsNullOrEmpty(entry.Title) ? DBNull.Value : entry.Title);
                    command.Parameters.AddWithValue("@content", string.IsNullOrEmpty(entry.Content) ? DBNull.Value : entry.Content);
                    command.Parameters.AddWithValue("@entryDate", entry.EntryDate.ToString("yyyy-MM-dd"));
                    command.Parameters.AddWithValue("@updatedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    command.Parameters.AddWithValue("@primaryMood", string.IsNullOrEmpty(entry.PrimaryMood) ? DBNull.Value : entry.PrimaryMood);
                    command.Parameters.AddWithValue("@secondaryMood1", string.IsNullOrEmpty(entry.SecondaryMood1) ? DBNull.Value : entry.SecondaryMood1);
                    command.Parameters.AddWithValue("@secondaryMood2", string.IsNullOrEmpty(entry.SecondaryMood2) ? DBNull.Value : entry.SecondaryMood2);
                    command.Parameters.AddWithValue("@moodCategory", string.IsNullOrEmpty(entry.MoodCategory) ? DBNull.Value : entry.MoodCategory);
                    command.Parameters.AddWithValue("@category", string.IsNullOrEmpty(entry.Category) ? DBNull.Value : entry.Category);
                    command.Parameters.AddWithValue("@tags", string.IsNullOrEmpty(entry.Tags) ? DBNull.Value : entry.Tags);

                    await command.ExecuteNonQueryAsync();
                    Console.WriteLine($"Updated entry ID: {entry.Id}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in SaveEntry: {ex.Message}");
                throw; // Re-throw to handle in UI
            }
        }

        // Debug method to check database schema
        public async Task DebugDatabaseSchema()
        {
            try
            {
                Console.WriteLine("=== DATABASE SCHEMA ===");
                
                // Check table structure
                using var command = _connection.CreateCommand();
                command.CommandText = "PRAGMA table_info(JournalEntries)";
                
                using var reader = await command.ExecuteReaderAsync();
                Console.WriteLine("Table columns:");
                while (await reader.ReadAsync())
                {
                    Console.WriteLine($"  {reader.GetInt32(0)}: {reader.GetString(1)} ({reader.GetString(2)})");
                }
                
                // Check row count
                command.CommandText = "SELECT COUNT(*) FROM JournalEntries";
                var count = await command.ExecuteScalarAsync();
                Console.WriteLine($"Total rows: {count}");
                
                // Check first few rows
                command.CommandText = "SELECT * FROM JournalEntries LIMIT 3";
                using var dataReader = await command.ExecuteReaderAsync();
                int rowNum = 0;
                while (await dataReader.ReadAsync())
                {
                    rowNum++;
                    Console.WriteLine($"\nRow {rowNum}:");
                    for (int i = 0; i < dataReader.FieldCount; i++)
                    {
                        try
                        {
                            var columnName = dataReader.GetName(i);
                            var value = dataReader.IsDBNull(i) ? "NULL" : dataReader.GetValue(i).ToString();
                            Console.WriteLine($"  {columnName}: {value}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"  Column {i}: ERROR - {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in DebugDatabaseSchema: {ex.Message}");
            }
        }

        // Get entry count by user
        public async Task<int> GetEntryCountByUserId(int userId)
        {
            try
            {
                using var command = _connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM JournalEntries WHERE UserId = @userId";
                command.Parameters.AddWithValue("@userId", userId);
                
                var result = await command.ExecuteScalarAsync();
                return Convert.ToInt32(result);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetEntryCountByUserId: {ex.Message}");
                return 0;
            }
        }

        // Get streak count by user
        public async Task<int> GetStreakCountByUserId(int userId)
        {
            try
            {
                var entries = await GetEntriesByUserId(userId);
                var orderedEntries = entries.OrderByDescending(e => e.EntryDate).ToList();
                
                int streak = 0;
                var currentDate = DateTime.Today;

                foreach (var entry in orderedEntries)
                {
                    if (entry.EntryDate.Date == currentDate)
                    {
                        streak++;
                        currentDate = currentDate.AddDays(-1);
                    }
                    else
                        break;
                }
                return streak;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetStreakCountByUserId: {ex.Message}");
                return 0;
            }
        }

        // Delete entry with user verification
        public async Task<bool> DeleteEntry(int id, int? userId = null)
        {
            try
            {
                using var command = _connection.CreateCommand();
                
                if (userId.HasValue)
                {
                    command.CommandText = "DELETE FROM JournalEntries WHERE Id = @id AND UserId = @userId";
                    command.Parameters.AddWithValue("@userId", userId.Value);
                }
                else
                {
                    command.CommandText = "DELETE FROM JournalEntries WHERE Id = @id";
                }
                
                command.Parameters.AddWithValue("@id", id);
                
                var rowsAffected = await command.ExecuteNonQueryAsync();
                Console.WriteLine($"Deleted {rowsAffected} rows for entry ID {id}");
                return rowsAffected > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in DeleteEntry: {ex.Message}");
                return false;
            }
        }

        // Get user categories
        public async Task<List<string>> GetUserCategories(int userId)
        {
            try
            {
                var categories = new List<string>();
                
                using var command = _connection.CreateCommand();
                command.CommandText = @"
                    SELECT DISTINCT Category 
                    FROM JournalEntries 
                    WHERE UserId = @userId 
                    AND Category IS NOT NULL 
                    AND Category != ''
                    ORDER BY Category";

                command.Parameters.AddWithValue("@userId", userId);

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    if (!reader.IsDBNull(0))
                    {
                        categories.Add(reader.GetString(0));
                    }
                }

                return categories;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting user categories: {ex.Message}");
                return new List<string>();
            }
        }

        // Get all moods used by user
        public async Task<List<string>> GetUserMoods(int userId)
        {
            try
            {
                var moods = new List<string>();
                
                using var command = _connection.CreateCommand();
                command.CommandText = @"
                    SELECT DISTINCT PrimaryMood 
                    FROM JournalEntries 
                    WHERE UserId = @userId 
                    AND PrimaryMood IS NOT NULL 
                    AND PrimaryMood != ''
                    UNION
                    SELECT DISTINCT SecondaryMood1 
                    FROM JournalEntries 
                    WHERE UserId = @userId 
                    AND SecondaryMood1 IS NOT NULL 
                    AND SecondaryMood1 != ''
                    UNION
                    SELECT DISTINCT SecondaryMood2 
                    FROM JournalEntries 
                    WHERE UserId = @userId 
                    AND SecondaryMood2 IS NOT NULL 
                    AND SecondaryMood2 != ''
                    ORDER BY PrimaryMood";

                command.Parameters.AddWithValue("@userId", userId);

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    if (!reader.IsDBNull(0))
                    {
                        moods.Add(reader.GetString(0));
                    }
                }

                return moods;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting user moods: {ex.Message}");
                return new List<string>();
            }
        }

        // Get most common mood by user
        public async Task<string?> GetMostCommonMoodByUserId(int userId)
        {
            try
            {
                using var command = _connection.CreateCommand();
                command.CommandText = @"
                    SELECT PrimaryMood, COUNT(*) as Count
                    FROM JournalEntries 
                    WHERE UserId = @userId 
                    AND PrimaryMood IS NOT NULL 
                    AND PrimaryMood != ''
                    GROUP BY PrimaryMood
                    ORDER BY Count DESC
                    LIMIT 1";

                command.Parameters.AddWithValue("@userId", userId);

                using var reader = await command.ExecuteReaderAsync();
                if (await reader.ReadAsync() && !reader.IsDBNull(0))
                {
                    return reader.GetString(0);
                }
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting most common mood: {ex.Message}");
                return null;
            }
        }

        // Get last entry date by user
        public async Task<DateTime?> GetLastEntryDateByUserId(int userId)
        {
            try
            {
                using var command = _connection.CreateCommand();
                command.CommandText = @"
                    SELECT MAX(EntryDate)
                    FROM JournalEntries 
                    WHERE UserId = @userId";

                command.Parameters.AddWithValue("@userId", userId);

                var result = await command.ExecuteScalarAsync();
                if (result != null && result != DBNull.Value)
                {
                    return SafeParseDateTime(result.ToString());
                }
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting last entry date: {ex.Message}");
                return null;
            }
        }

        // Clear all entries for a user (for debugging/cleanup)
        public async Task<bool> ClearUserEntries(int userId)
        {
            try
            {
                using var command = _connection.CreateCommand();
                command.CommandText = "DELETE FROM JournalEntries WHERE UserId = @userId";
                command.Parameters.AddWithValue("@userId", userId);
                
                var rowsAffected = await command.ExecuteNonQueryAsync();
                Console.WriteLine($"Cleared {rowsAffected} entries for user {userId}");
                return rowsAffected >= 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error clearing user entries: {ex.Message}");
                return false;
            }
        }

        // Backup database (optional) - FIXED THE ERROR HERE
        public async Task<bool> BackupDatabase(string backupPath)
        {
            try
            {
                // Ensure backup directory exists
                var backupDir = Path.GetDirectoryName(backupPath);
                if (!Directory.Exists(backupDir))
                {
                    Directory.CreateDirectory(backupDir);
                }
                
                // Close connection before copying
                _connection?.Close();
                
                // Copy the database file
                File.Copy(_dbPath, backupPath, true);
                
                // Reopen connection
                if (_connection != null && _connection.State != System.Data.ConnectionState.Open)
                {
                    _connection.Open();
                }
                
                Console.WriteLine($"Database backed up to: {backupPath}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error backing up database: {ex.Message}");
                return false;
            }
        }

        // ========== NEW METHOD: Get Statistics ==========
        public async Task<Dictionary<string, object>> GetUserStatistics(int userId)
        {
            var stats = new Dictionary<string, object>();
            
            try
            {
                // Total entries
                var totalEntries = await GetEntryCountByUserId(userId);
                stats["TotalEntries"] = totalEntries;
                
                // Average words per entry
                var entries = await GetEntriesByUserId(userId);
                if (entries.Count > 0)
                {
                    var totalWords = entries.Sum(e => e.WordCount);
                    stats["AverageWords"] = totalWords / entries.Count;
                    stats["TotalWords"] = totalWords;
                }
                else
                {
                    stats["AverageWords"] = 0;
                    stats["TotalWords"] = 0;
                }
                
                // Current streak
                var streak = await GetStreakCountByUserId(userId);
                stats["CurrentStreak"] = streak;
                
                // Most common mood
                var commonMood = await GetMostCommonMoodByUserId(userId);
                stats["MostCommonMood"] = commonMood ?? "No data";
                
                // Last entry date
                var lastEntry = await GetLastEntryDateByUserId(userId);
                stats["LastEntryDate"] = lastEntry?.ToString("yyyy-MM-dd") ?? "No entries";
                
                Console.WriteLine($"Statistics for user {userId}: Total={totalEntries}, Streak={streak}, AvgWords={stats["AverageWords"]}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetUserStatistics: {ex.Message}");
            }
            
            return stats;
        }

        public void Dispose()
        {
            _connection?.Close();
            _connection?.Dispose();
        }
    }
}