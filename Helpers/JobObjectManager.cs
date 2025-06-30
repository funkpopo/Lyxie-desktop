using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Lyxie_desktop.Helpers
{
    /// <summary>
    /// Manages a Windows Job Object to ensure that all child processes are terminated when the parent process exits.
    /// This class is Windows-specific.
    /// </summary>
    public sealed class JobObjectManager : IDisposable
    {
        private IntPtr _jobHandle;
        private bool _disposed;

        public JobObjectManager()
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("JobObjectManager is only supported on Windows.");
            }

            _jobHandle = CreateJobObject(IntPtr.Zero, null);
            if (_jobHandle == IntPtr.Zero)
            {
                throw new InvalidOperationException($"Failed to create a job object. Error: {Marshal.GetLastWin32Error()}");
            }

            var extendedInfo = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
            {
                BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
                {
                    LimitFlags = JobObjectLimit.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                }
            };

            var length = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
            var pExtendedInfo = Marshal.AllocHGlobal(length);
            try
            {
                Marshal.StructureToPtr(extendedInfo, pExtendedInfo, false);
                if (!SetInformationJobObject(_jobHandle, JobObjectInfoClass.JobObjectExtendedLimitInformation, pExtendedInfo, (uint)length))
                {
                    throw new InvalidOperationException($"Failed to set job object information. Error: {Marshal.GetLastWin32Error()}");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(pExtendedInfo);
            }
        }

        public bool AddProcess(Process process)
        {
            if (process == null) throw new ArgumentNullException(nameof(process));
            return AddProcess(process.Handle);
        }

        public bool AddProcess(IntPtr processHandle)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(JobObjectManager));
            if (_jobHandle == IntPtr.Zero) return false;

            return AssignProcessToJobObject(_jobHandle, processHandle);
        }

        public void Dispose()
        {
            if (_disposed) return;
            
            Close();
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        private void Close()
        {
            if (_jobHandle != IntPtr.Zero)
            {
                CloseHandle(_jobHandle);
                _jobHandle = IntPtr.Zero;
            }
        }

        ~JobObjectManager()
        {
            Close();
        }

        #region P/Invoke Declarations

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetInformationJobObject(IntPtr hJob, JobObjectInfoClass jobObjectInfoClass, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        private enum JobObjectInfoClass
        {
            JobObjectBasicAccountingInformation = 1,
            JobObjectBasicLimitInformation = 2,
            JobObjectBasicProcessIdList = 3,
            JobObjectBasicUIRestrictions = 4,
            JobObjectSecurityLimitInformation = 5,
            JobObjectEndOfJobTimeInformation = 6,
            JobObjectAssociateCompletionPortInformation = 7,
            JobObjectBasicAndIoAccountingInformation = 8,
            JobObjectExtendedLimitInformation = 9,
            JobObjectJobSetInformation = 10,
            JobObjectGroupInformation = 11,
            JobObjectNotificationLimitInformation = 12,
            JobObjectLimitViolationInformation = 13,
            JobObjectGroupInformationEx = 14,
            JobObjectCpuRateControlInformation = 15,
            JobObjectCompletionFilter = 16,
            JobObjectCompletionCounter = 17,
            JobObjectNetRateControlInformation = 18,
            JobObjectNotificationLimitInformation2 = 19,
            JobObjectLimitViolationInformation2 = 20,
            JobObjectCreateSilo = 21,
            JobObjectSiloBasicInformation = 22,
            JobObjectSiloRootDirectory = 23,
            JobObjectServerSiloBasicInformation = 24,
            JobObjectServerSiloUserSharedData = 25,
            JobObjectServerSiloInitialize = 26,
            JobObjectServerSiloRunningState = 27,
            JobObjectSiloObjectDirectories = 28,
            MaxJobObjectInfoClass = 29
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public Int64 PerProcessUserTimeLimit;
            public Int64 PerJobUserTimeLimit;
            public JobObjectLimit LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public UInt32 ActiveProcessLimit;
            public Int64 Affinity;
            public UInt32 PriorityClass;
            public UInt32 SchedulingClass;
        }

        [Flags]
        private enum JobObjectLimit : uint
        {
            JOB_OBJECT_LIMIT_WORKINGSET = 0x00000001,
            JOB_OBJECT_LIMIT_PROCESS_TIME = 0x00000002,
            JOB_OBJECT_LIMIT_JOB_TIME = 0x00000004,
            JOB_OBJECT_LIMIT_ACTIVE_PROCESS = 0x00000008,
            JOB_OBJECT_LIMIT_AFFINITY = 0x00000010,
            JOB_OBJECT_LIMIT_PRIORITY_CLASS = 0x00000020,
            JOB_OBJECT_LIMIT_PRESERVE_JOB_TIME = 0x00000040,
            JOB_OBJECT_LIMIT_SCHEDULING_CLASS = 0x00000080,
            JOB_OBJECT_LIMIT_PROCESS_MEMORY = 0x00000100,
            JOB_OBJECT_LIMIT_JOB_MEMORY = 0x00000200,
            JOB_OBJECT_LIMIT_DIE_ON_UNHANDLED_EXCEPTION = 0x00000400,
            JOB_OBJECT_LIMIT_BREAKAWAY_OK = 0x00000800,
            JOB_OBJECT_LIMIT_SILENT_BREAKAWAY_OK = 0x00001000,
            JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000,
            JOB_OBJECT_LIMIT_SUBSET_AFFINITY = 0x00004000,
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IO_COUNTERS
        {
            public UInt64 ReadOperationCount;
            public UInt64 WriteOperationCount;
            public UInt64 OtherOperationCount;
            public UInt64 ReadTransferCount;
            public UInt64 WriteTransferCount;
            public UInt64 OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        #endregion
    }
} 