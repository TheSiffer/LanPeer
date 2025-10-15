using LanPeer.Interfaces;
using System;
using System.Timers;
namespace LanPeer
{
    public sealed class CodeManager : ICodeManager
    {
        //private static readonly Lazy<CodeManager> _instance =
        //    new(() => new CodeManager());

        //public static CodeManager? instance; //=> _instance.Value;

        private string currentCode = "";
        private readonly System.Timers.Timer timer;
        private readonly Random random = new Random();
        private readonly object _lock = new();
        public event Action<string>? OnCodeExpired;


        public DateTime LastGeneratedAt { get; private set; }

        public CodeManager()
        {
            GenerateNewCode();
            timer = new System.Timers.Timer(200000); //20 secs
            timer.Elapsed += (s, e) => GenerateNewCode();
            timer.AutoReset = true;
            timer.Start();
        }

        private void GenerateNewCode()
        {
            lock (_lock)
            {
                currentCode = random.Next(1000, 9999).ToString();
                LastGeneratedAt = DateTime.UtcNow;
                OnCodeExpired?.Invoke(currentCode); //fire event to alert subscribers
                Console.WriteLine($"[CodeManager] New code generated: {currentCode}");
            }
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

        public string ForceRegenerate()
        {
            GenerateNewCode();
            return currentCode;
        }
    }
}
