// Jarfix/Core/JavaRuntime.cs
using System;

namespace Jarfix.Core
{
    public class JavaRuntime
    {
        public string JavawPath { get; set; } = "";
        public string Vendor { get; set; } = "";
        public int MajorVersion { get; set; } = 0;
        public bool Is64Bit { get; set; } = true;
        public string InstallPath { get; set; } = "";
        public string Confidence { get; set; } = "low";

        public string DisplayName()
        {
            return $"{Vendor} {MajorVersion} {(Is64Bit ? "x64" : "x86")} — {JavawPath}";
        }
    }
}
