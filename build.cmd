@echo off

set buildToolsPath=%ProgramFiles(x86)%\MSBuild
set msbuild="%buildToolsPath%\14.0\bin\amd64\MSBuild.exe"
set sln=GroboTrace\GroboTrace.sln
set VCTargetsPath=%buildToolsPath%\Microsoft.Cpp\v4.0\V140

set target=Output
if exist %target% rd /s /q %target% || exit /b 1
mkdir %target% || exit /b 1

Tools\nuget.exe restore %sln% || exit /b 1

%msbuild% /v:q /t:Rebuild /p:Configuration=Release /nodeReuse:false /maxcpucount %sln% || exit /b 1