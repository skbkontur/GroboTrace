const string outDir = @"C:\GroboTrace";

if(Directory.Exists(outDir)){
	var guid = Guid.NewGuid();
	foreach(var file in Directory.EnumerateFiles(outDir, "*")){
		if(file.EndsWith(".dll") || file.EndsWith(".pdb"))
			File.Move(file, file + guid);
	}
}else{
	Directory.CreateDirectory(outDir);
}

foreach(var file in Directory.EnumerateFiles(Directory.GetCurrentDirectory(), "*")){
	var fileName = Path.GetFileName(file);
	File.Copy(file, Path.Combine(outDir, fileName), true);
}

Environment.SetEnvironmentVariable("COR_ENABLE_PROFILING", "1", EnvironmentVariableTarget.Machine);
Environment.SetEnvironmentVariable("COR_PROFILER", "{1bde2824-ad74-46f0-95a4-d7e7dab3b6b6}", EnvironmentVariableTarget.Machine);
Environment.SetEnvironmentVariable("COR_PROFILER_PATH", "C:\GroboTrace\ClrProfiler.dll", EnvironmentVariableTarget.Machine);