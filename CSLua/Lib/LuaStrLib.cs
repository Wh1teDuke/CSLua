
using CSLua.Utils;
// ReSharper disable InconsistentNaming

namespace CSLua.Lib;

using StringBuilder = System.Text.StringBuilder;
using Convert = Convert;

public static class LuaStrLib
{
	public const string LIB_NAME = "string";

	private const int CAP_UNFINISHED 	= -1;
	private const int CAP_POSITION		= -2;
	private const int LUA_MAXCAPTURES 	= 32;
	private const char L_ESC 			= '%';
	private const string FLAGS			= "-+ #0";
	private static readonly char[] SPECIALS;

	static LuaStrLib() => SPECIALS = "^$*+?.([%-".ToCharArray();
	
	public static NameFuncPair NameFuncPair => new (LIB_NAME, OpenLib);
	public static NameFuncPair SafeNameFuncPair => new (LIB_NAME, OpenSafeLib);

	public static int OpenLib(ILuaState lua)
	{
		Span<NameFuncPair> define = 
		[
			new("byte", 	Str_Byte),
			new("char", 	Str_Char),
			new("dump", 	Str_Dump),
			new("find", 	Str_Find),
			new("format", 	Str_Format),
			new("gmatch", 	Str_Gmatch),
			new("gsub", 	Str_Gsub),
			new("len", 		Str_Len),
			new("lower", 	Str_Lower),
			new("match", 	Str_Match),
			new("rep", 		Str_Rep),
			new("reverse", 	Str_Reverse),
			new("sub", 		Str_Sub),
			new("upper", 	Str_Upper),
		];

		lua.L_NewLib(define);
		CreateMetaTable(lua);

		return 1;
	}
	
	public static int OpenSafeLib(ILuaState lua)
	{
		Span<NameFuncPair> define = 
		[
			new("byte", 	Str_Byte),
			new("char", 	Str_Char),
			//new("dump", 	Str_Dump),
			new("find", 	Str_Find),
			new("format", 	Str_Format),
			new("gmatch", 	Str_Gmatch),
			new("gsub", 	Str_Gsub),
			new("len", 		Str_Len),
			new("lower", 	Str_Lower),
			new("match", 	Str_Match),
			new("rep", 		Str_Rep),
			new("reverse", 	Str_Reverse),
			new("sub", 		Str_Sub),
			new("upper", 	Str_Upper),
		];

		lua.L_NewLib(define);
		CreateMetaTable(lua);

		return 1;
	}

	private static void CreateMetaTable(ILuaState lua)
	{
		lua.CreateTable(0, 1); // table to be metatable for strings
		lua.PushString("" ); // dummy string
		lua.PushValue(-2); // copy table
		lua.SetMetaTable(-2); // set table as metatable for strings
		lua.Pop(1);
		lua.PushValue(-2); // get string library
		lua.SetField(-2, "__index"); // metatable.__index = string
		lua.Pop(1); // pop metatable
	}

	private static int PosRelative(int pos, int len)
	{
		if (pos >= 0) return pos;
		if (0 - pos > len) return 0;
		return len - (-pos) + 1;
	}

	private static int Str_Byte(ILuaState lua)
	{
		var s = lua.L_CheckString(1);
		var posi = PosRelative(lua.L_OptInt(2, 1), s.Length);
		var pose = PosRelative(lua.L_OptInt(3, posi), s.Length);
		if (posi < 1) posi = 1;
		if (pose > s.Length) pose = s.Length;
		if (posi > pose) return 0; // empty interval; return no values
		var n = pose - posi + 1;
		if (posi + n <= pose) // overflow?
			return lua.L_Error("String slice too long");
		lua.L_CheckStack(n, "String slice too long");
		for (var i = 0; i < n; ++i)
			lua.PushInteger((byte)s[posi + i - 1]);
		return n;
	}

	private static int Str_Char(ILuaState lua)
	{
		var n = lua.GetTop();
		var sb = new StringBuilder();
		for (var i = 1; i <= n; ++i)
		{
			var c = lua.L_CheckInteger(i);
			lua.L_ArgCheck((char)c == c, i, "value out of range");
			sb.Append((char)c);
		}
		lua.PushString(sb.ToString());
		return 1;
	}

	private static int Str_Dump(ILuaState lua)
	{
		lua.L_CheckType(1, LuaType.LUA_TFUNCTION);
		lua.SetTop(1);
		var bsb = new ByteStringBuilder();

		if (lua.Dump(WriteFunc) != DumpStatus.OK)
			return lua.L_Error( "Unable to dump given function" );
		lua.PushString(bsb.ToString());
		return 1;

		DumpStatus WriteFunc(byte[] bytes, int start, int length)
		{
			bsb.Append(bytes, start, length);
			return DumpStatus.OK;
		}
	}

	private record CaptureInfo
	{
		public int Len;
		public int Init;
	}

	private sealed class MatchState
	{
		public readonly ILuaState	Lua;
		public readonly string		Src;
		public readonly string		Pattern;

		public int				Level;
		public int				SrcInit;
		public int				SrcEnd;
		public int				PatternEnd;
		public readonly CaptureInfo[] Capture;

		public MatchState(ILuaState lua, string src, string pattern)
		{
			Lua = lua;
			Src = src;
			Pattern = pattern;
			Capture = new CaptureInfo[LUA_MAXCAPTURES];
			for (var i = 0; i < LUA_MAXCAPTURES; ++i)
				Capture[i] = new CaptureInfo();
		}
	}

	private static int ClassEnd(MatchState ms, int p)
	{
		var lua = ms.Lua;
		switch (ms.Pattern[p++])
		{
			case L_ESC:
			{
				if (p == ms.PatternEnd)
					lua.L_Error( "malformed pattern (ends with '%')" );
				return p+1;
			}
			case '[':
			{
				if (ms.Pattern[p] == '^') p++;
				do {
					if (p == ms.PatternEnd)
						lua.L_Error( "malformed pattern (missing ']')" );
					if (ms.Pattern[p++] == L_ESC && p < ms.PatternEnd)
						p++; // skip escapes (e.g. `%]')
				} while (ms.Pattern[p] != ']');
				return p + 1;
			}
			default: return p;
		}
	}

	private static bool IsXDigit(char c)
	{
		return c switch
		{
			'0' or '1' or '2' or '3' or '4' or '5' or '6' or '7' or '8' or '9'
				or 'a' or 'b' or 'c' or 'd' or 'e' or 'f'
				or 'A' or 'B' or 'C' or 'D' or 'E' or 'F' => true,
			_ => false
		};
	}

	private static bool MatchClass(char c, char cl)
	{
		bool res;
		switch(cl)
		{
			case 'a': res = char.IsLetter(c); break;
			case 'c': res = char.IsControl(c); break;
			case 'd': res = char.IsDigit(c); break;
			case 'g': throw new NotImplementedException();
			case 'l': res = char.IsLower(c); break;
			case 'p': res = char.IsPunctuation(c); break;
			case 's': res = char.IsWhiteSpace(c); break;
			case 'u': res = char.IsUpper(c); break;
			case 'w': res = char.IsLetterOrDigit(c); break;
			case 'x': res = IsXDigit(c); break;
			case 'z': res = (c == '\0'); break;  /* deprecated option */
			default: return (cl == c);
		}
		return res;
	}

	private static bool MatchBreaketClass( MatchState ms, char c, int p, int ec )
	{
		var sig = true;
		if (ms.Pattern[p + 1] == '^')
		{
			sig = false;
			p++; // skip the `^'
		}
		while (++p < ec)
		{
			if (ms.Pattern[p] == L_ESC)
			{
				p++;
				if (MatchClass(c, ms.Pattern[p]))
					return sig;
			}
			else if (ms.Pattern[p + 1] == '-' && (p + 2 < ec))
			{
				p += 2;
				if (ms.Pattern[p - 2] <= c && c <= ms.Pattern[p])
					return sig;
			}
			else if (ms.Pattern[p] == c) return sig;
		}
		return !sig;
	}

	private static bool SingleMatch(MatchState ms, char c, int p, int ep)
	{
		return ms.Pattern[p] switch
		{
			'.' => true // matches any char
			,
			L_ESC => MatchClass(c, ms.Pattern[p + 1]),
			'[' => MatchBreaketClass(ms, c, p, ep - 1),
			_ => ms.Pattern[p] == c
		};
	}

	private static int MatchBalance(MatchState ms, int s, int p)
	{
		var lua = ms.Lua;
		if (p >= ms.PatternEnd - 1)
			lua.L_Error("malformed pattern (missing arguments to '%b')");
		if (ms.Src[s] != ms.Pattern[p]) return -1;
		var b = ms.Pattern[p];
		var e = ms.Pattern[p + 1];
		var count = 1;
		while (++s < ms.SrcEnd)
		{
			if (ms.Src[s] == e)
			{
				if (--count == 0) return s + 1;
			}
			else if (ms.Src[s] == b) count++;
		}
		return -1; //string ends out of balance
	}

	private static int MaxExpand( MatchState ms, int s, int p, int ep )
	{
		var i = 0; // counts maximum expand for item
		while( (s+i) < ms.SrcEnd && SingleMatch( ms, ms.Src[s+i], p, ep ) )
			i++;
		// keeps trying to match with the maximum repetitions
		while( i >= 0 )
		{
			var res = Match( ms, (s+i), (ep+1) );
			if( res >= 0 ) return res;
			i--; // else didn't match; reduce 1 repetition to try again
		}
		return -1;
	}

	private static int MinExpand( MatchState ms, int s, int p, int ep )
	{
		for(;;)
		{
			var res = Match( ms, s, ep+1 );
			if( res >= 0 )
				return res;
			if( s < ms.SrcEnd && SingleMatch( ms, ms.Src[s], p, ep ) )
				s++; // try with one more repetition
			else return -1;
		}
	}

	private static int CaptureToClose( MatchState ms )
	{
		var lua = ms.Lua;
		var level=ms.Level;
		for( level--; level>=0; level-- )
		{
			if( ms.Capture[level].Len == CAP_UNFINISHED )
				return level;
		}
		return lua.L_Error( "invalid pattern capture" );
	}

	private static int StartCapture( MatchState ms, int s, int p, int what )
	{
		var lua = ms.Lua;
		var level = ms.Level;
		if( level >= LUA_MAXCAPTURES )
			lua.L_Error( "too many captures" );
		ms.Capture[level].Init = s;
		ms.Capture[level].Len = what;
		ms.Level = level + 1;
		var res = Match( ms, s, p );
		if( res == -1 ) // match failed?
			ms.Level--;
		return res;
	}

	private static int EndCapture( MatchState ms, int s, int p )
	{
		var l = CaptureToClose( ms );
		ms.Capture[l].Len = s - ms.Capture[l].Init; // close capture
		var res = Match( ms, s, p );
		if( res == -1 ) // match failed?
			ms.Capture[l].Len = CAP_UNFINISHED; // undo capture
		return res;
	}

	private static int CheckCapture( MatchState ms, char l )
	{
		var lua = ms.Lua;
		var i = l - '1';
		if( i < 0 || i >= ms.Level || ms.Capture[i].Len == CAP_UNFINISHED )
			return lua.L_Error( "invalid capture index %d", i+1 );
		return i;
	}

	private static int MatchCapture( MatchState ms, int s, char l )
	{
		var i = CheckCapture( ms, l );
		var len = ms.Capture[i].Len;
		if( ms.SrcEnd - s >= len &&
		    string.Compare(ms.Src, ms.Capture[i].Init, ms.Src, s, len) == 0 )
			return s + len;
		return -1;
	}

	private static int Match(MatchState ms, int s, int p)
	{
		var lua = ms.Lua;
		init: // using goto's to optimize tail recursion
		if (p == ms.PatternEnd)
			return s;
		switch (ms.Pattern[p])
		{
			case '(': // start capture
			{
				if (ms.Pattern[p + 1] == ')') // position capture?
					return StartCapture(ms, s, p + 2, CAP_POSITION);
				return StartCapture(ms, s, p + 1, CAP_UNFINISHED);
			}
			case ')': // end capture
			{
				return EndCapture(ms, s, p + 1);
			}
			case '$':
			{
				if (p + 1 == ms.PatternEnd) // is the `$' the last char in pattern?
					return (s == ms.SrcEnd) ? s : -1; // check end of string
				goto default;
			}
			case L_ESC: // escaped sequences not in the format class[*+?-]?
			{
				switch( ms.Pattern[p+1] )
				{
					case 'b': // balanced string?
					{
						s = MatchBalance( ms, s, p+2 );
						if( s == -1 ) return -1;
						p += 4; goto init; // else return match(ms, s, p+4);
					}
					case 'f': // frontier?
					{
						p += 2;
						if( ms.Pattern[p] != '[' )
							lua.L_Error( "missing '[' after '%f' in pattern" );
						var ep = ClassEnd( ms, p ); //points to what is next
						var previous = (s == ms.SrcInit) ? '\0' : ms.Src[s-1];
						if( MatchBreaketClass(ms, previous, p, ep-1) ||
						    !MatchBreaketClass(ms, ms.Src[s], p, ep-1) ) return -1;
						p = ep; goto init; // else return match( ms, s, ep );
					}
					case '0':
					case '1':
					case '2':
					case '3':
					case '4':
					case '5':
					case '6':
					case '7':
					case '8':
					case '9': // capture results (%0-%9)?
					{
						s = MatchCapture( ms, s, ms.Pattern[p+1] );
						if( s == -1 ) return -1;
						p+=2; goto init; // else return match(ms, s, p+2);
					}
					default: goto dflt;
				}
			}
			default: dflt: // pattern class plus optional suffix
			{
				var ep = ClassEnd( ms, p );
				var m = s < ms.SrcEnd && SingleMatch(ms, ms.Src[s], p, ep);
				if(ep < ms.PatternEnd){
					switch(ms.Pattern[ep]) //fix gmatch bug patten is [^a]
					{
						case '?': // optional
						{
							if( m )
							{
								var res = Match(ms, s+1, ep+1);
								if( res != -1 )
									return res;
							}
							p=ep+1; goto init; // else return match(ms, s, ep+1);
						}
						case '*': // 0 or more repetitions
						{
							return MaxExpand(ms, s, p, ep);
						}
						case '+': // 1 or more repetitions
						{
							return (m ? MaxExpand(ms, s+1, p, ep) : -1);
						}
						case '-': // 0 or more repetitions (minimum)
						{
							return MinExpand(ms, s, p, ep);
						}
					}
				}
				if(!m) return -1;
				s++; p=ep; goto init; // else return match(ms, s+1, ep);
			}
		}
	}

	private static void PushOneCapture
	( MatchState ms
		, int i
		, int start
		, int end
	)
	{
		var lua = ms.Lua;
		if( i >= ms.Level )
		{
			if( i == 0 ) // ms.Level == 0, too
				lua.PushString( ms.Src.Substring( start, end-start ) );
			else
				lua.L_Error( "invalid capture index" );
		}
		else
		{
			var l = ms.Capture[i].Len;
			if( l == CAP_UNFINISHED )
				lua.L_Error( "unfinished capture" );
			if( l == CAP_POSITION )
				lua.PushInteger( ms.Capture[i].Init - ms.SrcInit + 1 );
			else
				lua.PushString( ms.Src.Substring( ms.Capture[i].Init, l ) );
		}
	}

	private static int PushCaptures(ILuaState lua, MatchState ms, int spos, int epos )
	{
		var nLevels = (ms.Level == 0 && spos >= 0) ? 1 : ms.Level;
		lua.L_CheckStack(nLevels, "too many captures");
		for( var i=0; i<nLevels; ++i )
			PushOneCapture( ms, i, spos, epos );
		return nLevels; // number of strings pushed
	}

	private static bool NoSpecials(string pattern) => 
		pattern.IndexOfAny(SPECIALS) == -1;

	private static int StrFindAux(ILuaState lua, bool find)
	{
		var s = lua.L_CheckString(1);
		var p = lua.L_CheckString(2);
		var init = PosRelative(lua.L_OptInt(3, 1), s.Length);
		if (init < 1) init = 1;
		else if (init > s.Length + 1) // start after string's end?
		{
			lua.PushNil(); // cannot find anything
			return 1;
		}
		// explicit request or no special characters?
		if (find && (lua.ToBoolean(4) || NoSpecials(p)))
		{
			// do a plain search
			var pos = s.IndexOf(p, init - 1, StringComparison.Ordinal);
			if (pos >= 0)
			{
				lua.PushInteger(pos + 1);
				lua.PushInteger(pos + p.Length);
				return 2;
			}
		}
		else
		{
			var s1 = init-1;
			var ppos = 0;
			var anchor = p[ppos] == '^';
			if (anchor)
				ppos++; // skip anchor character

			var ms = new MatchState(lua, s, p)
			{
				SrcInit = s1,
				SrcEnd = s.Length,
				PatternEnd = p.Length
			};

			do
			{
				ms.Level = 0;
				var res = Match(ms, s1, ppos);
				if (res != -1)
				{
					if (find)
					{
						lua.PushInteger(s1 + 1); // start
						lua.PushInteger(res);  // end
						return PushCaptures(lua, ms, -1, 0) + 2;
					}

					return PushCaptures(lua, ms, s1, res);
				}
			} while (s1++ < ms.SrcEnd && !anchor);
		}
		lua.PushNil(); // not found
		return 1;
	}

	private static int Str_Find(ILuaState lua) => StrFindAux(lua, true);

	private static int ScanFormat(ILuaState lua, string format, int s)
	{
		var p = s;
		// skip flags
		while (p < format.Length && format[p] != '\0' && FLAGS.Contains(format[p]))
			p++;
		if (p - s > FLAGS.Length)
			lua.L_Error("invalid format (repeat flags)");
		if (char.IsDigit(format[p])) p++; // Skip width
		if (char.IsDigit(format[p])) p++; // (2 digits at most)
		if (format[p] == '.' )
		{
			p++;
			if (char.IsDigit(format[p])) p++; // Skip precision
			if (char.IsDigit(format[p])) p++; // (2 digits at most)
		}
		if (char.IsDigit(format[p]))
			lua.L_Error("invalid format (width of precision too long)");
		return p;
	}

	private static int Str_Format(ILuaState lua)
	{
		var top = lua.GetTop();
		var sb = new StringBuilder();
		var arg = 1;
		var format = lua.L_CheckString(arg);
		var s = 0;
		var e = format.Length;
		while (s < e)
		{
			if (format[s] != L_ESC)
			{
				sb.Append(format[s++]);
				continue;
			}

			if (format[++s] == L_ESC)
			{
				sb.Append(format[s++]);
				continue;
			}

			// else format item
			if (++arg > top)
				lua.L_ArgError(arg, "no value");

			s = ScanFormat(lua, format, s);
			switch (format[s++]) // TODO: properly handle form
			{
				case 'c':
				{
					sb.Append((char)lua.L_CheckInteger(arg));
					break;
				}
				case 'd': case 'i':
				{
					var n = lua.L_CheckInteger(arg);
					sb.Append(n);
					break;
				}
				case 'u':
				{
					var n = lua.L_CheckInteger(arg);
					lua.L_ArgCheck(n >= 0, arg,
						"not a non-negative number is proper range");
					sb.Append(n);
					break;
				}
				case 'o':
				{
					var n = lua.L_CheckInteger(arg);
					lua.L_ArgCheck(n >= 0, arg,
						"not a non-negative number is proper range");
					sb.Append(Convert.ToString(n, 8));
					break;
				}
				case 'x':
				{
					var n = lua.L_CheckInteger(arg);
					lua.L_ArgCheck(n >= 0, arg,
						"not a non-negative number is proper range" );
					// sb.Append( string.Format("{0:x}", n) );
					sb.Append($"{n:x}");
					break;
				}
				case 'X':
				{
					var n = lua.L_CheckInteger(arg);
					lua.L_ArgCheck(n >= 0, arg,
						"not a non-negative number is proper range");
					// sb.Append( string.Format("{0:X}", n) );
					sb.Append($"{n:X}");
					break;
				}
				case 'e':  case 'E':
				{
					sb.Append($"{lua.L_CheckNumber(arg):E}");
					break;
				}
				case 'f':
				{
					sb.Append($"{lua.L_CheckNumber(arg):F}");
					break;
				}
#if LUA_USE_AFORMAT
				case 'a': case 'A':
#endif
				case 'g': case 'G':
				{
					sb.Append($"{lua.L_CheckNumber(arg):G}");
					break;
				}
				case 'q':
				{
					AddQuoted(lua, sb, arg);
					break;
				}
				case 's':
				{
					sb.Append(lua.L_ToString(arg));
					break;
				}
				default: // also treat cases `pnLlh'
				{
					return lua.L_Error("invalid option '{0}' to 'format'",
						format[s - 1]);
				}
			}
		}
		lua.PushString(sb.ToString());
		return 1;
	}

	private static void AddQuoted(ILuaState lua, StringBuilder sb, int arg)
	{
		var s = lua.L_CheckString(arg);
		sb.Append('"');
		for(var i = 0; i < s.Length; ++i) 
		{
			var c = s[i];
			if (c is '"' or '\\' or '\n')
				sb.Append('\\').Append(c);
			else if(c == '\0' || char.IsControl(c)) 
			{
				if (i + 1 >= s.Length || !char.IsDigit(s[i + 1]))
					sb.Append($"\\{(int)c:D}");
				else
					sb.Append($"\\{(int)c:D3}");
			}
			else
				sb.Append(c);
		}
		sb.Append('"');
	}

	private static int GmatchAux(ILuaState lua)
	{
		var src = lua.ToString(lua.UpValueIndex(1));
		var pattern = lua.ToString(lua.UpValueIndex(2));

		var ms = new MatchState(lua, src, pattern)
		{
			SrcInit = 0,
			SrcEnd = src.Length,
			PatternEnd = pattern.Length
		};

		for (var s = lua.ToInteger(lua.UpValueIndex(3))
		     ; s <= ms.SrcEnd
		     ; s++)
		{
			ms.Level = 0;
			var e = Match(ms, s, 0);
			if (e == -1) continue;

			var newStart = e == 0 ? 1: e;
			lua.PushInteger(newStart);
			lua.Replace(lua.UpValueIndex(3));
			return PushCaptures(lua, ms, s, e);
		}
		return 0; // not found
	}

	private static int Str_Gmatch(ILuaState lua)
	{
		lua.L_CheckString(1);
		lua.L_CheckString(2);
		lua.SetTop(2);
		lua.PushInteger(0);
		lua.PushCSharpClosure(GmatchAux, 3);
		return 1;
	}
		
	private static void Add_S(MatchState ms, StringBuilder b, int s, int e) 
	{
		var news = ms.Lua.ToString(3);
		for (var i = 0; i < news.Length; i++) 
		{
			if (news[i] != L_ESC)
				b.Append(news[i]);
			else 
			{
				i++;  /* skip ESC */
				if (!char.IsDigit(news[i]))
					b.Append(news[i]);
				else if (news[i] == '0')
					b.Append(ms.Src.AsSpan(s, (e - s))); 
				else 
				{
					PushOneCapture(ms, news[i] - '1', s, e);
					b.Append(ms.Lua.ToString(-1));  /* add capture to accumulated result */
				}
			}
		}
	}
		
	private static void Add_Value(MatchState ms, StringBuilder b, int s, int e) 
	{
		var lua = ms.Lua;
		// ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
		switch (lua.Type(3)) {
			case LuaType.LUA_TNUMBER:
			case LuaType.LUA_TSTRING: {
				Add_S(ms, b, s, e);
				return;
			}
			case LuaType.LUA_TFUNCTION: {
				lua.PushValue(3);
				var n = PushCaptures(lua, ms, s, e);
				lua.Call(n, 1);
				break;
			}
			case LuaType.LUA_TTABLE: {
				PushOneCapture(ms, 0, s, e);
				lua.GetTable(3);
				break;
			}
			default: break;
		}
		if (lua.ToBoolean(-1)==false) {  /* nil or false? */
			lua.Pop(1);
			b.Append(ms.Src.AsSpan(s, (e - s)));  /* keep original text */
		}
		else if (!lua.IsString(-1))
			lua.L_Error("invalid replacement value (a %s)", lua.L_TypeName(-1));
		else
			b.Append(lua.ToString(-1));
	}

	private static int Str_Gsub(ILuaState lua)
	{
		var src = lua.L_CheckString(1);
		var srcl = src.Length;
		var p = lua.L_CheckString(2);
		var tr = lua.Type(3);
		var maxS = lua.L_OptInt(4, srcl + 1);
		var anchor = 0;
		if (p[0] == '^')
		{
			p = p[1..];
			anchor = 1;
		}
		var b = new StringBuilder(srcl);
		lua.L_ArgCheck(
			tr is LuaType.LUA_TNUMBER or LuaType.LUA_TSTRING or
				LuaType.LUA_TFUNCTION or LuaType.LUA_TTABLE, 3,
			"string/function/table expected");
		var n = 0;
		var ms = new MatchState(lua, src, p)
		{
			SrcInit = 0,
			SrcEnd = srcl,
			PatternEnd = p.Length
		};
		var s = 0;
		while (n < maxS) 
		{
			ms.Level = 0;
			var e = Match(ms, s, 0);
			if (e != -1) {
				n++;
				Add_Value(ms, b, s, e);
			}
			if ((e != -1) && e > s) /* non empty match? */
				s = e;  /* skip it */
			else if (s < ms.SrcEnd)
			{
				var c = src[s];
				++s;
				b.Append(c);
			}
			else break;
			if (anchor != 0) break;
		}
		b.Append(src.AsSpan(s, ms.SrcEnd - s));
		lua.PushString(b.ToString());
		lua.PushInteger(n);  /* number of substitutions */
		return 2;
	}

	private static int Str_Len(ILuaState lua)
	{
		var s = lua.L_CheckString(1);
		lua.PushInteger(s.Length);
		return 1;
	}

	private static int Str_Lower(ILuaState lua)
	{
		var s = lua.L_CheckString(1);
		lua.PushString(s.ToLower());
		return 1;
	}

	private static int Str_Match(ILuaState lua) => StrFindAux(lua, false);

	private static int Str_Rep(ILuaState lua)
	{
		var str = lua.ToString(1);
		var n = lua.ToInteger(2);
		string? sep = null;
		if (lua.IsString(3))
			sep = lua.ToString(3);

		if ((string.IsNullOrEmpty(str) && sep == null) || n < 1)
		{
			lua.PushString("");
		}
		else
		{
			var builder = new StringBuilder();
			for (var i = 0; i < n; i++)
			{
				builder.Append(str);
				if (sep != null && i + 1 < n) builder.Append(sep);
			}
			lua.PushString(builder.ToString());
		}

		return 1;
	}

	private static int Str_Reverse(ILuaState lua)
	{
		var s = lua.L_CheckString(1);
		var sb = new StringBuilder(s.Length);
		for (var i = s.Length - 1; i >= 0; --i)
			sb.Append(s[i]);
		lua.PushString(sb.ToString());
		return 1;
	}

	private static int Str_Sub( ILuaState lua )
	{
		var s = lua.L_CheckString(1);
		var start = PosRelative( lua.L_CheckInteger(2), s.Length );
		var end = PosRelative( lua.L_OptInt(3, -1), s.Length );
		if( start < 1 ) start = 1;
		if( end > s.Length ) end = s.Length;
		if( start <= end )
			lua.PushString( s.Substring(start-1, end-start+1) );
		else
			lua.PushString( "" );
		return 1;
	}

	private static int Str_Upper( ILuaState lua )
	{
		var s = lua.L_CheckString(1);
		lua.PushString( s.ToUpper() );
		return 1;
	}
}