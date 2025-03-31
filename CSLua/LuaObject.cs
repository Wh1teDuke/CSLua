using System.Runtime.CompilerServices;
using CSLua.Parse;
// ReSharper disable InconsistentNaming
namespace CSLua;

public struct TValue : IEquatable<TValue>
{
	private const ulong BOOLEAN_FALSE = 0;
	private const ulong BOOLEAN_TRUE = 1;

	public object OValue;
	public double NValue;
	public ulong UInt64Value =>
		BitConverter.DoubleToUInt64Bits(NValue);
	public int Tt;

	public override bool Equals(object? o) => 
		o is TValue value && Equals(value);
	
	public override int GetHashCode()
	{
		unchecked
		{
			var hashCode = Tt;
			hashCode = (hashCode * 397) ^ NValue.GetHashCode();
			hashCode = (hashCode * 397) ^ OValue?.GetHashCode() ?? 0;
			return hashCode;
		}
	}

	public bool Equals(TValue o)
	{
		if (Tt != o.Tt) return false;

		return Tt switch
		{
			(int)LuaType.LUA_TNIL => true,
			(int)LuaType.LUA_TBOOLEAN => AsBool() == o.AsBool(),
			(int)LuaType.LUA_TNUMBER => NValue == o.NValue,
			(int)LuaType.LUA_TUINT64 => UInt64Value == o.UInt64Value,
			(int)LuaType.LUA_TSTRING => AsString() == o.AsString(),
			_ => ReferenceEquals(OValue, o.OValue)
		};
	}

	public static bool operator==(TValue lhs, TValue rhs) => lhs.Equals(rhs);
	public static bool operator!=(TValue lhs, TValue rhs) => !lhs.Equals(rhs);

	public bool IsNil() => Tt == (int)LuaType.LUA_TNIL;
	public bool IsBoolean() => Tt == (int)LuaType.LUA_TBOOLEAN;
	public bool IsNumber() => Tt == (int)LuaType.LUA_TNUMBER;
	public bool IsUInt64() => Tt == (int)LuaType.LUA_TUINT64;
	public bool IsString() => Tt == (int)LuaType.LUA_TSTRING;
	public bool IsTable() => Tt == (int)LuaType.LUA_TTABLE;
	public bool IsFunction() => Tt == (int)LuaType.LUA_TFUNCTION;
	public bool IsThread() => Tt == (int)LuaType.LUA_TTHREAD;

	public bool IsLuaClosure() => OValue is LuaLClosureValue;
	public bool IsCsClosure() => OValue is LuaCsClosureValue;
	public bool AsBool() => UInt64Value != BOOLEAN_FALSE;
	public string AsString() => OValue as string;
	public LuaTable AsTable() => OValue as LuaTable;
	public LuaLClosureValue AsLuaClosure() => (LuaLClosureValue)OValue;
	public LuaCsClosureValue AsCSClosure() => (LuaCsClosureValue)OValue;
	public ILuaState AsThread() => (ILuaState)OValue;

	internal void SetNil() 
	{
		Tt = (int)LuaType.LUA_TNIL;
		NValue = 0.0;
		OValue = null!;
	}
	internal void SetBool(bool v) 
	{
		Tt = (int)LuaType.LUA_TBOOLEAN;
		NValue = BitConverter.UInt64BitsToDouble(
			v ? BOOLEAN_TRUE : BOOLEAN_FALSE);
		OValue = null!;
	}
	internal void SetObj(StkId v) 
	{
		Tt = v.V.Tt;
		NValue = v.V.NValue;
		OValue = v.V.OValue;
	}
	internal void SetDouble(double v) 
	{
		Tt = (int)LuaType.LUA_TNUMBER;
		NValue = v;
		OValue = null!;
	}
	internal void SetUInt64(ulong v) 
	{
		Tt = (int)LuaType.LUA_TUINT64;
		NValue = BitConverter.UInt64BitsToDouble(v);
		OValue = null!;
	}
	internal void SetString(string v) 
	{
		Tt = (int)LuaType.LUA_TSTRING;
		NValue = 0.0;
		OValue = v;
	}
	internal void SetTable(LuaTable v) 
	{
		Tt = (int)LuaType.LUA_TTABLE;
		NValue = 0.0;
		OValue = v;
	}
	internal void SetThread(LuaState v) 
	{
		Tt = (int)LuaType.LUA_TTHREAD;
		NValue = 0.0;
		OValue = v;
	}
	internal void SetUserData(object v) 
	{
		Tt = (int)LuaType.LUA_TLIGHTUSERDATA;
		NValue = 0.0;
		OValue = v;
	}
	
	internal void SetLuaClosure(LuaLClosureValue v) 
	{
		Tt = (int)LuaType.LUA_TFUNCTION;
		NValue = 0.0;
		OValue = v;
	}
	internal void SetCSClosure(LuaCsClosureValue v) 
	{
		Tt = (int)LuaType.LUA_TFUNCTION;
		NValue = 0.0;
		OValue = v;
	}

	public override string ToString()
	{
		if (IsString()) return $"(string, {AsString()})";
		if (IsNumber()) return $"(number, {NValue})";
		if (IsBoolean()) return $"(bool, {AsBool()})";
		if (IsUInt64()) return $"(uint64, {UInt64Value})";
		if (IsNil()) return "(nil)";
		if (IsTable()) return $"(table: {OValue})";
		if (IsThread()) return $"(thread: {OValue})";
		if (IsFunction())
			if (IsLuaClosure()) return $"(Lua function)";
			else if (IsCsClosure()) return $"(C# function)";
		return $"(type:{Tt})";
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
		var detail = V.IsString() ? V.AsString().Replace("\n", "»") : "...";
		return $"StkId - {LuaState.TypeName((LuaType)V.Tt)} - {detail}";
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
	public readonly LuaState L = null!;
	

	public LuaUpValue(LuaState l = null, int stackIndex = -1)
	{
		L = l;
		StackIndex = stackIndex;
		Value.SetNil();
	}

	public StkId StkId => 
		StackIndex != -1 ? L.Ref[StackIndex] : new StkId(ref Value);
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