
#define LUA_COMPAT_UNPACK

namespace CSLua.Lib;

using StringBuilder = System.Text.StringBuilder;

internal static class LuaTableLib
{
	public const string LIB_NAME = "table";
	
	public static NameFuncPair NameFuncPair => new (LIB_NAME, OpenLib);

	public static int OpenLib(ILuaState lua)
	{
		Span<NameFuncPair> define =
		[
			new("concat", 	TBL_Concat),
			new("maxn", 	TBL_MaxN),
			new("insert", 	TBL_Insert),
			new("pack", 	TBL_Pack),
			new("unpack", 	TBL_Unpack),
			new("remove", 	TBL_Remove),
			new("sort", 	TBL_Sort),
			new("length",   TBL_Length),
		];

		lua.NewLib(define);

#if LUA_COMPAT_UNPACK
		// _G.unpack = table.unpack
		lua.GetField(-1, "unpack");
		lua.SetGlobal("unpack");
#endif

		return 1;
	}

	private static int TBL_Concat(ILuaState lua)
	{
		var sep = lua.OptString(2, "");
		lua.CheckType(1, LuaType.LUA_TTABLE);
		var i = lua.OptInt(3, 1);
		var last = lua.Opt(lua.CheckInteger, 4, ((ILuaAuxLib)lua).Len(1));

		var sb = new StringBuilder();
		for (; i < last; ++i)
		{
			lua.RawGetI(1, i);
			if (!lua.IsString(-1))
				lua.Error(
					"Invalid value ({0}) at index {1} in table for 'concat'",
					lua.TypeName(-1), i);
			sb.Append(lua.ToString(-1));
			sb.Append(sep);
			lua.Pop(1);
		}
		if (i == last) // Add last value (if interval was not empty)
		{
			lua.RawGetI(1, i);
			if (!lua.IsString(-1))
				lua.Error(
					"Invalid value ({0}) at index {1} in table for 'concat'",
					lua.TypeName(-1), i);
			sb.Append(lua.ToString(-1));
			lua.Pop(1);
		}

		lua.PushString(sb.ToString());
		return 1;
	}

	private static int TBL_MaxN(ILuaState lua)
	{
		var max = 0.0;
		lua.CheckType(1, LuaType.LUA_TTABLE);
		lua.PushNil(); // first key

		while(lua.Next(1))
		{
			lua.Pop(1); // remove value
			if (lua.Type(-1) == LuaType.LUA_TNUMBER)
			{
				var v = lua.ToNumber(-1);
				if (v > max) max = v;
			}
		}
		lua.PushNumber(max);
		return 1;
	}

	private static int AuxGetN(ILuaState lua, int n)
	{
		lua.CheckType(n, LuaType.LUA_TTABLE);
		return ((ILuaAuxLib)lua).Len(n);
	}

	private static int TBL_Insert(ILuaState lua)
	{
		var e = AuxGetN(lua, 1) + 1; // first empty element
		int pos; // where to insert new element
		switch (lua.GetTop())
		{
			case 2: // called with only 2 arguments
			{
				pos = e; // insert new element at the end
				break;
			}
			case 3:
			{
				pos = lua.CheckInteger(2); // 2nd argument is the position
				if (pos > e) e = pos; // 'grow' array if necessary
				for (var i = e; i > pos; --i) // move up elements
				{
					lua.RawGetI(1, i - 1);
					lua.RawSetI(1, i); // t[i] = t[i-1]
				}
				break;
			}
			default:
			{
				return lua.Error("Wrong number of arguments to 'insert'");
			}
		}
		lua.RawSetI(1, pos); // t[pos] = v
		return 0;
	}

	private static int TBL_Remove(ILuaState lua)
	{
		var e = AuxGetN(lua, 1);
		var pos = lua.OptInt(2, e);
		if (!(1 <= pos && pos <= e)) // Position is outside bounds?
			return 0; // Nothing to remove
		lua.RawGetI(1, pos); /* result = t[pos] */
		for (; pos < e; ++pos)
		{
			lua.RawGetI(1, pos + 1);
			lua.RawSetI(1, pos); // t[pos] = t[pos+1]
		}
		lua.PushNil();
		lua.RawSetI(1, e); // t[2] = nil
		return 1;
	}

	private static int TBL_Pack(ILuaState lua)
	{
		var n = lua.GetTop(); // number of elements to pack
		lua.CreateTable(n, 1); // create result table
		lua.PushInteger(n);
		lua.SetField(-2, "n"); // t.n = number of elements
		if (n <= 0) return 1; // return table
		// at least one element?
		lua.PushValue(1);
		lua.RawSetI(-2, 1); // insert first element
		lua.Replace(1); // move table into index 1
		for (var i= n; i >= 2; --i) // assign other elements
			lua.RawSetI(1, i);
		return 1; // return table
	}

	private static int TBL_Unpack(ILuaState lua)
	{
		lua.CheckType(1, LuaType.LUA_TTABLE);
		var i = lua.OptInt(2, 1);
		var e = lua.OptInt(3, ((ILuaAuxLib)lua).Len(1));
		if (i > e) return 0; // empty range
		var n = e - i + 1; // number of elements
		if (n <= 0 || !lua.CheckStack(n)) // n <= 0 means arith. overflow
			return lua.Error("too many results to unpack");
		lua.RawGetI(1, i); // push arg[i] (avoiding overflow problems
		while (i++ < e) // push arg[i + 1...e]
			lua.RawGetI(1, i);
		return n;
	}

	// quick sort ////////////////////////////////////////////////////////

	private static void Set2(ILuaState lua, int i, int j)
	{
		lua.RawSetI(1, i);
		lua.RawSetI(1, j);
	}

	private static bool SortComp(ILuaState lua, int a, int b)
	{
		if (!lua.IsNil(2)) // function?
		{
			lua.PushValue(2);
			lua.PushValue(a - 1); // -1 to compensate function
			lua.PushValue(b - 2); // -2 to compensate function add `a'
			lua.Call(2, 1);
			var res = lua.ToBoolean(-1);
			lua.Pop(1);
			return res;
		}

		// a < b?
		return lua.Compare(a, b, LuaEq.LUA_OPLT);
	}

	private static void AuxSort(ILuaState lua, int l, int u)
	{
		while (l < u) // For tail recursion
		{
			// sort elements a[l], a[(l+u)/2] and a[u]
			lua.RawGetI(1, l);
			lua.RawGetI(1, u);
			if (SortComp( lua, -1, -2)) // a[u] < a[l]?
				Set2(lua, l, u);
			else
				lua.Pop(2);
			if (u - l == 1) break; // only 2 elements
			var i = (l + u) / 2;
			lua.RawGetI(1, i);
			lua.RawGetI(1, l);
			if (SortComp(lua, -2, -1)) // a[i] < a[l]?
				Set2(lua, i, l);
			else
			{
				lua.Pop(1); // remove a[l]
				lua.RawGetI(1, u);
				if (SortComp(lua, -1, -2)) // a[u] < a[i]?
					Set2(lua, i, u);
				else
					lua.Pop(2);
			}
			if (u - l == 2) break; // only 3 arguments
			lua.RawGetI(1, i); // Pivot
			lua.PushValue(-1);
			lua.RawGetI(1, u - 1);
			Set2(lua, i, u - 1);
			/* a[l] <= P == a[u-1] <= a[u], only need to sort from l+1 to u-2 */
			i = l;
			var j = u - 1;
			for (;;) 
			{  /* invariant: a[l..i] <= P <= a[j..u] */
				/* repeat ++i until a[i] >= P */
				lua.RawGetI(1, ++i);
				while (SortComp(lua, -1, -2))
				{
					if (i >= u) lua.Error("invalid order function for sorting");
					lua.Pop(1);  /* remove a[i] */
					lua.RawGetI(1, ++i);
				}
				/* repeat --j until a[j] <= P */
				lua.RawGetI(1, --j);
				while (SortComp(lua, -3, -1))
				{
					if (j <= l) lua.Error("invalid order function for sorting");
					lua.Pop(1);  /* remove a[j] */
					lua.RawGetI(1, --j);
				}
				if (j < i) 
				{
					lua.Pop(3);  /* pop pivot, a[i], a[j] */
					break;
				}
				Set2(lua, i, j);
			}
			lua.RawGetI(1, u - 1);
			lua.RawGetI(1, i);
			Set2(lua, u - 1, i);  /* swap pivot (a[u-1]) with a[i] */
			/* a[l..i-1] <= a[i] == P <= a[i+1..u] */
			/* adjust so that smaller half is in [j..i] and larger one in [l..u] */
			if (i - l < u - i) 
			{
				j = l; i -= 1; l = i + 2;
			}
			else 
			{
				j = i + 1; i = u; u = j - 2;
			}
			AuxSort(lua, j, i);  /* call recursively the smaller one */
		}  /* repeat the routine for the larger one */
	}

	private static int TBL_Sort(ILuaState lua)
	{
		var n = AuxGetN(lua, 1);
		lua.CheckStack(40, "");  /* assume array is smaller than 2^40 */
		if (!lua.IsNoneOrNil(2))  /* is there a 2nd argument? */
			lua.CheckType(2, LuaType.LUA_TFUNCTION);
		lua.SetTop(2);  /* make sure there is two arguments */
		AuxSort(lua, 1, n);
		return 0;
	}

	private static int TBL_Length(ILuaState lua)
	{
		lua.CheckType(1, LuaType.LUA_TTABLE);
		lua.PushNil();
		var count = 0;
		while (lua.Next(1))
		{
			lua.Pop(1);
			count++;
		}
		lua.PushInteger(count);
		return 1;
	}
}