using System;
using System.Collections.Generic;

namespace JournalApp.Components.Models
{
    public class JournalEntry
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public DateTime EntryDate { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string PrimaryMood { get; set; } = string.Empty;
        public string SecondaryMood1 { get; set; } = string.Empty;
        public string SecondaryMood2 { get; set; } = string.Empty;
        public string MoodCategory { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Tags { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
       

        public List<string> TagList { get; set; } = new();
        
        public int WordCount => string.IsNullOrWhiteSpace(Content) ? 0 : 
            Content.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
        
        public int ReadingTime => (int)Math.Ceiling(WordCount / 200.0); // 200 words per minute
    }
}