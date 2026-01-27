using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JournalApp.Components.Models;
using Microsoft.Extensions.Logging;

namespace JournalApp.Components.Services
{
    public class UserStateService
    {
        private readonly AuthDatabase _authDb;
        private readonly ILogger<UserStateService>? _logger;

        // Store tokens for "Remember Me" auto-login
        private readonly Dictionary<string, string> _tokenStore = new();

        public User? CurrentUser { get; private set; }
        public bool IsAuthenticated => CurrentUser != null;

        public event Action? OnUserStateChanged;

        public UserStateService(AuthDatabase authDb, ILogger<UserStateService>? logger = null)
        {
            _authDb = authDb;
            _logger = logger;

            _logger?.LogInformation("UserStateService initialized");
            Console.WriteLine("=== UserStateService Constructor Called ===");
        }

        // Login / Register

        public async Task<bool> LoginAsync(string username, string password)
        {
            try
            {
                if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                    return false;

                var userExists = await _authDb.UserExists(username);
                if (!userExists)
                    return false;

                var user = await _authDb.Login(username, password);
                if (user != null)
                {
                    CurrentUser = user;
                    NotifyStateChanged();
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"Login error for user: {username}");
                return false;
            }
        }

        public async Task<bool> RegisterAsync(string username, string password)
        {
            if (string.IsNullOrEmpty(username) || username.Length < 3)
                return false;
            if (string.IsNullOrEmpty(password) || password.Length < 4)
                return false;

            var exists = await _authDb.UserExists(username);
            if (exists) return false;

            var success = await _authDb.Register(username, password);
            if (success)
            {
                // Auto-login after registration
                return await LoginAsync(username, password);
            }

            return false;
        }

        public void Logout()
        {
            CurrentUser = null;
            NotifyStateChanged();
        }

        //  Remember Me / Token 
        // Generates a token for Remember Me auto-login
       
        public async Task<string> GenerateTokenForUser(string username)
        {
            if (string.IsNullOrEmpty(username)) throw new ArgumentNullException(nameof(username));

            var token = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
            _tokenStore[username] = token;
            return token;
        }
        
        // Login using token from localStorage
    
        public async Task<bool> LoginWithToken(string token)
        {
            if (string.IsNullOrEmpty(token)) return false;

            var username = _tokenStore.FirstOrDefault(x => x.Value == token).Key;
            if (string.IsNullOrEmpty(username)) return false;

            var user = await _authDb.GetUserByUsername(username);
            if (user != null)
            {
                CurrentUser = user;
                NotifyStateChanged();
                return true;
            }

            return false;
        }

        // Password / Account 

        public async Task<bool> ChangePassword(string currentPassword, string newPassword)
        {
            if (!IsAuthenticated) return false;
            return await _authDb.ChangePassword(CurrentUser!.UserId, currentPassword, newPassword);
        }

        public async Task<bool> DeleteAccount(string password)
        {
            if (!IsAuthenticated) return false;

            var success = await _authDb.DeleteAccount(CurrentUser!.UserId, password);
            if (success) Logout();
            return success;
        }

        public async Task<bool> VerifyPassword(string password)
        {
            if (!IsAuthenticated) return false;

            var user = await _authDb.Login(CurrentUser!.Username, password);
            return user != null;
        }

        // Debug 
        public void DebugPrintStatus()
        {
            Console.WriteLine($"=== USER STATE DEBUG ===");
            Console.WriteLine($"IsAuthenticated: {IsAuthenticated}");
            Console.WriteLine($"CurrentUser: {CurrentUser?.Username ?? "null"}");
            Console.WriteLine($"User ID: {CurrentUser?.UserId.ToString() ?? "null"}");
            Console.WriteLine($"=========================");
        }

        private void NotifyStateChanged()
        {
            try
            {
                OnUserStateChanged?.Invoke();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"=== State change notification error: {ex.Message} ===");
            }
        }
    }
}
