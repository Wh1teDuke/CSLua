
// ReSharper disable InconsistentNaming

namespace CSLua.Lib;

using Math = Math;
using Random = Random;
using BitConverter = BitConverter;

public static class LuaMathLib
{
	public const string LIB_NAME = "math";

	private const double RADIANS_PER_DEGREE = Math.PI / 180.0;

	private static readonly ThreadLocal<Random> _randObj = 
		new (() => new Random());
	
	public static NameFuncPair NameFuncPair => new (LIB_NAME, OpenLib);

	public static int OpenLib(ILuaState lua)
	{
		Span<NameFuncPair> define =
		[
			new("abs",   		Math_Abs),
			new("acos",  		Math_Acos),
			new("asin",  		Math_Asin),
			new("atan2", 		Math_Atan2),
			new("atan",  		Math_Atan),
			new("ceil",  		Math_Ceil),
			new("cosh",  		Math_Cosh),
			new("cos",   		Math_Cos),
			new("deg",   		Math_Deg),
			new("exp",   		Math_Exp),
			new("floor", 		Math_Floor),
			new("fmod",  		Math_Fmod),
			new("frexp", 		Math_Frexp),
			new("ldexp", 		Math_Ldexp),
			new("log10", 		Math_Log10),
			new("log",   		Math_Log),
			new("max",   		Math_Max),
			new("min",   		Math_Min),
			new("modf",  		Math_Modf),
			new("pow",   		Math_Pow),
			new("rad",   		Math_Rad),
			new("random",		Math_Random),
			new("randomseed",	Math_RandomSeed),
			new("sinh", 		Math_Sinh),
			new("sin",   		Math_Sin),
			new("sqrt",  		Math_Sqrt),
			new("tanh",   		Math_Tanh),
			new("tan",   		Math_Tan),
			new("tointeger",    Math_ToInt),
			new("type",         Math_Type),
		];

		lua.NewLib(define);

		SetField(lua, "pi", Math.PI);
		SetField(lua, "huge", double.MaxValue);
		SetField(lua, "maxinteger", long.MaxValue);
		SetField(lua, "mininteger", long.MinValue);

		return 1;
	}

	private static void SetField(ILuaState L, string key, double value)
	{
		L.PushNumber(value);
		L.SetField(-2, key);
	}
	
	private static void SetField(ILuaState L, string key, long value)
	{
		L.PushInt64(value);
		L.SetField(-2, key);
	}

	private static int Math_Abs(ILuaState lua)
	{
		lua.PushNumber(Math.Abs(lua.CheckNumber(1)));
		return 1;
	}

	private static int Math_Acos(ILuaState lua)
	{
		lua.PushNumber(Math.Acos(lua.CheckNumber(1)));
		return 1;
	}

	private static int Math_Asin(ILuaState lua)
	{
		lua.PushNumber(Math.Asin(lua.CheckNumber(1)));
		return 1;
	}

	private static int Math_Atan2(ILuaState lua)
	{
		lua.PushNumber(Math.Atan2(lua.CheckNumber(1),
			lua.CheckNumber(2)));
		return 1;
	}

	private static int Math_Atan(ILuaState lua)
	{
		lua.PushNumber(Math.Atan(lua.CheckNumber(1)));
		return 1;
	}

	private static int Math_Ceil(ILuaState lua)
	{
		lua.PushNumber(Math.Ceiling(lua.CheckNumber(1)));
		return 1;
	}

	private static int Math_Cosh(ILuaState lua)
	{
		lua.PushNumber(Math.Cosh(lua.CheckNumber(1)));
		return 1;
	}

	private static int Math_Cos(ILuaState lua)
	{
		lua.PushNumber(Math.Cos(lua.CheckNumber(1)));
		return 1;
	}

	private static int Math_Deg(ILuaState lua)
	{
		lua.PushNumber(lua.CheckNumber(1) / RADIANS_PER_DEGREE);
		return 1;
	}

	private static int Math_Exp(ILuaState lua)
	{
		lua.PushNumber(Math.Exp(lua.CheckNumber(1)));
		return 1;
	}

	private static int Math_Floor(ILuaState lua)
	{
		lua.PushNumber(Math.Floor(lua.CheckNumber(1)));
		return 1;
	}

	private static int Math_Fmod(ILuaState lua)
	{
		lua.PushNumber(Math.IEEERemainder(
			lua.CheckNumber(1),
			lua.CheckNumber(2)));
		return 1;
	}

	private static int Math_Frexp(ILuaState lua)
	{
		var d = lua.CheckNumber(1);

		// Translate the double into sign, exponent and mantissa.
		var bits = BitConverter.DoubleToInt64Bits(d);
		// Note that the shift is sign-extended, hence the test against -1 not 1
		var negative = (bits < 0);
		var exponent = (int) ((bits >> 52) & 0x7ffL);
		var mantissa = bits & 0xfffffffffffffL;

		// Subnormal numbers; exponent is effectively one higher,
		// but there's no extra normalisation bit in the mantissa
		if (exponent == 0)
		{
			exponent++;
		}
		// Normal numbers; leave exponent as it is but add extra
		// bit to the front of the mantissa
		else
		{
			mantissa |= (1L << 52);
		}

		// Bias the exponent. It's actually biased by 1023, but we're
		// treating the mantissa as m.0 rather than 0.m, so we need
		// to subtract another 52 from it.
		exponent -= 1075;

		if (mantissa == 0) 
		{
			lua.PushNumber(0.0);
			lua.PushNumber(0.0);
			return 2;
		}

		/* Normalize */
		while ((mantissa & 1) == 0) 
		{    /*  i.e., Mantissa is even */
			mantissa >>= 1;
			exponent++;
		}

		double m = mantissa;
		double e = exponent;
		while (m >= 1)
		{
			m /= 2.0;
			e += 1.0;
		}

		if (negative) m = -m;
		lua.PushNumber(m);
		lua.PushNumber(e);
		return 2;
	}

	private static int Math_Ldexp(ILuaState lua)
	{
		lua.PushNumber(lua.CheckNumber(1) * Math.Pow(2, lua.CheckNumber(2)));
		return 1;
	}

	private static int Math_Log10(ILuaState lua)
	{
		lua.PushNumber(Math.Log10(lua.CheckNumber(1)));
		return 1;
	}

	private static int Math_Log(ILuaState lua)
	{
		var x = lua.CheckNumber(1);
		double res;
		if (lua.IsNoneOrNil(2))
			res = Math.Log(x);
		else
		{
			var logBase = (int)lua.CheckNumber(2);
			res = logBase == 10 ? Math.Log10(x) : Math.Log(x, logBase);
		}
		lua.PushNumber(res);
		return 1;
	}

	private static int Math_Max(ILuaState lua)
	{
		var n = lua.GetTop();
		var dmax = lua.CheckNumber(1);
		for (var i = 2; i <= n; ++i)
		{
			var d = lua.CheckNumber(i);
			if (d > dmax) dmax = d;
		}
		lua.PushNumber(dmax);
		return 1;
	}

	private static int Math_Min(ILuaState lua)
	{
		var n = lua.GetTop();
		var dmin = lua.CheckNumber(1);
		for (var i = 2; i <= n; ++i)
		{
			var d = lua.CheckNumber(i);
			if (d < dmin) dmin = d;
		}
		lua.PushNumber(dmin);
		return 1;
	}

	private static int Math_Modf(ILuaState lua)
	{
		var d = lua.CheckNumber(1);
		var c = Math.Floor(d);
		lua.PushNumber(c);
		lua.PushNumber(d - c);
		return 2;
	}

	private static int Math_Pow(ILuaState lua)
	{
		lua.PushNumber(
			Math.Pow(lua.CheckNumber(1),
			lua.CheckNumber(2)));
		return 1;
	}

	private static int Math_Rad(ILuaState lua)
	{
		lua.PushNumber(lua.CheckNumber(1) * RADIANS_PER_DEGREE);
		return 1;
	}

	private static int Math_Random(ILuaState lua)
	{
		var r = _randObj.Value!.NextDouble();
		switch (lua.GetTop())
		{
			case 0: // no argument
				lua.PushNumber(r);
				break;
			case 1:
			{
				var u = lua.CheckNumber(1);
				lua.ArgCheck(1.0 <= u, 1, "interval is empty");
				lua.PushNumber(Math.Floor(r * u) + 1.0); // int in [1, u]
				break;
			}
			case 2:
			{
				var l = lua.CheckNumber(1);
				var u = lua.CheckNumber(2);
				lua.ArgCheck(l <= u, 2, "Interval is empty");
				lua.PushNumber(Math.Floor(r * (u - l + 1)) + l); // int in [l, u]
				break;
			}
			default: return lua.Error("Wrong number of arguments");
		}
		return 1;
	}

	private static int Math_RandomSeed(ILuaState lua)
	{
		_randObj.Value = new Random((int)lua.CheckUnsigned(1));
		_randObj.Value!.Next();
		return 0;
	}

	private static int Math_Sinh(ILuaState lua)
	{
		lua.PushNumber(Math.Sinh(lua.CheckNumber(1)));
		return 1;
	}

	private static int Math_Sin(ILuaState lua)
	{
		lua.PushNumber(Math.Sin(lua.CheckNumber(1)));
		return 1;
	}

	private static int Math_Sqrt(ILuaState lua)
	{
		lua.PushNumber(Math.Sqrt(lua.CheckNumber(1)));
		return 1;
	}

	private static int Math_Tanh(ILuaState lua)
	{
		lua.PushNumber(Math.Tanh(lua.CheckNumber(1)));
		return 1;
	}

	private static int Math_Tan(ILuaState lua)
	{
		lua.PushNumber(Math.Tan(lua.CheckNumber(1)));
		return 1;
	}

	private static int Math_ToInt(ILuaState lua)
	{
		var n = lua.ToIntegerX(1, out var valid);
		if (valid) 
			lua.PushInt64(n);
		else
		{
			lua.CheckAny(1);
			lua.PushNil();
		}
		return 1;
	}

	private static int Math_Type(ILuaState lua)
	{
		var t = lua.Type(1);
		if (t == LuaType.LUA_TINT64) lua.PushString("integer");
		if (t == LuaType.LUA_TNUMBER) lua.PushString("float");
		lua.CheckAny(1);
		lua.PushNil();
		return 1;
	}
}