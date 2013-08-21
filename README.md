# soldr

*For people who use Visual Studio on a large codebase.*

**soldr** is a tool for working with many inter-dependent .sln files, making it easy to build &amp; manage big code bases based on Visual Studio.

**soldr** can build your entire codebase in a single command, without having to manage manually a set of complex MSBuild files.


## Why?

We have a large codebase, where each product (or big sub-product) lives in a separate .sln. Since we like to reuse code, some projects in one .sln can depend and use code from another project in another .sln.

Our options for building this monster are:

1. Ugly: Create a single, huge, **messy** .sln that contains all our .csproj projects, and specify all dependencies as project dependencies.
2. Manually: build each .sln, copy files over to the next .sln and build it, repeating the process until all dependencies are built.
3. Soldr: Use an automatic tool to build (or generate an MSBuild file) correctly and quickly.


## Features

* Generate an MSBuild file for building many .sln's correctly according to their dependencies
* Create a visual graph showing the dependencies between projects or between solutions
* Verify correctnes of .csproj files and assembly references, including things not checked by Visual Studio or MSBuild




## How it works

In a nutshell:

1. soldr scans your source directory for .sln and .csproj files.
2. Then, soldr deduces dependencies across projects from separate solutions by looking at project and assembly references.
3. Using the cool QuickGraph nuget package, soldr builds a graph of dependencies and sorts it to find the correct build order.
4. Finally, soldr can generate an MSBuild file for you to build any .sln or even your entire codebase with a single MSBuild command.



## Quick start

### Requirements

* Windows
* .NET 4.0
* Visual Studio (2010? TBD which version) or Visual Studio Shell

### Assumed source code conventions 

* Each .sln file is in it's own directory
* Assemblies used by an .sln are under a directory called Components, arranged by the name of the source .sln

Example directory structure:

    /
    /SolutionA/
              /SolutionA.sln
              /ProjectA
              ...
    /SolutionB/
              /SolutionB.sln
              /ProjectB (depends on ProjectA from SolutionA)
              /Components/SolutionA/ProjectA.dll

              

### Building soldr

Clone:

    git clone git@github.com:bdb-opensource/soldr.git
    
Build (requires .NET 4.0 and Visual Studio to be installed):

    cd soldr
    build.cmd
    
or, open `Soldr.sln` in Visual Studio and build it.

### Basic usage

Here's how to generate an MSBuild file that includes all inter-.sln dependencies.

    cd path/to/my/source/root
    path/to/soldr.exe -b . --all-slns -o
    MSBuild.exe build.proj 

### Help

    soldr --help

