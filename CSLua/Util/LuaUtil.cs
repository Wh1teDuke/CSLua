
#define API_CHECK
#define LUA_ASSERT

namespace CSLua.Util;

using DebugS = System.Diagnostics.Debug;
using NumberStyles = System.Globalization.NumberStyles;

public static class LuaUtil
{
	public static readonly CsClosure TracebackErrHandler = new (lua =>
	{
		lua.Traceback(lua, lua.ToString(-1), 1);
		return 1;
	});
	
	private static void Throw(params ReadOnlySpan<string?> msgs) => 
		throw new LuaException(string.Join("", msgs));

	public static void Assert(bool condition)
	{
#if LUA_ASSERT
		if (!condition) Throw("Assert failed!");
		DebugS.Assert(condition);
#endif
	}

	public static void Assert(bool condition, string message)
	{
#if LUA_ASSERT
		if (!condition)
			Throw("Assert failed! ", message);
		DebugS.Assert(condition, message);
#endif
	}

	public static void Assert(bool condition, string message, string detailMessage)
	{
#if LUA_ASSERT
		if (!condition)
			Throw("Assert failed! ", message, "\n", detailMessage);
		DebugS.Assert(condition, message, detailMessage);
#endif
	}

	public static void ApiCheck(bool condition, string message)
	{
#if LUA_ASSERT && API_CHECK
		Assert(condition, message);
#endif
	}

	public static void ApiCheckNumElems(LuaState lua, int n)
	{
#if LUA_ASSERT
		var n2 = (lua.TopIndex - lua.CI.FuncIndex);
		Assert(n < n2, $"Not enough elements in the stack ({n} < {n2})");
#endif
	}

	public static void InvalidIndex()
	{
#if LUA_ASSERT
		Assert(false, "invalid index");
#endif
	}
	
	public static bool Str2Decimal(ReadOnlySpan<char> s, out double result)
	{
		result = 0.0;

		if (s.Contains('n') || s.Contains('N')) // reject `inf' and `nan'
			return false;

		var pos = 0;
		if (s.Contains('x') || s.Contains('X'))
			result = StrX2Number(s, ref pos);
		else
			result = Str2Number(s, ref pos);

		if (pos == 0)
			return false; // nothing recognized

		while (pos < s.Length && char.IsWhiteSpace(s[pos])) ++pos;
		return pos == s.Length; // OK if no trailing characters
	}

	private static bool IsNegative(ReadOnlySpan<char> s, ref int pos)
	{
		if (pos >= s.Length) return false;

		var c = s[pos];
		if (c == '-')
		{
			++pos;
			return true;
		}

		if (c == '+') ++pos;
		return false;
	}

	private static bool IsXDigit(char c)
	{
		if (char.IsDigit(c))
			return true;

		return c switch
		{
			>= 'a' and <= 'f' or >= 'A' and <= 'F' => true,
			_ => false
		};
	}

	private static double ReadHexa(ReadOnlySpan<char> s, ref int pos, double r, out int count)
	{
		count = 0;
		while (pos < s.Length && IsXDigit(s[pos]))
		{
			r = (r * 16.0) + int.Parse(s.Slice(pos, 1), NumberStyles.HexNumber);
			++pos;
			++count;
		}
		return r;
	}

	private static double ReadDecimal(
		ReadOnlySpan<char> s, ref int pos, double r, out int count)
	{
		count = 0;
		while (pos < s.Length && char.IsDigit(s[pos]))
		{
			r = (r * 10.0) + char.GetNumericValue(s[pos]);
			++pos;
			++count;
		}
		return r;
	}

	// Following C99 specification for 'strtod'
	public static double StrX2Number(
		ReadOnlySpan<char> s, ref int curpos)
	{
		var pos = curpos;
		while (pos < s.Length && char.IsWhiteSpace(s[pos])) ++pos;
		var negative = IsNegative(s, ref pos);

		// check `0x'
		if (pos >= s.Length || 
		    !(s[pos] == '0' && (s[pos + 1] == 'x' || s[pos + 1] == 'X')))
			return 0.0;

		pos += 2; // skip `0x'

		var r = 0.0;
		var e = 0;
		r = ReadHexa(s, ref pos, r, out var i);
		if (pos < s.Length && s[pos] == '.')
		{
			++pos; // skip `.'
			r = ReadHexa(s, ref pos, r, out e);
		}
		if (i == 0 && e == 0)
			return 0.0; // invalid format (no digit)

		// each fractional digit divides value by 2^-4
		e *= -4;
		curpos = pos;

		// exponent part
		if (pos < s.Length && (s[pos] == 'p' || s[pos] == 'P'))
		{
			++pos; // skip `p'
			var expNegative = IsNegative(s, ref pos);
			if (pos >= s.Length || !char.IsDigit(s[pos]))
				goto ret;

			var exp1 = 0;
			while (pos < s.Length && char.IsDigit(s[pos]))
			{
				exp1 = exp1 * 10 + (int)char.GetNumericValue(s[pos]);
				++pos;
			}
			if (expNegative) exp1 = -exp1;
			e += exp1;
		}
		curpos = pos;

		ret:
		if (negative) r = -r;

		return r * Math.Pow(2.0, e);
	}

	public static double Str2Number(ReadOnlySpan<char> s, ref int curpos)
	{
		var pos = curpos;
		while (pos < s.Length && char.IsWhiteSpace(s[pos])) ++pos;
		var negative = IsNegative(s, ref pos);

		var r = 0.0;
		var f = 0;
		r = ReadDecimal(s, ref pos, r, out var i);
		if (pos < s.Length && s[pos] == '.')
		{
			++pos;
			r = ReadDecimal(s, ref pos, r, out f);
		}
		if (i == 0 && f == 0)
			return 0.0;

		f = -f;
		curpos = pos;

		// exponent part
		var e = 0.0;
		if (pos < s.Length && (s[pos] == 'e' || s[pos] == 'E'))
		{
			++pos;
			var expNegative = IsNegative(s, ref pos);
			if (pos >= s.Length || !char.IsDigit(s[pos]))
				goto ret;

			e = ReadDecimal(s, ref pos, e, out _);
			if (expNegative) e = -e;
			f += (int)e;
		}
		curpos = pos;

		ret:
		if (negative) 
			r = -r;

		return r * Math.Pow(10, f);
	}
	
	public static string Traceback(
		LuaState L, string? msg = null, int level = 0)
	{
		var result = L.DoTraceback(L, msg, level);
		L.Pop(1);
		return result;
	}
}