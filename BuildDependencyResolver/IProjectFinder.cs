using System;
using System.Collections.Generic;
using BuildDependencyReader.ProjectFileParser;
using System.IO;
namespace BuildDependencyReader.BuildDependencyResolver
{
    public interface IProjectFinder
    {
        string GetBuildPathForAssemblyReference(AssemblyReference assemblyReference);
        IEnumerable<Project> FindProjectForAssemblyReference(AssemblyReference assemblyReference);
        FileInfo GetSLNFileForProject(Project project);
        IEnumerable<Project> GetProjectsOfSLN(FileInfo slnFileInfo);
    }
}
