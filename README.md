# GroboTrace
GroboTrace is a lightweight .NET performance profiler. It is designed to run inside any .NET application 100% of its lifetime with relatively small performance overhead. This allows you to get an insight into current bottlencks of your app right in production environment.

## Setup
1. Build GroboTrace using `build.cmd` script
2. On each host where you are going to use GroboTrace:
  1. Create directory for profiler, e.g. `C:\GroboTrace`
  2. Copy the following files to profiler directory:
  ```
  Output\ClrProfiler.dll
  Output\ClrProfiler.pdb
  Output\GroboTrace.dll
  Output\GroboTrace.pdb
  Output\GroboTrace.Core.dll
  Output\GroboTrace.Core.pdb
  ```
  3. Set machine-wide environment variables:
  ```
  COR_ENABLE_PROFILING = 1
  COR_PROFILER_PATH = C:\GroboTrace\ClrProfiler.dll
  COR_PROFILER = {1bde2824-ad74-46f0-95a4-d7e7dab3b6b6}
  ```
  4. Create config file `C:\GroboTrace\GroboTrace.ini` with CRLF-separated list of process names you want to profile:
  ```
  Foo.exe
  Bar.Baz.exe
  ```

## Known issues:
* GroboTrace currently does not play well with multi-AppDomain apps, i.e. ASP.NET web sites hosted in IIS.
* GroboTrace might cause crashes of ReSharper NUnit Test Runner in VisualStudio.
* Memory usage overhead is currently not as small as it could be (see [#1](../../issues/1)).
