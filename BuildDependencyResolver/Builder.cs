using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BuildDependencyReader.Common;
using BuildDependencyReader.ProjectFileParser;
using Mono.Cecil;
using System.IO;

namespace BuildDependencyReader.BuildDependencyResolver
{
    public class Builder
    {
        protected static readonly log4net.ILog _logger = log4net.LogManager.GetLogger(
            System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);


        /// <summary>
        /// <para>Builds the given solution. Steps:</para>
        /// <para>1. Update dependencies (components) by copying all referenced assemblies from their building projects' output paths to the HintPath</para>
        /// <para>2. Clean the solution</para>
        /// <para>3. Build the solution</para>
        /// <para>Use <paramref name="ignoredDependencyAssemblies"/> to ignore system and/or third party assemblies that are not part of the build tree (those that will not be found when the <paramref name="projectFinder"/> will search for a project that builds them)</para>
        /// </summary>
        /// <param name="projectFinder"></param>
        /// <param name="solutionFileName"></param>
        /// <param name="ignoredDependencyAssemblies">Patterns for dependent assemblies to ignore when trying to find a building project to copy from.</param>
        /// <param name="ignoreOnlyMatching">Flips the meaning of the ignored assemblies so that ALL assemblies will be ignored, EXCEPT the ones matching the given patterns</param>
        public static void BuildSolution(IProjectFinder projectFinder, string solutionFileName, Regex[] ignoredDependencyAssemblies, bool ignoreOnlyMatching, bool ignoreMissing)
        {
            _logger.InfoFormat("Building Solution: '{0}'", solutionFileName);
            UpdateComponentsFromBuiltProjects(projectFinder, solutionFileName, ignoredDependencyAssemblies, ignoreOnlyMatching, ignoreMissing);
            ValidateSolutionReadyForBuild(projectFinder, solutionFileName, ignoredDependencyAssemblies, ignoreOnlyMatching);
            _logger.DebugFormat("\tCleaning...");
            MSBuild(solutionFileName, "/t:clean");
            _logger.DebugFormat("\tBuilding...");
            MSBuild(solutionFileName);
            _logger.InfoFormat("Done: {0} ('{1}')\n", Path.GetFileName(solutionFileName), solutionFileName);
        }

        protected static void ValidateSolutionReadyForBuild(IProjectFinder projectFinder, string solutionFileName, Regex[] ignoredDependencyAssemblies, bool ignoreOnlyMatching)
        {
            foreach (var project in projectFinder.GetProjectsOfSLN(solutionFileName))
            {
                project.ValidateHintPaths(ignoredDependencyAssemblies, ignoreOnlyMatching);
                project.ValidateAssemblyReferencesAreAvailable();
            }
        }

        public static void UpdateComponentsFromBuiltProjects(IProjectFinder projectFinder, string solutionFileName, Regex[] assemblyNamePatterns, bool ignoreOnlyMatching, bool ignoreMissing)
        {
            _logger.DebugFormat("Updating components: {0} (copying dependencies required for '{1}')...", Path.GetFileName(solutionFileName), solutionFileName);
            var assemblyReferences = projectFinder.GetProjectsOfSLN(solutionFileName)
                                                  .SelectMany(x => x.AssemblyReferences)
                                                  .Distinct();
            Builder.CopyAssemblyReferencesFromBuiltProjects(projectFinder, assemblyNamePatterns, ignoreOnlyMatching, assemblyReferences, ignoreMissing);
            _logger.InfoFormat("Updated components required by: {0} ('{1}')", Path.GetFileName(solutionFileName), solutionFileName);
        }

        protected static bool IncludeAssemblyWhenCopyingDeps(AssemblyReference assemblyReference, Regex[] assemblyNamePatterns, bool ignoreOnlyMatching)
        {
            if (assemblyNamePatterns.Any())
            {
                return BoolExtensions.Flip(assemblyNamePatterns.Any(r => r.IsMatch(assemblyReference.Name)), ignoreOnlyMatching);
            }
            // when no patterns are given, include all assemblies (don't filter)
            return true;
        }


        protected static void MSBuild(string solutionFileName, string args = "")
        {
            var process = new Process();
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.UseShellExecute = false;
            // TODO: Remove hard-coded path to msbuild
            process.StartInfo.FileName = @"C:\Windows\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe";
            process.StartInfo.Arguments = String.Format("/nologo /v:quiet \"{0}\" {1}", solutionFileName, args);
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.RedirectStandardOutput = true;
            process.Start();

            // Must read process outputs before calling WaitForExit to prevent deadlocks
            LogWarnProcessOutputs(process, Path.GetFileName(solutionFileName));

            process.WaitForExit();
            if (0 != process.ExitCode)
            {
                var errorMessage = "Build failed: " + solutionFileName;
                _logger.Error(errorMessage);
                throw new Exception(errorMessage);
            }
        }

        protected static void LogWarnProcessOutputs(Process process, string messagePrefix)
        {
            var processStdOut = process.StandardOutput.ReadToEnd().Trim();
            if (processStdOut.Any())
            {
                _logger.WarnFormat("{0}: stdout of '{1} {2}':\n\n{3}\n", messagePrefix, process.StartInfo.FileName, process.StartInfo.Arguments, StringExtensions.Tabify(processStdOut));
            }
            var processStdErr = process.StandardError.ReadToEnd().Trim();
            if (processStdErr.Any())
            {
                _logger.WarnFormat("{0}: stderr of '{1} {2}':\n\n{3}\n", messagePrefix, process.StartInfo.FileName, process.StartInfo.Arguments, StringExtensions.Tabify(processStdErr));
            }
        }

        public static void CopyAssemblyReferencesFromBuiltProjects(
            IProjectFinder projectFinder, Regex[] assemblyNamePatterns, bool ignoreOnlyMatching, IEnumerable<AssemblyReference> assemblyReferences, bool ignoreMissing)
        {
            // TODO: Refactor this mess, and save for each indirect reference the dependency path that caused it to be included in a unified way

            var originalAssemblyReferenceNames = new HashSet<string>(assemblyReferences.Select(ComparableAssemblyName));

            var remainingReferences = new Queue<AssemblyReference>(assemblyReferences);
            var indirectReferencesOutsideSolution = new List<IndirectReferenceInfo>();

            var ignoredAssemblies = new List<AssemblyReference>();
            var badHintPathAssemblies = new List<AssemblyReference>();
            var missingProjects = new List<AssemblyReference>();
            var unbuiltProjects = new List<Project>();

            while (remainingReferences.Any())
            {
                var assemblyReference = remainingReferences.Dequeue();
                if (false == IncludeAssemblyWhenCopyingDeps(assemblyReference, assemblyNamePatterns, ignoreOnlyMatching))
                {
                    _logger.DebugFormat("Not copying ignored assembly: '{0}'", assemblyReference.ToString());
                    ignoredAssemblies.Add(assemblyReference);
                    continue;
                }

                if (String.IsNullOrWhiteSpace(assemblyReference.HintPath))
                {
                    _logger.DebugFormat("Can't copy dependency (no target path): Missing HintPath for assembly reference: '{0}', used by projects:\n{1}",
                        assemblyReference, ProjectsUsingAssemblyReference(projectFinder, assemblyReference));
                    badHintPathAssemblies.Add(assemblyReference);
                    continue;
                }
                var targetPath = System.IO.Path.GetDirectoryName(assemblyReference.HintPath);

                var buildingProject = projectFinder.FindProjectForAssemblyReference(assemblyReference).SingleOrDefault();
                if (null == buildingProject)
                {
                    _logger.DebugFormat("Can't find dependency (no building project): No project builds assembly reference: '{0}', used by projects:\n{1}",
                        assemblyReference,
                        ProjectsUsingAssemblyReference(projectFinder, assemblyReference));
                    missingProjects.Add(assemblyReference);
                    continue;
                }

                if (ignoreMissing && (false == System.IO.Directory.Exists(buildingProject.GetAbsoluteOutputPath())))
                {
                    _logger.DebugFormat("Ignoring (not copying) all components from not-built project: {0}", buildingProject.ToString());
                    unbuiltProjects.Add(buildingProject);
                    continue;
                }

                var projectOutputs = buildingProject.GetBuiltProjectOutputs().ToArray();
                _logger.InfoFormat("Copy: {0,-40} -> {1}", buildingProject.Name, targetPath);
                CopyFilesToDirectory(projectOutputs, targetPath, ignoreMissing);

                // Add sub-references - the indirectly referenced assemblies, the ones used by the current assemblyReference
                var explicitTargetPath = System.IO.Path.GetDirectoryName(assemblyReference.ExplicitHintPath);
                // TODO: Refactor
                AddIndirectReferences(projectFinder, assemblyNamePatterns, ignoreOnlyMatching, originalAssemblyReferenceNames,
                    remainingReferences, targetPath, buildingProject, projectOutputs, explicitTargetPath, assemblyReference, indirectReferencesOutsideSolution);
            }

            WarnAboutRemainingIndirectReferences(projectFinder, originalAssemblyReferenceNames, indirectReferencesOutsideSolution);
            WarnAboutUncopiedAssemblies(assemblyNamePatterns, ignoreOnlyMatching, ignoredAssemblies, badHintPathAssemblies, missingProjects, unbuiltProjects);
        }

        private static void WarnAboutUncopiedAssemblies(Regex[] assemblyNamePatterns, bool ignoreOnlyMatching, List<AssemblyReference> ignoredAssemblies, List<AssemblyReference> badHintPathAssemblies, List<AssemblyReference> missingProjects, List<Project> unbuiltProjects)
        {
            var messageBuilder = new StringBuilder();
            _logger.DebugFormat("Ignored dependencies: {0}", 
                MessageForNonZeroStat(ignoredAssemblies.Count,
                    String.Format("ignored assemblies ({0} patterns: {1})",
                        ignoreOnlyMatching ? "matched one or more of the" : "did not match any of the",
                        String.Join(", ", assemblyNamePatterns.Select(x => "'" + x.ToString() + "'")))));
            messageBuilder.Append(MessageForNonZeroStat(badHintPathAssemblies.Count, "assemblies with missing or wrong HintPath"));
            messageBuilder.Append(MessageForNonZeroStat(missingProjects.Count, "assemblies from unknown projects"));
            messageBuilder.Append(MessageForNonZeroStat(unbuiltProjects.Count, "assemblies from projects that are not built (could not find outputs)"));
            if (0 < messageBuilder.Length)
            {
                var message = "Dependencies not copied: (see verbose output for more details)\n" + messageBuilder.ToString();
                _logger.Warn(message);
            }
        }

        private static string MessageForNonZeroStat(int value, string msg)
        {
            return (0 == value )
                 ? String.Empty 
                 : String.Format("\t{0} {1}\n", value, msg);
        }

        protected static void WarnAboutRemainingIndirectReferences(IProjectFinder projectFinder, HashSet<string> originalAssemblyReferenceNames,
            List<IndirectReferenceInfo> indirectReferencesOutsideSolution)
        {
            foreach (var indirectReferenceInfo in
                indirectReferencesOutsideSolution
                    .Where(x => false == originalAssemblyReferenceNames.Contains(ComparableAssemblyName(x.IndirectReference))))
            {
                _logger.WarnFormat(@"Skipped indirect reference from solution other than the direct reference that caused it:
    Indirect reference:             {0}
    Indirect reference built by:    {1}
    Required by project:            {2}
    Which builds reference:         {3}
    Which is used directly by projects:
    {4}
",
                    indirectReferenceInfo.IndirectReference,
                    indirectReferenceInfo.IndirectReferenceProject,
                    indirectReferenceInfo.DirectReferenceProject,
                    indirectReferenceInfo.DirectReference,
                    StringExtensions.Tabify(ProjectsUsingAssemblyReference(projectFinder, indirectReferenceInfo.DirectReference)));
            }
        }

        protected static void AddIndirectReferences(IProjectFinder projectFinder, Regex[] assemblyNamePatterns, bool ignoreOnlyMatching,
            HashSet<string> originalAssemblyReferenceNames, Queue<AssemblyReference> remainingReferences, string targetPath,
            Project buildingProject, FileInfo[] projectOutputs, string explicitTargetPath, AssemblyReference assemblyReference,
            List<IndirectReferenceInfo> indirectReferencesOutsideSolution)
        {
            var indirectReferences = GetIndirectReferences(originalAssemblyReferenceNames, targetPath, projectOutputs, explicitTargetPath)
                                            .Where(x => IncludeAssemblyWhenCopyingDeps(x, assemblyNamePatterns, ignoreOnlyMatching))
                                            .ToArray();

            if (false == indirectReferences.Any())
            {
                return;
            }
            var buildingSolution = projectFinder.GetSLNFileForProject(buildingProject);

            _logger.DebugFormat("Adding indirect references due to reference {0} built by project: '{1}'\n{2}",
                assemblyReference, buildingProject, StringExtensions.Tabify(indirectReferences.Select(x => x.ToString())));
            foreach (var indirectReference in indirectReferences)
            {
                var indirectReferenceBuildingProject = projectFinder.FindProjectForAssemblyReference(indirectReference).SingleOrDefault();
                var indirectReferenceInfo = new IndirectReferenceInfo(assemblyReference, buildingProject, indirectReference, indirectReferenceBuildingProject);
                if (null != indirectReferenceBuildingProject)
                {
                    if (projectFinder.GetSLNFileForProject(indirectReferenceBuildingProject).FullName.Equals(buildingSolution.FullName, StringComparison.InvariantCultureIgnoreCase))
                    {
                        originalAssemblyReferenceNames.Add(ComparableAssemblyName(indirectReference));
                        remainingReferences.Enqueue(indirectReference);
                        continue;
                    }
                }
                indirectReferencesOutsideSolution.Add(indirectReferenceInfo);
            }
        }

        protected static AssemblyReference[] GetIndirectReferences(HashSet<string> originalAssemblyReferenceNames, string targetPath, FileInfo[] projectOutputs, string explicitTargetPath)
        {
            return projectOutputs.Select(x => x.FullName) // Get the file name
                                 .Where(IsAssemblyFileName) // filter only assembly files
                                 .Select(AssemblyDefinition.ReadAssembly) // read the assembly
                                 .SelectMany(x => x.Modules.SelectMany(module => module.AssemblyReferences)) // get the references
                // create an AssemblyReference object
                // TODO: a reference doesn't neccesarily have to be a dll file
                                 .Select(subRef => new AssemblyReference(subRef.FullName,
                                                                         System.IO.Path.Combine(targetPath, subRef.Name + ".dll"),
                                                                         System.IO.Path.Combine(explicitTargetPath, subRef.Name + ".dll")))
                                 .Where(x => false == originalAssemblyReferenceNames.Contains(ComparableAssemblyName(x))) // filter out the ones we already have
                                 .ToArray();
        }

        protected static bool IsAssemblyFileName(string fileName)
        {
            var trimmed = fileName.Trim();
            return trimmed.EndsWith(".dll", StringComparison.InvariantCultureIgnoreCase)
                || trimmed.EndsWith(".exe", StringComparison.InvariantCultureIgnoreCase);
        }

        protected static string ComparableAssemblyName(AssemblyReference assemblyReference)
        {
            return assemblyReference.AssemblyNameFromFullName().Trim().ToLowerInvariant();
        }

        protected static string ProjectsUsingAssemblyReference(IProjectFinder projectFinder, AssemblyReference assemblyReference)
        {
            return StringExtensions.Tabify(projectFinder.AllProjectsInPath()
                                                        .Where(x => x.AssemblyReferences
                                                                    .Contains(assemblyReference))
                                                        .Select(x => x.ToString()));
        }

        protected static void CopyFilesToDirectory(IEnumerable<FileInfo> files, string targetPath, bool ignoreMissing)
        {
            foreach (var file in files)
            {
                var source = file.FullName;
                var target = System.IO.Path.Combine(targetPath, file.Name);

                if (ignoreMissing && (false == System.IO.File.Exists(source)))
                {
                    _logger.DebugFormat("copy: ignoring missing file {2}, not copying: {0} -> {1}...", source, target, Path.GetFileName(target));
                }
                _logger.DebugFormat("copy: {2} ({0} -> {1})...", source, target, Path.GetFileName(target));
                System.IO.Directory.CreateDirectory(targetPath);
                System.IO.File.Copy(source, target, true);
            }
        }

    }
}
