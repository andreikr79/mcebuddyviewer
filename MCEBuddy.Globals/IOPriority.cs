using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using System.IO;
using System.Diagnostics;

namespace MCEBuddy.Globals
{
    /// <summary>
    /// I/O scheduling priority
    /// </summary>
    public enum PriorityTypes
    {
        ABOVE_NORMAL_PRIORITY_CLASS = 0x00008000,
        BELOW_NORMAL_PRIORITY_CLASS = 0x00004000,
        HIGH_PRIORITY_CLASS = 0x00000080,
        IDLE_PRIORITY_CLASS = 0x00000040,
        NORMAL_PRIORITY_CLASS = 0x00000020,
        PROCESS_MODE_BACKGROUND_BEGIN = 0x00100000, // Not supported by Windows XP/2003
        PROCESS_MODE_BACKGROUND_END = 0x00200000, // Not supported by Windows XP/2003
        REALTIME_PRIORITY_CLASS = 0x00000100
    }

    public class IOPriority
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool SetPriorityClass(IntPtr handle, uint priorityClass);

        [DllImport("kernel32.dll")]
        static extern IntPtr OpenThread(ThreadAccess dwDesiredAccess, bool bInheritHandle, uint dwThreadId);
        
        [DllImport("kernel32.dll")]
        static extern uint SuspendThread(IntPtr hThread);

        [DllImport("kernel32.dll")]
        static extern int ResumeThread(IntPtr hThread);

        [Flags]
        public enum ThreadAccess : int
        {
            TERMINATE = (0x0001),
            SUSPEND_RESUME = (0x0002),
            GET_CONTEXT = (0x0008),
            SET_CONTEXT = (0x0010),
            SET_INFORMATION = (0x0020),
            QUERY_INFORMATION = (0x0040),
            SET_THREAD_TOKEN = (0x0080),
            IMPERSONATE = (0x0100),
            DIRECT_IMPERSONATION = (0x0200)
        }

        /// <summary>
        /// Suspends a process
        /// </summary>
        /// <param name="PID">Process ID</param>
        public static void SuspendProcess(int PID)
        {
            Process proc = Process.GetProcessById(PID);

            if (proc.ProcessName == string.Empty)
                return;

            foreach (ProcessThread pT in proc.Threads)
            {
                IntPtr pOpenThread = OpenThread(ThreadAccess.SUSPEND_RESUME, false, (uint)pT.Id);

                if (pOpenThread == IntPtr.Zero)
                {
                    break;
                }

                SuspendThread(pOpenThread);
            }
        }

        /// <summary>
        /// Resumes a process
        /// </summary>
        /// <param name="PID">Process ID</param>
        public static void ResumeProcess(int PID)
        {
            Process proc = Process.GetProcessById(PID);

            if (proc.ProcessName == string.Empty)
                return;

            foreach (ProcessThread pT in proc.Threads)
            {
                IntPtr pOpenThread = OpenThread(ThreadAccess.SUSPEND_RESUME, false, (uint)pT.Id);

                if (pOpenThread == IntPtr.Zero)
                {
                    break;
                }

                ResumeThread(pOpenThread);
            }
        }

        /// <summary>
        /// Change the scheduling priority for the I/O operations for the current process
        /// </summary>
        /// <param name="priority">I/O Priority</param>
        public static void SetPriority(PriorityTypes priority)
        {
            try { SetPriorityClass(GetCurrentProcess(), (uint)priority); }
            catch { }
        }

        /// <summary>
        /// Change the scheduling priority for the I/O operations for a specific process
        /// </summary>
        /// <param name="process">Pointer to the process</param>
        /// <param name="priority">I/O Priority</param>
        public static void SetPriority(IntPtr process, PriorityTypes priority)
        {
            if (priority == PriorityTypes.PROCESS_MODE_BACKGROUND_BEGIN || priority == PriorityTypes.PROCESS_MODE_BACKGROUND_END)
                throw new ArgumentException("Process mode background can only set for current process");

            try { SetPriorityClass(process, (uint)priority); }
            catch { }
        }
    }
}
