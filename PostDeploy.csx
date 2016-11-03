const string outDir = @"C:\GroboTrace";
const string configFileName = "GroboTrace.ini";
var fileExtensions = new [] {".dll", ".pdb", ".xml"};

if(Directory.Exists(outDir)){
	var guid = Guid.NewGuid();
	foreach(var file in Directory.EnumerateFiles(outDir, "*")){
		var fileToDelete = file;
		if(fileExtensions.Any(file.EndsWith)){
			fileToDelete = file + guid;
			File.Move(file, fileToDelete);
		}
		try{
			File.Delete(fileToDelete);
		}
		catch (Exception e){
			Console.Out.WriteLine("Can't delete {0} : {1}", fileToDelete, e.Message);
		}
	}
}else{
	Directory.CreateDirectory(outDir);
}

foreach(var file in Directory.EnumerateFiles(Directory.GetCurrentDirectory(), "*")){
	if(fileExtensions.Any(file.EndsWith)){
		var fileName = Path.GetFileName(file);
		File.Copy(file, Path.Combine(outDir, fileName), true);
	}
}

Environment.SetEnvironmentVariable("COR_ENABLE_PROFILING", "1", EnvironmentVariableTarget.Machine);
Environment.SetEnvironmentVariable("COR_PROFILER", "{1bde2824-ad74-46f0-95a4-d7e7dab3b6b6}", EnvironmentVariableTarget.Machine);
Environment.SetEnvironmentVariable("COR_PROFILER_PATH", @"C:\GroboTrace\ClrProfiler.dll", EnvironmentVariableTarget.Machine);