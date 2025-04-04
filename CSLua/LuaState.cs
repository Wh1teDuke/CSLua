// #define ENABLE_DUMP_STACK
// #define DEBUG_RECORD_INS

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using CSLua.Extensions;
using CSLua.Lib;
using CSLua.Parse;
using CSLua.Util;

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

	public CsDelegate? ContinueFunc;
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
			LuaUtil.Assert(IsLua);
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

	public StkId Top => Ref(TopIndex);
	
	public StkId IncTop() => Ref(TopIndex++);
	
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public StkId Ref(int index) => new(ref Stack[index]);

	private int Index(StkId v)
	{
		unsafe
		{
#pragma warning disable CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type
			fixed (TValue* arr = &Stack[0])
#pragma warning restore CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type
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

		UpvalHead		= new LuaUpValue(this, 0);
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
		CheckStackX(0);
	}

	internal void ApiIncrTop()
	{
		IncTop();
		// ULDebug.Log("[ApiIncrTop] ==== TopIndex:" + TopIndex);
		// ULDebug.Log("[ApiIncrTop] ==== CI.TopIndex:" + CI.TopIndex);
		LuaUtil.ApiCheck(TopIndex <= CI.TopIndex, "Stack overflow");
	}

	private void InitStack()
	{
		Stack = new TValue[LuaDef.BASIC_STACK_SIZE];
		StackLast = LuaDef.BASIC_STACK_SIZE - LuaDef.EXTRA_STACK;
		for (var i = 0; i < LuaDef.BASIC_STACK_SIZE; ++i) 
		{
			Stack[i].SetNil();
		}

		BaseCI = new CallInfo[LuaDef.BASE_CI_SIZE];
		for (var i = 0; i < LuaDef.BASE_CI_SIZE; ++i) 
		{
			BaseCI[i] = new CallInfo { Index = i };
		}
		
		TopIndex = 0;
		CI = BaseCI[0];
		CI.FuncIndex = TopIndex;
		IncTop().V.SetNil(); // 'function' entry for this 'ci'
		CI.TopIndex = TopIndex + LuaDef.LUA_MINSTACK;
	}

	private void InitRegistry()
	{
		var mt = new TValue();
		var rmt = new StkId(ref mt);

		G.Registry.V.SetTable(new LuaTable(this));

		mt.SetThread(this);
		G.Registry.V.AsTable()!.SetInt(LuaDef.LUA_RIDX_MAINTHREAD, rmt);

		mt.SetTable(new LuaTable(this));
		G.Registry.V.AsTable()!.SetInt(LuaDef.LUA_RIDX_GLOBALS, rmt);
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
	
	
	// grep `NoTagMethodFlags' if num of TMS >= 32
	private enum TMS
	{
		TM_INDEX, TM_NEWINDEX, TM_GC, TM_MODE, TM_LEN, TM_EQ, TM_ADD, TM_SUB,
		TM_MUL, TM_DIV, TM_MOD, TM_POW, TM_UNM, TM_LT, TM_LE, TM_CONCAT, 
		TM_CALL, TM_ITER,
	}

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
			TMS.TM_ITER => "__iter",
			_ => throw new ArgumentOutOfRangeException(nameof(tm), tm, null)
		};
	}

	private static bool TryGetTM(LuaTable? mt, TMS tm, out StkId val)
	{
		val = StkId.Nil;
		if (mt == null) return false;
		
		if (!mt.TryGetStr(GetTagMethodName(tm), out val) || val.V.IsNil()) // no tag method?
		{
			// Cache this fact
			mt.NoTagMethodFlags |= 1u << (int)tm;
			return false;
		}

		return true;
	}

	private bool TryGetTMByObj(StkId o, TMS tm, out StkId val)
	{
		val = StkId.Nil;
		LuaTable? mt;

		switch (o.V.Type)
		{
			case LuaType.LUA_TTABLE:
			{
				var tbl = o.V.AsTable()!;
				mt = tbl.MetaTable;
				break;
			}
			case LuaType.LUA_TUSERDATA:
				throw new NotImplementedException();

			default:
			{
				mt = G.MetaTables[(int)o.V.Type];
				break;
			}
		}

		return mt != null && mt.TryGetStr(GetTagMethodName(tm), out val);
	}
	
	#region LuaAPI.cs
	LuaState ILua.NewThread()
	{
		var newLua = new LuaState(G);

		Top.V.SetThread(newLua);
		ApiIncrTop();

		newLua.BaseFolder = BaseFolder;

		return newLua;
	}

	private readonly record struct LoadParameter(
		LuaState L, ILoadInfo LoadInfo, string? Name, string? Mode);

	private void CheckMode(string? given, string expected)
	{
		if (given != null && !given.Contains(expected[0]))
		{
			var msg = $"Attempt to load a {expected} chunk (mode is '{given}')"; 
			O_PushString(msg);
			Throw(ThreadStatus.LUA_ERRSYNTAX, msg);
		}
	}

	private static void Load(ref LoadParameter param)
	{
		var L = param.L;

		LuaProto proto;
		if (param.LoadInfo is ProtoLoadInfo plInfo)
			proto = plInfo.Proto;

		else if (param.LoadInfo.PeekByte() == LuaConf.LUA_SIGNATURE[0])
		{
			L.CheckMode(param.Mode, "binary");
			proto = UnDump.LoadBinary(param.LoadInfo, param.Name ?? "???");
		}
		else
		{
			L.CheckMode(param.Mode, "text");
			var parser = Parser.Read(
				param.LoadInfo, param.Name, L.NumCSharpCalls);
			proto = parser.Proto;
		}

		if (param.Name is { } name)
			proto.Source = name;

		var cl = new LuaClosure(proto);
		LuaUtil.Assert(cl.Length == cl.Proto.Upvalues.Count);

		for (var i = 0; i < cl.Length; i++)
		{
			cl.Upvals[i] = new LuaUpValue(L);
		}

		L.Top.V.SetLuaClosure(cl);
		L.IncrTop();
	}

	public ThreadStatus Load<T>(
		T loadInfo, string? name = null, string? mode = null
		) where T: struct, ILoadInfo
	{
		var param  = new LoadParameter(this, loadInfo, name, mode);
		var status = PCall(Load, ref param, TopIndex, ErrFunc);
		if (status != ThreadStatus.LUA_OK) return status;

		var below = Ref(TopIndex - 1);
		LuaUtil.Assert(below.V.IsFunction() && below.V.IsLuaClosure());
		var cl = below.V.AsLuaClosure()!;
		if (cl.Length == 1) 
		{
			G.Registry.V.AsTable()!.TryGetInt(LuaDef.LUA_RIDX_GLOBALS, out var gt);
			cl.Upvals[0] = new LuaUpValue();
			cl.Upvals[0].Value.SetObj(gt);
		}

		return status;
	}

	DumpStatus ILua.Dump(LuaWriter writeFunc)
	{
		LuaUtil.ApiCheckNumElems(this, 1);

		var below = Ref(TopIndex - 1);
		if (!below.V.IsFunction() || !below.V.IsLuaClosure())
			return DumpStatus.ERROR;

		var o = below.V.AsLuaClosure();
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
		CallK(numArgs, numResults);

	public void CallK(int numArgs, int numResults,
		int context = 0, CsDelegate? continueFunc = null)
	{
		LuaUtil.ApiCheck(continueFunc == null || !CI.IsLua,
			"Cannot use continuations inside hooks");
		LuaUtil.ApiCheckNumElems(this, numArgs + 1);
		LuaUtil.ApiCheck(Status == ThreadStatus.LUA_OK,
			"Cannot do calls on non-normal thread");
		CheckResults(numArgs, numResults);
		var func = Ref(TopIndex - (numArgs + 1));

		// Need to prepare continuation?
		if (continueFunc != null && NumNonYieldable == 0)
		{
			CI.ContinueFunc = continueFunc;
			CI.Context		= context;
			Call(func, numResults, true);
		}
		// No continuation or no yieldable
		else
		{
			Call(func, numResults, false);
		}
		AdjustResults(numResults);
	}

	private struct CallS
	{
		public LuaState L;
		public int FuncIndex;
		public int NumResults;
	}

	private static void Call(ref CallS ud) => 
		ud.L.Call(ud.L.Ref(ud.FuncIndex), ud.NumResults, false);

	private void CheckResults(int numArgs, int numResults)
	{
		LuaUtil.ApiCheck(numResults == LuaDef.LUA_MULTRET ||
		              CI.TopIndex - TopIndex >= numResults - numArgs,
			"Results from function overflow current stack size");
	}

	private void AdjustResults(int numResults)
	{
		if (numResults == LuaDef.LUA_MULTRET && CI.TopIndex < TopIndex) 
			CI.TopIndex = TopIndex;
	}

	public ThreadStatus PCall(int numArgs, int numResults, int errFunc = 0) => 
		PCallK(numArgs, numResults, errFunc);

	public ThreadStatus PCallK( 
		int numArgs, int numResults, int errFunc = 0,
		int context = 0, CsDelegate? continueFunc = null)
	{
		LuaUtil.ApiCheck(continueFunc == null || !CI.IsLua,
			"Cannot use continuations inside hooks");
		LuaUtil.ApiCheckNumElems(this, numArgs + 1);
		LuaUtil.ApiCheck(Status == ThreadStatus.LUA_OK,
			"Cannot do calls on non-normal thread");
		CheckResults(numArgs, numResults);

		int func;
		if (errFunc == 0)
			func = 0;
		else
		{
			if (!Index2Addr(errFunc, out _)) LuaUtil.InvalidIndex();
			errFunc += errFunc <= 0 ? TopIndex : CI.FuncIndex;
			func = errFunc;
		}

		ThreadStatus status;
		var c = new CallS { L = this, FuncIndex = TopIndex - (numArgs + 1) };
		if (continueFunc == null || NumNonYieldable > 0) // No continuation or no yieldable?
		{
			c.NumResults = numResults;
			status = PCall(Call, ref c, c.FuncIndex, func);
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

			Call(Ref(c.FuncIndex), numResults, true);

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
		LuaUtil.Assert(ci.ContinueFunc != null); // Must have a continuation
		LuaUtil.Assert(NumNonYieldable == 0);
		// Finish 'CallK'
		AdjustResults(ci.NumResults);
		// Call continuation function
		if ((ci.CallStatus & CallStatus.CIST_STAT) == 0) // No call status?
		{
			ci.Status = ThreadStatus.LUA_YIELD; // 'Default' status
		}
		LuaUtil.Assert(ci.Status != ThreadStatus.LUA_OK);
		ci.CallStatus = (ci.CallStatus
		                & ~(CallStatus.CIST_YPCALL | CallStatus.CIST_STAT))
		                | CallStatus.CIST_YIELDED;

		var n = ci.ContinueFunc!(this); // Call
		LuaUtil.ApiCheckNumElems(this, n);
		// Finish `D_PreCall'
		PosCall(TopIndex - n);
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
				FinishOp(); // Finish interrupted instruction
				Execute(); // Execute down to higher C# `boundary'
			}
		}
	}

	private readonly record struct UnrollParam(LuaState L);
	private static void UnrollWrap(ref UnrollParam param) => param.L.Unroll();

	private void ResumeError(string msg, int firstArg)
	{
		TopIndex = firstArg;
		Top.V.SetString(msg);
		IncrTop();
		Throw(ThreadStatus.LUA_RESUME_ERROR, msg);
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
		if (FindPCall() is not {} ci) // No recover point
			return false;

		var oldTop = ci.ExtraIndex;
		Close(oldTop);
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
			ResumeError("C# stack overflow", firstArg);
		if (Status == ThreadStatus.LUA_OK) // may be starting a coroutine
		{
			if (ci.Index > 0) // not in base level
			{
				ResumeError("cannot resume non-suspended coroutine", firstArg);
			}
			if (!PreCall(Ref(firstArg - 1), LuaDef.LUA_MULTRET)) // Lua function?
			{
				Execute(); // call it
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
				Execute(); // just continue running Lua code
			}
			else // `common' yield
			{
				if (ci.ContinueFunc != null)
				{
					ci.Status = ThreadStatus.LUA_YIELD; // `default' status
					ci.CallStatus |= CallStatus.CIST_YIELDED;
					var n = ci.ContinueFunc(this); // call continuation
					LuaUtil.ApiCheckNumElems(this, n);
					firstArg = TopIndex - n; // yield results come from continuation
				}
				PosCall(firstArg);
			}
			Unroll();
		}
		LuaUtil.Assert(numCSharpCalls == NumCSharpCalls);
	}

	private record struct ResumeParam(LuaState L, int FirstArg);

	private static void ResumeWrap(ref ResumeParam param) => 
		param.L.Resume(param.FirstArg);

	ThreadStatus ILua.Resume(ILuaState from, int numArgs)
	{
		var fromState = from as LuaState;
		NumCSharpCalls = (fromState != null) ? fromState.NumCSharpCalls + 1 : 1;
		NumNonYieldable = 0; // Allow yields

		LuaUtil.ApiCheckNumElems(this, (Status == ThreadStatus.LUA_OK) ? numArgs + 1 : numArgs);

		var resumeParam = new ResumeParam
		{ L = this, FirstArg = TopIndex - numArgs };
		var status = RawRunProtected(ResumeWrap, ref resumeParam);
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
					status = RawRunProtected(UnrollWrap, ref unrollParam);
				}
				else // Unrecoverable error
				{
					Status = status; // Mark thread as 'dead'
					SetErrorObj(status, TopIndex);
					CI.TopIndex = TopIndex;
					break;
				}
			}
			LuaUtil.Assert(status == Status);
		}

		NumNonYieldable = 1; // Do not allow yields
		NumCSharpCalls--;
		LuaUtil.Assert(NumCSharpCalls == (fromState?.NumCSharpCalls ?? 0));
		return status;
	}

	public int Yield(int numResults) => YieldK(numResults);

	public int YieldK(
		int numResults, int context = 0, CsDelegate? continueFunc = null)
	{
		var ci = CI;
		LuaUtil.ApiCheckNumElems(this, numResults);

		if (NumNonYieldable > 0)
		{
			if (this != G.MainThread)
				RunError("attempt to yield across metamethod/C-call boundary");
			else
				RunError("attempt to yield from outside a coroutine");
		}
		Status = ThreadStatus.LUA_YIELD;
		ci.ExtraIndex = ci.FuncIndex; // save current `func'
		if (ci.IsLua) // inside a hook
		{
			LuaUtil.ApiCheck(continueFunc == null, "hooks cannot continue after yielding");
		}
		else
		{
			ci.ContinueFunc = continueFunc;
			if (ci.ContinueFunc != null) // is there a continuation
				ci.Context = context;
			ci.FuncIndex = TopIndex - (numResults + 1);
			throw _YieldExcp.Value!;
		}
		LuaUtil.Assert((ci.CallStatus & CallStatus.CIST_HOOKED) != 0); // must be inside a hook
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
			LuaUtil.ApiCheck(
				index <= StackLast - (CI.FuncIndex + 1), 
				"New top too large");
			var newTop = CI.FuncIndex + 1 + index;
			for (var i = TopIndex; i < newTop; ++i)
				Stack[i].SetNil();
			TopIndex = newTop;
		}
		else
		{
			LuaUtil.ApiCheck(
				-(index + 1) <= (TopIndex - (CI.FuncIndex + 1)), 
				"Invalid new top");
			TopIndex += index + 1;
		}
	}

	public void Remove(int index)
	{
		if (!Index2Addr(index, out _))
			LuaUtil.InvalidIndex();

		index += index <= 0 ? TopIndex : CI.FuncIndex;
		for (var i = index + 1; i < TopIndex; ++i)
			Ref(i - 1).Set(Ref(i));

		--TopIndex;
	}

	void ILua.Insert(int index)
	{
		if (!Index2Addr(index, out var p))
			LuaUtil.InvalidIndex();

		var i = TopIndex;
		index += index <= 0 ? TopIndex : CI.FuncIndex;
		while (i > index) 
		{
			Stack[i].SetObj(Ref(i - 1));
			i--;
		}
		p.Set(Top);
	}

	private void MoveTo(StkId fr, int index)
	{
		if (!Index2Addr(index, out var to))
			LuaUtil.InvalidIndex();

		to.Set(fr);
	}

	void ILua.Replace(int index)
	{
		LuaUtil.ApiCheckNumElems(this, 1);
		MoveTo(Ref(--TopIndex), index);
	}

	public void Copy(int fromIndex, int toIndex)
	{
		if (!Index2Addr(fromIndex, out var fr))
			LuaUtil.InvalidIndex();
		MoveTo(fr, toIndex);
	}

	void ILua.XMove(ILuaState to, int n)
	{
		var toLua = (LuaState)to;
		if (this == toLua) return;

		LuaUtil.ApiCheckNumElems(this, n);
		LuaUtil.ApiCheck(G == toLua.G, "moving among independent states");
		LuaUtil.ApiCheck(toLua.CI.TopIndex - toLua.TopIndex >= n, "not enough elements to move");

		var index = TopIndex - n;
		TopIndex = index;
		for (var i = 0; i < n; ++i)
		{
			toLua.IncTop().Set(Ref(index + i));
		}
	}

	private void GrowStack(int size) => GrowStackX(size);

	private readonly record struct GrowStackParam(
		LuaState L, int Size);

	private static void GrowStackWrap(ref GrowStackParam param) => 
		param.L.GrowStack(param.Size);

	private static readonly PFuncDelegate<GrowStackParam> DG_GrowStack =
		GrowStackWrap;

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
				res = RawRunProtected(
					DG_GrowStack, ref param) == ThreadStatus.LUA_OK;
			}
		}

		if (res && CI.TopIndex < TopIndex + size)
			CI.TopIndex = TopIndex + size; // adjust frame top

		return res;
	}

	int ILua.Error()
	{
		LuaUtil.ApiCheckNumElems(this, 1);
		var msg = API.ToString(-1)!;
		ErrorMsg(msg);
		return 0;
	}

	int ILua.UpValueIndex(int i) => LuaDef.LUA_REGISTRYINDEX - i;

	private static string? AuxUpvalue(StkId addr, int n, out StkId val)
	{
		val = StkId.Nil;
		if (!addr.V.IsFunction())
			return null;

		if (addr.V.IsLuaClosure()) 
		{
			var f = addr.V.AsLuaClosure()!;
			var p = f.Proto;
			if (!(1 <= n && n <= p.Upvalues.Count))
				return null;
			val = f.Upvals[n - 1].StkId;
			var name = p.Upvalues[n - 1].Name;
			return (name == null) ? "" : name;
		}

		if (addr.V.IsCsClosure()) 
		{
			var f = addr.V.AsCSClosure()!;
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

		LuaUtil.ApiCheckNumElems(this, 1);

		if (AuxUpvalue(addr, n, out var val) is not {} result)
			return null;

		--TopIndex;
		val.Set(Top);
		return result;
	}

	public void CreateTable(int nArray, int nRec)
	{
		var tbl = new LuaTable(this);
		Top.V.SetTable(tbl);
		ApiIncrTop();
		if (nArray > 0 || nRec > 0)
			tbl.Resize(nArray, nRec);
	}

	public void NewTable() => CreateTable(0, 0);

	public bool Next(int index)
	{
		if (!Index2Addr(index, out var addr))
			throw new LuaException("Table expected");

		var tbl = addr.V.AsTable();
		if (tbl == null)
			throw new LuaException("Table expected");

		var key = Ref(TopIndex - 1);
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
			LuaUtil.ApiCheck(false, "Table expected");

		var tbl = addr.V.AsTable();
		LuaUtil.ApiCheck(tbl != null, "Table expected");

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

		if (!addr.V.IsTable())
			throw new LuaException("Table expected");

		var tbl = addr.V.AsTable()!;
		var below = Ref(TopIndex - 1);

		tbl.TryGet(below, out var value);
		below.Set(value);
	}

	void ILua.RawSetI(int index, int n)
	{
		LuaUtil.ApiCheckNumElems(this, 1);
		if (!Index2Addr(index, out var addr))
			LuaUtil.InvalidIndex();
		LuaUtil.ApiCheck(addr.V.IsTable(), "Table expected");
		var tbl = addr.V.AsTable()!;
		tbl.SetInt(n, Ref(--TopIndex));
	}

	void ILua.RawSet(int index)
	{
		LuaUtil.ApiCheckNumElems(this, 2);
		if (!Index2Addr(index, out var addr))
			LuaUtil.InvalidIndex();
		LuaUtil.ApiCheck(addr.V.IsTable(), "Table expected");
		var tbl = addr.V.AsTable()!;
		tbl.Set(Ref(TopIndex - 2), Ref(TopIndex - 1));
		TopIndex -= 2;
	}

	void ILua.GetField(int index, string key)
	{
		if (!Index2Addr(index, out var addr))
			LuaUtil.InvalidIndex();

		Top.V.SetString(key);
		var below = Top;
		ApiIncrTop();
		GetTable(addr, below, below);
	}

	public void SetField(int index, string key)
	{
		if (!Index2Addr(index, out var addr))
			LuaUtil.InvalidIndex();

		IncTop().V.SetString(key);
		SetTable(addr, Ref(TopIndex - 1), Ref(TopIndex - 2));
		TopIndex -= 2;
	}

	void ILua.GetTable(int index)
	{
		if (!Index2Addr(index, out var addr))
			LuaUtil.InvalidIndex();

		var below = Ref(TopIndex - 1);
		GetTable(addr, below, below);
	}

	void ILua.SetTable(int index)
	{
		LuaUtil.ApiCheckNumElems(this, 2);
		if (!Index2Addr(index, out var addr))
			LuaUtil.InvalidIndex();

		var key = Ref(TopIndex - 2);
		var val = Ref(TopIndex - 1);
		SetTable(addr, key, val);
		TopIndex -= 2;
	}

	void ILua.Concat(int n)
	{
		LuaUtil.ApiCheckNumElems(this, n);
		if (n >= 2)
		{
			Concat(n);
		}
		else if (n == 0)
		{
			Top.V.SetString("");
			ApiIncrTop();
		}
	}

	public LuaType Type(int index)
	{
		return !Index2Addr(index, out var addr) 
			? LuaType.LUA_TNONE : addr.V.Type;
	}

	internal static string TypeName(LuaType t)
	{
		return t switch
		{
			LuaType.LUA_TNIL => "nil",
			LuaType.LUA_TBOOLEAN => "boolean",
			LuaType.LUA_TLIGHTUSERDATA => "lightuserdata",
			LuaType.LUA_TINT64 => "long",
			LuaType.LUA_TNUMBER => "number",
			LuaType.LUA_TSTRING => "string",
			LuaType.LUA_TTABLE => "table",
			LuaType.LUA_TLIST => "list",
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
		TypeName(v.V.Type);

	// For internal use only; will not trigger an error in ApiIncrTop() due to Top exceeding CI.Top
	private void O_PushString(string s)
	{
		Top.V.SetString(s);
		IncrTop();
	}

	public bool IsNil(int index) => Type(index) == LuaType.LUA_TNIL;

	public bool IsNone(int index) => Type(index) == LuaType.LUA_TNONE;

	public bool IsNoneOrNil(int index)
	{
		var t = Type(index);
		return t is LuaType.LUA_TNONE or LuaType.LUA_TNIL;
	}

	public bool IsString(int index)
	{
		var t = Type(index);
		return t is LuaType.LUA_TSTRING or LuaType.LUA_TNUMBER;
	}

	public bool IsTable(int index) => Type(index) == LuaType.LUA_TTABLE;
	public bool IsList(int index) => Type(index) == LuaType.LUA_TLIST;
	public bool IsFunction(int index) => Type(index) == LuaType.LUA_TFUNCTION;

	bool ILua.Compare(int index1, int index2, LuaEq op)
	{
		if (!Index2Addr(index1, out var addr1))
			LuaUtil.InvalidIndex();

		if (!Index2Addr(index2, out var addr2))
			LuaUtil.InvalidIndex();

		switch (op)
		{
			case LuaEq.LUA_OPEQ: return EqualObj(addr1, addr2, false);
			case LuaEq.LUA_OPLT: return LessThan(addr1, addr2);
			case LuaEq.LUA_OPLE: return LessEqual(addr1, addr2);
			default: LuaUtil.ApiCheck(false, "Invalid option"); return false;
		}
	}

	bool ILua.RawEqual(int index1, int index2)
	{
		if (!Index2Addr(index1, out var addr1))
			return false;

		if (!Index2Addr(index2, out var addr2))
			return false;

		return RawEqualObj(addr1, addr2);
	}

	int ILua.RawLen(int index)
	{
		if (!Index2Addr(index, out var addr))
			LuaUtil.InvalidIndex();

		return addr.V.Type switch
		{
			LuaType.LUA_TSTRING => addr.V.AsString()?.Length ?? 0,
			LuaType.LUA_TTABLE => addr.V.AsTable()?.Length ?? 0,
			LuaType.LUA_TLIST => addr.V.AsList()?.Count ?? 0,
			LuaType.LUA_TUSERDATA => throw new NotImplementedException(),
			_ => 0
		};
	}

	void ILua.Len(int index)
	{
		if (!Index2Addr(index, out var addr))
			LuaUtil.InvalidIndex();

		ObjLen(Top, addr);
		ApiIncrTop();
	}

	public void PushNil()
	{
		Top.V.SetNil();
		ApiIncrTop();
	}

	public void PushBoolean(bool b)
	{
		Top.V.SetBool(b);
		ApiIncrTop();
	}

	public void PushNumber(double n)
	{
		Top.V.SetDouble(n);
		ApiIncrTop();
	}

	public void PushInteger(int n)
	{
		Top.V.SetDouble(n);
		ApiIncrTop();
	}

	void ILua.PushUnsigned(uint n)
	{
		Top.V.SetDouble(n);
		ApiIncrTop();
	}

	public void PushString(string? s)
	{
		if (s == null)
		{
			PushNil();
		}
		else
		{
			Top.V.SetString(s);
			ApiIncrTop();	
		}
	}

	public void PushLuaClosure(LuaClosure f)
	{
		Top.V.SetLuaClosure(f);
		ApiIncrTop();
	}
	
	public void PushCsClosure(CsClosure f)
	{
		Top.V.SetCSClosure(f);
		ApiIncrTop();
	}
	
	public void PushTable(LuaTable table)
	{
		Top.V.SetTable(table);
		ApiIncrTop();
	}

	public void PushList(List<TValue> list)
	{
		Top.V.SetList(list);
		ApiIncrTop();
	}

	public void PushCsDelegate(CsDelegate f, int n = 0)
	{
		if (n == 0)
		{
			Top.V.SetCSClosure(new CsClosure(f));
		}
		else
		{
			// C# Function with UpValue
			LuaUtil.ApiCheckNumElems(this, n);
			LuaUtil.ApiCheck(n <= LuaLimits.MAXUPVAL, "Upvalue index too large");

			var cscl = new CsClosure(f, n);
			var index = TopIndex - n;
			TopIndex = index;
			for (var i = 0; i < n; ++i)
				cscl.Upvals[i].SetObj(Ref(index + i));

			Top.V.SetCSClosure(cscl);
		}
		ApiIncrTop();
	}

	public void PushValue(int index)
	{
		if (!Index2Addr(index, out var addr))
			LuaUtil.InvalidIndex();

		Top.Set(addr);
		ApiIncrTop();
	}

	public void PushGlobalTable() => 
		API.RawGetI(LuaDef.LUA_REGISTRYINDEX, LuaDef.LUA_RIDX_GLOBALS);

	public void PushLightUserData(object o)
	{
		Top.V.SetUserData(o);
		ApiIncrTop();
	}
	
	public void Push(TValue value)
	{
		Top.Set(new StkId(ref value));
		ApiIncrTop();
	}

	public void PushInt64(long o)
	{
		Top.V.SetInt64(o);
		ApiIncrTop();
	}

	bool ILua.PushThread()
	{
		Top.V.SetThread(this);
		ApiIncrTop();
		return G.MainThread == this;
	}

	public void Pop(int n) => SetTop(-n - 1);
	
	bool ILua.GetMetaTable(int index)
	{
		if (!Index2Addr(index, out var addr))
			LuaUtil.InvalidIndex();

		LuaTable? mt;
		switch (addr.V.Type)
		{
			case LuaType.LUA_TTABLE:
			{
				var tbl = addr.V.AsTable()!;
				mt = tbl.MetaTable;
				break;
			}
			case LuaType.LUA_TUSERDATA:
			{
				throw new NotImplementedException();
			}
			default:
			{
				mt = G.MetaTables[(int)addr.V.Type];
				break;
			}
		}
		if (mt == null) return false;
		Top.V.SetTable(mt);
		ApiIncrTop();
		return true;
	}

	bool ILua.SetMetaTable(int index)
	{
		LuaUtil.ApiCheckNumElems(this, 1);

		if (!Index2Addr(index, out var addr))
			LuaUtil.InvalidIndex();

		var below = Ref(TopIndex - 1);
		LuaTable? mt;
		if (below.V.IsNil())
			mt = null;
		else
		{
			LuaUtil.ApiCheck(below.V.IsTable(), "Table expected");
			mt = below.V.AsTable();
		}

		switch (addr.V.Type)
		{
			case LuaType.LUA_TTABLE:
				var tbl = addr.V.AsTable()!;
				tbl.MetaTable = mt;
				break;
			case LuaType.LUA_TUSERDATA:
				throw new NotImplementedException();
			default:
				G.MetaTables[(int)addr.V.Type] = mt;
				break;
		}
		--TopIndex;
		return true;
	}

	public void GetGlobal(string name)
	{
		G.Registry.V.AsTable()!.TryGetInt(LuaDef.LUA_RIDX_GLOBALS, out var gt);
		IncTop().V.SetString(name);
		GetTable(gt, Ref(TopIndex - 1), Ref(TopIndex - 1));
	}

	public void SetGlobal(string name)
	{
		G.Registry.V.AsTable()!.TryGetInt(LuaDef.LUA_RIDX_GLOBALS, out var gt);
		IncTop().V.SetString(name);
		SetTable(gt, Ref(TopIndex - 1), Ref(TopIndex - 2));
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

	public string? ToString(int index)
	{
		if (!Index2Addr(index, out var addr))
			return null;

		if (addr.V.IsString())
			return addr.V.AsString();

		if (!ToStringX(addr))
			return null;

		if (!Index2Addr(index, out addr))
			return null;

		LuaUtil.Assert(addr.V.IsString());
		return addr.V.AsString();
	}

	double ILua.ToNumberX(int index, out bool isNum)
	{
		if (!Index2Addr(index, out var addr))
		{
			isNum = false;
			return 0.0;
		}

		if (addr.V.IsNumber()) 
		{
			isNum = true;
			return addr.V.NValue;
		}
		
		if (addr.V.IsInt64()) 
		{
			isNum = true;
			return addr.V.AsInt64();
		}

		if (addr.V.IsString()) 
		{
			var n = new TValue();
			if (ToNumber(addr, new StkId(ref n))) 
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

		if (addr.V.IsNumber()) 
		{
			isNum = true;
			return (int)addr.V.NValue;
		}
		
		if (addr.V.IsInt64()) 
		{
			isNum = true;
			return (int)addr.V.AsInt64();
		}

		if (addr.V.IsString()) 
		{
			var n = new TValue();
			if (ToNumber(addr, new StkId(ref n))) 
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

		if (addr.V.IsNumber())
		{
			isNum = true;
			return (uint)addr.V.NValue;
		}
		
		if (addr.V.IsInt64())
		{
			isNum = true;
			return (uint)addr.V.AsInt64();
		}

		if (addr.V.IsString())
		{
			var n = new TValue();
			if (ToNumber(addr, new StkId(ref n))) 
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
		Index2Addr(index, out var addr) && addr.V.IsBoolean();

	public bool IsThread(int index) =>
		Index2Addr(index, out var addr) && addr.V.IsThread();

	long ILua.ToInt64X(int index, out bool isNum)
	{
		if (!Index2Addr(index, out var addr)) 
		{
			isNum = false;
			return 0;
		}

		if (!addr.V.IsInt64()) 
		{
			isNum = false;
			return 0;
		}

		isNum = true;
		return addr.V.AsInt64();
	}

	public long ToInt64(int index) =>
		API.ToInt64X(index, out _);

	public object? ToObject(int index) => 
		!Index2Addr(index, out var addr) ? null : addr.V.OValue;

	public object? ToUserData(int index)
	{
		if (!Index2Addr(index, out var addr))
			return null;

		return addr.V.Type switch
		{
			LuaType.LUA_TUSERDATA => throw new NotImplementedException(),
			LuaType.LUA_TLIGHTUSERDATA => addr.V.OValue,
			_ => null
		};
	}

	public LuaTable? ToTable(int index) =>
		!Index2Addr(index, out var addr) ? null : addr.V.AsTable();

	public List<TValue>? ToList(int index)
	{
		if (!Index2Addr(index, out var addr))
			return null;

		if (addr.V.AsList() is {} list)
			return list;
		
		return null;
	}

	public LuaClosure? ToLuaClosure(int index)
	{
		if (!Index2Addr(index, out var addr))
			return null;

		if (addr.V.IsFunction() && addr.V.IsLuaClosure())
			return addr.V.AsLuaClosure();

		return null;
	}

	public ILuaState ToThread(int index)
	{
		if (!Index2Addr(index, out var addr))
			return null!;
		return (addr.V.IsThread() ? addr.V.AsThread() : null)!;
	}
	
	internal bool Index2Addr(int index, out StkId addr)
	{
		addr = StkId.Nil;
		var ci = CI;
		if (index > 0)
		{
			var addrIndex = ci.FuncIndex + index;
			LuaUtil.ApiCheck(
				index <= ci.TopIndex - (ci.FuncIndex + 1), 
				"Unacceptable index");
			if (addrIndex >= TopIndex)
				return false;

			addr = Ref(addrIndex);
			return true;
		}

		if (index > LuaDef.LUA_REGISTRYINDEX)
		{
			LuaUtil.ApiCheck(
				index != 0 && -index <= TopIndex - (ci.FuncIndex + 1),
				"invalid index");
			addr = Ref(TopIndex + index);
			return true;
		}

		if (index == LuaDef.LUA_REGISTRYINDEX)
		{
			addr = G.Registry;
			return true;
		}
		// upvalues

		index = LuaDef.LUA_REGISTRYINDEX - index;
		LuaUtil.ApiCheck(
			index <= LuaLimits.MAXUPVAL + 1,
			"Upvalue index too large");

		var func = Ref(ci.FuncIndex);
		LuaUtil.Assert(func.V.IsFunction());
		LuaUtil.Assert(func.V.IsCsClosure());

		var clcs = func.V.AsCSClosure()!;
		if (index > clcs.Upvals.Length)
			return false;

		addr = clcs.Ref(index - 1);
		return true;
	}
	#endregion
	#region LuaFunc.cs
	private LuaUpValue FindUpval(int level)
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

	private void Close(int level)
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

	private static string? GetLocalName(LuaProto proto, int localNumber, int pc)
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
	private static void Throw(ThreadStatus errCode, string msg) => 
		throw new LuaRuntimeException(errCode, msg);

	private ThreadStatus RawRunProtected<T>(
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
		var old = Ref(oldTop);
		switch (errCode)
		{
			case ThreadStatus.LUA_ERRMEM: // Memory error?
				old.V.SetString("Not enough memory");
				break;

			case ThreadStatus.LUA_ERRERR:
				old.V.SetString("Error in error handling");
				break;

			default: // Error message on current top
				old.Set(Ref(TopIndex - 1));
				break;
		}
		TopIndex = oldTop + 1;
	}

	private ThreadStatus PCall<T>(
		PFuncDelegate<T> func, ref T ud, int oldTopIndex, int errFunc
		) where T: struct
	{
		var oldCIIndex = CI.Index;
		var oldNumNonYieldable= NumNonYieldable;
		var oldErrFunc = ErrFunc;

		ErrFunc = errFunc;
		var status = RawRunProtected(func, ref ud);
		if (status != ThreadStatus.LUA_OK) // Error occurred?
		{
			Close(oldTopIndex);
			SetErrorObj(status, oldTopIndex);
			CI = BaseCI[oldCIIndex];
			NumNonYieldable = oldNumNonYieldable;
		}
		ErrFunc = oldErrFunc;
		return status;
	}

	private void Call(StkId func, int nResults, bool allowYield)
	{
		if (++NumCSharpCalls >= LuaLimits.LUAI_MAXCCALLS)
		{
			if (NumCSharpCalls == LuaLimits.LUAI_MAXCCALLS)
				RunError("CSharp Stack Overflow");
			else if (NumCSharpCalls >=
			         LuaLimits.LUAI_MAXCCALLS + (LuaLimits.LUAI_MAXCCALLS >> 3))
				Throw(ThreadStatus.LUA_ERRERR, "CSharp Stack Overflow");
		}
		if (!allowYield)
			NumNonYieldable++;
		if (!PreCall(func, nResults)) // Is a Lua function ?
			Execute();
		if (!allowYield)
			NumNonYieldable--;
		NumCSharpCalls--;
	}

	/// <summary>
	/// Return true if function has been executed
	/// </summary>
	private bool PreCall(StkId func, int nResults)
	{
		// prepare for Lua call

#if DEBUG_D_PRE_CALL
		ULDebug.Log("============================ D_PreCall func:" + func);
#endif

		var funcIndex = Index(func);
		if (!func.V.IsFunction()) 
		{
			// not a function, retry with 'function' tag method
			TryFuncTM(ref func);

			// now it must be a function
			return PreCall(func, nResults);
		}

		if (func.V.IsLuaClosure()) 
		{
			var cl = func.V.AsLuaClosure();
			LuaUtil.Assert(cl != null);
			var p = cl!.Proto;

			CheckStackX(p.MaxStackSize + p.NumParams);

			// Complete the parameters
			var n = (TopIndex - funcIndex) - 1;
			for (; n < p.NumParams; ++n)
			{
				IncTop().V.SetNil();
			}

			var stackBase = !p.IsVarArg 
				? (funcIndex + 1) : AdjustVarargs(p, n);
				
			CI = ExtendCI();
			CI.NumResults = nResults;
			CI.FuncIndex = funcIndex;
			CI.BaseIndex = stackBase;
			CI.TopIndex  = stackBase + p.MaxStackSize;
			LuaUtil.Assert(CI.TopIndex <= StackLast);
			CI.SavedPc = new InstructionPtr(p.Code, 0);
			CI.CallStatus = CallStatus.CIST_LUA;

			TopIndex = CI.TopIndex;

			return false;
		}

		if (func.V.IsCsClosure()) 
		{
			var cscl = func.V.AsCSClosure();
			LuaUtil.Assert(cscl != null);

			CheckStackX(LuaDef.LUA_MINSTACK);

			CI = ExtendCI();
			CI.NumResults = nResults;
			CI.FuncIndex = funcIndex;
			CI.TopIndex = TopIndex + LuaDef.LUA_MINSTACK;
			CI.CallStatus = CallStatus.CIST_NONE;

			// Do the actual call
			var n = cscl!.Fun(this);
				
			// Poscall
			PosCall(TopIndex - n);

			return true;
		}

		throw new NotImplementedException();
	}

	private int PosCall(int firstResultIndex)
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
			Ref(resIndex++).Set(Ref(firstResultIndex++));
		}
		while (i-- > 0)
		{
#if DEBUG_D_POS_CALL
			ULDebug.Log("[D] ==== PosCall new LuaNil()");
#endif
			Stack[resIndex++].SetNil();
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
		LuaUtil.Assert(actual >= NumFixArgs, "AdjustVarargs (actual >= NumFixArgs) is false");

		var fixedArg = TopIndex - actual; 	// first fixed argument
		var stackBase = TopIndex;		// final position of first argument
		for (var i = stackBase; i < stackBase + NumFixArgs; ++i)
		{
			Ref(i).Set(Ref(fixedArg));
			Stack[fixedArg++].SetNil();
		}
		TopIndex = stackBase + NumFixArgs;
		return stackBase;
	}

	private bool TryFuncTM(ref StkId func)
	{
		var val = StkId.Nil;
		if (!TryGetTMByObj(func, TMS.TM_CALL, out val)
				|| !val.V.IsFunction())
			TypeError(func, "call");

		// Open a hole inside the stack at 'func'
		var funcIndex = Index(func);
		for (var i = TopIndex; i > funcIndex; --i) 
			Stack[i].SetObj(Ref(i - 1));

		IncrTop();
		func = Ref(funcIndex);
		func.Set(val);
		return true;
	}

	private void CheckStackX(int n)
	{
		if (StackLast - TopIndex <= n)
			GrowStackX(n);
		// TODO: FOR DEBUGGING
		// else
		// 	CondMoveStack();
	}

	// some space for error handling
	private const int ERRORSTACKSIZE = LuaConf.LUAI_MAXSTACK + 200;

	private void GrowStackX(int n)
	{
		var size = Stack.Length;
		if (size > LuaConf.LUAI_MAXSTACK)
			Throw(ThreadStatus.LUA_ERRERR, "Stack Overflow");

		var needed = TopIndex + n + LuaDef.EXTRA_STACK;
		var newSize = 2 * size;
		if (newSize > LuaConf.LUAI_MAXSTACK) 
			newSize = LuaConf.LUAI_MAXSTACK;
		if (newSize < needed) 
			newSize = needed;
		if (newSize > LuaConf.LUAI_MAXSTACK)
		{
			ReallocStack(ERRORSTACKSIZE);
			RunError("Stack Overflow");
		}
		else
		{
			ReallocStack(newSize);
		}
	}

	private void ReallocStack(int size)
	{
		LuaUtil.Assert(size is <= LuaConf.LUAI_MAXSTACK or ERRORSTACKSIZE);
		var newStack = new TValue[size];
		var i = 0;
		for (; i < Stack.Length; ++i) 
		{
			newStack[i] = Stack[i];
		}
		for (; i < size; ++i) 
		{
			newStack[i].SetNil();
		}
		Stack = newStack;
		StackLast = size - LuaDef.EXTRA_STACK;
	}
	#endregion
	#region LuaAuxLib.cs
	public const int LEVELS1 = 12; // Size of the first part of the stack
	public const int LEVELS2 = 10; // Size of the second part of the stack

	public void Where(int level)
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

	public int Error(string fmt, params object[] args)
	{
		Where(1);
		API.PushString(string.Format(fmt, args));
		API.Concat(2);
		return API.Error();
	}

	public void CheckStack(int size, string? msg = null)
	{
		// Keep some extra space to run error routines, if needed
		if (!API.CheckStack(size + LuaDef.LUA_MINSTACK))
		{
			Error(msg != null ? $"stack overflow ({msg})" : "stack overflow");
		}
	}

	public void CheckAny(int nArg)
	{
		if (Type(nArg) == LuaType.LUA_TNONE)
			ArgError(nArg, "Value expected");
	}

	public double CheckNumber(int nArg)
	{
		var d = API.ToNumberX(nArg, out var isNum);
		if (!isNum) TagError(nArg, LuaType.LUA_TNUMBER);
		return d;
	}

	public int CheckInteger(int nArg)
	{
		var d = API.ToIntegerX(nArg, out var isNum);
		if (!isNum) TagError(nArg, LuaType.LUA_TNUMBER);
		return d;
	}

	public long CheckInt64(int nArg)
	{
		var v = API.ToInt64X(nArg, out var isNum);
		if (!isNum) TagError(nArg, LuaType.LUA_TINT64);
		return v;
	}

	public string CheckString(int nArg)
	{
		var s = ToString(nArg);
		if (s == null) TagError(nArg, LuaType.LUA_TSTRING);
		return s!;
	}

	public LuaTable CheckTable(int nArg)
	{
		var s = ToTable(nArg);
		if (s == null) TagError(nArg, LuaType.LUA_TTABLE);
		return s!;
	}

	public uint CheckUnsigned(int nArg)
	{
		var d = API.ToUnsignedX(nArg, out var isNum);
		if (!isNum) TagError(nArg, LuaType.LUA_TNUMBER);
		return d;
	}

	public object CheckUserData(int nArg)
	{
		var o = API.ToUserData(nArg);
		if (o == null) TagError(nArg, LuaType.LUA_TUSERDATA);
		return o!;
	}

	public List<TValue> CheckList(int narg)
	{
		var o = ToList(narg);
		if (o == null) TagError(narg, LuaType.LUA_TLIST);
		return o!;
	}

	public LuaClosure CheckLuaFunction(int narg)
	{
		var o = ToLuaClosure(narg);
		if (o == null) TagError(narg, LuaType.LUA_TFUNCTION);
		return o!;
	}

	public T Opt<T>(Func<int,T> f, int n, T def)
	{
		var t = API.Type(n);
		return t is LuaType.LUA_TNONE or LuaType.LUA_TNIL ? def : f(n);
	}

	public int OptInt(int nArg, int def)
	{
		var t = API.Type(nArg);
		return t is LuaType.LUA_TNONE or LuaType.LUA_TNIL ?
			def : CheckInteger(nArg);
	}

	public string OptString(int nArg, string def)
	{
		var t = API.Type(nArg);
		return t is LuaType.LUA_TNONE or LuaType.LUA_TNIL ?
			def : CheckString(nArg);
	}

	private int TypeError(int index, string typeName)
	{
		var msg = $"{typeName} expected, got {TypeName(index)}";
		PushString(msg);
		return ArgError(index, msg);
	}

	private void TagError(int index, LuaType t) => 
		TypeError(index, TypeName(t));

	public void CheckType(int index, LuaType t)
	{
		if (Type(index) != t) TagError(index, t);
	}

	public void ArgCheck(bool cond, int nArg, string extraMsg)
	{
		if (!cond) ArgError(nArg, extraMsg);
	}

	public int ArgError(int nArg, string extraMsg)
	{
		var ar = new LuaDebug();
		if (!GetStack(ar, 0)) // no stack frame ?
			return Error("Bad argument {0} ({1})", nArg, extraMsg);

		GetInfo(ar, "n");
		if (ar.NameWhat == "method")
		{
			nArg--; // Do not count 'self'
			if (nArg == 0) // Error is in the self argument itself?
				return Error("Calling '{0}' on bad self", ar.Name);
		}
		ar.Name ??= PushGlobalFuncName(ar) ? ToString(-1)! : "?";
		return Error("Bad argument {0} to '{1}' ({2})",
			nArg, ar.Name, extraMsg);
	}

	public string TypeName(int index) => API.TypeName(API.Type(index));

	public bool GetMetaField(int obj, string name)
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

	public bool CallMeta(int obj, string name)
	{
		obj = AbsIndex(obj);
		if (!GetMetaField(obj, name)) // No metafield?
			return false;

		PushValue(obj);
		Call(1, 1);
		return true;
	}

	private void PushFuncName(LuaDebug ar)
	{
		if (ar.NameWhat.Length > 0 && ar.NameWhat[0] != '\0') // Is there a name?
			PushString($"function '{ar.Name}'");
		else if (ar.What.Length > 0 && ar.What[0] == 'm') // Main?
			PushString("main chunk");
		else if (ar.What.Length > 0 && ar.What[0] == 'C')
		{
			if (PushGlobalFuncName(ar))
			{
				PushString($"function '{API.ToString(-1)}'");
				Remove(-2); // Remove name
			}
			// !! Maybe is a function pushed from C#
			else if (Top.V.AsLuaClosure() is { Proto.Name: {} fName })
			{
				PushString($"function '{fName}'");
			}
			else if (Top.V.AsCSClosure() is {} csClosure)
			{
				PushString($"function '{csClosure.Name}'");
			}
			else
				PushString("?");
		}
		else
			PushString($"function <{ar.ShortSrc}:{ar.LineDefined}>");
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

	public void Traceback(
		ILuaState otherLua, string? msg = null, int level = 0) =>
		DoTraceback(otherLua, msg, level);

	public string DoTraceback(
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
		return ToString(-1)!;
	}

	public int Len(int index)
	{
		API.Len(index);

		var l = API.ToIntegerX(-1, out var isNum);
		if (!isNum) Error("Object length is not a number");
		Pop(1);
		return l;
	}

	public ThreadStatus LoadBuffer(string s, string? name = null) => 
		LoadBufferX(s, name);

	public ThreadStatus LoadBufferX(
		string s, string? name = null, string? mode = null)
	{
		var loadInfo = new StringLoadInfo(s);
		return Load(loadInfo, name, mode);
	}

	public ThreadStatus LoadBytes(byte[] bytes, string name)
	{
		var loadInfo = new BytesLoadInfo(bytes);
		return API.Load(loadInfo, name, null);
	}

	private static ThreadStatus ErrFile(string what, int fNameIdx) => 
		ThreadStatus.LUA_ERRFILE;

	public ThreadStatus LoadFile(string? filename) => 
		LoadFileX(filename, null);

	public ThreadStatus LoadFileX(string? filename, string? mode)
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
			status = API.Load(loadInfo, API.ToString(-1)!, mode);
		}
		catch (LuaRuntimeException e)
		{
			API.PushString($"Cannot open {filename}: {e.Message}");
			return ThreadStatus.LUA_ERRFILE;
		}

		API.Remove(fNameIdx);
		return status;
	}

	public ThreadStatus LoadString(string s, string? name = null) =>
		LoadBuffer(s, name);

	public ThreadStatus DoString(string s, string? name = null)
	{
		var status = LoadString(s, name);
		return status != ThreadStatus.LUA_OK ? 
			status :
			PCall(0, LuaDef.LUA_MULTRET);
	}

	public ThreadStatus LoadProto(LuaProto proto, string? name = null)
	{
		var loadInfo = new ProtoLoadInfo(proto);
		return Load(loadInfo, name);
	}

	public ThreadStatus Compile(string name, string code)
	{
		var res = LoadString(code);
		if (res == ThreadStatus.LUA_OK) SetGlobal(name);
		return res;
	}

	public ThreadStatus DoFile(string filename)
	{
		var status = LoadFile(filename);
		return status != ThreadStatus.LUA_OK 
			? status 
			: PCall(0, LuaDef.LUA_MULTRET, 0);
	}
	
	public ThreadStatus DoProto(LuaProto proto, string? name = null)
	{
		var status = LoadProto(proto, name);
		return status != ThreadStatus.LUA_OK 
			? status
			: PCall(0, LuaDef.LUA_MULTRET);
	}

	public string Gsub(string src, string pattern, string rep)
	{
		var res = src.Replace(pattern, rep);
		PushString(res);
		return res;
	}
		
	public string ToStringX(int index)
	{
		if (CallMeta(index, "__tostring")) 
			return ToString(-1)!; // No metafield?

		switch (Type(index))
		{
			case LuaType.LUA_TNUMBER:
			case LuaType.LUA_TINT64:
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
				API.PushString($"{TypeName(index)}: {ToObject(index).GetHashCode():X}");
				break;
		}

		return ToString(-1)!;
	}

	public void OpenLibs(bool global = true)
	{
		Span<NameFuncPair> define = 
		[
			LuaBaseLib.NameFuncPair,
			//
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
			Open(t, global);
		}
	}
	
	public void OpenSafeLibs(bool global = true)
	{
		Span<NameFuncPair> define = 
		[
			LuaBaseLib.SafeNameFuncPair,
			//
			LuaBitLib.NameFuncPair,
			LuaCoroLib.NameFuncPair,
			LuaDebugLib.SafeNameFuncPair,
			LuaEncLib.NameFuncPair,
			//LuaFFILib.NameFuncPair,
			//LuaIOLib.NameFuncPair,
			LuaMathLib.NameFuncPair,
			LuaOSLib.SafeNameFuncPair,
			LuaPkgLib.NameFuncPair,
			LuaStrLib.SafeNameFuncPair,
			LuaTableLib.NameFuncPair,
		];

		foreach (var t in define)
		{
			Open(t, global);
		}
	}

	public void Open(NameFuncPair library, bool global = true)
	{
		Require(library.Name, library.Func, global);
		Pop(1);
	}

	public void Require(
		string moduleName, CsDelegate openFunc, bool global)
	{
		PushCsDelegate(openFunc);
		PushString(moduleName);
		Call(1, 1);
		GetSubTable(LuaDef.LUA_REGISTRYINDEX, LuaDef.LUA_LOADED);
		PushValue(-2);
		SetField(-2, moduleName);
		Pop(1);
		if (global)
		{
			PushValue(-1);
			SetGlobal(moduleName);
		}
	}

	public int GetSubTable(int index, string fname)
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

	public void NewLibTable(ReadOnlySpan<NameFuncPair> define) => 
		CreateTable(0, define.Length);

	public void NewLib(ReadOnlySpan<NameFuncPair> define)
	{
		NewLibTable(define);
		SetFuncs(define, 0);
	}

	public void SetFuncs(ReadOnlySpan<NameFuncPair> list, int nup)
	{
		// TODO: Check Version
		CheckStack(nup, "Too many upvalues");
		foreach (var (name, func) in list)
		{
			for (var i = 0; i < nup; ++i)
				PushValue(-nup);
			PushCsDelegate(func, nup);
			SetField(-(nup + 2), name);
		}
		Pop(nup);
	}

	private bool FindField(int objIndex, int level)
	{
		if (level == 0 || !IsTable(-1))
			return false; // Not found

		PushNil(); // Start 'next' loop
		while (Next(-2)) // for each pair in table
		{
			if (Type(-2) == LuaType.LUA_TSTRING) // ignore non-string keys
			{
				if (API.RawEqual(objIndex, -1)) // found object?
				{
					Pop(1); // remove value (but keep name)
					return true;
				}

				if (FindField(objIndex, level - 1)) // try recursively
				{
					Remove(-2); // remove table (but keep name)
					PushString(".");
					API.Insert(-2); // place '.' between the two names
					Concat(3);
					return true;
				}
			}
			Pop(1); // remove value
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

	public int RefTo(int t)
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

	public void Unref(int t, int reference)
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
	private static double Arith(LuaOp op, double v1, double v2)
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
		if (v.V.IsNil())
			return true;
		if (v.V.IsBoolean() && v.V.AsBool() == false)
			return true;
		return false;
	}

	private static bool ToString(StkId o) => 
		o.V.IsString() || ToStringX(o);

	private LuaClosure? GetCurrentLuaFunc(CallInfo ci) => 
		ci.IsLua ? Stack[ci.FuncIndex].AsLuaClosure() : null;

	private int GetCurrentLine(CallInfo ci)
	{
		LuaUtil.Assert(ci.IsLua);
		var cl = Stack[ci.FuncIndex].AsLuaClosure()!;
		return cl.Proto.GetFuncLine(ci.CurrentPc);
	}
	#endregion
	#region VM.cs
	private const int MAXTAGLOOP = 100;

	private ref struct ExecuteEnvironment(LuaState L, Span<TValue> K)
	{
		public readonly Span<TValue> K = K;
		public int 				Base;
		public Instruction 		I;

		public int RAIndex => Base + I.GETARG_A();

		private int RBIndex => Base + I.GETARG_B();
		
		public StkId RA => new (ref L.Stack[RAIndex]);

		public StkId RB => new (ref L.Stack[RBIndex]);

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private StkId RK(int x) => 
			Instruction.ISK(x) 
				? new StkId(ref K[Instruction.INDEXK(x)])
				: new StkId(ref L.Stack[Base + x]);

		public StkId RKB => RK(I.GETARG_B());

		public StkId RKC => RK(I.GETARG_C());
	}

	private void Execute()
	{
		var ci = CI;
		newFrame:
		LuaUtil.Assert(ci == CI);
		var cl = Stack[ci.FuncIndex].AsLuaClosure()!;
		var env = new ExecuteEnvironment(
				this, CollectionsMarshal.AsSpan(cl.Proto.K))
		{ Base = ci.BaseIndex };

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

			// WARNING: several calls may realloc the stack and invalidate 'ra'
			var raIdx = env.RAIndex;
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
					LuaUtil.Assert(ci.SavedPc.Value.GET_OPCODE() == OpCode.OP_EXTRAARG);
					var rb = env.K[ci.SavedPc.ValueInc.GETARG_Ax()];
					ra.Set(new StkId(ref rb));
					break;
				}

				case OpCode.OP_LOADBOOL:
				{
					ra.V.SetBool(i.GETARG_B() != 0);
					if (i.GETARG_C() != 0)
						ci.SavedPc.Index += 1; // Skip next instruction (if C)
					break;
				}

				case OpCode.OP_LOADNIL:
				{
					var b = i.GETARG_B();
					var index = raIdx;
					do { Stack[index++].SetNil(); } 
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
					GetTable(cl.Upvals[b].StkId, key, ra);
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
					GetTable(tbl, key, ra);
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
					SetTable(cl.Upvals[a].StkId, key, val);
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
					SetTable(ra, key, val);
					break;
				}

				case OpCode.OP_NEWTABLE:
				{
					var b = i.GETARG_B();
					var c = i.GETARG_C();
					var tbl = new LuaTable(this);
					ra.V.SetTable(tbl);
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
					var ra1 = Ref(raIdx + 1);
					var rb  = env.RB;
					ra1.Set(rb);
					GetTable(rb, env.RKC, ra);
					env.Base = ci.BaseIndex;
					break;
				}

				case OpCode.OP_ADD:
				{
					var rkb = env.RKB;
					var rkc = env.RKC;
					if (rkb.V.IsNumber() && rkc.V.IsNumber())
						ra.V.SetDouble(rkb.V.NValue + rkc.V.NValue);
					else if (rkb.V.IsInt64() && rkc.V.IsInt64())
						ra.V.SetInt64(rkb.V.AsInt64() + rkc.V.AsInt64());
					else
						Arith(ra, rkb, rkc, TMS.TM_ADD);

					env.Base = ci.BaseIndex;
					break;
				}

				case OpCode.OP_SUB:
				{
					var rkb = env.RKB;
					var rkc = env.RKC;
					if (rkb.V.IsNumber() && rkc.V.IsNumber())
						ra.V.SetDouble(rkb.V.NValue - rkc.V.NValue);
					else if (rkb.V.IsInt64() && rkc.V.IsInt64())
						ra.V.SetInt64(rkb.V.AsInt64() - rkc.V.AsInt64());
					else
						Arith(ra, rkb, rkc, TMS.TM_SUB);
					env.Base = ci.BaseIndex;
					break;
				}

				case OpCode.OP_MUL:
				{
					var rkb = env.RKB;
					var rkc = env.RKC;
					if (rkb.V.IsNumber() && rkc.V.IsNumber())
						ra.V.SetDouble(rkb.V.NValue * rkc.V.NValue);
					else if (rkb.V.IsInt64() && rkc.V.IsInt64())
						ra.V.SetInt64(rkb.V.AsInt64() * rkc.V.AsInt64());
					else
						Arith(ra, rkb, rkc, TMS.TM_MUL);
					env.Base = ci.BaseIndex;
					break;
				}

				case OpCode.OP_DIV:
				{
					var rkb = env.RKB;
					var rkc = env.RKC;
					if (rkb.V.IsNumber() && rkc.V.IsNumber())
						ra.V.SetDouble(rkb.V.NValue / rkc.V.NValue);
					else if (rkb.V.IsInt64() && rkc.V.IsInt64())
						ra.V.SetInt64(rkb.V.AsInt64() / rkc.V.AsInt64());
					else
						Arith(ra, rkb, rkc, TMS.TM_DIV);
					env.Base = ci.BaseIndex;
					break;
				}

				case OpCode.OP_MOD:
				{
					var rkb = env.RKB;
					var rkc = env.RKC;
					if (rkb.V.IsNumber() && rkc.V.IsNumber())
					{
						var v1 = rkb.V.NValue;
						var v2 = rkc.V.NValue;
						ra.V.SetDouble(v1 - Math.Floor(v1 / v2) * v2);
					}
					else
						Arith(ra, rkb, rkc, TMS.TM_MOD);

					env.Base = ci.BaseIndex;
					break;
				}

				case OpCode.OP_POW:
				{
					var rkb = env.RKB;
					var rkc = env.RKC;
					if (rkb.V.IsNumber() && rkc.V.IsNumber())
						ra.V.SetDouble(Math.Pow(rkb.V.NValue, rkc.V.NValue));
					else if (rkb.V.IsInt64() && rkc.V.IsInt64())
						ra.V.SetInt64((long)Math.Pow(rkb.V.AsInt64(), rkc.V.AsInt64()));
					else
						Arith(ra, rkb, rkc, TMS.TM_POW);
					env.Base = ci.BaseIndex;
					break;
				}

				case OpCode.OP_UNM:
				{
					var rb = env.RB;
					if (rb.V.IsNumber())
						ra.V.SetDouble(-rb.V.NValue);
					else if (rb.V.IsInt64())
						ra.V.SetInt64(-rb.V.AsInt64());
					else
					{
						Arith(ra, rb, rb, TMS.TM_UNM);
						env.Base = ci.BaseIndex;
					}
					break;
				}

				case OpCode.OP_NOT:
				{
					var rb = env.RB;
					ra.V.SetBool(IsFalse(rb));
					break;
				}

				case OpCode.OP_LEN:
				{
					ObjLen(ra, env.RB);
					env.Base = ci.BaseIndex;
					break;
				}

				case OpCode.OP_CONCAT:
				{
					var b = i.GETARG_B();
					var c = i.GETARG_C();
					TopIndex = env.Base + c + 1;
					Concat(c - b + 1);
					env.Base = ci.BaseIndex;

					//raIdx = env.Base + i.GETARG_A();
					ra = env.RA; // 'V_Concat' may invoke TMs and move the stack
					var rb = env.RB;
					ra.Set(rb);

					TopIndex = ci.TopIndex; // Restore top
					break;
				}

				case OpCode.OP_JMP:
				{
					DoJump(ci, i, 0);
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
						DoNextJump(ci);
					}
					env.Base = ci.BaseIndex;
					break;
				}

				case OpCode.OP_LT:
				{
					var expectCmpResult = i.GETARG_A() != 0;
					if (LessThan(env.RKB, env.RKC) != expectCmpResult)
						ci.SavedPc.Index += 1;
					else
						DoNextJump(ci);
					env.Base = ci.BaseIndex;
					break;
				}

				case OpCode.OP_LE:
				{
					var expectCmpResult = i.GETARG_A() != 0;
					if (LessEqual(env.RKB, env.RKC) != expectCmpResult)
						ci.SavedPc.Index += 1;
					else
						DoNextJump(ci);
					env.Base = ci.BaseIndex;
					break;
				}

				case OpCode.OP_TEST:
				{
					if ((i.GETARG_C() != 0) ? IsFalse(ra) : !IsFalse(ra))
					{
						ci.SavedPc.Index += 1;
					}
					else DoNextJump(ci);

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
						DoNextJump(ci);
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
					if (PreCall(ra, nResults)) // C# function?
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
						
					LuaUtil.Assert(i.GETARG_C() - 1 == LuaDef.LUA_MULTRET);

					var called = PreCall(ra, LuaDef.LUA_MULTRET);

					// C# function ?
					if (called)
						env.Base = ci.BaseIndex;

					// LuaFunction
					else
					{
						var nci = CI;				// called frame
						var oci = BaseCI[CI.Index - 1]; // caller frame
						var nfunc = Ref(nci.FuncIndex);// called function
						var ofunc = Ref(oci.FuncIndex);// caller function
						var ncl = nfunc.V.AsLuaClosure()!;
						//var ocl = ofunc.V.AsLuaClosure()!;

						// Last stack slot filled by 'precall'
						var lim = nci.BaseIndex + ncl.Proto.NumParams;

						// close all upvalues from previous call
						if (cl.Proto.P.Count > 0) Close(env.Base);

						// Move new frame into old one
						var nIndex = nci.FuncIndex;
						var oIndex = oci.FuncIndex;
						while (nIndex < lim) 
							Stack[oIndex++].SetObj(Ref(nIndex++));

						oci.BaseIndex = oIndex + (nci.BaseIndex - nIndex); // correct base
						oci.TopIndex = oIndex + (TopIndex - nIndex); // correct top
						TopIndex = oci.TopIndex;
						oci.SavedPc = nci.SavedPc;
						oci.CallStatus |= CallStatus.CIST_TAIL; // function was tail called
						ci = CI = oci; // remove new frame

						var ocl = ofunc.V.AsLuaClosure()!;
						LuaUtil.Assert(TopIndex == oci.BaseIndex + ocl.Proto.MaxStackSize);

						goto newFrame; // restart luaV_execute over new Lua function
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
					if (cl.Proto.P.Count > 0) Close(env.Base);
					b = PosCall(raIdx);
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
					var ra1 = Ref(raIdx + 1);
					var ra2 = Ref(raIdx + 2);
					var ra3 = Ref(raIdx + 3);
						
					var step 	= ra2.V.NValue;
					var idx 	= ra.V.NValue + step;	// increment index
					var limit = ra1.V.NValue;

					if ((0 < step) ? idx <= limit : limit <= idx)
					{
						ci.SavedPc.Index += i.GETARG_sBx(); // jump back
						ra.V.SetDouble(idx); // updateinternal index...
						ra3.V.SetDouble(idx);// ... and external index
					}

					break;
				}

				case OpCode.OP_FORPREP:
				{
					var init = new TValue();
					var limit = new TValue();
					var step = new TValue();

					var ra1 = Ref(raIdx + 1);
					var ra2 = Ref(raIdx + 2);

					// WHY: Why limit is not used ?

					if (!ToNumber(ra, new StkId(ref init)))
						RunError("'for' initial value must be a number");
					if (!ToNumber(ra1, new StkId(ref limit)))
						RunError("'for' limit must be a number");
					if (!ToNumber(ra2, new StkId(ref step)))
						RunError("'for' step must be a number");

					// Replace values in case they were strings initially
					ra1.Set(new StkId(ref limit));
					ra2.Set(new StkId(ref step));
					
					ra.V.SetDouble(init.NValue - step.NValue);
					ci.SavedPc.Index += i.GETARG_sBx();
					break;
				}

				case OpCode.OP_TFORCALL:
				{
					var rai = raIdx;
					var cbi = raIdx + 3;

					if (!ra.V.IsFunction())
					{
						if (ra.V.IsList())
						{
							TopIndex = cbi + 1;
							Stack[TopIndex - 1].SetCSClosure(LuaListLib.PairsCl);
						}
						else if (!TryGetTMByObj(ra, TMS.TM_ITER, out var tm)
						    || !tm.V.IsFunction())
						{
							TopIndex = cbi;
							GetGlobal("pairs");
							if (!Stack[cbi].IsFunction())
							{
								TypeError(ra, "iterate");
							}
						}
						else
						{
							Stack[cbi].SetObj(tm);
						}

						Stack[cbi + 1].SetObj(ra);
						TopIndex = cbi + 2;
						// PROTECT
						Call(Ref(cbi), 3, true);
						env.Base = ci.BaseIndex;
						//
						TopIndex = CI.TopIndex;
						rai = env.RAIndex;
						//ra = env.RA;
						cbi = rai + 3;
						Stack[rai + 2].SetObj(Ref(cbi + 2));
						Stack[rai + 1].SetObj(Ref(cbi + 1));
						Stack[rai].SetObj(Ref(cbi));
						TopIndex = rai + 3;
					}
					
					Stack[cbi + 2].SetObj(Ref(rai + 2));
					Stack[cbi + 1].SetObj(Ref(rai + 1));
					Stack[cbi].SetObj(Ref(rai));

					var callBase = Ref(cbi);
					TopIndex = cbi + 3; // func. +2 args (state and index)

					Call(callBase, i.GETARG_C(), true);

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
					LuaUtil.Assert(i.GET_OPCODE() == OpCode.OP_TFORLOOP);
					goto l_tforloop;
				}

				case OpCode.OP_TFORLOOP:
					l_tforloop:
				{
					var ra1 = Ref(raIdx + 1);
					if (!ra1.V.IsNil())	// continue loop?
					{
						ra.Set(ra1);
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
						LuaUtil.Assert(ci.SavedPc.Value.GET_OPCODE() == OpCode.OP_EXTRAARG);
						c = ci.SavedPc.ValueInc.GETARG_Ax();
					}

					var tbl = ra.V.AsTable();
					LuaUtil.Assert(tbl != null);

					var last = ((c - 1) * LuaDef.LFIELDS_PER_FLUSH) + n;
					var rai = raIdx;
					for (; n > 0; --n) tbl!.SetInt(last--, Ref(rai + n));
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
					PushClosure(p, cl.Upvals, env.Base, ref ra.V);
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
						CheckStackX(n);
						raIdx = env.Base + i.GETARG_A(); // previous call may change the stack
						ra = env.RA; 

						TopIndex = raIdx + n;
					}

					var p = raIdx;
					var q = env.Base - n;
					for (var j = 0; j < b; ++j) 
					{
						if (j < n)
							Stack[p++].SetObj(Ref(q++));
						else
							Stack[p++].SetNil();
					}
					break;
				}

				case OpCode.OP_EXTRAARG:
				{
					LuaUtil.Assert(false);
					NotImplemented(i);
					break;
				}

				default:
					NotImplemented(i);
					break;
			}
		}
	}

	private static void NotImplemented(Instruction i)
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

		return TryGetTM(et, tm, out val);
	}

	private void GetTable(StkId t, StkId key, StkId val)
	{
		// Index a list?
		if (t.V.AsList() is {} list)
		{
			var n = new TValue();
			if (!ToNumber(key, new StkId(ref n)))
				SimpleTypeError(t, "index");

			var index = (int)n.NValue;
			if (index < 0 || index >= list.Count)
				RunError("Index '{0}' not in range {1} ..< {2}", index, 0, list.Count);
			
			val.Set(new StkId(ref CollectionsMarshal.AsSpan(list)[index]));
			return;
		}
		
		for (var loop = 0; loop < MAXTAGLOOP; ++loop) 
		{
			StkId tmObj;
			if (t.V.IsTable()) 
			{
				var tbl = t.V.AsTable()!;
				tbl.TryGet(key, out var res);
				if (!res.V.IsNil()) 
				{
					val.Set(res);
					return;
				}
				
				if (!TryFastTM(tbl.MetaTable, TMS.TM_INDEX, out tmObj)) 
				{
					val.Set(res);
					return;
				}

				// else will try the tag method
			}
			else 
			{
				if (!TryGetTMByObj(t, TMS.TM_INDEX, out tmObj) || tmObj.V.IsNil()) 
					SimpleTypeError(t, "index");
			}

			if (tmObj.V.IsFunction()) 
			{
				CallTM(tmObj, t, key, val, true);
				return;
			}

			t = tmObj;
		}
		RunError("Loop in gettable");
	}

	private void SetTable(StkId t, StkId key, StkId val)
	{
		// Index a list?
		if (t.V.AsList() is {} list)
		{
			var n = new TValue();
			if (!ToNumber(key, new StkId(ref n)))
				SimpleTypeError(t, "index");

			var index = (int)n.NValue;
			if (index < 0 || index >= list.Count)
				RunError("Index '{0}' not in range {1} ..< {2}", index, 0, list.Count);
			
			list[index] = val.V;
			return;
		}
		
		for (var loop = 0; loop < MAXTAGLOOP; ++loop)
		{
			StkId tmObj;
			if (t.V.IsTable()) 
			{
				var tbl = t.V.AsTable()!;
				tbl.TryGet(key, out var oldVal);
				if (!oldVal.V.IsNil())
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
				if (!TryGetTMByObj(t, TMS.TM_NEWINDEX, out tmObj) || tmObj.V.IsNil())
					SimpleTypeError(t, "index");
			}

			if (tmObj.V.IsFunction()) 
			{
				CallTM(tmObj, t, key, val, false);
				return;
			}

			t = tmObj;
		}
		RunError("loop in settable");
	}

	private void PushClosure(
		LuaProto p, LuaUpValue[] encup, int stackBase, ref TValue ra)
	{
		var cl = (
			p.Upvalues.Count == 0 ||
			p.Upvalues is [{ IsEnv: true }])
			? p.Pure : new LuaClosure(p);

		ra.SetLuaClosure(cl);
		for (var i = 0; i < p.Upvalues.Count; ++i)
		{
			// ULDebug.Log("~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~ V_PushClosure i:" + i);
			// ULDebug.Log("~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~ V_PushClosure InStack:" + p.Upvalues[i].InStack);
			// ULDebug.Log("~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~ V_PushClosure Index:" + p.Upvalues[i].Index);

			if (p.Upvalues[i].InStack) // Upvalue refers to local variable
				cl.Upvals[i] = FindUpval(
					stackBase + p.Upvalues[i].Index);
			else	// Get upvalue from enclosing function
				cl.Upvals[i] = encup[p.Upvalues[i].Index];
		}
	}

	private void ObjLen(StkId ra, StkId rb)
	{
		var tmObj = StkId.Nil;

		var rbt = rb.V.AsTable();
		if (rbt != null)
		{
			if (TryFastTM(rbt.MetaTable, TMS.TM_LEN, out tmObj))
				goto calltm;
			ra.V.SetDouble(rbt.Length);
			return;
		}

		if (rb.V.AsString() is {} rbs)
		{
			ra.V.SetDouble(rbs.Length);
			return;
		}
		
		if (rb.V.AsList() is {} list)
		{
			ra.V.SetDouble(list.Count);
			return;
		}

		if (!TryGetTMByObj(rb, TMS.TM_LEN, out tmObj) || tmObj.V.IsNil())
			TypeError(rb, "get length of");

		calltm:
		CallTM(tmObj, rb, rb, ra, true);
	}

	private void Concat(int total)
	{
		LuaUtil.Assert(total >= 2);

		do
		{
			var n = 2;
			var lhs = Ref(TopIndex - 2);
			var rhs = Ref(TopIndex - 1);
			if (!(lhs.V.IsString() || lhs.V.IsNumber()) || !ToString(rhs))
			{
				if (!CallBinTM(lhs, rhs, lhs, TMS.TM_CONCAT))
					ConcatError(lhs, rhs);
			}
			else if (rhs.V.AsString()!.Length == 0)
				ToString(lhs);
			else if (lhs.V.IsString() && lhs.V.AsString()!.Length == 0)
				lhs.V.SetObj(rhs);
			else
			{
				var sb = new StringBuilder();
				n = 0;
				for (; n < total; ++n)
				{
					var cur = Ref(TopIndex - (n + 1));

					if (cur.V.IsString())
						sb.Insert(0, cur.V.AsString());
					else if (cur.V.IsNumber())
						sb.Insert(0, cur.V.NValue);
					else
						break;
				}

				var dest = Ref(TopIndex - n);
				dest.V.SetString(sb.ToString());
			}
			total -= n - 1;
			TopIndex -= (n - 1);
		} while (total > 1);
	}

	private void DoJump(CallInfo ci, Instruction i, int e)
	{
		var a = i.GETARG_A();
		if (a > 0) Close(ci.BaseIndex + (a - 1));
		ci.SavedPc += i.GETARG_sBx() + e;
	}

	private void DoNextJump(CallInfo ci)
	{
		var i = ci.SavedPc.Value;
		DoJump(ci, i, 1);
	}

	private static bool ToNumber(StkId obj, StkId n)
	{
		if (obj.V.IsNumber()) 
		{
			n.V.SetDouble(obj.V.NValue);
			return true;
		}

		if (obj.V.IsInt64())
		{
			n.V.SetDouble(obj.V.AsInt64());
			return true;
		}

		if (obj.V.IsString()) 
		{
			if (LuaUtil.Str2Decimal(
				    obj.V.AsString().AsSpan(), out var val))
			{
				n.V.SetDouble(val);
				return true;
			}
		}

		return false;
	}

	private static bool ToStringX(StkId v)
	{
		if (v.V.IsNumber())
		{
			v.V.SetString(v.V.NValue.ToString(
				CultureInfo.InvariantCulture));
			return true;	
		}

		if (v.V.IsInt64())
		{
			v.V.SetString(v.V.AsInt64().ToString(
				CultureInfo.InvariantCulture));
			return true;	
		}
		
		return false;
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
		
		IncTop().Set(f);  // Push function
		IncTop().Set(p1); // Push 1st argument
		IncTop().Set(p2); // Push 2nd argument

		if (!hasRes) // No result? p3 is 3rd argument
		{
			IncTop().Set(p3);
		}
		CheckStackX(0);
		Call(func, (hasRes ? 1 : 0), CI.IsLua);
		if (hasRes) // if has result, move it to its place
		{
			--TopIndex;
			Ref(result).Set(Top);
		}
	}

	private bool CallBinTM(StkId p1, StkId p2, StkId res, TMS tm)
	{
		if (!TryGetTMByObj(p1, tm, out var tmObj) || tmObj.V.IsNil())
		{
			if (!TryGetTMByObj(p2, tm, out tmObj))
				return false;
		}

		CallTM(tmObj, p1, p2, res, true);
		return true;
	}

	private void Arith(StkId ra, StkId rb, StkId rc, TMS op)
	{
		var nb = TValue.Nil();
		var nc = TValue.Nil();
		var rNb = new StkId(ref nb);
		var rNc = new StkId(ref nc);
		
		if (ToNumber(rb, rNb) && ToNumber(rc, rNc))
		{
			var res = Arith(TMS2OP(op), nb.NValue, nc.NValue);
			ra.V.SetDouble(res);
		}
		else if (!CallBinTM(rb, rc, ra, op))
		{
			ArithError(rb, rc);
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

	private bool LessThan(StkId lhs, StkId rhs)
	{
		// Compare number
		if (lhs.V.IsNumber() && rhs.V.IsNumber())
			return lhs.V.NValue < rhs.V.NValue;
		
		if (lhs.V.IsInt64() && rhs.V.IsInt64())
			return lhs.V.AsInt64() < rhs.V.AsInt64();

		// Compare string
		if (lhs.V.IsString() && rhs.V.IsString())
			return string.CompareOrdinal(lhs.V.AsString(), rhs.V.AsString()) < 0;

		var res = CallOrderTM(lhs, rhs, TMS.TM_LT, out var error);
		if (error)
		{
			OrderError(lhs, rhs);
			return false;
		}
		return res;
	}

	private bool LessEqual(StkId lhs, StkId rhs)
	{
		// Compare number
		if (lhs.V.IsNumber() && rhs.V.IsNumber())
			return lhs.V.NValue <= rhs.V.NValue;
		
		if (lhs.V.IsInt64() && rhs.V.IsInt64())
			return lhs.V.AsInt64() <= rhs.V.AsInt64();

		// Compare string
		if (lhs.V.IsString() && rhs.V.IsString())
			return string.CompareOrdinal(lhs.V.AsString(), rhs.V.AsString()) <= 0;

		// First try 'le'
		var res = CallOrderTM(rhs, rhs, TMS.TM_LE, out var error);
		if (!error) return res;

		// else try 'lt'
		res = CallOrderTM(rhs, lhs, TMS.TM_LT, out error);
		if (!error) return res;

		OrderError(rhs, rhs);
		return false;
	}

	private void FinishOp()
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
				var tmp = Ref(stackBase + i.GETARG_A());
				tmp.Set(Ref(--TopIndex));
				break;
			}

			case OpCode.OP_LE: case OpCode.OP_LT: case OpCode.OP_EQ:
			{
				var res = !IsFalse(Ref(TopIndex - 1));
				--TopIndex;
				// metamethod should not be called when operand is K
				LuaUtil.Assert(!Instruction.ISK(i.GETARG_B()));
				if (op == OpCode.OP_LE && // '<=' Using '<' instead?
				    (!TryGetTMByObj(Ref(stackBase + i.GETARG_B()), TMS.TM_LE, out var v)
				     || v.V.IsNil()))
				{
					res = !res; // invert result
				}

				var ci = BaseCI[ciIndex];
				LuaUtil.Assert(ci.SavedPc.Value.GET_OPCODE() == OpCode.OP_JMP);
				if ((res ? 1 : 0) != i.GETARG_A())
					if ((i.GETARG_A() == 0) == res) // Condition failed?
					{
						ci.SavedPc.Index++; // Skip jump instruction
					}
				break;
			}

			case OpCode.OP_CONCAT:
			{
				var top = Ref(TopIndex - 1); // Top when 'CallBinTM' was called
				var b = i.GETARG_B(); // First element to concatenate
				var total = TopIndex - 1 - (stackBase + b); // Yet to concatenate
				var tmp = Ref(TopIndex - 2);
				tmp.Set(top); // Put TM result in proper position
				if (total > 1) // Are there elements to concat?
				{
					--TopIndex;
					Concat(total);
				}
				// Move final result to final position
				var ci = BaseCI[ciIndex];
				var tmp2 = Ref(ci.BaseIndex + i.GETARG_A());
				tmp2.Set(Ref(TopIndex - 1));
				TopIndex = ci.TopIndex;
				break;
			}

			case OpCode.OP_TFORCALL:
			{
				var ci = BaseCI[ciIndex];
				LuaUtil.Assert(
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
				LuaUtil.Assert(false);
				break;
		}
	}

	internal bool RawEqualObj(StkId t1, StkId t2) => 
		(t1.V.Type == t2.V.Type) && EqualObject(t1, t2, true);

	private bool EqualObj(StkId t1, StkId t2, bool rawEq) => 
		(t1.V.Type == t2.V.Type) && EqualObject(t1, t2, rawEq);

	private bool TryGetEqualTM(LuaTable? mt1, LuaTable? mt2, TMS tm, out StkId val)
	{
		// No metamethod
		if (!TryFastTM(mt1, tm, out val)) return false;
		// Same metatables => same metamethods
		if (mt1 == mt2) return true;
		
		// No metamethod
		if (!TryFastTM(mt2, tm, out var tm2)) return false;

		// Same metamethods?
		if (RawEqualObj(val, tm2)) return true;

		return false;
	}

	private bool EqualObject(StkId t1, StkId t2, bool rawEq)
	{
		LuaUtil.Assert(t1.V.Type == t2.V.Type);
		var tm = StkId.Nil;
		switch (t1.V.Type)
		{
			case LuaType.LUA_TNIL:
				return true;
			case LuaType.LUA_TNUMBER:
				// ReSharper disable once CompareOfFloatsByEqualityOperator
				return t1.V.NValue == t2.V.NValue;
			case LuaType.LUA_TINT64:
				return t1.V.AsInt64() == t2.V.AsInt64();
			case LuaType.LUA_TBOOLEAN:
				return t1.V.AsBool() == t2.V.AsBool();
			case LuaType.LUA_TSTRING:
				return t1.V.AsString() == t2.V.AsString();
			case LuaType.LUA_TUSERDATA:
				throw new NotImplementedException();
			case LuaType.LUA_TLIST:
			{
				var l1 = t1.V.AsList()!;
				var l2 = t2.V.AsList()!;
				if (ReferenceEquals(l1, l2)) return true;
				if (rawEq) return false;
				if (l1.Count != l2.Count) return false;

				var s1 = CollectionsMarshal.AsSpan(l1);
				var s2 = CollectionsMarshal.AsSpan(l2);
				for (var i = 0; i < l1.Count; ++i)
				{
					if (!EqualObject(
						    new StkId(ref s1[i]), new StkId(ref s2[i]), rawEq))
						return false;
				}

				return true;
			}
			case LuaType.LUA_TTABLE:
			{
				var tbl1 = t1.V.AsTable()!;
				var tbl2 = t2.V.AsTable()!;
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

		if (what[0] == '>')
		{
			func = Ref(--TopIndex);

			LuaUtil.ApiCheck(func.V.IsFunction(), "Function expected");
		}
		else
		{
			ci = BaseCI[ar.ActiveCIIndex];
			func = Ref(ci.FuncIndex);
			LuaUtil.Assert(func.V.IsFunction());
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
					LuaUtil.Assert(func.V.IsFunction());
					if (func.V.IsLuaClosure()) 
					{
						var lcl = func.V.AsLuaClosure()!;
						ar.NumUps = lcl.Length;
						ar.IsVarArg = lcl.Proto.IsVarArg;
						ar.NumParams = lcl.Proto.NumParams;
					}
					else if (func.V.IsCsClosure()) 
					{
						var ccl = func.V.AsCSClosure()!;
						ar.NumUps = ccl.Upvals.Length;
						ar.IsVarArg = true;
						ar.NumParams = 0;
					}
					else throw new NotImplementedException();
					break;
				case 't':
					ar.IsTailCall =
						(ci != null) &&
						((ci.CallStatus & CallStatus.CIST_TAIL) != 0);
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
		LuaUtil.Assert(func.V.IsFunction());
		if (func.V.IsLuaClosure()) 
		{
			var lcl = func.V.AsLuaClosure()!;
			var p = lcl.Proto;
			var lineInfo = p.LineInfo;
			var t = new LuaTable(this);
			var v = new TValue();

			Top.V.SetTable(t);
			IncrTop();

			v.SetBool(true);
			var rv = new StkId(ref v);
			foreach (var t1 in lineInfo)
				t.SetInt(t1, rv);
		}
		else if (func.V.IsCsClosure()) 
		{
			Top.V.SetNil();
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
		LuaUtil.Assert(func.V.IsFunction());
		if (func.V.IsLuaClosure())
		{
			var lcl = func.V.AsLuaClosure()!;
			var p = lcl.Proto;
			ar.Source = string.IsNullOrEmpty(p.Source) ? "=?" : p.Source;
			ar.LineDefined = p.LineDefined;
			ar.LastLineDefined = p.LastLineDefined;
			ar.What = (ar.LineDefined == 0) ? "main" : "C";
		}
		else if (func.V.IsCsClosure())
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

		string? root = null;
		var p = proto;
		while (p.Parent != null) p = p.Parent;
		root = p.RootName;
		
		var src = root;

		if (src == null)
			src = proto.Source;
		if (src == "")
			src = "?";

		if (src == proto.Source && src.Contains('\n'))
		{
			// Get portion
			var lines = src.Replace("\r","").Split('\n');
			src = (lines.Length >= line ? lines[line - 1] : "?").TrimStart();
		}

		// Cannot use PushString, because PushString is part of the API interface
		// The ApiIncrTop function in the API interface will check if Top exceeds CI.Top, causing an error
		// api.PushString(msg);
		O_PushString($"{src}:{line}: {msg}");
	}

	internal void RunError(string fmt, params object[] args)
	{
		var msg = string.Format(fmt, args);
		AddInfo(msg);
		ErrorMsg(msg);
	}

	private void ErrorMsg(string msg)
	{
		if (ErrFunc != 0) // Is there an error handling function?
		{
			var errFunc = Ref(ErrFunc);

			if (!errFunc.V.IsFunction())
				Throw(ThreadStatus.LUA_ERRERR, msg);

			var below = Ref(TopIndex - 1);
			Top.V.SetObj(below);
			below.V.SetObj(errFunc);
			IncrTop();

			Call(below, 1, false);
		}

		Throw(ThreadStatus.LUA_ERRRUN, msg);
	}

	private static string UpValueName(LuaProto p, int uv) => 
		p.Upvalues[uv].Name;

	private string? GetUpvalName(CallInfo ci, StkId o, out string name)
	{
		var func = Ref(ci.FuncIndex);
		LuaUtil.Assert(func.V.IsFunction() && func.V.IsLuaClosure());

		var lcl = func.V.AsLuaClosure()!;
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
			if (val.IsString()) 
			{ // literal constant?
				name = val.AsString();
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

				case OpCode.OP_TFORCALL: 
				// effect all regs above its base
					if (reg >= a + 2)
						setReg = pc;
					break;

				case OpCode.OP_CALL:
				case OpCode.OP_TAILCALL: 
				// effect all registers above base
					if (reg >= a)
						setReg = pc;
					break;
					
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
					if (Coder.TestAMode(op) && reg == a) setReg = pc;
					break;
			}
		}
		return setReg;
	}

	private string? GetObjName(
		LuaProto proto, int lastpc, int reg, out string? name)
	{
		name = null;
		if (GetLocalName(proto, reg + 1, lastpc) is {} lName)
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
					? GetLocalName(proto, t + 1, pc)
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
				if (val.IsString())
				{
					name = val.AsString();
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
#pragma warning disable CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type
			fixed (TValue* arr = &Stack[0])
#pragma warning restore CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type
			{
				var value = arr;
				for (var p = ci.BaseIndex; p < ci.TopIndex; p++, value++)
					if (value == oIdx) return true;
			}
		}
		return false;
	}

	private void SimpleTypeError(StkId o, string op)
	{
		var t = ObjTypeName(o);
		RunError("Attempt to {0} a {1} value", op, t);
	}

	private void TypeError(StkId o, string op)
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
				var lcl = Stack[ci.FuncIndex].AsLuaClosure()!;
				kind = GetObjName(lcl.Proto, ci.CurrentPc,
					(Index(o) - ci.BaseIndex), out name);
			}
		}
		if (kind != null)
			RunError("Attempt to {0} {1} '{2}' (a {3} value)",
				op, kind, name!, t);
		else
			RunError("Attempt to {0} a {1} value", op, t);
	}

	private void ArithError(StkId p1, StkId p2)
	{
		var n = new TValue();
		if (!ToNumber(p1, new StkId(ref n))) p2 = p1; // first operand is wrong

		TypeError(p2, "Perform arithmetic on");
	}

	private void OrderError(StkId p1, StkId p2)
	{
		var t1 = ObjTypeName(p1);
		var t2 = ObjTypeName(p2);
		if (t1 == t2)
			RunError("Attempt to compare two {0} values", t1);
		else
			RunError("Attempt to compare {0} with {1}", t1, t2);
	}

	private void ConcatError(StkId p1, StkId p2)
	{
		if (p1.V.IsString() || p1.V.IsNumber()) p1 = p2;
		LuaUtil.Assert(!(p1.V.IsString() || p1.V.IsNumber()));
		TypeError(p1, "concatenate");
	}
	#endregion
}