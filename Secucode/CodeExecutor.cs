using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Python.Runtime;

namespace Secucode
{
    public class CodeExecutor
    {
        private const int TimeoutInMilliseconds = 5000;

        public async Task<string> ExecutePythonCodeAsync(string code, string input = "")
        {
            try
            {
                if (!PythonEngine.IsInitialized)
                    return "Python is not initialized. Please call InitializePython() from the main application.";

                using (Py.GIL())
                {
                    dynamic sys = Py.Import("sys");
                    dynamic io = Py.Import("io");

                    sys.stdin = io.StringIO(input);
                    sys.stdout = io.StringIO();
                    sys.stderr = io.StringIO();

                    var scope = Py.CreateScope();

                    string result = await Task.Run(() =>
                    {
                        try
                        {
                            scope.Exec(code); // ✅ Safe here because we run it inside GIL on the same thread
                            return sys.stdout.getvalue().ToString();
                        }
                        catch (PythonException ex)
                        {
                            return $"Python Error: {ex.Message}\n{sys.stderr.getvalue()}";
                        }
                    });

                    return result;
                }
            }
            catch (Exception ex)
            {
                return $"Execution error: {ex.Message}";
            }
        }



        public string ExecuteJavaCode(string code, string input = "")
        {
            string javaFile = "UserCode.java";
            File.WriteAllText(javaFile, code);

            string compileOutput = ExecuteCommand("javac", javaFile);
            if (!string.IsNullOrWhiteSpace(compileOutput))
                return $"Java Compilation Error:\n{compileOutput}";

            return ExecuteCommand("java", "-cp . UserCode", input);
        }

        public string ExecuteCppCode(string code, string input = "")
        {
            string cppFile = "UserCode.cpp";
            string exeFile = "UserCode.exe";
            File.WriteAllText(cppFile, code);

            string compileOutput = ExecuteCommand("g++", $"{cppFile} -o {exeFile}");
            if (!string.IsNullOrWhiteSpace(compileOutput))
                return $"C++ Compilation Error:\n{compileOutput}";

            return ExecuteCommand(exeFile, "", input);
        }

        public string ExecuteJavaScriptCode(string code, string input = "")
        {
            string jsFile = "UserCode.js";
            File.WriteAllText(jsFile, code);
            return ExecuteCommand("node", jsFile, input);
        }

        private string ExecuteCommand(string command, string args, string input = "")
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = command,
                    Arguments = args,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using Process proc = new Process { StartInfo = psi };
                proc.Start();

                if (!string.IsNullOrWhiteSpace(input))
                {
                    using StreamWriter writer = proc.StandardInput;
                    writer.Write(input);
                }

                if (!proc.WaitForExit(TimeoutInMilliseconds))
                {
                    proc.Kill();
                    return "Error: Execution timed out. Possible missing input or infinite loop.";
                }

                string output = proc.StandardOutput.ReadToEnd();
                string error = proc.StandardError.ReadToEnd();

                return string.IsNullOrWhiteSpace(error) ? output : $"Error:\n{error}";
            }
            catch (Exception ex)
            {
                return $"Execution failed: {ex.Message}";
            }
        }
    }
}
