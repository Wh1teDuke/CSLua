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
			(int)LuaType.LUA_TBOOLEAN => BValue() == o.BValue(),
			(int)LuaType.LUA_TNUMBER => NValue == o.NValue,
			(int)LuaType.LUA_TUINT64 => UInt64Value == o.UInt64Value,
			(int)LuaType.LUA_TSTRING => SValue() == o.SValue(),
			_ => ReferenceEquals(OValue, o.OValue)
		};
	}

	public static bool operator==(TValue lhs, TValue rhs) => lhs.Equals(rhs);
	public static bool operator!=(TValue lhs, TValue rhs) => !lhs.Equals(rhs);

	public bool TtIsNil() => Tt == (int)LuaType.LUA_TNIL;
	public bool TtIsBoolean() => Tt == (int)LuaType.LUA_TBOOLEAN;
	public bool TtIsNumber() => Tt == (int)LuaType.LUA_TNUMBER;
	public bool TtIsUInt64() => Tt == (int)LuaType.LUA_TUINT64;
	public bool TtIsString() => Tt == (int)LuaType.LUA_TSTRING;
	public bool TtIsTable() => Tt == (int)LuaType.LUA_TTABLE;
	public bool TtIsFunction() => Tt == (int)LuaType.LUA_TFUNCTION;
	public bool TtIsThread() => Tt == (int)LuaType.LUA_TTHREAD;

	public bool ClIsLuaClosure() => OValue is LuaLClosureValue;
	public bool ClIsCsClosure() => OValue is LuaCsClosureValue;
	public bool ClIsLcsClosure() => OValue is CSharpFunctionDelegate;
	public bool BValue() => UInt64Value != BOOLEAN_FALSE;
	public string SValue() => OValue as string;
	public LuaTable HValue() => OValue as LuaTable;
	public LuaLClosureValue ClLValue() => (LuaLClosureValue)OValue;
	public LuaCsClosureValue ClCsValue() => (LuaCsClosureValue)OValue;
	public ILuaState THValue() => (ILuaState)OValue;

	internal void SetNilValue() 
	{
		Tt = (int)LuaType.LUA_TNIL;
		NValue = 0.0;
		OValue = null!;
	}
	internal void SetBValue(bool v) 
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
	internal void SetNValue(double v) 
	{
		Tt = (int)LuaType.LUA_TNUMBER;
		NValue = v;
		OValue = null!;
	}
	internal void SetUInt64Value(ulong v) 
	{
		Tt = (int)LuaType.LUA_TUINT64;
		NValue = BitConverter.UInt64BitsToDouble(v);
		OValue = null!;
	}
	internal void SetSValue(string v) 
	{
		Tt = (int)LuaType.LUA_TSTRING;
		NValue = 0.0;
		OValue = v;
	}
	internal void SetHValue(LuaTable v) 
	{
		Tt = (int)LuaType.LUA_TTABLE;
		NValue = 0.0;
		OValue = v;
	}
	internal void SetThValue(LuaState v) 
	{
		Tt = (int)LuaType.LUA_TTHREAD;
		NValue = 0.0;
		OValue = v;
	}
	internal void SetPValue(object v) 
	{
		Tt = (int)LuaType.LUA_TLIGHTUSERDATA;
		NValue = 0.0;
		OValue = v;
	}
	
	internal void SetClLValue(LuaLClosureValue v) 
	{
		Tt = (int)LuaType.LUA_TFUNCTION;
		NValue = 0.0;
		OValue = v;
	}
	internal void SetClCsValue(LuaCsClosureValue v) 
	{
		Tt = (int)LuaType.LUA_TFUNCTION;
		NValue = 0.0;
		OValue = v;
	}
	internal void SetClLcsValue(CSharpFunctionDelegate v) 
	{
		Tt = (int)LuaType.LUA_TFUNCTION;
		NValue = 0.0;
		OValue = v;
	}

	public override string ToString()
	{
		if (TtIsString()) return $"(string, {SValue()})";
		if (TtIsNumber()) return $"(number, {NValue})";
		if (TtIsBoolean()) return $"(bool, {BValue()})";
		if (TtIsUInt64()) return $"(uint64, {UInt64Value})";
		if (TtIsNil()) return "(nil)";
		if (TtIsTable()) return $"(table: {OValue})";
		if (TtIsThread()) return $"(thread: {OValue})";
		if (TtIsFunction())
			if (ClIsLuaClosure()) return $"(Lua function)";
			else if (ClIsCsClosure()) return $"(C# function)";
		return $"(type:{Tt})";
	}

	public static TValue Nil()
	{
		var result = new TValue();
		result.SetNilValue();
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
		var detail = V.TtIsString() ? V.SValue().Replace("\n", "»") : "...";
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
		Value.SetNilValue();
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
			Upvals[i].SetNilValue();
	}
	
	public StkId Ref(int index) => new (ref Upvals[index]);
}