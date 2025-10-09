using System;
using System.Timers;
namespace LanPeer
{
    public sealed class CodeManager
    {
        private static readonly Lazy<CodeManager> _instance = 
            new Lazy<CodeManager>(() => new CodeManager());

        public static CodeManager instance => _instance.Value;

        private string currentCode;
        private readonly System.Timers.Timer timer;
        private readonly Random random = new Random();

        public DateTime LastGeneratedAt { get; private set; }

        public CodeManager()
        {
            timer = new System.Timers.Timer(15000);
            timer.Elapsed += (s, e) => GenerateNewCode();
            timer.AutoReset = true;
            timer.Start();
        }

        private void GenerateNewCode()
        {
            lock (this)
            {
                currentCode = random.Next(1000, 9999).ToString();
                LastGeneratedAt = DateTime.UtcNow;
            }
        }

        public string GetCode()
        {
            lock (this)
            {
                return currentCode;
            }
        }

        public bool Validate(string code)
        {
            lock (this)
            {
                return currentCode == code;
            }
        }

        public void ForceRegenerate()
        {
            GenerateNewCode();
        }
    }
}
