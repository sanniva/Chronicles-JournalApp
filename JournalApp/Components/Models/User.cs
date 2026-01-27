using SQLite;

namespace JournalApp.Components.Models
{
    public class User
    {
        [PrimaryKey, AutoIncrement]
        public int UserId { get; set; }

        [Unique]
        public string Username { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        //public string Theme { get; set; } = "light";
        public DateTime CreatedAt { get; set; }
        public DateTime? LastLogin { get; set; }
    }
}