// Jarfix/Core/JarAssociationFixer.cs
using Microsoft.Win32;
using System;
using System.Runtime.InteropServices;

namespace Jarfix.Core
{
    public static class JarAssociationFixer
    {
        [DllImport("shell32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

        private const int SHCNE_ASSOCCHANGED = 0x08000000;
        private const uint SHCNF_IDLIST = 0x0000;

        public static bool SetJarAssociationUserScope(JavaRuntime runtime)
        {
            try
            {
                CleanupFileExtsOverrides();
                
                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\.jar"))
                {
                    key.SetValue("", "jarfile", RegistryValueKind.String);
                }
                
                var cmd = $"\"{runtime.JavawPath}\" -jar \"%1\" %*";
                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\jarfile\shell\open\command"))
                {
                    key.SetValue("", cmd, RegistryValueKind.String);
                }
            
                SetJarIcon(runtime);
                RefreshShell();    

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void SetJarIcon(JavaRuntime runtime)
        {
            try
            {
                var javaDir = System.IO.Path.GetDirectoryName(runtime.JavawPath);
                if (string.IsNullOrEmpty(javaDir))
                    return;
                    
                string? iconPath = null;  

                var possibleIconPaths = new[]
                {
                    System.IO.Path.Combine(javaDir, "javaw.exe"),  
                    System.IO.Path.Combine(javaDir, "..", "lib", "icons", "jar.ico"),
                    System.IO.Path.Combine(javaDir, "java.exe"), 
                };

                foreach (var path in possibleIconPaths)
                {
                    var normalizedPath = System.IO.Path.GetFullPath(path);
                    if (System.IO.File.Exists(normalizedPath))
                    {
                        iconPath = normalizedPath;
                        break;
                    }
                }

                if (!string.IsNullOrEmpty(iconPath))
                {
                    using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\jarfile\DefaultIcon"))
                    {
                        key.SetValue("", $"\"{iconPath}\",0", RegistryValueKind.String);
                    }
                }
            }
            catch
            {
            }
        }

        private static void RefreshShell()
        {
            try
            {
                SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
            }
            catch
            {
            }
        }

        private static void CleanupFileExtsOverrides()
        {   
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.jar\OpenWithProgids", 
                    writable: true))
                {
                    if (key != null)
                    {
                        foreach (var valueName in key.GetValueNames())
                        {
                            try
                            {
                                key.DeleteValue(valueName);
                            }
                            catch
                            {
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.jar\OpenWithList", 
                    writable: true))
                {
                    if (key != null)
                    {
                        foreach (var valueName in key.GetValueNames())
                        {
                            try
                            {
                                key.DeleteValue(valueName);
                            }
                            catch
                            {
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(
                    @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.jar", 
                    throwOnMissingSubKey: false);
            }
            catch
            {
            }
        }
    }
}