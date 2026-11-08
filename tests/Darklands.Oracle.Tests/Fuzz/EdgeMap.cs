using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;

namespace Darklands.Oracle.Tests.Fuzz;

/// <summary>
/// The AFL edge map of an instrumented assembly (SharpFuzz's <c>sharpfuzz</c> CLI run on a copy of the build output; the committed build
/// has no instrumentation and no package reference): SharpFuzz.Common.Trace.SharedMem is pointed at a map of ours by reflection. Null
/// when SharpFuzz.Common is not beside the tests (a plain build): the fuzzers then fall back to a coarser signal.
/// </summary>
internal sealed class EdgeMap
{
    public const int Size = 1 << 16;

    private readonly IntPtr _memory;
    private readonly FieldInfo _previous;
    private readonly byte[] _zero = new byte[Size];
    private readonly byte[] _read = new byte[Size];

    private EdgeMap(IntPtr memory, FieldInfo previous)
    {
        _memory = memory;
        _previous = previous;
    }

    public static EdgeMap? Attach()
    {
        Type? trace;
        try
        {
            trace = Type.GetType("SharpFuzz.Common.Trace, SharpFuzz.Common", throwOnError: false);
        }
        catch (Exception e) when (e is FileNotFoundException or FileLoadException or BadImageFormatException)
        {
            return null;
        }
        if (trace?.GetField("SharedMem") is not { } shared || trace.GetField("PrevLocation") is not { } previous)
        {
            return null;
        }
        var memory = Marshal.AllocHGlobal(Size);
        // the field is a byte*: stored through a two-instruction method (no unsafe code here)
        var set = new DynamicMethod("SetSharedMem", typeof(void), [typeof(IntPtr)], typeof(EdgeMap).Module, skipVisibility: true);
        var il = set.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stsfld, shared);
        il.Emit(OpCodes.Ret);
        set.CreateDelegate<Action<IntPtr>>()(memory);
        var map = new EdgeMap(memory, previous);
        map.Reset();
        return map;
    }

    public void Reset()
    {
        Marshal.Copy(_zero, 0, _memory, Size);
        _previous.SetValue(null, 0);
    }

    /// <summary>The run's edges as features (edge &lt;&lt; 3 | AFL count bucket); false when the map stayed empty (not instrumented).</summary>
    public bool Collect(List<long> features)
    {
        Marshal.Copy(_memory, _read, 0, Size);
        var any = false;
        for (var i = 0; i < Size; i++)
        {
            if (_read[i] == 0)
            {
                continue;
            }
            any = true;
            features.Add(((long)i << 3) | (long)(uint)Bucket(_read[i]));
        }
        return any;
    }

    public static int Bucket(int count) => count switch { 1 => 0, 2 => 1, 3 => 2, < 8 => 3, < 16 => 4, < 32 => 5, < 128 => 6, _ => 7 };
}
