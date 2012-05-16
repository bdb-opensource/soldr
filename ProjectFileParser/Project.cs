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
        #region Constants

        protected static readonly XNamespace CSProjNamespace = "http://schemas.microsoft.com/developer/msbuild/2003";
        // Thanks to http://regexhero.net/tester/
        protected static readonly Regex CONFIG_PLATFORM_REGEX
            = new Regex(@"\' *\$\(Configuration\)(\|\$\(Platform\))? *\' *=+ *\'(?<config>[^\|]*)(\|(?<platform>[^']*))?\'");

        #endregion

        #region Public Members

        public string Name { get; protected set; }
        public string Path { get; protected set; }
        public IEnumerable<Project> ProjectReferences { get; protected set; }
        public IEnumerable<AssemblyReference> AssemblyReferences { get; protected set; }
        public IEnumerable<ProjectConfiguration> Configurations { get; protected set; }
        public Nullable<ProjectConfiguration> DefaultConfiguration { get; protected set; }

        #endregion

        #region Protected Members

        protected static Dictionary<String, Project> ResolvedProjectsCache = new Dictionary<string, Project>();

        #endregion

        #region Constructors

        protected Project() { }

        #endregion

        #region Public Methods

        public override string ToString()
        {
            return string.Format("{{ Project: Name = '{0}', Path = '{1}' }}", this.Name, this.Path);
        }

        public static Project FromCSProj(string filePath)
        {
            return GetProjectForPath(System.IO.Path.GetFullPath(Uri.UnescapeDataString(filePath)));
        }


        public static string ResolvePath(string basePath, string pathToResolve)
        {
            return System.IO.Path.IsPathRooted(pathToResolve) 
                   ? pathToResolve
                   : System.IO.Path.GetFullPath(System.IO.Path.Combine(basePath, pathToResolve));
        }

        #endregion

        #region Protected Methods

        protected static void ValidateFileExists(string filePath)
        {
            if (false == File.Exists(filePath))
            {
                throw new ArgumentException(String.Format("File does not exist: '{0}'", filePath));
            }
        }

        protected static Project GetProjectForPath(string fullPath)
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


        protected static Project CreateProjectFromCSProj(string fullPath)
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
                project.DefaultConfiguration = FindDefaultConfiguration(project, document);
                project.AssemblyReferences = GetAssemblyReferences(project.Path, projectDirectory, document).ToArray();
                project.ProjectReferences = GetProjectReferences(projectDirectory, document).ToArray();
                return project;
            }
            catch (Exception e)
            {
                throw new Exception("Error while trying to process project from path: " + fullPath, e);
            }
        }

        private static Nullable<ProjectConfiguration> FindDefaultConfiguration(Project project, XDocument document)
        {
            var defaultConfigurationElement = document.Descendants(CSProjNamespace + "Configuration").SingleOrDefault();
            if (null == defaultConfigurationElement)
            {
                return null;
            }
            string defaultConfigurationName = defaultConfigurationElement.Value.Trim();
            var configsWithMatchingName = project.Configurations
                                                 .Where(x => x.Configuration.ToLowerInvariant().Equals(defaultConfigurationName.ToLowerInvariant()))
                                                 .ToArray();

            string defaultPlatform = null;
            var defaultPlatformElement = document.Descendants(CSProjNamespace + "Platform").SingleOrDefault();
            if (null != defaultPlatformElement)
            {
                defaultPlatform = defaultPlatformElement.Value.Trim().ToLowerInvariant();
            }
            if (String.IsNullOrWhiteSpace(defaultPlatform)) {
                return configsWithMatchingName.SingleOrDefault(x => String.IsNullOrWhiteSpace(x.Platform));
            }

            return configsWithMatchingName.Where(x => false == String.IsNullOrWhiteSpace(x.Platform))
                                          .SingleOrDefault(x => defaultPlatform.Equals(x.Platform.ToLowerInvariant()));
        }

        protected static IEnumerable<ProjectConfiguration> GetProjectConfigurations(XDocument document)
        {
            return document.Descendants(CSProjNamespace + "PropertyGroup")
                           .Where(IsConfigurationPropertyGroup)
                           .Select(ParseProjectConfiguration);
        }

        protected static IEnumerable<AssemblyReference> GetAssemblyReferences(string projectFileName, string projectDirectory, XDocument csprojDocument)
        {
            return csprojDocument.Descendants(CSProjNamespace + "Reference")
                                 .Select(x => ParseAssemblyReferenceElement(projectFileName, projectDirectory, x));
        }

        protected static IEnumerable<Project> GetProjectReferences(string projectDirectory, XDocument csprojDocument)
        {
            return csprojDocument.Descendants(CSProjNamespace + "ProjectReference")
                                 .Select(x => GetProjectFromProjectReferenceNode(projectDirectory, x));
        }


        protected static ProjectConfiguration ParseProjectConfiguration(XElement configurationPropertyGroupElement)
        {
            var conditionAttr = configurationPropertyGroupElement.Attribute("Condition");
            var match = CONFIG_PLATFORM_REGEX.Match(conditionAttr.Value);
            if ((false == match.Success) || String.IsNullOrWhiteSpace(match.Groups["config"].Value))
            {
                throw new Exception(String.Format("Failed to parse configuration Condition attribute: '{0}'. Match was: '{1}'.",
                                                    conditionAttr, match));
            }
            var outputPath = configurationPropertyGroupElement.Descendants(CSProjNamespace + "OutputPath")
                                                    .Single()
                                                    .Value;
            return new ProjectConfiguration(match.Groups["config"].Value, match.Groups["platform"].Value, outputPath);
        }

        protected static bool IsConfigurationPropertyGroup(XElement propertyGroupElement)
        {
            var conditionAttribute = propertyGroupElement.Attribute("Condition");
            return (null != conditionAttribute)
                && conditionAttribute.Value.ToLowerInvariant().Contains("$(Configuration)".ToLowerInvariant());
        }

        protected static AssemblyReference ParseAssemblyReferenceElement(string projectFileName, string projectDirectory, XElement referenceNode)
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
            return new AssemblyReference(assemblyName, hintPath);
        }

        protected static Project GetProjectFromProjectReferenceNode(string projectDirectory, XElement projectReferenceNode)
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
            return project;
        }

        #endregion
    }
}
