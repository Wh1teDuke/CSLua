using System.Runtime.CompilerServices;
using CSLua.Parse;
using CSLua.Utils;

// ReSharper disable InconsistentNaming
namespace CSLua;

public struct TValue : IEquatable<TValue>
{
	private const ulong BOOLEAN_FALSE = 0;
	private const ulong BOOLEAN_TRUE = 1;

	public object? OValue;
	public double NValue;
	public LuaType Type;

	public override bool Equals(object? o) => 
		o is TValue value && Equals(value);
	
	public override int GetHashCode()
	{
		unchecked
		{
			var hashCode = (int)Type;
			hashCode = (hashCode * 397) ^ NValue.GetHashCode();
			hashCode = (hashCode * 397) ^ OValue?.GetHashCode() ?? 0;
			return hashCode;
		}
	}

	public bool Equals(TValue o)
	{
		if (Type != o.Type) return false;

		return Type switch
		{
			LuaType.LUA_TNIL => true,
			LuaType.LUA_TBOOLEAN => AsBool() == o.AsBool(),
			LuaType.LUA_TNUMBER => NValue == o.NValue,
			LuaType.LUA_TUINT64 => AsUInt64 == o.AsUInt64,
			LuaType.LUA_TSTRING => AsString() == o.AsString(),
			_ => ReferenceEquals(OValue, o.OValue)
		};
	}

	public static bool operator==(TValue lhs, TValue rhs) => lhs.Equals(rhs);
	public static bool operator!=(TValue lhs, TValue rhs) => !lhs.Equals(rhs);

	public bool IsNil() => Type == LuaType.LUA_TNIL;
	public bool IsBoolean() => Type == LuaType.LUA_TBOOLEAN;
	public bool IsNumber() => Type == LuaType.LUA_TNUMBER;
	public bool IsUInt64() => Type == LuaType.LUA_TUINT64;
	public bool IsString() => Type == LuaType.LUA_TSTRING;
	public bool IsTable() => Type == LuaType.LUA_TTABLE;
	public bool IsFunction() => Type == LuaType.LUA_TFUNCTION;
	public bool IsThread() => Type == LuaType.LUA_TTHREAD;
	public bool IsUserData() => Type == LuaType.LUA_TLIGHTUSERDATA;
	public bool IsList() => Type == LuaType.LUA_TLIST;

	public bool IsLuaClosure() => OValue is LuaLClosureValue;
	public bool IsCsClosure() => OValue is LuaCsClosureValue;
	
	public ulong AsUInt64 =>
		BitConverter.DoubleToUInt64Bits(NValue);
	public bool AsBool() => AsUInt64 != BOOLEAN_FALSE;
	public string? AsString() => OValue as string;
	public LuaTable? AsTable() => OValue as LuaTable;
	public LuaLClosureValue? AsLuaClosure() => OValue as LuaLClosureValue;
	public LuaCsClosureValue? AsCSClosure() => OValue as LuaCsClosureValue;
	public ILuaState? AsThread() => OValue as ILuaState;
	public List<TValue>? AsList() => OValue as List<TValue>;

	internal void SetNil() 
	{
		Type = LuaType.LUA_TNIL;
		NValue = 0.0;
		OValue = null!;
	}
	internal void SetBool(bool v) 
	{
		Type = LuaType.LUA_TBOOLEAN;
		NValue = BitConverter.UInt64BitsToDouble(
			v ? BOOLEAN_TRUE : BOOLEAN_FALSE);
		OValue = null!;
	}
	internal void SetObj(StkId v) 
	{
		Type = v.V.Type;
		NValue = v.V.NValue;
		OValue = v.V.OValue;
	}
	
	internal void SetList(List<TValue> v) 
	{
		Type = LuaType.LUA_TLIST;
		NValue = 0;
		OValue = v;
	}
	
	internal void SetDouble(double v) 
	{
		Type = LuaType.LUA_TNUMBER;
		NValue = v;
		OValue = null!;
	}
	internal void SetUInt64(ulong v) 
	{
		Type = LuaType.LUA_TUINT64;
		NValue = BitConverter.UInt64BitsToDouble(v);
		OValue = null!;
	}
	internal void SetString(string v) 
	{
		Type = LuaType.LUA_TSTRING;
		NValue = 0.0;
		OValue = v;
	}
	internal void SetTable(LuaTable v) 
	{
		Type = LuaType.LUA_TTABLE;
		NValue = 0.0;
		OValue = v;
	}
	internal void SetThread(LuaState v) 
	{
		Type = LuaType.LUA_TTHREAD;
		NValue = 0.0;
		OValue = v;
	}
	internal void SetUserData(object v) 
	{
		Type = LuaType.LUA_TLIGHTUSERDATA;
		NValue = 0.0;
		OValue = v;
	}
	
	internal void SetLuaClosure(LuaLClosureValue v) 
	{
		Type = LuaType.LUA_TFUNCTION;
		NValue = 0.0;
		OValue = v;
	}
	internal void SetCSClosure(LuaCsClosureValue v) 
	{
		Type = LuaType.LUA_TFUNCTION;
		NValue = 0.0;
		OValue = v;
	}

	public override string ToString()
	{
		if (IsString()) return $"(string, {AsString()})";
		if (IsNumber()) return $"(number, {NValue})";
		if (IsBoolean()) return $"(bool, {AsBool()})";
		if (IsUInt64()) return $"(uint64, {AsUInt64})";
		if (IsNil()) return "(nil)";
		if (IsTable()) return $"(table: {OValue})";
		if (IsThread()) return $"(thread: {OValue})";
		if (IsList()) return $"(list: {OValue})";
		if (IsFunction())
			if (IsLuaClosure()) return "(Lua function)";
			else if (IsCsClosure()) return "(C# function)";
		return $"(type:{Type})";
	}

	public static TValue Nil()
	{
		var result = new TValue();
		result.SetNil();
		return result;
	}
}

public readonly ref struct StkId(ref TValue v)
{
	private static TValue _nil = TValue.Nil();
	public static StkId Nil => new (ref _nil);
	
	public readonly ref TValue V = ref v;
	
	public void Set(StkId other) => V.SetObj(other);

	internal unsafe TValue* PtrIndex => (TValue*)Unsafe.AsPointer(ref V);
	
	public override string ToString()
	{
		var detail = V.IsString() ? V.AsString()!.Replace("\n", "»") : "...";
		return $"StkId - {LuaState.TypeName(V.Type)} - {detail}";
	}
}

public sealed class LuaLClosureValue(LuaProto p)
{
	public readonly LuaProto 	 Proto = p;
	public readonly LuaUpValue[] Upvals = new LuaUpValue[p.Upvalues.Count];
	public int Length => Upvals.Length;
}

public record struct LocVar(string VarName, int StartPc, int EndPc);
public record struct UpValueDesc(string Name, int Index, bool InStack, bool IsEnv);

public sealed class LuaProto
{
	public readonly List<Instruction> 	Code = [];
	public readonly List<TValue>		K = [];
	public readonly List<LuaProto>		P = [];
	public readonly List<UpValueDesc>	Upvalues = [];
	public readonly List<int>			LineInfo = [];
	public readonly List<LocVar>		LocVars = [];

	public int	LineDefined;
	public int	LastLineDefined;

	public int	NumParams;
	public bool	IsVarArg;
	public byte	MaxStackSize;

	public string Source = "";

	private LuaLClosureValue? _pure;

	public int GetFuncLine(int pc) => 
		(0 <= pc && pc < LineInfo.Count) ? LineInfo[pc] : 0;

	public LuaLClosureValue Pure => _pure ??= new LuaLClosureValue(this);
}
	
public sealed class LuaUpValue
{
	public LuaUpValue? Next = null;
	public TValue Value;
	public int StackIndex;
	public int Index;

	private readonly LuaState? L;
	

	public LuaUpValue(LuaState? l = null, int stackIndex = -1)
	{
		Util.Assert(stackIndex == -1 || l != null);
		L = l;
		StackIndex = stackIndex;
		Value.SetNil();
	}

	public StkId StkId => 
		StackIndex != -1 ? L!.Ref[StackIndex] : new StkId(ref Value);
}

public sealed class LuaCsClosureValue
{
	public readonly CSharpFunctionDelegate F;
	public readonly TValue[]	Upvals;

	public LuaCsClosureValue(CSharpFunctionDelegate f)
	{
		F = f;
		Upvals = [];
	}

	public LuaCsClosureValue(CSharpFunctionDelegate f, int len)
	{
		F = f;
		Upvals = new TValue[len];
		for (var i = 0; i < len; ++i) 
			Upvals[i].SetNil();
	}
	
	public StkId Ref(int index) => new (ref Upvals[index]);
}