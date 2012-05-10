using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BuildDependencyReader.ProjectFileParser;

namespace BuildDependencyReader.PrintProjectDependencies
{
    class Program
    {
        static void Main(string[] args)
        {
            var projectsToTraverse = new Queue<Project>(args.Select(x => Project.FromCSProj(x)));

            while (projectsToTraverse.Any())
            {
                var project = projectsToTraverse.Dequeue();

                Console.WriteLine(project);

                foreach (var referencedAssembly in project.AssemblyReferences)
                {
                    Console.WriteLine("\t" + referencedAssembly.ToString());
                }

                foreach (var subProject in project.ProjectReferences)
                {
                    projectsToTraverse.Enqueue(subProject);
                }
            }
        }
    }
}
