using LanPeer.Interfaces;
using System;
using System.Timers;
namespace LanPeer.Managers
{
    public sealed class CodeManager : ICodeManager
    {
        //private static readonly Lazy<CodeManager> _instance =
        //    new(() => new CodeManager());

        //public static CodeManager? instance; //=> _instance.Value;

        private string currentCode = string.Empty;
        private readonly System.Timers.Timer timer;
        private readonly Random random = new Random();
        private readonly object _lock = new();
        public event Action<string>? OnCodeExpired;


        public DateTime LastGeneratedAt { get; private set; }

        public CodeManager()
        {

            timer = new System.Timers.Timer(30000); //30 secs
            timer.Elapsed += (s, e) => GenerateNewCode();
            timer.AutoReset = true;
            timer.Start();
            GenerateNewCode();
        }

        private async Task GenerateNewCode()
        {
            await Task.Run(() =>
            {
                lock (_lock)
                {
                    currentCode = random.Next(1000, 9999).ToString();
                    LastGeneratedAt = DateTime.UtcNow;
                    OnCodeExpired?.Invoke(currentCode); //fire event to alert subscribers
                    Console.WriteLine($"[CodeManager] New code generated: {currentCode}");
                    timer.Stop();
                    timer.Start();
                }
            });
        }

        public string GetCode()
        {
            lock (_lock)
            {
                return currentCode;
            }
        }

        public bool Validate(string code)
        {
            lock (_lock)
            {
                return currentCode == code;
            }
        }

        public async Task<string> ForceRegenerate()
        {
            await GenerateNewCode();
            return currentCode;
        }
    }
}
