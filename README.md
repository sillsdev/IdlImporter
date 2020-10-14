# IDLImporter

Cross-platform tool to import COM interfaces from an IDL file for use with .NET. To be used in
addition to Microsoft's TLBImp. Can handle c-style arrays and `OLECHAR` pointers. Can run on
Windows and Linux.

## Installation

Install the [SIL.IdlImporter](https://www.nuget.org/packages/SIL.IdlImporter) nuget package.

## Building

```bash
msbuild /t:Restore
msbuild
```
