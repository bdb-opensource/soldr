using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using System.Text;
using System.IO;
using System.Text.RegularExpressions;

namespace BuildDependencyReader.ProjectFileParser
{
    public class Project
    {
        protected static readonly XNamespace CSProjNamespace = "http://schemas.microsoft.com/developer/msbuild/2003";

        public string Name { get; protected set; }
        public string Path { get; protected set; }
        public IEnumerable<Project> ProjectReferences { get; protected set; }
        public IEnumerable<AssemblyReference> AssemblyReferences { get; protected set; }
        private IEnumerable<ProjectConfiguration> Configurations { get; protected set; }

        protected static Dictionary<String, Project> ResolvedProjectsCache = new Dictionary<string, Project>();

        // Thanks to http://regexhero.net/tester/
        protected static readonly Regex CONFIG_PLATFORM_REGEX 
            = new Regex(@"\' *\$\(Configuration\)\|\$\(Platform\) *\' *=+ *\'(?<config>[^\|]*)\|(?<platform>[^']*)\'");

        protected Project() { }

        public override string ToString()
        {
            return string.Format("{{ Project: Name = '{0}', Path = '{1}' }}", this.Name, this.Path);
        }

        public static Project FromCSProj(string filePath)
        {
            return GetProjectForPath(System.IO.Path.GetFullPath(Uri.UnescapeDataString(filePath)));
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

        
        private static Project CreateProjectFromCSProj(string fullPath)
        {
            try
            {
                var project = new Project();
                var projectDirectory = System.IO.Path.GetDirectoryName(fullPath);

                ValidateFileExists(fullPath);

                project.Path = fullPath;

                var document = XDocument.Load(fullPath);

                project.Name = document.Descendants(CSProjNamespace + "AssemblyName").Single().Value;
                project.Configurations = GetProjectConfigurations(document).ToArray();
                project.AssemblyReferences = GetAssemblyReferences(project.Path, projectDirectory, document).ToArray();
                project.ProjectReferences = GetProjectReferences(projectDirectory, document).ToArray();
                return project;
            }
            catch (Exception e)
            {
                throw new Exception("Error while trying to process project from path: " + fullPath, e);
            }
        }

        private static IEnumerable<ProjectConfiguration> GetProjectConfigurations(XDocument document)
        {
            List<ProjectConfiguration> configurations = new List<ProjectConfiguration>();
            foreach (var configurationElement in document.Descendants(CSProjNamespace + "PropertyGroup")
                                                         .Where(x => x.Attribute("Condition").Value.Contains("$(Configuration)")))
            {
                var conditionAttr = configurationElement.Attribute("Condition");
                var match = CONFIG_PLATFORM_REGEX.Match(conditionAttr.Value);
                if ((false == match.Success) || (match.Groups.Cast<Capture>().Select(x => x.Value).Any(String.IsNullOrWhiteSpace)))
                {
                    throw new Exception(String.Format("Failed to parse configuration Condition attribute: '{0}'. Match was: '{1}'.", 
                                                      conditionAttr, match));
                }
                var outputPath = configurationElement.Descendants(CSProjNamespace + "OutputPath")
                                                     .Single()
                                                     .Value;
                configurations.Add(new ProjectConfiguration(match.Groups["config"].Value, match.Groups["platform"].Value, outputPath));
            }
            return configurations;
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

        public static string ResolvePath(string basePath, string pathToResolve)
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
