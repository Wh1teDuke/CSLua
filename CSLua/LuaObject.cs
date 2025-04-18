﻿using System.Runtime.CompilerServices;
using CSLua.Parse;
using CSLua.Util;

// ReSharper disable InconsistentNaming
namespace CSLua;

public struct TValue : IEquatable<TValue>
{
	private const long BOOLEAN_FALSE = 0;
	private const long BOOLEAN_TRUE = 1;

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
			// ReSharper disable once CompareOfFloatsByEqualityOperator
			LuaType.LUA_TNUMBER => NValue == o.NValue,
			LuaType.LUA_TINT64 => AsInt64() == o.AsInt64(),
			LuaType.LUA_TSTRING => AsString() == o.AsString(),
			_ => ReferenceEquals(OValue, o.OValue)
		};
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool operator==(TValue lhs, TValue rhs) => lhs.Equals(rhs);
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool operator!=(TValue lhs, TValue rhs) => !lhs.Equals(rhs);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool IsNil() => Type == LuaType.LUA_TNIL;
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool IsBoolean() => Type == LuaType.LUA_TBOOLEAN;
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool IsNumber() => Type == LuaType.LUA_TNUMBER;
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool IsInt64() => Type == LuaType.LUA_TINT64;
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool IsString() => Type == LuaType.LUA_TSTRING;
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool IsTable() => Type == LuaType.LUA_TTABLE;
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool IsFunction() => Type == LuaType.LUA_TFUNCTION;
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool IsThread() => Type == LuaType.LUA_TTHREAD;
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool IsUserData() => Type == LuaType.LUA_TLIGHTUSERDATA;
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool IsList() => Type == LuaType.LUA_TLIST;
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool IsLuaClosure() => OValue is LuaClosure;
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool IsCsClosure() => OValue is CsClosure;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]

	public ClosureType? GetClosureType()
	{
		if (!IsLuaClosure()) return null;
		return IsLuaClosure()? ClosureType.LUA : ClosureType.CSHARP;
	}
	
	public long AsInt64() =>
		BitConverter.DoubleToInt64Bits(NValue);
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool AsBool() => AsInt64() != BOOLEAN_FALSE;
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public string? AsString() => OValue as string;
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public LuaTable? AsTable() => OValue as LuaTable;
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public object? AsUserData() => OValue;
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public LuaClosure? AsLuaClosure() => OValue as LuaClosure;
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public CsClosure? AsCSClosure() => OValue as CsClosure;
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public ILuaState? AsThread() => OValue as ILuaState;
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public List<TValue>? AsList() => OValue as List<TValue>;

	public void SetNil() 
	{
		Type = LuaType.LUA_TNIL;
		NValue = 0.0; OValue = null!;
	}

	public void SetBool(bool v) 
	{
		Type = LuaType.LUA_TBOOLEAN;
		NValue = BitConverter.Int64BitsToDouble(
			v ? BOOLEAN_TRUE : BOOLEAN_FALSE);
		OValue = null!;
	}

	public void SetObj(StkId v) 
	{
		Type = v.V.Type;
		NValue = v.V.NValue; OValue = v.V.OValue;
	}
	
	public void SetList(List<TValue> v) 
	{
		Type = LuaType.LUA_TLIST;
		NValue = 0; OValue = v;
	}
	
	public void SetDouble(double v) 
	{
		Type = LuaType.LUA_TNUMBER;
		NValue = v; OValue = null!;
	}

	public void SetInt64(long v) 
	{
		Type = LuaType.LUA_TINT64;
		NValue = BitConverter.Int64BitsToDouble(v);
		OValue = null!;
	}

	public void SetString(string v) 
	{
		Type = LuaType.LUA_TSTRING;
		NValue = 0.0; OValue = v;
	}

	public void SetTable(LuaTable v) 
	{
		Type = LuaType.LUA_TTABLE;
		NValue = 0.0; OValue = v;
	}

	public void SetThread(LuaState v) 
	{
		Type = LuaType.LUA_TTHREAD;
		NValue = 0.0; OValue = v;
	}

	public void SetUserData(object v) 
	{
		Type = LuaType.LUA_TLIGHTUSERDATA;
		NValue = 0.0; OValue = v;
	}
	
	public void SetLuaClosure(LuaClosure v) 
	{
		Type = LuaType.LUA_TFUNCTION;
		NValue = 0.0; OValue = v;
	}

	public void SetCSClosure(CsClosure v) 
	{
		Type = LuaType.LUA_TFUNCTION;
		NValue = 0.0; OValue = v;
	}

	public override string ToString()
	{
		if (IsString()) return $"(string, {AsString()})";
		if (IsNumber()) return $"(number, {NValue})";
		if (IsBoolean()) return $"(bool, {AsBool()})";
		if (IsInt64()) return $"(long, {AsInt64()})";
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
	
	public static TValue Of(string value)
	{
		var result = new TValue();
		result.SetString(value);
		return result;
	}
	
	public static TValue Of(long value)
	{
		var result = new TValue();
		result.SetInt64(value);
		return result;
	}
	
	public static TValue Of(bool value)
	{
		var result = new TValue();
		result.SetBool(value);
		return result;
	}
}

public readonly ref struct StkId(ref TValue v)
{
	private static TValue _nil = TValue.Nil();
	public static StkId Nil => new (ref _nil);

	public readonly ref TValue V = ref v;

	public void Set(StkId other) => V.SetObj(other);

#pragma warning disable CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type
	internal unsafe TValue* PtrIndex => (TValue*)Unsafe.AsPointer(ref V);
#pragma warning restore CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type

	public override string ToString()
	{
		var detail = V.IsString() ? V.AsString()!.Replace("\n", "»") : "...";
		return $"StkId - {LuaState.TypeName(V.Type)} - {detail}";
	}
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

	public LuaProto? Parent;

	public int	LineDefined;
	public int	LastLineDefined;

	public int	NumParams;
	public bool	IsVarArg;
	public byte	MaxStackSize;

	public string Source = "";
	public string? Name = null;
	public string? RootName = null;

	private LuaClosure? _pure;

	public int GetFuncLine(int pc) => 
		(0 <= pc && pc < LineInfo.Count) ? LineInfo[pc] : 0;

	public LuaClosure Pure => _pure ??= new LuaClosure(this);
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
		LuaUtil.Assert(stackIndex == -1 || l != null);
		L = l;
		StackIndex = stackIndex;
		Value.SetNil();
	}

	public StkId StkId => 
		StackIndex != -1 ? L!.Ref(StackIndex) : new StkId(ref Value);
}

public interface BaseClosure;

public sealed class LuaClosure(LuaProto p): BaseClosure
{
	public readonly LuaProto 	 Proto = p;
	public readonly LuaUpValue[] Upvals = new LuaUpValue[p.Upvalues.Count];
	public int Length => Upvals.Length;
}

public sealed class CsClosure: BaseClosure
{
	public readonly CsDelegate Fun;
	public readonly TValue[] Upvals;
	public readonly string Name;

	public CsClosure(CsDelegate fun, string name = "C#Closure")
	{
		Fun = fun;
		Upvals = [];
		Name = name;
	}

	public CsClosure(CsDelegate fun, int len, string name = "C#Closure")
	{
		Fun = fun;
		Upvals = new TValue[len];
		for (var i = 0; i < len; ++i) 
			Upvals[i].SetNil();
		Name = name;
	}

	public StkId Ref(int index) => new (ref Upvals[index]);
}