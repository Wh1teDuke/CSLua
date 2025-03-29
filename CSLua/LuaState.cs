// #define ENABLE_DUMP_STACK
// #define DEBUG_RECORD_INS

using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;
using CSLua.Lib;
using CSLua.Parse;
using CSLua.Utils;
// ReSharper disable InconsistentNaming
// ReSharper disable SwitchStatementHandlesSomeKnownEnumValuesWithDefault
// ReSharper disable SwitchExpressionHandlesSomeKnownEnumValuesWithExceptionInDefault
// ReSharper disable BitwiseOperatorOnEnumWithoutFlags

namespace CSLua;

using InstructionPtr = Pointer<Instruction>;

public struct Pointer<T>(List<T> list, int index)
{
	private readonly List<T> _list = list;
	public int Index { get; set; } = index;

	public T Value { get => _list[Index]; set => _list[Index] = value; }

	public T ValueInc => _list[Index++];

	public static Pointer<T> operator +(Pointer<T> lhs, int rhs) => 
		new(lhs._list, lhs.Index + rhs);

	public static Pointer<T> operator -(Pointer<T> lhs, int rhs) => 
		new(lhs._list, lhs.Index - rhs);
}

public enum CallStatus
{
	CIST_NONE		= 0,

	CIST_LUA		= (1<<0),	/* call is running a Lua function */
	CIST_HOOKED		= (1<<1),	/* call is running a debug hook */
	CIST_REENTRY	= (1<<2),	/* call is running on same invocation of
	                               luaV_execute of previous call */
	CIST_YIELDED	= (1<<3),	/* call reentered after suspension */
	CIST_YPCALL		= (1<<4),	/* call is a yieldable protected call */
	CIST_STAT		= (1<<5),	/* call has an error status (pcall) */
	CIST_TAIL		= (1<<6),	/* call was tail called */
}

public sealed class CallInfo
{
	public int Index;

	public int FuncIndex;
	public int TopIndex;

	public int NumResults;
	public CallStatus CallStatus;

	public CSharpFunctionDelegate? ContinueFunc;
	public int Context;
	public int ExtraIndex;
	public int OldErrFunc;
	public ThreadStatus Status;

	// For Lua functions
	public int BaseIndex;
	public InstructionPtr SavedPc;

	public bool IsLua => (CallStatus & CallStatus.CIST_LUA) != 0;

	public int CurrentPc
	{
		get
		{
			Util.Assert(IsLua);
			return SavedPc.Index - 1;
		}
	}
}

public sealed class GlobalState(LuaState state)
{
	private TValue _registry;

	public readonly LuaTable?[] MetaTables = new LuaTable[(int)LuaType.LUA_NUMTAGS];
	public readonly LuaState	MainThread = state;
	
	public StkId Registry => new (ref _registry);
}

public sealed class LuaState : ILuaState
{
	public readonly GlobalState	G;
	public TValue[]			Stack;
	public CallInfo 		CI;
	public CallInfo[] 		BaseCI;
	public readonly LuaUpValue	UpvalHead;

	public int				NumNonYieldable;
	public int				NumCSharpCalls;
	public int				ErrFunc;
	public int				StackLast;
	public int				TopIndex;
	
	public ThreadStatus		Status { get; private set; }

	public string BaseFolder { get; set; }

	public StkId Top => Ref[TopIndex];
	
	public StkId IncTop() => Ref[TopIndex++];

	public readonly struct StackWrap(LuaState L)
	{ public StkId this[int index] => new(ref L.Stack[index]); }

	public readonly StackWrap Ref;

	private int Index(StkId v)
	{
		unsafe
		{
			fixed (TValue* arr = &Stack[0])
			{
				var d = v.PtrIndex - arr;
				return (int)d;
			}
		}
	}
	
#if DEBUG_RECORD_INS
	private Queue<Instruction> InstructionHistory;
#endif

	private readonly ILua API;

	public LuaState(GlobalState? g = null)
	{
		API = this;

		// Initialized inside InitStack()
		Stack = null!;
		CI = null!;
		BaseCI = null!;
		//

		UpvalHead =		new LuaUpValue(this, 0);
		Ref				= new StackWrap(this);
		BaseFolder		= Environment.CurrentDirectory;
		NumNonYieldable = 1;
		NumCSharpCalls  = 0;
		Status			= ThreadStatus.LUA_OK;

		if (g == null)
		{
			G = new GlobalState(this);
			InitRegistry();
		}
		else
		{
			G = g;
		}

		ErrFunc = 0;

#if DEBUG_RECORD_INS
		InstructionHistory = new Queue<Instruction>();
#endif

		InitStack();
	}

	private void IncrTop()
	{
		IncTop();
		D_CheckStack(0);
	}

	internal void ApiIncrTop()
	{
		IncTop();
		// ULDebug.Log("[ApiIncrTop] ==== TopIndex:" + TopIndex);
		// ULDebug.Log("[ApiIncrTop] ==== CI.TopIndex:" + CI.TopIndex);
		Util.ApiCheck(TopIndex <= CI.TopIndex, "Stack overflow");
	}

	private void InitStack()
	{
		Stack = new TValue[LuaDef.BASIC_STACK_SIZE];
		StackLast = LuaDef.BASIC_STACK_SIZE - LuaDef.EXTRA_STACK;
		for (var i = 0; i < LuaDef.BASIC_STACK_SIZE; ++i) 
		{
			Stack[i].SetNilValue();
		}

		TopIndex = 0;
		BaseCI = new CallInfo[LuaDef.BASE_CI_SIZE];
		for (var i = 0; i < LuaDef.BASE_CI_SIZE; ++i) 
		{
			var newCI = new CallInfo();
			BaseCI[i] = newCI;
			newCI.Index = i;
		}
		CI = BaseCI[0];
		CI.FuncIndex = TopIndex;
		IncTop().V.SetNilValue(); // 'function' entry for this 'ci'
		CI.TopIndex = TopIndex + LuaDef.LUA_MINSTACK;
	}

	private void InitRegistry()
	{
		var mt = new TValue();
		var rmt = new StkId(ref mt);

		G.Registry.V.SetHValue(new LuaTable(this));

		mt.SetThValue(this);
		G.Registry.V.HValue().SetInt(LuaDef.LUA_RIDX_MAINTHREAD, rmt);

		mt.SetHValue(new LuaTable(this));
		G.Registry.V.HValue().SetInt(LuaDef.LUA_RIDX_GLOBALS, rmt);
	}

	private string DumpStackToString(int baseIndex, string tag = "")
	{
		var sb = new StringBuilder();
		sb.Append($"===================================================================== DumpStack: {tag}").Append('\n');
		sb.Append($"== BaseIndex: {baseIndex}").Append('\n');
		sb.Append($"== Top.Index: {TopIndex}").Append('\n');
		sb.Append($"== CI.Index: {CI.Index}").Append('\n');
		sb.Append($"== CI.TopIndex: {CI.TopIndex}").Append('\n');
		sb.Append($"== CI.Func.Index: {CI.FuncIndex}").Append('\n');
		for (var i = 0; i < Stack.Length || i <= TopIndex; ++i)
		{
			var isTop = TopIndex == i;
			var isBase = baseIndex == i;
			var inStack = i < Stack.Length;

			var postfix = (isTop || isBase)
				? $"<--------------------- {(isBase ? "[BASE]" : "")}{(isTop ? "[TOP]" : "")}"
				: "";
			var body = $"======== {i - baseIndex}/{i} > {(inStack ? Stack[i].ToString() : "")} {postfix}";

			sb.Append(body).Append('\n');
		}
		return sb.ToString();
	}

	public void DumpStack(int baseIndex, string tag = "") => 
		ULDebug.Log(DumpStackToString(baseIndex, tag));

	private static string GetTagMethodName(TMS tm)
	{
		return tm switch
		{
			TMS.TM_INDEX => "__index",
			TMS.TM_NEWINDEX => "__newindex",
			TMS.TM_GC => "__gc",
			TMS.TM_MODE => "__mode",
			TMS.TM_LEN => "__len",
			TMS.TM_EQ => "__eq",
			TMS.TM_ADD => "__add",
			TMS.TM_SUB => "__sub",
			TMS.TM_MUL => "__mul",
			TMS.TM_DIV => "__div",
			TMS.TM_MOD => "__mod",
			TMS.TM_POW => "__pow",
			TMS.TM_UNM => "__unm",
			TMS.TM_LT => "__lt",
			TMS.TM_LE => "__le",
			TMS.TM_CONCAT => "__concat",
			TMS.TM_CALL => "__call",
			_ => throw new ArgumentOutOfRangeException(nameof(tm), tm, null)
		};
	}

	private static bool T_TryGetTM(LuaTable? mt, TMS tm, out StkId val)
	{
		val = StkId.Nil;
		if (mt == null) return false;
		
		if (!mt.TryGetStr(GetTagMethodName(tm), out val) || val.V.TtIsNil()) // no tag method?
		{
			// Cache this fact
			mt.NoTagMethodFlags |= 1u << (int)tm;
			return false;
		}

		return true;
	}

	private bool T_TryGetTMByObj(StkId o, TMS tm, out StkId val)
	{
		val = StkId.Nil;
		LuaTable? mt;

		switch (o.V.Tt)
		{
			case (int)LuaType.LUA_TTABLE:
			{
				var tbl = o.V.HValue();
				mt = tbl.MetaTable;
				break;
			}
			case (int)LuaType.LUA_TUSERDATA:
				throw new NotImplementedException();

			default:
			{
				mt = G.MetaTables[o.V.Tt];
				break;
			}
		}

		return mt != null && mt.TryGetStr(GetTagMethodName(tm), out val);
	}
	
	#region LuaAPI.cs
	LuaState ILua.NewThread()
	{
		var newLua = new LuaState(G);

		Top.V.SetThValue(newLua);
		ApiIncrTop();

		newLua.BaseFolder = BaseFolder;

		return newLua;
	}

	private readonly record struct LoadParameter(
		LuaState L, ILoadInfo LoadInfo, string Name, string? Mode);

	private void CheckMode(string? given, string expected)
	{
		if (given != null && !given.Contains(expected[0]))
		{
			var msg = $"Attempt to load a {expected} chunk (mode is '{given}')"; 
			O_PushString(msg);
			D_Throw(ThreadStatus.LUA_ERRSYNTAX, msg);
		}
	}

	private static void F_Load(ref LoadParameter param)
	{
		var L = param.L;

		LuaProto proto;
		if (param.LoadInfo is ProtoLoadInfo plInfo)
			proto = plInfo.Proto;
		else if (param.LoadInfo.PeekByte() == LuaConf.LUA_SIGNATURE[0])
		{
			L.CheckMode(param.Mode, "binary");
			proto = UnDump.LoadBinary(param.LoadInfo, param.Name);
		}
		else
		{
			L.CheckMode(param.Mode, "text");
			var parser = Parser.Read(param.LoadInfo, param.Name);
			proto = parser.Proto;
		}

		var cl = new LuaLClosureValue(proto);
		Util.Assert(cl.Length == cl.Proto.Upvalues.Count);

		for (var i = 0; i < cl.Length; i++)
		{
			cl.Upvals[i] = new LuaUpValue(L);
			cl.Upvals[i].Value.SetNilValue();
		}

		L.Top.V.SetClLValue(cl);
		L.IncrTop();
	}

	private static readonly PFuncDelegate<LoadParameter> DG_F_Load = F_Load;

	public ThreadStatus Load<T>(
		T loadInfo, string name, string? mode) where T: struct, ILoadInfo
	{
		var param  = new LoadParameter(this, loadInfo, name, mode);
		var status = D_PCall(DG_F_Load, ref param, TopIndex, ErrFunc);
		if (status != ThreadStatus.LUA_OK) return status;

		var below = Ref[TopIndex - 1];
		Util.Assert(below.V.TtIsFunction() && below.V.ClIsLuaClosure());
		var cl = below.V.ClLValue();
		if (cl.Length == 1) 
		{
			G.Registry.V.HValue().TryGetInt(LuaDef.LUA_RIDX_GLOBALS, out var gt);
			cl.Upvals[0] = new LuaUpValue();
			cl.Upvals[0].Value.SetObj(gt);
		}

		return status;
	}

	DumpStatus ILua.Dump(LuaWriter writeFunc)
	{
		Util.ApiCheckNumElems(this, 1);

		var below = Ref[TopIndex - 1];
		if (!below.V.TtIsFunction() || !below.V.ClIsLuaClosure())
			return DumpStatus.ERROR;

		var o = below.V.ClLValue();
		if (o == null) return DumpStatus.ERROR;

		return DumpState.Dump(o.Proto, writeFunc, false);
	}

	ThreadStatus ILua.GetContext(out int context)
	{
		if ((CI.CallStatus & CallStatus.CIST_YIELDED) != 0)
		{
			context = CI.Context;
			return CI.Status;
		}

		context = 0;
		return ThreadStatus.LUA_OK;
	}

	public void Call(int numArgs, int numResults) => 
		API.CallK(numArgs, numResults, 0);

	void ILua.CallK(int numArgs, int numResults,
		int context, CSharpFunctionDelegate? continueFunc)
	{
		Util.ApiCheck(continueFunc == null || !CI.IsLua,
			"Cannot use continuations inside hooks");
		Util.ApiCheckNumElems(this, numArgs + 1);
		Util.ApiCheck(Status == ThreadStatus.LUA_OK,
			"Cannot do calls on non-normal thread");
		CheckResults(numArgs, numResults);
		var func = Ref[TopIndex - (numArgs + 1)];

		// Need to prepare continuation?
		if (continueFunc != null && NumNonYieldable == 0)
		{
			CI.ContinueFunc = continueFunc;
			CI.Context		= context;
			D_Call(func, numResults, true);
		}
		// No continuation or no yieldable
		else
		{
			D_Call(func, numResults, false);
		}
		AdjustResults(numResults);
	}

	private struct CallS
	{
		public LuaState L;
		public int FuncIndex;
		public int NumResults;
	}

	private static void F_Call(ref CallS ud) => 
		ud.L.D_Call(ud.L.Ref[ud.FuncIndex], ud.NumResults, false);

	private static readonly PFuncDelegate<CallS> DG_F_Call = F_Call;

	private void CheckResults(int numArgs, int numResults)
	{
		Util.ApiCheck(numResults == LuaDef.LUA_MULTRET ||
		              CI.TopIndex - TopIndex >= numResults - numArgs,
			"Results from function overflow current stack size");
	}

	private void AdjustResults(int numResults)
	{
		if (numResults == LuaDef.LUA_MULTRET && CI.TopIndex < TopIndex) 
			CI.TopIndex = TopIndex;
	}

	ThreadStatus ILua.PCall(int numArgs, int numResults, int errFunc) => 
		API.PCallK(numArgs, numResults, errFunc, 0);

	public ThreadStatus PCallK( 
		int numArgs, int numResults, int errFunc,
		int context, CSharpFunctionDelegate? continueFunc)
	{
		Util.ApiCheck(continueFunc == null || !CI.IsLua,
			"Cannot use continuations inside hooks");
		Util.ApiCheckNumElems(this, numArgs + 1);
		Util.ApiCheck(Status == ThreadStatus.LUA_OK,
			"Cannot do calls on non-normal thread");
		CheckResults(numArgs, numResults);

		int func;
		if (errFunc == 0)
			func = 0;
		else
		{
			if (!Index2Addr(errFunc, out _))
				Util.InvalidIndex();
			errFunc += errFunc <= 0 ? TopIndex : CI.FuncIndex;
			func = errFunc;
		}

		ThreadStatus status;
		var c = new CallS { L = this, FuncIndex = TopIndex - (numArgs + 1) };
		if (continueFunc == null || NumNonYieldable > 0) // no continuation or no yieldable?
		{
			c.NumResults = numResults;
			status = D_PCall(DG_F_Call, ref c, c.FuncIndex, func);
		}
		else
		{
			var ciIndex = CI.Index;
			CI.ContinueFunc = continueFunc;
			CI.Context		= context;
			CI.ExtraIndex	= c.FuncIndex;
			CI.OldErrFunc	= ErrFunc;
			ErrFunc = func;
			CI.CallStatus |= CallStatus.CIST_YPCALL;

			D_Call(Ref[c.FuncIndex], numResults, true);

			var ci = BaseCI[ciIndex];
			ci.CallStatus &= ~CallStatus.CIST_YPCALL;
			ErrFunc = ci.OldErrFunc;
			status = ThreadStatus.LUA_OK;
		}
		AdjustResults(numResults);
		return status;
	}

	private void FinishCSharpCall()
	{
		var ci = CI;
		Util.Assert(ci.ContinueFunc != null); // Must have a continuation
		Util.Assert(NumNonYieldable == 0);
		// Finish 'CallK'
		AdjustResults(ci.NumResults);
		// Call continuation function
		if ((ci.CallStatus & CallStatus.CIST_STAT) == 0) // No call status?
		{
			ci.Status = ThreadStatus.LUA_YIELD; // 'Default' status
		}
		Util.Assert(ci.Status != ThreadStatus.LUA_OK);
		ci.CallStatus = (ci.CallStatus
		                & ~(CallStatus.CIST_YPCALL | CallStatus.CIST_STAT))
		                | CallStatus.CIST_YIELDED;

		var n = ci.ContinueFunc!(this); // Call
		Util.ApiCheckNumElems(this, n);
		// Finish `D_PreCall'
		D_PosCall(TopIndex - n);
	}

	private void Unroll()
	{
		while (true)
		{
			if (CI.Index == 0) // Stack is empty?
				return; // Coroutine finished normally
			if (!CI.IsLua) // C# function?
				FinishCSharpCall();
			else // Lua function
			{
				V_FinishOp(); // Finish interrupted instruction
				V_Execute(); // Execute down to higher C# `boundary'
			}
		}
	}

	private readonly record struct UnrollParam(LuaState L);
	private static void UnrollWrap(ref UnrollParam param) => param.L.Unroll();

	private static readonly PFuncDelegate<UnrollParam> DG_Unroll = UnrollWrap;

	private void ResumeError(string msg, int firstArg)
	{
		TopIndex = firstArg;
		Top.V.SetSValue(msg);
		IncrTop();
		D_Throw(ThreadStatus.LUA_RESUME_ERROR, msg);
	}

	// Check whether thread has suspended protected call
	private CallInfo? FindPCall()
	{
		for (var i = CI.Index; i >= 0; --i) 
		{
			var ci = BaseCI[i];
			if ((ci.CallStatus & CallStatus.CIST_YPCALL) != 0)
				return ci;
		}
		return null; // No pending pcall
	}

	private bool Recover(ThreadStatus status)
	{
		var ci = FindPCall();
		if (ci == null) // No recover point
			return false;

		var oldTop = ci.ExtraIndex;
		F_Close(oldTop);
		SetErrorObj(status, oldTop);
		CI = ci;
		NumNonYieldable = 0;
		ErrFunc = ci.OldErrFunc;
		ci.CallStatus |= CallStatus.CIST_STAT;
		ci.Status = status;
		return true;
	}

	// Do the work for `lua_resume' in protected mode
	private void Resume(int firstArg)
	{
		var numCSharpCalls = NumCSharpCalls;
		var ci = CI;
		if (numCSharpCalls >= LuaLimits.LUAI_MAXCCALLS)
			ResumeError("C stack overflow", firstArg);
		if (Status == ThreadStatus.LUA_OK) // may be starting a coroutine
		{
			if (ci.Index > 0) // not in base level
			{
				ResumeError("cannot resume non-suspended coroutine", firstArg);
			}
			if (!D_PreCall(Ref[firstArg - 1], LuaDef.LUA_MULTRET)) // Lua function?
			{
				V_Execute(); // call it
			}
		}
		else if (Status != ThreadStatus.LUA_YIELD)
		{
			ResumeError("cannot resume dead coroutine", firstArg);
		}
		else // resume from previous yield
		{
			Status = ThreadStatus.LUA_OK;
			ci.FuncIndex = ci.ExtraIndex;
			if (ci.IsLua) // yielded inside a hook?
			{
				V_Execute(); // just continue running Lua code
			}
			else // `common' yield
			{
				if (ci.ContinueFunc != null)
				{
					ci.Status = ThreadStatus.LUA_YIELD; // `default' status
					ci.CallStatus |= CallStatus.CIST_YIELDED;
					var n = ci.ContinueFunc(this); // call continuation
					Util.ApiCheckNumElems(this, n);
					firstArg = TopIndex - n; // yield results come from continuation
				}
				D_PosCall(firstArg);
			}
			Unroll();
		}
		Util.Assert(numCSharpCalls == NumCSharpCalls);
	}

	private record struct ResumeParam(LuaState L, int FirstArg);

	private static void ResumeWrap(ref ResumeParam param) => 
		param.L.Resume(param.FirstArg);

	private static readonly PFuncDelegate<ResumeParam> DG_Resume = ResumeWrap;

	ThreadStatus ILua.Resume(ILuaState from, int numArgs)
	{
		var fromState = from as LuaState;
		NumCSharpCalls = (fromState != null) ? fromState.NumCSharpCalls + 1 : 1;
		NumNonYieldable = 0; // Allow yields

		Util.ApiCheckNumElems(this, (Status == ThreadStatus.LUA_OK) ? numArgs + 1 : numArgs);

		var resumeParam = new ResumeParam
		{ L = this, FirstArg = TopIndex - numArgs };
		var status = D_RawRunProtected(DG_Resume, ref resumeParam);
		if (status == ThreadStatus.LUA_RESUME_ERROR) // Error calling 'lua_resume'?
		{
			status = ThreadStatus.LUA_ERRRUN;
		}
		else // Yield or regular error
		{
			while (status != ThreadStatus.LUA_OK &&
			       status != ThreadStatus.LUA_YIELD) // Error?
			{
				if (Recover(status)) // Recover point?
				{
					var unrollParam = new UnrollParam(this);
					status = D_RawRunProtected(DG_Unroll, ref unrollParam);
				}
				else // Unrecoverable error
				{
					Status = status; // Mark thread as 'dead'
					SetErrorObj(status, TopIndex);
					CI.TopIndex = TopIndex;
					break;
				}
			}
			Util.Assert(status == Status);
		}

		NumNonYieldable = 1; // Do not allow yields
		NumCSharpCalls--;
		Util.Assert(NumCSharpCalls == ((fromState != null) ? fromState.NumCSharpCalls : 0));
		return status;
	}

	int ILua.Yield(int numResults) => API.YieldK(numResults, 0);

	int ILua.YieldK(
		int numResults, int context, CSharpFunctionDelegate? continueFunc)
	{
		var ci = CI;
		Util.ApiCheckNumElems(this, numResults);

		if (NumNonYieldable > 0)
		{
			if (this != G.MainThread)
				G_RunError("attempt to yield across metamethod/C-call boundary");
			else
				G_RunError("attempt to yield from outside a coroutine");
		}
		Status = ThreadStatus.LUA_YIELD;
		ci.ExtraIndex = ci.FuncIndex; // save current `func'
		if (ci.IsLua) // inside a hook
		{
			Util.ApiCheck(continueFunc == null, "hooks cannot continue after yielding");
		}
		else
		{
			ci.ContinueFunc = continueFunc;
			if (ci.ContinueFunc != null) // is there a continuation
				ci.Context = context;
			ci.FuncIndex = TopIndex - (numResults + 1);
			throw _YieldExcp.Value!;
		}
		Util.Assert((ci.CallStatus & CallStatus.CIST_HOOKED) != 0); // must be inside a hook
		return 0;
	}
	
	public int AbsIndex(int index)
	{
		return index is > 0 or <= LuaDef.LUA_REGISTRYINDEX
			? index : TopIndex - CI.FuncIndex + index;
	}

	public int GetTop() => TopIndex - (CI.FuncIndex + 1);

	public void SetTop(int index)
	{
		if (index >= 0)
		{
			Util.ApiCheck(
				index <= StackLast - (CI.FuncIndex + 1), 
				"New top too large");
			var newTop = CI.FuncIndex + 1 + index;
			for (var i = TopIndex; i < newTop; ++i)
				Stack[i].SetNilValue();
			TopIndex = newTop;
		}
		else
		{
			Util.ApiCheck(
				-(index + 1) <= (TopIndex - (CI.FuncIndex + 1)), 
				"Invalid new top");
			TopIndex += index + 1;
		}
	}

	public void Remove(int index)
	{
		if (!Index2Addr(index, out _))
			Util.InvalidIndex();

		index += index <= 0 ? TopIndex : CI.FuncIndex;
		for (var i = index + 1; i < TopIndex; ++i)
			Ref[i - 1].V.SetObj(Ref[i]);

		--TopIndex;
	}

	void ILua.Insert(int index)
	{
		if (!Index2Addr(index, out var p))
			Util.InvalidIndex();

		var i = TopIndex;
		index += index <= 0 ? TopIndex : CI.FuncIndex;
		while (i > index) 
		{
			Stack[i].SetObj(Ref[i - 1]);
			i--;
		}
		p.Set(Top);
	}

	private void MoveTo(StkId fr, int index)
	{
		if (!Index2Addr(index, out var to))
			Util.InvalidIndex();

		to.Set(fr);
	}

	void ILua.Replace(int index)
	{
		Util.ApiCheckNumElems(this, 1);
		MoveTo(Ref[--TopIndex], index);
	}

	public void Copy(int fromIndex, int toIndex)
	{
		if (!Index2Addr(fromIndex, out var fr))
			Util.InvalidIndex();
		MoveTo(fr, toIndex);
	}

	void ILua.XMove(ILuaState to, int n)
	{
		var toLua = (LuaState)to;
		if (this == toLua) return;

		Util.ApiCheckNumElems(this, n);
		Util.ApiCheck(G == toLua.G, "moving among independent states");
		Util.ApiCheck(toLua.CI.TopIndex - toLua.TopIndex >= n, "not enough elements to move");

		var index = TopIndex - n;
		TopIndex = index;
		for (var i = 0; i < n; ++i)
		{
			toLua.IncTop().Set(Ref[index + i]);
		}
	}

	private void GrowStack(int size) => D_GrowStack(size);

	private readonly record struct GrowStackParam(
		LuaState L, int Size);

	private static void GrowStackWrap(ref GrowStackParam param) => 
		param.L.GrowStack(param.Size);

	private static readonly PFuncDelegate<GrowStackParam> DG_GrowStack = GrowStackWrap;

	bool ILua.CheckStack(int size)
	{
		bool res;

		if (StackLast - TopIndex > size)
			res = true;
		// need to grow stack
		else
		{
			var inuse = TopIndex + LuaDef.EXTRA_STACK;
			if (inuse > LuaConf.LUAI_MAXSTACK - size)
				res = false;
			else 
			{
				var param = new GrowStackParam(this, size);
				res = D_RawRunProtected(
					DG_GrowStack, ref param) == ThreadStatus.LUA_OK;
			}
		}

		if (res && CI.TopIndex < TopIndex + size)
			CI.TopIndex = TopIndex + size; // adjust frame top

		return res;
	}

	int ILua.Error()
	{
		Util.ApiCheckNumElems(this, 1);
		var msg = API.ToString(-1);
		G_ErrorMsg(msg);
		return 0;
	}

	int ILua.UpValueIndex(int i) => LuaDef.LUA_REGISTRYINDEX - i;

	private string? AuxUpvalue(StkId addr, int n, out StkId val)
	{
		val = StkId.Nil;
		if (!addr.V.TtIsFunction())
			return null;

		if (addr.V.ClIsLuaClosure()) 
		{
			var f = addr.V.ClLValue();
			var p = f.Proto;
			if (!(1 <= n && n <= p.Upvalues.Count))
				return null;
			val = f.Upvals[n - 1].StkId;
			var name = p.Upvalues[n - 1].Name;
			return (name == null) ? "" : name;
		}

		if (addr.V.ClIsCsClosure()) 
		{
			var f = addr.V.ClCsValue();
			if (!(1 <= n && n <= f.Upvals.Length))
				return null;
			val = f.Ref(n - 1);
			return "";
		}

		return null;
	}

	string? ILua.GetUpValue(int funcIndex, int n)
	{
		if (!Index2Addr(funcIndex, out var addr))
			return null;

		if (AuxUpvalue(addr, n, out var val) is not {} result)
			return null;

		Top.Set(val);
		ApiIncrTop();
		return result;
	}

	string? ILua.SetUpValue(int funcIndex, int n)
	{
		if (!Index2Addr(funcIndex, out var addr))
			return null;

		Util.ApiCheckNumElems(this, 1);

		if (AuxUpvalue(addr, n, out var val) is not {} result)
			return null;

		--TopIndex;
		val.Set(Top);
		return result;
	}

	public void CreateTable(int nArray, int nRec)
	{
		var tbl = new LuaTable(this);
		Top.V.SetHValue(tbl);
		ApiIncrTop();
		if (nArray > 0 || nRec > 0)
			tbl.Resize(nArray, nRec);
	}

	public void NewTable() => API.CreateTable(0, 0);

	bool ILua.Next(int index)
	{
		if (!Index2Addr(index, out var addr))
			throw new LuaException("Table expected");

		var tbl = addr.V.HValue();
		if (tbl == null)
			throw new LuaException("Table expected");

		var key = Ref[TopIndex - 1];
		if (tbl.Next(key, Top))
		{
			ApiIncrTop();
			return true;
		}

		--TopIndex;
		return false;
	}

	public void RawGetI(int index, int n)
	{
		if (!Index2Addr(index, out var addr))
			Util.ApiCheck(false, "Table expected");

		var tbl = addr.V.HValue();
		Util.ApiCheck(tbl != null, "Table expected");

		tbl!.TryGetInt(n, out var v);
		Top.Set(v);
		ApiIncrTop();
	}

	string ILua.DebugGetInstructionHistory()
	{
#if DEBUG_RECORD_INS
		var sb = new System.Text.StringBuilder();
		foreach (var i in InstructionHistory) {
			sb.AppendLine(i.ToString());
		}
		return sb.ToString();
#else
		return "DEBUG_RECORD_INS not defined";
#endif
	}

	void ILua.RawGet(int index)
	{
		if (!Index2Addr(index, out var addr))
			throw new LuaException("Table expected");

		if (!addr.V.TtIsTable())
			throw new LuaException("Table expected");

		var tbl = addr.V.HValue();
		var below = Ref[TopIndex - 1];

		tbl.TryGet(below, out var value);
		below.V.SetObj(value);
	}

	void ILua.RawSetI(int index, int n)
	{
		Util.ApiCheckNumElems(this, 1);
		if (!Index2Addr(index, out var addr))
			Util.InvalidIndex();
		Util.ApiCheck(addr.V.TtIsTable(), "Table expected");
		var tbl = addr.V.HValue();
		tbl.SetInt(n, Ref[--TopIndex]);
	}

	void ILua.RawSet(int index)
	{
		Util.ApiCheckNumElems(this, 2);
		if (!Index2Addr(index, out var addr))
			Util.InvalidIndex();
		Util.ApiCheck(addr.V.TtIsTable(), "Table expected");
		var tbl = addr.V.HValue();
		tbl.Set(Ref[TopIndex - 2], Ref[TopIndex - 1]);
		TopIndex -= 2;
	}

	void ILua.GetField(int index, string key)
	{
		if (!Index2Addr(index, out var addr))
			Util.InvalidIndex();

		Top.V.SetSValue(key);
		var below = Top;
		ApiIncrTop();
		V_GetTable(addr, below, below);
	}

	public void SetField(int index, string key)
	{
		if (!Index2Addr(index, out var addr))
			Util.InvalidIndex();

		IncTop().V.SetSValue(key);
		V_SetTable(addr, Ref[TopIndex - 1], Ref[TopIndex - 2]);
		TopIndex -= 2;
	}

	void ILua.GetTable(int index)
	{
		if (!Index2Addr(index, out var addr))
			Util.InvalidIndex();

		var below = Ref[TopIndex - 1];
		V_GetTable(addr, below, below);
	}

	void ILua.SetTable(int index)
	{
		Util.ApiCheckNumElems(this, 2);
		if (!Index2Addr(index, out var addr))
			Util.InvalidIndex();

		var key = Ref[TopIndex - 2];
		var val = Ref[TopIndex - 1];
		V_SetTable(addr, key, val);
		TopIndex -= 2;
	}

	void ILua.Concat(int n)
	{
		Util.ApiCheckNumElems(this, n);
		if (n >= 2)
		{
			V_Concat(n);
		}
		else if (n == 0)
		{
			Top.V.SetSValue("");
			ApiIncrTop();
		}
	}

	public LuaType Type(int index)
	{
		return !Index2Addr(index, out var addr) 
			? LuaType.LUA_TNONE : (LuaType)addr.V.Tt;
	}

	internal static string TypeName(LuaType t)
	{
		return t switch
		{
			LuaType.LUA_TNIL => "nil",
			LuaType.LUA_TBOOLEAN => "boolean",
			LuaType.LUA_TLIGHTUSERDATA => "lightuserdata",
			LuaType.LUA_TUINT64 => "UInt64",
			LuaType.LUA_TNUMBER => "number",
			LuaType.LUA_TSTRING => "string",
			LuaType.LUA_TTABLE => "table",
			LuaType.LUA_TFUNCTION => "function",
			LuaType.LUA_TUSERDATA => "userdata",
			LuaType.LUA_TTHREAD => "thread",
			LuaType.LUA_TPROTO => "proto",
			LuaType.LUA_TUPVAL => "upval",
			_ => "no value"
		};
	}

	string ILua.TypeName(LuaType t) => TypeName(t);

	private static string ObjTypeName(StkId v) => 
		TypeName((LuaType)v.V.Tt);

	// For internal use only; will not trigger an error in ApiIncrTop() due to Top exceeding CI.Top
	private void O_PushString(string s)
	{
		Top.V.SetSValue(s);
		IncrTop();
	}

	bool ILua.IsNil(int index) => API.Type(index) == LuaType.LUA_TNIL;

	bool ILua.IsNone(int index) => API.Type(index) == LuaType.LUA_TNONE;

	bool ILua.IsNoneOrNil(int index)
	{
		var t = API.Type(index);
		return t is LuaType.LUA_TNONE or LuaType.LUA_TNIL;
	}

	bool ILua.IsString(int index)
	{
		var t = API.Type(index);
		return t is LuaType.LUA_TSTRING or LuaType.LUA_TNUMBER;
	}

	public bool IsTable(int index) => API.Type(index) == LuaType.LUA_TTABLE;

	public bool IsFunction(int index) => 
		API.Type(index) == LuaType.LUA_TFUNCTION;

	bool ILua.Compare(int index1, int index2, LuaEq op)
	{
		if (!Index2Addr(index1, out var addr1))
			Util.InvalidIndex();

		if (!Index2Addr(index2, out var addr2))
			Util.InvalidIndex();

		switch (op)
		{
			case LuaEq.LUA_OPEQ: return EqualObj(addr1, addr2, false);
			case LuaEq.LUA_OPLT: return V_LessThan(addr1, addr2);
			case LuaEq.LUA_OPLE: return V_LessEqual(addr1, addr2);
			default: Util.ApiCheck(false, "Invalid option"); return false;
		}
	}

	bool ILua.RawEqual(int index1, int index2)
	{
		if (!Index2Addr(index1, out var addr1))
			return false;

		if (!Index2Addr(index2, out var addr2))
			return false;

		return V_RawEqualObj(addr1, addr2);
	}

	int ILua.RawLen(int index)
	{
		if (!Index2Addr(index, out var addr))
			Util.InvalidIndex();

		switch (addr.V.Tt)
		{
			case (int)LuaType.LUA_TSTRING:
				var s = addr.V.SValue();
				return s == null ? 0 : s.Length;
			case (int)LuaType.LUA_TUSERDATA:
				throw new NotImplementedException();
			case (int)LuaType.LUA_TTABLE:
				return addr.V.HValue().Length;
			default: return 0;
		}
	}

	void ILua.Len(int index)
	{
		if (!Index2Addr(index, out var addr))
			Util.InvalidIndex();

		V_ObjLen(Top, addr);
		ApiIncrTop();
	}

	public void PushNil()
	{
		Top.V.SetNilValue();
		ApiIncrTop();
	}

	public void PushBoolean(bool b)
	{
		Top.V.SetBValue(b);
		ApiIncrTop();
	}

	public void PushNumber(double n)
	{
		Top.V.SetNValue(n);
		ApiIncrTop();
	}

	public void PushInteger(int n)
	{
		Top.V.SetNValue(n);
		ApiIncrTop();
	}

	void ILua.PushUnsigned(uint n)
	{
		Top.V.SetNValue(n);
		ApiIncrTop();
	}

	public void PushString(string? s)
	{
		if (s == null)
		{
			API.PushNil();
		}
		else
		{
			Top.V.SetSValue(s);
			ApiIncrTop();	
		}
	}

	public void PushCSharpFunction(CSharpFunctionDelegate f) => 
		API.PushCSharpClosure(f, 0);

	public void PushLuaFunction(LuaLClosureValue f)
	{
		Top.V.SetClLValue(f);
		ApiIncrTop();
	}
	
	public void PushTable(LuaTable table)
	{
		Top.V.SetHValue(table);
		ApiIncrTop();
	}

	public void PushCSharpClosure(CSharpFunctionDelegate f, int n)
	{
		if (n == 0)
		{
			Top.V.SetClCsValue(new LuaCsClosureValue(f));
		}
		else
		{
			// C# Function with UpValue
			Util.ApiCheckNumElems(this, n);
			Util.ApiCheck(n <= LuaLimits.MAXUPVAL, "Upvalue index too large");

			var cscl = new LuaCsClosureValue(f, n);
			var index = TopIndex - n;
			TopIndex = index;
			for (var i = 0; i < n; ++i)
				cscl.Upvals[i].SetObj(Ref[index + i]);

			Top.V.SetClCsValue(cscl);
		}
		ApiIncrTop();
	}

	public void PushValue(int index)
	{
		if (!Index2Addr(index, out var addr))
			Util.InvalidIndex();

		Top.Set(addr);
		ApiIncrTop();
	}

	public void PushGlobalTable() => 
		API.RawGetI(LuaDef.LUA_REGISTRYINDEX, LuaDef.LUA_RIDX_GLOBALS);

	public void PushLightUserData(object o)
	{
		Top.V.SetPValue(o);
		ApiIncrTop();
	}
	
	public void Push(TValue value)
	{
		Top.Set(new StkId(ref value));
		ApiIncrTop();
	}

	public void PushUInt64(ulong o)
	{
		Top.V.SetUInt64Value(o);
		ApiIncrTop();
	}

	bool ILua.PushThread()
	{
		Top.V.SetThValue(this);
		ApiIncrTop();
		return G.MainThread == this;
	}

	public void Pop(int n) => SetTop(-n - 1);
	
	bool ILua.GetMetaTable(int index)
	{
		if (!Index2Addr(index, out var addr))
			Util.InvalidIndex();

		LuaTable? mt;
		switch (addr.V.Tt)
		{
			case (int)LuaType.LUA_TTABLE:
			{
				var tbl = addr.V.HValue();
				mt = tbl.MetaTable;
				break;
			}
			case (int)LuaType.LUA_TUSERDATA:
			{
				throw new NotImplementedException();
			}
			default:
			{
				mt = G.MetaTables[addr.V.Tt];
				break;
			}
		}
		if (mt == null) return false;
		Top.V.SetHValue(mt);
		ApiIncrTop();
		return true;
	}

	bool ILua.SetMetaTable(int index)
	{
		Util.ApiCheckNumElems(this, 1);

		if (!Index2Addr(index, out var addr))
			Util.InvalidIndex();

		var below = Ref[TopIndex - 1];
		LuaTable? mt;
		if (below.V.TtIsNil())
			mt = null;
		else
		{
			Util.ApiCheck(below.V.TtIsTable(), "Table expected");
			mt = below.V.HValue();
		}

		switch (addr.V.Tt)
		{
			case (int)LuaType.LUA_TTABLE:
				var tbl = addr.V.HValue();
				tbl.MetaTable = mt;
				break;
			case (int)LuaType.LUA_TUSERDATA:
				throw new NotImplementedException();
			default:
				G.MetaTables[addr.V.Tt] = mt;
				break;
		}
		--TopIndex;
		return true;
	}

	public void GetGlobal(string name)
	{
		G.Registry.V.HValue().TryGetInt(LuaDef.LUA_RIDX_GLOBALS, out var gt);
		IncTop().V.SetSValue(name);
		V_GetTable(gt, Ref[TopIndex - 1], Ref[TopIndex - 1]);
	}

	public void SetGlobal(string name)
	{
		G.Registry.V.HValue().TryGetInt(LuaDef.LUA_RIDX_GLOBALS, out var gt);
		IncTop().V.SetSValue(name);
		V_SetTable(gt, Ref[TopIndex - 1], Ref[TopIndex - 2]);
		TopIndex -= 2;
	}

	public ILuaState GetThread(out int arg)
	{
		if (IsThread(1))
		{
			arg = 1;
			return ToThread(1);
		}

		arg = 0;
		return this;
	}

	public string ToString(int index)
	{
		if (!Index2Addr(index, out var addr))
			return null!;

		if (addr.V.TtIsString())
			return addr.V.SValue();

		if (!V_ToString(ref addr.V))
			return null!;

		if (!Index2Addr(index, out addr))
			return null!;

		Util.Assert(addr.V.TtIsString());
		return addr.V.SValue();
	}

	double ILua.ToNumberX(int index, out bool isNum)
	{
		if (!Index2Addr(index, out var addr))
		{
			isNum = false;
			return 0.0;
		}

		if (addr.V.TtIsNumber()) 
		{
			isNum = true;
			return addr.V.NValue;
		}

		if (addr.V.TtIsString()) 
		{
			var n = new TValue();
			if (V_ToNumber(addr, new StkId(ref n))) 
			{
				isNum = true;
				return n.NValue;
			}
		}

		isNum = false;
		return 0;
	}

	public double ToNumber(int index) => API.ToNumberX(index, out _);

	int ILua.ToIntegerX(int index, out bool isNum)
	{
		if (!Index2Addr(index, out var addr))
		{
			isNum = false;
			return 0;
		}

		if (addr.V.TtIsNumber()) 
		{
			isNum = true;
			return (int)addr.V.NValue;
		}

		if (addr.V.TtIsString()) 
		{
			var n = new TValue();
			if (V_ToNumber(addr, new StkId(ref n))) 
			{
				isNum = true;
				return (int)n.NValue;
			}
		}

		isNum = false;
		return 0;
	}

	public int ToInteger(int index) => API.ToIntegerX(index, out _);
	
	public bool IsNumber(int index)
	{
		API.ToIntegerX(index, out var isNum);
		return isNum;
	}

	uint ILua.ToUnsignedX(int index, out bool isNum)
	{
		if (!Index2Addr(index, out var addr))
		{
			isNum = false;
			return 0;
		}

		if (addr.V.TtIsNumber())
		{
			isNum = true;
			return (uint)addr.V.NValue;
		}

		if (addr.V.TtIsString())
		{
			var n = new TValue();
			if (V_ToNumber(addr, new StkId(ref n))) 
			{
				isNum = true;
				return (uint)n.NValue;
			}
		}

		isNum = false;
		return 0;
	}

	uint ILua.ToUnsigned(int index) =>
		API.ToUnsignedX(index, out _);

	public bool ToBoolean(int index) =>
		Index2Addr(index, out var addr) && !IsFalse(addr);
	
	public bool IsBool(int index) =>
		Index2Addr(index, out var addr) && addr.V.TtIsBoolean();

	public bool IsThread(int index) =>
		Index2Addr(index, out var addr) && addr.V.TtIsThread();

	ulong ILua.ToUInt64X(int index, out bool isNum)
	{
		if (!Index2Addr(index, out var addr)) 
		{
			isNum = false;
			return 0;
		}

		if (!addr.V.TtIsUInt64()) 
		{
			isNum = false;
			return 0;
		}

		isNum = true;
		return addr.V.UInt64Value;
	}

	public ulong ToUInt64(int index) =>
		API.ToUInt64X(index, out _);

	public object ToObject(int index)
	{
		if (!Index2Addr(index, out var addr))
			return null!;
		return addr.V.OValue;
	}

	public object ToUserData(int index)
	{
		if (!Index2Addr(index, out var addr))
			return null!;

		return addr.V.Tt switch
		{
			(int)LuaType.LUA_TUSERDATA => throw new NotImplementedException(),
			(int)LuaType.LUA_TLIGHTUSERDATA => addr.V.OValue,
			(int)LuaType.LUA_TUINT64 => addr.V.UInt64Value,
			_ => null!
		};
	}
	
	public LuaLClosureValue ToLuaFunction(int index)
	{
		if (!Index2Addr(index, out var addr))
			return null!;

		if (addr.V.TtIsFunction() && addr.V.ClIsLuaClosure())//addr.V.UInt64Value == TValue.CLOSURE_LUA)
			return addr.V.ClLValue();

		return null!;
	}

	public ILuaState ToThread(int index)
	{
		if (!Index2Addr(index, out var addr))
			return null!;
		return (addr.V.TtIsThread() ? addr.V.THValue() : null)!;
	}
	
	internal bool Index2Addr(int index, out StkId addr)
	{
		addr = new StkId();
		var ci = CI;
		if (index > 0)
		{
			var addrIndex = ci.FuncIndex + index;
			Util.ApiCheck(
				index <= ci.TopIndex - (ci.FuncIndex + 1), 
				"Unacceptable index");
			if (addrIndex >= TopIndex)
				return false;

			addr = Ref[addrIndex];
			return true;
		}

		if (index > LuaDef.LUA_REGISTRYINDEX)
		{
			Util.ApiCheck(
				index != 0 && -index <= TopIndex - (ci.FuncIndex + 1),
				"invalid index");
			addr = Ref[TopIndex + index];
			return true;
		}

		if (index == LuaDef.LUA_REGISTRYINDEX)
		{
			addr = new StkId(ref G.Registry.V);
			return true;
		}
		// upvalues

		index = LuaDef.LUA_REGISTRYINDEX - index;
		Util.ApiCheck(
			index <= LuaLimits.MAXUPVAL + 1,
			"Upvalue index too large");
		var func = Ref[ci.FuncIndex];
		Util.Assert(func.V.TtIsFunction());

		if (func.V.ClIsLcsClosure())
			return false;

		Util.Assert(func.V.ClIsCsClosure());
		var clcs = func.V.ClCsValue();
		if (index > clcs.Upvals.Length)
			return false;

		addr = clcs.Ref(index - 1);
		return true;
	}
	#endregion
	#region LuaFunc.cs
	private LuaUpValue F_FindUpval(int level)
	{
#if DEBUG_FIND_UPVALUE
		ULDebug.Log("[F_FindUpval] >>>>>>>>>>>>>>>>>>>> level:" + level);
#endif
		var prev = UpvalHead;
		var node = prev.Next;
		while (node != null)
		{
#if DEBUG_FIND_UPVALUE
			ULDebug.Log("[F_FindUpval] >>>>>>>>>>>>>>>>>>>> upval.V:" + upval.V );
#endif
			if (node.StackIndex < level)
				break;
			if (node.StackIndex == level) 
				return node;
			prev = node;
			node = node.Next;
		}

		// Not found: create a new one
		//Util.Assert(node == null);
		var ret = new LuaUpValue(this, level);
		//var oldNext = G.UpvalHead.Next;
		//G.UpvalHead.Next = ret;
		//ret.Next = oldNext;
		prev.Next = ret;
		ret.Next = node;

#if DEBUG_FIND_UPVALUE
		ULDebug.Log("[F_FindUpval] >>>>>>>>>>>>>>>>>>>> create new one:" + ret.V);
#endif

		return ret;
	}

	private void F_Close(int level)
	{
		var upval = UpvalHead.Next;
		while (upval != null)
		{
			if (upval.StackIndex < level) break;
			
			upval.Value.SetObj(upval.StkId);
			upval.StackIndex = -1;

			upval = upval.Next;
			UpvalHead.Next = upval;
		}
	}

	private static string? F_GetLocalName(LuaProto proto, int localNumber, int pc)
	{
		for (var i = 0;
		     i < proto.LocVars.Count && proto.LocVars[i].StartPc <= pc;
		     ++i)
		{
			if (pc < proto.LocVars[i].EndPc) // Is variable active?
			{ 
				--localNumber;
				if (localNumber == 0)
					return proto.LocVars[i].VarName;
			}
		}

		return null;
	}
	#endregion
	#region Do.cs
	
	private static readonly ThreadLocal<LuaRuntimeException> _YieldExcp = 
		new (() => new LuaRuntimeException(ThreadStatus.LUA_YIELD, "YIELD"));
	
	[DoesNotReturn]
	private static void D_Throw(ThreadStatus errCode, string msg) => 
		throw new LuaRuntimeException(errCode, msg);

	private ThreadStatus D_RawRunProtected<T>(
		PFuncDelegate<T> func, ref T ud) where T: struct
	{
		var oldNumCSharpCalls = NumCSharpCalls;
		var res = ThreadStatus.LUA_OK;
		try
		{
			func(ref ud);
		}
		catch (LuaRuntimeException e)
		{
			NumCSharpCalls = oldNumCSharpCalls;
			if (e is LuaParserException)
				PushString(e.Message);
			res = e.ErrCode;
		}
		NumCSharpCalls = oldNumCSharpCalls;
		return res;
	}

	private void SetErrorObj(ThreadStatus errCode, int oldTop)
	{
		var old = Ref[oldTop];
		switch (errCode)
		{
			case ThreadStatus.LUA_ERRMEM: // Memory error?
				old.V.SetSValue("Not enough memory");
				break;

			case ThreadStatus.LUA_ERRERR:
				old.V.SetSValue("Error in error handling");
				break;

			default: // Error message on current top
				old.V.SetObj(Ref[TopIndex - 1]);
				break;
		}
		TopIndex = oldTop + 1;
	}

	private ThreadStatus D_PCall<T>(
		PFuncDelegate<T> func, ref T ud, int oldTopIndex, int errFunc) where T: struct
	{
		var oldCIIndex = CI.Index;
		var oldNumNonYieldable= NumNonYieldable;
		var oldErrFunc = ErrFunc;

		ErrFunc = errFunc;
		var status = D_RawRunProtected(func, ref ud);
		if (status != ThreadStatus.LUA_OK) // Error occurred?
		{
			F_Close(oldTopIndex);
			SetErrorObj(status, oldTopIndex);
			CI = BaseCI[oldCIIndex];
			NumNonYieldable = oldNumNonYieldable;
		}
		ErrFunc = oldErrFunc;
		return status;
	}

	private void D_Call(StkId func, int nResults, bool allowYield)
	{
		if (++NumCSharpCalls >= LuaLimits.LUAI_MAXCCALLS)
		{
			if (NumCSharpCalls == LuaLimits.LUAI_MAXCCALLS)
				G_RunError("CSharp Stack Overflow");
			else if (NumCSharpCalls >=
			         LuaLimits.LUAI_MAXCCALLS + (LuaLimits.LUAI_MAXCCALLS >> 3))
				D_Throw(ThreadStatus.LUA_ERRERR, "CSharp Stack Overflow");
		}
		if (!allowYield)
			NumNonYieldable++;
		if (!D_PreCall(func, nResults)) // Is a Lua function ?
			V_Execute();
		if (!allowYield)
			NumNonYieldable--;
		NumCSharpCalls--;
	}

	/// <summary>
	/// Return true if function has been executed
	/// </summary>
	private bool D_PreCall(StkId func, int nResults)
	{
		// prepare for Lua call

#if DEBUG_D_PRE_CALL
		ULDebug.Log("============================ D_PreCall func:" + func);
#endif

		var funcIndex = Index(func);
		if (!func.V.TtIsFunction()) 
		{
			// not a function, retry with 'function' tag method
			TryFuncTM(ref func);

			// now it must be a function
			return D_PreCall(func, nResults);
		}

		if (func.V.ClIsLuaClosure()) 
		{
			var cl = func.V.ClLValue();
			Util.Assert(cl != null);
			var p = cl!.Proto;

			D_CheckStack(p.MaxStackSize + p.NumParams);

				// Complete the parameters
			var n = (TopIndex - funcIndex) - 1;
			for (; n < p.NumParams; ++n)
			{
				IncTop().V.SetNilValue();
			}

			var stackBase = !p.IsVarArg 
				? (funcIndex + 1) : AdjustVarargs(p, n);
				
			CI = ExtendCI();
			CI.NumResults = nResults;
			CI.FuncIndex = funcIndex;
			CI.BaseIndex = stackBase;
			CI.TopIndex  = stackBase + p.MaxStackSize;
			Util.Assert(CI.TopIndex <= StackLast);
			CI.SavedPc = new InstructionPtr(p.Code, 0);
			CI.CallStatus = CallStatus.CIST_LUA;

			TopIndex = CI.TopIndex;

			return false;
		}

		if (func.V.ClIsCsClosure()) 
		{
			var cscl = func.V.ClCsValue();
			Util.Assert(cscl != null);

			D_CheckStack(LuaDef.LUA_MINSTACK);

			CI = ExtendCI();
			CI.NumResults = nResults;
			CI.FuncIndex = funcIndex;
			CI.TopIndex = TopIndex + LuaDef.LUA_MINSTACK;
			CI.CallStatus = CallStatus.CIST_NONE;

			// Do the actual call
			var n = cscl!.F(this);
				
			// Poscall
			D_PosCall(TopIndex - n);

			return true;
		}

		throw new NotImplementedException();
	}

	private int D_PosCall(int firstResultIndex)
	{
		// TODO: hook
		// be careful: CI may be changed after hook

		var resIndex = CI.FuncIndex;
		var wanted = CI.NumResults;

#if DEBUG_D_POS_CALL
		ULDebug.Log("[D] ==== PosCall enter");
		ULDebug.Log("[D] ==== PosCall res:" + res);
		ULDebug.Log("[D] ==== PosCall wanted:" + wanted);
#endif

		CI = BaseCI[CI.Index - 1];

		var i = wanted;
		for (; i != 0 && firstResultIndex < TopIndex; --i)
		{
#if DEBUG_D_POS_CALL
			ULDebug.Log("[D] ==== PosCall assign lhs res:" + res);
			ULDebug.Log("[D] ==== PosCall assign rhs firstResult:" + firstResult);
#endif
			Ref[resIndex++].V.SetObj(Ref[firstResultIndex++]);
		}
		while (i-- > 0)
		{
#if DEBUG_D_POS_CALL
			ULDebug.Log("[D] ==== PosCall new LuaNil()");
#endif
			Ref[resIndex++].V.SetNilValue();
		}
		TopIndex = resIndex;
#if DEBUG_D_POS_CALL
		ULDebug.Log("[D] ==== PosCall return " + (wanted - LuaDef.LUA_MULTRET));
#endif
		return (wanted - LuaDef.LUA_MULTRET);
	}

	private CallInfo ExtendCI()
	{
		var newIndex = CI.Index + 1;
		if (newIndex < BaseCI.Length) return BaseCI[newIndex];

		var newLength = BaseCI.Length * 2;
		var newBaseCI = new CallInfo[newLength];
		var i = 0;
		while (i < BaseCI.Length) 
		{
			newBaseCI[i] = BaseCI[i];
			++i;
		}
		while (i < newLength) 
		{
			var newCI = new CallInfo();
			newBaseCI[i] = newCI;
			newCI.Index = i;
			++i;
		}
		BaseCI = newBaseCI;
		CI = newBaseCI[CI.Index];
		return newBaseCI[newIndex];
	}

	private int AdjustVarargs(LuaProto p, int actual)
	{
		// In the case with '...' (variadic arguments)
		// Before invocation: func (base)fixed-p1 fixed-p2 var-p1 var-p2 top
		// After invocation: func nil            nil      var-p1 var-p2 (base)fixed-p1 fixed-p2 (reserved...) top
		//
		// In the case without '...' (no variadic arguments)
		// func (base)fixed-p1 fixed-p2 (reserved...) top

		var NumFixArgs = p.NumParams;
		Util.Assert(actual >= NumFixArgs, "AdjustVarargs (actual >= NumFixArgs) is false");

		var fixedArg = TopIndex - actual; 	// first fixed argument
		var stackBase = TopIndex;		// final position of first argument
		for (var i = stackBase; i < stackBase + NumFixArgs; ++i)
		{
			Ref[i].V.SetObj(Ref[fixedArg]);
			Ref[fixedArg++].V.SetNilValue();
		}
		TopIndex = stackBase + NumFixArgs;
		return stackBase;
	}

	private bool TryFuncTM(ref StkId func)
	{
		var val = StkId.Nil;
		if (!T_TryGetTMByObj(func, TMS.TM_CALL, out val)
				|| !val.V.TtIsFunction())
			G_TypeError(func, "call");

		// Open a hole inside the stack at 'func'
		var funcIndex = Index(func);
		for (var i = TopIndex; i > funcIndex; --i) 
			Stack[i].SetObj(Ref[i - 1]);

		IncrTop();
		func = Ref[funcIndex];
		func.Set(val);
		return true;
	}

	private void D_CheckStack(int n)
	{
		if (StackLast - TopIndex <= n)
			D_GrowStack(n);
		// TODO: FOR DEBUGGING
		// else
		// 	CondMoveStack();
	}

	// some space for error handling
	private const int ERRORSTACKSIZE = LuaConf.LUAI_MAXSTACK + 200;

	private void D_GrowStack(int n)
	{
		var size = Stack.Length;
		if (size > LuaConf.LUAI_MAXSTACK)
			D_Throw(ThreadStatus.LUA_ERRERR, "Stack Overflow");

		var needed = TopIndex + n + LuaDef.EXTRA_STACK;
		var newSize = 2 * size;
		if (newSize > LuaConf.LUAI_MAXSTACK) 
			newSize = LuaConf.LUAI_MAXSTACK;
		if (newSize < needed) 
			newSize = needed;
		if (newSize > LuaConf.LUAI_MAXSTACK)
		{
			D_ReallocStack(ERRORSTACKSIZE);
			G_RunError("Stack Overflow");
		}
		else
		{
			D_ReallocStack(newSize);
		}
	}

	private void D_ReallocStack(int size)
	{
		Util.Assert(size is <= LuaConf.LUAI_MAXSTACK or ERRORSTACKSIZE);
		var newStack = new TValue[size];
		var i = 0;
		for (; i < Stack.Length; ++i) 
		{
			newStack[i] = Stack[i];
		}
		for (; i < size; ++i) 
		{
			newStack[i].SetNilValue();
		}
		Stack = newStack;
		StackLast = size - LuaDef.EXTRA_STACK;
	}
	#endregion
	#region LuaAuxLib.cs
	public const int LEVELS1 = 12; // Size of the first part of the stack
	public const int LEVELS2 = 10; // Size of the second part of the stack

	public void L_Where(int level)
	{
		var ar = new LuaDebug();
		if (API.GetStack(ar, level)) // Check function at level
		{
			GetInfo(ar, "Sl"); // Get info about it
			if (ar.CurrentLine > 0) // Is there info?
			{
				API.PushString($"{ar.ShortSrc}:{ar.CurrentLine}: ");
				return;
			}
		}
		API.PushString(""); // else, no information available...
	}

	public int L_Error(string fmt, params object[] args)
	{
		L_Where(1);
		API.PushString(string.Format(fmt, args));
		API.Concat(2);
		return API.Error();
	}

	public void L_CheckStack(int size, string msg)
	{
		// Keep some extra space to run error routines, if needed
		if (!API.CheckStack(size + LuaDef.LUA_MINSTACK)) 
		{
			if (msg != null)
				L_Error($"stack overflow ({msg})");
			else
				L_Error("stack overflow");
		}
	}

	public void L_CheckAny(int nArg)
	{
		if (API.Type(nArg) == LuaType.LUA_TNONE)
			L_ArgError(nArg, "Value expected");
	}

	public double L_CheckNumber(int nArg)
	{
		var d = API.ToNumberX(nArg, out var isNum);
		if (!isNum) TagError(nArg, LuaType.LUA_TNUMBER);
		return d;
	}

	public ulong L_CheckUInt64(int nArg)
	{
		var v = API.ToUInt64X(nArg, out var isNum);
		if (!isNum) TagError(nArg, LuaType.LUA_TUINT64);
		return v;
	}

	public int L_CheckInteger(int nArg)
	{
		var d = API.ToIntegerX(nArg, out var isNum);
		if (!isNum) TagError(nArg, LuaType.LUA_TNUMBER);
		return d;
	}

	public string L_CheckString(int nArg)
	{
		var s = API.ToString(nArg);
		if (s == null) TagError(nArg, LuaType.LUA_TSTRING);
		return s!;
	}

	public uint L_CheckUnsigned(int nArg)
	{
		var d = API.ToUnsignedX(nArg, out var isNum);
		if (!isNum) TagError(nArg, LuaType.LUA_TNUMBER);
		return d;
	}

	public T L_Opt<T>(Func<int,T> f, int n, T def)
	{
		var t = API.Type(n);
		if (t is LuaType.LUA_TNONE or LuaType.LUA_TNIL)
			return def;
		return f(n);
	}

	public int L_OptInt(int nArg, int def)
	{
		var t = API.Type(nArg);
		if (t is LuaType.LUA_TNONE or LuaType.LUA_TNIL)
			return def;
		return L_CheckInteger(nArg);
	}

	public string L_OptString(int nArg, string def)
	{
		var t = API.Type(nArg);
		if (t is LuaType.LUA_TNONE or LuaType.LUA_TNIL)
			return def;
		return L_CheckString(nArg);
	}

	private int TypeError(int index, string typeName)
	{
		var msg = $"{typeName} expected, got {L_TypeName(index)}";
		API.PushString(msg);
		return L_ArgError(index, msg);
	}

	private void TagError(int index, LuaType t) => 
		TypeError(index, API.TypeName(t));

	public void L_CheckType(int index, LuaType t)
	{
		if (API.Type(index) != t) TagError(index, t);
	}

	public void L_ArgCheck(bool cond, int nArg, string extraMsg)
	{
		if (!cond) L_ArgError(nArg, extraMsg);
	}

	public int L_ArgError(int nArg, string extraMsg)
	{
		var ar = new LuaDebug();
		if (!API.GetStack(ar, 0)) // no stack frame ?
			return L_Error("Bad argument {0} ({1})", nArg, extraMsg);

		GetInfo(ar, "n");
		if (ar.NameWhat == "method")
		{
			nArg--; // Do not count 'self'
			if (nArg == 0) // Error is in the self argument itself?
				return L_Error("Calling '{0}' on bad self", ar.Name);
		}
		if (ar.Name == null)
			ar.Name = PushGlobalFuncName(ar) ? API.ToString(-1) : "?";
		return L_Error("Bad argument {0} to '{1}' ({2})",
			nArg, ar.Name, extraMsg);
	}

	public string L_TypeName(int index) => API.TypeName(API.Type(index));

	public bool L_GetMetaField(int obj, string name)
	{
		if (!API.GetMetaTable(obj)) // No metatable?
			return false;
		API.PushString(name);
		API.RawGet(-2);
		if (API.IsNil(-1))
		{
			API.Pop(2);
			return false;
		}

		API.Remove(-2);
		return true;
	}

	public bool L_CallMeta(int obj, string name)
	{
		obj = AbsIndex(obj);
		if (!L_GetMetaField(obj, name)) // No metafield?
			return false;

		PushValue(obj);
		Call(1, 1);
		return true;
	}

	private void PushFuncName(LuaDebug ar)
	{
		if (ar.NameWhat.Length > 0 && ar.NameWhat[0] != '\0') // Is there a name?
			API.PushString($"function '{ar.Name}'");
		else if (ar.What.Length > 0 && ar.What[0] == 'm') // Main?
			API.PushString("main chunk");
		else if (ar.What.Length > 0 && ar.What[0] == 'C')
		{
			if (PushGlobalFuncName(ar))
			{
				API.PushString($"function '{API.ToString(-1)}'");
				API.Remove(-2); // Remove name
			}
			else
				API.PushString("?");
		}
		else
			API.PushString($"function <{ar.ShortSrc}:{ar.LineDefined}>");
	}

	private int CountLevels()
	{
		var _ = 0;
		var li = 1;
		var le = 1;
		// Find an upper bound
		while (GetStack(ref _, le))
		{
			li = le;
			le *= 2;
		}
		// Do a binary search
		while (li < le)
		{
			var m = (li + le) / 2;
			if (GetStack(ref _, m))
				li = m + 1;
			else
				le = m;
		}
		return le - 1;
	}

	public void L_Traceback(
		ILuaState otherLua, string? msg = null, int level = 0)
	{
		L_DoTraceback(otherLua, msg, level);
	}

	public string L_DoTraceback(
		ILuaState otherLua, string? msg = null, int level = 0)
	{
		var oLua = (LuaState)otherLua;
		var ar = new LuaDebug();
		var top = API.GetTop();
		var numLevels = oLua.CountLevels();
		var mark = (numLevels > LEVELS1 + LEVELS2) ? LEVELS1 : 0;
		if (msg != null) API.PushString($"{msg}\n");
		API.PushString("stack traceback:");
		while (otherLua.GetStack(ar, level++))
		{
			if (level == mark) // Too many levels?
			{
				API.PushString("\n\t...");
				level = numLevels - LEVELS2; // And skip to last ones
			}
			else
			{
				oLua.GetInfo(ar, "Slnt");
				API.PushString($"\n\t{ar.ShortSrc}:");
				if (ar.CurrentLine > 0)
					API.PushString($"{ar.CurrentLine}:");
				API.PushString(" in ");
				PushFuncName(ar);
				if (ar.IsTailCall)
					API.PushString("\n\t(...tail calls...)");
				API.Concat(API.GetTop() - top);
			}
		}
		API.Concat(API.GetTop() - top);
		return ToString(-1);
	}

	public int L_Len(int index)
	{
		API.Len(index);

		var l = API.ToIntegerX(-1, out var isNum);
		if (!isNum) L_Error("Object length is not a number");
		API.Pop(1);
		return l;
	}

	public ThreadStatus L_LoadBuffer(string s, string name) => 
		L_LoadBufferX(s, name, null);

	public ThreadStatus L_LoadBufferX(string s, string name, string? mode)
	{
		var loadInfo = new StringLoadInfo(s);
		return Load(loadInfo, name, mode);
	}

	public ThreadStatus L_LoadBytes(byte[] bytes, string name)
	{
		var loadInfo = new BytesLoadInfo(bytes);
		return API.Load(loadInfo, name, null);
	}

	private static ThreadStatus ErrFile(string what, int fNameIdx) => 
		ThreadStatus.LUA_ERRFILE;

	public ThreadStatus L_LoadFile(string filename) => 
		L_LoadFileX(filename, null);

	public ThreadStatus L_LoadFileX(string? filename, string? mode)
	{
		ThreadStatus status;
		if (filename == null)
		{
			// Not implementing input from stdin for now
			throw new NotImplementedException();
		}

		var fNameIdx = API.GetTop() + 1;
		API.PushString("@" + filename);
		try
		{
			using var loadInfo = LuaFile.OpenFile(BaseFolder, filename);
			loadInfo.SkipComment();
			status = API.Load(loadInfo, API.ToString(-1), mode);
		}
		catch (LuaRuntimeException e)
		{
			API.PushString($"Cannot open {filename}: {e.Message}");
			return ThreadStatus.LUA_ERRFILE;
		}

		API.Remove(fNameIdx);
		return status;
	}

	public ThreadStatus L_LoadString(string s) => L_LoadBuffer(s, "???");

	public ThreadStatus L_DoString(string s)
	{
		var status = L_LoadString(s);
		return status != ThreadStatus.LUA_OK ? 
			status :
			API.PCall(0, LuaDef.LUA_MULTRET, 0);
	}

	public void Eval(string s)
	{
		var status = L_DoString(s);
		if (status == ThreadStatus.LUA_OK) return;

		var msg = ToString(-1);
		Pop(1);
		throw new LuaRuntimeException(status, msg);
	}
	
	public ThreadStatus L_LoadProto(string name, LuaProto proto)
	{
		var loadInfo = new ProtoLoadInfo(proto);
		return API.Load(loadInfo, name, null);
	}

	public ThreadStatus Compile(string name, string code)
	{
		var res = L_LoadString(code);
		if (res == ThreadStatus.LUA_OK) SetGlobal(name);
		return res;
	}

	public ThreadStatus L_DoFile(string filename)
	{
		var status = L_LoadFile(filename);
		if (status != ThreadStatus.LUA_OK)
			return status;
		return API.PCall(0, LuaDef.LUA_MULTRET, 0);
	}
	
	public ThreadStatus L_DoProto(LuaProto proto, string name = "???")
	{
		var status = L_LoadProto(name, proto);
		if (status != ThreadStatus.LUA_OK)
			return status;
		return API.PCall(0, LuaDef.LUA_MULTRET, 0);
	}

	public string L_Gsub(string src, string pattern, string rep)
	{
		var res = src.Replace(pattern, rep);
		API.PushString(res);
		return res;
	}
		
	public string L_ToString(int index)
	{
		if (L_CallMeta(index, "__tostring")) 
			return ToString(-1); // No metafield?

		switch (Type(index))
		{
			case LuaType.LUA_TNUMBER:
			case LuaType.LUA_TSTRING:
				API.PushValue(index);
				break;

			case LuaType.LUA_TBOOLEAN:
				API.PushString(API.ToBoolean(index) ? "true" : "false");
				break;

			case LuaType.LUA_TNIL:
				API.PushString("nil");
				break;

			default:
				API.PushString($"{L_TypeName(index)}: {ToObject(index).GetHashCode():X}");
				break;
		}
		return ToString(-1);
	}

	public void L_OpenLibs()
	{
		Span<NameFuncPair> define = 
		[
			LuaBaseLib.NameFuncPair,
			LuaBitLib.NameFuncPair,
			LuaCoroLib.NameFuncPair,
			LuaDebugLib.NameFuncPair,
			LuaEncLib.NameFuncPair,
			LuaFFILib.NameFuncPair,
			LuaIOLib.NameFuncPair,
			LuaMathLib.NameFuncPair,
			LuaOSLib.NameFuncPair,
			LuaPkgLib.NameFuncPair,
			LuaStrLib.NameFuncPair,
			LuaTableLib.NameFuncPair,
		];

		foreach (var t in define)
		{
			L_RequireF(t.Name, t.Func, true);
			Pop(1);
		}
	}
	
	public void L_OpenSafeLibs()
	{
		Span<NameFuncPair> define = 
		[
			LuaBaseLib.SafeNameFuncPair,
			//
			LuaBitLib.NameFuncPair,
			LuaCoroLib.NameFuncPair,
			LuaDebugLib.SafeNameFuncPair,
			LuaEncLib.NameFuncPair,
			//new(LuaFFILib.LIB_NAME,	LuaFFILib.OpenLib),
			//new(LuaIOLib.LIB_NAME,	LuaIOLib.OpenLib),
			LuaMathLib.NameFuncPair,
			LuaOSLib.SafeNameFuncPair,
			LuaPkgLib.NameFuncPair,
			LuaStrLib.SafeNameFuncPair,
			LuaTableLib.NameFuncPair,
		];

		foreach (var t in define)
		{
			L_RequireF(t.Name, t.Func, true);
			Pop(1);
		}
	}

	public void Open(NameFuncPair library, bool global = true)
	{
		L_RequireF(library.Name, library.Func, global);
		Pop(1);
	}

	public void L_RequireF(
		string moduleName, CSharpFunctionDelegate openFunc, bool global)
	{
		API.PushCSharpFunction(openFunc);
		API.PushString(moduleName);
		API.Call(1, 1);
		L_GetSubTable(LuaDef.LUA_REGISTRYINDEX, "_LOADED");
		API.PushValue(-2);
		API.SetField(-2, moduleName);
		API.Pop(1);
		if (global)
		{
			API.PushValue(-1);
			API.SetGlobal(moduleName);
		}
	}

	public int L_GetSubTable(int index, string fname)
	{
		API.GetField(index, fname);
		if (API.IsTable(-1))
			return 1;
		API.Pop(1);
		index = API.AbsIndex(index);
		API.NewTable();
		API.PushValue(-1);
		API.SetField(index, fname);
		return 0;
	}

	public void L_NewLibTable(ReadOnlySpan<NameFuncPair> define) => 
		CreateTable(0, define.Length);

	public void L_NewLib(ReadOnlySpan<NameFuncPair> define)
	{
		L_NewLibTable(define);
		L_SetFuncs(define, 0);
	}

	public void L_SetFuncs(ReadOnlySpan<NameFuncPair> list, int nup)
	{
		// TODO: Check Version
		L_CheckStack(nup, "Too many upvalues");
		foreach (var t in list)
		{
			for (var i = 0; i < nup; ++i)
				PushValue(-nup);
			PushCSharpClosure(t.Func, nup);
			SetField(-(nup + 2), t.Name);
		}
		Pop(nup);
	}

	private bool FindField(int objIndex, int level)
	{
		if (level == 0 || !IsTable(-1))
			return false; // Not found

		PushNil(); // Start 'next' loop
		while (API.Next(-2)) // for each pair in table
		{
			if (API.Type(-2) == LuaType.LUA_TSTRING) // ignore non-string keys
			{
				if (API.RawEqual(objIndex, -1)) // found object?
				{
					API.Pop(1); // remove value (but keep name)
					return true;
				}

				if (FindField(objIndex, level - 1)) // try recursively
				{
					API.Remove(-2); // remove table (but keep name)
					API.PushString(".");
					API.Insert(-2); // place '.' between the two names
					API.Concat(3);
					return true;
				}
			}
			API.Pop(1); // remove value
		}
		return false; // not found
	}

	private bool PushGlobalFuncName(LuaDebug ar)
	{
		var top = API.GetTop();
		GetInfo(ar, "f");
		API.PushGlobalTable();
		if (FindField(top + 1, 2))
		{
			API.Copy(-1, top + 1);
			API.Pop(2);
			return true;
		}

		API.SetTop(top); // remove function and global table
		return false;
	}

	private const int FreeList = 0;

	public int L_Ref(int t)
	{
		if (API.IsNil(-1))
		{
			API.Pop(1); // Remove from stack
			return LuaConstants.LUA_REFNIL; // `nil' has a unique fixed reference
		}

		t = API.AbsIndex(t);
		API.RawGetI(t, FreeList); // get first free element
		var reference = API.ToInteger(-1); // ref = t[freelist]
		API.Pop(1); // remove it from stack
		if (reference != 0) // any free element?
		{
			API.RawGetI(t, reference); // remove it from list
			API.RawSetI(t, FreeList); // t[freelist] = t[ref]
		}
		else // no free elements
			reference = API.RawLen(t) + 1; // get a new reference
		API.RawSetI(t, reference);
		return reference;
	}

	public void L_Unref(int t, int reference)
	{
		if (reference < 0) return;

		t = API.AbsIndex(t);
		API.RawGetI(t, FreeList);
		API.RawSetI(t, reference); // t[ref] = t[freelist]
		API.PushInteger(reference);
		API.RawSetI(t, FreeList); // t[freelist] = ref
	}
	#endregion
	#region LuaObject.cs
	private static double O_Arith(LuaOp op, double v1, double v2)
	{
		return op switch
		{
			LuaOp.LUA_OPADD => v1 + v2,
			LuaOp.LUA_OPSUB => v1 - v2,
			LuaOp.LUA_OPMUL => v1 * v2,
			LuaOp.LUA_OPDIV => v1 / v2,
			LuaOp.LUA_OPMOD => v1 - Math.Floor(v1 / v2) * v2,
			LuaOp.LUA_OPPOW => Math.Pow(v1, v2),
			LuaOp.LUA_OPUNM => -v1,
			_ => throw new NotImplementedException()
		};
	}

	private static bool IsFalse(StkId v)
	{
		if (v.V.TtIsNil())
			return true;
		if (v.V.TtIsBoolean() && v.V.BValue() == false)
			return true;
		return false;
	}

	private static bool ToString(ref TValue o) => 
		o.TtIsString() || V_ToString(ref o);

	private LuaLClosureValue? GetCurrentLuaFunc(CallInfo ci) => 
		ci.IsLua ? Stack[ci.FuncIndex].ClLValue() : null;

	private int GetCurrentLine(CallInfo ci)
	{
		Util.Assert(ci.IsLua);
		var cl = Stack[ci.FuncIndex].ClLValue();
		return cl.Proto.GetFuncLine(ci.CurrentPc);
	}
	#endregion
	#region VM.cs
	private const int MAXTAGLOOP = 100;

	private struct ExecuteEnvironment(LuaState L)
	{
		public List<TValue> 	K;
		public int 				Base;
		public Instruction 		I;

		public int RAIndex => Base + I.GETARG_A();
		public int RBIndex => Base + I.GETARG_B();
		
		public StkId RA => new (ref L.Stack[RAIndex]);

		public StkId RB => new (ref L.Stack[RBIndex]);

		public StkId RK(int x) => 
			Instruction.ISK(x) 
				? new StkId(ref CollectionsMarshal.AsSpan(K)[Instruction.INDEXK(x)])
				: new StkId(ref L.Stack[Base + x]);

		public StkId RKB => RK(I.GETARG_B());

		public StkId RKC => RK(I.GETARG_C());
	}

	private void V_Execute()
	{
		var ci = CI;
		newFrame:
		Util.Assert(ci == CI);
		var cl = Stack[ci.FuncIndex].ClLValue();
		var env = new ExecuteEnvironment(this)
		{ K = cl.Proto.K, Base = ci.BaseIndex };

#if DEBUG_NEW_FRAME
		ULDebug.Log("#### NEW FRAME #########################################################################");
		ULDebug.Log("## cl:" + cl);
		ULDebug.Log("## Base:" + env.Base);
		ULDebug.Log("########################################################################################");
#endif

		while (true)
		{
			var i = ci.SavedPc.ValueInc;
			env.I = i;

#if DEBUG_SRC_INFO
			int line = 0;
			string src = "";
			if (ci.IsLua) 
			{
				line = GetCurrentLine(ci);
				src = GetCurrentLuaFunc(ci).Proto.Source;
			}
#endif

			var raIdx = env.Base + i.GETARG_A();
			var ra = env.RA;

#if DEBUG_DUMP_INS_STACK
#if DEBUG_DUMP_INS_STACK_EX
			DumpStack(env.Base, i.ToString());
#else
			DumpStack(env.Base);
#endif
#endif
#if DEBUG_INSTRUCTION
			ULDebug.Log(System.DateTime.Now + " [VM] ======================================================================== Instruction: " + i
#if DEBUG_INSTRUCTION_WITH_STACK
			+ "\n" + DumpStackToString(env.Base.Index)
#endif
			);
#endif
#if DEBUG_RECORD_INS
			InstructionHistory.Enqueue(i);
			if (InstructionHistory.Count > 100)
				InstructionHistory.Dequeue();
#endif

			switch (i.GET_OPCODE())
			{
				case OpCode.OP_MOVE:
				{
					var rb = env.RB;

#if DEBUG_OP_MOVE
					ULDebug.Log("[VM] ==== OP_MOVE rb:" + rb);
					ULDebug.Log("[VM] ==== OP_MOVE ra:" + ra);
#endif
					ra.Set(rb);
					break;
				}

				case OpCode.OP_LOADK:
				{
					var rb = env.K[i.GETARG_Bx()];
					ra.Set(new StkId(ref rb));
					break;
				}

				case OpCode.OP_LOADKX:
				{
					Util.Assert(ci.SavedPc.Value.GET_OPCODE() == OpCode.OP_EXTRAARG);
					var rb = env.K[ci.SavedPc.ValueInc.GETARG_Ax()];
					ra.Set(new StkId(ref rb));
					break;
				}

				case OpCode.OP_LOADBOOL:
				{
					ra.V.SetBValue(i.GETARG_B() != 0);
					if (i.GETARG_C() != 0)
						ci.SavedPc.Index += 1; // Skip next instruction (if C)
					break;
				}

				case OpCode.OP_LOADNIL:
				{
					var b = i.GETARG_B();
					var index = raIdx;
					do { Stack[index++].SetNilValue(); } 
					while (b-- > 0);
					break;
				}

				case OpCode.OP_GETUPVAL:
				{
					var b = i.GETARG_B();
					ra.Set(cl.Upvals[b].StkId);
					var uv = cl.Upvals[b];
					var idx = env.Base + env.I.GETARG_A();
					uv.Index = idx; // Hack
#if DEBUG_OP_GETUPVAL
					// for (var j = 0; j < cl.Upvals.Length; ++j)
					// {
					//		ULDebug.Log("[VM] ==== GETUPVAL upval:" + cl.Upvals[j]);
					// }
					ULDebug.Log("[VM] ==== GETUPVAL b:" + b);
					ULDebug.Log("[VM] ==== GETUPVAL ra:" + ra);
#endif
					break;
				}

				case OpCode.OP_GETTABUP:
				{
					var b = i.GETARG_B();
					var key = env.RKC;
					V_GetTable(cl.Upvals[b].StkId, key, ra);
#if DEBUG_OP_GETTABUP
					ULDebug.Log("[VM] ==== OP_GETTABUP key:" + key);
					ULDebug.Log("[VM] ==== OP_GETTABUP val:" + ra);
#endif
					env.Base = ci.BaseIndex;
					break;
				}

				case OpCode.OP_GETTABLE:
				{
					var tbl = env.RB;
					var key = env.RKC;
					V_GetTable(tbl, key, ra);
#if DEBUG_OP_GETTABLE
					ULDebug.Log("[VM] ==== OP_GETTABLE key:"+key.ToString());
					ULDebug.Log("[VM] ==== OP_GETTABLE val:"+val.ToString());
#endif
					break;
				}

				case OpCode.OP_SETTABUP:
				{
					var a = i.GETARG_A();
					var key = env.RKB;
					var val = env.RKC;
					V_SetTable(cl.Upvals[a].StkId, key, val);
#if DEBUG_OP_SETTABUP
					ULDebug.Log("[VM] ==== OP_SETTABUP key:" + key.Value);
					ULDebug.Log("[VM] ==== OP_SETTABUP val:" + val.Value);
#endif
					env.Base = ci.BaseIndex;
					break;
				}

				case OpCode.OP_SETUPVAL:
				{
					var b = i.GETARG_B();
					cl.Upvals[b].StkId.Set(ra);
#if DEBUG_OP_SETUPVAL
					ULDebug.Log("[VM] ==== SETUPVAL b:" + b);
					ULDebug.Log("[VM] ==== SETUPVAL ra:" + ra);
#endif
					break;
				}

				case OpCode.OP_SETTABLE:
				{
					var key = env.RKB;
					var val = env.RKC;
#if DEBUG_OP_SETTABLE
					ULDebug.Log("[VM] ==== OP_SETTABLE key:" + key.ToString());
					ULDebug.Log("[VM] ==== OP_SETTABLE val:" + val.ToString());
#endif
					V_SetTable(ra, key, val);
					break;
				}

				case OpCode.OP_NEWTABLE:
				{
					var b = i.GETARG_B();
					var c = i.GETARG_C();
					var tbl = new LuaTable(this);
					ra.V.SetHValue(tbl);
					if (b > 0 || c > 0) tbl.Resize(b, c);
					break;
				}

				case OpCode.OP_SELF:
				{
					// OP_SELF put function referenced by a table on ra
					// and the table on ra+1
					//
					// RB:  table
					// RKC: key
					var ra1 = Ref[raIdx + 1];
					var rb  = env.RB;
					ra1.Set(rb);
					V_GetTable(rb, env.RKC, ra);
					env.Base = ci.BaseIndex;
					break;
				}

				case OpCode.OP_ADD:
				{
					var rkb = env.RKB;
					var rkc = env.RKC;
					if (rkb.V.TtIsNumber() && rkc.V.TtIsNumber())
						ra.V.SetNValue(rkb.V.NValue + rkc.V.NValue);
					else
						V_Arith(ra, rkb, rkc, TMS.TM_ADD);

					env.Base = ci.BaseIndex;
					break;
				}

				case OpCode.OP_SUB:
				{
					var rkb = env.RKB;
					var rkc = env.RKC;
					if (rkb.V.TtIsNumber() && rkc.V.TtIsNumber())
						ra.V.SetNValue(rkb.V.NValue - rkc.V.NValue);
					else
						V_Arith(ra, rkb, rkc, TMS.TM_SUB);
					env.Base = ci.BaseIndex;
					break;
				}

				case OpCode.OP_MUL:
				{
					var rkb = env.RKB;
					var rkc = env.RKC;
					if (rkb.V.TtIsNumber() && rkc.V.TtIsNumber())
						ra.V.SetNValue(rkb.V.NValue * rkc.V.NValue);
					else
						V_Arith(ra, rkb, rkc, TMS.TM_MUL);
					env.Base = ci.BaseIndex;
					break;
				}

				case OpCode.OP_DIV:
				{
					var rkb = env.RKB;
					var rkc = env.RKC;
					if (rkb.V.TtIsNumber() && rkc.V.TtIsNumber())
						ra.V.SetNValue(rkb.V.NValue / rkc.V.NValue);
					else
						V_Arith(ra, rkb, rkc, TMS.TM_DIV);
					env.Base = ci.BaseIndex;
					break;
				}

				case OpCode.OP_MOD:
				{
					var rkb = env.RKB;
					var rkc = env.RKC;
					if (rkb.V.TtIsNumber() && rkc.V.TtIsNumber())
					{
						var v1 = rkb.V.NValue;
						var v2 = rkc.V.NValue;
						ra.V.SetNValue(v1 - Math.Floor(v1 / v2) * v2);
					}
					else
						V_Arith(ra, rkb, rkc, TMS.TM_MOD);

					env.Base = ci.BaseIndex;
					break;
				}

				case OpCode.OP_POW:
				{
					var rkb = env.RKB;
					var rkc = env.RKC;
					if (rkb.V.TtIsNumber() && rkc.V.TtIsNumber())
						ra.V.SetNValue(Math.Pow(rkb.V.NValue, rkc.V.NValue));
					else
						V_Arith(ra, rkb, rkc, TMS.TM_POW);
					env.Base = ci.BaseIndex;
					break;
				}

				case OpCode.OP_UNM:
				{
					var rb = env.RB;
					if (rb.V.TtIsNumber())
						ra.V.SetNValue(-rb.V.NValue);
					else
					{
						V_Arith(ra, rb, rb, TMS.TM_UNM);
						env.Base = ci.BaseIndex;
					}
					break;
				}

				case OpCode.OP_NOT:
				{
					var rb = env.RB;
					ra.V.SetBValue(IsFalse(rb));
					break;
				}

				case OpCode.OP_LEN:
				{
					V_ObjLen(ra, env.RB);
					env.Base = ci.BaseIndex;
					break;
				}

				case OpCode.OP_CONCAT:
				{
					var b = i.GETARG_B();
					var c = i.GETARG_C();
					TopIndex = env.Base + c + 1;
					V_Concat(c - b + 1);
					env.Base = ci.BaseIndex;

					raIdx = env.Base + i.GETARG_A();
					ra = env.RA; // 'V_Concat' may invoke TMs and move the stack
					var rb = env.RB;
					ra.Set(rb);

					TopIndex = ci.TopIndex; // Restore top
					break;
				}

				case OpCode.OP_JMP:
				{
					V_DoJump(ci, i, 0);
					break;
				}

				case OpCode.OP_EQ:
				{
					var lhs = env.RKB;
					var rhs = env.RKC;
					var expectEq = i.GETARG_A() != 0;
#if DEBUG_OP_EQ
					ULDebug.Log("[VM] ==== OP_EQ lhs:" + lhs);
					ULDebug.Log("[VM] ==== OP_EQ rhs:" + rhs);
					ULDebug.Log("[VM] ==== OP_EQ expectEq:" + expectEq);
					ULDebug.Log("[VM] ==== OP_EQ (lhs.V == rhs.V):" + (lhs.V == rhs.V));
#endif
					if((lhs.V == rhs.V) != expectEq)
					{
						ci.SavedPc.Index += 1; // skip next jump instruction
					}
					else
					{
						V_DoNextJump(ci);
					}
					env.Base = ci.BaseIndex;
					break;
				}

				case OpCode.OP_LT:
				{
					var expectCmpResult = i.GETARG_A() != 0;
					if (V_LessThan(env.RKB, env.RKC) != expectCmpResult)
						ci.SavedPc.Index += 1;
					else
						V_DoNextJump(ci);
					env.Base = ci.BaseIndex;
					break;
				}

				case OpCode.OP_LE:
				{
					var expectCmpResult = i.GETARG_A() != 0;
					if (V_LessEqual(env.RKB, env.RKC) != expectCmpResult)
						ci.SavedPc.Index += 1;
					else
						V_DoNextJump(ci);
					env.Base = ci.BaseIndex;
					break;
				}

				case OpCode.OP_TEST:
				{
					if ((i.GETARG_C() != 0) ? IsFalse(ra) : !IsFalse(ra))
					{
						ci.SavedPc.Index += 1;
					}
					else V_DoNextJump(ci);

					env.Base = ci.BaseIndex;
					break;
				}

				case OpCode.OP_TESTSET:
				{
					var rb = env.RB;
					if ((i.GETARG_C() != 0) ? IsFalse(rb) : !IsFalse(rb))
					{
						ci.SavedPc.Index += 1;
					}
					else
					{
						ra.Set(rb);
						V_DoNextJump(ci);
					}
					env.Base = ci.BaseIndex;
					break;
				}

				case OpCode.OP_CALL:
				{
					var b = i.GETARG_B();
					var nResults = i.GETARG_C() - 1;
					if (b != 0) 
					{
						TopIndex = raIdx + b; // else previous instruction set top
					}
					if (D_PreCall(ra, nResults)) // C# function?
					{
						if (nResults >= 0)
						{
							TopIndex = ci.TopIndex;
						}
						env.Base = ci.BaseIndex;
					}
					else // Lua function
					{ 
						ci = CI;
						ci.CallStatus |= CallStatus.CIST_REENTRY;
						goto newFrame;
					}
					break;
				}

				case OpCode.OP_TAILCALL:
				{
					var b = i.GETARG_B();
					if (b != 0) 
					{
						TopIndex = raIdx + b; // else previous instruction set top
					}
						
					Util.Assert(i.GETARG_C() - 1 == LuaDef.LUA_MULTRET);

					var called = D_PreCall(ra, LuaDef.LUA_MULTRET);

					// C# function ?
					if (called)
						env.Base = ci.BaseIndex;

					// LuaFunction
					else
					{
						var nci = CI;				// called frame
						var oci = BaseCI[CI.Index - 1]; // caller frame
						var nfunc = Ref[nci.FuncIndex];// called function
						var ofunc = Ref[oci.FuncIndex];// caller function
						var ncl = nfunc.V.ClLValue();
						var ocl = ofunc.V.ClLValue();

						// Last stack slot filled by 'precall'
						var lim = nci.BaseIndex + ncl.Proto.NumParams;

						if (cl.Proto.P.Count > 0) F_Close(env.Base);

						// Move new frame into old one
						var nIndex = nci.FuncIndex;
						var oIndex = oci.FuncIndex;
						while (nIndex < lim) 
							Stack[oIndex++].SetObj(Ref[nIndex++]);

						oci.BaseIndex = oIndex + (nci.BaseIndex - nIndex);
						oci.TopIndex = oIndex + (TopIndex - nIndex);
						TopIndex = oci.TopIndex;
						oci.SavedPc = nci.SavedPc;
						oci.CallStatus |= CallStatus.CIST_TAIL;
						ci = CI = oci;

						ocl = ofunc.V.ClLValue();
						Util.Assert(TopIndex == oci.BaseIndex + ocl.Proto.MaxStackSize);

						goto newFrame;
					}

					break;
				}

				case OpCode.OP_RETURN:
				{
					var b = i.GETARG_B();
					if (b != 0)
					{
						TopIndex = raIdx + b - 1;
					}
					if (cl.Proto.P.Count > 0) F_Close(env.Base);
					b = D_PosCall(raIdx);
					if ((ci.CallStatus & CallStatus.CIST_REENTRY) == 0)
						return;
					ci = CI;
					if (b != 0)
					{
						TopIndex = ci.TopIndex;
					}
					goto newFrame;
				}

				case OpCode.OP_FORLOOP:
				{
					var ra1 = Ref[raIdx + 1];
					var ra2 = Ref[raIdx + 2];
					var ra3 = Ref[raIdx + 3];
						
					var step 	= ra2.V.NValue;
					var idx 	= ra.V.NValue + step;	// increment index
					var limit = ra1.V.NValue;

					if ((0 < step) ? idx <= limit : limit <= idx)
					{
						ci.SavedPc.Index += i.GETARG_sBx(); // jump back
						ra.V.SetNValue(idx); // updateinternal index...
						ra3.V.SetNValue(idx);// ... and external index
					}

					break;
				}

				case OpCode.OP_FORPREP:
				{
					var init = new TValue();
					var limit = new TValue();
					var step = new TValue();

					var ra1 = Ref[raIdx + 1];
					var ra2 = Ref[raIdx + 2];

					// WHY: Why limit is not used ?

					if (!V_ToNumber(ra, new StkId(ref init)))
						G_RunError("'for' initial value must be a number");
					if (!V_ToNumber(ra1, new StkId(ref limit)))
						G_RunError("'for' limit must be a number");
					if (!V_ToNumber(ra2, new StkId(ref step)))
						G_RunError("'for' step must be a number");

					// Replace values in case they were strings initially
					ra1.V.SetObj(new StkId(ref limit));
					ra2.V.SetObj(new StkId(ref step));
					
					ra.V.SetNValue(init.NValue - step.NValue);
					ci.SavedPc.Index += i.GETARG_sBx();
					break;
				}

				case OpCode.OP_TFORCALL:
				{
					var rai = raIdx;
					var cbi = raIdx + 3;
					Stack[cbi + 2].SetObj(Ref[rai + 2]);
					Stack[cbi + 1].SetObj(Ref[rai + 1]);
					Stack[cbi].SetObj(Ref[rai]);

					var callBase = Ref[cbi];
					TopIndex = cbi + 3; // func. +2 args (state and index)

					D_Call(callBase, i.GETARG_C(), true);

					env.Base = ci.BaseIndex;
					TopIndex = ci.TopIndex;
					
					i = ci.SavedPc.ValueInc;	// go to next instruction
					env.I = i;
					raIdx = env.Base + i.GETARG_A();
					ra = env.RA;

#if ENABLE_DUMP_STACK
                    DumpStack(env.Base);
#endif
#if DEBUG_INSTRUCTION
					ULDebug.Log("[VM] ============================================================ OP_TFORCALL Instruction: " + i);
#endif
					Util.Assert(i.GET_OPCODE() == OpCode.OP_TFORLOOP);
					goto l_tforloop;
				}

				case OpCode.OP_TFORLOOP:
					l_tforloop:
				{
					var ra1 = Ref[raIdx + 1];
					if (!ra1.V.TtIsNil())	// continue loop?
					{
						ra.V.SetObj(ra1);
						ci.SavedPc += i.GETARG_sBx();
					}
					break;
				}

				// sets the values for a range of array elements in a table(RA)
				// RA -> table
				// RB -> number of elements to set
				// C  -> encodes the block number of the table to be initialized
				// the values used to initialize the table are located in
				//   R(A+1), R(A+2) ...
				case OpCode.OP_SETLIST:
				{
					var n = i.GETARG_B();
					var c = i.GETARG_C();
					if (n == 0) n = (TopIndex - raIdx) - 1;
					if (c == 0)
					{
						Util.Assert(ci.SavedPc.Value.GET_OPCODE() == OpCode.OP_EXTRAARG);
						c = ci.SavedPc.ValueInc.GETARG_Ax();
					}

					var tbl = ra.V.HValue();
					Util.Assert(tbl != null);

					var last = ((c - 1) * LuaDef.LFIELDS_PER_FLUSH) + n;
					var rai = raIdx;
					for (; n > 0; --n) tbl!.SetInt(last--, Ref[rai + n]);
#if DEBUG_OP_SETLIST
					ULDebug.Log("[VM] ==== OP_SETLIST ci.Top:" + ci.Top.Index);
					ULDebug.Log("[VM] ==== OP_SETLIST Top:" + Top.Index);
#endif
					TopIndex = ci.TopIndex; // correct top (in case of previous open call)
					break;
				}

				case OpCode.OP_CLOSURE:
				{
					var p = cl.Proto.P[i.GETARG_Bx()];
					V_PushClosure(p, cl.Upvals, env.Base, ref ra.V);
#if DEBUG_OP_CLOSURE
					ULDebug.Log("OP_CLOSURE:" + ra.Value);
					var racl = ra.Value as LuaLClosure;
					if (racl != null)
					{
						for(int ii = 0; ii < racl.Upvals.Count; ++ii)
						{
							ULDebug.Log(ii + " ) " + racl.Upvals[ii]);
						}
					}
#endif
					break;
				}

				//
				// VARARG implements the vararg operator '...' in expressions.
				// VARARG copies B-1 parameters into a number of registers
				// starting from R(A), padding with nils if there aren't enough values.
				// If B is 0, VARARG copies as many values as it can based on
				// the number of parameters passed.
				// If a fixed number of values is required, B is a value greater than 1.
				// If any number of values is required, B is 0.
				//
				case OpCode.OP_VARARG:
				{
					var b = i.GETARG_B() - 1;
					var n = (env.Base - ci.FuncIndex) - cl.Proto.NumParams - 1;
					if (b < 0) // B == 0?
					{
						b = n;
						D_CheckStack(n);
						raIdx = env.Base + i.GETARG_A(); // previous call may change the stack
						ra = env.RA; 

						TopIndex = raIdx + n;
					}

					var p = raIdx;
					var q = env.Base - n;
					for (var j = 0; j < b; ++j) 
					{
						if (j < n)
							Stack[p++].SetObj(Ref[q++]);
						else
							Stack[p++].SetNilValue();
					}
					break;
				}

				case OpCode.OP_EXTRAARG:
				{
					Util.Assert(false);
					V_NotImplemented(i);
					break;
				}

				default:
					V_NotImplemented(i);
					break;
			}
		}
	}

	private static void V_NotImplemented(Instruction i)
	{
		ULDebug.LogError("[VM] ==================================== Not Implemented Instruction: " + i);
		// throw new NotImplementedException();
	}

	private static bool TryFastTM(LuaTable? et, TMS tm, out StkId val)
	{
		val = StkId.Nil;
		if (et == null) return false;

		if ((et.NoTagMethodFlags & (1u << (int)tm)) != 0u)
			return false;

		return T_TryGetTM(et, tm, out val);
	}

	private void V_GetTable(StkId t, StkId key, StkId val)
	{
		for (var loop = 0; loop < MAXTAGLOOP; ++loop) 
		{
			StkId tmObj;
			if (t.V.TtIsTable()) 
			{
				var tbl = t.V.HValue();
				tbl.TryGet(key, out var res);
				if (!res.V.TtIsNil()) 
				{
					val.V.SetObj(res);
					return;
				}
				
				if (!TryFastTM(tbl.MetaTable, TMS.TM_INDEX, out tmObj)) 
				{
					val.V.SetObj(res);
					return;
				}

				// else will try the tag method
			}
			else 
			{
				if (!T_TryGetTMByObj(t, TMS.TM_INDEX, out tmObj) || tmObj.V.TtIsNil())
					G_SimpleTypeError(t, "index");
			}

			if (tmObj.V.TtIsFunction()) 
			{
				CallTM(tmObj, t, key, val, true);
				return;
			}

			t = tmObj;
		}
		G_RunError("Loop in gettable");
	}

	private void V_SetTable(StkId t, StkId key, StkId val)
	{
		for (var loop = 0; loop < MAXTAGLOOP; ++loop)
		{
			StkId tmObj;
			if (t.V.TtIsTable()) 
			{
				var tbl = t.V.HValue();
				tbl.TryGet(key, out var oldVal);
				if (!oldVal.V.TtIsNil())
				{
					tbl.Set(key, val);
					return;
				}

				// check meta method
				if (!TryFastTM(tbl.MetaTable, TMS.TM_NEWINDEX, out tmObj)) 
				{
					tbl.Set(key, val);
					return;
				}

				// else will try the tag method
			}
			else 
			{
				if (!T_TryGetTMByObj(t, TMS.TM_NEWINDEX, out tmObj) || tmObj.V.TtIsNil())
					G_SimpleTypeError(t, "index");
			}

			if (tmObj.V.TtIsFunction()) 
			{
				CallTM(tmObj, t, key, val, false);
				return;
			}

			t = tmObj;
		}
		G_RunError("loop in settable");
	}

	private void V_PushClosure(
		LuaProto p, LuaUpValue[] encup, int stackBase, ref TValue ra)
	{
		var cl = (
			p.Upvalues.Count == 0 ||
			p.Upvalues is [{ IsEnv: true }])
			? p.Pure : new LuaLClosureValue(p);

		ra.SetClLValue(cl);
		for (var i = 0; i < p.Upvalues.Count; ++i)
		{
			// ULDebug.Log("~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~ V_PushClosure i:" + i);
			// ULDebug.Log("~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~ V_PushClosure InStack:" + p.Upvalues[i].InStack);
			// ULDebug.Log("~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~ V_PushClosure Index:" + p.Upvalues[i].Index);

			if (p.Upvalues[i].InStack) // Upvalue refers to local variable
				cl.Upvals[i] = F_FindUpval(
					stackBase + p.Upvalues[i].Index);
			else	// Get upvalue from enclosing function
				cl.Upvals[i] = encup[p.Upvalues[i].Index];
		}
	}

	private void V_ObjLen(StkId ra, StkId rb)
	{
		StkId tmObj;

		var rbt = rb.V.HValue();
		if (rbt != null)
		{
			if (TryFastTM(rbt.MetaTable, TMS.TM_LEN, out tmObj))
				goto calltm;
			ra.V.SetNValue(rbt.Length);
			return;
		}

		var rbs = rb.V.SValue();
		if (rbs != null)
		{
			ra.V.SetNValue(rbs.Length);
			return;
		}

		if (!T_TryGetTMByObj(rb, TMS.TM_LEN, out tmObj) || tmObj.V.TtIsNil())
			G_TypeError(rb, "get length of");

		calltm:
		CallTM(tmObj, rb, rb, ra, true);
	}

	private void V_Concat(int total)
	{
		Util.Assert(total >= 2);

		do
		{
			var n = 2;
			var lhs = Ref[TopIndex - 2];
			var rhs = Ref[TopIndex - 1];
			if (!(lhs.V.TtIsString() || lhs.V.TtIsNumber()) || !ToString(ref rhs.V))
			{
				if (!CallBinTM(lhs, rhs, lhs, TMS.TM_CONCAT))
					G_ConcatError(lhs, rhs);
			}
			else if (rhs.V.SValue().Length == 0)
				ToString(ref lhs.V);
			else if (lhs.V.TtIsString() && lhs.V.SValue().Length == 0)
				lhs.V.SetObj(rhs);
			else
			{
				var sb = new StringBuilder();
				n = 0;
				for (; n < total; ++n)
				{
					var cur = Ref[TopIndex - (n + 1)];

					if (cur.V.TtIsString())
						sb.Insert(0, cur.V.SValue());
					else if (cur.V.TtIsNumber())
						sb.Insert(0, cur.V.NValue);
					else
						break;
				}

				var dest = Ref[TopIndex - n];
				dest.V.SetSValue(sb.ToString());
			}
			total -= n - 1;
			TopIndex -= (n - 1);
		} while (total > 1);
	}

	private void V_DoJump(CallInfo ci, Instruction i, int e)
	{
		var a = i.GETARG_A();
		if (a > 0) F_Close(ci.BaseIndex + (a - 1));
		ci.SavedPc += i.GETARG_sBx() + e;
	}

	private void V_DoNextJump(CallInfo ci)
	{
		var i = ci.SavedPc.Value;
		V_DoJump(ci, i, 1);
	}

	private static bool V_ToNumber(StkId obj, StkId n)
	{
		if (obj.V.TtIsNumber()) 
		{
			n.V.SetNValue(obj.V.NValue);
			return true;
		}

		if (obj.V.TtIsString()) 
		{
			if (Util.Str2Decimal(
				    obj.V.SValue().AsSpan(), out var val)) 
			{
				n.V.SetNValue(val);
				return true;
			}
		}

		return false;
	}

	private static bool V_ToString(ref TValue v)
	{
		if (!v.TtIsNumber()) return false;

		v.SetSValue(v.NValue.ToString());
		return true;
	}

	private static LuaOp TMS2OP(TMS op)
	{
		return op switch
		{
			TMS.TM_ADD => LuaOp.LUA_OPADD,
			TMS.TM_SUB => LuaOp.LUA_OPSUB,
			TMS.TM_MUL => LuaOp.LUA_OPMUL,
			TMS.TM_DIV => LuaOp.LUA_OPDIV,
			TMS.TM_POW => LuaOp.LUA_OPPOW,
			TMS.TM_UNM => LuaOp.LUA_OPUNM,
			TMS.TM_MOD => LuaOp.LUA_OPMOD,
			_ => throw new NotImplementedException(op.ToString())
		};
	}

	private void CallTM(
		StkId f, StkId p1, StkId p2, StkId p3, bool hasRes)
	{
		var result = Index(p3);
		var func = Top;
		
		IncTop().V.SetObj(f);  // Push function
		IncTop().V.SetObj(p1); // Push 1st argument
		IncTop().V.SetObj(p2); // Push 2nd argument

		if (!hasRes) // No result? p3 is 3rd argument
		{
			IncTop().V.SetObj(p3);
		}
		D_CheckStack(0);
		D_Call(func, (hasRes ? 1 : 0), CI.IsLua);
		if (hasRes) // if has result, move it to its place
		{
			--TopIndex;
			Ref[result].V.SetObj(Top);
		}
	}

	private bool CallBinTM(StkId p1, StkId p2, StkId res, TMS tm)
	{
		if (!T_TryGetTMByObj(p1, tm, out var tmObj) || tmObj.V.TtIsNil())
		{
			if (!T_TryGetTMByObj(p2, tm, out tmObj))
				return false;
		}

		CallTM(tmObj, p1, p2, res, true);
		return true;
	}

	private void V_Arith(StkId ra, StkId rb, StkId rc, TMS op)
	{
		var nb = new TValue();
		var nc = new TValue();
		if (V_ToNumber(rb, new StkId(ref nb))
		    && V_ToNumber(rc, new StkId(ref nc)))
		{
			var res = O_Arith(TMS2OP(op), nb.NValue, nc.NValue);
			ra.V.SetNValue(res);
		}
		else if (!CallBinTM(rb, rc, ra, op))
		{
			G_ArithError(rb, rc);
		}
	}

	private bool CallOrderTM(StkId p1, StkId p2, TMS tm, out bool error)
	{
		if (!CallBinTM(p1, p2, Top, tm))
		{
			error = true; // no metamethod
			return false;
		}

		error = false;
		return !IsFalse(Top);
	}

	private bool V_LessThan(StkId lhs, StkId rhs)
	{
		// Compare number
		if (lhs.V.TtIsNumber() && rhs.V.TtIsNumber())
			return lhs.V.NValue < rhs.V.NValue;

		// Compare string
		if (lhs.V.TtIsString() && rhs.V.TtIsString())
			return string.CompareOrdinal(lhs.V.SValue(), rhs.V.SValue()) < 0;

		var res = CallOrderTM(lhs, rhs, TMS.TM_LT, out var error);
		if (error)
		{
			G_OrderError(lhs, rhs);
			return false;
		}
		return res;
	}

	private bool V_LessEqual(StkId lhs, StkId rhs)
	{
		// Compare number
		if (lhs.V.TtIsNumber() && rhs.V.TtIsNumber())
			return lhs.V.NValue <= rhs.V.NValue;

		// Compare string
		if (lhs.V.TtIsString() && rhs.V.TtIsString())
			return string.CompareOrdinal(lhs.V.SValue(), rhs.V.SValue()) <= 0;

		// First try 'le'
		var res = CallOrderTM(rhs, rhs, TMS.TM_LE, out var error);
		if (!error) return res;

		// else try 'lt'
		res = CallOrderTM(rhs, lhs, TMS.TM_LT, out error);
		if (!error) return res;

		G_OrderError(rhs, rhs);
		return false;
	}

	private void V_FinishOp()
	{
		var ciIndex = CI.Index;
		var stackBase = CI.BaseIndex;
		var i = (CI.SavedPc - 1).Value; // interrupted instruction
		var op = i.GET_OPCODE();
		switch (op)
		{
			case OpCode.OP_ADD: case OpCode.OP_SUB: 
			case OpCode.OP_MUL: case OpCode.OP_DIV:
			case OpCode.OP_MOD: case OpCode.OP_POW: 
			case OpCode.OP_UNM: case OpCode.OP_LEN:
			case OpCode.OP_GETTABUP: case OpCode.OP_GETTABLE: 
			case OpCode.OP_SELF:
			{
				var tmp = Ref[stackBase + i.GETARG_A()];
				tmp.V.SetObj(Ref[--TopIndex]);
				break;
			}

			case OpCode.OP_LE: case OpCode.OP_LT: case OpCode.OP_EQ:
			{
				var res = !IsFalse(Ref[TopIndex - 1]);
				--TopIndex;
				// metamethod should not be called when operand is K
				Util.Assert(!Instruction.ISK(i.GETARG_B()));
				if (op == OpCode.OP_LE && // '<=' Using '<' instead?
				    (!T_TryGetTMByObj(Ref[stackBase + i.GETARG_B()], TMS.TM_LE, out var v)
				     || v.V.TtIsNil()))
				{
					res = !res; // invert result
				}

				var ci = BaseCI[ciIndex];
				Util.Assert(ci.SavedPc.Value.GET_OPCODE() == OpCode.OP_JMP);
				if ((res ? 1 : 0) != i.GETARG_A())
					if ((i.GETARG_A() == 0) == res) // Condition failed?
					{
						ci.SavedPc.Index++; // Skip jump instruction
					}
				break;
			}

			case OpCode.OP_CONCAT:
			{
				var top = Ref[TopIndex - 1]; // Top when 'CallBinTM' was called
				var b = i.GETARG_B(); // First element to concatenate
				var total = TopIndex - 1 - (stackBase + b); // Yet to concatenate
				var tmp = Ref[TopIndex - 2];
				tmp.V.SetObj(top); // Put TM result in proper position
				if (total > 1) // Are there elements to concat?
				{
					--TopIndex;
					V_Concat(total);
				}
				// Move final result to final position
				var ci = BaseCI[ciIndex];
				var tmp2 = Ref[ci.BaseIndex + i.GETARG_A()];
				tmp2.V.SetObj(Ref[TopIndex - 1]);
				TopIndex = ci.TopIndex;
				break;
			}

			case OpCode.OP_TFORCALL:
			{
				var ci = BaseCI[ciIndex];
				Util.Assert(
					ci.SavedPc.Value.GET_OPCODE() == OpCode.OP_TFORLOOP);
				TopIndex = ci.TopIndex; // restore top
				break;
			}

			case OpCode.OP_CALL:
			{
				if (i.GETARG_C() - 1 >= 0) // numResults >= 0?
				{
					var ci = BaseCI[ciIndex];
					TopIndex = ci.TopIndex;// restore top
				}
				break;
			}

			case OpCode.OP_TAILCALL: case OpCode.OP_SETTABUP: 
			case OpCode.OP_SETTABLE:
				break;

			default:
				Util.Assert(false);
				break;
		}
	}

	internal bool V_RawEqualObj(StkId t1, StkId t2) => 
		(t1.V.Tt == t2.V.Tt) && V_EqualObject(t1, t2, true);

	private bool EqualObj(StkId t1, StkId t2, bool rawEq) => 
		(t1.V.Tt == t2.V.Tt) && V_EqualObject(t1, t2, rawEq);

	private bool TryGetEqualTM(LuaTable? mt1, LuaTable? mt2, TMS tm, out StkId val)
	{
		// No metamethod
		if (!TryFastTM(mt1, tm, out val)) return false;
		// Same metatables => same metamethods
		if (mt1 == mt2) return true;
		
		// No metamethod
		if (!TryFastTM(mt2, tm, out var tm2)) return false;

		// Same metamethods?
		if (V_RawEqualObj(val, tm2)) return true;

		return false;
	}

	private bool V_EqualObject(StkId t1, StkId t2, bool rawEq)
	{
		Util.Assert(t1.V.Tt == t2.V.Tt);
		var tm = StkId.Nil;
		switch (t1.V.Tt)
		{
			case (int)LuaType.LUA_TNIL:
				return true;
			case (int)LuaType.LUA_TNUMBER:
				return t1.V.NValue == t2.V.NValue;
			case (int)LuaType.LUA_TUINT64:
				return t1.V.UInt64Value == t2.V.UInt64Value;
			case (int)LuaType.LUA_TBOOLEAN:
				return t1.V.BValue() == t2.V.BValue();
			case (int)LuaType.LUA_TSTRING:
				return t1.V.SValue() == t2.V.SValue();
			case (int)LuaType.LUA_TUSERDATA:
				throw new NotImplementedException();
			case (int)LuaType.LUA_TTABLE:
			{
				var tbl1 = t1.V.HValue();
				var tbl2 = t2.V.HValue();
				if (ReferenceEquals(tbl1, tbl2))
					return true;
				if (rawEq)
					return false;
				if (!TryGetEqualTM(tbl1.MetaTable, tbl2.MetaTable, TMS.TM_EQ, out tm))
					return false; // No TM?
				break;
			}
			default:
				return t1.V.OValue == t2.V.OValue;
		}

		CallTM(tm, t1, t2, Top, true); // Call TM
		return !IsFalse(Top);
	}
	#endregion
	#region LuaDebug.cs
	public bool GetStack(LuaDebug ar, int level) => 
		GetStack(ref ar.ActiveCIIndex, level);

	public bool GetStack(ref int activeCIIndex, int level)
	{
		if (level < 0)
			return false;

		int index;
		for (index = CI.Index; level > 0 && index > 0; --index) 
			level--;

		var status = false;
		if (level == 0 && index > 0) 
		{
			status = true;
			activeCIIndex = index;
		}
		return status;
	}

	public int GetInfo(LuaDebug ar, string what)
	{
		CallInfo? ci = null;
		StkId func;

		var	pos = 0;
		if (what[pos] == '>')
		{
			func = Ref[TopIndex - 1];

			Util.ApiCheck(func.V.TtIsFunction(), "Function expected");
			pos++;
			--TopIndex;
		}
		else
		{
			ci = BaseCI[ar.ActiveCIIndex];
			func = Ref[ci.FuncIndex];
			Util.Assert(func.V.TtIsFunction());
		}

		// var IsClosure(func.Value) ? func.Value
		var status = AuxGetInfo(what, ar, func, ci);
		if (what.Contains('f'))
		{
			Top.Set(func);
			IncrTop();
		}
		if (what.Contains('L'))
		{
			CollectValidLines(func);
		}
		return status;
	}

	private int AuxGetInfo(
		string what, LuaDebug ar, StkId func, CallInfo? ci)
	{
		var status = 1;
		foreach (var c in what)
		{
			switch (c)
			{
				case 'S':
					FuncInfo(ar, func);
					break;
				case 'l':
					ar.CurrentLine = ci is { IsLua: true } ? GetCurrentLine(ci) : -1;
					break;
				case 'u':
					Util.Assert(func.V.TtIsFunction());
					if (func.V.ClIsLuaClosure()) 
					{
						var lcl = func.V.ClLValue();
						ar.NumUps = lcl.Length;
						ar.IsVarArg = lcl.Proto.IsVarArg;
						ar.NumParams = lcl.Proto.NumParams;
					}
					else if (func.V.ClIsCsClosure()) 
					{
						var ccl = func.V.ClCsValue();
						ar.NumUps = ccl.Upvals.Length;
						ar.IsVarArg = true;
						ar.NumParams = 0;
					}
					else throw new NotImplementedException();
					break;
				case 't':
					ar.IsTailCall = (ci != null)
						? ((ci.CallStatus & CallStatus.CIST_TAIL) != 0)
						: false;
					break;
				case 'n':
					if (ci != null
					    && ((ci.CallStatus & CallStatus.CIST_TAIL) == 0)
					    && BaseCI[ci.Index - 1].IsLua)
					{
						ar.NameWhat = GetFuncName(BaseCI[ci.Index - 1], out ar.Name!)!;
					}
					else
					{
						ar.NameWhat = null!;
					}
					if (ar.NameWhat == null)
					{
						ar.NameWhat = ""; // Not Found
						ar.Name = null!;
					}
					break;
				case 'L':
				case 'f': // Handled by GetInfo
					break;
				default: status = 0; // Invalid option
					break;
			}
		}
		return status;
	}

	private void CollectValidLines(StkId func)
	{
		Util.Assert(func.V.TtIsFunction());
		if (func.V.ClIsLuaClosure()) 
		{
			var lcl = func.V.ClLValue();
			var p = lcl.Proto;
			var lineInfo = p.LineInfo;
			var t = new LuaTable(this);
			var v = new TValue();

			Top.V.SetHValue(t);
			IncrTop();

			v.SetBValue(true);
			var rv = new StkId(ref v);
			foreach (var t1 in lineInfo)
				t.SetInt(t1, rv);
		}
		else if (func.V.ClIsCsClosure()) 
		{
			Top.V.SetNilValue();
			IncrTop();
		}
		else throw new NotImplementedException();
	}

	private string? GetFuncName(CallInfo ci, out string? name)
	{
		var proto = GetCurrentLuaFunc(ci)!.Proto; // calling function
		var pc = ci.CurrentPc; // calling instruction index
		var ins = proto.Code[pc]; // calling instruction

		TMS tm;
		switch (ins.GET_OPCODE())
		{
			case OpCode.OP_CALL:
			case OpCode.OP_TAILCALL:  /* Get function name */
				return GetObjName(proto, pc, ins.GETARG_A(), out name);

			case OpCode.OP_TFORCALL: /* for iterator */
				name = "for iterator";
				return "for iterator";

			/* all other instructions can call only through metamethods */
			case OpCode.OP_SELF:
			case OpCode.OP_GETTABUP:
			case OpCode.OP_GETTABLE: tm = TMS.TM_INDEX; break;

			case OpCode.OP_SETTABUP:
			case OpCode.OP_SETTABLE: tm = TMS.TM_NEWINDEX; break;

			case OpCode.OP_EQ: tm = TMS.TM_EQ; break;
			case OpCode.OP_ADD: tm = TMS.TM_ADD; break;
			case OpCode.OP_SUB: tm = TMS.TM_SUB; break;
			case OpCode.OP_MUL: tm = TMS.TM_MUL; break;
			case OpCode.OP_DIV: tm = TMS.TM_DIV; break;
			case OpCode.OP_MOD: tm = TMS.TM_MOD; break;
			case OpCode.OP_POW: tm = TMS.TM_POW; break;
			case OpCode.OP_UNM: tm = TMS.TM_UNM; break;
			case OpCode.OP_LEN: tm = TMS.TM_LEN; break;
			case OpCode.OP_LT: tm = TMS.TM_LT; break;
			case OpCode.OP_LE: tm = TMS.TM_LE; break;
			case OpCode.OP_CONCAT: tm = TMS.TM_CONCAT; break;

			default:
				name = null;
				return null;  /* else no useful name can be found */
		}

		name = GetTagMethodName(tm);
		return "metamethod";
	}

	private static void FuncInfo(LuaDebug ar, StkId func)
	{
		Util.Assert(func.V.TtIsFunction());
		if (func.V.ClIsLuaClosure())
		{
			var lcl = func.V.ClLValue();
			var p = lcl.Proto;
			ar.Source = string.IsNullOrEmpty(p.Source) ? "=?" : p.Source;
			ar.LineDefined = p.LineDefined;
			ar.LastLineDefined = p.LastLineDefined;
			ar.What = (ar.LineDefined == 0) ? "main" : "C";
		}
		else if (func.V.ClIsCsClosure())
		{
			ar.Source = "=[C#]";
			ar.LineDefined = -1;
			ar.LastLineDefined = -1;
			ar.What = "C#";
		}
		else throw new NotImplementedException();
		
		// ----------------------------
		// 'literal' source
		var len = ar.Source.Length;
		var len1 = Math.Min(LuaDef.LUA_IDSIZE, len - 1); 
		if (ar.Source[0] == '=')
		{
			ar.ShortSrc = ar.Source.Substring(
				1, len1);
		}
		else if (ar.Source[0] == '@')
		{
			if (len < LuaDef.LUA_IDSIZE)
			{
				ar.ShortSrc = ar.Source[1..];
			}
			else
			{
				ar.ShortSrc = string.Concat(
					"...",
					ar.Source.AsSpan(1, len1 - 3));
			}
		}
		else
		{
			var src = ar.Source.TrimStart();
			var firstLineIdx = src.IndexOf('\n');
			ar.ShortSrc = "[source \"";
			var req = LuaDef.LUA_IDSIZE - (ar.ShortSrc.Length + 3 + 2); // Save space for prefix+suffix
			if (len < req && firstLineIdx == -1)
			{
				ar.ShortSrc += src;
			}
			else
			{
				len = firstLineIdx != -1 ? firstLineIdx : src.Length;
				len = Math.Min(len, req);
				ar.ShortSrc += src[..len];
				ar.ShortSrc += "...";
			}
			ar.ShortSrc += "\"]";
		}
	}

	private void AddInfo(string msg)
	{
		// TODO
		if (!CI.IsLua) return;

		var proto = GetCurrentLuaFunc(CI)!.Proto;
		var line = GetCurrentLine(CI);
		var src = proto.Source;
		if (src == null)
			src = "???";
		else
		{
			// Get portion
			var lines = src.Replace("\r","").Split('\n');
			src = (lines.Length >= line ? lines[line - 1] : "?").TrimStart();
		}

		// Cannot use PushString, because PushString is part of the API interface
		// The ApiIncrTop function in the API interface will check if Top exceeds CI.Top, causing an error
		// api.PushString(msg);
		// Hack!
		var oldTop = CI.TopIndex;
		CI.TopIndex = int.MaxValue;
		msg = Util.Traceback(this, msg);
		CI.TopIndex = oldTop;
		// end Hack!
		O_PushString($"{src}:{line}: {msg}");
	}

	internal void G_RunError(string fmt, params object[] args)
	{
		var msg = string.Format(fmt, args);
		AddInfo(msg);
		G_ErrorMsg(msg);
	}

	private void G_ErrorMsg(string msg)
	{
		if (ErrFunc != 0) // Is there an error handling function?
		{
			var errFunc = Ref[ErrFunc];

			if (!errFunc.V.TtIsFunction())
				D_Throw(ThreadStatus.LUA_ERRERR, msg);

			var below = Ref[TopIndex - 1];
			Top.V.SetObj(below);
			below.V.SetObj(errFunc);
			IncrTop();

			D_Call(below, 1, false);
		}

		D_Throw(ThreadStatus.LUA_ERRRUN, msg);
	}

	private static string UpValueName(LuaProto p, int uv) => 
		p.Upvalues[uv].Name;

	private string? GetUpvalName(CallInfo ci, StkId o, out string name)
	{
		var func = Ref[ci.FuncIndex];
		Util.Assert(func.V.TtIsFunction() && func.V.ClIsLuaClosure());

		var lcl = func.V.ClLValue();
		var oIdx = Index(o);
		for (var i = 0; i < lcl.Length; ++i) 
		{
			if (lcl.Upvals[i].Index == oIdx) 
			{
				name = UpValueName(lcl.Proto, i);
				return "upvalue";
			}
		}
		name = "";
		return null;
	}

	private void KName(LuaProto proto, int pc, int c, out string? name)
	{
		if (Instruction.ISK(c)) 
		{ // is 'c' a constant
			var val = proto.K[Instruction.INDEXK(c)];
			if (val.TtIsString()) 
			{ // literal constant?
				name = val.SValue();
				return;
			}
			// else no reasonable name found
		}
		else 
		{ // 'c' is a register
			var what = GetObjName(proto, pc, c, out name);
			if (what == "constant") { // found a constant name
				return; // 'name' already filled
			}
			// else no reasonable name found
		}
		name = "?"; // no reasonable name found
	}

	private static int FindSetReg(LuaProto proto, int lastpc, int reg)
	{
		var setReg = -1; // keep last instruction that changed `reg'
		for (var pc = 0; pc < lastpc; ++pc) 
		{
			var ins = proto.Code[pc];
			var op = ins.GET_OPCODE();
			var a= ins.GETARG_A();

			switch (op) 
			{
				case OpCode.OP_LOADNIL: {
					var b = ins.GETARG_B();
					// set registers from `a' to `a+b'
					if (a <= reg && reg <= a + b)
						setReg = pc;
					break;
				}

				case OpCode.OP_TFORCALL: {
					// effect all regs above its base
					if (reg >= a + 2)
						setReg = pc;
					break;
				}

				case OpCode.OP_CALL:
				case OpCode.OP_TAILCALL: {
					// effect all registers above base
					if (reg >= a)
						setReg = pc;
					break;
				}
					
				case OpCode.OP_JMP: {
					var b = ins.GETARG_sBx();
					var dest = pc + 1 + b;
					// jump is forward and do not skip `lastpc'
					if (pc < dest && dest <= lastpc)
						pc += b; // do the jump
					break;
				}

				case OpCode.OP_TEST: // jumped code can change `a'
					if (reg == a)
						setReg = pc;
					break;

				default: // any instruction that set A
					if (Coder.TestAMode(op) && reg == a) {
						setReg = pc;
					}
					break;
			}
		}
		return setReg;
	}

	private string? GetObjName(
		LuaProto proto, int lastpc, int reg, out string? name)
	{
		name = null;
		if (F_GetLocalName(proto, reg + 1, lastpc) is {} lName)
		{
			name = lName;
			return "local"; // Is a local?
		}

		// else try symbolic execution
		var pc = FindSetReg(proto, lastpc, reg);
		if (pc == -1) return null; // Could not find reasonable name

		var ins = proto.Code[pc];
		var op = ins.GET_OPCODE();

		switch (op)
		{
			case OpCode.OP_MOVE: {
				var b = ins.GETARG_B(); // move from 'b' to 'a'
				if (b < ins.GETARG_A())
					return GetObjName(proto, pc, b, out name);
				break;
			}
			case OpCode.OP_GETTABUP:
			case OpCode.OP_GETTABLE: {
				var k = ins.GETARG_C();
				var t = ins.GETARG_B();
				var vn = (op == OpCode.OP_GETTABLE)
					? F_GetLocalName(proto, t + 1, pc)
					: UpValueName(proto, t);
				KName(proto, pc, k, out name);
				return (vn == LuaDef.LUA_ENV) ? "global" : "field";
			}

			case OpCode.OP_GETUPVAL: {
				name = UpValueName(proto, ins.GETARG_B());
				return "upvalue";
			}

			case OpCode.OP_LOADK:
			case OpCode.OP_LOADKX: {
				var b = (op == OpCode.OP_LOADK)
					? ins.GETARG_Bx()
					: proto.Code[pc + 1].GETARG_Ax();
				var val = proto.K[b];
				if (val.TtIsString())
				{
					name = val.SValue();
					return "constant";
				}
				break;
			}

			case OpCode.OP_SELF: {
				var k = ins.GETARG_C(); // key index
				KName(proto, pc, k, out name);
				return "method";
			}

			default: break; // Go through to return null
		}

		return null; // could not find reasonable name
	}

	private bool IsInStack(CallInfo ci, StkId o)
	{
		unsafe
		{
			var oIdx = o.PtrIndex;
			fixed (TValue* arr = &Stack[0])
			{
				var value = arr;
				for (var p = ci.BaseIndex; p < ci.TopIndex; p++, value++)
					if (value == oIdx) return true;
			}
		}
		return false;
	}

	private void G_SimpleTypeError(StkId o, string op)
	{
		var t = ObjTypeName(o);
		G_RunError("Attempt to {0} a {1} value", op, t);
	}

	private void G_TypeError(StkId o, string op)
	{
		var ci = CI;
		string? name = null;
		string? kind = null;
		var t = ObjTypeName(o);
		if (ci.IsLua)
		{
			kind = GetUpvalName(ci, o, out name);
			if (kind != null && IsInStack(ci, o))
			{
				var lcl = Ref[ci.FuncIndex].V.ClLValue();
				kind = GetObjName(lcl.Proto, ci.CurrentPc,
					(Index(o) - ci.BaseIndex), out name);
			}
		}
		if (kind != null)
			G_RunError("Attempt to {0} {1} '{2}' (a {3} value)",
				op, kind, name!, t);
		else
			G_RunError("Attempt to {0} a {1} value", op, t);
	}

	private void G_ArithError(StkId p1, StkId p2)
	{
		var n = new TValue();
		if (!V_ToNumber(p1, new StkId(ref n))) p2 = p1; // first operand is wrong

		G_TypeError(p2, "Perform arithmetic on");
	}

	private void G_OrderError(StkId p1, StkId p2)
	{
		var t1 = ObjTypeName(p1);
		var t2 = ObjTypeName(p2);
		if (t1 == t2)
			G_RunError("Attempt to compare two {0} values", t1);
		else
			G_RunError("Attempt to compare {0} with {1}", t1, t2);
	}

	private void G_ConcatError(StkId p1, StkId p2)
	{
		if (p1.V.TtIsString() || p1.V.TtIsNumber()) p1 = p2;
		Util.Assert(!(p1.V.TtIsString() || p1.V.TtIsNumber()));
		G_TypeError(p1, "concatenate");
	}
	#endregion
}