﻿using System.Runtime.CompilerServices;
using CSLua.Util;
using NotImplementedException = System.NotImplementedException;
// ReSharper disable SwitchStatementHandlesSomeKnownEnumValuesWithDefault
// ReSharper disable InconsistentNaming

namespace CSLua.Parse;

using InstructionPtr = Pointer<Instruction>;

public readonly record struct Instruction(uint Val)
{
	public static explicit operator Instruction(uint val) => new(val);
	public static explicit operator uint(Instruction i) => i.Val;

	public override string ToString()
	{
		var op      = GET_OPCODE();
		var a    = GETARG_A();
		var b    = GETARG_B();
		var c    = GETARG_C();
		var ax   = GETARG_Ax();
		var bx   = GETARG_Bx();
		var sbx  = GETARG_sBx();
		var mode = OpCodeInfo.GetMode(op);
		switch (mode.OpMode)
		{
			case OpMode.iABC:
			{
				var ret = $"{op,-9} {a}";
				if (mode.BMode != OpArgMask.OpArgN)
					ret += " " + (ISK(b) ? MYK(INDEXK(b)) : b);
				if (mode.CMode != OpArgMask.OpArgN)
					ret += " " + (ISK(c) ? MYK(INDEXK(c)) : c);
				return ret;
			}
			case OpMode.iABx:
			{
				var ret = $"{op,-9} {a}";
				if (mode.BMode == OpArgMask.OpArgK)
					ret += " " + MYK(bx);
				else if (mode.BMode == OpArgMask.OpArgU)
					ret += " " + bx;
				return ret;
			}
			case OpMode.iAsBx:
				return $"{op,-9} {a} {sbx}";
			case OpMode.iAx:
				return $"{op,-9} {MYK(ax)}";
			default:
				throw new NotImplementedException();
		}
	}

	public const int SIZE_C = 9;
	public const int SIZE_B = 9;
	public const int SIZE_Bx = (SIZE_C + SIZE_B);
	public const int SIZE_A = 8;
	public const int SIZE_Ax = (SIZE_C + SIZE_B + SIZE_A);

	public const int SIZE_OP = 6;

	public const int POS_OP = 0;
	public const int POS_A = (POS_OP + SIZE_OP);
	public const int POS_C = (POS_A + SIZE_A);
	public const int POS_B = (POS_C + SIZE_C);
	public const int POS_Bx = POS_C;
	public const int POS_Ax = POS_A;

#pragma warning disable 0429
	public const int MAXARG_Bx  = SIZE_Bx < LuaConf.LUAI_BITSINT
			? ((1 << SIZE_Bx) - 1) : LuaLimits.MAX_INT;
	public const int MAXARG_sBx = SIZE_Bx < LuaConf.LUAI_BITSINT
			? (MAXARG_Bx >> 1) : LuaLimits.MAX_INT;
#pragma warning restore 0429

	public const int MAXARG_Ax = ((1 << SIZE_Ax) - 1);

	public const int MAXARG_A = ((1 << SIZE_A) - 1);
	public const int MAXARG_B = ((1 << SIZE_B) - 1);
	public const int MAXARG_C = ((1 << SIZE_C) - 1);

	public const int BITRK = (1 << (SIZE_B - 1));

	public const int MAXINDEXRK = (BITRK - 1);

	public static int RKASK(int x) => (x | BITRK);

	public static bool ISK(int x) => ((x) & BITRK) != 0;

	public static int INDEXK(int r) => (r & ~BITRK);

	public static int MYK(int x) => (-1 - x);

	public static uint MASK1(int size, int pos) => 
		((~((~((uint)0)) << size)) << pos);

	public static uint MASK0(int size, int pos) => (~MASK1(size, pos));

	public OpCode GET_OPCODE() => 
		(OpCode)((Val >> POS_OP) & MASK1(SIZE_OP, 0));

	public Instruction SET_OPCODE(OpCode op)
	{
		var nValue = (Val & MASK0(SIZE_OP, POS_OP)) |
		        ((((uint)op) << POS_OP) & MASK1(SIZE_OP, POS_OP));
		return new Instruction(nValue);
	}

	public int GETARG(int pos, int size) => 
		(int)((Val >> pos) & MASK1(size, 0));

	public Instruction SETARG(int value, int pos, int size)
	{
		var nValue = ((Val & MASK0(size, pos)) |
		         (((uint)value << pos) & MASK1(size, pos)));
		return new Instruction(nValue);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int GETARG_A() => GETARG(POS_A, SIZE_A);

	public Instruction SETARG_A(int value) => SETARG(value, POS_A, SIZE_A);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int GETARG_B() => GETARG(POS_B, SIZE_B);

	public Instruction SETARG_B(int value) => SETARG(value, POS_B, SIZE_B);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int GETARG_C() => GETARG(POS_C, SIZE_C);

	public Instruction SETARG_C(int value) => SETARG(value, POS_C, SIZE_C);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int GETARG_Bx() => GETARG(POS_Bx, SIZE_Bx);

	public Instruction SETARG_Bx(int value) => SETARG(value, POS_Bx, SIZE_Bx);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int GETARG_Ax() => GETARG(POS_Ax, SIZE_Ax);

	public Instruction SETARG_Ax(int value) => SETARG(value, POS_Ax, SIZE_Ax);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int GETARG_sBx() => GETARG_Bx() - MAXARG_sBx;

	public Instruction SETARG_sBx(int value) => SETARG_Bx(value + MAXARG_sBx);

	public static Instruction CreateAB(OpCode op, int a, int b) =>
		CreateABC(op, a, b, 0);
	
	public static Instruction CreateABC(OpCode op, int a, int b, int c)
	{
		return (Instruction)((((uint)op) << POS_OP)
		                      | ((uint)a << POS_A)
		                      | ((uint)b << POS_B)
		                      | ((uint)c << POS_C));
	}

	public static Instruction CreateAC(OpCode op, int a, int c) =>
		CreateABC(op, a, 0, c);

	public static Instruction CreateABx(OpCode op, int a, uint bc)
	{
		return (Instruction)((((uint)op) << POS_OP)
		                      | ((uint)a << POS_A)
		                      | (bc << POS_Bx));
	}
	
	public static Instruction CreateAsBx(OpCode op, int a, int sBx) => 
		CreateABx(op, a, ((uint)sBx) + MAXARG_sBx);

	public static Instruction CreateAx(OpCode op, int a) => 
		(Instruction)((((uint)op) << POS_OP) | ((uint)a << POS_Ax));
}

public static class Coder
{
	public const int NO_JUMP = -1;
	private const int NO_REG = ((1 << Instruction.SIZE_A) - 1);

	private static void FreeReg(FuncState fs, int reg)
	{
		if (!Instruction.ISK(reg) && reg >= fs.NumActVar)
		{
			fs.FreeReg--;
			LuaUtil.Assert(reg == fs.FreeReg);
		}
	}

	private static void FreeExp(FuncState fs, ExpDesc e)
	{
		if (e.Kind == ExpKind.VNONRELOC) FreeReg(fs, e.Info);
	}

	private static bool IsNumeral(ExpDesc e) =>
		e.Kind == ExpKind.VKNUM
		&& e is { ExitTrue: NO_JUMP, ExitFalse: NO_JUMP };

	private static bool ConstFolding(OpCode op, ExpDesc e1, ExpDesc e2)
	{
		if (!IsNumeral(e1) || !IsNumeral(e2))
			return false;

		if (op is OpCode.OP_DIV or OpCode.OP_MOD && e2.NumberValue == 0.0)
			return false; // do not attempt to divide by 0

		// ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
		switch (op)
		{
			case OpCode.OP_ADD:
				e1.NumberValue += e2.NumberValue;
				break;
			case OpCode.OP_SUB:
				e1.NumberValue -= e2.NumberValue;
				break;
			case OpCode.OP_MUL:
				e1.NumberValue *= e2.NumberValue;
				break;
			case OpCode.OP_DIV:
				e1.NumberValue /= e2.NumberValue;
				break;
			case OpCode.OP_MOD:
				var v1 = e1.NumberValue;
				var v2 = e2.NumberValue;
				e1.NumberValue = v1 - Math.Floor(v1 / v2) * v2;
				break;
			case OpCode.OP_POW:
				e1.NumberValue = Math.Pow(e1.NumberValue, e2.NumberValue);
				break;
			case OpCode.OP_UNM:
				e1.NumberValue = -e1.NumberValue;
				break;
			default:
				throw new LuaException("ConstFolding unknown op: " + op);
		}

		return true;
	}

	public static void FixLine(FuncState fs, int line) => 
		fs.Proto.LineInfo[fs.Pc - 1] = line;

	private static void CodeArith(FuncState fs, OpCode op,
		ExpDesc e1, ExpDesc e2, int line)
	{
		if (ConstFolding(op, e1, e2)) return;

		var o2 = (op != OpCode.OP_UNM && op != OpCode.OP_LEN)
			? Exp2RK(fs, e2) : 0;
		var o1 = Exp2RK(fs, e1);
		if (o1 > o2)
		{
			FreeExp(fs, e1);
			FreeExp(fs, e2);
		}
		else
		{
			FreeExp(fs, e2);
			FreeExp(fs, e1);
		}
		e1.Info = CodeABC(fs, op, 0, o1, o2);
		e1.Kind = ExpKind.VRELOCABLE;
		FixLine(fs, line);
	}

	public static bool TestTMode(OpCode op) => OpCodeInfo.GetMode(op).TMode;

	public static bool TestAMode(OpCode op) => OpCodeInfo.GetMode(op).AMode;

	private static void FixJump(FuncState fs, int pc, int dest)
	{
		var jmp = fs.Proto.Code[pc];
		var offset = dest - (pc + 1);
		LuaUtil.Assert(dest != NO_JUMP);
		if (Math.Abs(offset) > Instruction.MAXARG_sBx)
			fs.Lexer.SyntaxError("Control structure too long");
		fs.Proto.Code[pc] = jmp.SETARG_sBx(offset);
	}

	// returns current `pc' and mark it as a jump target
	// (to avoid wrong optimizations with consecutive
	// instructions not in the same basic block)
	public static int GetLabel(FuncState fs)
	{
		fs.LastTarget = fs.Pc;
		return fs.Pc;
	}

	private static int GetJump(FuncState fs, int pc)
	{
		var offset = fs.Proto.Code[pc].GETARG_sBx();
		if (offset == NO_JUMP) // point to itself represents end of list
			return NO_JUMP; // end of list
		return (pc + 1) + offset; // turn offset into absolute position
	}

	private static InstructionPtr GetJumpControl(FuncState fs, int pc)
	{
		var pi = new InstructionPtr(fs.Proto.Code, pc);
		if (pc >= 1 && TestTMode((pi - 1).Value.GET_OPCODE()))
			return (pi - 1);
		return pi;
	}

	// check whether list has any jump that do not produce a value
	// (or produce an inverted value)
	private static bool NeedValue(FuncState fs, int list)
	{
		for (; list != NO_JUMP; list = GetJump(fs, list))
		{
			var i = GetJumpControl(fs, list).Value;
			if (i.GET_OPCODE() != OpCode.OP_TESTSET)
				return true;
		}
		return false;
	}

	private static bool PatchTestReg(FuncState fs, int node, int reg)
	{
		var pi = GetJumpControl(fs, node);
		if (pi.Value.GET_OPCODE() != OpCode.OP_TESTSET)
			return false; // cannot patch other instructions

		if (reg != NO_REG && reg != pi.Value.GETARG_B())
			pi.Value = pi.Value.SETARG_A(reg);
		else
			pi.Value = Instruction.CreateABC(OpCode.OP_TEST,
				pi.Value.GETARG_B(), 0, pi.Value.GETARG_C());

		return true;
	}

	private static void RemoveValues(FuncState fs, int list)
	{
		for (; list != NO_JUMP; list = GetJump(fs, list))
			PatchTestReg(fs, list, NO_REG);
	}

	private static void PatchListAux(
		FuncState fs, int list, int vtarget, int reg, int dtarget)
	{
		while (list != NO_JUMP)
		{
			var next = GetJump(fs, list);
			if (PatchTestReg(fs, list, reg))
				FixJump(fs, list, vtarget);
			else
				FixJump(fs, list, dtarget); // jump to default target
			list = next;
		}
	}

	private static void DischargeJpc(FuncState fs)
	{
		PatchListAux(fs, fs.Jpc, fs.Pc, NO_REG, fs.Pc);
		fs.Jpc = NO_JUMP;
	}

	private static void InvertJump(FuncState fs, ExpDesc e)
	{
		var pc = GetJumpControl(fs, e.Info);
		LuaUtil.Assert(TestTMode(pc.Value.GET_OPCODE())
		            && pc.Value.GET_OPCODE() != OpCode.OP_TESTSET
		            && pc.Value.GET_OPCODE() != OpCode.OP_TEST );
		pc.Value = pc.Value.SETARG_A(pc.Value.GETARG_A() == 0 ? 1 : 0);
	}

	private static int JumpOnCond(FuncState fs, ExpDesc e, bool cond)
	{
		if (e.Kind == ExpKind.VRELOCABLE)
		{
			var ie = fs.GetCode( e ).Value;
			if (ie.GET_OPCODE() == OpCode.OP_NOT)
			{
				fs.Pc--; // remove previous OP_NOT
				return CondJump( fs, OpCode.OP_TEST, ie.GETARG_B(), 0,
					(cond ? 0 : 1) );
			}
			// else go through
		}
		Discharge2AnyReg(fs, e);
		FreeExp(fs, e);
		return CondJump(fs, OpCode.OP_TESTSET, NO_REG, e.Info,
			(cond ? 1 : 0));
	}

	public static void GoIfTrue(FuncState fs, ExpDesc e)
	{
		int pc; // pc of last jump
		DischargeVars(fs, e);

		switch (e.Kind)
		{
			case ExpKind.VJMP:
				InvertJump(fs, e);
				pc = e.Info;
				break;

			case ExpKind.VK:
			case ExpKind.VKNUM:
			case ExpKind.VTRUE:
				pc = NO_JUMP;
				break;

			default:
				pc = JumpOnCond(fs, e, false);
				break;
		}

		// insert last jump in `f' list
		e.ExitFalse = Concat(fs, e.ExitFalse, pc);
		PatchToHere(fs, e.ExitTrue);
		e.ExitTrue = NO_JUMP;
	}

	public static void GoIfFalse(FuncState fs, ExpDesc e)
	{
		DischargeVars(fs, e);
		var pc = e.Kind switch // pc of last jump
		{
			ExpKind.VJMP => e.Info,
			ExpKind.VNIL or ExpKind.VFALSE => NO_JUMP,
			_ => JumpOnCond(fs, e, true)
		};

		// insert last jump in 't' list
		e.ExitTrue = Concat(fs, e.ExitTrue, pc);
		PatchToHere(fs, e.ExitFalse);
		e.ExitFalse = NO_JUMP;
	}

	private static void CodeNot(FuncState fs, ExpDesc e)
	{
		DischargeVars(fs, e);

		switch (e.Kind)
		{
			case ExpKind.VNIL:
			case ExpKind.VFALSE:
				e.Kind = ExpKind.VTRUE;
				break;

			case ExpKind.VK:
			case ExpKind.VKNUM:
			case ExpKind.VTRUE:
				e.Kind = ExpKind.VFALSE;
				break;

			case ExpKind.VJMP:
				InvertJump(fs, e);
				break;

			case ExpKind.VRELOCABLE:
			case ExpKind.VNONRELOC:
				Discharge2AnyReg(fs, e);
				FreeExp(fs, e);
				e.Info = CodeABC(fs, OpCode.OP_NOT, 0, e.Info, 0);
				e.Kind = ExpKind.VRELOCABLE;
				break;

			default:
				throw new LuaException("CodeNot unknown e.Kind: " + e.Kind);
		}

		// Swap true and false lists
		(e.ExitFalse, e.ExitTrue) = (e.ExitTrue, e.ExitFalse);

		RemoveValues(fs, e.ExitFalse);
		RemoveValues(fs, e.ExitTrue);
	}

	private static void CodeComp(
		FuncState fs, OpCode op, int cond, ExpDesc e1, ExpDesc e2)
	{
		var o1 = Exp2RK(fs, e1);
		var o2 = Exp2RK(fs, e2);
		FreeExp(fs, e2);
		FreeExp(fs, e1);

		// exchange args to replace by '<' or '<='
		if (cond == 0 && op != OpCode.OP_EQ) 
		{
			(o1, o2) = (o2, o1);
			cond = 1;
		}
		e1.Info = CondJump(fs, op, cond, o1, o2);
		e1.Kind = ExpKind.VJMP;
	}

	public static void Prefix(FuncState fs, UnOpr op, ExpDesc e, int line)
	{
		var e2 = new ExpDesc
		{
			ExitTrue = NO_JUMP,
			ExitFalse = NO_JUMP,
			Kind = ExpKind.VKNUM,
			NumberValue = 0.0
		};

		switch (op)
		{
			case UnOpr.MINUS: 
				if (IsNumeral(e)) // minus constant?
				{
					e.NumberValue = -e.NumberValue;
				}
				else
				{
					Exp2AnyReg(fs, e);
					CodeArith(fs, OpCode.OP_UNM, e, e2, line);
				}
				break;

			case UnOpr.NOT: 
				CodeNot(fs, e);
				break;

			case UnOpr.LEN: 
				Exp2AnyReg(fs, e); // cannot operate on constants
				CodeArith(fs, OpCode.OP_LEN, e, e2, line);
				break;

			default:
				throw new LuaException("[Coder]Prefix Unknown UnOpr: " + op);
		}
	}

	public static void Infix(FuncState fs, BinOpr op, ExpDesc e)
	{
		switch (op)
		{
			case BinOpr.AND: 
				GoIfTrue(fs, e);
				break;

			case BinOpr.OR: 
				GoIfFalse(fs, e);
				break;

			case BinOpr.CONCAT: 
				Exp2NextReg(fs, e); // operand must be on the 'stack'
				break;

			case BinOpr.ADD:
			case BinOpr.SUB:
			case BinOpr.MUL:
			case BinOpr.DIV:
			case BinOpr.MOD:
			case BinOpr.POW: 
				if (!IsNumeral(e))
					Exp2RK(fs, e);
				break;

			default: 
				Exp2RK(fs, e);
				break;
		}
	}

	public static void PosFix(
		FuncState fs, BinOpr op, ExpDesc e1, ExpDesc e2, int line)
	{
		switch (op)
		{
			case BinOpr.AND: 
				LuaUtil.Assert(e1.ExitTrue == NO_JUMP);
				DischargeVars(fs, e2);
				e2.ExitFalse = Concat(fs, e2.ExitFalse, e1.ExitFalse);
				e1.CopyFrom(e2);
				break;
			case BinOpr.OR: 
				LuaUtil.Assert(e1.ExitFalse == NO_JUMP);
				DischargeVars(fs, e2);
				e2.ExitTrue = Concat(fs, e2.ExitTrue, e1.ExitTrue);
				e1.CopyFrom(e2);
				break;
			case BinOpr.CONCAT: 
				Exp2Val(fs, e2);
				var pe2 = fs.GetCode(e2);
				if (e2.Kind == ExpKind.VRELOCABLE &&
				    pe2.Value.GET_OPCODE() == OpCode.OP_CONCAT)
				{
					LuaUtil.Assert(e1.Info == pe2.Value.GETARG_B() - 1);
					FreeExp(fs, e1);
					pe2.Value = pe2.Value.SETARG_B(e1.Info);
					e1.Kind = ExpKind.VRELOCABLE;
					e1.Info = e2.Info;
				}
				else
				{
					// operand must be on the `stack'
					Exp2NextReg(fs, e2);
					CodeArith(fs, OpCode.OP_CONCAT, e1, e2, line);
				}
				break;
			case BinOpr.ADD: 
				CodeArith(fs, OpCode.OP_ADD, e1, e2, line);
				break;
			case BinOpr.SUB: 
				CodeArith(fs, OpCode.OP_SUB, e1, e2, line);
				break;
			case BinOpr.MUL: 
				CodeArith(fs, OpCode.OP_MUL, e1, e2, line);
				break;
			case BinOpr.DIV: 
				CodeArith(fs, OpCode.OP_DIV, e1, e2, line);
				break;
			case BinOpr.MOD: 
				CodeArith(fs, OpCode.OP_MOD, e1, e2, line);
				break;
			case BinOpr.POW: 
				CodeArith(fs, OpCode.OP_POW, e1, e2, line);
				break;
			case BinOpr.EQ: 
				CodeComp(fs, OpCode.OP_EQ, 1, e1, e2);
				break;
			case BinOpr.LT: 
				CodeComp(fs, OpCode.OP_LT, 1, e1, e2);
				break;
			case BinOpr.LE: 
				CodeComp(fs, OpCode.OP_LE, 1, e1, e2);
				break;
			case BinOpr.NE: 
				CodeComp(fs, OpCode.OP_EQ, 0, e1, e2);
				break;
			case BinOpr.GT: 
				CodeComp(fs, OpCode.OP_LT, 0, e1, e2);
				break;
			case BinOpr.GE: 
				CodeComp(fs, OpCode.OP_LE, 0, e1, e2);
				break;
			default: LuaUtil.Assert(false); break;
		}
	}

	public static int Jump( FuncState fs )
	{
		var jpc = fs.Jpc; // save list of jumps to here
		fs.Jpc = NO_JUMP;
		var j = CodeAsBx(fs, OpCode.OP_JMP, 0, NO_JUMP);
		j = Concat(fs, j, jpc);
		return j;
	}

	public static void JumpTo(FuncState fs, int target) => 
		PatchList(fs, Jump(fs), target);

	public static void Ret(FuncState fs, int first, int nret) => 
		CodeABC(fs, OpCode.OP_RETURN, first, nret + 1, 0);

	private static int CondJump(FuncState fs, OpCode op, int a, int b, int c)
	{
		CodeABC(fs, op, a, b, c);
		return Jump(fs);
	}

	public static void PatchList(FuncState fs, int list, int target)
	{
		if (target == fs.Pc)
			PatchToHere(fs, list);
		else
		{
			LuaUtil.Assert(target < fs.Pc);
			PatchListAux(fs, list, target, NO_REG, target);
		}
	}

	public static void PatchClose(FuncState fs, int list, int level)
	{
		level++; // argument is +1 to reserve 0 as non-op
		while (list != NO_JUMP)
		{
			var next = GetJump(fs, list);
			var pi = new InstructionPtr(fs.Proto.Code, list);
			LuaUtil.Assert(pi.Value.GET_OPCODE() == OpCode.OP_JMP &&
			            (pi.Value.GETARG_A() == 0 ||
			              pi.Value.GETARG_A() >= level));
			pi.Value = pi.Value.SETARG_A(level);
			list = next;
		}
	}

	public static void PatchToHere(FuncState fs, int list)
	{
		GetLabel(fs);
		fs.Jpc = Concat(fs, fs.Jpc, list);
	}

	public static int Concat(FuncState fs, int l1, int l2)
	{
		if (l2 == NO_JUMP)
			return l1;
		if (l1 == NO_JUMP)
			return l2;
		var list = l1;
		var next = GetJump(fs, list);

		// find last element
		while (next != NO_JUMP)
		{
			list = next;
			next = GetJump(fs, list);
		}
		FixJump(fs, list, l2);
		return l1;
	}

	public static int StringK(FuncState fs, string s)
	{
		var o = new TValue();
		o.SetString(s);
		return AddK(fs, ref o, ref o);
	}

	public static int NumberK(FuncState fs, double r)
	{
		var o = new TValue();
		o.SetDouble(r);
		return AddK(fs, ref o, ref o);
	}

	private static int BoolK(FuncState fs, bool b)
	{
		var o = new TValue();
		o.SetBool(b);
		return AddK(fs, ref o, ref o);
	}

	private static int NilK(FuncState fs)
	{
		// // cannot use nil as key;
		// // instead use table itself to represent nil
		// var k = fs.H;
		// var o = new LuaNil();
		// return AddK( fs, k, o );

		var o = new TValue();
		o.SetNil();
		return AddK(fs, ref o, ref o);
	}

	public static int AddK(FuncState fs, ref TValue key, ref TValue v)
	{
		if (fs.H.TryGetValue(key, out var idx))
			return idx;

		idx = fs.Proto.K.Count;
		fs.H.Add(key, idx);
		fs.Proto.K.Add(v);
		return idx;
	}

	public static void Indexed(FuncState fs, ExpDesc t, ExpDesc k)
	{
		t.Ind.T = t.Info;
		t.Ind.Idx = Exp2RK(fs, k);
		t.Ind.Vt = (t.Kind == ExpKind.VUPVAL) ? ExpKind.VUPVAL
			: ExpKind.VLOCAL; // FIXME
		t.Kind = ExpKind.VINDEXED;
	}

	private static bool HasJumps(ExpDesc e) => e.ExitTrue != e.ExitFalse;

	private static int CodeLabel(FuncState fs, int a, int b, int jump)
	{
		GetLabel(fs); // those instructions may be jump targets
		return CodeABC(fs, OpCode.OP_LOADBOOL, a, b, jump);
	}

	private static void Discharge2Reg(FuncState fs, ExpDesc e, int reg)
	{
		DischargeVars(fs, e);
		switch (e.Kind)
		{
			case ExpKind.VNIL: 
				CodeNil(fs, reg, 1);
				break;
			case ExpKind.VFALSE:
			case ExpKind.VTRUE: 
				CodeABC(fs, OpCode.OP_LOADBOOL, reg,
					(e.Kind == ExpKind.VTRUE ? 1 : 0), 0);
				break;
			case ExpKind.VK: 
				CodeK(fs, reg, e.Info);
				break;
			case ExpKind.VKNUM: 
				CodeK(fs, reg, NumberK(fs, e.NumberValue));
				break;
			case ExpKind.VRELOCABLE: 
				var pi = fs.GetCode(e);
				pi.Value = pi.Value.SETARG_A(reg);
				break;
			case ExpKind.VNONRELOC: 
				if (reg != e.Info)
					CodeABC(fs, OpCode.OP_MOVE, reg, e.Info, 0);
				break;
			default: 
				LuaUtil.Assert(e.Kind is ExpKind.VVOID or ExpKind.VJMP);
				return; // nothing to do...
		}
		e.Info = reg;
		e.Kind = ExpKind.VNONRELOC;
	}

	public static void CheckStack(FuncState fs, int n)
	{
		var newStack = fs.FreeReg + n;
		if (newStack <= fs.Proto.MaxStackSize) return;

		if (newStack >= LuaLimits.MAXSTACK) 
			fs.Lexer.SyntaxError("function or expression too complex");
		fs.Proto.MaxStackSize = (byte)newStack;
	}

	public static void ReserveRegs(FuncState fs, int n)
	{
		CheckStack(fs, n);
		fs.FreeReg += n;
	}

	private static void Discharge2AnyReg(FuncState fs, ExpDesc e)
	{
		if (e.Kind != ExpKind.VNONRELOC)
		{
			ReserveRegs(fs, 1);
			Discharge2Reg(fs, e, fs.FreeReg - 1);
		}
	}

	private static void Exp2Reg(FuncState fs, ExpDesc e, int reg)
	{
		Discharge2Reg(fs, e, reg);
		if (e.Kind == ExpKind.VJMP)
			e.ExitTrue = Concat(fs, e.ExitTrue, e.Info);

		if (HasJumps(e))
		{
			var p_f = NO_JUMP;
			var p_t = NO_JUMP;
			if (NeedValue(fs, e.ExitTrue) || NeedValue(fs, e.ExitFalse))
			{
				var fj = (e.Kind == ExpKind.VJMP) ? NO_JUMP : Jump(fs);
				p_f = CodeLabel(fs, reg, 0, 1);
				p_t = CodeLabel(fs, reg, 1, 0);
				PatchToHere(fs, fj);
			}

			// position after whole expression
			var final = GetLabel(fs);
			PatchListAux(fs, e.ExitFalse, final, reg, p_f);
			PatchListAux(fs, e.ExitTrue,  final, reg, p_t);
		}

		e.ExitFalse = NO_JUMP;
		e.ExitTrue  = NO_JUMP;
		e.Info = reg;
		e.Kind = ExpKind.VNONRELOC;
	}

	public static void Exp2NextReg(FuncState fs, ExpDesc e)
	{
		DischargeVars(fs, e);
		FreeExp(fs, e);
		ReserveRegs(fs, 1);
		Exp2Reg(fs, e, fs.FreeReg - 1);
	}

	public static void Exp2Val(FuncState fs, ExpDesc e)
	{
		if (HasJumps(e))
			Exp2AnyReg(fs, e);
		else
			DischargeVars(fs, e);
	}

	public static int Exp2RK(FuncState fs, ExpDesc e)
	{
		Exp2Val(fs, e);
		switch (e.Kind)
		{
			case ExpKind.VTRUE:
			case ExpKind.VFALSE:
			case ExpKind.VNIL:
			{
				// constant fits in RK operand?
				if (fs.Proto.K.Count <= Instruction.MAXINDEXRK)
				{
					e.Info = (e.Kind == ExpKind.VNIL) ? NilK(fs)
						: BoolK(fs, (e.Kind == ExpKind.VTRUE));
					e.Kind = ExpKind.VK;
					return Instruction.RKASK(e.Info);
				}

				break;
			}
			case ExpKind.VKNUM:
			case ExpKind.VK:
			{
				if (e.Kind == ExpKind.VKNUM)
				{
					e.Info = NumberK(fs, e.NumberValue);
					e.Kind = ExpKind.VK;
				}

				if (e.Info <= Instruction.MAXINDEXRK)
					return Instruction.RKASK(e.Info);
				break;
			}
			default: break;
		}

		return Exp2AnyReg(fs, e);
	}

	public static int Exp2AnyReg(FuncState fs, ExpDesc e)
	{
		DischargeVars(fs, e);
		if (e.Kind == ExpKind.VNONRELOC)
		{
			// exp is already in a register
			if (!HasJumps(e))
				return e.Info;

			// reg. is not a local?
			if (e.Info >= fs.NumActVar)
			{
				Exp2Reg(fs, e, e.Info);
				return e.Info;
			}
		}
		Exp2NextReg(fs, e); // default
		return e.Info;
	}

	public static void Exp2AnyRegUp(FuncState fs, ExpDesc e)
	{
		if (e.Kind != ExpKind.VUPVAL || HasJumps(e)) 
			Exp2AnyReg(fs, e);
	}

	public static void DischargeVars(FuncState fs, ExpDesc e)
	{
		switch(e.Kind)
		{
			case ExpKind.VLOCAL:
				e.Kind = ExpKind.VNONRELOC;
				break;

			case ExpKind.VUPVAL:
				e.Info = CodeABC(fs, OpCode.OP_GETUPVAL, 0, e.Info, 0);
				e.Kind = ExpKind.VRELOCABLE;
				break;

			case ExpKind.VINDEXED:
				var op = OpCode.OP_GETTABUP;
				FreeReg(fs, e.Ind.Idx);
				if (e.Ind.Vt == ExpKind.VLOCAL)
				{
					FreeReg(fs, e.Ind.T);
					op = OpCode.OP_GETTABLE;
				}
				e.Info = CodeABC(fs, op, 0, e.Ind.T, e.Ind.Idx);
				e.Kind = ExpKind.VRELOCABLE;
				break;

			case ExpKind.VVARARG:
			case ExpKind.VCALL:
				SetOneRet(fs, e);
				break;

			default: break;
		}
	}

	public static void SetReturns(FuncState fs, ExpDesc e, int nResults)
	{
		if (e.Kind == ExpKind.VCALL) { // expression is an open function call?
			var pi = fs.GetCode(e);
			pi.Value = pi.Value.SETARG_C(nResults + 1);
		}
		else if (e.Kind == ExpKind.VVARARG) {
			var pi = fs.GetCode(e);
			pi.Value = pi.Value.SETARG_B(nResults + 1).SETARG_A(fs.FreeReg);
			ReserveRegs(fs, 1);
		}
	}

	public static void SetMultiRet(FuncState fs, ExpDesc e) => 
		SetReturns(fs, e, LuaDef.LUA_MULTRET);

	public static void SetOneRet(FuncState fs, ExpDesc e)
	{
		// expression is an open function call?
		if (e.Kind == ExpKind.VCALL)
		{
			e.Kind = ExpKind.VNONRELOC;
			e.Info = (fs.GetCode(e)).Value.GETARG_A();
		}
		else if (e.Kind == ExpKind.VVARARG)
		{
			var pi = fs.GetCode(e);
			pi.Value = pi.Value.SETARG_B(2);
			e.Kind = ExpKind.VRELOCABLE; // can relocate its simple result
		}
	}

	public static void StoreVar(FuncState fs, ExpDesc v, ExpDesc e)
	{
		switch (v.Kind)
		{
			case ExpKind.VLOCAL: 
				FreeExp(fs, e);
				Exp2Reg(fs, e, v.Info);
				break;

			case ExpKind.VUPVAL: {
				var c = Exp2AnyReg(fs, e);
				CodeABC(fs, OpCode.OP_SETUPVAL, c, v.Info, 0);
				break;
			}

			case ExpKind.VINDEXED: {
				var op = (v.Ind.Vt == ExpKind.VLOCAL)
					? OpCode.OP_SETTABLE
					: OpCode.OP_SETTABUP;
				var c = Exp2RK(fs, e);
				CodeABC(fs, op, v.Ind.T, v.Ind.Idx, c);
				break;
			}

			default:
				throw new NotImplementedException("invalid var kind to store");
		}
		FreeExp(fs, e);
	}

	public static void Self(FuncState fs, ExpDesc e, ExpDesc key)
	{
		Exp2AnyReg(fs, e);
		var eReg = e.Info; // register where 'e' is placed
		FreeExp(fs, e);
		e.Info = fs.FreeReg; // base register for op_self
		e.Kind = ExpKind.VNONRELOC;
		ReserveRegs(fs, 2);
		CodeABC(fs, OpCode.OP_SELF, e.Info, eReg, Exp2RK(fs, key));
		FreeExp(fs, key);
	}

	public static void SetList(FuncState fs, int t, int nElems, int toStore)
	{
		var c = (nElems - 1) / LuaDef.LFIELDS_PER_FLUSH + 1;
		var b = (toStore == LuaDef.LUA_MULTRET) ? 0 : toStore;
		LuaUtil.Assert(toStore != 0);

		if (c <= Instruction.MAXARG_C)
		{
			CodeABC(fs, OpCode.OP_SETLIST, t, b, c);
		}
		else if (c <= Instruction.MAXARG_Ax)
		{
			CodeABC(fs, OpCode.OP_SETLIST, t, b, 0);
			CodeExtraArg(fs, c);
		}
		else
		{
			fs.Lexer.SyntaxError("constructor too long");
		}

		// free registers with list values
		fs.FreeReg = t + 1;
	}

	public static void CodeNil(FuncState fs, int from, int n)
	{
		var l = from + n - 1; // last register to set nil
		if (fs.Pc > fs.LastTarget) // no jumps to current position?
		{
			var previous = new InstructionPtr(fs.Proto.Code, fs.Pc - 1);
			if (previous.Value.GET_OPCODE() == OpCode.OP_LOADNIL)
			{
				var pfrom = previous.Value.GETARG_A();
				var pl = pfrom + previous.Value.GETARG_B();

				// can connect both?
				if ((pfrom <= from && from <= pl + 1) ||
				    (from <= pfrom && pfrom <= l + 1))
				{
					if (pfrom < from) from = pfrom; // from = min(from, pfrom)
					if (pl > l) l = pl; // l=max(l,pl)
					previous.Value = previous.Value.SETARG_A(from);
					previous.Value = previous.Value.SETARG_B(l - from);
					return;
				}
			}
			// else go through
		}

		// else no optimization
		CodeABC(fs, OpCode.OP_LOADNIL, from, n - 1, 0);
	}

	private static int CodeExtraArg(FuncState fs, int a)
	{
		LuaUtil.Assert(a <= Instruction.MAXARG_Ax);
		return Code(fs, Instruction.CreateAx(OpCode.OP_EXTRAARG, a));
	}

	public static int CodeK(FuncState fs, int reg, int k)
	{
		if (k <= Instruction.MAXARG_Bx)
			return CodeABx(fs, OpCode.OP_LOADK, reg, (uint)k);
		var p = CodeABx(fs, OpCode.OP_LOADKX, reg, 0);
		CodeExtraArg(fs, k);
		return p;
	}

	public static int CodeAsBx(FuncState fs, OpCode op, int a, int sBx) => 
		CodeABx(fs, op, a, ((uint)sBx)+Instruction.MAXARG_sBx);

	public static int CodeABx(FuncState fs, OpCode op, int a, uint bc)
	{
		var mode = OpCodeInfo.GetMode(op);
		LuaUtil.Assert(mode.OpMode is OpMode.iABx or OpMode.iAsBx);
		LuaUtil.Assert(mode.CMode == OpArgMask.OpArgN);
		LuaUtil.Assert(a < Instruction.MAXARG_A & bc <= Instruction.MAXARG_Bx);
		return Code(fs, Instruction.CreateABx(op, a, bc));
	}

	public static int CodeABC(FuncState fs, OpCode op, int a, int b, int c) => 
		Code(fs, Instruction.CreateABC(op, a, b, c));

	private static int Code(FuncState fs, Instruction i)
	{
		DischargeJpc(fs); // `pc' will change

		while (fs.Proto.Code.Count <= fs.Pc) 
			fs.Proto.Code.Add(new Instruction(LuaLimits.MAX_INT));
		fs.Proto.Code[fs.Pc] = i;

		while (fs.Proto.LineInfo.Count <= fs.Pc) 
			fs.Proto.LineInfo.Add(LuaLimits.MAX_INT);
		fs.Proto.LineInfo[fs.Pc] = fs.Lexer.LastLine;

		return fs.Pc++;
	}
}