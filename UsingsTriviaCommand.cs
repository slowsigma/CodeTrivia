using EnvDTE;

using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Forms;
using Task = System.Threading.Tasks.Task;

namespace CodeTrivia
{
    /// <summary>
    /// Command handler
    /// </summary>
    internal sealed class UsingsTriviaCommand
    {
        /// <summary>
        /// Command ID.
        /// </summary>
        public const int CommandId = 0x0100;

        /// <summary>
        /// Command menu group (command set GUID).
        /// </summary>
        public static readonly Guid CommandSet = new Guid("55f82d13-0bc3-4ff9-aaf2-1be6dff00131");

        /// <summary>
        /// VS Package that provides this command, not null.
        /// </summary>
        private readonly AsyncPackage package;

        /// <summary>
        /// Initializes a new instance of the <see cref="UsingsTriviaCommand"/> class.
        /// Adds our command handlers for menu (commands must exist in the command table file)
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        /// <param name="commandService">Command service to add command to, not null.</param>
        private UsingsTriviaCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            this.package = package ?? throw new ArgumentNullException(nameof(package));
            commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

            var menuCommandID = new CommandID(CommandSet, CommandId);
            var menuItem = new MenuCommand(this.Execute, menuCommandID);
            commandService.AddCommand(menuItem);
        }

        /// <summary>
        /// Gets the instance of the command.
        /// </summary>
        public static UsingsTriviaCommand Instance
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets the service provider from the owner package.
        /// </summary>
        private Microsoft.VisualStudio.Shell.IAsyncServiceProvider ServiceProvider
        {
            get
            {
                return this.package;
            }
        }

        /// <summary>
        /// Initializes the singleton instance of the command.
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        public static async Task InitializeAsync(AsyncPackage package)
        {
            // Verify the current thread is the UI thread - the call to AddCommand in UsingsTriviaCommand's constructor requires
            // the UI thread.
            ThreadHelper.ThrowIfNotOnUIThread();

            OleMenuCommandService commandService = await package.GetServiceAsync((typeof(IMenuCommandService))) as OleMenuCommandService;
            Instance = new UsingsTriviaCommand(package, commandService);
        }

        /// <summary>
        /// This function is the callback used to execute the command when the menu item is clicked.
        /// See the constructor to see how the menu item is associated with this function using
        /// OleMenuCommandService service and MenuCommand class.
        /// </summary>
        /// <param name="sender">Event sender.</param>
        /// <param name="e">Event args.</param>
        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var compModel = (IComponentModel)Package.GetGlobalService(typeof(SComponentModel));
            var workspace = (Workspace)compModel.GetService<VisualStudioWorkspace>();
            var solution = workspace?.CurrentSolution;
            var stats = new Dictionary<String, Int32>();
            var projCount = (Int32)0;
            var treeCount = (Int32)0;

            void AddStats(String key, Int32 count)
            {
                if (stats.ContainsKey(key))
                    stats[key] += count;
                else
                    stats.Add(key, count);
            }

            if (solution != null)
            {
                foreach (var project in solution.Projects)
                {
                    if (project.SupportsCompilation)
                    {
                        projCount++;
                        var comp = project.GetCompilationAsync();

                        comp.Wait();

                        foreach (var tree in comp.Result.SyntaxTrees)
                        {
                            treeCount++;

                            var model = comp.Result.GetSemanticModel(tree);

                            var nsCounts = tree.GetRoot()
                                                .DescendantNodes()
                                                .Select(node => model.GetSymbolInfo(node))
                                                .Where(chk => chk.Symbol != null
                                                                && !chk.Symbol.Name.StartsWith(".")
                                                                && chk.Symbol.Kind != SymbolKind.Namespace)
                                                .Select(sym => sym.Symbol.ContainingNamespace)
                                                .Where(cns => cns != null)
                                                .GroupBy(y => y.ToString())
                                                .Select(g => (Namespace: g.Key, Count: g.Count()));

                            foreach (var (Namespace, Count) in nsCounts)
                                AddStats(Namespace, Count);
                        }
                    }
                }
            }

            if (projCount > 0)
            {
                Clipboard.Clear();
                Clipboard.SetText(String.Concat($"Total Projects: {projCount}\r\n", $"Tree Count: {treeCount}\r\n", String.Join("\r\n", stats.OrderBy(o => o.Key).Select(s => $"{s.Key} @ {s.Value}"))));
            }

            string message = projCount == 0
                             ? $"No projects were loaded or compatible with 'using ...' counts."
                             : $"Project 'using ...' counts copied to clipboard.";
            string title = "Show Usings";

            // Show a message box to prove we were here
            VsShellUtilities.ShowMessageBox(
                this.package,
                message,
                title,
                OLEMSGICON.OLEMSGICON_INFO,
                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }
    }
}
