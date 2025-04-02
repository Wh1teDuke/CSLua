using System.Runtime.InteropServices;
using CSLua.Util;

// ReSharper disable InconsistentNaming

namespace CSLua.Parse;

using InstructionPtr = Pointer<Instruction>;

public sealed class FuncState
{
	public FuncState? Prev;
	public LuaProto Proto = new();
	public BlockCnt Block = null!;
	public LuaState State = null!;
	public LLex		Lexer = null!;

	public readonly Dictionary<TValue, int> H = [];

	public int NumActVar;
	public int FreeReg;

	public int Pc;
	public int LastTarget;
	public int Jpc;
	public int FirstLocal;

	public InstructionPtr GetCode(ExpDesc e) => new(Proto.Code, e.Info);
}

public sealed class BlockCnt
{
	public BlockCnt? Previous;
	public int		FirstLabel;
	public int		FirstGoto;
	public int		NumActVar;
	public bool		HasUpValue;
	public bool		IsLoop;
}

public sealed class ConstructorControl
{
	public readonly ExpDesc	ExpLastItem = new();
	public required ExpDesc	ExpTable;
	public int		NumRecord;
	public int		NumArray;
	public int		NumToStore;
}

public enum ExpKind
{
	VVOID,	/* no value */
	VNIL,
	VTRUE,
	VFALSE,
	VK,		/* info = index of constant in `k' */
	VKNUM,	/* nval = numerical value */
	VNONRELOC,	/* info = result register */
	VLOCAL,	/* info = local register */
	VUPVAL,       /* info = index of upvalue in 'upvalues' */
	VINDEXED,	/* t = table register/upvalue; idx = index R/K */
	VJMP,		/* info = instruction pc */
	VRELOCABLE,	/* info = instruction pc */
	VCALL,	/* info = instruction pc */
	VVARARG	/* info = instruction pc */
}

internal enum AssignmentKind { NORMAL_ASSIGNMENT, COMPOUND_ASSIGNMENT }

public static class ExpKindUtl
{
	public static bool VKIsVar(ExpKind k) =>
		((int)ExpKind.VLOCAL <= (int)k && (int)k <= (int)ExpKind.VINDEXED);

	public static bool VKIsInReg(ExpKind k) => 
		k is ExpKind.VNONRELOC or ExpKind.VLOCAL;
}
	
public enum BinOpr
{
	ADD, SUB, MUL, DIV, MOD, POW, CONCAT, EQ, LT, LE,
	NE, GT, GE, AND, OR, NOBINOPR,
}

public enum UnOpr { MINUS, NOT, LEN, NOUNOPR, }

public sealed class ExpDesc
{
	public ExpKind Kind;

	public int Info;

	public struct IndData
	{
		public int T;
		public int Idx;
		public ExpKind Vt;
	}

	public IndData Ind;

	public double NumberValue;

	public int ExitTrue;
	public int ExitFalse;

	public void CopyFrom(ExpDesc e)
	{
		Kind 			= e.Kind;
		Info 			= e.Info;
		Ind 			= e.Ind;
		NumberValue 	= e.NumberValue;
		ExitTrue 		= e.ExitTrue;
		ExitFalse 		= e.ExitFalse;
	}
}

public readonly record struct VarDesc(int Index);

public record struct LabelDesc(
	string Name, int Pc, int Line, int NumActVar);
/*
	public string 	Name;		// label identifier
	public int 		Pc;			// position in code
	public int		Line;		// line where it appear
	public int		NumActVar;	// local level where it appears in current block
*/

public sealed class LHSAssign
{
	public LHSAssign? 	Prev, Next;
	public readonly ExpDesc	Exp = new();
}

public sealed class Parser
{
	public static Parser Read(
		ILoadInfo loadInfo, string? name, int numCSharpCalls)
	{
		var parser = new Parser(name, new LLex(loadInfo, name));
		parser._NumCSharpCalls += numCSharpCalls;
		var topFuncState = new FuncState();
		parser.MainFunc(topFuncState);
		parser.Proto = topFuncState.Proto;
		parser.Proto.Name = name;

		return parser;
	}

	public static Parser Read(string code, string name = "???") =>
		Read(new StringLoadInfo(code), name, 0);

	private const int 		MAXVARS = 200;

	private readonly List<VarDesc> 	_actVars;
	private readonly List<LabelDesc> _pendingGotos;
	private readonly List<LabelDesc> _activeLabels;

	private readonly LLex 	_lexer;
	private FuncState 		_curFunc;

	private int _NumCSharpCalls;

	public readonly string? Name;

	public LuaProto Proto { get; private set; } = null!;

	private Parser(string? name, LLex lexer)
	{
		Name = name;
		_lexer = lexer;
		
		_actVars = [];
		_pendingGotos = [];
		_activeLabels = [];
		_curFunc = null!; // Initialized before reading
	}

	private LuaProto AddPrototype()
	{
		var p = new LuaProto();
		_curFunc.Proto.P.Add(p);
		return p;
	}

	private void CodeClosure(ExpDesc v)
	{
		// Not null here
		var fs = _curFunc.Prev!;
		InitExp(v, ExpKind.VRELOCABLE,
			Coder.CodeABx(fs, OpCode.OP_CLOSURE, 0,
				(uint)(fs.Proto.P.Count - 1)));

		// Fix it at stack top
		Coder.Exp2NextReg(fs, v);
	}

	private void OpenFunc(FuncState fs, BlockCnt block)
	{
		fs.Lexer = _lexer;
		fs.Prev = _curFunc;
		
		if (_curFunc != null)
			fs.Proto.Parent = _curFunc.Proto;

		_curFunc = fs;

		fs.Pc = 0;
		fs.LastTarget = 0;
		fs.Jpc = Coder.NO_JUMP;
		fs.FreeReg = 0;
		fs.NumActVar = 0;
		fs.FirstLocal = _actVars.Count;

		// registers 0/1 are always valid
		fs.Proto.MaxStackSize = 2;
		fs.Proto.Source = _lexer.Source();
		fs.Proto.RootName = Name;

		EnterBlock(fs, block, false);
	}

	private void CloseFunc()
	{
		Coder.Ret(_curFunc, 0, 0);
		LeaveBlock(_curFunc);
		// will become null before returning from MainFunc
		_curFunc = _curFunc.Prev!;
	}

	private void MainFunc(FuncState fs)
	{
		var v = new ExpDesc();
		var block = new BlockCnt();
		OpenFunc(fs, block);
		fs.Proto.IsVarArg = true; // Main func is always vararg
		InitExp(v, ExpKind.VLOCAL, 0);
		NewUpValue(fs, LuaDef.LUA_ENV, v, true);
		_lexer.Next(); // Read first token
		StatList();
		// check TK_EOS
		CloseFunc();
	}

	private bool BlockFollow(bool withUntil)
	{
		return _lexer.Token.TokenType switch
		{
			TK.ELSE or TK.ELSEIF or TK.END or TK.EOS => true,
			TK.UNTIL => withUntil,
			_ => false
		};
	}

	private bool TestNext(int tokenType)
	{
		if (_lexer.Token.Val1 != tokenType) return false;
		_lexer.Next();
		return true;
	}

	private void Check(int tokenType)
	{
		if (_lexer.Token.Val1 == tokenType) return;

		ErrorExpected(tokenType);
	}
	
	private void Check(TK tokenType)
	{
		if (_lexer.Token.Val1 == (int)tokenType) return;

		ErrorExpected(tokenType);
	}


	private void CheckNext(int tokenType)
	{
		Check(tokenType);
		_lexer.Next();
	}
	
	private void CheckNext(TK tokenType)
	{
		Check(tokenType);
		_lexer.Next();
	}

	private void CheckCondition(bool cond, string msg)
	{
		if (!cond)
			_lexer.SyntaxError(msg);
	}

	private void EnterLevel()
	{
		++_NumCSharpCalls;
		CheckLimit(_curFunc, _NumCSharpCalls,
			LuaLimits.LUAI_MAXCCALLS, "C# levels");
	}

	private void LeaveLevel() => --_NumCSharpCalls;

	private void SemanticError(string msg)
	{
		// TODO
		_lexer.SyntaxError(msg);
	}

	private void ErrorLimit(FuncState fs, int limit, string what)
	{
		var line = fs.Proto.LineDefined;
		var where = (line == 0)
			? "main function"
			: $"function at line {line}";
		var msg = string.Format("too many {0} (limit is {1}) in {2}",
			what, limit, where);
		_lexer.SyntaxError(msg);
	}

	private void CheckLimit(FuncState fs, int v, int l, string what)
	{
		if (v > l)
			ErrorLimit(fs, l, what);
	}

	private int RegisterLocalVar(string varname)
	{
		var v = new LocVar
		{
			VarName = varname,
			StartPc = 0,
			EndPc = 0
		};
		_curFunc.Proto.LocVars.Add(v);
		return _curFunc.Proto.LocVars.Count - 1;
	}

	private VarDesc NewLocalVar(string name)
	{
		var fs = _curFunc;
		var reg = RegisterLocalVar(name);
		CheckLimit(fs, _actVars.Count + 1 - fs.FirstLocal,
			MAXVARS, "local variables");
		var v = new VarDesc { Index = reg };
		return v;
	}

	private ref LocVar GetLocalVar(FuncState fs, int i)
	{
		var idx = _actVars[fs.FirstLocal + i].Index;
		LuaUtil.Assert(idx < fs.Proto.LocVars.Count);
		var span = CollectionsMarshal.AsSpan(fs.Proto.LocVars);
		return ref span[idx];
	}

	private void AdjustLocalVars(int nvars)
	{
		var fs = _curFunc;
		fs.NumActVar += nvars;
		for (; nvars > 0; --nvars)
		{
			ref var v = ref GetLocalVar(fs, fs.NumActVar - nvars);
			v.StartPc = fs.Pc;
		}
	}

	private void RemoveVars(FuncState fs, int toLevel)
	{
		var len = fs.NumActVar - toLevel;
		while (fs.NumActVar > toLevel)
		{
			ref var v = ref GetLocalVar(fs, --fs.NumActVar);
			v.EndPc = fs.Pc;
		}
		_actVars.RemoveRange(_actVars.Count-len, len);
	}

	private void CloseGoto(int g, LabelDesc label)
	{
		var gt = _pendingGotos[g];
		LuaUtil.Assert(gt.Name == label.Name);
		if (gt.NumActVar < label.NumActVar)
		{
			var v = GetLocalVar(_curFunc, gt.NumActVar);
			var msg = 
				$"<goto {gt.Name}> at line {gt.Line} jumps into the scope of local '{v.VarName}'";
			SemanticError(msg);
		}
		Coder.PatchList(_curFunc, gt.Pc, label.Pc);
			
		_pendingGotos.RemoveAt(g);
	}

	// try to close a goto with existing labels; this solves backward jumps
	private bool FindLabel(int g)
	{
		var gt = _pendingGotos[g];
		var block = _curFunc.Block;
		for (var i = block.FirstLabel; i < _activeLabels.Count; ++i)
		{
			var label = _activeLabels[i];

			// correct label?
			if (label.Name == gt.Name)
			{
				if (gt.NumActVar > label.NumActVar &&
				    (block.HasUpValue || _activeLabels.Count > block.FirstLabel))
				{
					Coder.PatchClose(_curFunc, gt.Pc, label.NumActVar);
				}
				CloseGoto(g, label);
				return true;
			}
		}
		return false;
	}

	private LabelDesc NewLabelEntry(string name, int line, int pc)
	{
		var desc = new LabelDesc
		{ Name = name, Pc = pc, Line = line, NumActVar = _curFunc.NumActVar };
		return desc;
	}

	// check whether new label `label' matches any pending gotos in current
	// block; solves forward jumps
	private void FindGoTos(LabelDesc label)
	{
		var i = _curFunc.Block.FirstGoto;
		while (i < _pendingGotos.Count)
		{
			if (_pendingGotos[i].Name == label.Name)
				CloseGoto(i, label);
			else
				++i;
		}
	}

	// "export" pending gotos to outer level, to check them against
	// outer labels; if the block being exited has upvalues, and
	// the goto exits the scope of any variable (which can be the
	// upvalue), close those variables being exited.
	private void MoveGoTosOut(FuncState fs, BlockCnt block)
	{
		var i = block.FirstGoto;

		// correct pending gotos to current block and try to close it
		// with visible labels
		while (i < _pendingGotos.Count)
		{
			var gt = _pendingGotos[i];
			if (gt.NumActVar > block.NumActVar)
			{
				if (block.HasUpValue)
					Coder.PatchClose(fs, gt.Pc, block.NumActVar);
				_pendingGotos[i] = gt with { NumActVar = block.NumActVar };
			}
			if (!FindLabel(i))
				++i; // move to next one
		}
	}

	// Create a label named 'break' to resolve break statements
	private void BreakLabel()
	{
		var desc = NewLabelEntry("break", 0, _curFunc.Pc);
		_activeLabels.Add(desc);
		FindGoTos(_activeLabels[^1]);
	}

	// Generates an error for an undefined 'goto'; choose appropriate
	// Message when label name is a reserved word (which can only be `'break')
	private void UndefGoto(LabelDesc gt)
	{
		var template = LLex.IsReservedWord(gt.Name)
			? "<{0}> at line {1} not inside a loop"
			: "no visible label '{0}' for <goto> at line {1}";
		var msg = string.Format(template, gt.Name, gt.Line);
		SemanticError(msg);
	}

	private void EnterBlock(FuncState fs, BlockCnt block, bool isLoop)
	{
		block.IsLoop 		= isLoop;
		block.NumActVar 	= fs.NumActVar;
		block.FirstLabel 	= _activeLabels.Count;
		block.FirstGoto 	= _pendingGotos.Count;
		block.HasUpValue 	= false;
		block.Previous 		= fs.Block;
		fs.Block 			= block;
		LuaUtil.Assert(fs.FreeReg == fs.NumActVar);
	}

	private void LeaveBlock(FuncState fs)
	{
		var block = fs.Block;

		if (block is { Previous: not null, HasUpValue: true })
		{
			var j = Coder.Jump(fs);
			Coder.PatchClose(fs, j, block.NumActVar);
			Coder.PatchToHere(fs, j);
		}

		if (block.IsLoop)
			BreakLabel();

		// Last iteration will leave this value to null
		// But we don't need it anyway
		fs.Block = block.Previous!;
		RemoveVars(fs, block.NumActVar);
		LuaUtil.Assert(block.NumActVar == fs.NumActVar);
		fs.FreeReg =  fs.NumActVar; // free registers
		_activeLabels.RemoveRange(block.FirstLabel,
			_activeLabels.Count - block.FirstLabel);

		// inner block?
		if (block.Previous != null)
			MoveGoTosOut(fs, block);

		// pending gotos in outer block?
		else if(block.FirstGoto < _pendingGotos.Count)
			UndefGoto(_pendingGotos[block.FirstGoto]); // error
	}

	private static UnOpr GetUnOpr(int op)
	{
		return op switch
		{
			(int)TK.NOT => UnOpr.NOT,
			'-' => UnOpr.MINUS,
			'#' => UnOpr.LEN,
			_ => UnOpr.NOUNOPR
		};
	}

	private static BinOpr GetBinOpr(int op)
	{
		return op switch
		{
			'+' => BinOpr.ADD,
			'-' => BinOpr.SUB,
			'*' => BinOpr.MUL,
			'/' => BinOpr.DIV,
			'%' => BinOpr.MOD,
			'^' => BinOpr.POW,
			(int)TK.CONCAT => BinOpr.CONCAT,
			(int)TK.NE => BinOpr.NE,
			(int)TK.EQ => BinOpr.EQ,
			'<' => BinOpr.LT,
			(int)TK.LE => BinOpr.LE,
			'>' => BinOpr.GT,
			(int)TK.GE => BinOpr.GE,
			(int)TK.AND => BinOpr.AND,
			(int)TK.OR => BinOpr.OR,
			(int)TK.PLUSEQ => BinOpr.ADD,
			(int)TK.SUBEQ => BinOpr.SUB,
			(int)TK.MULTEQ => BinOpr.MUL,
			(int)TK.DIVEQ => BinOpr.DIV,
			(int)TK.MODEQ => BinOpr.MOD,
			(int)TK.BANDEQ => BinOpr.AND,
			(int)TK.BOREQ => BinOpr.OR,
			(int)TK.CONCATEQ => BinOpr.CONCAT,
			_ => BinOpr.NOBINOPR
		};
	}

	private static int GetBinOprLeftPrior(BinOpr opr)
	{
		return opr switch
		{
			BinOpr.ADD => 6,
			BinOpr.SUB => 6,
			BinOpr.MUL => 7,
			BinOpr.DIV => 7,
			BinOpr.MOD => 7,
			BinOpr.POW => 10,
			BinOpr.CONCAT => 5,
			BinOpr.EQ => 3,
			BinOpr.LT => 3,
			BinOpr.LE => 3,
			BinOpr.NE => 3,
			BinOpr.GT => 3,
			BinOpr.GE => 3,
			BinOpr.AND => 2,
			BinOpr.OR => 1,
			BinOpr.NOBINOPR => throw new LuaParserException("GetBinOprLeftPrior(NOBINOPR)"),
			_ => throw new LuaException("Unknown BinOpr: " + opr)
		};
	}

	private static int GetBinOprRightPrior(BinOpr opr)
	{
		return opr switch
		{
			BinOpr.ADD => 6,
			BinOpr.SUB => 6,
			BinOpr.MUL => 7,
			BinOpr.DIV => 7,
			BinOpr.MOD => 7,
			BinOpr.POW => 9,
			BinOpr.CONCAT => 4,
			BinOpr.EQ => 3,
			BinOpr.LT => 3,
			BinOpr.LE => 3,
			BinOpr.NE => 3,
			BinOpr.GT => 3,
			BinOpr.GE => 3,
			BinOpr.AND => 2,
			BinOpr.OR => 1,
			BinOpr.NOBINOPR => throw new LuaParserException("GetBinOprRightPrior(NOBINOPR)"),
			_ => throw new LuaException("Unknown BinOpr: " + opr)
		};
	}

	private const int UnaryPrior = 8;

	// statlist -> { stat [';'] }
	private void StatList()
	{
		while (!BlockFollow(true))
		{
			if (_lexer.Token.TokenType == TK.RETURN)
			{
				Statement();
				return; // 'return' must be last statement
			}
			Statement();
		}
	}

	// fieldsel -> ['.' | ':'] NAME
	private void FieldSel(ExpDesc v)
	{
		var fs = _curFunc;
		var key = new ExpDesc();
		Coder.Exp2AnyRegUp(fs, v);
		_lexer.Next(); // skip the dot or colon
		CodeString(key, CheckName());
		Coder.Indexed(fs, v, key);
	}

	// cond -> exp
	private int Cond()
	{
		var v = new ExpDesc();
		Expr(v); // read condition

		// 'falses' are all equal here
		if (v.Kind == ExpKind.VNIL)
			v.Kind = ExpKind.VFALSE;

		Coder.GoIfTrue(_curFunc, v);
		return v.ExitFalse;
	}

	private void GotoStat(int pc)
	{
		string label;
		if (TestNext( (int)TK.GOTO))
			label = CheckName();
		else if (TestNext((int)TK.CONTINUE))
			label = "continue";
		else
		{
			_lexer.Next();
			label = "break";
		}

		_pendingGotos.Add( NewLabelEntry( label, _lexer.LineNumber, pc ) );

		// close it if label already defined
		FindLabel( _pendingGotos.Count-1 );
	}

	// Check for repeated labels on the same block
	private void CheckRepeated(FuncState fs, List<LabelDesc> list, string label)
	{
		for (var i = fs.Block.FirstLabel; i < list.Count; ++i)
		{
			if (label != list[i].Name) continue;
			SemanticError(
				$"label '{label}' already defined on line {list[i].Line}");
		}
	}

	// Skip no-op statements
	private void SkipNoOpStat()
	{
		while (true)
		{
			var t = _lexer.Token;
			if (!(t.Val1 == ';' || t.TokenType == TK.DBCOLON)) break;
			Statement();
		}
	}

	private void LabelStat(string label, int line)
	{
		var fs = _curFunc;
		CheckRepeated(fs, _activeLabels, label);
		CheckNext(TK.DBCOLON);

		var desc = NewLabelEntry(label, line, Coder.GetLabel(fs));
		_activeLabels.Add(desc);
		SkipNoOpStat();
		if (BlockFollow(false))
		{
			// Assume that locals are already out of scope
			desc.NumActVar = fs.Block.NumActVar;
		}
		FindGoTos(desc);
	}

	// whilestat -> WHILE cond DO block END
	private void WhileStat(int line)
	{
		var fs = _curFunc;
		var block = new BlockCnt();

		_lexer.Next(); // skip WHILE
		var whileInit = Coder.GetLabel(fs);
		var condExit = Cond();
		EnterBlock(fs, block, true);
		CheckNext(TK.DO);
		Block();
		Coder.JumpTo(fs, whileInit);
		CheckMatch((int)TK.END, (int)TK.WHILE, line);
		ContinueLabel(whileInit);
		LeaveBlock(fs);
		Coder.PatchToHere(fs, condExit);
	}

	// repeatstat -> REPEAT block UNTIL cond
	private void RepeatStat(int line)
	{
		var fs = _curFunc;
		var repeatInit = Coder.GetLabel( fs );
		var blockLoop  = new BlockCnt();
		var blockScope = new BlockCnt();
		EnterBlock(fs, blockLoop, true);
		EnterBlock(fs, blockScope, false);
		_lexer.Next();
		StatList();
		CheckMatch((int)TK.UNTIL, (int)TK.REPEAT, line);
		var iter = fs.Pc;
		var condExit = Cond();
		if (blockScope.HasUpValue)
		{
			Coder.PatchClose(fs, condExit, blockScope.NumActVar);
		}
		LeaveBlock(fs);
		Coder.PatchList(fs, condExit, repeatInit); // close the loop
		ContinueLabel(iter);
		LeaveBlock(fs);
	}

	private int Exp1()
	{
		var e = new ExpDesc();
		Expr(e);
		Coder.Exp2NextReg(_curFunc, e);
		LuaUtil.Assert(e.Kind == ExpKind.VNONRELOC);
		return e.Info;
	}

	// forbody -> DO block
	private void ForBody(int t, int line, int nVars, bool isNum)
	{
		var fs = _curFunc;
		var block = new BlockCnt();
		AdjustLocalVars(3); // control variables
		CheckNext(TK.DO);
		var prep = isNum ? Coder.CodeAsBx(fs, OpCode.OP_FORPREP, t, Coder.NO_JUMP)
			: Coder.Jump(fs);
		EnterBlock(fs, block, false);
		AdjustLocalVars(nVars);
		Coder.ReserveRegs(fs, nVars);
		Block();
		LeaveBlock(fs);
		Coder.PatchToHere(fs, prep);
		ContinueLabel(Coder.GetLabel(fs));

		int endFor;
		if (isNum) // numeric for?
		{
			endFor = Coder.CodeAsBx(fs, OpCode.OP_FORLOOP, t, Coder.NO_JUMP);
		}
		else // generic for
		{
			Coder.CodeABC(fs, OpCode.OP_TFORCALL, t, 0, nVars);
			Coder.FixLine(fs, line);
			endFor = Coder.CodeAsBx(fs, OpCode.OP_TFORLOOP, t + 2, Coder.NO_JUMP);
		}
		Coder.PatchList(fs, endFor, prep + 1);
		Coder.FixLine(fs, line);
	}

	// fornum -> NAME = exp1,expe1[,exp1] forbody
	private void ForNum(string varname, int line)
	{
		var fs = _curFunc;
		var save = fs.FreeReg;
		_actVars.Add(NewLocalVar("(for index)"));
		_actVars.Add(NewLocalVar("(for limit)"));
		_actVars.Add(NewLocalVar("(for step)"));
		_actVars.Add(NewLocalVar(varname));
		CheckNext('=');
		Exp1(); // initial value
		CheckNext(',');
		Exp1(); // limit
		if (TestNext(',')) Exp1(); // optional step
		else // default step = 1
		{
			Coder.CodeK(fs, fs.FreeReg, Coder.NumberK(fs, 1));
			Coder.ReserveRegs(fs, 1);
		}
		ForBody(save, line, 1, true);
	}

	// forlist -> NAME {,NAME} IN explist forbody
	private void ForList(string indexName)
	{
		var fs = _curFunc;
		var e = new ExpDesc();
		var nvars = 4; // gen, state, control, plus at least one declared var
		var save = fs.FreeReg;

		// create control variables
		_actVars.Add(NewLocalVar("(for generator)"));
		_actVars.Add(NewLocalVar("(for state)"));
		_actVars.Add(NewLocalVar("(for control)"));

		// create declared variables
		_actVars.Add(NewLocalVar(indexName));
		while (TestNext(','))
		{
			_actVars.Add(NewLocalVar(CheckName()));
			nvars++;
		}
		CheckNext(TK.IN);
		var line = _lexer.LineNumber;
		AdjustAssign(3, ExpList(e), e);
		Coder.CheckStack(fs, 3); // extra space to call generator
		ForBody(save, line, nvars - 3, false);
	}

	// forstat -> FOR (fornum | forlist) END
	private void ForStat(int line)
	{
		var fs = _curFunc;
		var block = new BlockCnt();
		EnterBlock( fs, block, true );
		_lexer.Next(); // skip `for'
		var varName = CheckName();
		switch (_lexer.Token.Val1)
		{
			case '=': ForNum(varName, line); break;
			case ',': case (int)TK.IN: ForList(varName); break;
			default: 
				_lexer.SyntaxError("'=' or 'in' expected, got: " + _lexer.Token);
				break;
		}
		CheckMatch((int)TK.END, (int)TK.FOR, line);
		LeaveBlock(fs);
	}

	// test_then_block -> [IF | ELSEIF] cond THEN block
	private int TestThenBlock(int escapeList)
	{
		var fs = _curFunc;
		var block = new BlockCnt();
		int jf; // instruction to skip `then' code (if condition is false)

		// skip IF or ELSEIF
		_lexer.Next();

		// read condition
		var v = new ExpDesc();
		Expr(v);

		CheckNext (TK.THEN);
		if (_lexer.Token.Val1 is (int)TK.GOTO or (int)TK.BREAK)
		{
			// will jump to label if condition is true
			Coder.GoIfFalse(_curFunc, v);

			// must enter block before `goto'
			EnterBlock(fs, block, false);

			// handle goto/break
			GotoStat(v.ExitTrue);

			while (TestNext(';'));  /* skip semicolons */

			// `goto' is the entire block?
			if (BlockFollow(false))
			{
				LeaveBlock(fs);
				return escapeList;
			}

			jf = Coder.Jump(fs);
		}
		// regular case (not goto/break)
		else
		{
			// skip over block if condition is false
			Coder.GoIfTrue(_curFunc, v);
			EnterBlock(fs, block, false);
			jf = v.ExitFalse;
		}

		// `then' part
		StatList();
		LeaveBlock(fs);

		// followed by `else' or `elseif'
		if (_lexer.Token.Val1 is (int)TK.ELSE or (int)TK.ELSEIF)
		{
			// must jump over it
			escapeList = Coder.Concat(fs, escapeList, Coder.Jump(fs));
		}
		Coder.PatchToHere(fs, jf);
		return escapeList;
	}

	// ifstat -> IF cond THEN block {ELSEIF cond THEN block} [ELSE block] END
	private void IfStat(int line)
	{
		var fs = _curFunc;

		// exit list for finished parts
		var escapeList = Coder.NO_JUMP;

		// IF cond THEN block
		escapeList = TestThenBlock(escapeList);

		// ELSEIF cond THEN block
		while (_lexer.Token.TokenType == TK.ELSEIF)
			escapeList = TestThenBlock(escapeList);

		// `else' part
		if (TestNext((int)TK.ELSE))
			Block();

		CheckMatch((int)TK.END, (int)TK.IF, line);
		Coder.PatchToHere(fs, escapeList);
	}

	private void LocalFunc()
	{
		var b = new ExpDesc();
		var fs = _curFunc;
		var name = CheckName();
		var v = NewLocalVar(name);
		_actVars.Add(v);
		AdjustLocalVars(1); // enter its scope
		Body(b, false, _lexer.LineNumber, name);
		GetLocalVar(fs, b.Info).StartPc = fs.Pc;
	}

	// stat -> LOCAL NAME {`,' NAME} [`=' explist]
	private void LocalStat()
	{
		var nvars = 0;
		int nexps;
		var e = new ExpDesc();
		do {
			var v = NewLocalVar(CheckName());
			_actVars.Add(v);
			++nvars;
		} while (TestNext(','));

		if (TestNext('='))
			nexps = ExpList(e);
		else
		{
			e.Kind = ExpKind.VVOID;
			nexps = 0;
		}
		AdjustAssign(nvars, nexps, e);
		AdjustLocalVars(nvars);
	}

	// funcname -> NAME {fieldsel} [`:' NAME]
	private bool FuncName(ExpDesc v, out string name)
	{
		SingleVar(v, out name);
		while (_lexer.Token.Val1 == '.')
		{
			FieldSel(v);
		}
		if (_lexer.Token.Val1 == ':')
		{
			FieldSel(v);
			return true; // is method
		}

		return false;
	}

	// funcstat -> FUNCTION funcname BODY
	private void FuncStat(int line)
	{
		var v = new ExpDesc();
		var b = new ExpDesc();
		_lexer.Next();
		var isMethod = FuncName(v, out var name);
		Body(b, isMethod, line, name);
		Coder.StoreVar(_curFunc, v, b);
		Coder.FixLine(_curFunc, line);
	}

	// stat -> func | assignment
	private void ExprStat()
	{
		var v = new LHSAssign();
		SuffixedExp(v.Exp);

		// stat -> assignment ?
		if ((_lexer.Token.Val1 is '=' or ',' or > (int)TK.COMP_START and < (int)TK.COMP_END))
		{
			v.Prev = null;
			Assignment(v, 1);
		}
		// stat -> func
		else
		{
			if (v.Exp.Kind != ExpKind.VCALL)
				_lexer.SyntaxError("Syntax error: Expected VCALL, got " + v.Exp.Kind);

			var pi = _curFunc.GetCode(v.Exp);
			pi.Value = pi.Value.SETARG_C(1); // call statement uses no results
		}
	}

	// stat -> RETURN [explist] [';']
	private void RetStat()
	{
		var fs = _curFunc;
		int first, nret; // registers with returned values
		if (BlockFollow(true) || _lexer.Token.Val1 == ';')
		{
			first = nret = 0; // return no values
		}
		else
		{
			var e = new ExpDesc();
			nret = ExpList(e);
			if (HasMultiRet(e.Kind))
			{
				Coder.SetMultiRet(fs, e);
				if (e.Kind == ExpKind.VCALL && nret == 1) // tail call?
				{
					var pi = fs.GetCode(e);
					pi.Value = pi.Value.SET_OPCODE(OpCode.OP_TAILCALL);
					LuaUtil.Assert(pi.Value.GETARG_A() == fs.NumActVar);
				}
				first = fs.NumActVar;
				nret = LuaDef.LUA_MULTRET;
			}
			else
			{
				if (nret == 1) // only one single value
				{
					first = Coder.Exp2AnyReg(fs, e);
				}
				else
				{
					Coder.Exp2NextReg(fs, e); // values must go to the `stack'
					first = fs.NumActVar;
					LuaUtil.Assert(nret == fs.FreeReg - first);
				}
			}
		}
		Coder.Ret(fs, first, nret);
		TestNext(';'); // skip optional semicolon
	}

	private void Statement()
	{
		// ULDebug.Log("Statement ::" + Lexer.Token);
		var line = _lexer.LineNumber;
		EnterLevel();
		switch (_lexer.Token.Val1)
		{
			case ';': 
				_lexer.Next();
				break;

			// stat -> ifstat
			case (int)TK.IF: 
				IfStat(line);
				break;

			// stat -> whilestat
			case (int)TK.WHILE: 
				WhileStat(line);
				break;

			// stat -> DO block END
			case (int)TK.DO: 
				_lexer.Next();
				Block();
				CheckMatch((int)TK.END, (int)TK.DO, line);
				break;

			// stat -> forstat
			case (int)TK.FOR: 
				ForStat(line);
				break;

			// stat -> repeatstat
			case (int)TK.REPEAT: 
				RepeatStat(line);
				break;

			// stat -> funcstat
			case (int)TK.FUNCTION: 
				FuncStat(line);
				break;

			// stat -> localstat
			case (int)TK.LOCAL: 
				_lexer.Next();

				// local function?
				if (TestNext((int)TK.FUNCTION))
					LocalFunc();
				else
					LocalStat();
				break;

			// stat -> label
			case (int)TK.DBCOLON: 
				_lexer.Next(); // skip double colon
				LabelStat(CheckName(), line);
				break;

			// stat -> retstat
			case (int)TK.RETURN: 
				_lexer.Next(); // skip RETURN
				RetStat();
				break;

			// stat -> breakstat
			// stat -> 'goto' NAME
			case (int)TK.BREAK:
			case (int)TK.CONTINUE: /* stat -> continuestat */
			case (int)TK.GOTO: 
				GotoStat(Coder.Jump(_curFunc));
				break;

			// stat -> func | assignment
			default:
				ExprStat();
				break;
		}
		// ULDebug.Log("MaxStackSize: " + CurFunc.Proto.MaxStackSize);
		// ULDebug.Log("FreeReg: " + CurFunc.FreeReg);
		// ULDebug.Log("NumActVar: " + CurFunc.NumActVar);
		LuaUtil.Assert(_curFunc.Proto.MaxStackSize >= _curFunc.FreeReg &&
		            _curFunc.FreeReg >= _curFunc.NumActVar);
		_curFunc.FreeReg = _curFunc.NumActVar; // free registers
		LeaveLevel();
	}

	private string CheckName()
	{
		// ULDebug.Log(Lexer.Token);
		if (_lexer.Token.Kind != TokenKind.Named)
		{
			ULDebug.LogError(_lexer.LineNumber + ":" + _lexer.Token);
			_lexer.SyntaxError(
				"Syntax error: Expected function name, got " + _lexer.Token);
		}

		var name = _lexer.Token.str!;
		_lexer.Next();
		return name;
	}

	private int? SearchVar(FuncState fs, string name)
	{
		for (var i = fs.NumActVar - 1; i >= 0; i--)
		{
			if (name == GetLocalVar(fs, i).VarName)
				return i;
		}
		return null; // not found
	}

	private static void MarkUpvalue(FuncState fs, int level)
	{
		var block = fs.Block;
		while (block!.NumActVar > level) block = block.Previous;
		block.HasUpValue = true;
	}

	private ExpKind SingleVarAux(
		FuncState? fs, string name, ExpDesc e, bool flag)
	{
		if (fs == null)
			return ExpKind.VVOID;

		// look up locals at current level
		if (SearchVar(fs, name) is {} v)
		{
			InitExp(e, ExpKind.VLOCAL, v);
			if (!flag) MarkUpvalue(fs, v); // local will be used as an upval
			return ExpKind.VLOCAL;
		}

		// not found as local at current level; try upvalues
		if (SearchUpValues(fs, name) is not {} idx) // not found?
		{
			if (SingleVarAux(fs.Prev, name, e, false) == ExpKind.VVOID)
				return ExpKind.VVOID; // not found; is a global
			idx = NewUpValue(fs, name, e);
		}
		InitExp(e, ExpKind.VUPVAL, idx);
		return ExpKind.VUPVAL;
	}

	private void SingleVar(ExpDesc e, out string name)
	{
		name = CheckName();
		if (SingleVarAux(_curFunc, name, e, true) != ExpKind.VVOID) return;
		var key = new ExpDesc();
		SingleVarAux(_curFunc, LuaDef.LUA_ENV, e, true);
		LuaUtil.Assert(e.Kind is ExpKind.VLOCAL or ExpKind.VUPVAL);
		CodeString(key, name);
		Coder.Indexed(_curFunc, e, key);
	}

	private void AdjustAssign(int nvars, int nexps, ExpDesc e)
	{
		var fs = _curFunc;
		var extra = nvars - nexps;
		if (HasMultiRet(e.Kind))
		{
			// includes call itself
			++extra;
			if (extra < 0)
				extra = 0;
			Coder.SetReturns(fs, e, extra);
			if (extra > 1)
				Coder.ReserveRegs(fs, extra - 1);
		}
		else
		{
			if (e.Kind != ExpKind.VVOID)
				Coder.Exp2NextReg(fs, e); // close last expression
			if (extra > 0)
			{
				var reg = fs.FreeReg;
				Coder.ReserveRegs(fs, extra);
				Coder.CodeNil(fs, reg, extra);
			}
		}
		if (nexps > nvars)
			fs.FreeReg -= nexps - nvars; // Remove extra values
	}

	// check whether, in an assignment to an upvalue/local variable, the
	// upvalue/local variable is begin used in a previous assignment to a
	// table. If so, save original upvalue/local value in a safe place and
	// use this safe copy in the previous assignment.
	private void CheckConflict(LHSAssign? lh, ExpDesc v)
	{
		var fs = _curFunc;

		// eventual position to save local variable
		var extra = fs.FreeReg;
		var conflict = false;

		// check all previous assignments
		for (; lh != null; lh = lh.Prev)
		{
			var e = lh.Exp;
			// assign to a table?
			if (e.Kind == ExpKind.VINDEXED)
			{
				// table is the upvalue/local being assigned now?
				if (e.Ind.Vt == v.Kind && e.Ind.T == v.Info)
				{
					conflict = true;
					e.Ind.Vt = ExpKind.VLOCAL;
					e.Ind.T  = extra; // previous assignment will use safe copy
				}
				// index is the local being assigned? (index cannot be upvalue)
				if (v.Kind == ExpKind.VLOCAL && e.Ind.Idx == v.Info)
				{
					conflict = true;
					e.Ind.Idx = extra; // previous assignment will use safe copy
				}
			}
		}
		if (conflict)
		{
			// copy upvalue/local value to a temporary (in position 'extra')
			var op = (v.Kind == ExpKind.VLOCAL) ? OpCode.OP_MOVE : OpCode.OP_GETUPVAL;
			Coder.CodeABC(fs, op, extra, v.Info, 0);
			Coder.ReserveRegs(fs, 1);
		}
	}

	// assignment -> ',' suffixedexp assignment
	private AssignmentKind Assignment(LHSAssign lh, int nVars)
	{
		var kind = AssignmentKind.NORMAL_ASSIGNMENT;
		CheckCondition(ExpKindUtl.VKIsVar(lh.Exp.Kind), "syntax error");
		var e = new ExpDesc();

		if (TestNext(','))
		{
			var nv = new LHSAssign { Prev = lh, Next = null };
			lh.Next = nv;
			SuffixedExp(nv.Exp);
			if (nv.Exp.Kind != ExpKind.VINDEXED)
				CheckConflict(lh, nv.Exp);
			CheckLimit(_curFunc, nVars + _NumCSharpCalls,
				LuaLimits.LUAI_MAXCCALLS, "C# levels");
			kind = Assignment(nv, nVars + 1);
		}
		else if (_lexer.Token.Val1 is > (int)TK.COMP_START and < (int)TK.COMP_END) 
		{ 
			/* restassign -> opeq expr */
			CheckCondition(nVars == 1, "compound assignment not allowed on tuples");
			return CompoundAssignment(lh, nVars);
		} 
		else
		{
			CheckNext('=');
			var nexps = ExpList(e);
			if (nexps != nVars)
			{
				AdjustAssign(nVars, nexps, e);
			}
			else
			{
				Coder.SetOneRet(_curFunc, e);
				Coder.StoreVar(_curFunc, lh.Exp, e);
				return kind; /* avoid default */
			}
		}

		if (kind == AssignmentKind.COMPOUND_ASSIGNMENT)
			return kind;

		// default assignment
		InitExp(e, ExpKind.VNONRELOC, _curFunc.FreeReg - 1);
		Coder.StoreVar(_curFunc, lh.Exp, e);
		return kind;
	}

	private AssignmentKind CompoundAssignment(LHSAssign lh, int nVars)
	{
		// https://github.com/SnapperTT/lua-luajit-compound-operators
		var bop = GetBinOpr(_lexer.Token.Val1);
		var fState = _curFunc;
		var toLevel = fState.NumActVar;
		var oldFree = fState.FreeReg;
		var line = _lexer.LineNumber;
		var e = new ExpDesc();
		var infix = new ExpDesc();

		var assign = lh;
		while(assign.Prev != null) 
			assign = assign.Prev;

		_lexer.Next();

		/* create temporary local variables to lock up any registers needed by indexed lvalues. */
		var top = fState.NumActVar;
		var a = lh;
		while (a != null)
		{
			var exp = a.Exp;
			// protect both the table and index result registers,
			// ensuring that they won't be overwritten prior to the
			// storevar calls.

			if (exp.Kind == ExpKind.VINDEXED)
			{
				if (!Instruction.ISK(exp.Ind.T) && exp.Ind.T >= top)
					top = exp.Ind.T + 1;
				if (!Instruction.ISK(exp.Ind.Idx) && exp.Ind.Idx >= top)
					top = exp.Ind.Idx + 1;
			}
			a = a.Prev;
		}

		var nExtra = top - fState.NumActVar;
		if (nExtra != 0)
		{
			for (var i = 0; i < nExtra; i++)
				NewLocalVar("(temp)");
			AdjustLocalVars(nExtra);
		}

		var npexs = 0;
		while (true)
		{
			if (assign == null)
			{
				_lexer.SyntaxError("too many right hand side values in compound assignment");
			}

			infix.CopyFrom(assign.Exp);
			Coder.Infix(fState, bop, infix);
			Expr(e);

			if (_lexer.Token.Val1 == ',')
			{
				Coder.PosFix(fState, bop, infix, e, line);
				Coder.StoreVar(fState, assign.Exp, infix);
				assign = assign.Next;
				npexs++;
			}

			if (!TestNext(',')) break;
		}

		if (npexs + 1 == nVars)
		{
			Coder.PosFix(fState, bop, infix, e, line);
			Coder.StoreVar(fState, lh.Exp, infix);
		}
		else if (HasMultiRet(e.Kind))
		{
			AdjustAssign(nVars - npexs, 1, e);
			assign = lh;

			var top2 = _curFunc.FreeReg - 1;
			var firstTop = top2;

			for (var i = 0; i < nVars - npexs; i++)
			{
				infix = assign.Exp;
				Coder.Infix(fState, bop, infix);
				InitExp(e, ExpKind.VNONRELOC, top2--);
				Coder.PosFix(fState, bop, infix, infix, line);
				Coder.StoreVar(fState, assign.Exp, infix);
			}
		}
		else
		{
			_lexer.SyntaxError("insufficient right hand variables in compound assignment.");
		}
		
		RemoveVars(fState, toLevel);
		if (oldFree < fState.FreeReg)
			fState.FreeReg = oldFree;

		return AssignmentKind.COMPOUND_ASSIGNMENT;
	}

	private int ExpList(ExpDesc e)
	{
		var n = 1; // at least one expression
		Expr(e);
		while (TestNext(','))
		{
			Coder.Exp2NextReg(_curFunc, e);
			Expr(e);
			n++;
		}
		return n;
	}
		
	private void Expr(ExpDesc e) => SubExpr(e, 0);

	private BinOpr SubExpr(ExpDesc e, int limit)
	{
		// ULDebug.Log("SubExpr limit:" + limit);
		EnterLevel();
		var uop = GetUnOpr(_lexer.Token.Val1);
		if (uop != UnOpr.NOUNOPR)
		{
			var line = _lexer.LineNumber;
			_lexer.Next();
			SubExpr(e, UnaryPrior);
			Coder.Prefix(_curFunc, uop, e, line);
		}
		else SimpleExp(e);

		// Expand while operators have priorities higher than `limit'
		var op = GetBinOpr(_lexer.Token.Val1);
		while (op != BinOpr.NOBINOPR && GetBinOprLeftPrior(op) > limit)
		{
			// ULDebug.Log("op:" + op);
			var line = _lexer.LineNumber;
			_lexer.Next();
			Coder.Infix(_curFunc, op, e);

			// Read sub-expression with higher priority
			var e2 = new ExpDesc();
			var nextOp = SubExpr(e2, GetBinOprRightPrior(op));
			Coder.PosFix(_curFunc, op, e, e2, line);
			op = nextOp;
		}
		LeaveLevel();
		return op;
	}

	private static bool HasMultiRet(ExpKind k) => 
		k is ExpKind.VCALL or ExpKind.VVARARG;

	private void ErrorExpected(int token) => 
		_lexer.SyntaxError($"{(char)token} expected but got {_lexer.Token}");
	
	private void ErrorExpected(TK token) => 
		_lexer.SyntaxError($"{token} expected");

	private void CheckMatch(int what, int who, int where)
	{
		if (TestNext(what)) return;

		if (where == _lexer.LineNumber)
			ErrorExpected(what);
		else
			_lexer.SyntaxError(
				$"{((char)what).ToString()} expected (to close {((char)who).ToString()} at line {where})");
	}

	private void ContinueLabel(int pc)
	{
		// https://github.com/SnapperTT/lua-luajit-compound-operators
		var label = NewLabelEntry("continue", 0, pc);
		FindGoTos(label);
	}

	// block -> statlist
	private void Block()
	{
		var fs = _curFunc;
		var block = new BlockCnt();
		EnterBlock(fs, block, false);
		StatList();
		LeaveBlock(fs);
	}

	// index -> '[' expr ']'
	private void YIndex(ExpDesc v)
	{
		_lexer.Next();
		Expr(v);
		Coder.Exp2Val(_curFunc, v);
		CheckNext(']');
	}

	// recfield -> (NAME | '[' exp1 ']') = exp1
	private void RecField(ConstructorControl cc)
	{
		var fs = _curFunc;
		var reg = fs.FreeReg;
		var key = new ExpDesc();
		var val = new ExpDesc();
		if (_lexer.Token.TokenType == TK.NAME)
		{
			CheckLimit(fs, cc.NumRecord, LuaLimits.MAX_INT,
				"items in a constructor");
			CodeString(key, CheckName());
		}
		// ls->t.token == '['
		else
		{
			YIndex(key);
		}
		cc.NumRecord++;
		CheckNext('=');
		var rkkey = Coder.Exp2RK(fs, key);
		Expr(val);
		Coder.CodeABC(fs, OpCode.OP_SETTABLE, cc.ExpTable.Info, rkkey,
			Coder.Exp2RK(fs, val));
		fs.FreeReg = reg; // free registers
	}

	private static void CloseListField(FuncState fs, ConstructorControl cc)
	{
		// there is no list item
		if (cc.ExpLastItem.Kind == ExpKind.VVOID)
			return;

		Coder.Exp2NextReg(fs, cc.ExpLastItem);
		cc.ExpLastItem.Kind = ExpKind.VVOID;
		if (cc.NumToStore == LuaDef.LFIELDS_PER_FLUSH)
		{
			// flush
			Coder.SetList(fs, cc.ExpTable.Info, cc.NumArray, cc.NumToStore);

			// no more item pending
			cc.NumToStore = 0;
		}
	}

	private static void LastListField(FuncState fs, ConstructorControl cc)
	{
		if (cc.NumToStore == 0)
			return;

		if (HasMultiRet(cc.ExpLastItem.Kind))
		{
			Coder.SetMultiRet(fs, cc.ExpLastItem);
			Coder.SetList(fs, cc.ExpTable.Info, cc.NumArray, LuaDef.LUA_MULTRET);

			// do not count last expression (unknown number of elements)
			cc.NumArray--;
		}
		else
		{
			if (cc.ExpLastItem.Kind != ExpKind.VVOID)
				Coder.Exp2NextReg(fs, cc.ExpLastItem);
			Coder.SetList(fs, cc.ExpTable.Info, cc.NumArray, cc.NumToStore);
		}
	}

	// listfield -> exp
	private void ListField(ConstructorControl cc)
	{
		Expr(cc.ExpLastItem);
		CheckLimit(_curFunc, cc.NumArray, LuaLimits.MAX_INT,
			"items in a constructor");
		cc.NumArray++;
		cc.NumToStore++;
	}

	// field -> listfield | recfield
	private void Field(ConstructorControl cc)
	{
		switch (_lexer.Token.Val1)
		{
			// may be 'listfield' or 'recfield'
			case (int)TK.NAME: 
				// expression?
				if (_lexer.GetLookAhead().Val1 != '=')
					ListField(cc);
				else
					RecField(cc);
				break;

			case '[': 
				RecField(cc);
				break;

			default: 
				ListField(cc);
				break;
		}
	}

	// converts an integer to a "floating point byte", represented as
	// (eeeeexxx), where the real value is (1xxx) * 2^(eeeee - 1) if
	// eeeee != 0 and (xxx) otherwise.
	private static int Integer2FloatingPointByte(uint x)
	{
		var e = 0; // exponent
		if (x < 8) return (int)x;
		while (x >= 0x10)
		{
			x = (x + 1) >> 1;
			++e;
		}
		return ((e + 1) << 3) | ((int)x - 8);
	}

	// constructor -> '{' [ field { sep field } [sep] ] '}'
	// sep -> ',' | ';'
	private void Constructor(ExpDesc t)
	{
		var fs = _curFunc;
		var line = _lexer.LineNumber;
		var pc = Coder.CodeABC(fs, OpCode.OP_NEWTABLE, 0, 0, 0);
		var cc = new ConstructorControl { ExpTable = t };
		InitExp(t, ExpKind.VRELOCABLE, pc);
		InitExp(cc.ExpLastItem, ExpKind.VVOID, 0); // no value (yet)
		Coder.Exp2NextReg(fs, t);
		CheckNext('{');
		do {
			LuaUtil.Assert(cc.ExpLastItem.Kind == ExpKind.VVOID ||
			            cc.NumToStore > 0);
			if (_lexer.Token.Val1 == '}')
				break;
			CloseListField(fs, cc);
			Field(cc);
		} while(TestNext(',') || TestNext(';'));
		CheckMatch('}', '{', line);
		LastListField(fs, cc);

		// set initial array size and table size
		// 因为没有实现 OP_NEWTABLE 对 ARG_B 和 ARG_C 的处理, 所以这里也暂不实现
		// 不影响逻辑, 只是效率差别
		// var ins = fs.Proto.Code[pc];
		// ins.SETARG_B( 0 );
		// ins.SETARG_C( 0 );

		// Forget it, let’s implement it anyway. It’s easier to check if the generated bytecode matches luac.
		var ins = fs.Proto.Code[pc];
		ins.SETARG_B(Integer2FloatingPointByte((uint)cc.NumArray));
		ins.SETARG_C(Integer2FloatingPointByte((uint)cc.NumRecord));
		fs.Proto.Code[pc] = ins; // Instruction is a value type. Sigh.
	}

	private void ParList()
	{
		var numParams = 0;
		_curFunc.Proto.IsVarArg = false;

		// is `parlist' not empty?
		if (_lexer.Token.Val1 != ')')
		{
			do {
				// ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
				switch (_lexer.Token.TokenType)
				{
					// param -> NAME
					case TK.NAME: 
						var v = NewLocalVar(CheckName());
						_actVars.Add(v);
						++numParams;
						break;

					case TK.DOTS: 
						_lexer.Next();
						_curFunc.Proto.IsVarArg = true;
						break;

					default: 
						_lexer.SyntaxError("<name> or '...' expected");
						break;
				}
			} while (!_curFunc.Proto.IsVarArg && TestNext(','));
		}
		AdjustLocalVars(numParams);
		_curFunc.Proto.NumParams = _curFunc.NumActVar;
		Coder.ReserveRegs(_curFunc, _curFunc.NumActVar);
	}

	private void Body(ExpDesc e, bool isMethod, int line, string name)
	{
		var newFs = new FuncState();
		var block = new BlockCnt();
		newFs.Proto = AddPrototype();
		newFs.Proto.LineDefined = line;
		newFs.Proto.Name = name;
		OpenFunc(newFs, block);
		CheckNext('(');
		if (isMethod)
		{
			// create `self' parameter
			var v = NewLocalVar("self");
			_actVars.Add(v);
			AdjustLocalVars(1);
		}
		ParList();
		CheckNext(')');
		StatList();
		newFs.Proto.LastLineDefined = _lexer.LineNumber;
		CheckMatch((int)TK.END, (int)TK.FUNCTION, line);
		CodeClosure(e);
		CloseFunc();
	}

	private void FuncArgs(ExpDesc e, int line)
	{
		var args = new ExpDesc();
		switch (_lexer.Token.Val1)
		{
			// funcargs -> `(' [ explist ] `)'
			case '(': 
				_lexer.Next();
				if (_lexer.Token.Val1 == ')') // arg list is empty?
					args.Kind = ExpKind.VVOID;
				else {
					ExpList(args);
					Coder.SetMultiRet(_curFunc, args);
				}
				CheckMatch(')', '(', line);
				break;

			// funcargs -> constructor
			case '{': 
				Constructor(args);
				break;

			// funcargs -> STRING
			case (int)TK.STRING:
				CodeString(args, _lexer.Token.str!);
				_lexer.Next();
				break;

			default: 
				_lexer.SyntaxError( "function arguments expected" );
				break;
		}

		LuaUtil.Assert(e.Kind == ExpKind.VNONRELOC);
		var baseReg = e.Info;
		int nparams;
		if (HasMultiRet(args.Kind))
			nparams = LuaDef.LUA_MULTRET;
		else {
			if (args.Kind != ExpKind.VVOID)
				Coder.Exp2NextReg(_curFunc, args); // close last argument
			nparams = _curFunc.FreeReg - (baseReg + 1);
		}
		InitExp(e, ExpKind.VCALL, Coder.CodeABC(_curFunc,
			OpCode.OP_CALL, baseReg, nparams + 1, 2));
		Coder.FixLine( _curFunc, line );

		// call remove function and arguments and leaves
		// (unless changed) one result
		_curFunc.FreeReg = baseReg + 1;
	}

	// ==============================================================
	// Expression parsing
	// ==============================================================

	// primaryexp -> NAME | '(' expr ')'
	private void PrimaryExp(ExpDesc e)
	{
		switch (_lexer.Token.Val1)
		{
			case '(': 
				var line = _lexer.LineNumber;
				_lexer.Next();
				Expr(e);
				CheckMatch(')', '(', line);
				Coder.DischargeVars(_curFunc, e);
				return;

			case (int)TK.NAME: 
				SingleVar(e, out _);
				return;

			default: 
				_lexer.SyntaxError("unexpected symbol: " + _lexer.Token);
				return;
		}
	}

	// suffixedexp -> primaryexp { '.' NAME | '[' exp ']' | ':' NAME funcargs | funcargs
	private void SuffixedExp(ExpDesc e)
	{
		var fs = _curFunc;
		var line = _lexer.LineNumber;
		PrimaryExp(e);
		for (;;)
		{
			switch (_lexer.Token.Val1)
			{
				case '.': // fieldsel
					FieldSel(e);
					break;
				case '[': { // `[' exp1 `]'
					var key = new ExpDesc();
					Coder.Exp2AnyRegUp(fs, e);
					YIndex(key);
					Coder.Indexed(fs, e, key);
					break;
				}
				case ':': { // `:' NAME funcargs
					var key = new ExpDesc();
					_lexer.Next();
					CodeString(key, CheckName());
					Coder.Self(fs, e, key);
					FuncArgs(e, line);
					break;
				}
				case '(':
				case (int)TK.STRING:
				case '{': // funcargs
					Coder.Exp2NextReg(_curFunc, e);
					FuncArgs(e, line);
					break;
				default: return;
			}
		}
	}

	private void SimpleExp(ExpDesc e)
	{
		var t = _lexer.Token;
		switch (t.Val1)
		{
			case (int)TK.NUMBER: 
				InitExp(e, ExpKind.VKNUM, 0);
				e.NumberValue = t.Val2;
				break;

			case (int)TK.STRING:
				CodeString(e, _lexer.Token.str!);
				break;

			case (int)TK.NIL: 
				InitExp(e, ExpKind.VNIL, 0);
				break;

			case (int)TK.TRUE: 
				InitExp(e, ExpKind.VTRUE, 0);
				break;

			case (int)TK.FALSE: 
				InitExp(e, ExpKind.VFALSE, 0);
				break;

			case (int)TK.DOTS: 
				CheckCondition(_curFunc.Proto.IsVarArg,
					"Cannot use '...' outside a vararg function");
				InitExp(e, ExpKind.VVARARG,
					Coder.CodeABC(_curFunc, OpCode.OP_VARARG, 0, 1, 0));
				break;

			case '{': 
				Constructor(e);
				return;

			case (int)TK.FUNCTION: 
				_lexer.Next();
				Body(e, false, _lexer.LineNumber, "(anonymous)");
				return;

			default: 
				SuffixedExp(e);
				return;
		}
		_lexer.Next();
	}

	private static int? SearchUpValues(FuncState fs, string name)
	{
		var upvalues = fs.Proto.Upvalues;
		for (var i = 0; i < upvalues.Count; ++i)
		{
			if (upvalues[i].Name == name)
				return i;
		}
		return null;
	}

	private static int NewUpValue(FuncState fs, string name, ExpDesc e, bool isEnv = false)
	{
		var f = fs.Proto;
		var idx = f.Upvalues.Count;
		var upval = new UpValueDesc
		{
			InStack = (e.Kind == ExpKind.VLOCAL),
			Index = e.Info,
			Name = name,
			IsEnv = isEnv,
		};
		f.Upvalues.Add(upval);
		return idx;
	}

	private void CodeString(ExpDesc e, string s) => 
		InitExp(e, ExpKind.VK, Coder.StringK(_curFunc, s));

	private static void InitExp(ExpDesc e, ExpKind k, int i)
	{
		e.Kind = k;
		e.Info = i;
		e.ExitTrue = Coder.NO_JUMP;
		e.ExitFalse = Coder.NO_JUMP;
	}
}