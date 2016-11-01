@echo off
set dotNetBasePath=%windir%\Microsoft.NET\Framework
if exist %dotNetBasePath%64 set dotNetBasePath=%dotNetBasePath%64
for /R %dotNetBasePath% %%i in (*msbuild.exe) do set msbuild=%%i

set target=GroboTrace\GroboTrace.sln
SET VCTargetsPath=C:\Program Files (x86)\MSBuild\Microsoft.Cpp\v4.0\V140

%msbuild% /v:q /t:Rebuild /p:Configuration=Release /nodeReuse:false /maxcpucount %target%