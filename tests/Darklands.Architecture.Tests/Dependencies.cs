using System.Reflection;
using System.Reflection.Emit;

namespace Darklands.Architecture.Tests;

/// <summary>One use of a member or type by a product type: who uses it, what, and whether it is a write (a field store or a property setter).</summary>
internal readonly record struct Use(Type Owner, Type Target, string Member, bool Write)
{
    public override string ToString() => $"{Owner.FullName} -> {Target.FullName}.{Member}{(Write ? " (write)" : "")}";
}

/// <summary>
/// The product assemblies and what their types use, read from the compiled code: signatures (base types, interfaces, fields,
/// properties, parameters, return types) and every member or type token in the method bodies. Uses inside compiler-made types
/// (lambdas, iterators, async state machines) and nested types count for the outermost type that declares them.
/// </summary>
internal static class Dependencies
{
    /// <summary>The product assemblies by simple name (every src project; Darklands.Host.Sdl builds DarklandsRP).</summary>
    public static readonly string[] Products =
    [
        "Darklands.Core", "Darklands.Persistence", "Darklands.Content", "Darklands.Cards", "Darklands.Simulation",
        "Darklands.Platform", "Darklands.Rendering", "Darklands.Audio", "Darklands.Audio.Mt32", "Darklands.Launcher",
        "Darklands.DevConsole", "Darklands.Presentation", "Darklands.Host.Headless", "DarklandsRP",
    ];

    public static Assembly Load(string name) => Assembly.Load(name);

    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly Dictionary<short, OpCode> Codes = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(f => (OpCode)f.GetValue(null)!)
        .ToDictionary(o => o.Value);

    private static readonly Dictionary<string, List<Use>> Cache = [];

    /// <summary>Every use the types of <paramref name="assembly"/> make of types outside the base library's System.Object.</summary>
    public static IReadOnlyList<Use> Uses(string assembly)
    {
        lock (Cache)
        {
            if (!Cache.TryGetValue(assembly, out var uses))
            {
                uses = [.. Scan(Load(assembly))];
                Cache[assembly] = uses;
            }
            return uses;
        }
    }

    /// <summary>The outermost type declaring <paramref name="type"/>.</summary>
    public static Type Outermost(Type type)
    {
        while (type.DeclaringType is { } outer)
        {
            type = outer;
        }
        return type;
    }

    private static IEnumerable<Use> Scan(Assembly assembly)
    {
        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            types = [.. e.Types.OfType<Type>()];
        }
        foreach (var type in types)
        {
            var owner = Outermost(type);
            foreach (var used in Signature(type).SelectMany(Expand))
            {
                yield return new Use(owner, used, "", false);
            }
            foreach (var method in type.GetMethods(All).Cast<MethodBase>().Concat(type.GetConstructors(All)))
            {
                foreach (var use in Body(owner, type, method))
                {
                    yield return use;
                }
            }
        }
    }

    private static IEnumerable<Type> Signature(Type type)
    {
        if (type.BaseType is { } baseType)
        {
            yield return baseType;
        }
        foreach (var i in type.GetInterfaces())
        {
            yield return i;
        }
        foreach (var field in type.GetFields(All))
        {
            yield return field.FieldType;
        }
        foreach (var property in type.GetProperties(All))
        {
            yield return property.PropertyType;
        }
        foreach (var method in type.GetMethods(All))
        {
            yield return method.ReturnType;
            foreach (var p in method.GetParameters())
            {
                yield return p.ParameterType;
            }
        }
        foreach (var ctor in type.GetConstructors(All))
        {
            foreach (var p in ctor.GetParameters())
            {
                yield return p.ParameterType;
            }
        }
    }

    private static IEnumerable<Use> Body(Type owner, Type type, MethodBase method)
    {
        byte[]? il;
        try
        {
            il = method.GetMethodBody()?.GetILAsByteArray();
        }
        catch (BadImageFormatException)
        {
            il = null;
        }
        if (il is null)
        {
            yield break;
        }
        var typeArgs = type.IsGenericType ? type.GetGenericArguments() : null;
        var methodArgs = method.IsGenericMethod ? method.GetGenericArguments() : null;
        for (var i = 0; i < il.Length;)
        {
            short value = il[i++];
            if (value == 0xFE)
            {
                value = unchecked((short)(0xFE00 | il[i++]));
            }
            var op = Codes[value];
            var size = op.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, i),
                _ => 4,
            };
            if (op.OperandType is OperandType.InlineMethod or OperandType.InlineField or OperandType.InlineType or OperandType.InlineTok)
            {
                var token = BitConverter.ToInt32(il, i);
                MemberInfo? member;
                try
                {
                    member = method.Module.ResolveMember(token, typeArgs, methodArgs);
                }
                catch (Exception e) when (e is ArgumentException or BadImageFormatException or TypeLoadException or MissingMemberException)
                {
                    member = null;
                }
                switch (member)
                {
                    case Type t:
                        foreach (var e in Expand(t))
                        {
                            yield return new Use(owner, e, "", false);
                        }
                        break;
                    case FieldInfo f when f.DeclaringType is { } d:
                        yield return new Use(owner, d, f.Name, op == OpCodes.Stfld || op == OpCodes.Stsfld);
                        break;
                    case MethodBase m when m.DeclaringType is { } d:
                        yield return new Use(owner, d, m.Name, m.IsSpecialName && m.Name.StartsWith("set_", StringComparison.Ordinal));
                        break;
                }
            }
            i += size;
        }
    }

    /// <summary>A type with its element types and generic arguments (List&lt;Party&gt; uses Party too).</summary>
    private static IEnumerable<Type> Expand(Type type)
    {
        if (type.HasElementType)
        {
            foreach (var e in Expand(type.GetElementType()!))
            {
                yield return e;
            }
            yield break;
        }
        if (type.IsGenericParameter)
        {
            yield break;
        }
        yield return type;
        if (type.IsConstructedGenericType)
        {
            foreach (var e in type.GetGenericArguments().SelectMany(Expand))
            {
                yield return e;
            }
        }
    }

    /// <summary>The simple name of the assembly that declares <paramref name="type"/> (for an array or a generic, its element or definition).</summary>
    public static string AssemblyOf(Type type)
    {
        while (type.HasElementType)
        {
            type = type.GetElementType()!;
        }
        return type.Assembly.GetName().Name ?? "";
    }
}
