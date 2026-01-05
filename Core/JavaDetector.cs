// Jarfix/Core/JavaDetector.cs
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Jarfix.Core
{
    public static class JavaDetector
    {
        static readonly string[] VendorHints = new[]
        {
            "azul", "zulu", "adoptium", "temurin", "microsoft", "oracle", "corretto", "bellsoft", "amazon", "jdk", "java"
        };

        static readonly string[] ProgramFilesRoots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs")
        };
        public static async Task<List<JavaRuntime>> DetectInstalledJavaAsync()
        {
            var results = new List<JavaRuntime>();

            foreach (var keyPath in GetJavaSoftRegistryKeys())
            {
                foreach (var candidate in GetJavaPathsFromJavaSoftKey(keyPath))
                {
                    var rt = await ProbeJavaAsync(candidate);
                    if (rt != null) results.Add(rt);
                }
            }

            foreach (var candidate in GetCandidatesFromUninstallRegistry())
            {
                var rt = await ProbeJavaAsync(candidate);
                if (rt != null) results.Add(rt);
            }

            foreach (var candidate in GetCandidatesFromProgramFiles())
            {
                var rt = await ProbeJavaAsync(candidate);
                if (rt != null) results.Add(rt);
            }

            foreach (var candidate in GetJavaFromPath())
            {
                var rt = await ProbeJavaAsync(candidate);
                if (rt != null) results.Add(rt);
            }

            var distinct = results
                .GroupBy(r => Path.GetFullPath(r.JavawPath), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            foreach (var r in distinct)
            {
                if (IsOnPath(r.JavawPath)) r.Confidence = "high";
                else if (IsUnderProgramFiles(r.JavawPath)) r.Confidence = "medium";
                else r.Confidence = "low";
            }

            var vendorPreference = new[] { "Adoptium", "Azul", "Microsoft", "Oracle", "Corretto", "BellSoft", "Unknown" };

            distinct.Sort((a, b) =>
            {
                var cmp = b.MajorVersion.CompareTo(a.MajorVersion);
                if (cmp != 0) return cmp;
                cmp = (b.Is64Bit ? 1 : 0).CompareTo(a.Is64Bit ? 1 : 0);
                if (cmp != 0) return cmp;
                cmp = CompareConfidence(b.Confidence, a.Confidence);
                if (cmp != 0) return cmp;
                var ai = Array.IndexOf(vendorPreference, a.Vendor ?? "Unknown");
                var bi = Array.IndexOf(vendorPreference, b.Vendor ?? "Unknown");
                return ai.CompareTo(bi);
            });

            return distinct;
        }

        static int CompareConfidence(string a, string b)
        {
            int score(string s)
            {
                return s == "high" ? 3 : s == "medium" ? 2 : 1;
            }
            return score(a).CompareTo(score(b));
        }

        static IEnumerable<(RegistryHive hive, RegistryView view, string subKey)> GetJavaSoftRegistryKeys()
        {
            var keys = new List<(RegistryHive, RegistryView, string)>();

            var hives = new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser };
            var views = new[] { RegistryView.Registry64, RegistryView.Registry32 };

            foreach (var hive in hives)
            {
                foreach (var view in views)
                {
                    keys.Add((hive, view, @"SOFTWARE\JavaSoft\Java Runtime Environment"));
                    keys.Add((hive, view, @"SOFTWARE\JavaSoft\Java Development Kit"));
                }
            }

            return keys;
        }

        static IEnumerable<string> GetJavaPathsFromJavaSoftKey((RegistryHive hive, RegistryView view, string subKey) keyInfo)
        {
            var results = new List<string>();
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(keyInfo.hive, keyInfo.view);
                using var jrek = baseKey.OpenSubKey(keyInfo.subKey);
                if (jrek == null) return results;
                var current = jrek.GetValue("CurrentVersion") as string;
                if (!string.IsNullOrWhiteSpace(current))
                {
                    using var verKey = jrek.OpenSubKey(current);
                    if (verKey != null)
                    {
                        var javaHome = verKey.GetValue("JavaHome") as string;
                        if (!string.IsNullOrWhiteSpace(javaHome))
                        {
                            var javaw = Path.Combine(javaHome, "bin", "javaw.exe");
                            if (File.Exists(javaw)) results.Add(javaw);
                        }
                        var runtimeLib = verKey.GetValue("RuntimeLib") as string;
                        if (!string.IsNullOrWhiteSpace(runtimeLib) && File.Exists(runtimeLib)) results.Add(runtimeLib);
                    }
                }

                foreach (var sub in jrek.GetSubKeyNames())
                {
                    using var v = jrek.OpenSubKey(sub);
                    if (v == null) continue;
                    var javaHome = v.GetValue("JavaHome") as string;
                    if (!string.IsNullOrWhiteSpace(javaHome))
                    {
                        var javaw = Path.Combine(javaHome, "bin", "javaw.exe");
                        if (File.Exists(javaw)) results.Add(javaw);
                    }
                }
            }
            catch
            {
            }
            return results.Distinct(StringComparer.OrdinalIgnoreCase);
        }
        static IEnumerable<string> GetCandidatesFromUninstallRegistry()
        {
            var results = new List<string>();
            var hivesViews = new (RegistryHive hive, RegistryView view)[]
            {
                (RegistryHive.LocalMachine, RegistryView.Registry64),
                (RegistryHive.LocalMachine, RegistryView.Registry32),
                (RegistryHive.CurrentUser, RegistryView.Registry64),
                (RegistryHive.CurrentUser, RegistryView.Registry32)
            };

            foreach (var hv in hivesViews)
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hv.hive, hv.view);
                    using var uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                    if (uninstall == null) continue;
                    foreach (var sub in uninstall.GetSubKeyNames())
                    {
                        try
                        {
                            using var sk = uninstall.OpenSubKey(sub);
                            if (sk == null) continue;
                            var displayName = (sk.GetValue("DisplayName") as string) ?? "";
                            if (string.IsNullOrWhiteSpace(displayName)) continue;
                            var lname = displayName.ToLowerInvariant();

                            if (!VendorHints.Any(h => lname.Contains(h))) continue;

                            var installLocation = sk.GetValue("InstallLocation") as string;
                            if (!string.IsNullOrWhiteSpace(installLocation))
                            {
                                var p = Path.Combine(installLocation, "bin", "javaw.exe");
                                if (File.Exists(p)) results.Add(p);
                            }

                            var displayIcon = sk.GetValue("DisplayIcon") as string;
                            if (!string.IsNullOrWhiteSpace(displayIcon))
                            {
                                var m = Regex.Match(displayIcon, "^(\"?)([^\",]+)"); 
                                if (m.Success)
                                {
                                    var p = m.Groups[2].Value.Trim('"');
                                    if (File.Exists(p) && p.EndsWith("javaw.exe", StringComparison.OrdinalIgnoreCase))
                                        results.Add(p);
                                    else
                                    {
                                        var tryBin = Path.Combine(Path.GetDirectoryName(p) ?? p, "bin", "javaw.exe");
                                        if (File.Exists(tryBin)) results.Add(tryBin);
                                    }
                                }
                            }

                            var uninstallString = sk.GetValue("UninstallString") as string;
                            if (!string.IsNullOrWhiteSpace(uninstallString))
                            {
                                var m2 = Regex.Match(uninstallString, "\"(?<p>[^\"]+\\.exe)\"");
                                if (!m2.Success)
                                    m2 = Regex.Match(uninstallString, "^(?<p>[^ ]+\\.exe)");
                                if (m2.Success)
                                {
                                    var p = m2.Groups["p"].Value;
                                    if (File.Exists(p) && p.EndsWith("javaw.exe", StringComparison.OrdinalIgnoreCase))
                                        results.Add(p);
                                }
                            }
                        }
                        catch { continue; }
                    }
                }
                catch { continue; }
            }

            return results.Distinct(StringComparer.OrdinalIgnoreCase);
        }
        static IEnumerable<string> GetCandidatesFromProgramFiles()
        {
            var results = new List<string>();
            foreach (var root in ProgramFilesRoots)
            {
                if (string.IsNullOrWhiteSpace(root)) continue;
                try
                {
                    if (!Directory.Exists(root)) continue;
                    foreach (var dir in Directory.EnumerateDirectories(root))
                    {
                        var name = Path.GetFileName(dir) ?? "";
                        var lname = name.ToLowerInvariant();
                        if (VendorHints.Any(h => lname.Contains(h)))
                        {
                            foreach (var sub in Directory.EnumerateDirectories(dir))
                            {
                                var javaw = Path.Combine(sub, "bin", "javaw.exe");
                                if (File.Exists(javaw)) results.Add(javaw);
                            }

                            var tryTop = Path.Combine(dir, "bin", "javaw.exe");
                            if (File.Exists(tryTop)) results.Add(tryTop);
                        }

                        if (Regex.IsMatch(name, @"jdk-?\d+") || Regex.IsMatch(name, @"jre1?\.?\d+"))
                        {
                            var javaw = Path.Combine(dir, "bin", "javaw.exe");
                            if (File.Exists(javaw)) results.Add(javaw);
                        }
                    }

                    var pfJava = Path.Combine(root, "Java");
                    if (Directory.Exists(pfJava))
                    {
                        foreach (var sub in Directory.EnumerateDirectories(pfJava))
                        {
                            var javaw = Path.Combine(sub, "bin", "javaw.exe");
                            if (File.Exists(javaw)) results.Add(javaw);
                        }
                    }
                }
                catch { }
            }

            return results.Distinct(StringComparer.OrdinalIgnoreCase);
        }
        static IEnumerable<string> GetJavaFromPath()
        {
            var list = new List<string>();
            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            var parts = pathEnv.Split(Path.PathSeparator);
            foreach (var p in parts)
            {
                try
                {
                    var candidate = Path.Combine(p, "javaw.exe");
                    if (File.Exists(candidate)) list.Add(candidate);
                }
                catch { }
            }

            return list.Distinct(StringComparer.OrdinalIgnoreCase);
        }

        static bool IsOnPath(string fullPath)
        {
            try
            {
                var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
                var parts = pathEnv.Split(Path.PathSeparator).Select(x => Path.GetFullPath(x.Trim())).ToList();
                var dir = Path.GetDirectoryName(fullPath) ?? "";
                return parts.Any(p => string.Equals(p, dir, StringComparison.OrdinalIgnoreCase));
            }
            catch { return false; }
        }

        static bool IsUnderProgramFiles(string fullPath)
        {
            try
            {
                fullPath = Path.GetFullPath(fullPath);
                foreach (var root in ProgramFilesRoots)
                {
                    if (string.IsNullOrEmpty(root)) continue;
                    var rf = Path.GetFullPath(root);
                    if (fullPath.StartsWith(rf, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            catch { }
            return false;
        }

        public static async Task<JavaRuntime?> ProbeJavaAsync(string javaExePath)
        {
            if (string.IsNullOrWhiteSpace(javaExePath)) return null;
            try
            {
                javaExePath = Path.GetFullPath(javaExePath);
                if (!File.Exists(javaExePath)) return null;

                var psi = new ProcessStartInfo
                {
                    FileName = javaExePath,
                    Arguments = "-XshowSettings:properties -version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                using var proc = Process.Start(psi);
                if (proc == null) return null;

                var stdOutTask = proc.StandardOutput.ReadToEndAsync();
                var stdErrTask = proc.StandardError.ReadToEndAsync();

                var timeout = Task.Delay(4000);
                var finished = await Task.WhenAny(Task.WhenAll(stdOutTask, stdErrTask), timeout);
                if (finished == timeout)
                {
                    try { proc.Kill(); } catch { }
                    return null;
                }

                var output = (await stdOutTask) + "\n" + (await stdErrTask);
                var text = output.Trim();

                var major = ParseMajorVersion(text);
                if (major == 0) return null;

                var vendor = ParseVendor(text, javaExePath);
                var is64 = IsBinary64(javaExePath);
                return new JavaRuntime
                {
                    JavawPath = javaExePath,
                    Vendor = vendor,
                    MajorVersion = major,
                    Is64Bit = is64,
                    InstallPath = Path.GetDirectoryName(Path.GetDirectoryName(javaExePath)) ?? "",
                    Confidence = "low"
                };
            }
            catch
            {
                return null;
            }
        }
        static string ParseVendor(string text, string path)
        {
            var lower = (text + " " + path).ToLowerInvariant();
            if (lower.Contains("azul") || lower.Contains("zulu")) return "Azul";
            if (lower.Contains("temurin") || lower.Contains("adoptium") || lower.Contains("adoptopenjdk")) return "Adoptium";
            if (lower.Contains("microsoft")) return "Microsoft";
            if (lower.Contains("oracle")) return "Oracle";
            if (lower.Contains("corretto")) return "Corretto";
            if (lower.Contains("bellsoft") || lower.Contains("liberica")) return "BellSoft";
            if (lower.Contains("amazon")) return "Amazon";
            return "Unknown";
        }

        static int ParseMajorVersion(string text)
        {
            var m = Regex.Match(text, "version\\s+\"(?<v>[0-9]+(\\.[0-9_]+)*)\"", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var v = m.Groups["v"].Value;
                if (v.StartsWith("1."))
                {
                    var parts = v.Split('.');
                    if (parts.Length >= 2 && int.TryParse(parts[1].Split('_')[0], out var maj))
                        return maj;
                }
                else
                {
                    var parts = v.Split('.');
                    if (parts.Length >= 1 && int.TryParse(parts[0], out var maj))
                        return maj;
                }
            }

            m = Regex.Match(text, "openjdk\\s+(?<v>[0-9]+(\\.[0-9]+)*)", RegexOptions.IgnoreCase);
            if (m.Success && int.TryParse(m.Groups["v"].Value.Split('.')[0], out var v2))
                return v2;

            m = Regex.Match(text, "([0-9]{2})\\.[0-9]+\\.[0-9]+");
            if (m.Success && int.TryParse(m.Groups[1].Value, out var v3))
                return v3;

            return 0;
        }

        static bool IsBinary64(string exePath)
        {
            var p = exePath.ToLowerInvariant();
            if (p.Contains("program files (x86)") || p.Contains("\\x86\\") || p.Contains("i386") || p.Contains("wow6432")) return false;
            return true;
        }
    }
}
