public static class GroboTraceInstaller
{
    public static void Install(string targetDir, string commaSeparatedProcessNamesToProfile)
    {
        Console.Out.WriteLine("Will install GroboTrace to: {0}", targetDir);
        if (string.IsNullOrWhiteSpace(targetDir))
            throw new ArgumentException("targetDir is empty");
        var processNamesToProfile = commaSeparatedProcessNamesToProfile
            .Split(new [] {','}, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrEmpty(x))
            .ToArray();
        DoInstall(targetDir, processNamesToProfile);
        Console.Out.WriteLine("GroboTrace is successfully installed to: {0}", targetDir);
        Console.Out.WriteLine("GroboTrace will profile the following processes:\n{0}", string.Join(Environment.NewLine, processNamesToProfile));
    }

    private static void DoInstall(string targetDir, string[] processNamesToProfile)
    {
        PrepareTargetDir(targetDir);
        CopyGroboTraceBinaries(targetDir);
        SetEnvironmentVariables(targetDir);
        PrepareGroboTraceConfig(targetDir, processNamesToProfile);
    }

    private static void PrepareTargetDir(string targetDir)
    {
        if (!Directory.Exists(targetDir))
            Directory.CreateDirectory(targetDir);
        else
        {
            var guid = Guid.NewGuid();
            foreach (var file in Directory.EnumerateFiles(targetDir, "*"))
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception e)
                {
                    Console.Out.WriteLine($"Failed to delete file {file}, will move it to {guid}. ErrorMessage: {e.Message}");
                    File.Move(file, $"{file}_{guid}");
                }
            }
        }
    }

    private static void CopyGroboTraceBinaries(string targetDir)
    {
        var currentDir = Directory.GetCurrentDirectory();
        foreach (var file in Directory.EnumerateFiles(currentDir, "*").Where(x => binFileExtensions.Any(x.EndsWith)))
            File.Copy(file, Path.Combine(targetDir, Path.GetFileName(file)), overwrite: true);
    }

    private static void SetEnvironmentVariables(string targetDir)
    {
        Environment.SetEnvironmentVariable("COR_ENABLE_PROFILING", "1", EnvironmentVariableTarget.Machine);
        Environment.SetEnvironmentVariable("COR_PROFILER", "{1bde2824-ad74-46f0-95a4-d7e7dab3b6b6}", EnvironmentVariableTarget.Machine);
        Environment.SetEnvironmentVariable("COR_PROFILER_PATH", Path.Combine(targetDir, "ClrProfiler.dll"), EnvironmentVariableTarget.Machine);
    }

    private static void PrepareGroboTraceConfig(string targetDir, string[] processNamesToProfile)
    {
        var configFilename = Path.Combine(targetDir, "GroboTrace.ini");
        File.WriteAllText(configFilename, string.Join(Environment.NewLine, processNamesToProfile));
    }

    private static readonly string[] binFileExtensions = new [] {".dll", ".pdb"};
}

GroboTraceInstaller.Install(Octopus.Parameters["GroboTrace.TargetDir"], Octopus.Parameters["GroboTrace.ProcessNamesToProfile"]);