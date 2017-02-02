# GroboTrace
Lightweight .Net performance profiler

GroboTrace is designed to run inside a .Net application 100% of its lifetime with relatively small performance overhead. 
It allows you to get an insight into current bottlencks of your apps right in production environment.

Known issues:
* GroboTrace currently does play well with multi-AppDomain apps, i.e. ASP.NET web sites hosted in IIS.
* GroboTrace might cause crashes of ReSharper NUnit Test Runner in VisualStudio.
* Memory usage overhead is currently not as small as it could be (see [#1](../../issues/1)).
