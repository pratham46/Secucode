using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace Secucode
{
    // Runs student code as a child process in a fresh temp folder.
    // ponytail: no sandbox — code runs with the app's privileges; isolate (container/VM/low-integrity user) if exams are high-stakes.
    public static class CodeExecutor
    {
        private const int RunTimeoutMs = 5000;
        private const int CompileTimeoutMs = 30000;

        public static Task<string> RunAsync(string language, string code, string input) =>
            Task.Run(() => Run(language, code, input));

        private static string Run(string language, string code, string input)
        {
            string dir = Path.Combine(Path.GetTempPath(), "secucode_run_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                switch (language)
                {
                    case "Python":
                        File.WriteAllText(Path.Combine(dir, "main.py"), code);
                        return Exec("python", "main.py", dir, input, RunTimeoutMs).Output;

                    case "JavaScript":
                        File.WriteAllText(Path.Combine(dir, "main.js"), code);
                        return Exec("node", "main.js", dir, input, RunTimeoutMs).Output;

                    case "Java":
                        // Single-file source launch (JDK 11+): any class name works, no separate javac step.
                        File.WriteAllText(Path.Combine(dir, "Main.java"), code);
                        return Exec("java", "Main.java", dir, input, CompileTimeoutMs).Output;

                    case "C++":
                        File.WriteAllText(Path.Combine(dir, "main.cpp"), code);
                        var compile = Exec("g++", "main.cpp -o main.exe", dir, "", CompileTimeoutMs);
                        if (compile.ExitCode != 0)
                            return "C++ Compilation Error:\n" + compile.Output;
                        return Exec(Path.Combine(dir, "main.exe"), "", dir, input, RunTimeoutMs).Output;

                    default:
                        return "Unknown language: " + language;
                }
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { /* exe may still be locked briefly */ }
            }
        }

        private static (int ExitCode, string Output) Exec(string file, string args, string workDir, string input, int timeoutMs)
        {
            var psi = new ProcessStartInfo(file, args)
            {
                WorkingDirectory = workDir,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            // Output goes to a plain TextBox; ANSI color codes would show up as garbage.
            psi.Environment.Remove("FORCE_COLOR");
            psi.Environment["NO_COLOR"] = "1";

            Process proc;
            try
            {
                proc = Process.Start(psi)!;
            }
            catch (Win32Exception)
            {
                return (-1, $"'{Path.GetFileName(file)}' was not found. Install it and make sure it is on PATH.");
            }

            using (proc)
            {
                // Read both streams asynchronously so a chatty program can't fill the pipe and deadlock.
                var stdout = proc.StandardOutput.ReadToEndAsync();
                var stderr = proc.StandardError.ReadToEndAsync();

                try
                {
                    proc.StandardInput.Write(input);
                    proc.StandardInput.Close(); // EOF, so programs reading input don't hang until timeout
                }
                catch (IOException) { /* program exited without reading stdin */ }

                if (!proc.WaitForExit(timeoutMs))
                {
                    proc.Kill(entireProcessTree: true);
                    return (-1, "Error: Execution timed out (infinite loop or waiting for more input?).");
                }

                string output = stdout.Result;
                string error = stderr.Result;
                if (proc.ExitCode != 0)
                    return (proc.ExitCode, output + (string.IsNullOrWhiteSpace(error) ? "" : "\nError:\n" + error) + $"\n(exit code {proc.ExitCode})");
                return (0, output + (string.IsNullOrWhiteSpace(error) ? "" : "\n" + error));
            }
        }
    }
}
