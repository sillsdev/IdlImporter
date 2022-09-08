# IDLImporter

[![NuGet version (SIL.IdlImporter)](https://img.shields.io/nuget/v/SIL.IdlImporter.svg?style=flat-square)](https://www.nuget.org/packages/SIL.IdlImporter)
[![Build and Pack](https://github.com/sillsdev/IdlImporter/actions/workflows/CI-CD.yml/badge.svg)](https://github.com/sillsdev/IdlImporter/actions/workflows/CI-CD.yml)

Cross-platform tool to import COM interfaces from an IDL file for use with
.NET. To be used in addition to Microsoft's TLBImp. Can handle c-style arrays
and `OLECHAR` pointers. Can run on Windows and Linux.

## Installation

Install the [SIL.IdlImporter](https://www.nuget.org/packages/SIL.IdlImporter) nuget package.

## Building

```bash
dotnet restore
dotnet build
```
