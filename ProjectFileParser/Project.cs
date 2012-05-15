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
            return GetProjectForPath(System.IO.Path.GetFullPath(filePath));
        }

        private static Project GetProjectForPath(string fullPath)
        {
            Project cachedProject = null;
            if (ResolvedProjectsCache.TryGetValue(fullPath, out cachedProject))
            {
                return cachedProject;
            }

            var project = CreateProjectFromCSProj(fullPath);
            ResolvedProjectsCache.Add(fullPath, project);
            return project;
        }

        public IEnumerable<KeyValuePair<Project, Project>> DeepDependencies()
        {
            var projectsToTraverse = new Queue<KeyValuePair<Project,Project>>();
            projectsToTraverse.Enqueue(new KeyValuePair<Project, Project>(this, this));

            while (projectsToTraverse.Any())
            {
                var projectPair = projectsToTraverse.Dequeue();
                var project = projectPair.Value;

                if (projectPair.Key != projectPair.Value)
                {
                    yield return projectPair;
                }

                foreach (var subProject in project.ProjectReferences)
                {
                    projectsToTraverse.Enqueue(new KeyValuePair<Project, Project>(project,subProject));
                }
            }
        }

        private static Project CreateProjectFromCSProj(string fullPath)
        {
            var project = new Project();
            var projectDirectory = System.IO.Path.GetDirectoryName(fullPath);

            ValidateFileExists(fullPath);

            project.Path = fullPath;

            var document = XDocument.Load(fullPath);

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
                    hintPath = ResolvePath(projectDirectory, Uri.UnescapeDataString(hintPathNode.Value));
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
                string absoluteFilePath = ResolvePath(projectDirectory, Uri.UnescapeDataString(projectReferenceNode.Attribute("Include").Value));
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
