@echo off

set buildToolsPath=%ProgramFiles(x86)%\MSBuild
set msbuild="%buildToolsPath%\14.0\bin\amd64\MSBuild.exe"
set sln=GroboTrace\GroboTrace.sln
SET VCTargetsPath=%buildToolsPath%\Microsoft.Cpp\v4.0\V140

set target=Output
if exist %target% rd /s /q %target%
mkdir %target%

Assemblies\nuget.exe restore %sln%                                                    

%msbuild% /v:q /t:Rebuild /p:Configuration=Release /nodeReuse:false /maxcpucount %sln%