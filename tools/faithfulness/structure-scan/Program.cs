// The structure of the C# that cites original functions, for tools/faithfulness/structure.py.
//
// Every `// orig:` comment (docs/TRACEABILITY.md) is attached to the syntax node it stands above. A node
// that is a member (method, constructor, property with code, indexer, operator, type), a local function or
// an expression holding a lambda (a card option's handler) is a *unit*: it implements the original
// functions its comment cites. For each unit this tool lists, in source order:
//
//   calls     every reference to another member of src/ (calls, property reads and writes, object
//             creation): a member whose declaration is a unit stands for the coordinates it cites; a
//             member without a trace is followed into (an inlined helper of the port, up to a few levels);
//             with the constant arguments of the call (or the constant a property is set to), and whether
//             it is reached through a property (data access: the original reads memory there)
//   mentions  every coordinate any comment inside the unit (or a followed helper) names, including
//             statement-level `// orig:` lines (code the port inlined)
//   consts    the constant operands of comparisons, `case` labels and bit masks (as 16-bit words)
//   stores    the constants assigned to fields, properties and elements
//   literals  every integer constant the unit (or a followed helper) uses, as a set
//   decisions the number of decision points (if, ?:, case/arm, &&, ||, ??, loops)
//
// Calls and stores in a statement under a `// DIVERGENCE:` or `// DORMANT:` comment are marked documented:
// the difference is already registered.
//
// Units nested in a unit (handlers) are left out of its lists. Coordinates are as written; thunks are
// resolved by structure.py. All of src/ is bound as one compilation; the few references it cannot bind
// (package types of the hosts) are counted, and STRUCTURE_SCAN_ERRORS=<file> lists them.
//
//   dotnet run -c Release --project tools/faithfulness/structure-scan -- <repository root> <out.json>
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace StructureScan;

internal static partial class Program
{
    [GeneratedRegex(@"\bR\d{3}\+0x[0-9A-Fa-f]{1,5}\b|\broot [0-9A-Fa-f]{4}:[0-9A-Fa-f]{4}\b|\bthunk 09C0:[0-9A-Fa-f]{4}\b")]
    private static partial Regex Coordinate();

    [GeneratedRegex(@"^//\s*(DIVERGENCE|DORMANT)")]
    private static partial Regex DocumentedLine();

    [GeneratedRegex(@"^//\s*orig:")]
    private static partial Regex OrigLine();

    [GeneratedRegex(@"^//\s*(orig:|DORMANT|DIVERGENCE|UNCERTAIN:|C:|BUG:)")]
    private static partial Regex MarkerLine();

    private const int FollowDepth = 4;

    private static readonly Dictionary<SyntaxNode, Unit> Units = [];
    private static readonly Dictionary<SyntaxTree, SemanticModel> Models = [];
    // interface, abstract and virtual members -> their implementations and overrides in src/
    private static readonly Dictionary<ISymbol, List<ISymbol>> Implementations = new(SymbolEqualityComparer.Default);
    private static string root = "";

    private static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("usage: structure-scan <repository root> <out.json>");
            return 2;
        }
        root = Path.GetFullPath(args[0]);
        var options = new CSharpParseOptions(LanguageVersion.Preview);
        var trees = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Order(StringComparer.Ordinal)
            .Select(f => CSharpSyntaxTree.ParseText(File.ReadAllText(f), options, f))
            .ToList();
        trees.Add(CSharpSyntaxTree.ParseText(
            "global using System; global using System.Collections.Generic; global using System.IO; global using System.Linq;"
            + " global using System.Threading; global using System.Threading.Tasks;", options, "GlobalUsings.cs"));
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Select(p => MetadataReference.CreateFromFile(p));
        var compilation = CSharpCompilation.Create("scan", trees, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        foreach (var tree in trees)
        {
            Models[tree] = compilation.GetSemanticModel(tree);
        }
        // references the scan cannot bind (package types of the hosts) leave those calls out
        var unbound = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Console.WriteLine($"{unbound.Count} binding errors");
        if (Environment.GetEnvironmentVariable("STRUCTURE_SCAN_ERRORS") is { } errorFile)
        {
            File.WriteAllLines(errorFile, unbound.Select(d => d.ToString()));
        }
        foreach (var tree in trees)
        {
            FindUnits(tree);
            FindImplementations(tree);
        }
        var output = Units.Values.OrderBy(u => u.File, StringComparer.Ordinal).ThenBy(u => u.Line).Select(Describe).ToList();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
        File.WriteAllText(args[1], JsonSerializer.Serialize(output));
        Console.WriteLine($"{output.Count} units -> {args[1]}");
        return 0;
    }

    private sealed record Unit(SyntaxNode Node, string File, int Line, string Kind, string Member, List<string> Cites);

    private static void FindUnits(SyntaxTree tree)
    {
        var rootNode = tree.GetRoot();
        foreach (var token in rootNode.DescendantTokens())
        {
            var cites = OrigCites(token.LeadingTrivia);
            if (cites.Count == 0)
            {
                continue;
            }
            var node = token.Parent!;
            while (node.Parent is not null && node.Parent.GetFirstToken() == token && node.Parent is not CompilationUnitSyntax)
            {
                node = node.Parent;
            }
            var kind = KindOf(node);
            if (kind is null)
            {
                continue;
            }
            if (Units.TryGetValue(node, out var existing))
            {
                existing.Cites.AddRange(cites.Where(c => !existing.Cites.Contains(c)));
                continue;
            }
            Units[node] = new Unit(node, Path.GetRelativePath(root, tree.FilePath).Replace('\\', '/'),
                node.GetLocation().GetLineSpan().StartLinePosition.Line + 1, kind, MemberName(node), cites);
        }
    }

    // the coordinates of the `// orig:` lines in a comment block (with their continuation lines)
    private static List<string> OrigCites(SyntaxTriviaList trivia)
    {
        var cites = new List<string>();
        var inOrig = false;
        foreach (var t in trivia)
        {
            if (!t.IsKind(SyntaxKind.SingleLineCommentTrivia))
            {
                continue;
            }
            var text = t.ToString();
            if (MarkerLine().IsMatch(text))
            {
                // a marker line can carry the trace too: "// DORMANT: line 9 is disabled. orig: R058+0x0874 ..."
                inOrig = OrigLine().IsMatch(text) || text.Contains(" orig: ", StringComparison.Ordinal);
            }
            if (inOrig)
            {
                cites.AddRange(Coordinate().Matches(text).Select(m => m.Value).Where(c => !cites.Contains(c)));
            }
        }
        return cites;
    }

    private static string? KindOf(SyntaxNode node) => node switch
    {
        BaseTypeDeclarationSyntax => "type",
        // an auto-property is data: its comment cites the code that reads and writes it, not an implementation
        BasePropertyDeclarationSyntax p when p.AccessorList?.Accessors.All(a => a.Body is null && a.ExpressionBody is null) == true
            && p is not PropertyDeclarationSyntax { ExpressionBody: not null } => null,
        BaseMethodDeclarationSyntax or BasePropertyDeclarationSyntax or AccessorDeclarationSyntax => "member",
        LocalFunctionStatementSyntax => "local",
        FieldDeclarationSyntax f when f.DescendantNodes().Any(IsFunction) => "handler",
        ArgumentSyntax or ExpressionSyntax or ExpressionElementSyntax or SwitchExpressionArmSyntax
            when node.DescendantNodesAndSelf().Any(IsFunction) => "handler",
        _ => null,
    };

    private static bool IsFunction(SyntaxNode n) => n is AnonymousFunctionExpressionSyntax;

    private static string MemberName(SyntaxNode node)
    {
        var parts = new List<string>();
        for (var n = node; n is not null; n = n.Parent)
        {
            switch (n)
            {
                case BaseTypeDeclarationSyntax t: parts.Add(t.Identifier.Text); break;
                case MethodDeclarationSyntax m: parts.Add(m.Identifier.Text + "()"); break;
                case ConstructorDeclarationSyntax: parts.Add(".ctor"); break;
                case PropertyDeclarationSyntax p: parts.Add(p.Identifier.Text); break;
                case LocalFunctionStatementSyntax l: parts.Add(l.Identifier.Text + "()"); break;
                case BaseNamespaceDeclarationSyntax ns: parts.Add(ns.Name.ToString()); break;
            }
        }
        parts.Reverse();
        return string.Join('.', parts).Replace(".(", "(", StringComparison.Ordinal);
    }

    private static object Describe(Unit u)
    {
        var calls = new List<object[]>();
        var mentions = new List<string>();
        var stack = new HashSet<SyntaxNode> { u.Node };
        var literals = new HashSet<int>();
        Walk(u.Node, u.Node, calls, mentions, literals, stack, 0, null);
        var consts = new List<int>();
        var stores = new List<int>();
        var decisions = 0;
        foreach (var n in Own(u.Node, u.Node))
        {
            if (n is AssignmentExpressionSyntax assign && Word(Models[n.SyntaxTree], assign.Right) is { } stored
                && !Documented(n, u.Node))
            {
                stores.Add(stored);
            }
            decisions += n switch
            {
                IfStatementSyntax or ConditionalExpressionSyntax or WhileStatementSyntax or DoStatementSyntax
                    or ForStatementSyntax or ForEachStatementSyntax or CaseSwitchLabelSyntax or CasePatternSwitchLabelSyntax
                    or SwitchExpressionArmSyntax => 1,
                BinaryExpressionSyntax b when b.IsKind(SyntaxKind.LogicalAndExpression) || b.IsKind(SyntaxKind.LogicalOrExpression)
                    || b.IsKind(SyntaxKind.CoalesceExpression) => 1,
                _ => 0,
            };
            foreach (var e in ConstOperands(n))
            {
                var v = Models[e.SyntaxTree].GetConstantValue(e);
                if (v.HasValue && v.Value is not null and not bool and not string && TryWord(v.Value, out var w))
                {
                    consts.Add(w);
                }
            }
        }
        return new
        {
            file = u.File,
            line = u.Line,
            kind = u.Kind,
            member = u.Member,
            cites = u.Cites,
            calls,
            mentions = mentions.Distinct().ToList(),
            consts,
            stores,
            literals = literals.Order().ToList(),
            decisions,
        };
    }

    private static IEnumerable<ExpressionSyntax> ConstOperands(SyntaxNode n)
    {
        switch (n)
        {
            case BinaryExpressionSyntax b when b.Kind() is SyntaxKind.EqualsExpression or SyntaxKind.NotEqualsExpression
                or SyntaxKind.LessThanExpression or SyntaxKind.LessThanOrEqualExpression or SyntaxKind.GreaterThanExpression
                or SyntaxKind.GreaterThanOrEqualExpression or SyntaxKind.BitwiseAndExpression:
                yield return b.Left;
                yield return b.Right;
                break;
            case CaseSwitchLabelSyntax c:
                yield return c.Value;
                break;
            case ConstantPatternSyntax p:
                yield return p.Expression;
                break;
            case RelationalPatternSyntax r:
                yield return r.Expression;
                break;
        }
    }

    private static bool TryWord(object v, out int w)
    {
        try
        {
            w = (int)(Convert.ToInt64(v, System.Globalization.CultureInfo.InvariantCulture) & 0xFFFF);
            return true;
        }
        catch (Exception e) when (e is InvalidCastException or OverflowException or FormatException)
        {
            w = 0;
            return false;
        }
    }

    // the nodes of a unit, without the units nested in it
    private static IEnumerable<SyntaxNode> Own(SyntaxNode unit, SyntaxNode top) =>
        unit.DescendantNodes(n => n == top || !Units.ContainsKey(n));

    private static void Walk(SyntaxNode node, SyntaxNode top, List<object[]> calls, List<string> mentions,
        HashSet<int> literals, HashSet<SyntaxNode> stack, int depth, string? via, bool documentedHere = false,
        bool viaProperty = false)
    {
        foreach (var trivia in node.DescendantTrivia(n => n == top || !Units.ContainsKey(n)))
        {
            if (trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) || trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
                || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia))
            {
                mentions.AddRange(Coordinate().Matches(trivia.ToString()).Select(m => m.Value));
            }
        }
        var model = Models[node.SyntaxTree];
        foreach (var n in Own(node, top))
        {
            if (Units.ContainsKey(n) && n != top)
            {
                continue;
            }
            if (n is LiteralExpressionSyntax or IdentifierNameSyntax or MemberAccessExpressionSyntax
                && Word(model, (ExpressionSyntax)n) is { } literal)
            {
                literals.Add(literal);
                if (HighWord(model, (ExpressionSyntax)n) is { } high)
                {
                    literals.Add(high);
                }
            }
            ISymbol? symbol = n switch
            {
                SimpleNameSyntax name when name.Parent is not MemberAccessExpressionSyntax { Name: var right } || right == name
                    => model.GetSymbolInfo(name).Symbol,
                BaseObjectCreationExpressionSyntax create => model.GetSymbolInfo(create).Symbol,
                // a user-defined operator (Money's comparisons, say) is a call of the member that declares it
                BinaryExpressionSyntax or PrefixUnaryExpressionSyntax or PostfixUnaryExpressionSyntax
                    or AssignmentExpressionSyntax => model.GetSymbolInfo(n).Symbol is IMethodSymbol
                    { MethodKind: MethodKind.UserDefinedOperator } op ? op : null,
                _ => null,
            };
            if (symbol is not (IMethodSymbol or IPropertySymbol))
            {
                continue;
            }
            symbol = symbol is IMethodSymbol { ReducedFrom: { } reduced } ? reduced : symbol.OriginalDefinition;
            var line = n.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            if (symbol is IMethodSymbol { ContainingType: { Name: "Math", ContainingNamespace.Name: "System" } } math)
            {
                // the run-time library's min, max and clamp, which the port writes for the original's own helpers
                calls.Add([line, "bcl:Math." + math.Name, via ?? "", math.Name, ConstArgs(n, model), documentedHere || Documented(n, top), viaProperty]);
                continue;
            }
            var targets = new List<ISymbol> { symbol };
            if (!symbol.DeclaringSyntaxReferences.Any(r => UnitOf(r.GetSyntax()) is not null)
                && Implementations.TryGetValue(symbol, out var impls))
            {
                // a call through an interface or a virtual member: what its implementations in src/ cite
                // (a member every implementation shares; the scripted test doubles live in tests/, not here)
                targets = impls;
            }
            var args = ConstArgs(n, model);
            var documented = documentedHere || Documented(n, top);
            foreach (var target in targets)
            {
                Reference(target, line, args, documented, viaProperty || symbol is IPropertySymbol, calls, mentions, literals, stack, depth, via);
            }
        }
    }

    // the constant words of a call's arguments (null: computed), or of the value a property is set to
    private static List<int?> ConstArgs(SyntaxNode name, SemanticModel model)
    {
        var expr = name.Parent is MemberAccessExpressionSyntax access && access.Name == name ? access : name;
        IEnumerable<ExpressionSyntax> values = expr switch
        {
            BaseObjectCreationExpressionSyntax create => create.ArgumentList?.Arguments.Select(a => a.Expression) ?? [],
            _ when expr.Parent is InvocationExpressionSyntax call && call.Expression == expr
                => call.ArgumentList.Arguments.Select(a => a.Expression),
            _ when expr.Parent is AssignmentExpressionSyntax set && set.Left == expr => [set.Right],
            _ => [],
        };
        // a 32-bit constant is two words in the original (low word first)
        return values.SelectMany(v => HighWord(model, v) is { } high ? [Word(model, v), high] : new[] { Word(model, v) }).ToList();
    }

    private static int? Word(SemanticModel model, ExpressionSyntax e)
    {
        var v = Constant(model, e);
        return v.HasValue && v.Value is not null and not string && TryWord(v.Value is bool b ? (b ? 1 : 0) : v.Value, out var w) ? w : null;
    }

    // a compile-time constant, or the constant a `static readonly` field is initialised with
    // (`static readonly int Riding = (int)Skill.Riding`)
    private static Optional<object?> Constant(SemanticModel model, ExpressionSyntax e)
    {
        var v = model.GetConstantValue(e);
        if (v.HasValue || e is not (IdentifierNameSyntax or MemberAccessExpressionSyntax)
            || model.GetSymbolInfo(e).Symbol is not IFieldSymbol { IsStatic: true, IsReadOnly: true } field)
        {
            return v;
        }
        return field.DeclaringSyntaxReferences.Select(r => r.GetSyntax()).OfType<VariableDeclaratorSyntax>()
            .FirstOrDefault()?.Initializer?.Value is { } init && Models.TryGetValue(init.SyntaxTree, out var m)
            ? m.GetConstantValue(init) : v;
    }

    // the high word of a constant above 0xFFFF (a long flag mask, a 32-bit count)
    private static int? HighWord(SemanticModel model, ExpressionSyntax e)
    {
        var v = Constant(model, e);
        if (!v.HasValue || v.Value is null or string or bool or char or float or double or decimal)
        {
            return null;
        }
        var x = Convert.ToDecimal(v.Value, System.Globalization.CultureInfo.InvariantCulture);
        return x > 0xFFFF && x <= uint.MaxValue ? (int)(((ulong)x >> 16) & 0xFFFF) : null;
    }

    private static void Reference(ISymbol symbol, int line, List<int?> args, bool documented, bool viaProperty, List<object[]> calls,
        List<string> mentions, HashSet<int> literals, HashSet<SyntaxNode> stack, int depth, string? via)
    {
        foreach (var decl in symbol.DeclaringSyntaxReferences.Select(r => r.GetSyntax()))
        {
            var unitNode = UnitOf(decl);
            if (unitNode is not null)
            {
                foreach (var c in Units[unitNode].Cites)
                {
                    calls.Add([line, c, via ?? "", symbol.Name, args, documented, viaProperty]);
                }
                break;
            }
            // an untraced helper: followed at each use (its calls and draws count every time), not into itself
            // (a type declaration is a record's implicit member: nothing to follow)
            if (depth < FollowDepth && decl is not BaseTypeDeclarationSyntax && Models.ContainsKey(decl.SyntaxTree) && stack.Add(decl))
            {
                Walk(decl, decl, calls, mentions, literals, stack, depth + 1, via ?? symbol.Name, documented, viaProperty);
                stack.Remove(decl);
            }
        }
    }

    private static void FindImplementations(SyntaxTree tree)
    {
        var model = Models[tree];
        foreach (var decl in tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            if (model.GetDeclaredSymbol(decl) is not INamedTypeSymbol type)
            {
                continue;
            }
            foreach (var member in type.GetMembers())
            {
                var overridden = member switch
                {
                    IMethodSymbol m => m.OverriddenMethod as ISymbol,
                    IPropertySymbol p => p.OverriddenProperty,
                    _ => null,
                };
                if (overridden is not null)
                {
                    Add(overridden.OriginalDefinition, member);
                }
            }
            foreach (var iface in type.AllInterfaces)
            {
                foreach (var member in iface.GetMembers())
                {
                    if (type.FindImplementationForInterfaceMember(member) is { } impl
                        && SymbolEqualityComparer.Default.Equals(impl.ContainingType, type))
                    {
                        Add(member.OriginalDefinition, impl);
                    }
                }
            }
        }

        static void Add(ISymbol member, ISymbol impl)
        {
            if (!Implementations.TryGetValue(member, out var list))
            {
                Implementations[member] = list = [];
            }
            if (!list.Contains(impl, SymbolEqualityComparer.Default))
            {
                list.Add(impl);
            }
        }
    }

    // a node inside a statement (of the unit) that a DIVERGENCE or DORMANT comment stands above
    private static bool Documented(SyntaxNode n, SyntaxNode top)
    {
        for (var x = n; x is not null && x != top; x = x.Parent)
        {
            if (x is StatementSyntax or SwitchSectionSyntax or ArgumentSyntax or ExpressionElementSyntax or SwitchExpressionArmSyntax
                && x.GetLeadingTrivia().Any(t => t.IsKind(SyntaxKind.SingleLineCommentTrivia) && DocumentedLine().IsMatch(t.ToString())))
            {
                return true;
            }
        }
        return false;
    }

    // the unit a declaration is (a member, or an accessor or local function whose comment is on it)
    // (a type's comment cites what the type covers, not what its implicit constructor does: no unit)
    private static SyntaxNode? UnitOf(SyntaxNode decl)
    {
        if (Units.TryGetValue(decl, out var unit) && unit.Kind != "type")
        {
            return decl;
        }
        if (decl is VariableDeclaratorSyntax { Parent.Parent: FieldDeclarationSyntax field } && Units.ContainsKey(field))
        {
            return field;
        }
        return null;
    }
}
