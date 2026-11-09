using System.Security.AccessControl;
using System.Security.Principal;

namespace SiteMonitor.Core.Config;

public static class DataFolder
{
    /// <summary>
    /// Restricts the data folder to Administrators + SYSTEM. Machine-scope DPAPI can be decrypted by
    /// any local account, so the SMTP password must not be readable by ordinary users.
    /// The logs subfolder (no secrets) is additionally readable by signed-in users, because Explorer
    /// runs un-elevated under UAC and would otherwise get "access denied" even for admins.
    /// </summary>
    public static void EnsureSecured(string path, string? logDirectory = null)
    {
        Directory.CreateDirectory(path);
        if (logDirectory is not null) Directory.CreateDirectory(logDirectory);
        if (!OperatingSystem.IsWindows()) return;
        const InheritanceFlags Inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        try
        {
            var sec = new DirectorySecurity();
            sec.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            foreach (var sid in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
                sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null),
                    FileSystemRights.FullControl, Inherit, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(path).SetAccessControl(sec);
        }
        catch
        {
            // Not fatal (e.g. running interactively without admin rights).
        }

        if (logDirectory is null) return;
        try
        {
            var logs = new DirectoryInfo(logDirectory);
            var logSec = logs.GetAccessControl();
            logSec.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
                FileSystemRights.ReadAndExecute, Inherit, PropagationFlags.None, AccessControlType.Allow));
            logs.SetAccessControl(logSec);
        }
        catch
        {
            // Not fatal; logs are still viewable from an elevated process.
        }
    }
}
