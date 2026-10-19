// The structure of the C# that cites original functions, for tools/faithfulness/structure.py.
//
// Every `// orig:` comment (docs/TRACEABILITY.md) is attached to the syntax node it stands above (a card script's
// untraced `Run` stands for the first trace line of its class: the card function). A node
// that is a member (method, constructor, property with code, indexer, operator, type), a local function or
// an expression holding a lambda (a card option's handler) is a *unit*: it implements the original
// functions its comment cites. For each unit this tool lists, in source order:
//
//   calls     every reference to another member of src/ (calls, property reads and writes, object
//             creation): a member whose declaration is a unit stands for the coordinates it cites; a
//             member without a trace is followed into (an inlined helper of the port, up to a few levels);
//             with the constant arguments of the call (or the constant a property is set to), whether
//             it is reached through a property (data access: the original reads memory there), and its
//             context (0, or the lambda body it is in: code that runs when the lambda is invoked); listed
//             in evaluation order (a call after its receiver and arguments)
//   mentions  every coordinate any comment inside the unit (or a followed helper) names, including
//             statement-level `// orig:` lines (code the port inlined)
//   installs  what the units nested in it cite (the handlers a card passes its menu: its option actions)
//   consts    the constant operands of comparisons, `case` labels and bit masks (as 16-bit words)
//   stores    the constants assigned to fields, properties and elements (not to locals and parameters)
//   literals  every integer constant the unit (or a followed helper) uses, as a set
//   decisions the number of decision points (if, ?:, case/arm, &&, ||, ??, loops), with those of the followed helpers
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

    // the words before a coordinate that make it the site of code the member shares or the source of its data, not a
    // function it implements: "the common tail of R120+0x0C88 / _0388", "repeated inline in R126+0x1B08", "DS:DBBB, filled
    // by R007+0x0188" (an example, "e.g. R059+0x0CEC", stays a cite: a shared handler stands for every card's copy)
    [GeneratedRegex(@"\b(tail of|repeated inline in|repeated in|filled by)\b")]
    private static partial Regex PartPhrase();

    // the coordinates a unit cites as the sites of a shared part (a call of the unit is followed into, not a call of them)
    private static readonly Dictionary<SyntaxNode, HashSet<string>> Parts = [];

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
        CardEntries();
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
            var cites = OrigCites(token.LeadingTrivia, out var parts);
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
            if (!Parts.TryGetValue(node, out var known))
            {
                Parts[node] = known = [];
            }
            known.UnionWith(parts);
            if (Units.TryGetValue(node, out var existing))
            {
                existing.Cites.AddRange(cites.Where(c => !existing.Cites.Contains(c)));
                continue;
            }
            Units[node] = new Unit(node, Path.GetRelativePath(root, tree.FilePath).Replace('\\', '/'),
                node.GetLocation().GetLineSpan().StartLinePosition.Line + 1, kind, MemberName(node), cites);
        }
    }

    // A card script traces its card function on the class (`// orig: R064+0x2D38 (1B0B:0008) matched`); an untraced
    // `Run` override of the class is that function's code: it stands for the coordinates of the class's first trace line.
    private static void CardEntries()
    {
        foreach (var type in Units.Values.Where(u => u.Kind == "type").ToList())
        {
            if (type.Node is not ClassDeclarationSyntax cls
                || cls.Members.OfType<MethodDeclarationSyntax>().FirstOrDefault(m => m.Identifier.Text == "Run"
                    && m.Modifiers.Any(SyntaxKind.OverrideKeyword)) is not { } run
                || Units.ContainsKey(run))
            {
                continue;
            }
            var first = cls.GetLeadingTrivia()
                .FirstOrDefault(t => t.IsKind(SyntaxKind.SingleLineCommentTrivia) && OrigLine().IsMatch(t.ToString()));
            var cites = Coordinate().Matches(first.ToString()).Select(m => m.Value).Distinct().ToList();
            if (cites.Count > 0)
            {
                var line = run.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                Units[run] = new Unit(run, type.File, line, "member", MemberName(run), cites);
            }
        }
    }

    // the coordinates of the `// orig:` lines in a comment block (with their continuation lines)
    private static List<string> OrigCites(SyntaxTriviaList trivia, out HashSet<string> parts)
    {
        var cites = new List<string>();
        parts = [];
        var inOrig = false;
        var inPart = false;
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
                inPart = false;
            }
            if (inOrig)
            {
                cites.AddRange(Coordinate().Matches(text).Select(m => m.Value).Where(c => !cites.Contains(c)));
                // (the phrase holds to the end of the orig line's block: its continuation lines list more sites)
                var phrase = PartPhrase().Match(text);
                var from = inPart ? -1 : phrase.Success ? phrase.Index : int.MaxValue;
                parts.UnionWith(Coordinate().Matches(text).Where(m => m.Index > from).Select(m => m.Value));
                inPart |= phrase.Success;
            }
        }
        return cites;
    }

    private static string? KindOf(SyntaxNode node) => node switch
    {
        BaseTypeDeclarationSyntax => "type",
        // a delegate the host supplies (`Func<int> LoreLevel { get; init; }`) stands for the function it supplies
        PropertyDeclarationSyntax { Type: GenericNameSyntax { Identifier.Text: "Func" or "Action" } } => "member",
        PropertyDeclarationSyntax { Type: IdentifierNameSyntax { Identifier.Text: "Action" } } => "member",
        // an auto-property is data: its comment cites the code that reads and writes it, not an implementation
        BasePropertyDeclarationSyntax p when p.AccessorList?.Accessors.All(a => a.Body is null && a.ExpressionBody is null) == true
            && p is not PropertyDeclarationSyntax { ExpressionBody: not null } => null,
        BaseMethodDeclarationSyntax or BasePropertyDeclarationSyntax or AccessorDeclarationSyntax => "member",
        LocalFunctionStatementSyntax => "local",
        FieldDeclarationSyntax f when f.DescendantNodes().Any(IsFunction) => "handler",
        // a handler kept in a local (`var noLine = new MenuLine(_ => ...)`) under its trace
        LocalDeclarationStatementSyntax l when l.DescendantNodes().Any(IsFunction) => "handler",
        ArgumentSyntax or ExpressionSyntax or ExpressionElementSyntax or SwitchExpressionArmSyntax
            when node.DescendantNodesAndSelf().Any(IsFunction) => "handler",
        // a menu line a helper builds (`Passage(c, 0)` under its trace): the handler is in the helper
        ArgumentSyntax { Expression: InvocationExpressionSyntax } => "handler",
        ExpressionElementSyntax { Expression: InvocationExpressionSyntax } => "handler",
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
        Contexts.Clear();
        FollowedDecisions = 0;
        Walk(u.Node, u.Node, calls, mentions, literals, stack, 0, null);
        var consts = new List<int>();
        var stores = new List<int>();
        var decisions = 0;
        var installs = new List<string>();
        foreach (var n in Own(u.Node, u.Node))
        {
            if (n != u.Node && Units.TryGetValue(n, out var nested))
            {
                installs.AddRange(nested.Cites);
            }
            // (a local or parameter is the port's own working value, not the game's state)
            if (n is AssignmentExpressionSyntax assign && Word(Models[n.SyntaxTree], assign.Right) is { } stored
                && Models[n.SyntaxTree].GetSymbolInfo(assign.Left).Symbol is not (ILocalSymbol or IParameterSymbol)
                && !Documented(n, u.Node))
            {
                stores.Add(stored);
            }
            decisions += DecisionOf(n);
            foreach (var e in ConstOperands(n))
            {
                var v = Models[e.SyntaxTree].GetConstantValue(e);
                if (v.HasValue && v.Value is not null and not bool and not string && TryWord(v.Value, out var w))
                {
                    consts.Add(w);
                }
            }
        }
        // (and those of the untraced helpers it follows, at each use, as their calls)
        decisions += FollowedDecisions;
        // (the sites of a part the unit shares are named, not implemented, beside a function it does implement)
        var parts = Parts.TryGetValue(u.Node, out var p) ? p : [];
        // (a unit that only shares code of several sites is compared with the first: comparing it with all of them would
        // make one comparison of functions that share a statement)
        var own = u.Cites.Any(c => !parts.Contains(c))
            ? u.Cites.Where(c => !parts.Contains(c)).ToList()
            : u.Cites.Take(1).ToList();
        mentions.AddRange(u.Cites.Except(own));
        return new
        {
            file = u.File,
            line = u.Line,
            kind = u.Kind,
            member = u.Member,
            cites = own,
            calls,
            mentions = mentions.Distinct().ToList(),
            installs = installs.Distinct().ToList(),
            consts,
            stores,
            literals = literals.Order().ToList(),
            decisions,
        };
    }

    private static int FollowedDecisions;

    // a decision point: if, ?:, a loop, a case or arm, &&, ||, ??
    private static int DecisionOf(SyntaxNode n) => n switch
    {
        IfStatementSyntax or ConditionalExpressionSyntax or WhileStatementSyntax or DoStatementSyntax
            or ForStatementSyntax or ForEachStatementSyntax or CaseSwitchLabelSyntax or CasePatternSwitchLabelSyntax
            or SwitchExpressionArmSyntax => 1,
        BinaryExpressionSyntax b when b.IsKind(SyntaxKind.LogicalAndExpression) || b.IsKind(SyntaxKind.LogicalOrExpression)
            || b.IsKind(SyntaxKind.CoalesceExpression) => 1,
        _ => 0,
    };

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
        bool viaProperty = false, int context = 0, List<string>? path = null)
    {
        var walk = ++Walks;
        foreach (var trivia in node.DescendantTrivia(n => n == top || !Units.ContainsKey(n)))
        {
            if (trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) || trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
                || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia))
            {
                mentions.AddRange(Coordinate().Matches(trivia.ToString()).Select(m => m.Value));
            }
        }
        var model = Models[node.SyntaxTree];
        // the references in evaluation order: a call after its receiver and arguments (`PassHours(Random(3) + 3)`
        // draws first), a property set after its value; a followed helper's calls stay together at its call
        // (an operand that ends where its operator does, `a < F(x)`, is evaluated first: the later start comes first)
        var made = new List<(int End, int Start, int Seq, List<object[]> Calls)>();
        var all = calls;
        foreach (var n in Own(node, top))
        {
            calls = [];
            made.Add((Evaluated(n), n.SpanStart, made.Count, calls));
            if (depth > 0)
            {
                FollowedDecisions += DecisionOf(n);
            }
            var here = ContextOf(n, node, context);
            var arms = ArmsOf(n, node, walk, path);
            arms.Add("@" + walk + "." + n.SpanStart);
            if (ExitScope(n, node) is { } scope)
            {
                arms.Add("!" + walk + "." + scope.SpanStart + "." + scope.Span.End);
            }
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
            // a local function's code runs where it is called (it is followed there), not where it is declared
            if (n.Ancestors().TakeWhile(a => a != node).Any(a => a is LocalFunctionStatementSyntax))
            {
                continue;
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
                calls.Add([line, "bcl:Math." + math.Name, via ?? "", math.Name, ConstArgs(n, model), documentedHere || Documented(n, top), viaProperty, here, arms]);
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
            if (symbol is IMethodSymbol { MethodKind: MethodKind.Ordinary or MethodKind.LocalFunction } && n is SimpleNameSyntax && !Invoked(n))
            {
                // a method group made a delegate (`SneakIn(c, chance, Recognise)`): its code runs when it is invoked
                here = ContextOf(n.Parent!, node, here, deferred: true);
            }
            foreach (var target in targets)
            {
                Reference(target, line, args, documented, viaProperty || symbol is IPropertySymbol, calls, mentions, literals, stack, depth, via, here, arms);
            }
        }
        all.AddRange(made.OrderBy(m => m.End).ThenByDescending(m => m.Start).ThenBy(m => m.Seq).SelectMany(m => m.Calls));
    }

    // the code of a lambda runs when it is invoked, not where it is written (a destination `() => ...` passed to a
    // helper, a hint, a LINQ selector): each lambda body is a context of its own, whose calls have no order against
    // the code around it; 0 is the unit's own code
    private static readonly Dictionary<(SyntaxNode, int), int> Contexts = [];

    private static int ContextOf(SyntaxNode n, SyntaxNode walked, int context, bool deferred = false)
    {
        var lambda = deferred ? n : n.Ancestors().TakeWhile(a => a != walked).OfType<AnonymousFunctionExpressionSyntax>().FirstOrDefault();
        if (lambda is null)
        {
            return context;
        }
        if (!Contexts.TryGetValue((lambda, context), out var id))
        {
            Contexts[(lambda, context)] = id = Contexts.Count + 1;
        }
        return id;
    }

    private static int Walks;

    // the branch arms a node is in, as "walk.position:arm" (the then or else of an `if` or `?:`, a switch section
    // or arm, and the code after an `if` whose then-part returns or throws, which is that if's else): two calls
    // in different arms of one branch never both run, and have no order; a followed helper's arms come after the
    // arms of its call site
    private static List<string> ArmsOf(SyntaxNode n, SyntaxNode walked, int walk, List<string>? outer)
    {
        var arms = new List<string>(outer ?? []);
        for (SyntaxNode child = n, a = n.Parent!; a is not null && child != walked; child = a, a = a.Parent!)
        {
            var id = walk + "." + a.SpanStart + "." + (int)a.Kind();
            switch (a)
            {
                case IfStatementSyntax i when child == i.Statement: arms.Add(id + ":0"); break;
                case IfStatementSyntax i when child == i.Else: arms.Add(id + ":1"); break;
                case ConditionalExpressionSyntax q when child == q.WhenTrue: arms.Add(id + ":0"); break;
                case ConditionalExpressionSyntax q when child == q.WhenFalse: arms.Add(id + ":1"); break;
                case SwitchStatementSyntax sw when child is SwitchSectionSyntax sec: arms.Add(id + ":" + sw.Sections.IndexOf(sec)); break;
                case SwitchExpressionSyntax sx when child is SwitchExpressionArmSyntax arm: arms.Add(id + ":" + sx.Arms.IndexOf(arm)); break;
                case BlockSyntax or SwitchSectionSyntax when child is StatementSyntax st:
                    var list = a is BlockSyntax b ? b.Statements : ((SwitchSectionSyntax)a).Statements;
                    foreach (var before in list.TakeWhile(x => x != st))
                    {
                        if (before is IfStatementSyntax { Else: null } exit && Exits(exit.Statement))
                        {
                            arms.Add(walk + "." + exit.SpanStart + "." + (int)exit.Kind() + ":1");
                        }
                    }
                    break;
            }
            if (a == walked)
            {
                break;
            }
        }
        return arms;
    }

    // the innermost block (or statement) the code at n cannot leave: a return or throw follows n's statement in it, or n is
    // in the return itself; what comes after it never runs after n ("@walk.position" and "!walk.start.end" in the arms)
    private static SyntaxNode? ExitScope(SyntaxNode n, SyntaxNode walked)
    {
        for (SyntaxNode child = n, a = n.Parent!; a is not null && child != walked; child = a, a = a.Parent!)
        {
            if (child is ReturnStatementSyntax or ThrowStatementSyntax)
            {
                return child;
            }
            if (a is BlockSyntax or SwitchSectionSyntax && child is StatementSyntax st)
            {
                var list = a is BlockSyntax b ? b.Statements : ((SwitchSectionSyntax)a).Statements;
                if (list.SkipWhile(x => x != st).Skip(1).Any(x => x is ReturnStatementSyntax or ThrowStatementSyntax))
                {
                    return a;
                }
            }
            if (a is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax || a == walked)
            {
                break;
            }
        }
        return null;
    }

    private static bool Exits(StatementSyntax s) => s switch
    {
        ReturnStatementSyntax or ThrowStatementSyntax => true,
        BlockSyntax b => b.Statements.LastOrDefault() is { } last && Exits(last),
        _ => false,
    };

    private static bool Invoked(SyntaxNode n)
    {
        var expr = n.Parent is MemberAccessExpressionSyntax access && access.Name == n ? access : n;
        return expr.Parent is InvocationExpressionSyntax call && call.Expression == expr;
    }

    // where the evaluation of what a node references completes: the end of the call, creation or assignment it names
    private static int Evaluated(SyntaxNode n)
    {
        var expr = n.Parent is MemberAccessExpressionSyntax access && access.Name == n ? access : n;
        return expr.Parent switch
        {
            InvocationExpressionSyntax call when call.Expression == expr => call.Span.End,
            AssignmentExpressionSyntax set when set.Left == expr => set.Span.End,
            _ => n.Span.End,
        };
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
        List<string> mentions, HashSet<int> literals, HashSet<SyntaxNode> stack, int depth, string? via, int context, List<string> arms)
    {
        foreach (var decl in symbol.DeclaringSyntaxReferences.Select(r => r.GetSyntax()))
        {
            var unitNode = UnitOf(decl);
            var parts = unitNode is not null && Parts.TryGetValue(unitNode, out var p) ? p : [];
            if (unitNode is not null && Units[unitNode].Cites.Any(c => !parts.Contains(c)))
            {
                foreach (var c in Units[unitNode].Cites.Where(c => !parts.Contains(c)))
                {
                    calls.Add([line, c, via ?? "", symbol.Name, args, documented, viaProperty, context, arms]);
                }
                break;
            }
            // (a member that only shares a part of the functions it names is followed, as an untraced helper is)
            // an untraced helper: followed at each use (its calls and draws count every time), not into itself
            // (a type declaration is a record's implicit member: nothing to follow)
            if (depth < FollowDepth && decl is not BaseTypeDeclarationSyntax && Models.ContainsKey(decl.SyntaxTree) && stack.Add(decl))
            {
                Walk(decl, decl, calls, mentions, literals, stack, depth + 1, via ?? symbol.Name, documented, viaProperty, context, arms);
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
            if ((x is StatementSyntax or SwitchSectionSyntax or ArgumentSyntax or ExpressionElementSyntax or SwitchExpressionArmSyntax
                    || x.Parent is InitializerExpressionSyntax)
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
