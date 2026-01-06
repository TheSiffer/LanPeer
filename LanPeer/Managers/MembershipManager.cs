using FubarDev.FtpServer.AccountManagement;
using System.Collections.Concurrent;
using System.Security.Claims;

namespace LanPeer.Managers
{
    public sealed class MembershipManager : IMembershipProvider
    {
        private readonly ConcurrentDictionary<string, string> _users = new();
        public void AddUser(string username, string password)
        {
            _users[username] = password;
        }
        public void RemoveUser(string username) 
        {
            _users.TryRemove(username, out _);
        }
        public Task<MemberValidationResult> ValidateUserAsync(string username, string password)
        {
            if(_users.TryGetValue(username, out var stored) && stored == password)
            {
                return Task.FromResult(
                    new MemberValidationResult(
                        MemberValidationStatus.AuthenticatedUser, 
                        new ClaimsPrincipal(
                            new ClaimsIdentity(
                                new[] { 
                                    new Claim(ClaimTypes.Name, username) }, 
                                "FtpAuth"))));
            }
            return Task.FromResult(new MemberValidationResult(MemberValidationStatus.InvalidLogin));
        }
    }
}
