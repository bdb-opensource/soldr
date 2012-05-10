using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using System.Text;
using System.IO;

namespace BuildDependencyReader.ProjectFileParser
{
    public class Project
    {
        protected static readonly XNamespace CSProjNamespace = "http://schemas.microsoft.com/developer/msbuild/2003";

        public string Name { get; protected set; }
        public string Path { get; protected set; }
        public IEnumerable<Project> ProjectReferences { get; protected set; }
        public IEnumerable<AssemblyReference> AssemblyReferences { get; protected set; }

        protected static Dictionary<String, Project> ResolvedProjectsCache = new Dictionary<string, Project>();

        protected Project() { }

        public override string ToString()
        {
            return string.Format("{{ Project: Name = '{0}', Path = '{1}' }}", this.Name, this.Path);
        }

        public static Project FromCSProj(string filePath)
        {
            var fullPath = System.IO.Path.GetFullPath(filePath);

            Project cachedProject = null;
            if (ResolvedProjectsCache.TryGetValue(filePath, out cachedProject))
            {
                return cachedProject;
            }

            var project = CreateProjectFromCSProj(filePath, fullPath);
            ResolvedProjectsCache.Add(fullPath, project);
            return project;
        }

        private static Project CreateProjectFromCSProj(string filePath, string fullPath)
        {
            var project = new Project();
            var projectDirectory = System.IO.Path.GetDirectoryName(fullPath);

            ValidateFileExists(fullPath);

            project.Path = fullPath;

            var document = XDocument.Load(filePath);

            project.Name = document.Descendants(CSProjNamespace + "AssemblyName").Single().Value;
            project.AssemblyReferences = GetAssemblyReferences(project.Path, projectDirectory, document);
            project.ProjectReferences = GetProjectReferences(projectDirectory, document);
            return project;
        }

        private static IEnumerable<AssemblyReference> GetAssemblyReferences(string projectFileName, string projectDirectory, XDocument csprojDocument)
        {
            var assemblyReferences = new List<AssemblyReference>();
            foreach (var referenceNode in csprojDocument.Descendants(CSProjNamespace + "Reference"))
            {
                var assemblyName = referenceNode.Attribute("Include").Value;
                string hintPath = null;
                var hintPathNode = referenceNode.Descendants(CSProjNamespace + "HintPath")
                                                .SingleOrDefault();
                if (null != hintPathNode)
                {
                    hintPath = ResolvePath(projectDirectory, hintPathNode.Value);
                    if (false == File.Exists(hintPath))
                    {
                        throw new AssemblyReferenceHintPathDoesNotExist(assemblyName, hintPath, projectFileName);
                    }
                }

                assemblyReferences.Add(new AssemblyReference(assemblyName, hintPath));
            }
            return assemblyReferences;
        }

        private static IEnumerable<Project> GetProjectReferences(string projectDirectory, XDocument csprojDocument)
        {
            foreach (var projectReferenceNode in csprojDocument.Descendants(CSProjNamespace + "ProjectReference"))
            {
                string absoluteFilePath = ResolvePath(projectDirectory, projectReferenceNode.Attribute("Include").Value);
                ValidateFileExists(absoluteFilePath);
                Project project;
                try
                {
                    project = Project.FromCSProj(absoluteFilePath);
                }
                catch (Exception e)
                {
                    throw new ArgumentException("Error when trying to resolve referenced project: " + absoluteFilePath, e);
                }
                yield return project;
            }
        }

        private static string ResolvePath(string basePath, string pathToResolve)
        {
            return System.IO.Path.IsPathRooted(pathToResolve) 
                   ? pathToResolve
                   : System.IO.Path.GetFullPath(System.IO.Path.Combine(basePath, pathToResolve));
        }

        private static void ValidateFileExists(string filePath)
        {
            if (false == File.Exists(filePath))
            {
                throw new ArgumentException(String.Format("File does not exist: '{0}'", filePath));
            }
        }
    }
}
