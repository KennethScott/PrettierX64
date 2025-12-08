using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace PrettierX64
{
    internal class NodeProcess
    {
        private readonly PrettierPackage _package;
        private string _installDir;
        private string _executable;

        private const string PrettierRelativePath = @"node_modules\prettier\bin\prettier.cjs";
        private const string PrettierRelativePathFallback =
            @"node_modules\prettier\bin\prettier.js";

        private const int PrettierTimeoutSeconds = 15;
        private const int NpmTimeoutMinutes = 5;

        private string Packages
        {
            get { return $"prettier@{_package.optionPage.EmbeddedVersion}"; }
        }

        public bool IsInstalling { get; private set; }

        public bool IsReadyToExecute()
        {
            return File.Exists(_executable);
        }

        public NodeProcess(PrettierPackage package)
        {
            _package = package;
        }

        public async Task<bool> EnsurePackageInstalledAsync()
        {
            // These values are refreshed on each run to ensure they match the Prettier version
            // value currently in settings (OptionPageGrid.EmbeddedVersion).
            _installDir = Path.Combine(
                Path.GetTempPath(),
                Vsix.Name,
                Packages
                    .Replace(':', '_')
                    .Replace('@', '_')
                    .Replace(Path.DirectorySeparatorChar, '_')
            );

            _executable = Path.Combine(_installDir, PrettierRelativePath);

            if (IsInstalling)
                return false;

            if (IsReadyToExecute())
                return true;

            IsInstalling = true;

            try
            {
                return await InstallEmbeddedPrettierAsync(isRetry: false).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.Log(ex);
                return false;
            }
            finally
            {
                IsInstalling = false;
            }
        }

        public async Task<string> ExecuteProcessAsync(
            string input,
            Encoding encoding,
            string filePath
        )
        {
            // Ensure our embedded/default Prettier is installed
            if (!await EnsurePackageInstalledAsync().ConfigureAwait(false))
                return null;

            string prettierScript = FindPrettierScript(filePath) ?? _executable;
            if (string.IsNullOrEmpty(prettierScript) || !File.Exists(prettierScript))
            {
                Logger.Log("Prettier script not found.");
                return null;
            }

            // Call prettier directly instead of via cmd /c
            var start = new ProcessStartInfo(
                "node.exe",
                $"\"{prettierScript}\" --stdin-filepath \"{filePath}\""
            )
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                StandardErrorEncoding = Encoding.UTF8,
                StandardOutputEncoding = encoding,
            };

            ModifyPathVariable(start);

            try
            {
                using (var proc = Process.Start(start))
                {
                    if (proc == null)
                    {
                        Logger.Log("Failed to start Prettier process.");
                        return null;
                    }

                    // Write input and close stdin so Prettier knows it's done
                    using (var writer = new StreamWriter(proc.StandardInput.BaseStream, encoding))
                    {
                        await writer.WriteAsync(input).ConfigureAwait(false);
                    }

                    // Read stdout/stderr concurrently
                    Task<string> stdOutTask = proc.StandardOutput.ReadToEndAsync();
                    Task<string> stdErrTask = proc.StandardError.ReadToEndAsync();

                    Task<string[]> completionTask = Task.WhenAll(stdOutTask, stdErrTask);
                    var timeoutTask = Task.Delay(TimeSpan.FromSeconds(PrettierTimeoutSeconds));

                    Task finished = await Task.WhenAny(completionTask, timeoutTask)
                        .ConfigureAwait(false);

                    if (finished == timeoutTask)
                    {
                        try
                        {
                            if (!proc.HasExited)
                                proc.Kill();
                        }
                        catch
                        {
                            // ignore kill failures
                        }

                        Logger.Log($"Prettier timed out after {PrettierTimeoutSeconds} seconds.");
                        return null;
                    }

                    // Both streams completed
                    string output = await stdOutTask.ConfigureAwait(false);
                    string error = await stdErrTask.ConfigureAwait(false);

                    if (!string.IsNullOrEmpty(error))
                        Logger.Log(error);

                    // No need to WaitForExit here; streams completing implies process exit
                    return output;
                }
            }
            catch (Exception ex)
            {
                Logger.Log(ex);
                return null;
            }
        }

        private async Task<bool> InstallEmbeddedPrettierAsync(bool isRetry)
        {
            if (!Directory.Exists(_installDir))
                Directory.CreateDirectory(_installDir);

            // 1. npm init -y
            Logger.Log($"npm init -y (working dir: {_installDir})");
            (bool Success, _, _) = await RunNpmAsync("init -y").ConfigureAwait(false);
            if (!Success)
            {
                Logger.Log("npm init -y failed.");
                return false;
            }

            // 2. npm install prettier@X
            Logger.Log($"npm install {Packages} (this can take a few minutes)");
            (bool Success, string Output, string Error) installResult = await RunNpmAsync(
                    $"install {Packages}"
                )
                .ConfigureAwait(false);

            if (!installResult.Success)
            {
                string error = installResult.Error ?? string.Empty;

                // Handle ETARGET (invalid version) by falling back to the default version
                // Only try this once to prevent infinite recursion
                if (
                    !isRetry
                    && error.Contains("code ETARGET")
                    && !_package.optionPage.EmbeddedVersion.Equals(
                        OptionPageGrid.PrettierFallbackVersion,
                        StringComparison.Ordinal
                    )
                )
                {
                    Logger.Log(
                        $"ETARGET detected for version '{_package.optionPage.EmbeddedVersion}'. "
                            + $"Falling back to '{OptionPageGrid.PrettierFallbackVersion}'."
                    );

                    _package.optionPage.EmbeddedVersion = OptionPageGrid.PrettierFallbackVersion;

                    // Recompute paths for the fallback version and retry once
                    _installDir = Path.Combine(
                        Path.GetTempPath(),
                        Vsix.Name,
                        Packages
                            .Replace(':', '_')
                            .Replace('@', '_')
                            .Replace(Path.DirectorySeparatorChar, '_')
                    );
                    _executable = Path.Combine(_installDir, PrettierRelativePath);

                    return await InstallEmbeddedPrettierAsync(isRetry: true).ConfigureAwait(false);
                }

                Logger.Log("npm install failed.");
                return false;
            }

            return true;
        }

        private async Task<(bool Success, string Output, string Error)> RunNpmAsync(
            string arguments,
            TimeSpan? timeout = null
        )
        {
            if (timeout == null)
            {
                timeout = TimeSpan.FromMinutes(NpmTimeoutMinutes);
            }

            // Find npm-cli.js - it's usually in the npm installation
            string npmCliPath = FindNpmCliPath();
            if (string.IsNullOrEmpty(npmCliPath))
            {
                Logger.Log("npm-cli.js not found.");
                return (false, null, "npm-cli.js not found.");
            }

            var start = new ProcessStartInfo("node.exe", $"\"{npmCliPath}\" {arguments}")
            {
                WorkingDirectory = _installDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            ModifyPathVariable(start);

            try
            {
                using (var proc = Process.Start(start))
                {
                    if (proc == null)
                    {
                        Logger.Log("Failed to start npm process.");
                        return (false, null, "Failed to start npm process.");
                    }

                    Task<string> stdOutTask = proc.StandardOutput.ReadToEndAsync();
                    Task<string> stdErrTask = proc.StandardError.ReadToEndAsync();

                    Task<string[]> completionTask = Task.WhenAll(stdOutTask, stdErrTask);
                    var timeoutTask = Task.Delay(timeout.Value);

                    Task finished = await Task.WhenAny(completionTask, timeoutTask)
                        .ConfigureAwait(false);

                    if (finished == timeoutTask)
                    {
                        try
                        {
                            if (!proc.HasExited)
                                proc.Kill();
                        }
                        catch
                        {
                            // ignore
                        }

                        string timeoutMessage = $"npm {arguments} timed out after {timeout.Value}.";
                        Logger.Log(timeoutMessage);
                        return (false, null, timeoutMessage);
                    }

                    string output = await stdOutTask.ConfigureAwait(false);
                    string error = await stdErrTask.ConfigureAwait(false);

                    if (!string.IsNullOrEmpty(output))
                        Logger.Log(output);

                    if (!string.IsNullOrEmpty(error))
                        Logger.Log(error);

                    bool success = proc.ExitCode == 0;
                    return (success, output, error);
                }
            }
            catch (Exception ex)
            {
                Logger.Log(ex);
                return (false, null, ex.ToString());
            }
        }

        private string FindNpmCliPath()
        {
            // npm-cli.js is typically in: <node_dir>\node_modules\npm\bin\npm-cli.js
            string ideDir = GetIdeDirectory();
            if (string.IsNullOrEmpty(ideDir))
                return null;

            // Check VS's bundled node first
            if (Directory.Exists(ideDir))
            {
                // Safely navigate up two directory levels
                DirectoryInfo ideDirectory = Directory.GetParent(ideDir);
                if (ideDirectory?.Parent != null)
                {
                    string parent = ideDirectory.Parent.FullName;

                    string[] candidates = new[]
                    {
                        Path.Combine(parent, @"Web\External\node_modules\npm\bin\npm-cli.js"),
                        Path.Combine(
                            ideDir,
                            @"Extensions\Microsoft\Web Tools\External\node_modules\npm\bin\npm-cli.js"
                        ),
                    };

                    foreach (string candidate in candidates)
                    {
                        if (File.Exists(candidate))
                            return candidate;
                    }
                }
            }

            // Fallback: try to find npm in PATH
            string npmCmd = FindExecutableInPath("npm.cmd");
            if (!string.IsNullOrEmpty(npmCmd))
            {
                // npm.cmd is in <npm_dir>\npm.cmd, npm-cli.js is in <npm_dir>\node_modules\npm\bin\npm-cli.js
                string npmDir = Path.GetDirectoryName(npmCmd);
                string cliPath = Path.Combine(npmDir, @"node_modules\npm\bin\npm-cli.js");
                if (File.Exists(cliPath))
                    return cliPath;
            }

            return null;
        }

        private string FindPrettierScript(string filePath)
        {
            string currentDir = filePath;

            while ((currentDir = Path.GetDirectoryName(currentDir)) != null)
            {
                if (File.Exists(Path.Combine(currentDir, "package.json")))
                {
                    // Try .cjs first (newer versions)
                    string script = Path.Combine(currentDir, PrettierRelativePath);
                    if (File.Exists(script))
                    {
                        Logger.Log($"Using prettier from {script}");
                        return script;
                    }

                    // Fallback to .js
                    script = Path.Combine(currentDir, PrettierRelativePathFallback);
                    if (File.Exists(script))
                    {
                        Logger.Log($"Using prettier from {script}");
                        return script;
                    }
                }
            }

            return null;
        }

        private static string FindExecutableInPath(string executable)
        {
            string pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(pathEnv))
                return null;

            foreach (string path in pathEnv.Split(';'))
            {
                try
                {
                    string fullPath = Path.Combine(path.Trim(), executable);
                    if (File.Exists(fullPath))
                        return fullPath;
                }
                catch { }
            }

            return null;
        }

        private static void ModifyPathVariable(ProcessStartInfo start)
        {
            string path = start.EnvironmentVariables["PATH"] ?? string.Empty;

            string ideDir = GetIdeDirectory();
            if (string.IsNullOrEmpty(ideDir))
                return; // Just return early, PATH is already set from start.EnvironmentVariables

            if (Directory.Exists(ideDir))
            {
                var pathsToAdd = new List<string>();

                // Safely navigate up two directory levels
                DirectoryInfo ideDirectory = Directory.GetParent(ideDir);
                if (ideDirectory?.Parent != null)
                {
                    string parent = ideDirectory.Parent.FullName;
                    string rc2Preview1Path = Path.Combine(parent, @"Web\External");

                    if (Directory.Exists(rc2Preview1Path))
                    {
                        pathsToAdd.Add(rc2Preview1Path);
                    }
                    else
                    {
                        pathsToAdd.Add(
                            Path.Combine(ideDir, @"Extensions\Microsoft\Web Tools\External")
                        );
                        pathsToAdd.Add(
                            Path.Combine(ideDir, @"Extensions\Microsoft\Web Tools\External\git")
                        );
                    }

                    path = $"{path};{string.Join(";", pathsToAdd)}";
                }
            }

            start.EnvironmentVariables["PATH"] = path;
        }

        private static string GetIdeDirectory()
        {
            try
            {
                var process = Process.GetCurrentProcess();
                return Path.GetDirectoryName(process.MainModule.FileName);
            }
            catch (Exception ex)
            {
                Logger.Log($"Unable to get IDE directory: {ex.Message}");
                return null;
            }
        }
    }
}
