# cosln

*For people who use Visual Studio on a large codebase.*

**cosln** is a tool for working with many inter-dependent .sln files, making it easy to build &amp; manage big code bases based on Visual Studio.

**cosln** can build your entire codebase in a single command, without having to manage manually a set of complex MSBuild files.

## Why?

We have a large codebase, where each product (or big sub-product) lives in a separate .sln. Since we like to reuse code, some projects in one .sln can depend and use code from another project in another .sln.

Our options for building this monster are:

1. Ugly: Create a single, huge, **messy** .sln that contains all our .csproj projects, and specify all depencies as project dependencies.
2. Manually: build each .sln, copy files over to the next .sln and build it, repeating the process until all dependencies are built.
3. Cosln: Use an automatic tool to build (or generate an MSBuild file) correctly and quickly.


## Features

* Generate an MSBuild file for building many .sln's correctly according to their dependencies
* Create a visual graph showing the dependencies between projects or between solutions
* Verify correctnes of .csproj files and assembly references, including things not checked by Visual Studio or MSBuild




## How it works

In a nutshell:

1. cosln scans your source directory for .sln and .csproj files.
2. Then, cosln deduces dependencies across projects from separate solutions by looking at project and assembly references.
3. Using the cool QuickGraph nuget package, cosln builds a graph of dependencies and sorts it to find the correct build order.
4. Finally, cosln can generate an MSBuild file for you to build any .sln or even your entire codebase with a single MSBuild command.
