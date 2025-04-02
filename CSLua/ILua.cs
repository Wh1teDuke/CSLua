

// #define DEBUG_RECORD_INS

using CSLua.Util;

namespace CSLua;

public interface ILoadInfo
{
	int ReadByte();
	int PeekByte();
}

public sealed class LuaDebug
{
	public string? 		Name;
	public string 		NameWhat;
	public int 			ActiveCIIndex;
	public int			CurrentLine;
	public int			NumUps;
	public bool			IsVarArg;
	public int			NumParams;
	public bool			IsTailCall;
	public string		Source;
	public int			LineDefined;
	public int			LastLineDefined;
	public string		What;
	public string		ShortSrc;
}

public delegate int CSharpFunctionDelegate(ILuaState state);

public interface ILua
{
	public static ILua New() => new LuaState();
	
	LuaState NewThread();

	ThreadStatus Load<T>(T loadInfo, string name, string? mode) where T: struct, ILoadInfo;
	DumpStatus Dump(LuaWriter writeFunc);

	ThreadStatus GetContext(out int context);
	void Call(int numArgs, int numResults);
	void CallK(int numArgs, int numResults,
		int context, CSharpFunctionDelegate? continueFunc = null);
	ThreadStatus PCall(int numArgs, int numResults, int errFunc);
	ThreadStatus PCallK(int numArgs, int numResults, int errFunc,
		int context, CSharpFunctionDelegate? continueFunc = null);

	ThreadStatus Resume(ILuaState from, int numArgs);
	int Yield(int numResults);
	int YieldK(int numResults,
		int context, CSharpFunctionDelegate? continueFunc = null);

	int  AbsIndex(int index);
	int  GetTop();
	void SetTop(int top);

	void Remove(int index);
	void Insert(int index);
	void Replace(int index);
	void Copy(int fromIndex, int toIndex);
	void XMove(ILuaState to, int n);

	bool CheckStack(int size);
	bool GetStack(LuaDebug ar, int level);
	int  Error();

	int  UpValueIndex(int i);
	string? GetUpValue(int funcIndex, int n);
	string? SetUpValue(int funcIndex, int n);

	void CreateTable(int nArray, int nRec);
	void NewTable();
	bool Next(int index);
	void RawGetI(int index, int n);
	void RawSetI(int index, int n);
	void RawGet(int index);
	void RawSet(int index);
	void GetField(int index, string key);
	void SetField(int index, string key);
	void GetTable(int index);
	void SetTable(int index);

	void Concat(int n);

	LuaType Type(int index);
	string TypeName(LuaType t);
	bool IsNil(int index);
	bool IsNone(int index);
	bool IsNoneOrNil(int index);
	bool IsNumber(int index);
	bool IsString(int index);
	bool IsTable(int index);
	bool IsFunction(int index);
	bool IsBool(int index);

	bool Compare(int index1, int index2, LuaEq op);
	bool RawEqual(int index1, int index2);
	int  RawLen(int index);
	void Len(int index);

	void PushNil();
	void PushBoolean(bool b);
	void PushNumber(double n);
	void PushInteger(int n);
	void PushUnsigned(uint n);
	void PushString(string s);
	void PushCSharpFunction(CSharpFunctionDelegate f);
	void PushCSharpClosure(CSharpFunctionDelegate f, int n);
	void PushValue(int index);
	void PushGlobalTable();
	void PushLightUserData(object o);
	void PushInt64(long o);
	bool PushThread();
	void PushLuaFunction(LuaClosure f);
	void PushCsFunction(CsClosure f);
	void PushTable(LuaTable table);
	void PushList(List<TValue> list);

	void Pop(int n);

	bool GetMetaTable(int index);
	bool SetMetaTable(int index);

	void GetGlobal(string name);
	void SetGlobal(string name);

	public bool TestStack(ReadOnlySpan<LuaType> args)
	{
		if (GetTop() != args.Length) return false;
		var i = 1;
		foreach (var arg in args)
		{
			if (Type(i++) != arg) return false;
		}

		return true;
	}

	ILuaState GetThread(out int arg);

	string?	ToString(int index);
	double 	ToNumberX(int index, out bool isNum);
	double 	ToNumber(int index);
	int		ToIntegerX(int index, out bool isNum);
	int	ToInteger(int index);
	uint	ToUnsignedX(int index, out bool isNum);
	uint	ToUnsigned(int index);
	bool   	ToBoolean(int index);
	long	ToInt64(int index);
	long	ToInt64X(int index, out bool isNum);
	object?	ToObject(int index);
	object? ToUserData(int index);
	List<TValue>? ToList(int index);
	ILuaState	ToThread(int index);
	public LuaClosure? ToLuaFunction(int index);

	ThreadStatus Status { get; }

	string BaseFolder { get; set; }

	string 	DebugGetInstructionHistory();
}

public interface ILuaState : ILua, ILuaAuxLib;

internal delegate void PFuncDelegate<T>(ref T ud) where T: struct;