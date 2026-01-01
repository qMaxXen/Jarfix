// Jarfix/Core/JarAssociationFixer.cs
using Microsoft.Win32;
using System;

namespace Jarfix.Core
{
    public static class JarAssociationFixer
    {
        public static bool SetJarAssociationUserScope(JavaRuntime runtime)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\.jar"))
                {
                    key.SetValue("", "jarfile", RegistryValueKind.String);
                }
                
                var cmd = $"\"{runtime.JavawPath}\" -jar \"%1\" %*";
                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\jarfile\shell\open\command"))
                {
                    key.SetValue("", cmd, RegistryValueKind.String);
                }
                
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}