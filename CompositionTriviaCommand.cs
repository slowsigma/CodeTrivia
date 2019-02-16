using EnvDTE;

using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

using Microsoft.CodeAnalysis.CSharp;

using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Forms;
using System.Xml.Linq;
using Task = System.Threading.Tasks.Task;

namespace CodeTrivia
{
    /// <summary>
    /// Command handler
    /// </summary>
    internal sealed class CompositionTriviaCommand
    {
        /// <summary>
        /// Command ID.
        /// </summary>
        public const int CommandId = 0x0100;

        /// <summary>
        /// Command menu group (command set GUID).
        /// </summary>
        public static readonly Guid CommandSet = new Guid("853c7e0f-25b5-48ec-9660-ccc098f1243f");

        /// <summary>
        /// VS Package that provides this command, not null.
        /// </summary>
        private readonly AsyncPackage package;

        /// <summary>
        /// Initializes a new instance of the <see cref="CompositionTriviaCommand"/> class.
        /// Adds our command handlers for menu (commands must exist in the command table file)
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        /// <param name="commandService">Command service to add command to, not null.</param>
        private CompositionTriviaCommand(AsyncPackage package, OleMenuCommandService commandService)
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
        public static CompositionTriviaCommand Instance
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
            // Switch to the main thread - the call to AddCommand in CompositionTriviaCommand's constructor requires
            // the UI thread.
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            OleMenuCommandService commandService = await package.GetServiceAsync((typeof(IMenuCommandService))) as OleMenuCommandService;
            Instance = new CompositionTriviaCommand(package, commandService);
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

            var compModel   = (IComponentModel)Package.GetGlobalService(typeof(SComponentModel));
            var workspace   = (Workspace)compModel.GetService<VisualStudioWorkspace>();
            var solution    = workspace?.CurrentSolution;

            var solutionElement = (XElement)null;

            var projCount   = (Int32)0;
            var treeCount   = (Int32)0;

            //XAttribute Attribute(String name, String value)
            //    => String.IsNullOrWhiteSpace(value)
            //       ? null
            //       : new XAttribute(name, value);

            String LogicalAssembly(String assembly)
                =>   assembly.Equals("mscorelib")   ? "Microsoft.Net"
                   : assembly.Equals("netstandard") ? "Microsoft.Net"
                   :                                  assembly;

            XElement DeclaredElement(String id, String name, String kind, String access, XElement fileElement)
                => new XElement(kind,
                                new XAttribute("id", id),
                                new XAttribute("Name", name),
                                new XAttribute("Kind", kind),
                                new XAttribute("Access", access),
                                fileElement);

            XElement ReferenceElement(String id, String @namespace, String name, String assembly)
                => new XElement("Reference",
                                new XAttribute("id", id),
                                new XAttribute("Assembly", LogicalAssembly(assembly)),
                                new XAttribute("Namespace", @namespace),
                                new XAttribute("Type", name));

            String TypeName(ISymbol symbol)
                => symbol.ContainingType == null
                   ? symbol.Name
                   : $"{TypeName(symbol.ContainingType)}.{symbol.Name}";

            void AggregateNamedType(XElement container, String containerId, HashSet<String> references, INamedTypeSymbol namedType)
            {
                if (namedType.Name != String.Empty)
                {
                    var id = $"{namedType.ContainingNamespace}.{TypeName(namedType)}";

                    if (!id.StartsWith(containerId) && !references.Contains(id))
                    {
                        container.Add(ReferenceElement(id, namedType.ContainingNamespace.ToString(), namedType.Name, namedType.ContainingAssembly?.Name));
                        references.Add(id);
                    }
                }

                if (namedType.IsTupleType && namedType.TupleUnderlyingType != null)
                {
                    AggregateNamedType(container, containerId, references, namedType.TupleUnderlyingType);

                    foreach (var element in namedType.TupleElements)
                        AggregateReference(container, containerId, references, element);
                }
                else if (namedType.IsGenericType)
                {
                    foreach (var typeArg in namedType.TypeArguments)
                        AggregateReference(container, containerId, references, typeArg);

                    foreach (var typeParam in namedType.TypeParameters)
                        AggregateReference(container, containerId, references, typeParam);
                }
            }

            void AggregateArrayType(XElement container, String containerId, HashSet<String> references, IArrayTypeSymbol arrayType)
            {
                if (arrayType.ContainingNamespace != null)
                {
                    var id = $"{arrayType.ContainingNamespace}.{arrayType.Name}"; // should always be "System.Array"

                    if (arrayType.ContainingNamespace.Name == null)
                        throw new Exception("");

                    if (!references.Contains(id))
                    {
                        container.Add(ReferenceElement(id, arrayType.ContainingNamespace.Name, arrayType.Name, arrayType.ContainingAssembly?.Name));
                        references.Add(id);
                    }
                }

                if (arrayType.ElementType is ISymbol symbol)
                    AggregateReference(container, containerId, references, symbol);
            }

            void AggregateReference(XElement container, String containerId, HashSet<String> references, ISymbol reference)
            {
                if (reference is IArrayTypeSymbol arrayTypeSymbol)
                    AggregateArrayType(container, containerId, references, arrayTypeSymbol);
                else if (reference is INamedTypeSymbol namedTypeSymbol)
                    AggregateNamedType(container, containerId, references, namedTypeSymbol);
            }

            void Aggregate(XElement container,
                           XElement fileElement,
                           Dictionary<String, XElement> types,
                           Dictionary<String, HashSet<String>> typeReferences,
                           String declaredId,
                           SyntaxNode node,
                           SemanticModel model)
            {
                if (node.RawKind == (Int32)SyntaxKind.UsingDirective)
                    return;

                var declaration = model.GetDeclaredSymbol(node);
                var reference   = model.GetSymbolInfo(node).Symbol;

                if (   declaration != null
                    && declaration.Kind == SymbolKind.NamedType
                    && !String.IsNullOrWhiteSpace(declaration.Name))
                {
                    // is declaration, set the id, make new element and references set, and traverse
                    declaredId = $"{declaration.ContainingNamespace}.{TypeName(declaration)}";               // used to be incorrect: declaration.ToString();

                    var declareElement  = types.ContainsKey(declaredId)
                                          ? types[declaredId]
                                          : DeclaredElement(declaredId,
                                                            TypeName(declaration),
                                                            declaration.Kind.ToString(),
                                                            declaration.DeclaredAccessibility.ToString(),
                                                            fileElement);

                    if (!types.ContainsKey(declaredId))
                    {
                        types.Add(declaredId, declareElement);

                        container.Add(declareElement);
                        typeReferences.Add(declaredId, new HashSet<string>());

                        AggregateReference(declareElement, declaredId, typeReferences[declaredId], declaration);
                    }
                    else
                    {
                        declareElement.Add(fileElement);
                    }

                    foreach (var child in node.ChildNodes())
                        Aggregate(declareElement, fileElement, types, typeReferences, declaredId, child, model);
                }
                else if (   reference != null
                         && !reference.Name.StartsWith(".") &&
                         reference.Kind != SymbolKind.Namespace)
                {
                    if (declaredId.Length > 0)
                    {
                        var references = typeReferences[declaredId];

                        AggregateReference(container, declaredId, references, reference);
                    }

                    foreach (var child in node.ChildNodes())
                        Aggregate(container, fileElement, types, typeReferences, declaredId, child, model);
                }
                else
                {
                    // some intermediate node, traverse down
                    foreach (var child in node.ChildNodes())
                        Aggregate(container, fileElement, types, typeReferences, declaredId, child, model);
                }
            };

            if (solution != null)
            {
                solutionElement = new XElement("Solution", new XAttribute("FilePath", solution.FilePath));

                foreach (var project in solution.Projects.Where(p => p.SupportsCompilation))
                {
                    projCount++;

                    var projectElement = new XElement("Project", new XAttribute("Name", project.Name), new XAttribute("Assembly", project.AssemblyName), new XAttribute("FilePath", project.FilePath));
                    var typeReferences = new Dictionary<String, HashSet<String>>();

                    solutionElement.Add(projectElement);

                    var getComp = project.GetCompilationAsync()
                                         .AsWaitFor();
                    var comp    = getComp();

                    var types   = new Dictionary<String, XElement>();

                    foreach (var tree in comp.SyntaxTrees)
                    {
                        treeCount++;

                        var model       = comp.GetSemanticModel(tree, true);
                        var fileElement = new XElement("File", new XAttribute("FilePath", tree.FilePath));
                        var root        = tree.GetRoot();

                        if (root.Language == "C#")
                            Aggregate(projectElement, fileElement, types, typeReferences, String.Empty, root, model);
                    }
                }
            }

            if (treeCount > 0)
            {
                Clipboard.Clear();
                Clipboard.SetText(solutionElement.ToString());
            }

            string message = projCount == 0
                             ? $"No compatible projects were loaded."
                             : $"Composition data copied to clipboard.";
            string title = "Show Composition";

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
