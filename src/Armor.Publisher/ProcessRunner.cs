using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Armor.Publisher
{
    /// <summary>Thrown when an external command exits with a non-zero status.</summary>
    public sealed class ProcessFailedException : Exception
    {
        /// <summary>The exit code returned by the process.</summary>
        public int ExitCode { get; }

        /// <summary>Creates a new <see cref="ProcessFailedException"/>.</summary>
        public ProcessFailedException(string message, int exitCode) : base(message)
        {
            ExitCode = exitCode;
        }
    }

    /// <summary>Runs external tools (dotnet, iscc, hdiutil, dpkg-deb, ...) and streams their output.</summary>
    public static class ProcessRunner
    {
        /// <summary>
        /// Runs <paramref name="fileName"/> with <paramref name="arguments"/>, streaming stdout/stderr
        /// to the console. Throws <see cref="ProcessFailedException"/> on a non-zero exit code.
        /// </summary>
        /// <param name="fileName">Executable to run (resolved via PATH).</param>
        /// <param name="arguments">Arguments, already split into individual tokens.</param>
        /// <param name="workingDirectory">Working directory, or null for the current directory.</param>
        public static void Run(string fileName, IEnumerable<string> arguments, string? workingDirectory = null)
        {
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory
            };

            StringBuilder printable = new StringBuilder(fileName);
            foreach (string arg in arguments)
            {
                psi.ArgumentList.Add(arg);
                printable.Append(' ').Append(Quote(arg));
            }

            Console.WriteLine($"  $ {printable}");

            using Process process = new Process { StartInfo = psi };
            process.OutputDataReceived += (_, e) => { if (e.Data != null) Console.WriteLine("    " + e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) Console.Error.WriteLine("    " + e.Data); };

            try
            {
                process.Start();
            }
            catch (Exception ex)
            {
                throw new ProcessFailedException($"Failed to start '{fileName}': {ex.Message}. Is it installed and on PATH?", -1);
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();

            if (process.ExitCode != 0)
                throw new ProcessFailedException($"'{fileName}' exited with code {process.ExitCode}.", process.ExitCode);
        }

        private static string Quote(string arg)
        {
            return arg.Contains(' ') || arg.Length == 0 ? "\"" + arg + "\"" : arg;
        }
    }
}
