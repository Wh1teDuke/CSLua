
namespace CSLua.Lib;

public static class LuaBitLib
{
	public const string LIB_NAME = "bit32";
	private const int LUA_NBITS = 32;
	private const uint ALLONES = ~(((~(uint)0) << (LUA_NBITS - 1)) << 1);
	
	public static NameFuncPair NameFuncPair => new(LIB_NAME, OpenLib);

	public static int OpenLib(ILuaState lua)
	{
		Span<NameFuncPair> define =
		[
			new("arshift", 	B_ArithShift),
			new("band", 	B_And),
			new("bnot", 	B_Not),
			new("bor", 		B_Or),
			new("bxor", 	B_Xor),
			new("btest", 	B_Test),
			new("extract", 	B_Extract),
			new("lrotate", 	B_LeftRotate),
			new("lshift", 	B_LeftShift),
			new("replace", 	B_Replace),
			new("rrotate", 	B_RightRotate),
			new("rshift", 	B_RightShift),
		];

		lua.NewLib(define);
		return 1;
	}

	private static uint Trim(uint x) => (x & ALLONES);

	private static uint Mask(int n) => ~((ALLONES << 1) << (n-1));

	private static int B_Shift(ILuaState lua, uint r, int i)
	{
		if (i < 0) // shift right?
		{
			i = -i;
			r = Trim(r);
			if (i >= LUA_NBITS) r = 0;
			else r >>= i;
		}
		else // shift left
		{
			if (i >= LUA_NBITS) r = 0;
			else r <<= i;
			r = Trim(r);
		}
		lua.PushUnsigned(r);
		return 1;
	}

	private static int B_LeftShift(ILuaState lua) => 
		B_Shift(lua, lua.CheckUnsigned(1), lua.CheckInteger(2));

	private static int B_RightShift(ILuaState lua) => 
		B_Shift(lua, lua.CheckUnsigned(1), -lua.CheckInteger(2));

	private static int B_ArithShift(ILuaState lua)
	{
		var r = lua.CheckUnsigned(1);
		var i = lua.CheckInteger(2);
		if (i < 0 || ((r & ((uint)1 << (LUA_NBITS-1))) == 0))
			return B_Shift( lua, r, -i );
		// Arithmetic shift for 'negative' number
		if (i>= LUA_NBITS)
			r = ALLONES;
		else
			r = Trim((r >> i) | ~(~(uint)0 >> i)); // Add signal bit
		lua.PushUnsigned(r);
		return 1;
	}

	private static uint AndAux(ILuaState lua)
	{
		var n = lua.GetTop();
		var r = ~(uint)0;
		for (var i = 1; i <= n; ++i) 
			r &= lua.CheckUnsigned(i);
		return Trim(r);
	}

	private static int B_And(ILuaState lua)
	{
		var r = AndAux(lua);
		lua.PushUnsigned(r);
		return 1;
	}

	private static int B_Not(ILuaState lua)
	{
		var r = ~lua.CheckUnsigned(1);
		lua.PushUnsigned(Trim(r));
		return 1;
	}

	private static int B_Or(ILuaState lua)
	{
		var n = lua.GetTop();
		uint r = 0;
		for (var i = 1; i <= n; ++i) 
			r |= lua.CheckUnsigned(i);
		lua.PushUnsigned(Trim(r));
		return 1;
	}

	private static int B_Xor( ILuaState lua )
	{
		var n = lua.GetTop();
		uint r = 0;
		for (var i = 1; i <= n; ++i) 
			r ^= lua.CheckUnsigned(i);
		lua.PushUnsigned(Trim(r));
		return 1;
	}

	private static int B_Test(ILuaState lua)
	{
		var r = AndAux(lua);
		lua.PushBoolean(r != 0);
		return 1;
	}

	private static int FieldArgs( ILuaState lua, int farg, out int width )
	{
		var f = lua.CheckInteger(farg);
		var w = lua.OptInt(farg + 1, 1);
		lua.ArgCheck(0 <= f, farg, "Field cannot be negative");
		lua.ArgCheck(0 < w, farg+1, "Width must be positive");
		if (f + w > LUA_NBITS)
			lua.Error("Trying to access non-existent bits");
		width = w;
		return f;
	}

	private static int B_Extract(ILuaState lua)
	{
		var r = lua.CheckUnsigned(1);
		var f = FieldArgs(lua, 2, out var w);
		r = (r >> f) & Mask(w);
		lua.PushUnsigned(r);
		return 1;
	}

	private static int B_Rotate(ILuaState lua, int i)
	{
		var r = lua.CheckUnsigned(1);
		i &= (LUA_NBITS-1); // i = i % NBITS
		r = Trim(r);
		r = (r << i) | (r >> (LUA_NBITS - i));
		lua.PushUnsigned(Trim(r));
		return 1;
	}

	private static int B_LeftRotate(ILuaState lua) => 
		B_Rotate(lua, lua.CheckInteger(2));

	private static int B_RightRotate(ILuaState lua) => 
		B_Rotate(lua, -lua.CheckInteger(2));

	private static int B_Replace(ILuaState lua)
	{
		var r = lua.CheckUnsigned(1);
		var v = lua.CheckUnsigned(2);
		var f = FieldArgs(lua, 3, out var w);
		var m = Mask(w);
		v &= m; // Erase bits outside given width
		r = (r & ~(m << f)) | (v << f);
		lua.PushUnsigned(r);
		return 1;
	}

}